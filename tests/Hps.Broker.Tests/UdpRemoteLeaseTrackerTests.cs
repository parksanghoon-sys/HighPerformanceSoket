using System;
using System.Net;
using Xunit;

namespace Hps.Broker.Tests
{
    public sealed class UdpRemoteLeaseTrackerTests
    {
        // disabled tracker 테스트: 기본 비활성 상태에서도 SUBSCRIBE 는 기존 SubscriptionTable 동작을 그대로 수행해야 한다.
        // lease table 만 비워 두어 idle expiry 기능이 꺼진 기본 BrokerServer 동작을 보존한다.
        [Fact]
        public void Subscribe_WhenOptionsAreDisabled_UpdatesSubscriptionWithoutLease()
        {
            SubscriptionTable table = new SubscriptionTable();
            ManualTimeProvider time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-22T00:00:00Z"));
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint remote = new IPEndPoint(IPAddress.Loopback, 20000);
            UdpRemoteLeaseTracker tracker = new UdpRemoteLeaseTracker(table, UdpLeaseOptions.Disabled, time);

            bool added = tracker.Subscribe("alpha", endpoint, remote);

            Assert.True(added);
            Assert.True(table.IsSubscribed("alpha", BrokerSubscriber.ForUdp(endpoint, remote)));
            Assert.Equal(0, tracker.LeaseCount);
        }

        // SUBSCRIBE activity 테스트: 같은 remote 가 여러 topic 에 가입하면 lease 는 하나만 유지되어야 한다.
        // topic 수는 tracker 내부에서 관리되어 마지막 UNSUBSCRIBE 때만 lease 를 제거하는 기준이 된다.
        [Fact]
        public void Subscribe_WhenOptionsAreEnabled_CreatesOneLeaseForRemoteAcrossTopics()
        {
            SubscriptionTable table = new SubscriptionTable();
            ManualTimeProvider time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-22T00:00:00Z"));
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint remote = new IPEndPoint(IPAddress.Loopback, 20000);
            UdpRemoteLeaseTracker tracker = new UdpRemoteLeaseTracker(table, UdpLeaseOptions.CreateEnabled(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)), time);

            tracker.Subscribe("alpha", endpoint, remote);
            tracker.Subscribe("beta", endpoint, remote);
            tracker.Subscribe("alpha", endpoint, remote);

            Assert.True(table.IsSubscribed("alpha", BrokerSubscriber.ForUdp(endpoint, remote)));
            Assert.True(table.IsSubscribed("beta", BrokerSubscriber.ForUdp(endpoint, remote)));
            Assert.Equal(1, tracker.LeaseCount);
        }

        // UNSUBSCRIBE activity 테스트: remote 가 아직 다른 topic 에 남아 있으면 lease 를 유지하고,
        // 마지막 topic 을 떠날 때만 lease 를 제거해야 이후 sweep 이 이미 떠난 remote 를 다시 만지지 않는다.
        [Fact]
        public void Unsubscribe_WhenLastTopicIsRemoved_RemovesLease()
        {
            SubscriptionTable table = new SubscriptionTable();
            ManualTimeProvider time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-22T00:00:00Z"));
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint remote = new IPEndPoint(IPAddress.Loopback, 20000);
            UdpRemoteLeaseTracker tracker = new UdpRemoteLeaseTracker(table, UdpLeaseOptions.CreateEnabled(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)), time);

            tracker.Subscribe("alpha", endpoint, remote);
            tracker.Subscribe("beta", endpoint, remote);

            tracker.Unsubscribe("alpha", endpoint, remote);
            Assert.Equal(1, tracker.LeaseCount);

            tracker.Unsubscribe("beta", endpoint, remote);
            Assert.Equal(0, tracker.LeaseCount);
        }

        // publish activity 테스트: publisher-only remote 는 subscription lease 를 만들지 않아야 한다.
        // 그렇지 않으면 구독하지 않은 remote 가 idle sweep 대상에 쌓여 cleanup 비용과 상태 오염을 만든다.
        [Fact]
        public void MarkPublishActivity_WhenRemoteHasNoLease_DoesNotCreateLease()
        {
            SubscriptionTable table = new SubscriptionTable();
            ManualTimeProvider time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-22T00:00:00Z"));
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint remote = new IPEndPoint(IPAddress.Loopback, 20000);
            UdpRemoteLeaseTracker tracker = new UdpRemoteLeaseTracker(table, UdpLeaseOptions.CreateEnabled(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)), time);

            tracker.MarkPublishActivity(endpoint, remote);

            Assert.Equal(0, tracker.LeaseCount);
        }

        // endpoint close cleanup 테스트: UDP socket 이 닫히면 같은 local endpoint 에 묶인 모든 remote lease 와 subscription 이 제거되어야 한다.
        // 다른 local endpoint 의 같은 remote address 는 별도 subscriber 이므로 보존한다.
        [Fact]
        public void RemoveEndpoint_WhenEndpointCloses_RemovesOnlyThatEndpointLeasesAndSubscriptions()
        {
            SubscriptionTable table = new SubscriptionTable();
            ManualTimeProvider time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-22T00:00:00Z"));
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            FakeUdpEndpoint survivorEndpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10001));
            EndPoint remote = new IPEndPoint(IPAddress.Loopback, 20000);
            UdpRemoteLeaseTracker tracker = new UdpRemoteLeaseTracker(table, UdpLeaseOptions.CreateEnabled(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)), time);

            tracker.Subscribe("alpha", endpoint, remote);
            tracker.Subscribe("beta", endpoint, remote);
            tracker.Subscribe("alpha", survivorEndpoint, remote);

            int removed = tracker.RemoveEndpoint(endpoint);

            Assert.Equal(2, removed);
            Assert.False(table.IsSubscribed("alpha", BrokerSubscriber.ForUdp(endpoint, remote)));
            Assert.False(table.IsSubscribed("beta", BrokerSubscriber.ForUdp(endpoint, remote)));
            Assert.True(table.IsSubscribed("alpha", BrokerSubscriber.ForUdp(survivorEndpoint, remote)));
            Assert.Equal(1, tracker.LeaseCount);
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
