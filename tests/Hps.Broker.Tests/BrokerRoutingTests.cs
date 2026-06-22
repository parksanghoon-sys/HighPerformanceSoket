using System;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hps.Transport;
using Xunit;

namespace Hps.Broker.Tests
{
    public sealed class BrokerRoutingTests
    {
        // 기본 구독 라우팅 테스트: 하나의 connection 이 특정 topic 에 subscribe 되면
        // 같은 topic snapshot 에만 나타나고 다른 topic 에는 섞이지 않아야 fan-out topic 격리가 유지된다.
        [Fact]
        public void Subscribe_WhenConnectionSubscribes_AddsOnlyThatTopic()
        {
            SubscriptionTable table = new SubscriptionTable();
            FakeConnection alpha = new FakeConnection();
            FakeConnection beta = new FakeConnection();

            bool added = table.Subscribe("alpha", alpha);
            bool addedAgain = table.Subscribe("alpha", alpha);
            table.Subscribe("beta", beta);

            IConnection[] subscribers = new IConnection[4];
            int total = table.CopySubscribers("alpha", subscribers);

            Assert.True(added);
            Assert.False(addedAgain);
            Assert.True(table.IsSubscribed("alpha", alpha));
            Assert.False(table.IsSubscribed("alpha", beta));
            Assert.Equal(1, table.CountSubscribers("alpha"));
            Assert.Equal(1, total);
            Assert.Same(alpha, subscribers[0]);
        }

        // 해지 테스트: unsubscribe 는 지정 topic 의 지정 connection 만 제거해야 한다.
        // 제거 후 빈 topic entry 를 즉시 지우지 않는 NoCleanup 정책은 내부 구현 사항이며, public 관측은 구독자 0명이다.
        [Fact]
        public void Unsubscribe_WhenConnectionLeaves_RemovesOnlyThatSubscription()
        {
            SubscriptionTable table = new SubscriptionTable();
            FakeConnection alpha = new FakeConnection();
            FakeConnection beta = new FakeConnection();

            table.Subscribe("topic", alpha);
            table.Subscribe("topic", beta);

            bool removed = table.Unsubscribe("topic", alpha);
            bool removedAgain = table.Unsubscribe("topic", alpha);

            Assert.True(removed);
            Assert.False(removedAgain);
            Assert.False(table.IsSubscribed("topic", alpha));
            Assert.True(table.IsSubscribed("topic", beta));
            Assert.Equal(1, table.CountSubscribers("topic"));
        }

        // 연결 종료 정리 API 계약 테스트: transport 가 닫힌 connection 을 알려줄 때 Broker 는 topic 이름을 모르더라도
        // 해당 connection 을 모든 구독 set 에서 제거할 수 있어야 churn 서버에서 dead connection 참조가 누적되지 않는다.
        [Fact]
        public void SubscriptionTable_Contract_ExposesConnectionWideCleanup()
        {
            Type? tableType = Type.GetType("Hps.Broker.SubscriptionTable, Hps.Broker");
            Assert.NotNull(tableType);

            MethodInfo? unsubscribeAll = tableType!.GetMethod("UnsubscribeAll", new Type[] { typeof(IConnection) });

            Assert.NotNull(unsubscribeAll);
            Assert.Equal(typeof(int), unsubscribeAll!.ReturnType);
        }

        // endpoint 중심 fan-out 전환 계약 테스트: Broker routing table 이 raw TCP connection 배열만 노출하면
        // 이후 UDP endpoint 를 같은 publish 경로에 넣을 때 routing 값과 send target 모델을 다시 갈아엎어야 한다.
        // 먼저 TCP 동작을 유지한 채 구독자 snapshot 을 BrokerSubscriber 값으로 복사하는 public 경계를 고정한다.
        [Fact]
        public void SubscriptionTable_Contract_ExposesBrokerSubscriberSnapshot()
        {
            Type? tableType = Type.GetType("Hps.Broker.SubscriptionTable, Hps.Broker");
            Type? subscriberType = Type.GetType("Hps.Broker.BrokerSubscriber, Hps.Broker");
            Assert.NotNull(tableType);
            Assert.NotNull(subscriberType);

            MethodInfo? copySubscribers = tableType!.GetMethod(
                "CopySubscribers",
                new Type[] { typeof(string), subscriberType!.MakeArrayType() });

            Assert.NotNull(copySubscribers);
            Assert.Equal(typeof(int), copySubscribers!.ReturnType);
        }

