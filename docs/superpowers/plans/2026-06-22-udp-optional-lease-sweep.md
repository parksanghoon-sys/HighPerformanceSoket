# UDP Optional Lease Sweep Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** UDP stale remote cleanup 을 기본 비활성 상태로 유지하면서, Broker 계층에 내부 lease tracker 와 순수 sweep 로직을 작은 단위로 추가한다.

**Architecture:** `BrokerUdpDatagramHandler` 는 기존 `SubscriptionTable` 과 `BrokerPublisher` 흐름을 유지하되, UDP command activity 를 `UdpRemoteLeaseTracker` 로 위임한다. tracker 는 `(IUdpEndpoint, EndPoint)` key 로 lease 를 관리하고, sweep 은 만료된 remote target 별로 `SubscriptionTable.UnsubscribeAll(IUdpEndpoint, EndPoint)` 를 호출한다. Server host timer 와 운영자용 public 설정 표면은 이 계획의 범위가 아니다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, `TimeProvider`, 기존 `Hps.Broker`/`Hps.Transport` abstraction.

## Global Constraints

- TFM 은 `net9.0`, LangVersion 은 C# 8.0 이며 file-scoped namespace, record, target-typed `new()` 를 쓰지 않는다.
- 모든 문서와 주석은 한국어로 작성한다. public API 에는 XML doc 으로 의도, 동시성 가정, 소유권을 적는다.
- 코드 변경은 Red-Green-Refactor 를 따른다. 테스트에는 무엇을 검증하는지 한국어 주석을 붙인다.
- 작업은 기능별 작은 단위로 나누고, 각 Task 는 별도 커밋으로 끝낸다.
- D073 범위에 따라 기본 idle expiry 는 비활성이다. `BrokerServer` public 설정 API 와 host timer 는 이번 계획에서 구현하지 않는다.
- D008 정책에 따라 빈 topic entry eager cleanup 은 하지 않는다.
- UDP subscription identity 는 D060에 따라 `(IUdpEndpoint localEndpoint, EndPoint remoteEndPoint)` runtime target 이다. stable subscriber identity 를 추가하지 않는다.

---

## File Structure

- `src/Hps.Broker/Properties/AssemblyInfo.cs`
  - 테스트 assembly 에 internal broker lease 타입 접근을 허용한다.
- `src/Hps.Broker/UdpLeaseOptions.cs`
  - Broker 내부 lease tracker 설정값을 담는다. 기본값은 disabled 이다.
- `src/Hps.Broker/UdpRemoteLeaseTracker.cs`
  - UDP remote lease table, activity 갱신, endpoint cleanup, sweep 를 담당한다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`
  - SUBSCRIBE/UNSUBSCRIBE/PUBLISH/endpoint close activity 를 tracker 로 연결한다.
- `tests/Hps.Broker.Tests/UdpLeaseOptionsTests.cs`
  - options 기본값과 enabled 값 검증을 담당한다.
- `tests/Hps.Broker.Tests/UdpRemoteLeaseTrackerTests.cs`
  - tracker activity, topic count, endpoint cleanup, sweep 를 직접 검증한다.
- `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`
  - handler 가 tracker 를 실제 command 처리 경로에 연결하는지 검증한다.
- Root state docs
  - `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md` 를 각 Task 완료마다 갱신한다.

---

### Task 1: 내부 options 타입과 테스트 접근 경계

**Files:**
- Create: `src/Hps.Broker/Properties/AssemblyInfo.cs`
- Create: `src/Hps.Broker/UdpLeaseOptions.cs`
- Create: `tests/Hps.Broker.Tests/UdpLeaseOptionsTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Produces:
  - `internal sealed class UdpLeaseOptions`
  - `internal static UdpLeaseOptions Disabled { get; }`
  - `internal static UdpLeaseOptions CreateEnabled(TimeSpan idleTimeout, TimeSpan sweepInterval)`
  - `internal bool Enabled { get; }`
  - `internal TimeSpan IdleTimeout { get; }`
  - `internal TimeSpan SweepInterval { get; }`

- [ ] **Step 1: Write the failing test**

