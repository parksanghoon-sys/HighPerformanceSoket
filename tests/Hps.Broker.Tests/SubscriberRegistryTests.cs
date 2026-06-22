using System;
using System.Net;
using System.Reflection;
using Xunit;

namespace Hps.Broker.Tests
{
    public sealed class SubscriberRegistryTests
    {
        // registry 타입 계약 테스트: stable identity 기능은 SubscriptionTable 을 대체하지 않고 그 위에서 rebind metadata 만 관리해야 한다.
        // 타입 부재를 compile error 가 아니라 assertion failure 로 먼저 확인해 TDD Red 단계의 원인을 분명히 한다.
        [Fact]
        public void SubscriberRegistry_Contract_Exists()
        {
            Type? identity = Type.GetType("Hps.Broker.SubscriberIdentity, Hps.Broker");
            Type? result = Type.GetType("Hps.Broker.SubscriberRegistrationResult, Hps.Broker");
            Type? registry = Type.GetType("Hps.Broker.SubscriberRegistry, Hps.Broker");

            Assert.NotNull(identity);
            Assert.NotNull(result);
            Assert.NotNull(registry);
            Assert.True(registry!.IsClass);
            Assert.NotNull(registry.GetProperty("IdentityCount", BindingFlags.Instance | BindingFlags.NonPublic));
        }

        // registered subscribe 테스트: REGISTER 이후 SUBSCRIBE 는 runtime target 이 아니라 identity topic set 에 기록되어야 한다.
        // publish fan-out 은 여전히 SubscriptionTable 의 현재 online target 으로만 흘러야 한다.
        [Fact]
        public void Subscribe_WhenTargetIsRegistered_AddsCurrentTargetToTopic()
        {
            SubscriptionTable table = new SubscriptionTable();
            SubscriberRegistry registry = new SubscriberRegistry(table);
            FakeConnection connection = new FakeConnection();
            BrokerSubscriber target = BrokerSubscriber.ForTcp(connection);

            registry.Register(SubscriberIdentity.Create("device-a"), target, out _, out _);
            bool added = registry.Subscribe("alpha", target);

            Assert.True(added);
            Assert.True(table.IsSubscribed("alpha", connection));
            Assert.Equal(1, registry.IdentityCount);
        }

        // reconnect rebind 테스트: 같은 id 가 새 target 으로 다시 REGISTER 되면 old target 을 routing table 에서 제거하고
        // 보존된 topic set 을 new target 으로 다시 붙여야 한다.
        [Fact]
        public void Register_WhenSameIdentityUsesNewTarget_RebindsTopicsToNewTarget()
        {
            SubscriptionTable table = new SubscriptionTable();
            SubscriberRegistry registry = new SubscriberRegistry(table);
            FakeConnection oldConnection = new FakeConnection();
            FakeConnection newConnection = new FakeConnection();
            BrokerSubscriber oldTarget = BrokerSubscriber.ForTcp(oldConnection);
            BrokerSubscriber newTarget = BrokerSubscriber.ForTcp(newConnection);
            SubscriberIdentity identity = SubscriberIdentity.Create("device-a");

            registry.Register(identity, oldTarget, out _, out _);
            registry.Subscribe("alpha", oldTarget);
            SubscriberRegistrationResult result = registry.Register(identity, newTarget, out BrokerSubscriber? replaced, out string[] reboundTopics);

            Assert.Equal(SubscriberRegistrationResult.Rebound, result);
            Assert.True(replaced.HasValue);
            Assert.Equal(new string[] { "alpha" }, reboundTopics);
            Assert.False(table.IsSubscribed("alpha", oldConnection));
            Assert.True(table.IsSubscribed("alpha", newConnection));
        }

