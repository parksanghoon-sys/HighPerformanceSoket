# RIO Payload Registration Cache Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** RIO payload send path 에 남은 per-operation `RIORegisterBuffer`/`RIODeregisterBuffer` 비용을 connection resource bounded cache 로 줄인다.

**Architecture:** `RioPayloadRegistrationCache`가 backing `byte[]` object identity 별 buffer id 와 outstanding lease count 를 소유한다. `RioConnectionResource`가 cache 를 소유하고, payload send path 는 cache lease 를 얻어 `SendRegisteredBufferAsync(...)`를 호출한다. receive block 과 length-prefix block reuse 는 Task A 상태를 유지한다.

**Tech Stack:** .NET 9.0, C# 8.0, Winsock Registered I/O, xUnit.

## Global Constraints

- C# 8.0 문법만 사용한다.
- public API 를 추가하지 않는다.
- RIO opt-in backend 만 변경하고 `TransportFactory.CreateDefault()`는 SAEA 유지.
- payload cache 는 connection resource bounded cache 로 시작한다(D107).
- outstanding `RIOSend` request 중인 buffer id 는 deregister 하지 않는다.
- 모든 새 테스트에는 무엇을 검증하는지 한국어 주석을 단다.

---

## Files

- Create: `src/Hps.Transport.Rio/RioPayloadRegistrationCache.cs`
  - payload backing `byte[]` identity cache 와 lease owner 를 구현한다.
- Create: `tests/Hps.Transport.Rio.Tests/RioPayloadRegistrationCacheTests.cs`
  - pure owner behavior 를 fake registrar 로 검증한다.
- Modify: `src/Hps.Transport.Rio/RioTransport.cs`
  - `RioConnectionResource`가 cache 를 소유하고 payload send path 에 cache lease 를 연결한다.
- Modify: `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`
  - RIO loopback 에서 같은 backing payload block 재전송 시 payload registration 이 반복되지 않는지 검증한다.
- Modify: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`
  - 구현 결과, 검증, session-06 benchmark 를 기록한다.

---

### Task 1: Pure Payload Registration Cache Owner

**Files:**
- Create: `src/Hps.Transport.Rio/RioPayloadRegistrationCache.cs`
- Create: `tests/Hps.Transport.Rio.Tests/RioPayloadRegistrationCacheTests.cs`

**Interfaces:**
- Produces:
  - `internal sealed class RioPayloadRegistrationCache : IDisposable`
  - `internal RioPayloadBufferLease Acquire(byte[] block)`
  - `internal int CachedCount { get; }`
  - `internal interface IRioBufferRegistrar`
  - `internal readonly struct RioPayloadBufferLease : IDisposable`
  - `internal IntPtr BufferId { get; }`

- [ ] **Step 1: Write the failing pure owner tests**

Create `tests/Hps.Transport.Rio.Tests/RioPayloadRegistrationCacheTests.cs`:

```csharp
using System;
using Xunit;

namespace Hps.Transport.Rio.Tests
{
    public sealed class RioPayloadRegistrationCacheTests
    {
        // 같은 backing byte[]는 같은 native buffer id 를 재사용해야 한다.
        // 이 테스트는 payload send 두 번이 register/deregister 두 번으로 퇴행하지 않도록 cache hit 계약을 고정한다.
        [Fact]
        public void Acquire_WhenSameBlockIsReused_RegistersOnlyOnce()
        {
            RecordingRegistrar registrar = new RecordingRegistrar();
            using (RioPayloadRegistrationCache cache = new RioPayloadRegistrationCache(registrar, capacity: 4))
            {
                byte[] block = new byte[16];

                using (RioPayloadBufferLease first = cache.Acquire(block))
                using (RioPayloadBufferLease second = cache.Acquire(block))
                {
                    Assert.Equal(first.BufferId, second.BufferId);
                }

                Assert.Equal(1, registrar.RegisterCallCount);
                Assert.Equal(0, registrar.DeregisterCallCount);
                Assert.Equal(1, cache.CachedCount);
            }

            Assert.Equal(1, registrar.DeregisterCallCount);
        }

        // cache capacity 를 넘으면 outstanding lease 가 없는 가장 오래된 entry 를 해제해야 한다.
        // 이렇게 해야 장시간 실행 중 registered memory footprint 가 무한히 증가하지 않는다.
        [Fact]
        public void Acquire_WhenCapacityIsExceeded_EvictsIdleOldestEntry()
        {
            RecordingRegistrar registrar = new RecordingRegistrar();
            using (RioPayloadRegistrationCache cache = new RioPayloadRegistrationCache(registrar, capacity: 1))
            {
                byte[] firstBlock = new byte[16];
                byte[] secondBlock = new byte[16];

                cache.Acquire(firstBlock).Dispose();
                cache.Acquire(secondBlock).Dispose();

                Assert.Equal(2, registrar.RegisterCallCount);
                Assert.Equal(1, registrar.DeregisterCallCount);
                Assert.Equal(1, cache.CachedCount);
            }

            Assert.Equal(2, registrar.DeregisterCallCount);
        }