Create `tests/Hps.Broker.Tests/UdpLeaseOptionsTests.cs`:

```csharp
using System;
using Xunit;

namespace Hps.Broker.Tests
{
    public sealed class UdpLeaseOptionsTests
    {
        // 기본 옵션 테스트: D073은 idle expiry 기본값을 비활성으로 고정했다.
        // disabled 상태에서는 lease 갱신과 sweep 이 모두 건너뛰어 기존 UDP broker 동작이 유지되어야 한다.
        [Fact]
        public void Disabled_WhenRead_ReturnsDisabledZeroIntervals()
        {
            UdpLeaseOptions options = UdpLeaseOptions.Disabled;

            Assert.False(options.Enabled);
            Assert.Equal(TimeSpan.Zero, options.IdleTimeout);
            Assert.Equal(TimeSpan.Zero, options.SweepInterval);
        }

        // enabled 옵션 검증 테스트: idle timeout 과 sweep interval 은 시간 비교의 기준이므로
        // 0 이하 값을 허용하면 remote 가 즉시 만료되거나 timer 가 busy loop 로 흐를 수 있다.
        [Fact]
        public void Enabled_WhenNonPositiveIntervalsAreUsed_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                delegate { UdpLeaseOptions.CreateEnabled(TimeSpan.Zero, TimeSpan.FromSeconds(1)); });
            Assert.Throws<ArgumentOutOfRangeException>(
                delegate { UdpLeaseOptions.CreateEnabled(TimeSpan.FromSeconds(1), TimeSpan.Zero); });
        }

        // enabled 옵션 생성 테스트: 이번 단계는 운영자용 public 설정을 열지 않고 내부 options 값만 확정한다.
        // 이후 handler/tracker 는 이 값을 주입받아 기본 비활성 경로와 선택 활성 경로를 분리한다.
        [Fact]
        public void Enabled_WhenPositiveIntervalsAreUsed_StoresValues()
        {
            UdpLeaseOptions options = UdpLeaseOptions.CreateEnabled(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

            Assert.True(options.Enabled);
            Assert.Equal(TimeSpan.FromSeconds(30), options.IdleTimeout);
            Assert.Equal(TimeSpan.FromSeconds(5), options.SweepInterval);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter FullyQualifiedName~UdpLeaseOptionsTests
```

Expected: FAIL with compile errors that `UdpLeaseOptions` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/Hps.Broker/Properties/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Hps.Broker.Tests")]
```

Create `src/Hps.Broker/UdpLeaseOptions.cs`:

```csharp
using System;

namespace Hps.Broker
{
    /// <summary>
    /// UDP remote lease cleanup 의 내부 설정값이다.
    ///
    /// 이 타입은 BrokerServer 운영자용 public 설정 표면이 아니다. D073에 따라 기본값은 비활성이며,
    /// 구현 검증과 이후 host timer wiring 에서만 명시적으로 활성 옵션을 주입한다.
    /// </summary>
    internal sealed class UdpLeaseOptions
    {
        private static readonly UdpLeaseOptions DisabledInstance = new UdpLeaseOptions(false, TimeSpan.Zero, TimeSpan.Zero);

        private UdpLeaseOptions(bool enabled, TimeSpan idleTimeout, TimeSpan sweepInterval)
        {
            Enabled = enabled;
            IdleTimeout = idleTimeout;
            SweepInterval = sweepInterval;
        }

        internal static UdpLeaseOptions Disabled
        {
            get { return DisabledInstance; }
        }

        internal bool Enabled { get; }

        internal TimeSpan IdleTimeout { get; }

        internal TimeSpan SweepInterval { get; }

