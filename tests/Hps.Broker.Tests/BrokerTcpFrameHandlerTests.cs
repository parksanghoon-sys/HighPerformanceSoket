using System;
using System.Reflection;
using System.Text;
using Hps.Buffers;
using Hps.Protocol;
using Xunit;

namespace Hps.Broker.Tests
{
    public sealed class BrokerTcpFrameHandlerTests
    {
        // Broker TCP handler 는 Protocol frame callback 을 Broker 라우팅/fan-out 으로 연결하는 첫 end-to-end 결선이다.
        // 타입과 생성자 계약을 먼저 고정해 이후 동작 테스트가 실제 public 진입점으로 검증되도록 한다.
        [Fact]
        public void BrokerTcpFrameHandler_Contract_ExistsAndImplementsFrameHandler()
        {
            Type? handlerType = Type.GetType("Hps.Broker.BrokerTcpFrameHandler, Hps.Broker");

            Assert.NotNull(handlerType);
            Assert.True(typeof(ITcpFrameHandler).IsAssignableFrom(handlerType));
            Assert.NotNull(handlerType!.GetConstructor(new Type[] { typeof(SubscriptionTable), typeof(BrokerPublisher) }));
        }

        // SUBSCRIBE frame 처리 테스트: frame handler 는 command topic 을 routing table key 로 복사해 등록하고,
        // 수락한 frame guard ref 를 처리 후 Release 해야 TCP recv 조립 버퍼가 누수되지 않는다.
        [Fact]
        public void OnFrame_WhenSubscribeCommandArrives_AddsConnectionToTopicAndReleasesFrame()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            RefCountedBuffer frame = RentFrame(pool, "SUBSCRIBE alpha");
            SubscriptionTable subscriptions = new SubscriptionTable();
            FakeTransport transport = new FakeTransport();
            BrokerTcpFrameHandler handler = CreateHandler(subscriptions, transport);
            FakeConnection connection = new FakeConnection();

            handler.OnFrame(connection, frame);

            Assert.True(subscriptions.IsSubscribed("alpha", connection));
            Assert.Equal(0, pool.RentedCount);
        }

        // UNSUBSCRIBE frame 처리 테스트: 명시적 구독 해제는 protocol error 가 아니므로 connection 을 닫지 않는다.
        // handler 가 routing table 에서 해당 topic/connection 쌍만 제거하고 frame guard ref 를 반환하는지 확인한다.
        [Fact]
        public void OnFrame_WhenUnsubscribeCommandArrives_RemovesConnectionFromTopicAndKeepsConnectionOpen()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            RefCountedBuffer frame = RentFrame(pool, "UNSUBSCRIBE alpha");
            SubscriptionTable subscriptions = new SubscriptionTable();
            FakeTransport transport = new FakeTransport();
            BrokerTcpFrameHandler handler = CreateHandler(subscriptions, transport);
            FakeConnection connection = new FakeConnection();
            FakeConnection survivor = new FakeConnection();
            subscriptions.Subscribe("alpha", connection);
            subscriptions.Subscribe("alpha", survivor);
            subscriptions.Subscribe("beta", connection);

            handler.OnFrame(connection, frame);

