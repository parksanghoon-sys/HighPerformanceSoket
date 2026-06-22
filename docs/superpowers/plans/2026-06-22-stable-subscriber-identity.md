# Stable Subscriber Identity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `REGISTER <subscriber-id>` 기반 opt-in stable subscriber identity 를 추가해 reconnect 때 subscription metadata 를 새 runtime target 으로 재바인딩한다.

**Architecture:** 기본 v1 runtime target subscription 은 그대로 유지한다. stable identity 를 켠 경로에서만 Broker 계층에 `SubscriberRegistry`를 주입하고, registry 는 `SubscriberIdentity -> topic set + current BrokerSubscriber`를 관리한다. 실제 fan-out 은 계속 `SubscriptionTable`의 online `BrokerSubscriber` target 으로만 수행하므로 disconnected 기간의 payload buffering 은 만들지 않는다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, 기존 `Hps.Protocol` command decoder, `Hps.Broker` routing/handler, `Hps.Server` options/timer wiring.

## Global Constraints

- TFM 은 `net9.0`, LangVersion 은 C# 8.0 이며 file-scoped namespace, record, target-typed `new()` 를 쓰지 않는다.
- 모든 문서와 주석은 한국어로 작성한다. public API 에는 XML doc 으로 의도, 동시성 가정, 소유권을 적는다.
- 코드 변경은 Red-Green-Refactor 를 따른다. 테스트에는 무엇을 검증하는지 한국어 주석을 붙인다.
- 작업은 기능별 작은 단위로 나누고, 각 Task 는 별도 커밋으로 끝낸다.
- `EndpointId`는 stable routing key 로 승격하지 않는다. stable identity 는 Broker 계층의 별도 opt-in 모델이다.
- stable identity 는 subscription metadata 만 보존한다. disconnected 동안 publish payload 를 저장하거나 replay 하지 않는다.
- 기존 `SUBSCRIBE`/`UNSUBSCRIBE`/`PUBLISH` runtime target 경로는 `REGISTER`를 쓰지 않는 client 에서 그대로 동작해야 한다.
- 인증, 권한, TLS, configuration resolver, durable history, reliable delivery 는 이 계획 범위가 아니다.

---

## File Structure

- `src/Hps.Protocol/TcpCommandKind.cs`
  - `Register`, `Unregister` command kind 를 추가한다.
- `src/Hps.Protocol/TcpCommandDecoder.cs`
  - `REGISTER <subscriber-id>`와 `UNREGISTER <subscriber-id>`를 topic-only command 와 같은 token-only grammar 로 decode 한다.
- `tests/Hps.Protocol.Tests/TcpCommandDecoderTests.cs`
  - 새 command kind 계약과 malformed identity token 경계를 검증한다.
- `src/Hps.Broker/SubscriberIdentity.cs`
  - Broker stable identity token 값 타입이다. ASCII visible token, non-empty, no-space 를 검증한다.
- `src/Hps.Broker/SubscriberRegistrationResult.cs`
  - register 결과를 handler 가 protocol policy 로 바꿀 수 있게 하는 internal enum 이다.
- `src/Hps.Broker/SubscriberRegistry.cs`
  - identity topic set, current runtime target, target-to-identity map, rebind, disconnect retention state 를 관리한다.
- `tests/Hps.Broker.Tests/SubscriberIdentityTests.cs`
  - identity token validation 을 검증한다.
- `tests/Hps.Broker.Tests/SubscriberRegistryTests.cs`
  - pure registry rebind/disconnect/unregister 동작을 검증한다.
- `src/Hps.Broker/BrokerTcpFrameHandler.cs`
  - TCP `REGISTER`/`UNREGISTER`와 registered target 의 `SUBSCRIBE`/`UNSUBSCRIBE`를 registry 로 연결한다.
- `tests/Hps.Broker.Tests/BrokerTcpFrameHandlerTests.cs`
  - TCP reconnect rebinding 과 invalid duplicate registration close 정책을 검증한다.
- `src/Hps.Broker/UdpRemoteLeaseTracker.cs`
  - stable identity rebind 때 특정 remote lease 를 제거할 수 있는 helper 를 추가한다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`
  - UDP `REGISTER`/`UNREGISTER`와 registered remote 의 subscription/rebind 를 registry 와 lease tracker 로 연결한다.
- `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`
  - UDP remote rebind 와 invalid datagram drop 정책을 검증한다.
- `src/Hps.Server/BrokerServerOptions.cs`
  - stable identity opt-in 설정과 retention timeout 을 추가한다.
- `src/Hps.Server/BrokerServer.cs`
  - enabled options 일 때 shared `SubscriberRegistry`를 TCP/UDP handler 에 주입하고 retention sweep timer 를 소유한다.
- `tests/Hps.Server.Tests/BrokerServerOptionsTests.cs`
  - stable identity options validation 을 검증한다.
- `tests/Hps.Server.Tests/BrokerServerTests.cs`
  - Server host timer 생성/해제를 검증한다.
- Root state docs
  - `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, 필요 시 `DECISIONS.md`를 각 Task 완료마다 갱신한다.

---

### Task 1: Protocol REGISTER / UNREGISTER decode

**Files:**
- Modify: `src/Hps.Protocol/TcpCommandKind.cs`
- Modify: `src/Hps.Protocol/TcpCommandDecoder.cs`
- Modify: `tests/Hps.Protocol.Tests/TcpCommandDecoderTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Produces:
  - `TcpCommandKind.Register = 4`
  - `TcpCommandKind.Unregister = 5`
  - `TcpCommandDecoder.TryDecode(ReadOnlySpan<byte>, out TcpCommand, out TcpCommandDecodeError)`가 `REGISTER <subscriber-id>`와 `UNREGISTER <subscriber-id>`를 decode 한다.
  - Register/Unregister token 은 기존 `TcpCommand.Topic` span 으로 노출한다. 이름은 topic 이지만 이 단계에서는 command 의 단일 token argument 로 재사용한다.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Hps.Protocol.Tests/TcpCommandDecoderTests.cs`:

```csharp
        // REGISTER command 계약 테스트: stable subscriber identity 는 protocol command kind 로 분기되어야 한다.
        // enum 값이 없으면 handler 가 UNKNOWN command 로 닫는 기존 경로에서 벗어날 수 없다.
        [Fact]
        public void TcpCommandKind_Contract_ExposesRegisterCommands()
        {
            Assert.Contains("Register", Enum.GetNames(typeof(TcpCommandKind)));
            Assert.Contains("Unregister", Enum.GetNames(typeof(TcpCommandKind)));
        }

        // REGISTER decode 테스트: subscriber identity 는 frame buffer 안의 단일 token span 으로 반환된다.
        // Broker 계층이 장기 보관할 때만 string 으로 복사해 hot decode 경로의 불필요한 할당을 피한다.
        [Fact]
        public void TryDecode_WhenRegisterFrameContainsIdentity_ReturnsRegisterCommand()
        {
            TcpCommand command;
            TcpCommandDecodeError error;

            bool decoded = TcpCommandDecoder.TryDecode(Ascii("REGISTER device-a"), out command, out error);

            Assert.True(decoded);
            Assert.Equal(TcpCommandDecodeError.None, error);
            Assert.Equal(TcpCommandKind.Register, command.Kind);
            Assert.Equal("device-a", AsString(command.Topic));
            Assert.True(command.Payload.IsEmpty);
        }

        // UNREGISTER decode 테스트: identity registry 에서 명시 제거할 id 도 REGISTER 와 같은 token-only 문법을 사용한다.
        // payload 가 생기면 publish command 와 경계가 섞이므로 단일 token 만 허용한다.
        [Fact]
        public void TryDecode_WhenUnregisterFrameContainsIdentity_ReturnsUnregisterCommand()
        {
            TcpCommand command;
            TcpCommandDecodeError error;

            bool decoded = TcpCommandDecoder.TryDecode(Ascii("UNREGISTER device-a"), out command, out error);

            Assert.True(decoded);
            Assert.Equal(TcpCommandDecodeError.None, error);
            Assert.Equal(TcpCommandKind.Unregister, command.Kind);
            Assert.Equal("device-a", AsString(command.Topic));
            Assert.True(command.Payload.IsEmpty);
        }
```

Extend the malformed theory:

```csharp
        [InlineData("REGISTER", TcpCommandDecodeError.MissingTopic)]
        [InlineData("REGISTER ", TcpCommandDecodeError.MissingTopic)]
        [InlineData("REGISTER device-a extra", TcpCommandDecodeError.InvalidTopic)]
        [InlineData("UNREGISTER", TcpCommandDecodeError.MissingTopic)]
        [InlineData("UNREGISTER ", TcpCommandDecodeError.MissingTopic)]
        [InlineData("UNREGISTER device-a extra", TcpCommandDecodeError.InvalidTopic)]
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Protocol.Tests\Hps.Protocol.Tests.csproj --filter FullyQualifiedName~TcpCommandDecoderTests
```

Expected: FAIL with assertion failures for missing enum names and decode false for REGISTER/UNREGISTER.

- [ ] **Step 3: Write minimal implementation**

Modify `src/Hps.Protocol/TcpCommandKind.cs`:

```csharp
        /// <summary>
        /// stable subscriber identity 를 현재 runtime target 에 등록한다.
        /// </summary>
        Register = 4,

        /// <summary>
        /// stable subscriber identity 등록과 보존된 subscription metadata 를 제거한다.
        /// </summary>
        Unregister = 5
```

Modify `TcpCommandDecoder.TryDecode` command branch:

```csharp
            if (IsRegisterCommand(commandName))
                return TryDecodeTopicOnlyCommand(commandBody, TcpCommandKind.Register, out command, out error);

            if (IsUnregisterCommand(commandName))
                return TryDecodeTopicOnlyCommand(commandBody, TcpCommandKind.Unregister, out command, out error);
```

Add byte comparisons:

```csharp
        private static bool IsRegisterCommand(ReadOnlySpan<byte> commandName)
        {
            return commandName.Length == 8
                && commandName[0] == (byte)'R'
                && commandName[1] == (byte)'E'
                && commandName[2] == (byte)'G'
                && commandName[3] == (byte)'I'
                && commandName[4] == (byte)'S'
                && commandName[5] == (byte)'T'
                && commandName[6] == (byte)'E'
                && commandName[7] == (byte)'R';
        }

        private static bool IsUnregisterCommand(ReadOnlySpan<byte> commandName)
        {
            return commandName.Length == 10
                && commandName[0] == (byte)'U'
                && commandName[1] == (byte)'N'
                && commandName[2] == (byte)'R'
                && commandName[3] == (byte)'E'
                && commandName[4] == (byte)'G'
                && commandName[5] == (byte)'I'
                && commandName[6] == (byte)'S'
                && commandName[7] == (byte)'T'
                && commandName[8] == (byte)'E'
                && commandName[9] == (byte)'R';
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test tests\Hps.Protocol.Tests\Hps.Protocol.Tests.csproj --filter FullyQualifiedName~TcpCommandDecoderTests
```

Expected: PASS, all `TcpCommandDecoderTests` pass.

- [ ] **Step 5: Verify and commit**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
git diff --check
git status --short
git add src/Hps.Protocol/TcpCommandKind.cs src/Hps.Protocol/TcpCommandDecoder.cs tests/Hps.Protocol.Tests/TcpCommandDecoderTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: decode subscriber identity commands"
```

Expected: build warning 0/error 0, all tests pass, whitespace check passes.

---

### Task 2: SubscriberIdentity and SubscriberRegistry pure model

**Files:**
- Create: `src/Hps.Broker/SubscriberIdentity.cs`
- Create: `src/Hps.Broker/SubscriberRegistrationResult.cs`
- Create: `src/Hps.Broker/SubscriberRegistry.cs`
- Create: `tests/Hps.Broker.Tests/SubscriberIdentityTests.cs`
- Create: `tests/Hps.Broker.Tests/SubscriberRegistryTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Produces:
  - `internal readonly struct SubscriberIdentity : IEquatable<SubscriberIdentity>`
  - `internal static SubscriberIdentity Create(string value)`
  - `internal string Value { get; }`
  - `internal enum SubscriberRegistrationResult`
  - `internal sealed class SubscriberRegistry`
  - `internal SubscriberRegistrationResult Register(SubscriberIdentity identity, BrokerSubscriber target, out BrokerSubscriber? replacedTarget, out string[] reboundTopics)`
  - `internal int Unregister(SubscriberIdentity identity, BrokerSubscriber target)`
  - `internal bool Subscribe(string topic, BrokerSubscriber target)`
  - `internal bool Unsubscribe(string topic, BrokerSubscriber target)`
  - `internal int RemoveTarget(BrokerSubscriber target, DateTimeOffset now)`
  - `internal int RemoveUdpEndpoint(IUdpEndpoint endpoint, DateTimeOffset now)`
  - `internal int SweepDisconnected(DateTimeOffset now, TimeSpan retentionTimeout)`
  - `internal int IdentityCount { get; }`

