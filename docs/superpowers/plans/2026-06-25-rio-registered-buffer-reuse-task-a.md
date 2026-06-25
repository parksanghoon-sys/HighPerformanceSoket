# RIO Registered Buffer Reuse Task A Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** RIO receive block 과 length-prefix block 을 connection resource lifetime 에 한 번만 등록해 per-operation `RIORegisterBuffer`/`RIODeregisterBuffer` 비용을 제거한다.

**Architecture:** `RioConnectionResource`가 registered receive block 과 registered prefix block 을 소유한다. Receive loop 와 prefix send path 는 resource-owned buffer id 를 재사용하고, payload send path 는 D106에 따라 기존 per-operation registration 을 유지한다.

**Tech Stack:** .NET 9.0, C# 8.0, Winsock Registered I/O, xUnit.

---

## Files

- Modify: `src/Hps.Transport.Rio/RioTransport.cs`
  - `RioConnectionResource`에 receive/prefix registered buffer owner 를 추가한다.
  - receive loop 의 per-iteration register/deregister 를 제거한다.
  - prefix send path 를 resource-owned buffer id 로 전송한다.
  - payload send path 는 기존 per-operation registration helper 를 유지한다.
- Modify: `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`
  - existing receive/send/length-prefix/close tests 를 재사용한다.
  - 필요하면 internal diagnostic counter 는 public surface 없이 test assembly only 로 둔다.
- Modify: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`
  - 구현 결과와 benchmark observation 을 기록한다.

---

### Task 1: Receive Block Resource Registration

**Files:**
- Modify: `src/Hps.Transport.Rio/RioTransport.cs`
- Test: `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`

- [ ] **Step 1: Write the failing test**

Add an internal diagnostic property to `RioConnectionResource` only if needed:

```csharp
internal int ReceiveBufferRegistrationCount { get; private set; }
```

Prefer not to expose it if existing tests plus code review are enough. If adding a test, use a RIO loopback that sends two payloads over one connection and assert registration count stays 1.

- [ ] **Step 2: Verify current behavior**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~TcpLoopback_WhenRioAvailable_DeliversPayload"
```

Expected: pass before implementation. Current behavior is functionally correct; Red evidence for this task is design/performance evidence, not delivery failure.

- [ ] **Step 3: Implement resource-owned receive registration**

In `RioConnectionResource` constructor:

- rent one `byte[] ReceiveBlock = ReceivePool.Rent()`
- register it once: `ReceiveBufferId = RegisterPinnedArray(Native, ReceiveBlock)`
- dispose path deregisters `ReceiveBufferId` and returns `ReceiveBlock`

In `ReceiveLoopAsync`:

- remove per-iteration `ReceivePool.Rent()`
- remove local `bufferId`
- use `resource.ReceiveBlock` and `resource.ReceiveBufferId`
- do not return receive block in loop finally

- [ ] **Step 4: Run focused RIO tests**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore
```

Expected: all RIO tests pass.

---

### Task 2: Length Prefix Resource Registration

**Files:**
- Modify: `src/Hps.Transport.Rio/RioTransport.cs`
- Test: `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`

- [ ] **Step 1: Write/confirm regression test**

Use existing test:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~TcpLoopback_WhenRioAvailable_DeliversLengthPrefixedPayload"
```

Expected: pass before implementation. This is the behavioral guard for prefix framing.

- [ ] **Step 2: Implement resource-owned prefix registration**

In `RioConnectionResource` constructor:

- allocate pinned `byte[] LengthPrefixBlock = GC.AllocateUninitializedArray<byte>(TcpLengthPrefixSize, pinned: true)`
- register it once: `LengthPrefixBufferId = RegisterPinnedArray(Native, LengthPrefixBlock)`

In `SendLoopAsync`/`SendInFlightAsync`:

- remove send-loop local `lengthPrefixBuffer`
- write length into `resource.LengthPrefixBlock`
- send prefix through a helper using `resource.LengthPrefixBufferId`
- keep payload helper using per-operation registration

Dispose:

- deregister `LengthPrefixBufferId` after outstanding operations are impossible.

- [ ] **Step 3: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~TcpLoopback_WhenRioAvailable_DeliversLengthPrefixedPayload|FullyQualifiedName~TcpLoopback_WhenRioAvailable_DeliversSmallPayloadWithoutTimerScaleWake"
```

Expected: both pass.

---

### Task 3: Verification And Benchmark Observation

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

- [ ] **Step 3: Collect session-05 RIO benchmark**

```powershell
$dir = Join-Path (Get-Location) 'artifacts\benchmarks\rio-comparison\2026-06-25\session-05'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-restore -- --load --backend rio --report (Join-Path $dir 'rio-load.json')
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-restore -- --load-open-loop --backend rio --report (Join-Path $dir 'rio-open-loop.json')
```

Expected: delivery/drop/leak hard gate pass. Compare p50/p99/actual-rate against session-04.

- [ ] **Step 4: Update state docs**

Record:

- receive/prefix registration reuse completed
- payload registration cache remains separate
- verification commands
- session-05 benchmark numbers

- [ ] **Step 5: Commit**

```powershell
git add src/Hps.Transport.Rio/RioTransport.cs tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "perf: reuse rio receive prefix registrations"
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