            Assert.False(subscriptions.IsSubscribed("alpha", connection));
            Assert.True(subscriptions.IsSubscribed("alpha", survivor));
            Assert.True(subscriptions.IsSubscribed("beta", connection));
            Assert.Equal(1, subscriptions.CountSubscribers("alpha"));
            Assert.Equal(0, connection.CloseCallCount);
            Assert.Equal(0, pool.RentedCount);
        }

        // PUBLISH frame 처리 테스트: handler 는 command prefix 전체가 담긴 RefCountedBuffer 를 새로 복사하지 않고
        // decoder 가 계산한 payload offset/length 만 BrokerPublisher 에 넘겨 구독자에게 실제 payload slice 만 송신해야 한다.
        [Fact]
        public void OnFrame_WhenPublishCommandArrives_FanoutsPayloadRangeAndReleasesFrameGuard()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            RefCountedBuffer frame = RentFrame(pool, "PUBLISH alpha PAYLOAD");
            SubscriptionTable subscriptions = new SubscriptionTable();
            FakeConnection subscriber = new FakeConnection();
            subscriptions.Subscribe("alpha", subscriber);

            FakeTransport transport = new FakeTransport();
            BrokerTcpFrameHandler handler = CreateHandler(subscriptions, transport);
            FakeConnection publisherConnection = new FakeConnection();

            handler.OnFrame(publisherConnection, frame);

            Assert.Single(transport.AcceptedSends);
            Assert.Same(subscriber, transport.AcceptedSends[0].Connection);
            Assert.Same(frame, transport.AcceptedSends[0].Buffer.Buffer);
            Assert.Equal(14, transport.AcceptedSends[0].Buffer.Offset);
            Assert.Equal(7, transport.AcceptedSends[0].Buffer.Length);

            transport.ReleaseAcceptedBuffers();
            Assert.Equal(0, pool.RentedCount);
        }

        // 연결 종료 처리 테스트: Protocol adapter 는 topic 이름 없이 connection close 만 알려준다.
        // handler 가 UnsubscribeAll 을 호출해야 끊긴 connection 이 여러 topic 의 구독 set 에 dead reference 로 남지 않는다.
        [Fact]
        public void OnConnectionClosed_WhenConnectionHadSubscriptions_RemovesConnectionFromAllTopics()
        {
            SubscriptionTable subscriptions = new SubscriptionTable();
            FakeConnection closed = new FakeConnection();
            FakeConnection survivor = new FakeConnection();
            subscriptions.Subscribe("alpha", closed);
            subscriptions.Subscribe("beta", closed);
            subscriptions.Subscribe("alpha", survivor);

            BrokerTcpFrameHandler handler = CreateHandler(subscriptions, new FakeTransport());

            handler.OnConnectionClosed(closed);

            Assert.False(subscriptions.IsSubscribed("alpha", closed));
            Assert.False(subscriptions.IsSubscribed("beta", closed));
            Assert.True(subscriptions.IsSubscribed("alpha", survivor));
            Assert.Equal(1, subscriptions.CountSubscribers("alpha"));
            Assert.Equal(0, subscriptions.CountSubscribers("beta"));
        }

        // TCP stable identity reconnect 테스트: 같은 subscriber-id 로 새 connection 이 REGISTER 하면
        // 기존 topic subscription 이 새 runtime target 으로 이동해야 한다.
        [Fact]
        public void OnFrame_WhenRegisteredTcpSubscriberReconnects_RebindsSubscriptionsToNewConnection()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(128);
            SubscriptionTable subscriptions = new SubscriptionTable();
            SubscriberRegistry registry = new SubscriberRegistry(subscriptions);
            BrokerTcpFrameHandler handler = CreateHandler(subscriptions, new FakeTransport(), registry);
            FakeConnection oldConnection = new FakeConnection();
            FakeConnection newConnection = new FakeConnection();

            handler.OnFrame(oldConnection, RentFrame(pool, "REGISTER device-a"));
            handler.OnFrame(oldConnection, RentFrame(pool, "SUBSCRIBE alpha"));
            handler.OnFrame(newConnection, RentFrame(pool, "REGISTER device-a"));

            Assert.False(subscriptions.IsSubscribed("alpha", oldConnection));
            Assert.True(subscriptions.IsSubscribed("alpha", newConnection));
            Assert.Equal(1, oldConnection.CloseCallCount);
            Assert.Equal(0, pool.RentedCount);
        }

        // TCP invalid duplicate registration 테스트: 같은 connection 이 다른 id 로 REGISTER 하면
        // cleanup 기준이 모호해지므로 protocol error 로 닫고 기존 subscription 을 제거해야 한다.
        [Fact]
        public void OnFrame_WhenRegisteredTcpTargetUsesDifferentIdentity_ClosesConnectionAndCleansSubscriptions()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(128);
            SubscriptionTable subscriptions = new SubscriptionTable();
            SubscriberRegistry registry = new SubscriberRegistry(subscriptions);
            BrokerTcpFrameHandler handler = CreateHandler(subscriptions, new FakeTransport(), registry);
            FakeConnection connection = new FakeConnection();

            handler.OnFrame(connection, RentFrame(pool, "REGISTER device-a"));
            handler.OnFrame(connection, RentFrame(pool, "SUBSCRIBE alpha"));
            handler.OnFrame(connection, RentFrame(pool, "REGISTER device-b"));

            Assert.Equal(1, connection.CloseCallCount);
            Assert.False(subscriptions.IsSubscribed("alpha", connection));
            Assert.Equal(0, pool.RentedCount);
        }

        // TCP disconnect 보존 테스트: registered connection 이 닫히면 runtime target 은 제거되지만
        // 같은 id 로 새 connection 이 REGISTER 했을 때 이전 topic set 이 복구되어야 한다.
        [Fact]
        public void OnConnectionClosed_WhenRegisteredTcpSubscriberReconnectsLater_RestoresTopicSet()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(128);
            SubscriptionTable subscriptions = new SubscriptionTable();
            SubscriberRegistry registry = new SubscriberRegistry(subscriptions);
            BrokerTcpFrameHandler handler = CreateHandler(subscriptions, new FakeTransport(), registry);
            FakeConnection oldConnection = new FakeConnection();
            FakeConnection newConnection = new FakeConnection();

            handler.OnFrame(oldConnection, RentFrame(pool, "REGISTER device-a"));
            handler.OnFrame(oldConnection, RentFrame(pool, "SUBSCRIBE alpha"));
            handler.OnConnectionClosed(oldConnection);
            handler.OnFrame(newConnection, RentFrame(pool, "REGISTER device-a"));

            Assert.False(subscriptions.IsSubscribed("alpha", oldConnection));
            Assert.True(subscriptions.IsSubscribed("alpha", newConnection));
            Assert.Equal(0, pool.RentedCount);
        }

        // TCP explicit UNREGISTER 테스트: client 가 명시적으로 identity 등록을 해제하면 현재 subscription 과 metadata 를 함께 제거해야 한다.
        // 그렇지 않으면 같은 id 의 다음 REGISTER 때 사용자가 버린 topic set 이 되살아난다.
        [Fact]
        public void OnFrame_WhenRegisteredTcpSubscriberUnregisters_RemovesIdentityAndSubscriptions()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(128);
            SubscriptionTable subscriptions = new SubscriptionTable();
            SubscriberRegistry registry = new SubscriberRegistry(subscriptions);
            BrokerTcpFrameHandler handler = CreateHandler(subscriptions, new FakeTransport(), registry);
            FakeConnection oldConnection = new FakeConnection();
            FakeConnection newConnection = new FakeConnection();

            handler.OnFrame(oldConnection, RentFrame(pool, "REGISTER device-a"));
            handler.OnFrame(oldConnection, RentFrame(pool, "SUBSCRIBE alpha"));
            handler.OnFrame(oldConnection, RentFrame(pool, "UNREGISTER device-a"));
            handler.OnFrame(newConnection, RentFrame(pool, "REGISTER device-a"));

            Assert.False(subscriptions.IsSubscribed("alpha", oldConnection));
            Assert.False(subscriptions.IsSubscribed("alpha", newConnection));
            Assert.Equal(1, registry.IdentityCount);
            Assert.Equal(0, pool.RentedCount);
        }

        // malformed frame 처리 테스트: command decode 실패는 정상 흐름 오류이므로 handler 밖으로 예외를 던지지 않는다.
        // 대신 frame 을 Release 하고 connection 을 닫아 이후 같은 TCP stream 에서 모호한 protocol 상태가 이어지지 않게 한다.
        [Fact]
        public void OnFrame_WhenCommandIsMalformed_ReleasesFrameAndClosesConnection()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            RefCountedBuffer frame = RentFrame(pool, "UNKNOWN alpha");
            BrokerTcpFrameHandler handler = CreateHandler(new SubscriptionTable(), new FakeTransport());
            FakeConnection connection = new FakeConnection();

            handler.OnFrame(connection, frame);

            Assert.Equal(1, connection.CloseCallCount);
            Assert.Equal(0, pool.RentedCount);
        }

        // malformed command 후 transport close notify 가 다시 오지 않아도 Broker routing table 이 오염되면 안 된다.
        // SAEA receive loop 의 dispose 종료처럼 close 통지가 생략되는 경로를 대비해 protocol-error close 경로가 직접 cleanup 해야 한다.
        [Fact]
        public void OnFrame_WhenSubscribedConnectionSendsMalformedCommand_RemovesConnectionFromAllTopics()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            RefCountedBuffer frame = RentFrame(pool, "UNKNOWN alpha");
            SubscriptionTable subscriptions = new SubscriptionTable();
            FakeConnection connection = new FakeConnection();
            FakeConnection survivor = new FakeConnection();
            subscriptions.Subscribe("alpha", connection);
            subscriptions.Subscribe("beta", connection);
            subscriptions.Subscribe("alpha", survivor);

            BrokerTcpFrameHandler handler = CreateHandler(subscriptions, new FakeTransport());

            handler.OnFrame(connection, frame);

            Assert.Equal(1, connection.CloseCallCount);
            Assert.False(subscriptions.IsSubscribed("alpha", connection));
            Assert.False(subscriptions.IsSubscribed("beta", connection));
            Assert.True(subscriptions.IsSubscribed("alpha", survivor));
            Assert.Equal(1, subscriptions.CountSubscribers("alpha"));
            Assert.Equal(0, subscriptions.CountSubscribers("beta"));
            Assert.Equal(0, pool.RentedCount);
        }

        private static BrokerTcpFrameHandler CreateHandler(SubscriptionTable subscriptions, FakeTransport transport)
        {
            BrokerPublisher publisher = new BrokerPublisher(subscriptions, transport);
            return new BrokerTcpFrameHandler(subscriptions, publisher);
        }

        private static BrokerTcpFrameHandler CreateHandler(
            SubscriptionTable subscriptions,
            FakeTransport transport,
            SubscriberRegistry registry)
        {
            BrokerPublisher publisher = new BrokerPublisher(subscriptions, transport);
            return new BrokerTcpFrameHandler(subscriptions, publisher, registry, TimeProvider.System);
        }

        private static RefCountedBuffer RentFrame(PinnedBlockMemoryPool pool, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            RefCountedBuffer frame = pool.RentCounted();
            bytes.CopyTo(frame.Span);
            frame.SetLength(bytes.Length);
            return frame;
        }

    }
}