        // disconnect 보존 테스트: current target 이 닫히면 routing table 에서는 제거하지만 identity topic set 은 retention 대상이다.
        // 같은 id 가 다시 REGISTER 되면 payload replay 없이 이후 publish 대상만 복구한다.
        [Fact]
        public void RemoveTarget_WhenRegisteredTargetDisconnects_PreservesTopicsForReconnect()
        {
            SubscriptionTable table = new SubscriptionTable();
            SubscriberRegistry registry = new SubscriberRegistry(table);
            FakeConnection oldConnection = new FakeConnection();
            FakeConnection newConnection = new FakeConnection();
            BrokerSubscriber oldTarget = BrokerSubscriber.ForTcp(oldConnection);
            BrokerSubscriber newTarget = BrokerSubscriber.ForTcp(newConnection);
            SubscriberIdentity identity = SubscriberIdentity.Create("device-a");

            registry.Register(identity, oldTarget, out _, out _);
            registry.Subscribe("alpha", oldTarget);
            int removed = registry.RemoveTarget(oldTarget, DateTimeOffset.Parse("2026-06-22T00:00:00Z"));
            registry.Register(identity, newTarget, out _, out _);

            Assert.Equal(1, removed);
            Assert.False(table.IsSubscribed("alpha", oldConnection));
            Assert.True(table.IsSubscribed("alpha", newConnection));
        }

        // duplicate target 정책 테스트: 같은 runtime target 이 다른 stable id 로 REGISTER 되면 protocol error 로 처리할 수 있게 reject 해야 한다.
        // 한 socket 이 두 logical subscriber 로 동시에 보이면 cleanup 과 rebind 기준이 모호해진다.
        [Fact]
        public void Register_WhenSameTargetUsesDifferentIdentity_ReturnsTargetConflict()
        {
            SubscriptionTable table = new SubscriptionTable();
            SubscriberRegistry registry = new SubscriberRegistry(table);
            BrokerSubscriber target = BrokerSubscriber.ForTcp(new FakeConnection());

            registry.Register(SubscriberIdentity.Create("device-a"), target, out _, out _);
            SubscriberRegistrationResult result = registry.Register(SubscriberIdentity.Create("device-b"), target, out _, out _);

            Assert.Equal(SubscriberRegistrationResult.TargetAlreadyRegisteredWithDifferentIdentity, result);
        }

        // late REGISTER 정리 테스트: target 이 REGISTER 전에 runtime 구독을 만든 뒤 stable identity 로 전환되면,
        // 그 기존 구독은 identity metadata 에 없으므로 REGISTER 시점에 제거해야 이후 close cleanup 에서 stale target 이 남지 않는다.
        [Fact]
        public void Register_WhenTargetHadRuntimeSubscriptionsBeforeRegister_ClearsUntrackedRuntimeSubscriptions()
        {
            SubscriptionTable table = new SubscriptionTable();
            SubscriberRegistry registry = new SubscriberRegistry(table);
            FakeConnection connection = new FakeConnection();
            BrokerSubscriber target = BrokerSubscriber.ForTcp(connection);

            registry.Subscribe("alpha", target);
            SubscriberRegistrationResult result = registry.Register(SubscriberIdentity.Create("device-a"), target, out _, out _);
            int removed = registry.RemoveTarget(target, DateTimeOffset.Parse("2026-06-22T00:00:00Z"));

            Assert.Equal(SubscriberRegistrationResult.Registered, result);
            Assert.False(table.IsSubscribed("alpha", connection));
            Assert.Equal(0, removed);
        }

        // unregister 테스트: explicit UNREGISTER 는 identity metadata 와 현재 routing target 을 함께 제거해야 한다.
        // 이것이 없으면 disconnected identity retention 만료 전까지 원치 않는 재바인딩이 남는다.
        [Fact]
        public void Unregister_WhenCurrentTargetMatches_RemovesIdentityAndSubscriptions()
        {
            SubscriptionTable table = new SubscriptionTable();
            SubscriberRegistry registry = new SubscriberRegistry(table);
            FakeConnection connection = new FakeConnection();
            BrokerSubscriber target = BrokerSubscriber.ForTcp(connection);
            SubscriberIdentity identity = SubscriberIdentity.Create("device-a");

            registry.Register(identity, target, out _, out _);
            registry.Subscribe("alpha", target);
            int removed = registry.Unregister(identity, target);

            Assert.Equal(1, removed);
            Assert.False(table.IsSubscribed("alpha", connection));
            Assert.Equal(0, registry.IdentityCount);
        }

