using System;
using System.Net;
using System.Reflection;
using Hps.Buffers;
using Hps.Transport;
using Xunit;

namespace Hps.Broker.Tests
{
    public sealed class BrokerPublisherTests
    {
        // Broker publish fan-out 진입점은 SubscriptionTable 스냅샷을 읽고 Transport send 계약으로 넘기는 경계다.
        // 아직 구현이 없을 때 먼저 실패시켜, 이후 production code가 이 공개 진입점을 실제로 제공하는지 확인한다.
        [Fact]
        public void BrokerPublisher_Contract_Exists()
        {
            Type? publisherType = Type.GetType("Hps.Broker.BrokerPublisher, Hps.Broker");

            Assert.NotNull(publisherType);
        }

        // publish fan-out API는 routing table과 transport 계약 사이의 얇은 연결점이어야 한다.
        // 생성자와 Publish 시그니처를 먼저 고정해, 이후 테스트가 컴파일 우회 없이 실제 공개 API를 검증하게 만든다.
        [Fact]
        public void BrokerPublisher_Contract_ExposesPublishEntryPoint()
        {
            Type? publisherType = Type.GetType("Hps.Broker.BrokerPublisher, Hps.Broker");
            Assert.NotNull(publisherType);

            ConstructorInfo? constructor = publisherType!.GetConstructor(new Type[] { typeof(SubscriptionTable), typeof(ITransport) });
            MethodInfo? publish = publisherType.GetMethod("Publish", new Type[] { typeof(string), typeof(RefCountedBuffer) });
            MethodInfo? rangedPublish = publisherType.GetMethod("Publish", new Type[] { typeof(string), typeof(RefCountedBuffer), typeof(int), typeof(int) });

            Assert.NotNull(constructor);
            Assert.NotNull(publish);
            Assert.Equal(typeof(int), publish!.ReturnType);
            Assert.NotNull(rangedPublish);
            Assert.Equal(typeof(int), rangedPublish!.ReturnType);
        }

        // fan-out 성공 경로는 payload 를 구독자 수만큼 복사하지 않고 같은 RefCountedBuffer 에 AddRef 한 뒤
        // Transport 로 넘겨야 한다. publish guard ref 는 caller 가 유지하므로 Publish 직후에는 caller 가 아직 Release 해야 한다.
        [Fact]
        public void Publish_WhenTopicHasSubscribers_SendsSamePayloadReferenceToEachSubscriber()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
            RefCountedBuffer payload = pool.RentCounted();
            payload.Span[0] = 10;
            payload.Span[1] = 20;
            payload.Span[2] = 30;
            payload.SetLength(3);

            SubscriptionTable subscriptions = new SubscriptionTable();
            FakeConnection first = new FakeConnection();
            FakeConnection second = new FakeConnection();
            FakeConnection otherTopic = new FakeConnection();
            subscriptions.Subscribe("topic", first);
            subscriptions.Subscribe("topic", second);
            subscriptions.Subscribe("other", otherTopic);

            FakeTransport transport = new FakeTransport();
            BrokerPublisher publisher = new BrokerPublisher(subscriptions, transport);

            int accepted = publisher.Publish("topic", payload);

            Assert.Equal(2, accepted);
            Assert.Equal(2, transport.AcceptedSends.Count);
            Assert.All(transport.AcceptedSends, delegate(CapturedSend send)
            {
                Assert.Same(payload, send.Buffer.Buffer);
                Assert.Equal(0, send.Buffer.Offset);
                Assert.Equal(3, send.Buffer.Length);
            });

            transport.ReleaseAcceptedBuffers();
            payload.Release();
            Assert.Equal(0, pool.RentedCount);
        }

        // TrySend false 는 Transport 가 해당 ref 를 소유하지 않았다는 뜻이다. Broker 가 거부된 구독자 ref 를 즉시 Release 하지 않으면
        // caller guard 와 성공한 send ref 를 모두 해제한 뒤에도 pool 로 돌아가지 않아 단명 연결 fan-out 에서 누수가 된다.
        [Fact]
        public void Publish_WhenTransportRejectsSubscriber_ReleasesRejectedSubscriberReference()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
            RefCountedBuffer payload = pool.RentCounted();
            payload.SetLength(5);

            SubscriptionTable subscriptions = new SubscriptionTable();
            FakeConnection acceptedConnection = new FakeConnection();
            FakeConnection rejectedConnection = new FakeConnection();
            subscriptions.Subscribe("topic", acceptedConnection);
            subscriptions.Subscribe("topic", rejectedConnection);

            FakeTransport transport = new FakeTransport();
            transport.RejectConnection = rejectedConnection;
            BrokerPublisher publisher = new BrokerPublisher(subscriptions, transport);