        // TCP 구독을 기존 Subscribe(topic, IConnection) API 로 추가해도 내부 snapshot 은 endpoint target 값이어야 한다.
        // 이 테스트는 TCP command handler 를 흔들지 않고 table 내부 값을 다음 UDP broker 결선에 재사용 가능한 형태로 바꾸는지 검증한다.
        [Fact]
        public void CopySubscribers_WhenBrokerSubscriberDestinationIsUsed_CopiesTcpEndpointTargets()
        {
            Type? subscriberType = Type.GetType("Hps.Broker.BrokerSubscriber, Hps.Broker");
            Assert.NotNull(subscriberType);

            SubscriptionTable table = new SubscriptionTable();
            FakeConnection connection = new FakeConnection();
            table.Subscribe("topic", connection);

            Array destination = Array.CreateInstance(subscriberType!, 1);
            MethodInfo? copySubscribers = typeof(SubscriptionTable).GetMethod(
                "CopySubscribers",
                new Type[] { typeof(string), destination.GetType() });
            Assert.NotNull(copySubscribers);

            int total = (int)copySubscribers!.Invoke(table, new object[] { "topic", destination })!;
            object? subscriber = destination.GetValue(0);
            PropertyInfo? transportKind = subscriberType!.GetProperty("TransportKind");
            Assert.NotNull(transportKind);

            Assert.Equal(1, total);
            Assert.NotNull(subscriber);
            Assert.Equal(EndpointTransportKind.Tcp, transportKind!.GetValue(subscriber));
        }

        // UDP runtime target 계약 테스트: D060은 UDP 구독자를 stable id 가 아니라
        // bind 된 IUdpEndpoint 와 remote EndPoint 조합으로 표현한다고 결정했다.
        // 먼저 public factory 존재와 TransportKind 를 Red 로 고정해 TCP 전용 BrokerSubscriber 경계를 깨는 첫 단계를 검증한다.
        [Fact]
        public void BrokerSubscriber_Contract_ExposesUdpRuntimeTargetFactory()
        {
            Type? subscriberType = Type.GetType("Hps.Broker.BrokerSubscriber, Hps.Broker");
            Assert.NotNull(subscriberType);

            MethodInfo? forUdp = subscriberType!.GetMethod("ForUdp", new Type[] { typeof(IUdpEndpoint), typeof(EndPoint) });

            Assert.NotNull(forUdp);

            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 10001);
            object? subscriber = forUdp!.Invoke(null, new object[] { endpoint, remoteEndPoint });
            PropertyInfo? transportKind = subscriberType.GetProperty("TransportKind");
            Assert.NotNull(transportKind);

            Assert.NotNull(subscriber);
            Assert.Equal(EndpointTransportKind.Udp, transportKind!.GetValue(subscriber));
        }

        // UDP target identity 테스트: D060의 runtime target 은 local endpoint 객체와 remote EndPoint 값의 조합이다.
        // 같은 endpoint 객체와 같은 remote 주소는 duplicate subscribe 로 막고, 같은 endpoint 의 다른 remote 는 별도 구독자로 유지해야 한다.
        [Fact]
        public void Subscribe_WhenUdpRuntimeTargetsAreUsed_DeduplicatesByEndpointAndRemote()
        {
            SubscriptionTable table = new SubscriptionTable();
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint remote = new IPEndPoint(IPAddress.Loopback, 20000);
            EndPoint sameRemoteValue = new IPEndPoint(IPAddress.Loopback, 20000);
            EndPoint otherRemote = new IPEndPoint(IPAddress.Loopback, 20001);

            BrokerSubscriber first = BrokerSubscriber.ForUdp(endpoint, remote);
            BrokerSubscriber duplicate = BrokerSubscriber.ForUdp(endpoint, sameRemoteValue);
            BrokerSubscriber other = BrokerSubscriber.ForUdp(endpoint, otherRemote);

            bool added = table.Subscribe("topic", first);
            bool addedDuplicate = table.Subscribe("topic", duplicate);
            bool addedOther = table.Subscribe("topic", other);

            Assert.True(added);
            Assert.False(addedDuplicate);
            Assert.True(addedOther);
            Assert.True(table.IsSubscribed("topic", duplicate));
            Assert.Equal(2, table.CountSubscribers("topic"));
        }

        // 연결 종료 정리 동작 테스트: 하나의 connection 이 여러 topic 에 구독된 뒤 닫히면
        // 모든 topic 에서 해당 connection 참조만 제거되어야 dead connection 누적과 CountSubscribers 팽창을 막을 수 있다.
        [Fact]
        public void UnsubscribeAll_WhenConnectionIsClosed_RemovesItFromEveryTopicOnly()
        {
            SubscriptionTable table = new SubscriptionTable();
            FakeConnection closed = new FakeConnection();
            FakeConnection survivor = new FakeConnection();

            table.Subscribe("alpha", closed);
            table.Subscribe("beta", closed);
            table.Subscribe("alpha", survivor);

            int removed = table.UnsubscribeAll(closed);
            int removedAgain = table.UnsubscribeAll(closed);

            Assert.Equal(2, removed);
            Assert.Equal(0, removedAgain);
            Assert.False(table.IsSubscribed("alpha", closed));
            Assert.False(table.IsSubscribed("beta", closed));
            Assert.True(table.IsSubscribed("alpha", survivor));
            Assert.Equal(1, table.CountSubscribers("alpha"));
            Assert.Equal(0, table.CountSubscribers("beta"));
        }

