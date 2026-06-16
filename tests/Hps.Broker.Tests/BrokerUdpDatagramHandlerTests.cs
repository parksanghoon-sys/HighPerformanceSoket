using System;
using System.Net;
using System.Reflection;
using System.Text;
using Hps.Buffers;
using Hps.Transport;
using Xunit;

namespace Hps.Broker.Tests
{
    public sealed class BrokerUdpDatagramHandlerTests
    {
        // UDP broker handler 는 Transport UDP receive pump 와 Broker routing/fan-out 사이의 첫 연결점이다.
        // 먼저 public handler 경계를 고정해 server bind wiring 없이도 Broker 단위에서 datagram self-command 를 검증할 수 있게 한다.
        [Fact]
        public void BrokerUdpDatagramHandler_Contract_ExistsAndImplementsDatagramHandler()
        {
            Type? handlerType = Type.GetType("Hps.Broker.BrokerUdpDatagramHandler, Hps.Broker");

            Assert.NotNull(handlerType);
            Assert.True(typeof(ITransportDatagramHandler).IsAssignableFrom(handlerType));
            Assert.NotNull(handlerType!.GetConstructor(new Type[] { typeof(SubscriptionTable), typeof(BrokerPublisher) }));
        }

        // UDP SUBSCRIBE datagram 은 TCP connection 없이 remote endpoint 자체를 구독자로 등록해야 한다.
        // handler 는 datagram 최초 ref 를 소유하므로 command 처리 뒤 반드시 Release 해서 receive pool 누수를 막아야 한다.
        [Fact]
        public void OnDatagramReceived_WhenSubscribeCommandArrives_AddsUdpSubscriberAndReleasesDatagram()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            RefCountedBuffer datagram = RentDatagram(pool, "SUBSCRIBE alpha");
            SubscriptionTable subscriptions = new SubscriptionTable();
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20000);
            BrokerUdpDatagramHandler handler = CreateHandler(subscriptions, new FakeTransport());

            handler.OnDatagramReceived(endpoint, remoteEndPoint, datagram);