            int accepted = publisher.Publish("topic", payload);

            Assert.Equal(1, accepted);
            Assert.Single(transport.AcceptedSends);
            Assert.Same(acceptedConnection, transport.AcceptedSends[0].Connection);

            transport.ReleaseAcceptedBuffers();
            payload.Release();
            Assert.Equal(0, pool.RentedCount);
        }

        // TCP command frame 은 `PUBLISH topic payload` 전체가 하나의 RefCountedBuffer 안에 들어온다.
        // command handler 가 추가 복사 없이 payload 부분만 fan-out 하려면 BrokerPublisher 가 offset/length 범위를 그대로 TransportSendBuffer 에 넘겨야 한다.
        [Fact]
        public void Publish_WhenPayloadRangeIsSpecified_SendsOnlyThatRange()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
            RefCountedBuffer frame = pool.RentCounted();
            frame.SetLength(12);

            SubscriptionTable subscriptions = new SubscriptionTable();
            FakeConnection subscriber = new FakeConnection();
            subscriptions.Subscribe("topic", subscriber);

            FakeTransport transport = new FakeTransport();
            BrokerPublisher publisher = new BrokerPublisher(subscriptions, transport);

            int accepted = publisher.Publish("topic", frame, 8, 4);

            Assert.Equal(1, accepted);
            Assert.Single(transport.AcceptedSends);
            Assert.Same(frame, transport.AcceptedSends[0].Buffer.Buffer);
            Assert.Equal(8, transport.AcceptedSends[0].Buffer.Offset);
            Assert.Equal(4, transport.AcceptedSends[0].Buffer.Length);

            transport.ReleaseAcceptedBuffers();
            frame.Release();
            Assert.Equal(0, pool.RentedCount);
        }

        // 잘못된 payload slice 는 구독자가 없더라도 호출자 버그이므로 즉시 거부해야 한다.
        // 그렇지 않으면 command handler 의 offset 계산 오류가 0-subscriber topic 에서 조용히 묻혀 이후 fan-out 시점에만 드러난다.
        [Fact]
        public void Publish_WhenPayloadRangeIsInvalid_ThrowsBeforeFanOut()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
            RefCountedBuffer frame = pool.RentCounted();
            frame.SetLength(5);

            BrokerPublisher publisher = new BrokerPublisher(new SubscriptionTable(), new FakeTransport());

            Assert.Throws<ArgumentOutOfRangeException>(delegate()
            {
                publisher.Publish("topic", frame, 4, 2);
            });

            frame.Release();
            Assert.Equal(0, pool.RentedCount);
        }

        // mixed fan-out 테스트: Interface Server 목표에서는 같은 topic 의 발행 대상이 TCP connection 과 UDP remote target 을 함께 포함할 수 있다.
        // BrokerPublisher 는 payload 를 복사하지 않고 같은 RefCountedBuffer ref 를 TCP TrySend 와 UDP TrySendTo 로 각각 넘겨야 한다.
        [Fact]
        public void Publish_WhenTopicHasTcpAndUdpSubscribers_SendsToEachTransportTarget()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
            RefCountedBuffer payload = pool.RentCounted();
            payload.SetLength(6);

            FakeConnection tcpSubscriber = new FakeConnection();
            FakeUdpEndpoint udpEndpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20000);

            SubscriptionTable subscriptions = new SubscriptionTable();
            subscriptions.Subscribe("topic", tcpSubscriber);
            subscriptions.Subscribe("topic", BrokerSubscriber.ForUdp(udpEndpoint, remoteEndPoint));

            FakeTransport transport = new FakeTransport();
            BrokerPublisher publisher = new BrokerPublisher(subscriptions, transport);

            int accepted = publisher.Publish("topic", payload, 1, 4);

            Assert.Equal(2, accepted);
            Assert.Single(transport.AcceptedSends);
            Assert.Single(transport.AcceptedUdpSends);
            Assert.Same(tcpSubscriber, transport.AcceptedSends[0].Connection);
            Assert.Same(udpEndpoint, transport.AcceptedUdpSends[0].Endpoint);
            Assert.Equal(remoteEndPoint, transport.AcceptedUdpSends[0].RemoteEndPoint);
            Assert.Same(payload, transport.AcceptedSends[0].Buffer.Buffer);
            Assert.Same(payload, transport.AcceptedUdpSends[0].Buffer.Buffer);
            Assert.Equal(1, transport.AcceptedUdpSends[0].Buffer.Offset);
            Assert.Equal(4, transport.AcceptedUdpSends[0].Buffer.Length);

            transport.ReleaseAcceptedBuffers();
            payload.Release();
            Assert.Equal(0, pool.RentedCount);
        }
    }
}