        // UDP remote 만료 정리 테스트: D072의 idle sweep 은 특정 local endpoint/remote 조합만 모든 topic 에서 제거해야 한다.
        // 같은 UDP endpoint 의 다른 remote, 다른 endpoint 의 같은 remote, TCP subscriber 는 유지해야 sweep 이 과도한 cleanup 이 되지 않는다.
        [Fact]
        public void UnsubscribeAll_WhenUdpRemoteExpires_RemovesOnlyThatEndpointRemoteFromEveryTopic()
        {
            SubscriptionTable table = new SubscriptionTable();
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            FakeUdpEndpoint otherEndpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10001));
            EndPoint expiredRemote = new IPEndPoint(IPAddress.Loopback, 20000);
            EndPoint otherRemote = new IPEndPoint(IPAddress.Loopback, 20001);
            FakeConnection tcpSurvivor = new FakeConnection();
            BrokerSubscriber expiredAlpha = BrokerSubscriber.ForUdp(endpoint, expiredRemote);
            BrokerSubscriber expiredBeta = BrokerSubscriber.ForUdp(endpoint, expiredRemote);
            BrokerSubscriber sameEndpointOtherRemote = BrokerSubscriber.ForUdp(endpoint, otherRemote);
            BrokerSubscriber otherEndpointSameRemote = BrokerSubscriber.ForUdp(otherEndpoint, expiredRemote);
            table.Subscribe("alpha", expiredAlpha);
            table.Subscribe("beta", expiredBeta);
            table.Subscribe("alpha", sameEndpointOtherRemote);
            table.Subscribe("alpha", otherEndpointSameRemote);
            table.Subscribe("alpha", tcpSurvivor);

            int removed = table.UnsubscribeAll(endpoint, expiredRemote);
            int removedAgain = table.UnsubscribeAll(endpoint, expiredRemote);

            Assert.Equal(2, removed);
            Assert.Equal(0, removedAgain);
            Assert.False(table.IsSubscribed("alpha", expiredAlpha));
            Assert.False(table.IsSubscribed("beta", expiredBeta));
            Assert.True(table.IsSubscribed("alpha", sameEndpointOtherRemote));
            Assert.True(table.IsSubscribed("alpha", otherEndpointSameRemote));
            Assert.True(table.IsSubscribed("alpha", tcpSurvivor));
            Assert.Equal(3, table.CountSubscribers("alpha"));
            Assert.Equal(0, table.CountSubscribers("beta"));
        }

        // snapshot 복사 테스트: publish fan-out 은 caller 가 준비한 배열에 현재 구독자를 복사해 사용할 수 있어야 한다.
        // 반환값은 전체 구독자 수이고, destination 이 작으면 복사 가능한 앞부분만 채워 재시도 크기를 판단할 수 있게 한다.
        [Fact]
        public void CopySubscribers_WhenDestinationIsSmaller_ReturnsTotalSubscriberCount()
        {
            SubscriptionTable table = new SubscriptionTable();
            FakeConnection first = new FakeConnection();
            FakeConnection second = new FakeConnection();
            table.Subscribe("topic", first);
            table.Subscribe("topic", second);
            IConnection[] destination = new IConnection[1];

            int total = table.CopySubscribers("topic", destination);

            Assert.Equal(2, total);
            Assert.NotNull(destination[0]);
        }

        // D008 R1 회귀 테스트: 빈 topic 을 즉시 제거하는 eager-cleanup 은
        // "새 구독 추가"와 "기존 마지막 구독 해지"가 겹칠 때 새 구독을 제거된 set 에 넣어 유실시킬 수 있다.
        [Fact]
        public async Task SubscribeAndUnsubscribe_WhenTopicBecomesEmptyConcurrently_DoesNotLoseNewSubscriber()
        {
            const int Iterations = 20000;

            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                SubscriptionTable table = new SubscriptionTable();
                FakeConnection existing = new FakeConnection();
                FakeConnection incoming = new FakeConnection();
                table.Subscribe("topic", existing);

                using (ManualResetEventSlim start = new ManualResetEventSlim(false))
                {
                    Task subscribeTask = Task.Run(delegate()
                    {
                        start.Wait();
                        table.Subscribe("topic", incoming);
                    });
                    Task unsubscribeTask = Task.Run(delegate()
                    {
                        start.Wait();
                        table.Unsubscribe("topic", existing);
                    });

                    start.Set();
                    await Task.WhenAll(subscribeTask, unsubscribeTask);
                }

                Assert.True(table.IsSubscribed("topic", incoming), "iteration=" + iteration);
            }
        }
    }
}