            Assert.True(subscriptions.IsSubscribed("alpha", BrokerSubscriber.ForUdp(endpoint, remoteEndPoint)));
            Assert.Equal(0, endpoint.CloseCallCount);
            Assert.Equal(0, pool.RentedCount);
        }

        // UDP UNSUBSCRIBE datagram 은 같은 local endpoint/remote endpoint 조합의 해당 topic 구독만 제거해야 한다.
        // 다른 topic 구독은 유지되어야 명시적 unsubscribe 가 connection-wide cleanup 처럼 동작하는 회귀를 막을 수 있다.
        [Fact]
        public void OnDatagramReceived_WhenUnsubscribeCommandArrives_RemovesOnlyThatUdpTopicAndReleasesDatagram()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            RefCountedBuffer datagram = RentDatagram(pool, "UNSUBSCRIBE alpha");
            SubscriptionTable subscriptions = new SubscriptionTable();
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20000);
            BrokerSubscriber subscriber = BrokerSubscriber.ForUdp(endpoint, remoteEndPoint);
            subscriptions.Subscribe("alpha", subscriber);
            subscriptions.Subscribe("beta", subscriber);

            BrokerUdpDatagramHandler handler = CreateHandler(subscriptions, new FakeTransport());

            handler.OnDatagramReceived(endpoint, remoteEndPoint, datagram);

            Assert.False(subscriptions.IsSubscribed("alpha", subscriber));
            Assert.True(subscriptions.IsSubscribed("beta", subscriber));
            Assert.Equal(0, endpoint.CloseCallCount);
            Assert.Equal(0, pool.RentedCount);
        }

        // UDP PUBLISH datagram 은 datagram buffer 자체를 payload 소유권 단위로 사용한다.
        // command prefix 를 잘라낸 payload range 만 TrySendTo 로 넘기고, 원본 datagram guard ref 는 handler 가 반환해야 한다.
        [Fact]
        public void OnDatagramReceived_WhenPublishCommandArrives_FanoutsPayloadRangeAndReleasesDatagramGuard()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            RefCountedBuffer datagram = RentDatagram(pool, "PUBLISH alpha PAYLOAD");
            SubscriptionTable subscriptions = new SubscriptionTable();
            FakeUdpEndpoint publisherEndpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint publisherRemote = new IPEndPoint(IPAddress.Loopback, 20000);
            FakeUdpEndpoint subscriberEndpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10001));
            EndPoint subscriberRemote = new IPEndPoint(IPAddress.Loopback, 20001);
            subscriptions.Subscribe("alpha", BrokerSubscriber.ForUdp(subscriberEndpoint, subscriberRemote));

            FakeTransport transport = new FakeTransport();
            BrokerUdpDatagramHandler handler = CreateHandler(subscriptions, transport);

            handler.OnDatagramReceived(publisherEndpoint, publisherRemote, datagram);

            Assert.Single(transport.AcceptedUdpSends);
            Assert.Same(subscriberEndpoint, transport.AcceptedUdpSends[0].Endpoint);
            Assert.Equal(subscriberRemote, transport.AcceptedUdpSends[0].RemoteEndPoint);
            Assert.Same(datagram, transport.AcceptedUdpSends[0].Buffer.Buffer);
            Assert.Equal(14, transport.AcceptedUdpSends[0].Buffer.Offset);
            Assert.Equal(7, transport.AcceptedUdpSends[0].Buffer.Length);

            transport.ReleaseAcceptedBuffers();
            Assert.Equal(0, pool.RentedCount);
        }

        // malformed UDP command 는 shared UDP endpoint 를 닫지 않고 해당 datagram 만 폐기한다는 D060 정책을 검증한다.
        // TCP malformed command 와 달리 UDP endpoint 하나가 여러 remote 를 받으므로 protocol error 하나가 endpoint 전체를 죽이면 안 된다.
        [Fact]
        public void OnDatagramReceived_WhenCommandIsMalformed_DropsDatagramWithoutClosingEndpoint()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            RefCountedBuffer datagram = RentDatagram(pool, "UNSUBSCRIBE alpha beta");
            SubscriptionTable subscriptions = new SubscriptionTable();
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20000);
            BrokerSubscriber subscriber = BrokerSubscriber.ForUdp(endpoint, remoteEndPoint);
            subscriptions.Subscribe("alpha", subscriber);

            FakeTransport transport = new FakeTransport();
            BrokerUdpDatagramHandler handler = CreateHandler(subscriptions, transport);

            handler.OnDatagramReceived(endpoint, remoteEndPoint, datagram);

            Assert.True(subscriptions.IsSubscribed("alpha", subscriber));
            Assert.Empty(transport.AcceptedUdpSends);
            Assert.Equal(0, endpoint.CloseCallCount);
            Assert.Equal(0, pool.RentedCount);
        }

        // endpoint close notification 은 local UDP endpoint 에 묶인 모든 remote 구독을 제거해야 한다.
        // remote endpoint 이름을 모르는 상태에서도 닫힌 socket handle 로 fan-out 을 계속 시도하지 않게 하는 수명 cleanup 경계다.
        [Fact]
        public void OnDatagramEndpointClosed_WhenEndpointHadSubscriptions_RemovesAllSubscriptionsForThatEndpoint()
        {
            SubscriptionTable subscriptions = new SubscriptionTable();
            FakeUdpEndpoint closedEndpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            FakeUdpEndpoint survivorEndpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10001));
            EndPoint firstRemote = new IPEndPoint(IPAddress.Loopback, 20000);
            EndPoint secondRemote = new IPEndPoint(IPAddress.Loopback, 20001);
            BrokerSubscriber firstClosed = BrokerSubscriber.ForUdp(closedEndpoint, firstRemote);
            BrokerSubscriber secondClosed = BrokerSubscriber.ForUdp(closedEndpoint, secondRemote);
            BrokerSubscriber survivor = BrokerSubscriber.ForUdp(survivorEndpoint, firstRemote);
            subscriptions.Subscribe("alpha", firstClosed);
            subscriptions.Subscribe("beta", secondClosed);
            subscriptions.Subscribe("alpha", survivor);

            BrokerUdpDatagramHandler handler = CreateHandler(subscriptions, new FakeTransport());

            handler.OnDatagramEndpointClosed(closedEndpoint);

            Assert.False(subscriptions.IsSubscribed("alpha", firstClosed));
            Assert.False(subscriptions.IsSubscribed("beta", secondClosed));
            Assert.True(subscriptions.IsSubscribed("alpha", survivor));
            Assert.Equal(1, subscriptions.CountSubscribers("alpha"));
            Assert.Equal(0, subscriptions.CountSubscribers("beta"));
        }

        private static BrokerUdpDatagramHandler CreateHandler(SubscriptionTable subscriptions, FakeTransport transport)
        {
            BrokerPublisher publisher = new BrokerPublisher(subscriptions, transport);
            return new BrokerUdpDatagramHandler(subscriptions, publisher);
        }

        private static RefCountedBuffer RentDatagram(PinnedBlockMemoryPool pool, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            RefCountedBuffer datagram = pool.RentCounted();
            bytes.CopyTo(datagram.Span);
            datagram.SetLength(bytes.Length);
            return datagram;
        }
    }
}