        internal static UdpLeaseOptions CreateEnabled(TimeSpan idleTimeout, TimeSpan sweepInterval)
        {
            if (idleTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(idleTimeout));
            if (sweepInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(sweepInterval));

            return new UdpLeaseOptions(true, idleTimeout, sweepInterval);
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter FullyQualifiedName~UdpLeaseOptionsTests
```

Expected: PASS, 3 tests.

- [ ] **Step 5: Verify and commit**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
git diff --check
git status --short
git add src/Hps.Broker/Properties/AssemblyInfo.cs src/Hps.Broker/UdpLeaseOptions.cs tests/Hps.Broker.Tests/UdpLeaseOptionsTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: add udp lease options"
```

Expected: build warning 0/error 0, all tests pass, whitespace check passes.

---

### Task 2: Lease tracker activity 모델

**Files:**
- Create: `src/Hps.Broker/UdpRemoteLeaseTracker.cs`
- Create: `tests/Hps.Broker.Tests/UdpRemoteLeaseTrackerTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - `UdpLeaseOptions.Disabled`
  - `UdpLeaseOptions.CreateEnabled(TimeSpan idleTimeout, TimeSpan sweepInterval)`
  - `SubscriptionTable.Subscribe(string, BrokerSubscriber)`
  - `SubscriptionTable.Unsubscribe(string, BrokerSubscriber)`
  - `SubscriptionTable.UnsubscribeAll(IUdpEndpoint)`
- Produces:
  - `internal sealed class UdpRemoteLeaseTracker`
  - `internal UdpRemoteLeaseTracker(SubscriptionTable subscriptions, UdpLeaseOptions options, TimeProvider timeProvider)`
  - `internal bool Subscribe(string topic, IUdpEndpoint endpoint, EndPoint remoteEndPoint)`
  - `internal bool Unsubscribe(string topic, IUdpEndpoint endpoint, EndPoint remoteEndPoint)`
  - `internal void MarkPublishActivity(IUdpEndpoint endpoint, EndPoint remoteEndPoint)`
  - `internal int RemoveEndpoint(IUdpEndpoint endpoint)`
  - `internal int LeaseCount { get; }`

- [ ] **Step 1: Write the failing test**

Create `tests/Hps.Broker.Tests/UdpRemoteLeaseTrackerTests.cs` with these tests:

```csharp
using System;
using System.Net;
using Xunit;

namespace Hps.Broker.Tests
{
    public sealed class UdpRemoteLeaseTrackerTests
    {
        // disabled tracker 테스트: 기본 비활성 상태에서도 SUBSCRIBE/UNSUBSCRIBE 는 기존 SubscriptionTable 동작을 그대로 수행해야 한다.
        // lease table 만 비워 두어 idle expiry 기능이 꺼진 기본 BrokerServer 동작을 보존한다.
        [Fact]
        public void Subscribe_WhenOptionsAreDisabled_UpdatesSubscriptionWithoutLease()
        {
            SubscriptionTable table = new SubscriptionTable();
            ManualTimeProvider time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-22T00:00:00Z"));
            UdpRemoteLeaseTracker tracker = new UdpRemoteLeaseTracker(table, UdpLeaseOptions.Disabled, time);
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint remote = new IPEndPoint(IPAddress.Loopback, 20000);

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
            UdpRemoteLeaseTracker tracker = new UdpRemoteLeaseTracker(
                table,
                UdpLeaseOptions.CreateEnabled(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)),
                time);
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint remote = new IPEndPoint(IPAddress.Loopback, 20000);

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
            UdpRemoteLeaseTracker tracker = new UdpRemoteLeaseTracker(
                table,
                UdpLeaseOptions.CreateEnabled(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)),
                time);
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint remote = new IPEndPoint(IPAddress.Loopback, 20000);

            tracker.Subscribe("alpha", endpoint, remote);
            tracker.Subscribe("beta", endpoint, remote);

            tracker.Unsubscribe("alpha", endpoint, remote);
            Assert.Equal(1, tracker.LeaseCount);

            tracker.Unsubscribe("beta", endpoint, remote);
            Assert.Equal(0, tracker.LeaseCount);
        }

        // endpoint close cleanup 테스트: UDP socket 이 닫히면 같은 local endpoint 에 묶인 모든 remote lease 와 subscription 이 제거되어야 한다.
        // 다른 local endpoint 의 같은 remote address 는 별도 subscriber 이므로 보존한다.
        [Fact]
        public void RemoveEndpoint_WhenEndpointCloses_RemovesOnlyThatEndpointLeasesAndSubscriptions()
        {
            SubscriptionTable table = new SubscriptionTable();
            ManualTimeProvider time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-22T00:00:00Z"));
            UdpRemoteLeaseTracker tracker = new UdpRemoteLeaseTracker(
                table,
                UdpLeaseOptions.CreateEnabled(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)),
                time);
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            FakeUdpEndpoint survivorEndpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10001));
            EndPoint remote = new IPEndPoint(IPAddress.Loopback, 20000);

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
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter FullyQualifiedName~UdpRemoteLeaseTrackerTests
```

Expected: FAIL with compile errors that `UdpRemoteLeaseTracker` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/Hps.Broker/UdpRemoteLeaseTracker.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using Hps.Transport;

namespace Hps.Broker
{
    /// <summary>
    /// UDP remote subscription lease 를 Broker 계층에서 추적한다.
    ///
    /// tracker 는 SubscriptionTable 변경과 lease 변경을 같은 lock 안에서 직렬화한다. sweep 과 SUBSCRIBE 가 겹칠 때
    /// 새 구독이 만료 sweep 에 의해 제거되는 경합을 피하기 위해, UDP subscription 변경은 이 타입을 통해 수행한다.
    /// </summary>
    internal sealed class UdpRemoteLeaseTracker
    {
        private readonly object _gate;
        private readonly SubscriptionTable _subscriptions;
        private readonly UdpLeaseOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly Dictionary<UdpRemoteLeaseKey, UdpRemoteLease> _leases;

        internal UdpRemoteLeaseTracker(SubscriptionTable subscriptions, UdpLeaseOptions options, TimeProvider timeProvider)
        {
            if (subscriptions == null)
                throw new ArgumentNullException(nameof(subscriptions));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (timeProvider == null)
                throw new ArgumentNullException(nameof(timeProvider));

            _gate = new object();
            _subscriptions = subscriptions;
            _options = options;
            _timeProvider = timeProvider;
            _leases = new Dictionary<UdpRemoteLeaseKey, UdpRemoteLease>();
        }

        internal int LeaseCount
        {
            get
            {
                lock (_gate)
                {
                    return _leases.Count;
                }
            }
        }

        internal bool Subscribe(string topic, IUdpEndpoint endpoint, EndPoint remoteEndPoint)
        {
            ValidateTopic(topic);
            ValidateEndpoint(endpoint);
            ValidateRemoteEndPoint(remoteEndPoint);

            lock (_gate)
            {
                bool added = _subscriptions.Subscribe(topic, BrokerSubscriber.ForUdp(endpoint, remoteEndPoint));
                if (_options.Enabled)
                    GetOrCreateLease(endpoint, remoteEndPoint).MarkSubscribed(topic, _timeProvider.GetUtcNow());

                return added;
            }
        }

        internal bool Unsubscribe(string topic, IUdpEndpoint endpoint, EndPoint remoteEndPoint)
        {
            ValidateTopic(topic);
            ValidateEndpoint(endpoint);
            ValidateRemoteEndPoint(remoteEndPoint);

            lock (_gate)
            {
                bool removed = _subscriptions.Unsubscribe(topic, BrokerSubscriber.ForUdp(endpoint, remoteEndPoint));
                if (_options.Enabled && removed)
                {
                    UdpRemoteLeaseKey key = new UdpRemoteLeaseKey(endpoint, remoteEndPoint);
                    UdpRemoteLease? lease;
                    if (_leases.TryGetValue(key, out lease))
                    {
                        lease.MarkUnsubscribed(topic, _timeProvider.GetUtcNow());
                        if (lease.TopicCount == 0)
                            _leases.Remove(key);
                    }
                }

                return removed;
            }
        }

        internal void MarkPublishActivity(IUdpEndpoint endpoint, EndPoint remoteEndPoint)
        {
            ValidateEndpoint(endpoint);
            ValidateRemoteEndPoint(remoteEndPoint);

            if (!_options.Enabled)
                return;

            lock (_gate)
            {
                UdpRemoteLease? lease;
                if (_leases.TryGetValue(new UdpRemoteLeaseKey(endpoint, remoteEndPoint), out lease))
                    lease.Touch(_timeProvider.GetUtcNow());
            }
        }

        internal int RemoveEndpoint(IUdpEndpoint endpoint)
        {
            ValidateEndpoint(endpoint);

            lock (_gate)
            {
                if (_options.Enabled)
                {
                    List<UdpRemoteLeaseKey> removeKeys = new List<UdpRemoteLeaseKey>();
                    foreach (KeyValuePair<UdpRemoteLeaseKey, UdpRemoteLease> pair in _leases)
                    {
                        if (object.ReferenceEquals(pair.Key.Endpoint, endpoint))
                            removeKeys.Add(pair.Key);
                    }

                    for (int index = 0; index < removeKeys.Count; index++)
                        _leases.Remove(removeKeys[index]);
                }

                return _subscriptions.UnsubscribeAll(endpoint);
            }
        }

        private UdpRemoteLease GetOrCreateLease(IUdpEndpoint endpoint, EndPoint remoteEndPoint)
        {
            UdpRemoteLeaseKey key = new UdpRemoteLeaseKey(endpoint, remoteEndPoint);
            UdpRemoteLease? lease;
            if (!_leases.TryGetValue(key, out lease))
            {
                lease = new UdpRemoteLease();
                _leases.Add(key, lease);
            }

            return lease;
        }

        private static void ValidateTopic(string topic)
        {
            if (topic == null)
                throw new ArgumentNullException(nameof(topic));
            if (topic.Length == 0)
                throw new ArgumentException("Topic 은 비어 있을 수 없다.", nameof(topic));
        }

        private static void ValidateEndpoint(IUdpEndpoint endpoint)
        {
            if (endpoint == null)
                throw new ArgumentNullException(nameof(endpoint));
        }

        private static void ValidateRemoteEndPoint(EndPoint remoteEndPoint)
        {
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));
        }

        private readonly struct UdpRemoteLeaseKey : IEquatable<UdpRemoteLeaseKey>
        {
            internal UdpRemoteLeaseKey(IUdpEndpoint endpoint, EndPoint remoteEndPoint)
            {
                Endpoint = endpoint;
                RemoteEndPoint = remoteEndPoint;
            }

            internal IUdpEndpoint Endpoint { get; }

            internal EndPoint RemoteEndPoint { get; }

            public bool Equals(UdpRemoteLeaseKey other)
            {
                return object.ReferenceEquals(Endpoint, other.Endpoint)
                    && object.Equals(RemoteEndPoint, other.RemoteEndPoint);
            }

            public override bool Equals(object? obj)
            {
                if (obj is UdpRemoteLeaseKey)
                    return Equals((UdpRemoteLeaseKey)obj);

                return false;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (RuntimeHelpers.GetHashCode(Endpoint) * 397) ^ RemoteEndPoint.GetHashCode();
                }
            }
        }

        private sealed class UdpRemoteLease
        {
            private readonly HashSet<string> _topics;

            internal UdpRemoteLease()
            {
                _topics = new HashSet<string>(StringComparer.Ordinal);
            }

            internal DateTimeOffset LastSeen { get; private set; }

            internal int TopicCount
            {
                get { return _topics.Count; }
            }

            internal void MarkSubscribed(string topic, DateTimeOffset now)
            {
                _topics.Add(topic);
                LastSeen = now;
            }

            internal void MarkUnsubscribed(string topic, DateTimeOffset now)
            {
                _topics.Remove(topic);
                LastSeen = now;
            }

            internal void Touch(DateTimeOffset now)
            {
                LastSeen = now;
            }
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter FullyQualifiedName~UdpRemoteLeaseTrackerTests
```

Expected: PASS, 4 tests.

- [ ] **Step 5: Verify and commit**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
git diff --check
git status --short
git add src/Hps.Broker/UdpRemoteLeaseTracker.cs tests/Hps.Broker.Tests/UdpRemoteLeaseTrackerTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: track udp remote leases"
```

Expected: build warning 0/error 0, all tests pass, whitespace check passes.

---

### Task 3: 순수 sweep 메서드

**Files:**
- Modify: `src/Hps.Broker/UdpRemoteLeaseTracker.cs`
- Modify: `tests/Hps.Broker.Tests/UdpRemoteLeaseTrackerTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - `SubscriptionTable.UnsubscribeAll(IUdpEndpoint endpoint, EndPoint remoteEndPoint)`
  - `UdpLeaseOptions.IdleTimeout`
- Produces:
  - `internal int SweepExpired(DateTimeOffset now)`

- [ ] **Step 1: Write the failing tests**

Append these tests to `tests/Hps.Broker.Tests/UdpRemoteLeaseTrackerTests.cs`:

```csharp
        // sweep 만료 테스트: idle timeout 을 초과한 remote target 만 모든 topic 에서 제거되어야 한다.
        // sweep 은 topic entry 를 지우지 않고 SubscriptionTable 의 remote-wide primitive 만 사용한다.
        [Fact]
        public void SweepExpired_WhenLeaseIsOlderThanTimeout_RemovesRemoteFromEveryTopic()
        {
            SubscriptionTable table = new SubscriptionTable();
            ManualTimeProvider time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-22T00:00:00Z"));
            UdpRemoteLeaseTracker tracker = new UdpRemoteLeaseTracker(
                table,
                UdpLeaseOptions.CreateEnabled(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)),
                time);
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint expiredRemote = new IPEndPoint(IPAddress.Loopback, 20000);
            EndPoint survivorRemote = new IPEndPoint(IPAddress.Loopback, 20001);

            tracker.Subscribe("alpha", endpoint, expiredRemote);
            tracker.Subscribe("beta", endpoint, expiredRemote);
            tracker.Subscribe("alpha", endpoint, survivorRemote);
            time.Advance(TimeSpan.FromSeconds(31));

            int removed = tracker.SweepExpired(time.GetUtcNow());

            Assert.Equal(2, removed);
            Assert.False(table.IsSubscribed("alpha", BrokerSubscriber.ForUdp(endpoint, expiredRemote)));
            Assert.False(table.IsSubscribed("beta", BrokerSubscriber.ForUdp(endpoint, expiredRemote)));
            Assert.True(table.IsSubscribed("alpha", BrokerSubscriber.ForUdp(endpoint, survivorRemote)));
            Assert.Equal(1, tracker.LeaseCount);
        }

        // publish activity 갱신 테스트: 이미 lease 가 있는 remote 의 PUBLISH 는 last-seen 을 갱신해야 한다.
        // publisher-only remote lease 는 만들지 않으므로, 기존 subscriber activity 만 sweep 보존 신호로 취급한다.
        [Fact]
        public void SweepExpired_WhenPublishRefreshesExistingLease_KeepsRemote()
        {
            SubscriptionTable table = new SubscriptionTable();
            ManualTimeProvider time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-22T00:00:00Z"));
            UdpRemoteLeaseTracker tracker = new UdpRemoteLeaseTracker(
                table,
                UdpLeaseOptions.CreateEnabled(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)),
                time);
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint remote = new IPEndPoint(IPAddress.Loopback, 20000);

            tracker.Subscribe("alpha", endpoint, remote);
            time.Advance(TimeSpan.FromSeconds(20));
            tracker.MarkPublishActivity(endpoint, remote);
            time.Advance(TimeSpan.FromSeconds(20));

            int removed = tracker.SweepExpired(time.GetUtcNow());

            Assert.Equal(0, removed);
            Assert.True(table.IsSubscribed("alpha", BrokerSubscriber.ForUdp(endpoint, remote)));
            Assert.Equal(1, tracker.LeaseCount);
        }

        // disabled sweep 테스트: 기본 비활성 options 에서는 sweep 이 subscription 을 제거하지 않아야 한다.
        // 이는 D073의 "기본 BrokerServer 동작 유지" 계약을 sweep 경로에서도 고정한다.
        [Fact]
        public void SweepExpired_WhenOptionsAreDisabled_DoesNothing()
        {
            SubscriptionTable table = new SubscriptionTable();
            ManualTimeProvider time = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-22T00:00:00Z"));
            UdpRemoteLeaseTracker tracker = new UdpRemoteLeaseTracker(table, UdpLeaseOptions.Disabled, time);
            FakeUdpEndpoint endpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 10000));
            EndPoint remote = new IPEndPoint(IPAddress.Loopback, 20000);

            tracker.Subscribe("alpha", endpoint, remote);
            time.Advance(TimeSpan.FromHours(1));

            int removed = tracker.SweepExpired(time.GetUtcNow());

            Assert.Equal(0, removed);
            Assert.True(table.IsSubscribed("alpha", BrokerSubscriber.ForUdp(endpoint, remote)));
            Assert.Equal(0, tracker.LeaseCount);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter FullyQualifiedName~UdpRemoteLeaseTrackerTests
```

Expected: FAIL with compile errors that `SweepExpired(DateTimeOffset)` does not exist.

- [ ] **Step 3: Write minimal implementation**

Add to `UdpRemoteLeaseTracker`:

```csharp
        internal int SweepExpired(DateTimeOffset now)
        {
            if (!_options.Enabled)
                return 0;

            lock (_gate)
            {
                int removed = 0;
                List<UdpRemoteLeaseKey> expiredKeys = new List<UdpRemoteLeaseKey>();

                foreach (KeyValuePair<UdpRemoteLeaseKey, UdpRemoteLease> pair in _leases)
                {
                    if (now - pair.Value.LastSeen > _options.IdleTimeout)
                        expiredKeys.Add(pair.Key);
                }

                for (int index = 0; index < expiredKeys.Count; index++)
                {
                    UdpRemoteLeaseKey key = expiredKeys[index];
                    removed += _subscriptions.UnsubscribeAll(key.Endpoint, key.RemoteEndPoint);
                    _leases.Remove(key);
                }

                return removed;
            }
        }
```

Keep the call to `SubscriptionTable.UnsubscribeAll` inside the tracker lock. This intentionally serializes SUBSCRIBE/UNSUBSCRIBE activity through the same gate so a new subscription cannot be added between lease removal and routing-table cleanup.

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter FullyQualifiedName~UdpRemoteLeaseTrackerTests
```

Expected: PASS, 7 tests.

- [ ] **Step 5: Verify and commit**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
git diff --check
git status --short
git add src/Hps.Broker/UdpRemoteLeaseTracker.cs tests/Hps.Broker.Tests/UdpRemoteLeaseTrackerTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: sweep expired udp leases"
```

Expected: build warning 0/error 0, all tests pass, whitespace check passes.

---

### Task 4: BrokerUdpDatagramHandler wiring

**Files:**
- Modify: `src/Hps.Broker/BrokerUdpDatagramHandler.cs`
- Modify: `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - `UdpRemoteLeaseTracker.Subscribe`
  - `UdpRemoteLeaseTracker.Unsubscribe`
  - `UdpRemoteLeaseTracker.MarkPublishActivity`
  - `UdpRemoteLeaseTracker.RemoveEndpoint`
  - `UdpRemoteLeaseTracker.SweepExpired`
- Produces:
  - `internal BrokerUdpDatagramHandler(SubscriptionTable subscriptions, BrokerPublisher publisher, UdpLeaseOptions leaseOptions, TimeProvider timeProvider)`
  - `internal int SweepExpiredUdpLeases(DateTimeOffset now)`

- [ ] **Step 1: Write the failing tests**

Append these tests to `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`:

```csharp
        // handler sweep wiring 테스트: UDP SUBSCRIBE command 로 생성된 lease 가 만료되면
        // BrokerUdpDatagramHandler 의 sweep entry point 가 해당 remote subscription 을 제거해야 한다.
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

