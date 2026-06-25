# RIO IOCP Notification Completion Wait Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** RIO completion wait 를 polling/timer 기반에서 `RIONotify` + shared IOCP pump 기반으로 전환한다.

**Architecture:** `RioTransport`가 하나의 `RioCompletionPort`를 소유하고, 각 receive/send CQ는 `RioCompletionSignal`을 통해 IOCP notification 을 받는다. IOCP pump 는 CQ를 dequeue 하지 않고 signal 만 깨우며, 실제 dequeue/notify/close 는 기존 `RioConnectionResource` gate 로 직렬화한다.

**Tech Stack:** .NET 9.0, C# 8.0, Winsock Registered I/O, IOCP P/Invoke, xUnit.

---

## Files

- Modify: `src/Hps.Transport.Rio/RioNative.cs`
  - RIONotify delegate, notification completion structs, IOCP P/Invoke 를 추가한다.
- Create: `src/Hps.Transport.Rio/RioCompletionPort.cs`
  - transport-wide IOCP handle, pump task, signal registry, shutdown wake 를 소유한다.
- Create: `src/Hps.Transport.Rio/RioCompletionSignal.cs`
  - CQ별 OVERLAPPED/key/native notification memory 와 wait/fault/dispose 상태를 소유한다.
- Modify: `src/Hps.Transport.Rio/RioTransport.cs`
  - `RioCompletionPort`를 transport lifetime 에 묶고 `WaitForCompletionAsync`를 notification wait 로 바꾼다.
- Modify: `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`
  - 기존 latency/close tests 를 재사용하고 필요 시 IOCP wait 회귀 test 를 추가한다.
- Modify: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`
  - 구현 결과, benchmark 관측, 남은 tail 여부를 기록한다.

---

### Task 1: Native Notification Shape

**Files:**
- Modify: `src/Hps.Transport.Rio/RioNative.cs`
- Test: `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`

- [ ] **Step 1: Write the failing test**

Add a RIO available test that proves the loaded function table exposes notification-capable creation without changing default factory behavior.

```csharp
[Fact]
public void TryLoadFunctionTable_WhenRioAvailable_ExposesNotificationFunctions()
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
        RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
    {
        return;
    }

    Assert.True(RioNative.TryLoadFunctionTable(out RioNative? native));
    Assert.NotNull(native);
    Assert.True(native!.SupportsCompletionNotification);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~TryLoadFunctionTable_WhenRioAvailable_ExposesNotificationFunctions"
```

Expected: fail because `SupportsCompletionNotification` does not exist.

- [ ] **Step 3: Add native shapes**

In `RioNative.cs`:

- Add `RioNotifyDelegate`.
- Marshal `_functionTable.Notify` into `_notify`.
- Add `internal bool SupportsCompletionNotification { get { return _functionTable.Notify != IntPtr.Zero; } }`.
- Add `Notify(IntPtr completionQueue)` that returns the native error code.
- Add IOCP P/Invoke wrappers:
  - `CreateIoCompletionPort`
  - `GetQueuedCompletionStatusEx`
  - `PostQueuedCompletionStatus`
  - `CloseHandle`
- Add `RioNotificationCompletion`, `RioNotificationIocp`, and `NativeOverlapped64` structs with explicit comments about pointer lifetime.
- Add `CreateCompletionQueue(int queueSize, IntPtr notificationCompletion)` overload.

- [ ] **Step 4: Run focused test**

Run the same focused test.

Expected: pass on RIO available; skip by early return elsewhere.

- [ ] **Step 5: Run RIO suite**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore
```

Expected: all RIO tests pass.

- [ ] **Step 6: Commit**

```powershell
git add src/Hps.Transport.Rio/RioNative.cs tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs
git commit -m "feat: expose rio completion notification natives"
```

---

### Task 2: Completion Port And Signal Owners

**Files:**
- Create: `src/Hps.Transport.Rio/RioCompletionPort.cs`
- Create: `src/Hps.Transport.Rio/RioCompletionSignal.cs`
- Test: `tests/Hps.Transport.Rio.Tests/RioCompletionPortTests.cs`