- [ ] **Step 1: Write the failing tests**

Create `tests/Hps.Broker.Tests/SubscriberIdentityTests.cs`:

```csharp
using System;
using Xunit;

namespace Hps.Broker.Tests
{
    public sealed class SubscriberIdentityTests
    {
        // identity token 검증 테스트: registry key 는 장기 보관되는 문자열이므로 빈 값과 공백 포함 값을 거부해야 한다.
        // 공백을 허용하면 REGISTER command 의 token 경계와 routing identity 경계가 서로 달라진다.
        [Theory]
        [InlineData("")]
        [InlineData("device a")]
        [InlineData("device\t-a")]
        public void Create_WhenTokenIsInvalid_Throws(string value)
        {
            Assert.Throws<ArgumentException>(delegate { SubscriberIdentity.Create(value); });
        }

        // identity equality 테스트: 같은 ASCII token 은 같은 logical subscriber 로 취급해야 reconnect rebinding 이 동작한다.
        [Fact]
        public void Equals_WhenTokenMatches_ReturnsTrue()
        {
            SubscriberIdentity first = SubscriberIdentity.Create("device-a");
            SubscriberIdentity second = SubscriberIdentity.Create("device-a");

            Assert.Equal(first, second);
            Assert.Equal(first.GetHashCode(), second.GetHashCode());
            Assert.Equal("device-a", first.Value);
        }
    }
}
```

Create `tests/Hps.Broker.Tests/SubscriberRegistryTests.cs`:

```csharp
using System;
using Xunit;

namespace Hps.Broker.Tests
{
    public sealed class SubscriberRegistryTests
    {
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
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter "FullyQualifiedName~SubscriberIdentityTests|FullyQualifiedName~SubscriberRegistryTests"
```

Expected: FAIL with compile errors that `SubscriberIdentity`, `SubscriberRegistry`, and `SubscriberRegistrationResult` do not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/Hps.Broker/SubscriberRegistrationResult.cs`:

```csharp
namespace Hps.Broker
{
    /// <summary>
    /// stable subscriber REGISTER 처리 결과이다.
    /// </summary>
    internal enum SubscriberRegistrationResult
    {
        Registered = 1,
        AlreadyRegistered = 2,
        Rebound = 3,
        TargetAlreadyRegisteredWithDifferentIdentity = 4
    }
}
```

Create `src/Hps.Broker/SubscriberIdentity.cs`:

```csharp
using System;

namespace Hps.Broker
{
    /// <summary>
    /// Broker 계층에서 사용하는 stable logical subscriber id 이다.
    /// </summary>
    internal readonly struct SubscriberIdentity : IEquatable<SubscriberIdentity>
    {
        private SubscriberIdentity(string value)
        {
            Value = value;
        }

        internal string Value { get; }

        internal static SubscriberIdentity Create(string value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (value.Length == 0)
                throw new ArgumentException("Subscriber identity 는 비어 있을 수 없다.", nameof(value));

            for (int index = 0; index < value.Length; index++)
            {
                if (char.IsWhiteSpace(value[index]))
                    throw new ArgumentException("Subscriber identity 는 공백 문자를 포함할 수 없다.", nameof(value));
            }

            return new SubscriberIdentity(value);
        }

        public bool Equals(SubscriberIdentity other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            if (obj is SubscriberIdentity)
                return Equals((SubscriberIdentity)obj);

            return false;
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }
    }
}
```

Create `src/Hps.Broker/SubscriberRegistry.cs` with these members and behavior:

```csharp
using System;
using System.Collections.Generic;
using Hps.Transport;

namespace Hps.Broker
{
    /// <summary>
    /// stable subscriber identity 와 현재 runtime fan-out target 을 연결한다.
    /// </summary>
    internal sealed class SubscriberRegistry
    {
        private readonly object _gate;
        private readonly SubscriptionTable _subscriptions;
        private readonly Dictionary<SubscriberIdentity, Entry> _entries;
        private readonly Dictionary<BrokerSubscriber, SubscriberIdentity> _targetToIdentity;

        internal SubscriberRegistry(SubscriptionTable subscriptions)
        {
            if (subscriptions == null)
                throw new ArgumentNullException(nameof(subscriptions));

            _gate = new object();
            _subscriptions = subscriptions;
            _entries = new Dictionary<SubscriberIdentity, Entry>();
            _targetToIdentity = new Dictionary<BrokerSubscriber, SubscriberIdentity>();
        }

        internal int IdentityCount
        {
            get
            {
                lock (_gate)
                {
                    return _entries.Count;
                }
            }
        }

        internal SubscriberRegistrationResult Register(
            SubscriberIdentity identity,
            BrokerSubscriber target,
            out BrokerSubscriber? replacedTarget,
            out string[] reboundTopics)
        {
            ValidateTarget(target);
            replacedTarget = null;
            reboundTopics = Array.Empty<string>();

            lock (_gate)
            {
                SubscriberIdentity existingIdentity;
                if (_targetToIdentity.TryGetValue(target, out existingIdentity) && !existingIdentity.Equals(identity))
                    return SubscriberRegistrationResult.TargetAlreadyRegisteredWithDifferentIdentity;

                Entry? entry;
                if (!_entries.TryGetValue(identity, out entry))
                {
                    entry = new Entry();
                    _entries.Add(identity, entry);
                }

                if (entry.CurrentTarget.HasValue && entry.CurrentTarget.Value.Equals(target))
                {
                    reboundTopics = entry.CopyTopics();
                    return SubscriberRegistrationResult.AlreadyRegistered;
                }

                if (entry.CurrentTarget.HasValue)
                {
                    BrokerSubscriber oldTarget = entry.CurrentTarget.Value;
                    replacedTarget = oldTarget;
                    RemoveCurrentTargetFromTopics(entry, oldTarget);
                    _targetToIdentity.Remove(oldTarget);
                }

                entry.CurrentTarget = target;
                entry.LastDisconnectedAt = null;
                _targetToIdentity[target] = identity;
                AddCurrentTargetToTopics(entry, target);
                reboundTopics = entry.CopyTopics();

                return replacedTarget.HasValue ? SubscriberRegistrationResult.Rebound : SubscriberRegistrationResult.Registered;
            }
        }