        // handler publish activity 테스트: lease 가 있는 UDP subscriber remote 가 PUBLISH 를 보내면
        // last-seen 이 갱신되어 같은 timeout 창 안의 sweep 에서 제거되지 않아야 한다.
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
```

Add helper overload and manual time provider inside `BrokerUdpDatagramHandlerTests`:

```csharp
        private static BrokerUdpDatagramHandler CreateHandler(
            SubscriptionTable subscriptions,
            FakeTransport transport,
            UdpLeaseOptions leaseOptions,
            TimeProvider timeProvider)
        {
            BrokerPublisher publisher = new BrokerPublisher(subscriptions, transport);
            return new BrokerUdpDatagramHandler(subscriptions, publisher, leaseOptions, timeProvider);
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
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter FullyQualifiedName~BrokerUdpDatagramHandlerTests
```

Expected: FAIL with compile errors that the new constructor and `SweepExpiredUdpLeases` do not exist.

- [ ] **Step 3: Write minimal implementation**

Modify `src/Hps.Broker/BrokerUdpDatagramHandler.cs`:

```csharp
        private readonly UdpRemoteLeaseTracker _udpLeases;

        public BrokerUdpDatagramHandler(SubscriptionTable subscriptions, BrokerPublisher publisher)
            : this(subscriptions, publisher, UdpLeaseOptions.Disabled, TimeProvider.System)
        {
        }

        internal BrokerUdpDatagramHandler(
            SubscriptionTable subscriptions,
            BrokerPublisher publisher,
            UdpLeaseOptions leaseOptions,
            TimeProvider timeProvider)
        {
            if (subscriptions == null)
                throw new ArgumentNullException(nameof(subscriptions));
            if (publisher == null)
                throw new ArgumentNullException(nameof(publisher));

            _subscriptions = subscriptions;
            _publisher = publisher;
            _udpLeases = new UdpRemoteLeaseTracker(subscriptions, leaseOptions, timeProvider);
        }
```

Replace the UDP command branches:

```csharp
                if (command.Kind == TcpCommandKind.Subscribe)
                {
                    string topic = DecodeTopic(command.Topic);
                    _udpLeases.Subscribe(topic, endpoint, remoteEndPoint);
                    return;
                }

                if (command.Kind == TcpCommandKind.Unsubscribe)
                {
                    string topic = DecodeTopic(command.Topic);
                    _udpLeases.Unsubscribe(topic, endpoint, remoteEndPoint);
                    return;
                }

                if (command.Kind == TcpCommandKind.Publish)
                {
                    string topic = DecodeTopic(command.Topic);
                    _udpLeases.MarkPublishActivity(endpoint, remoteEndPoint);
                    _publisher.Publish(topic, datagram, command.PayloadOffset, command.Payload.Length);
                    return;
                }
```

Replace endpoint close cleanup:

```csharp
            _udpLeases.RemoveEndpoint(endpoint);
```

Add sweep entry point:

```csharp
        internal int SweepExpiredUdpLeases(DateTimeOffset now)
        {
            return _udpLeases.SweepExpired(now);
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter FullyQualifiedName~BrokerUdpDatagramHandlerTests
```

Expected: PASS. Existing UDP handler tests must still pass with the public constructor because it uses disabled lease options.

- [ ] **Step 5: Verify and commit**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
git diff --check
git status --short
git add src/Hps.Broker/BrokerUdpDatagramHandler.cs tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: wire udp lease tracker"
```

Expected: build warning 0/error 0, all tests pass, whitespace check passes.

---

## Scope Left After This Plan

- `BrokerServer` 가 `TimeProvider.CreateTimer` 로 `SweepExpiredUdpLeases` 를 주기 호출하는 host timer 단위.
- `BrokerServer` 또는 별도 host 의 운영자용 public configuration surface.
- 기본 idle timeout 과 sweep interval 값 확정.
- stable subscriber identity 와 reconnect rebinding.
- pending UDP send queue 취소, reliable UDP, 순서 보장.

## Self-Review

- Spec coverage: D073의 owner, key, internal options, `TimeProvider`, pure sweep, `UnsubscribeAll(IUdpEndpoint, EndPoint)` 사용을 Task 1~4가 모두 다룬다. host timer 와 public settings 는 D073 범위 밖으로 명시했다.
- Placeholder scan: 이 문서는 미정 값을 요구하지 않는다. 기본 timeout 값과 public 설정 표면은 구현 대상이 아니라 후속 범위로 분리했다.
- Type consistency: `UdpLeaseOptions`, `UdpRemoteLeaseTracker`, `SweepExpired(DateTimeOffset)`, `SweepExpiredUdpLeases(DateTimeOffset)` 이름과 인자는 Task 간 일치한다.