- [ ] **Step 1: Write failing lifecycle tests**

Create `RioCompletionPortTests.cs` with tests for pure managed lifecycle where possible:

```csharp
[Fact]
public async Task Signal_WhenCompleted_WakesSingleWaiter()
{
    using (RioCompletionPort completionPort = RioCompletionPort.CreateForTests())
    using (RioCompletionSignal signal = completionPort.CreateSignalForTests())
    {
        Task wait = signal.WaitAsync();
        signal.CompleteForTests();

        Task completed = await Task.WhenAny(wait, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);

        Assert.Same(wait, completed);
    }
}

[Fact]
public async Task Signal_WhenDisposed_WakesWaiterAsDisposed()
{
    using (RioCompletionPort completionPort = RioCompletionPort.CreateForTests())
    {
        RioCompletionSignal signal = completionPort.CreateSignalForTests();
        Task wait = signal.WaitAsync();

        signal.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async delegate()
        {
            await wait.ConfigureAwait(false);
        });
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~RioCompletionPortTests"
```

Expected: compile/test failure because new types do not exist.

- [ ] **Step 3: Implement `RioCompletionSignal`**

Create signal owner with:

- private lock
- `TaskCompletionSource<bool>` for current wait
- `bool _disposed`
- `bool _notifyArmed`
- native notification pointer fields reserved for Task 3
- `WaitAsync()`
- `CompleteFromPump()`
- `FaultFromPump(Exception)`
- `Dispose()`
- test-only factory/completion methods marked `internal`

Do not call RIO native APIs in Task 2.

- [ ] **Step 4: Implement `RioCompletionPort` skeleton**

Create completion port owner with:

- `CreateForTests()`
- `CreateSignalForTests()`
- registry by completion key
- `Dispose()` that faults all registered signals

The actual IOCP pump can be a no-op in Task 2; Task 3 wires native wait.

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~RioCompletionPortTests"
```

Expected: pass.

- [ ] **Step 6: Run solution build**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
```

Expected: warning 0, error 0.

- [ ] **Step 7: Commit**

```powershell
git add src/Hps.Transport.Rio/RioCompletionPort.cs src/Hps.Transport.Rio/RioCompletionSignal.cs tests/Hps.Transport.Rio.Tests/RioCompletionPortTests.cs
git commit -m "feat: add rio completion signal owners"
```

---

### Task 3: RIONotify + IOCP Wiring

**Files:**
- Modify: `src/Hps.Transport.Rio/RioCompletionPort.cs`
- Modify: `src/Hps.Transport.Rio/RioCompletionSignal.cs`
- Modify: `src/Hps.Transport.Rio/RioTransport.cs`
- Modify: `src/Hps.Transport.Rio/RioNative.cs`
- Test: `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`

- [ ] **Step 1: Confirm existing Red/benchmark evidence**

Run the existing latency test against current bounded polling before wiring:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~TcpLoopback_WhenRioAvailable_DeliversSmallPayloadWithoutTimerScaleWake"
```

Expected: pass. This test remains a regression guard, not the p99 gate.

Record current D102 benchmark values from `TODOS.md`: RIO load p99 16689.0 us, open-loop p99 16736.2 us.

- [ ] **Step 2: Create real completion port**

Change `RioTransport` constructor to create a `RioCompletionPort` when RIO resources are created, not during unavailable probe.
Keep default factory unchanged.

The port must:

- create IOCP handle through `RioNative.CreateIoCompletionPortHandle()`
- start one pump task
- wait with `GetQueuedCompletionStatusEx`
- map completion key or overlapped pointer to `RioCompletionSignal`
- call `signal.CompleteFromPump()`
- ignore late completions for disposed/unregistered signals
- use `PostQueuedCompletionStatus` to wake shutdown

- [ ] **Step 3: Create notification CQs**

Change `RioConnectionResource` constructor:

```csharp
ReceiveSignal = completionPort.CreateSignal();
SendSignal = completionPort.CreateSignal();
ReceiveCompletionQueue = Native.CreateCompletionQueue(CompletionQueueSize, ReceiveSignal.NotificationCompletionPointer);
SendCompletionQueue = Native.CreateCompletionQueue(CompletionQueueSize, SendSignal.NotificationCompletionPointer);
```

Dispose order:

1. mark disposed
2. dispose/fault signals
3. dispose socket
4. under `_completionGate`, close CQs
5. unregister signals from completion port

- [ ] **Step 4: Replace wait loop**

Change `WaitForCompletionAsync` signature to include `RioCompletionSignal signal`.

Algorithm:

```csharp
while (true)
{
    ThrowIfClosedOrDisposed();

    uint completed = resource.DequeueCompletion(completionQueue, results);
    if (completed != 0)
        return results[0];

    resource.ArmNotification(completionQueue, signal);
    await signal.WaitAsync().ConfigureAwait(false);
}
```

`ArmNotification` must run under `_completionGate`, call `Native.Notify(cq)`, and handle:

- `ERROR_SUCCESS`: armed.
- `WSAEALREADY`: already armed, wait.
- any other code: throw `SocketException`.

- [ ] **Step 5: Run RIO focused tests**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore
```