        // outstanding send 가 있는 entry 는 cache dispose 시점에 바로 deregister 하면 안 된다.
        // 마지막 lease release 가 들어올 때까지 deregister 를 지연해야 RIO outstanding request 계약을 지킨다.
        [Fact]
        public void Dispose_WhenLeaseIsOutstanding_DeregistersAfterLeaseRelease()
        {
            RecordingRegistrar registrar = new RecordingRegistrar();
            RioPayloadBufferLease lease;
            using (RioPayloadRegistrationCache cache = new RioPayloadRegistrationCache(registrar, capacity: 4))
            {
                lease = cache.Acquire(new byte[16]);
                cache.Dispose();

                Assert.Equal(0, registrar.DeregisterCallCount);
            }

            lease.Dispose();

            Assert.Equal(1, registrar.DeregisterCallCount);
        }

        // capacity 가 가득 찼고 모든 entry 가 outstanding 이면 unsafe eviction 대신 per-operation fallback lease 를 쓴다.
        // fallback lease 는 release 때 바로 deregister 되어 cache entry 로 남지 않는다.
        [Fact]
        public void Acquire_WhenCapacityIsFullAndAllEntriesAreOutstanding_UsesUncachedLease()
        {
            RecordingRegistrar registrar = new RecordingRegistrar();
            using (RioPayloadRegistrationCache cache = new RioPayloadRegistrationCache(registrar, capacity: 1))
            {
                RioPayloadBufferLease cached = cache.Acquire(new byte[16]);
                RioPayloadBufferLease fallback = cache.Acquire(new byte[16]);

                Assert.Equal(2, registrar.RegisterCallCount);
                Assert.Equal(1, cache.CachedCount);

                fallback.Dispose();
                Assert.Equal(1, registrar.DeregisterCallCount);

                cached.Dispose();
            }

            Assert.Equal(2, registrar.DeregisterCallCount);
        }

        private sealed class RecordingRegistrar : IRioBufferRegistrar
        {
            private int _next;

            internal int RegisterCallCount { get; private set; }

            internal int DeregisterCallCount { get; private set; }

            public IntPtr Register(byte[] block)
            {
                RegisterCallCount++;
                _next++;
                return new IntPtr(_next);
            }

            public void Deregister(IntPtr bufferId)
            {
                DeregisterCallCount++;
            }
        }
    }
}
```

- [ ] **Step 2: Verify Red**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~RioPayloadRegistrationCacheTests"
```

Expected: assertion failure or type absence failure because `RioPayloadRegistrationCache` does not exist.

- [ ] **Step 3: Implement minimal cache owner**

Create `src/Hps.Transport.Rio/RioPayloadRegistrationCache.cs` with:

- `Dictionary<byte[], Entry>` using `ReferenceEqualityComparer.Instance`.
- `Acquire(byte[] block)` validates non-null and returns `RioPayloadBufferLease`.
- cached entries increment/decrement `OutstandingLeaseCount`.
- `Dispose()` marks cache disposed and deregisters only idle entries.
- outstanding disposed entries deregister from `RioPayloadBufferLease.Dispose()`.

- [ ] **Step 4: Verify Green**

Run the same focused command.

Expected: all `RioPayloadRegistrationCacheTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add src\Hps.Transport.Rio\RioPayloadRegistrationCache.cs tests\Hps.Transport.Rio.Tests\RioPayloadRegistrationCacheTests.cs
git commit -m "feat: add rio payload registration cache owner"
```

---

### Task 2: Payload Send Path Cache Lease

**Files:**
- Modify: `src/Hps.Transport.Rio/RioTransport.cs`
- Modify: `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`

**Interfaces:**
- Consumes: `RioConnectionResource.PayloadRegistrationCache.Acquire(byte[] block)`
- Produces: payload send path no longer calls `RegisterPinnedArray(...)` on cache hit.

- [ ] **Step 1: Write the failing loopback test**

Add to `RioTransportTcpTests`:

```csharp
// 같은 backing payload block 을 같은 RIO connection 으로 두 번 보낼 때 payload registration 은 한 번만 일어나야 한다.
// receive/prefix resource registration 은 connection setup 전에 reset 되므로, reset 이후 증가는 payload cache miss 만 의미한다.
[Fact]
public async Task TcpLoopback_WhenRioAvailable_ReusesPayloadRegistrationForSameBackingBlock()
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
        RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
    {
        return;
    }

    RioBufferRegistrationDiagnostics diagnostics = GetRioBufferRegistrationDiagnostics();
    long registrations = await SendSameBackingPayloadTwiceAsync(diagnostics);

    Assert.Equal(1, registrations);
}
```

