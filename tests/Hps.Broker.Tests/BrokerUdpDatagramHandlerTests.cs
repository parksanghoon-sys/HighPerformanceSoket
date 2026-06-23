using System;
using System.Net;
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

        // UDP stable identity rebind 테스트: 같은 id 가 다른 remote endpoint 에서 다시 REGISTER 되면
        // 기존 remote subscription 과 lease 를 제거하고 새 remote 를 fan-out 대상으로 삼아야 한다.
        [Fact]
        public void OnDatagramReceived_WhenRegisteredUdpRemoteRebinds_MovesSubscriptionToNewRemote()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(128);
            SubscriptionTable subscriptions = new SubscriptionTable();
            SubscriberRegistry registry = new SubscriberRegistry(subscriptions);
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint oldRemote = new IPEndPoint(IPAddress.Loopback, 20000);
            EndPoint newRemote = new IPEndPoint(IPAddress.Loopback, 20001);
            BrokerUdpDatagramHandler handler = CreateHandler(
                subscriptions,
                new FakeTransport(),
                UdpLeaseOptions.CreateEnabled(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)),
                TimeProvider.System,
                registry);

            handler.OnDatagramReceived(endpoint, oldRemote, RentDatagram(pool, "REGISTER device-a"));
            handler.OnDatagramReceived(endpoint, oldRemote, RentDatagram(pool, "SUBSCRIBE alpha"));
            handler.OnDatagramReceived(endpoint, newRemote, RentDatagram(pool, "REGISTER device-a"));

            Assert.False(subscriptions.IsSubscribed("alpha", BrokerSubscriber.ForUdp(endpoint, oldRemote)));
            Assert.True(subscriptions.IsSubscribed("alpha", BrokerSubscriber.ForUdp(endpoint, newRemote)));
            Assert.Equal(0, pool.RentedCount);
        }

        // UDP duplicate target 정책 테스트: 같은 remote 가 다른 id 로 다시 REGISTER 하면 endpoint 전체를 닫지 않고
        // 해당 datagram 만 폐기해야 shared UDP socket 의 다른 remote 를 보존할 수 있다.
        [Fact]
        public void OnDatagramReceived_WhenRegisteredUdpRemoteUsesDifferentIdentity_DropsDatagramWithoutClosingEndpoint()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(128);
            SubscriptionTable subscriptions = new SubscriptionTable();
            SubscriberRegistry registry = new SubscriberRegistry(subscriptions);
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint remote = new IPEndPoint(IPAddress.Loopback, 20000);
            BrokerUdpDatagramHandler handler = CreateHandler(
                subscriptions,
                new FakeTransport(),
                UdpLeaseOptions.Disabled,
                TimeProvider.System,
                registry);

            handler.OnDatagramReceived(endpoint, remote, RentDatagram(pool, "REGISTER device-a"));
            handler.OnDatagramReceived(endpoint, remote, RentDatagram(pool, "SUBSCRIBE alpha"));
            handler.OnDatagramReceived(endpoint, remote, RentDatagram(pool, "REGISTER device-b"));

            Assert.Equal(0, endpoint.CloseCallCount);
            Assert.True(subscriptions.IsSubscribed("alpha", BrokerSubscriber.ForUdp(endpoint, remote)));
            Assert.Equal(0, pool.RentedCount);
        }

        // UDP invalid stable identity 테스트: decoder 는 ASCII space 만 token 구분자로 보므로 tab 이 포함된 identity 는 decode 이후
        // registry validation 에서 거부된다. 이 예외는 shared UDP endpoint close 로 전파되지 않고 해당 datagram drop 으로 끝나야 한다.
        [Theory]
        [InlineData("REGISTER device\t-a")]
        [InlineData("UNREGISTER device\t-a")]
        public void OnDatagramReceived_WhenStableIdentityTokenIsInvalid_DropsDatagramWithoutThrowingOrClosingEndpoint(string invalidCommand)
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(128);
            SubscriptionTable subscriptions = new SubscriptionTable();
            SubscriberRegistry registry = new SubscriberRegistry(subscriptions);
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint remote = new IPEndPoint(IPAddress.Loopback, 20000);
            BrokerSubscriber subscriber = BrokerSubscriber.ForUdp(endpoint, remote);
            BrokerUdpDatagramHandler handler = CreateHandler(
                subscriptions,
                new FakeTransport(),
                UdpLeaseOptions.Disabled,
                TimeProvider.System,
                registry);

            handler.OnDatagramReceived(endpoint, remote, RentDatagram(pool, "REGISTER device-a"));
            handler.OnDatagramReceived(endpoint, remote, RentDatagram(pool, "SUBSCRIBE alpha"));

            Exception? exception = Record.Exception(() => handler.OnDatagramReceived(endpoint, remote, RentDatagram(pool, invalidCommand)));

            Assert.Null(exception);
            Assert.Equal(0, endpoint.CloseCallCount);
            Assert.True(subscriptions.IsSubscribed("alpha", subscriber));
            Assert.Equal(0, pool.RentedCount);
        }

        // UDP late REGISTER lease cleanup 테스트: REGISTER 전에 만든 runtime 구독은 routing table 뿐 아니라
        // optional lease tracker 에서도 제거되어야 한다. 그렇지 않으면 active remote 가 PUBLISH 로 lease 를 계속 갱신해
        // 실제 구독이 없어도 lease metadata 가 남을 수 있다.
        [Fact]
        public void OnDatagramReceived_WhenUdpRemoteRegistersAfterRuntimeSubscribe_ClearsPreRegisterLease()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(128);
            SubscriptionTable subscriptions = new SubscriptionTable();
            SubscriberRegistry registry = new SubscriberRegistry(subscriptions);
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint remote = new IPEndPoint(IPAddress.Loopback, 20000);
            BrokerSubscriber subscriber = BrokerSubscriber.ForUdp(endpoint, remote);
            BrokerUdpDatagramHandler handler = CreateHandler(
                subscriptions,
                new FakeTransport(),
                UdpLeaseOptions.CreateEnabled(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)),
                TimeProvider.System,
                registry);

            handler.OnDatagramReceived(endpoint, remote, RentDatagram(pool, "SUBSCRIBE alpha"));
            handler.OnDatagramReceived(endpoint, remote, RentDatagram(pool, "REGISTER device-a"));

            Assert.False(subscriptions.IsSubscribed("alpha", subscriber));
            Assert.Equal(0, handler.UdpLeaseCount);
            Assert.Equal(0, pool.RentedCount);
        }

        // UDP explicit UNREGISTER 테스트: remote 가 identity 등록을 해제하면 현재 subscription 과 metadata 를 함께 제거해야 한다.
        // 같은 id 의 다음 REGISTER 때 사용자가 버린 topic 이 새 remote 로 복구되면 안 된다.
        [Fact]
        public void OnDatagramReceived_WhenRegisteredUdpRemoteUnregisters_RemovesIdentityAndSubscriptions()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(128);
            SubscriptionTable subscriptions = new SubscriptionTable();
            SubscriberRegistry registry = new SubscriberRegistry(subscriptions);
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint oldRemote = new IPEndPoint(IPAddress.Loopback, 20000);
            EndPoint newRemote = new IPEndPoint(IPAddress.Loopback, 20001);
            BrokerUdpDatagramHandler handler = CreateHandler(
                subscriptions,
                new FakeTransport(),
                UdpLeaseOptions.Disabled,
                TimeProvider.System,
                registry);

            handler.OnDatagramReceived(endpoint, oldRemote, RentDatagram(pool, "REGISTER device-a"));
            handler.OnDatagramReceived(endpoint, oldRemote, RentDatagram(pool, "SUBSCRIBE alpha"));
            handler.OnDatagramReceived(endpoint, oldRemote, RentDatagram(pool, "UNREGISTER device-a"));
            handler.OnDatagramReceived(endpoint, newRemote, RentDatagram(pool, "REGISTER device-a"));

            Assert.False(subscriptions.IsSubscribed("alpha", BrokerSubscriber.ForUdp(endpoint, oldRemote)));
            Assert.False(subscriptions.IsSubscribed("alpha", BrokerSubscriber.ForUdp(endpoint, newRemote)));
            Assert.Equal(1, registry.IdentityCount);
            Assert.Equal(0, pool.RentedCount);
        }

        // UDP endpoint close retention 테스트: registered UDP endpoint 가 닫히면 runtime target 은 제거하되 topic metadata 는 retention 대상이다.
        // 같은 id 가 다른 endpoint/remote 로 다시 REGISTER 하면 이전 topic set 이 새 target 으로 복구되어야 한다.
        [Fact]
        public void OnDatagramEndpointClosed_WhenRegisteredUdpRemoteReconnectsLater_RestoresTopicSet()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(128);
            SubscriptionTable subscriptions = new SubscriptionTable();
            SubscriberRegistry registry = new SubscriberRegistry(subscriptions);
            FakeUdpEndpoint oldEndpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            FakeUdpEndpoint newEndpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10001));
            EndPoint oldRemote = new IPEndPoint(IPAddress.Loopback, 20000);
            EndPoint newRemote = new IPEndPoint(IPAddress.Loopback, 20001);
            BrokerUdpDatagramHandler handler = CreateHandler(
                subscriptions,
                new FakeTransport(),
                UdpLeaseOptions.CreateEnabled(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)),
                TimeProvider.System,
                registry);

            handler.OnDatagramReceived(oldEndpoint, oldRemote, RentDatagram(pool, "REGISTER device-a"));
            handler.OnDatagramReceived(oldEndpoint, oldRemote, RentDatagram(pool, "SUBSCRIBE alpha"));
            handler.OnDatagramEndpointClosed(oldEndpoint);
            handler.OnDatagramReceived(newEndpoint, newRemote, RentDatagram(pool, "REGISTER device-a"));

            Assert.False(subscriptions.IsSubscribed("alpha", BrokerSubscriber.ForUdp(oldEndpoint, oldRemote)));
            Assert.True(subscriptions.IsSubscribed("alpha", BrokerSubscriber.ForUdp(newEndpoint, newRemote)));
            Assert.Equal(0, pool.RentedCount);
        }

        // UDP stable identity lease sweep 테스트: idle sweep 이 routing table 만 지우고 registry current target 을 그대로 두면
        // retention sweep 이 disconnected identity 를 제거하지 못한다. sweep 은 stable registry 에도 remote target 종료를 알려야 한다.
        [Fact]
        public void SweepExpiredUdpLeases_WhenRegisteredRemoteExpires_MarksRegistryTargetDisconnected()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(128);
            SubscriptionTable subscriptions = new SubscriptionTable();
            SubscriberRegistry registry = new SubscriberRegistry(subscriptions);
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20000);
            ManualTimeProvider time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-22T00:00:00Z"));
            BrokerUdpDatagramHandler handler = CreateHandler(
                subscriptions,
                new FakeTransport(),
                UdpLeaseOptions.CreateEnabled(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)),
                time,
                registry);

            handler.OnDatagramReceived(endpoint, remoteEndPoint, RentDatagram(pool, "REGISTER device-a"));
            handler.OnDatagramReceived(endpoint, remoteEndPoint, RentDatagram(pool, "SUBSCRIBE alpha"));

            time.Advance(TimeSpan.FromSeconds(31));
            int removed = handler.SweepExpiredUdpLeases(time.GetUtcNow());

            int expiredIdentities = registry.SweepDisconnected(time.GetUtcNow().AddMinutes(6), TimeSpan.FromMinutes(5));

            Assert.Equal(1, removed);
            Assert.Equal(1, expiredIdentities);
            Assert.False(subscriptions.IsSubscribed("alpha", BrokerSubscriber.ForUdp(endpoint, remoteEndPoint)));
            Assert.Equal(0, pool.RentedCount);
        }

        // handler sweep wiring 테스트는 UDP SUBSCRIBE command 로 생성된 lease 가 만료되면
        // BrokerUdpDatagramHandler 의 sweep entry point 가 해당 remote subscription 을 제거하는지 검증한다.
        [Fact]
        public void SweepExpiredUdpLeases_WhenSubscribedRemoteExpires_RemovesRemoteSubscription()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            RefCountedBuffer datagram = RentDatagram(pool, "SUBSCRIBE alpha");
            SubscriptionTable subscriptions = new SubscriptionTable();
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20000);
            ManualTimeProvider time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-22T00:00:00Z"));
            BrokerUdpDatagramHandler handler = CreateHandler(
                subscriptions,
                new FakeTransport(),
                UdpLeaseOptions.CreateEnabled(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)),
                time);

            handler.OnDatagramReceived(endpoint, remoteEndPoint, datagram);
            time.Advance(TimeSpan.FromSeconds(31));

            int removed = handler.SweepExpiredUdpLeases(time.GetUtcNow());

            Assert.Equal(1, removed);
            Assert.False(subscriptions.IsSubscribed("alpha", BrokerSubscriber.ForUdp(endpoint, remoteEndPoint)));
            Assert.Equal(0, pool.RentedCount);
        }

        // handler publish activity 테스트는 lease 가 있는 UDP subscriber remote 가 PUBLISH 를 보내면
        // last-seen 이 갱신되어 같은 timeout 창 안의 sweep 에서 제거되지 않는지 검증한다.
        [Fact]
        public void SweepExpiredUdpLeases_WhenSubscribedRemotePublishesBeforeTimeout_KeepsRemoteSubscription()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(128);
            SubscriptionTable subscriptions = new SubscriptionTable();
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20000);
            ManualTimeProvider time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-22T00:00:00Z"));
            FakeTransport transport = new FakeTransport();
            BrokerUdpDatagramHandler handler = CreateHandler(
                subscriptions,
                transport,
                UdpLeaseOptions.CreateEnabled(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)),
                time);

            handler.OnDatagramReceived(endpoint, remoteEndPoint, RentDatagram(pool, "SUBSCRIBE alpha"));
            time.Advance(TimeSpan.FromSeconds(20));
            handler.OnDatagramReceived(endpoint, remoteEndPoint, RentDatagram(pool, "PUBLISH alpha PAYLOAD"));
            time.Advance(TimeSpan.FromSeconds(20));

            int removed = handler.SweepExpiredUdpLeases(time.GetUtcNow());

            transport.ReleaseAcceptedBuffers();
            Assert.Equal(0, removed);
            Assert.True(subscriptions.IsSubscribed("alpha", BrokerSubscriber.ForUdp(endpoint, remoteEndPoint)));
            Assert.Equal(0, pool.RentedCount);
        }

        private static BrokerUdpDatagramHandler CreateHandler(SubscriptionTable subscriptions, FakeTransport transport)
        {
            BrokerPublisher publisher = new BrokerPublisher(subscriptions, transport);
            return new BrokerUdpDatagramHandler(subscriptions, publisher);
        }

        private static BrokerUdpDatagramHandler CreateHandler(
            SubscriptionTable subscriptions,
            FakeTransport transport,
            UdpLeaseOptions leaseOptions,
            TimeProvider timeProvider)
        {
            BrokerPublisher publisher = new BrokerPublisher(subscriptions, transport);
            return new BrokerUdpDatagramHandler(subscriptions, publisher, leaseOptions, timeProvider);
        }

        private static BrokerUdpDatagramHandler CreateHandler(
            SubscriptionTable subscriptions,
            FakeTransport transport,
            UdpLeaseOptions leaseOptions,
            TimeProvider timeProvider,
            SubscriberRegistry registry)
        {
            BrokerPublisher publisher = new BrokerPublisher(subscriptions, transport);
            return new BrokerUdpDatagramHandler(subscriptions, publisher, leaseOptions, timeProvider, registry);
        }

        private static RefCountedBuffer RentDatagram(PinnedBlockMemoryPool pool, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            RefCountedBuffer datagram = pool.RentCounted();
            bytes.CopyTo(datagram.Span);
            datagram.SetLength(bytes.Length);
            return datagram;
        }

        private sealed class ManualTimeProvider : TimeProvider
        {
            private DateTimeOffset _utcNow;

            internal ManualTimeProvider(DateTimeOffset utcNow)
            {
                _utcNow = utcNow;
            }

            public override DateTimeOffset GetUtcNow()
            {
                return _utcNow;
            }

            internal void Advance(TimeSpan delta)
            {
                _utcNow = _utcNow.Add(delta);
            }
        }
    }
}