Expected: all RIO tests pass.

- [ ] **Step 6: Run close/wake repeat**

Run:

```powershell
$ErrorActionPreference = 'Stop'; for ($i = 1; $i -le 10; $i++) { dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~TcpLoopback_WhenRioAvailable_RepeatedCloseAfterAcceptDoesNotCrash|FullyQualifiedName~TcpLoopback_WhenRioAvailable_DeliversSmallPayloadWithoutTimerScaleWake"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }
```

Expected: 10 iterations pass.

- [ ] **Step 7: Run solution build/test**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-restore
```

Expected: build warning 0/error 0, all tests pass.

- [ ] **Step 8: Commit**

```powershell
git add src/Hps.Transport.Rio/RioNative.cs src/Hps.Transport.Rio/RioCompletionPort.cs src/Hps.Transport.Rio/RioCompletionSignal.cs src/Hps.Transport.Rio/RioTransport.cs tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs
git commit -m "feat: wait for rio completions with iocp notifications"
```

---

### Task 4: Benchmark Observation And State Update

**Files:**
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/decisions/2026-06.md`

- [ ] **Step 1: Collect scratch benchmark artifacts**

Run:

```powershell
$dir = Join-Path (Get-Location) 'artifacts\benchmarks\rio-comparison\2026-06-25\session-04'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-restore -- --load --backend rio --report (Join-Path $dir 'rio-load.json')
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-restore -- --load-open-loop --backend rio --report (Join-Path $dir 'rio-open-loop.json')
```

Expected: delivery/drop/leak hard gate pass.

- [ ] **Step 2: Compare against D102 session-03**

Read:

- `artifacts/benchmarks/rio-comparison/2026-06-25/session-03/rio-load.json`
- `artifacts/benchmarks/rio-comparison/2026-06-25/session-03/rio-open-loop.json`
- `artifacts/benchmarks/rio-comparison/2026-06-25/session-04/rio-load.json`
- `artifacts/benchmarks/rio-comparison/2026-06-25/session-04/rio-open-loop.json`

Record actual-rate, p50, p99.

- [ ] **Step 3: Update state docs**

Update:

- `CURRENT_PLAN.md`: current result and next execution point.
- `TODOS.md`: move completed IOCP wait item to Completed; add follow-up if p99 tail remains.
- `CHANGELOG_AGENT.md`: commands and observed numbers.
- `DECISIONS.md` and archive: add a new decision only if result changes direction.

- [ ] **Step 4: Verify docs and working tree**

Run:

```powershell
git diff --check
git status -sb
```

Expected: no whitespace errors; scratch `artifacts/` remains ignored; `.claude/review` remains untracked.

- [ ] **Step 5: Commit**

```powershell
git add CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs/agent-state/decisions/2026-06.md
git commit -m "docs: record rio iocp completion benchmark results"
```

---

## Final Verification

After all tasks:

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
- no staged/uncommitted production or state-doc changes except intentional ignored scratch artifacts and user-owned `.claude/review` files