Use a helper that rents one `RefCountedBuffer`, sends it once, waits for one byte, calls `AddRef()` again while the buffer is still live, sends it again, then releases the test guard ref after both sends finish. This keeps backing array identity stable.

- [ ] **Step 2: Verify Red**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~ReusesPayloadRegistrationForSameBackingBlock"
```

Expected: fail with `Expected: 1, Actual: 2` because current payload send still registers per operation.

- [ ] **Step 3: Switch payload send path to cache lease**

In `RioTransport.cs`, first add an internal nested registrar:

```csharp
private sealed class RioNativeBufferRegistrar : IRioBufferRegistrar
{
    private readonly RioNative _native;

    internal RioNativeBufferRegistrar(RioNative native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public IntPtr Register(byte[] block)
    {
        return RegisterPinnedArray(_native, block);
    }

    public void Deregister(IntPtr bufferId)
    {
        _native.DeregisterBuffer(bufferId);
    }
}
```

In `RioConnectionResource` constructor:

```csharp
PayloadRegistrationCache = new RioPayloadRegistrationCache(new RioNativeBufferRegistrar(Native), capacity: 64);
```

Add property:

```csharp
internal RioPayloadRegistrationCache PayloadRegistrationCache { get; }
```

In `Dispose()` after CQ close and before signal dispose:

```csharp
PayloadRegistrationCache.Dispose();
```

Then replace the payload call in `SendInFlightAsync(...)`:

```csharp
await SendRegisteredArrayAsync(resource, connection, segment.Array, segment.Offset + sendBuffer.Offset, sendBuffer.Length).ConfigureAwait(false);
```

with:

```csharp
using (RioPayloadBufferLease lease = resource.PayloadRegistrationCache.Acquire(segment.Array))
{
    await SendRegisteredBufferAsync(
        resource,
        connection,
        lease.BufferId,
        segment.Offset + sendBuffer.Offset,
        sendBuffer.Length).ConfigureAwait(false);
}
```

Keep `SendRegisteredArrayAsync(...)` only if fallback paths still need it. If no production caller remains, remove it after tests are green.

- [ ] **Step 4: Verify Green**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~ReusesPayloadRegistrationForSameBackingBlock|FullyQualifiedName~ReusesReceiveBufferRegistrationAcrossPayloads|FullyQualifiedName~ReusesLengthPrefixRegistrationAcrossPayloads"
```

Expected: all three registration reuse tests pass.

- [ ] **Step 5: Run all focused RIO tests**

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore
```

Expected: all RIO tests pass.

- [ ] **Step 6: Commit**

```powershell
git add src\Hps.Transport.Rio\RioTransport.cs tests\Hps.Transport.Rio.Tests\RioTransportTcpTests.cs
git commit -m "perf: cache rio payload registrations"
```

---

### Task 3: Verification, Benchmark, State Update

**Files:**
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

- [ ] **Step 1: Run repeated close/wake tests**

```powershell
$ErrorActionPreference = 'Stop'; for ($i = 1; $i -le 10; $i++) { dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~TcpLoopback_WhenRioAvailable_RepeatedCloseAfterAcceptDoesNotCrash|FullyQualifiedName~TcpLoopback_WhenRioAvailable_DeliversSmallPayloadWithoutTimerScaleWake"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }
```

Expected: 10 iterations pass.

- [ ] **Step 2: Run solution build/test**

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-restore
```

Expected: build warning 0/error 0; all tests pass.

- [ ] **Step 3: Collect session-06 RIO benchmark**

```powershell
$dir = Join-Path (Get-Location) 'artifacts\benchmarks\rio-comparison\2026-06-25\session-06'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-restore -- --load --backend rio --report (Join-Path $dir 'rio-load.json')
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-restore -- --load-open-loop --backend rio --report (Join-Path $dir 'rio-open-loop.json')
```

Expected: delivery/drop/leak hard gate pass. Compare p50/p99/actual-rate against session-05.

- [ ] **Step 4: Update state docs**

Record:

- payload cache owner and send path wiring completed
- transport-wide shared cache remains deferred
- verification commands
- session-06 benchmark numbers

- [ ] **Step 5: Commit**

```powershell
git add CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "docs: record rio payload cache results"
```

---

## Final Verification

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-restore
git diff --check
git status -sb
```

Expected:

- build warning 0/error 0
- all tests pass
- no whitespace errors
- only user-owned `.claude/review` files remain untracked