        internal bool Subscribe(string topic, BrokerSubscriber target)
        {
            lock (_gate)
            {
                SubscriberIdentity identity;
                if (!_targetToIdentity.TryGetValue(target, out identity))
                    return _subscriptions.Subscribe(topic, target);

                Entry entry = _entries[identity];
                entry.Topics.Add(topic);
                if (entry.CurrentTarget.HasValue)
                    return _subscriptions.Subscribe(topic, entry.CurrentTarget.Value);

                return false;
            }
        }

        internal bool Unsubscribe(string topic, BrokerSubscriber target)
        {
            lock (_gate)
            {
                SubscriberIdentity identity;
                if (!_targetToIdentity.TryGetValue(target, out identity))
                    return _subscriptions.Unsubscribe(topic, target);

                Entry entry = _entries[identity];
                entry.Topics.Remove(topic);
                if (entry.CurrentTarget.HasValue)
                    return _subscriptions.Unsubscribe(topic, entry.CurrentTarget.Value);

                return false;
            }
        }

        internal int RemoveTarget(BrokerSubscriber target, DateTimeOffset now)
        {
            lock (_gate)
            {
                return RemoveTargetCore(target, now);
            }
        }

        internal int RemoveUdpEndpoint(IUdpEndpoint endpoint, DateTimeOffset now)
        {
            if (endpoint == null)
                throw new ArgumentNullException(nameof(endpoint));

            lock (_gate)
            {
                int removed = 0;
                List<BrokerSubscriber> targets = new List<BrokerSubscriber>();
                foreach (KeyValuePair<BrokerSubscriber, SubscriberIdentity> pair in _targetToIdentity)
                {
                    BrokerSubscriber target = pair.Key;
                    if (target.TransportKind == EndpointTransportKind.Udp
                        && object.ReferenceEquals(target.UdpEndpoint, endpoint))
                    {
                        targets.Add(target);
                    }
                }

                for (int index = 0; index < targets.Count; index++)
                    removed += RemoveTargetCore(targets[index], now);

                return removed;
            }
        }

        private int RemoveTargetCore(BrokerSubscriber target, DateTimeOffset now)
        {
            SubscriberIdentity identity;
            if (!_targetToIdentity.TryGetValue(target, out identity))
                return RemoveRuntimeTarget(target);

            Entry entry = _entries[identity];
            int removed = RemoveCurrentTargetFromTopics(entry, target);
            entry.CurrentTarget = null;
            entry.LastDisconnectedAt = now;
            _targetToIdentity.Remove(target);
            return removed;
        }

        internal int Unregister(SubscriberIdentity identity, BrokerSubscriber target)
        {
            lock (_gate)
            {
                Entry? entry;
                if (!_entries.TryGetValue(identity, out entry))
                    return 0;
                if (!entry.CurrentTarget.HasValue || !entry.CurrentTarget.Value.Equals(target))
                    return 0;

                int removed = RemoveCurrentTargetFromTopics(entry, target);
                _targetToIdentity.Remove(target);
                _entries.Remove(identity);
                return removed;
            }
        }

        internal int SweepDisconnected(DateTimeOffset now, TimeSpan retentionTimeout)
        {
            lock (_gate)
            {
                int removed = 0;
                List<SubscriberIdentity> expired = new List<SubscriberIdentity>();
                foreach (KeyValuePair<SubscriberIdentity, Entry> pair in _entries)
                {
                    if (!pair.Value.CurrentTarget.HasValue
                        && pair.Value.LastDisconnectedAt.HasValue
                        && now - pair.Value.LastDisconnectedAt.Value > retentionTimeout)
                    {
                        expired.Add(pair.Key);
                    }
                }

                for (int index = 0; index < expired.Count; index++)
                {
                    _entries.Remove(expired[index]);
                    removed++;
                }

                return removed;
            }
        }

        private int RemoveCurrentTargetFromTopics(Entry entry, BrokerSubscriber target)
        {
            int removed = 0;
            foreach (string topic in entry.Topics)
            {
                if (_subscriptions.Unsubscribe(topic, target))
                    removed++;
            }

            return removed;
        }

        private void AddCurrentTargetToTopics(Entry entry, BrokerSubscriber target)
        {
            foreach (string topic in entry.Topics)
                _subscriptions.Subscribe(topic, target);
        }

        private int RemoveRuntimeTarget(BrokerSubscriber target)
        {
            if (target.TransportKind == EndpointTransportKind.Tcp)
                return _subscriptions.UnsubscribeAll(target.TcpConnection);

            if (target.TransportKind == EndpointTransportKind.Udp)
                return _subscriptions.UnsubscribeAll(target.UdpEndpoint, target.UdpRemoteEndPoint);

            return 0;
        }

        private static void ValidateTarget(BrokerSubscriber target)
        {
            if (!target.IsValid)
                throw new ArgumentException("Stable subscriber target 은 유효한 BrokerSubscriber 여야 한다.", nameof(target));
        }

        private sealed class Entry
        {
            internal readonly HashSet<string> Topics = new HashSet<string>(StringComparer.Ordinal);
            internal BrokerSubscriber? CurrentTarget;
            internal DateTimeOffset? LastDisconnectedAt;