        // unsubscribe metadata 테스트: registered target 의 UNSUBSCRIBE 는 routing table 뿐 아니라 보존 topic set 에서도 제거되어야 한다.
        // 제거된 topic 이 남아 있으면 reconnect 때 사용자가 해제한 topic 이 다시 fan-out 대상이 된다.
        [Fact]
        public void Unsubscribe_WhenTargetIsRegistered_RemovesTopicFromReconnectMetadata()
        {
            SubscriptionTable table = new SubscriptionTable();
            SubscriberRegistry registry = new SubscriberRegistry(table);
            FakeConnection oldConnection = new FakeConnection();
            FakeConnection newConnection = new FakeConnection();
            BrokerSubscriber oldTarget = BrokerSubscriber.ForTcp(oldConnection);
            BrokerSubscriber newTarget = BrokerSubscriber.ForTcp(newConnection);
            SubscriberIdentity identity = SubscriberIdentity.Create("device-a");

            registry.Register(identity, oldTarget, out _, out _);
            registry.Subscribe("alpha", oldTarget);
            registry.Unsubscribe("alpha", oldTarget);
            registry.RemoveTarget(oldTarget, DateTimeOffset.Parse("2026-06-22T00:00:00Z"));
            registry.Register(identity, newTarget, out _, out _);

            Assert.False(table.IsSubscribed("alpha", oldConnection));
            Assert.False(table.IsSubscribed("alpha", newConnection));
        }

        // disconnected sweep 테스트: retention timeout 을 넘긴 disconnected identity 는 metadata 를 제거해야 한다.
        // sweep 되지 않으면 churn 환경에서 더 이상 돌아오지 않는 subscriber id 가 registry 에 계속 남는다.
        [Fact]
        public void SweepDisconnected_WhenRetentionExpired_RemovesDisconnectedIdentity()
        {
            SubscriptionTable table = new SubscriptionTable();
            SubscriberRegistry registry = new SubscriberRegistry(table);
            FakeConnection connection = new FakeConnection();
            BrokerSubscriber target = BrokerSubscriber.ForTcp(connection);
            SubscriberIdentity identity = SubscriberIdentity.Create("device-a");
            DateTimeOffset disconnectedAt = DateTimeOffset.Parse("2026-06-22T00:00:00Z");

            registry.Register(identity, target, out _, out _);
            registry.Subscribe("alpha", target);
            registry.RemoveTarget(target, disconnectedAt);
            int removed = registry.SweepDisconnected(disconnectedAt.AddMinutes(6), TimeSpan.FromMinutes(5));

            Assert.Equal(1, removed);
            Assert.Equal(0, registry.IdentityCount);
        }

        // UDP endpoint cleanup 테스트: endpoint close 는 해당 endpoint 의 현재 stable target 을 routing table 에서 제거하되 topic metadata 는 retention 대상이다.
        // 같은 id 가 다른 UDP remote 로 재등록되면 보존된 topic 이 새 runtime target 으로만 rebind 되어야 한다.
        [Fact]
        public void RemoveUdpEndpoint_WhenRegisteredUdpTargetDisconnects_PreservesTopicsForReconnect()
        {
            SubscriptionTable table = new SubscriptionTable();
            SubscriberRegistry registry = new SubscriberRegistry(table);
            FakeUdpEndpoint oldEndpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            FakeUdpEndpoint newEndpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10001));
            EndPoint oldRemote = new IPEndPoint(IPAddress.Loopback, 20000);
            EndPoint newRemote = new IPEndPoint(IPAddress.Loopback, 20001);
            BrokerSubscriber oldTarget = BrokerSubscriber.ForUdp(oldEndpoint, oldRemote);
            BrokerSubscriber newTarget = BrokerSubscriber.ForUdp(newEndpoint, newRemote);
            SubscriberIdentity identity = SubscriberIdentity.Create("device-a");

            registry.Register(identity, oldTarget, out _, out _);
            registry.Subscribe("alpha", oldTarget);
            int removed = registry.RemoveUdpEndpoint(oldEndpoint, DateTimeOffset.Parse("2026-06-22T00:00:00Z"));
            registry.Register(identity, newTarget, out _, out _);

            Assert.Equal(1, removed);
            Assert.False(table.IsSubscribed("alpha", oldTarget));
            Assert.True(table.IsSubscribed("alpha", newTarget));
        }
    }
}