            internal string[] CopyTopics()
            {
                string[] topics = new string[Topics.Count];
                Topics.CopyTo(topics);
                return topics;
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:

```powershell
dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter "FullyQualifiedName~SubscriberIdentityTests|FullyQualifiedName~SubscriberRegistryTests"
```

Expected: PASS for new identity/registry tests.

- [ ] **Step 5: Verify and commit**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
git diff --check
git status --short
git add src/Hps.Broker/SubscriberIdentity.cs src/Hps.Broker/SubscriberRegistrationResult.cs src/Hps.Broker/SubscriberRegistry.cs tests/Hps.Broker.Tests/SubscriberIdentityTests.cs tests/Hps.Broker.Tests/SubscriberRegistryTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: add subscriber identity registry"
```

Expected: build warning 0/error 0, all tests pass, whitespace check passes.

---

### Task 3: TCP handler stable identity integration

**Files:**
- Modify: `src/Hps.Broker/BrokerTcpFrameHandler.cs`
- Modify: `tests/Hps.Broker.Tests/BrokerTcpFrameHandlerTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - `SubscriberIdentity.Create(string)`
  - `SubscriberRegistry.Register(...)`
  - `SubscriberRegistry.Subscribe(...)`
  - `SubscriberRegistry.Unsubscribe(...)`
  - `SubscriberRegistry.Unregister(...)`
  - `SubscriberRegistry.RemoveTarget(...)`
- Produces:
  - `internal BrokerTcpFrameHandler(SubscriptionTable subscriptions, BrokerPublisher publisher, SubscriberRegistry? subscriberRegistry, TimeProvider timeProvider)`
  - TCP `REGISTER` command handling
  - TCP `UNREGISTER` command handling

- [ ] **Step 1: Write the failing tests**

Append to `tests/Hps.Broker.Tests/BrokerTcpFrameHandlerTests.cs`:

```csharp
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
```

Add helper overload:

```csharp
        private static BrokerTcpFrameHandler CreateHandler(
            SubscriptionTable subscriptions,
            FakeTransport transport,
            SubscriberRegistry registry)
        {
            BrokerPublisher publisher = new BrokerPublisher(subscriptions, transport);
            return new BrokerTcpFrameHandler(subscriptions, publisher, registry, TimeProvider.System);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter FullyQualifiedName~BrokerTcpFrameHandlerTests
```

Expected: FAIL with compile error that the internal handler constructor does not exist, or assertion failures for missing REGISTER behavior.

- [ ] **Step 3: Write minimal implementation**

Modify `BrokerTcpFrameHandler`:

```csharp
        private readonly SubscriberRegistry? _subscriberRegistry;
        private readonly TimeProvider _timeProvider;

        public BrokerTcpFrameHandler(SubscriptionTable subscriptions, BrokerPublisher publisher)
            : this(subscriptions, publisher, null, TimeProvider.System)
        {
        }

        internal BrokerTcpFrameHandler(
            SubscriptionTable subscriptions,
            BrokerPublisher publisher,
            SubscriberRegistry? subscriberRegistry,
            TimeProvider timeProvider)
        {
            if (subscriptions == null)
                throw new ArgumentNullException(nameof(subscriptions));
            if (publisher == null)
                throw new ArgumentNullException(nameof(publisher));
            if (timeProvider == null)
                throw new ArgumentNullException(nameof(timeProvider));

            _subscriptions = subscriptions;
            _publisher = publisher;
            _subscriberRegistry = subscriberRegistry;
            _timeProvider = timeProvider;
        }
```

Inside `OnFrame`, create `BrokerSubscriber target = BrokerSubscriber.ForTcp(connection);` after validation, then handle command branches:

```csharp
                if (command.Kind == TcpCommandKind.Register)
                {
                    closeConnection = !RegisterTcpTarget(connection, target, DecodeTopic(command.Topic));
                    return;
                }

                if (command.Kind == TcpCommandKind.Unregister)
                {
                    if (_subscriberRegistry != null)
                        _subscriberRegistry.Unregister(SubscriberIdentity.Create(DecodeTopic(command.Topic)), target);
                    return;
                }

                if (command.Kind == TcpCommandKind.Subscribe)
                {
                    string topic = DecodeTopic(command.Topic);
                    if (_subscriberRegistry != null)
                        _subscriberRegistry.Subscribe(topic, target);
                    else
                        _subscriptions.Subscribe(topic, connection);
                    return;
                }

                if (command.Kind == TcpCommandKind.Unsubscribe)
                {
                    string topic = DecodeTopic(command.Topic);
                    if (_subscriberRegistry != null)
                        _subscriberRegistry.Unsubscribe(topic, target);
                    else
                        _subscriptions.Unsubscribe(topic, connection);
                    return;
                }
```

Add helper:

```csharp
        private bool RegisterTcpTarget(IConnection connection, BrokerSubscriber target, string identityValue)
        {
            if (_subscriberRegistry == null)
                return true;

            SubscriberRegistrationResult result = _subscriberRegistry.Register(
                SubscriberIdentity.Create(identityValue),
                target,
                out BrokerSubscriber? replacedTarget,
                out _);

            if (result == SubscriberRegistrationResult.TargetAlreadyRegisteredWithDifferentIdentity)
                return false;

            if (replacedTarget.HasValue
                && replacedTarget.Value.TransportKind == EndpointTransportKind.Tcp
                && !object.ReferenceEquals(replacedTarget.Value.TcpConnection, connection))
            {
                replacedTarget.Value.TcpConnection.Close();
            }

            return true;
        }
```

Modify `OnConnectionClosed`:

```csharp
            if (_subscriberRegistry != null)
                _subscriberRegistry.RemoveTarget(BrokerSubscriber.ForTcp(connection), _timeProvider.GetUtcNow());
            else
                _subscriptions.UnsubscribeAll(connection);
```

- [ ] **Step 4: Run tests to verify they pass**

Run:

```powershell
dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter FullyQualifiedName~BrokerTcpFrameHandlerTests
```

Expected: PASS for all TCP handler tests. Existing public constructor tests still pass.

- [ ] **Step 5: Verify and commit**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
git diff --check
git status --short
git add src/Hps.Broker/BrokerTcpFrameHandler.cs tests/Hps.Broker.Tests/BrokerTcpFrameHandlerTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: wire tcp subscriber identity"
```

Expected: build warning 0/error 0, all tests pass, whitespace check passes.

---

### Task 4: UDP handler stable identity integration

**Files:**
- Modify: `src/Hps.Broker/UdpRemoteLeaseTracker.cs`
- Modify: `src/Hps.Broker/BrokerUdpDatagramHandler.cs`
- Modify: `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - `SubscriberRegistry.Register(...)`
  - `SubscriberRegistry.Subscribe(...)`
  - `SubscriberRegistry.Unsubscribe(...)`
  - `SubscriberRegistry.Unregister(...)`
  - `SubscriberRegistry.RemoveTarget(...)`
  - `SubscriberRegistry.RemoveUdpEndpoint(...)`
- Produces:
  - `internal BrokerUdpDatagramHandler(SubscriptionTable subscriptions, BrokerPublisher publisher, UdpLeaseOptions leaseOptions, TimeProvider timeProvider, SubscriberRegistry? subscriberRegistry)`
  - `internal int UdpRemoteLeaseTracker.RemoveRemote(IUdpEndpoint endpoint, EndPoint remoteEndPoint)`
  - `internal void UdpRemoteLeaseTracker.MarkSubscribedTopics(IUdpEndpoint endpoint, EndPoint remoteEndPoint, string[] topics)`

- [ ] **Step 1: Write the failing tests**

Append to `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`:

```csharp
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
```

Add helper overload:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter FullyQualifiedName~BrokerUdpDatagramHandlerTests
```

Expected: FAIL with compile error that the new UDP handler constructor does not exist.

- [ ] **Step 3: Write minimal implementation**

Add to `UdpRemoteLeaseTracker`:

```csharp
        internal void MarkSubscribedTopics(IUdpEndpoint endpoint, EndPoint remoteEndPoint, string[] topics)
        {
            ValidateEndpoint(endpoint);
            ValidateRemoteEndPoint(remoteEndPoint);
            if (topics == null)
                throw new ArgumentNullException(nameof(topics));
            if (!_options.Enabled)
                return;

            lock (_gate)
            {
                UdpRemoteLease lease = GetOrCreateLease(endpoint, remoteEndPoint);
                DateTimeOffset now = _timeProvider.GetUtcNow();
                for (int index = 0; index < topics.Length; index++)
                    lease.MarkSubscribed(topics[index], now);
            }
        }

        internal int RemoveRemote(IUdpEndpoint endpoint, EndPoint remoteEndPoint)
        {
            ValidateEndpoint(endpoint);
            ValidateRemoteEndPoint(remoteEndPoint);

            lock (_gate)
            {
                UdpRemoteLeaseKey key = new UdpRemoteLeaseKey(endpoint, remoteEndPoint);
                _leases.Remove(key);
                return _subscriptions.UnsubscribeAll(endpoint, remoteEndPoint);
            }
        }
```

Modify `BrokerUdpDatagramHandler` constructor chain:

```csharp
        private readonly SubscriberRegistry? _subscriberRegistry;
        private readonly TimeProvider _timeProvider;

        public BrokerUdpDatagramHandler(SubscriptionTable subscriptions, BrokerPublisher publisher)
            : this(subscriptions, publisher, UdpLeaseOptions.Disabled, TimeProvider.System, null)
        {
        }

        internal BrokerUdpDatagramHandler(
            SubscriptionTable subscriptions,
            BrokerPublisher publisher,
            UdpLeaseOptions leaseOptions,
            TimeProvider timeProvider)
            : this(subscriptions, publisher, leaseOptions, timeProvider, null)
        {
        }

        internal BrokerUdpDatagramHandler(
            SubscriptionTable subscriptions,
            BrokerPublisher publisher,
            UdpLeaseOptions leaseOptions,
            TimeProvider timeProvider,
            SubscriberRegistry? subscriberRegistry)
        {
            if (subscriptions == null)
                throw new ArgumentNullException(nameof(subscriptions));
            if (publisher == null)
                throw new ArgumentNullException(nameof(publisher));
            if (leaseOptions == null)
                throw new ArgumentNullException(nameof(leaseOptions));
            if (timeProvider == null)
                throw new ArgumentNullException(nameof(timeProvider));

            _subscriptions = subscriptions;
            _publisher = publisher;
            _timeProvider = timeProvider;
            _subscriberRegistry = subscriberRegistry;
            _udpLeases = new UdpRemoteLeaseTracker(subscriptions, leaseOptions, timeProvider);
        }
```

Handle `Register` and stable subscription branches in `OnDatagramReceived`:

```csharp
                BrokerSubscriber target = BrokerSubscriber.ForUdp(endpoint, remoteEndPoint);

                if (command.Kind == TcpCommandKind.Register)
                {
                    RegisterUdpTarget(target, DecodeTopic(command.Topic));
                    return;
                }

                if (command.Kind == TcpCommandKind.Unregister)
                {
                    if (_subscriberRegistry != null)
                        _subscriberRegistry.Unregister(SubscriberIdentity.Create(DecodeTopic(command.Topic)), target);
                    _udpLeases.RemoveRemote(endpoint, remoteEndPoint);
                    return;
                }

                if (command.Kind == TcpCommandKind.Subscribe)
                {
                    string topic = DecodeTopic(command.Topic);
                    _udpLeases.Subscribe(topic, endpoint, remoteEndPoint);
                    if (_subscriberRegistry != null)
                        _subscriberRegistry.Subscribe(topic, target);
                    return;
                }

                if (command.Kind == TcpCommandKind.Unsubscribe)
                {
                    string topic = DecodeTopic(command.Topic);
                    _udpLeases.Unsubscribe(topic, endpoint, remoteEndPoint);
                    if (_subscriberRegistry != null)
                        _subscriberRegistry.Unsubscribe(topic, target);
                    return;
                }
```

Add helper:

```csharp
        private void RegisterUdpTarget(BrokerSubscriber target, string identityValue)
        {
            if (_subscriberRegistry == null)
                return;

            SubscriberRegistrationResult result = _subscriberRegistry.Register(
                SubscriberIdentity.Create(identityValue),
                target,
                out BrokerSubscriber? replacedTarget,
                out string[] reboundTopics);

            if (result == SubscriberRegistrationResult.TargetAlreadyRegisteredWithDifferentIdentity)
                return;

            if (replacedTarget.HasValue && replacedTarget.Value.TransportKind == EndpointTransportKind.Udp)
                _udpLeases.RemoveRemote(replacedTarget.Value.UdpEndpoint, replacedTarget.Value.UdpRemoteEndPoint);

            if (target.TransportKind == EndpointTransportKind.Udp && reboundTopics.Length > 0)
                _udpLeases.MarkSubscribedTopics(target.UdpEndpoint, target.UdpRemoteEndPoint, reboundTopics);
        }
```

Modify endpoint close cleanup:

```csharp
            if (_subscriberRegistry != null)
                _subscriberRegistry.RemoveUdpEndpoint(endpoint, _timeProvider.GetUtcNow());
            _udpLeases.RemoveEndpoint(endpoint);
```

Do not close the UDP endpoint on invalid duplicate registration. UDP protocol error policy remains datagram-drop only.

- [ ] **Step 4: Run tests to verify they pass**

Run:

```powershell
dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter FullyQualifiedName~BrokerUdpDatagramHandlerTests
```

Expected: PASS for all UDP handler tests.

- [ ] **Step 5: Verify and commit**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
git diff --check
git status --short
git add src/Hps.Broker/UdpRemoteLeaseTracker.cs src/Hps.Broker/BrokerUdpDatagramHandler.cs tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: wire udp subscriber identity"
```

Expected: build warning 0/error 0, all tests pass, whitespace check passes.

---

### Task 5: BrokerServer opt-in settings and retention timer

**Files:**
- Modify: `src/Hps.Server/BrokerServerOptions.cs`
- Modify: `src/Hps.Server/BrokerServer.cs`
- Modify: `tests/Hps.Server.Tests/BrokerServerOptionsTests.cs`
- Modify: `tests/Hps.Server.Tests/BrokerServerTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - `SubscriberRegistry`
  - `SubscriberRegistry.SweepDisconnected(DateTimeOffset now, TimeSpan retentionTimeout)`
  - TCP/UDP handler internal constructors that accept `SubscriberRegistry?`
- Produces:
  - `public bool StableSubscriberIdentityEnabled { get; }`
  - `public TimeSpan StableSubscriberRetentionTimeout { get; }`
  - `public static BrokerServerOptions CreateWithStableSubscriberIdentity(TimeSpan retentionTimeout, TimeProvider? timeProvider)`
  - `public BrokerServerOptions WithStableSubscriberIdentity(TimeSpan retentionTimeout)`

- [ ] **Step 1: Write the failing tests**

Append to `tests/Hps.Server.Tests/BrokerServerOptionsTests.cs`:

```csharp
        // stable identity 기본 옵션 테스트: 기존 BrokerServerOptions.Default 는 runtime target subscription 만 사용해야 한다.
        // 기본값이 enabled 로 바뀌면 기존 client 가 REGISTER 없이도 registry 경로를 타는 회귀가 생긴다.
        [Fact]
        public void Default_WhenRead_DisablesStableSubscriberIdentity()
        {
            BrokerServerOptions options = BrokerServerOptions.Default;

            Assert.False(options.StableSubscriberIdentityEnabled);
            Assert.Equal(TimeSpan.Zero, options.StableSubscriberRetentionTimeout);
        }

        // stable identity 옵션 검증 테스트: disconnected identity retention 은 registry memory lifetime 을 결정한다.
        // 0 이하 값을 허용하면 즉시 삭제 또는 timer busy loop 정책으로 흐를 수 있으므로 명시적으로 거부한다.
        [Fact]
        public void CreateWithStableSubscriberIdentity_WhenRetentionIsNonPositive_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                delegate { BrokerServerOptions.CreateWithStableSubscriberIdentity(TimeSpan.Zero, TimeProvider.System); });
        }

        // stable identity 옵션 생성 테스트: feature 는 opt-in 이고 TimeProvider 는 기존 options 시간 소스와 공유한다.
        [Fact]
        public void CreateWithStableSubscriberIdentity_WhenRetentionIsPositive_StoresValues()
        {
            ManualTimeProvider timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-22T00:00:00Z"));

            BrokerServerOptions options = BrokerServerOptions.CreateWithStableSubscriberIdentity(
                TimeSpan.FromMinutes(5),
                timeProvider);

            Assert.True(options.StableSubscriberIdentityEnabled);
            Assert.Equal(TimeSpan.FromMinutes(5), options.StableSubscriberRetentionTimeout);
            Assert.Same(timeProvider, options.TimeProvider);
        }
```

Append to `tests/Hps.Server.Tests/BrokerServerTests.cs`:

```csharp
        // Server stable identity wiring 테스트: enabled options 로 TCP를 시작하면 BrokerServer 가 shared registry 를 TCP handler 에 주입해야 한다.
        // reconnect 후 같은 id 로 REGISTER 한 새 connection 이 기존 topic subscription 을 이어받는지 실제 handler 경계에서 검증한다.
        [Fact]
        public async Task StartTcpAsync_WhenStableSubscriberIdentityEnabled_WiresRegistryIntoTcpHandler()
        {
            FakeTransport transport = new FakeTransport();
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(128);
            ManualTimeProvider timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-22T00:00:00Z"));
            BrokerServerOptions options = BrokerServerOptions.CreateWithStableSubscriberIdentity(
                TimeSpan.FromMinutes(5),
                timeProvider);
            using (BrokerServer server = new BrokerServer(transport, pool, 128, options))
            {
                await server.StartTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                BrokerTcpFrameHandler handler = Assert.IsType<BrokerTcpFrameHandler>(transport.ReceiveHandler);
                FakeConnection oldConnection = new FakeConnection();
                FakeConnection newConnection = new FakeConnection();

                handler.OnFrame(oldConnection, RentFrame(pool, "REGISTER device-a"));
                handler.OnFrame(oldConnection, RentFrame(pool, "SUBSCRIBE alpha"));
                handler.OnConnectionClosed(oldConnection);
                handler.OnFrame(newConnection, RentFrame(pool, "REGISTER device-a"));

                Assert.True(ReadSubscriptionTable(server).IsSubscribed("alpha", newConnection));
                Assert.False(ReadSubscriptionTable(server).IsSubscribed("alpha", oldConnection));
                Assert.Equal(0, pool.RentedCount);
            }
        }

        // retention timer 수명 테스트: stable identity timer 는 Server host 가 소유하므로 StopAsync 에서 dispose 되어야 한다.
        // timer 가 남으면 stop 이후 registry sweep callback 이 들어와 host 수명 경계를 흐릴 수 있다.
        [Fact]
        public async Task StopAsync_WhenStableSubscriberIdentityEnabled_DisposesRetentionTimer()
        {
            FakeTransport transport = new FakeTransport();
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(128);
            ManualTimeProvider timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-22T00:00:00Z"));
            BrokerServerOptions options = BrokerServerOptions.CreateWithStableSubscriberIdentity(
                TimeSpan.FromMinutes(5),
                timeProvider);
            using (BrokerServer server = new BrokerServer(transport, pool, 128, options))
            {
                await server.StartTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                ManualTimer timer = Assert.Single(timeProvider.Timers);

                await server.StopAsync();

                Assert.Equal(1, timer.DisposeCallCount);
            }
        }
```

Add `RentFrame` helper to `BrokerServerTests` if missing:

```csharp
        private static RefCountedBuffer RentFrame(PinnedBlockMemoryPool pool, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            RefCountedBuffer frame = pool.RentCounted();
            bytes.CopyTo(frame.Span);
            frame.SetLength(bytes.Length);
            return frame;
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\Hps.Server.Tests\Hps.Server.Tests.csproj --filter "FullyQualifiedName~StableSubscriberIdentity|FullyQualifiedName~StableSubscriber"
```

Expected: FAIL with compile errors for missing BrokerServerOptions stable identity members.

- [ ] **Step 3: Write minimal implementation**

Modify `BrokerServerOptions` constructor and properties:

```csharp
        private BrokerServerOptions(
            bool udpLeaseSweepEnabled,
            TimeSpan udpLeaseIdleTimeout,
            TimeSpan udpLeaseSweepInterval,
            bool stableSubscriberIdentityEnabled,
            TimeSpan stableSubscriberRetentionTimeout,
            TimeProvider timeProvider)
```

Add properties:

```csharp
        public bool StableSubscriberIdentityEnabled { get; }

        public TimeSpan StableSubscriberRetentionTimeout { get; }
```

Add factories:

```csharp
        public static BrokerServerOptions CreateWithStableSubscriberIdentity(
            TimeSpan retentionTimeout,
            TimeProvider? timeProvider)
        {
            if (retentionTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(retentionTimeout));

            return new BrokerServerOptions(
                false,
                TimeSpan.Zero,
                TimeSpan.Zero,
                true,
                retentionTimeout,
                timeProvider ?? TimeProvider.System);
        }

        public BrokerServerOptions WithStableSubscriberIdentity(TimeSpan retentionTimeout)
        {
            if (retentionTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(retentionTimeout));

            return new BrokerServerOptions(
                UdpLeaseSweepEnabled,
                UdpLeaseIdleTimeout,
                UdpLeaseSweepInterval,
                true,
                retentionTimeout,
                TimeProvider);
        }
```

Modify `BrokerServer` fields:

```csharp
        private readonly SubscriberRegistry? _subscriberRegistry;
        private ITimer? _subscriberRetentionTimer;
```

In constructor after `_subscriptions`:

```csharp
            _subscriberRegistry = _options.StableSubscriberIdentityEnabled
                ? new SubscriberRegistry(_subscriptions)
                : null;
```

Construct handlers with registry:

```csharp
            _brokerFrameHandler = new BrokerTcpFrameHandler(_subscriptions, _publisher, _subscriberRegistry, _options.TimeProvider);
            _brokerDatagramHandler = new BrokerUdpDatagramHandler(
                _subscriptions,
                _publisher,
                CreateUdpLeaseOptions(_options),
                _options.TimeProvider,
                _subscriberRegistry);
```

Start a retention timer after successful TCP or UDP start:

```csharp
        private ITimer? CreateSubscriberRetentionTimer()
        {
            if (!_options.StableSubscriberIdentityEnabled || _subscriberRegistry == null)
                return null;

            return _options.TimeProvider.CreateTimer(
                OnSubscriberRetentionTimer,
                null,
                _options.StableSubscriberRetentionTimeout,
                _options.StableSubscriberRetentionTimeout);
        }

        private void OnSubscriberRetentionTimer(object? state)
        {
            if (_subscriberRegistry == null)
                return;

            _subscriberRegistry.SweepDisconnected(
                _options.TimeProvider.GetUtcNow(),
                _options.StableSubscriberRetentionTimeout);
        }
```

Ensure only one timer is created:

```csharp
        private void EnsureSubscriberRetentionTimerStarted()
        {
            if (_subscriberRetentionTimer != null)
                return;

            _subscriberRetentionTimer = CreateSubscriberRetentionTimer();
        }
```

Call `EnsureSubscriberRetentionTimerStarted()` inside the successful `StartTcpAsync` and `StartUdpAsync` lock blocks after endpoint/listener assignment. Dispose `_subscriberRetentionTimer` in `StopAsync` alongside `_udpLeaseSweepTimer`, and set it to null inside the lock before dispose.

- [ ] **Step 4: Run tests to verify they pass**

Run:

```powershell
dotnet test tests\Hps.Server.Tests\Hps.Server.Tests.csproj --filter "FullyQualifiedName~StableSubscriberIdentity|FullyQualifiedName~StableSubscriber"
```

Expected: PASS for stable identity options and server wiring tests.

- [ ] **Step 5: Verify and commit**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
git diff --check
git status --short
git add src/Hps.Server/BrokerServerOptions.cs src/Hps.Server/BrokerServer.cs tests/Hps.Server.Tests/BrokerServerOptionsTests.cs tests/Hps.Server.Tests/BrokerServerTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: enable stable subscriber identity"
```

Expected: build warning 0/error 0, all tests pass, whitespace check passes.

---

## Scope Left After This Plan

- 인증/권한 또는 configuration resolver 로 `subscriber-id`를 검증하는 production hardening.
- disconnected 기간 payload buffering, replay, reliable delivery.
- stable identity 를 diagnostics snapshot 에 operator-friendly name 으로 노출하는 server/metrics surface.
- process restart 뒤 identity persistence.
- topic/data type 별 QoS 와 drop policy 선택.

## Self-Review

- Spec coverage: `REGISTER`/`UNREGISTER`, opt-in registry, same-id rebind, same-target different-id rejection, disconnected metadata retention, no payload replay, TCP/UDP common namespace 를 Task 1~5가 다룬다.
- Placeholder scan: 이 계획은 자리표시자나 미완성 지시를 포함하지 않는다. 남은 항목은 `Scope Left After This Plan`에 명시한 범위 밖 기능이다.
- Type consistency: `SubscriberIdentity`, `SubscriberRegistry`, `SubscriberRegistrationResult`, `CreateWithStableSubscriberIdentity`, `WithStableSubscriberIdentity`, `SweepDisconnected` 이름과 인자는 Task 간 일치한다.
