# RIO UDP Completion Notification Wait Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** RIO UDP completion wait 를 TCP RIO와 같은 IOCP/RIONotify 방식으로 바꿔 open 상태 hot path 의 `Task.Delay(1)` fallback 을 제거한다.

**Architecture:** `RioUdpEndpoint`가 receive/send `RioCompletionSignal`을 소유하고, UDP receive/send completion queue 를 notification pointer 로 생성한다. `WaitForUdpCompletionAsync(...)`는 dequeue-first 원칙을 유지하되 open 상태에서는 `RIONotify` arm + signal wait 를 사용하고, close-drain 에서만 bounded fallback 을 유지한다.

**Tech Stack:** C# 8.0, .NET 9.0, Windows RIO P/Invoke, xUnit, existing `RioCompletionPort`/`RioCompletionSignal`.

---

## Context

D115는 RIO UDP p99 16.7ms tail 과 open-loop residual loss 의 다음 후보를 UDP CQ IOCP/RIONotify wait parity 로 정했다.
현재 `RioTransport.WaitForUdpCompletionAsync(...)`는 bounded `Task.Yield()` 뒤 `Task.Delay(1)`로 fallback 하며, 이 delay 는 Windows timer quantum 때문에 약 16ms tail 로 보일 수 있다.
TCP RIO는 이미 `CreateCompletionQueue(..., signal.NotificationCompletionPointer)`와 `RIONotify` 기반 wait 를 사용한다.

## Files

- Modify: `src/Hps.Transport.Rio/RioUdpEndpoint.cs`
  - receive/send `RioCompletionSignal` owner 추가
  - UDP CQ notification pointer 생성
  - `ArmNotification(...)` helper 추가
  - receive/send drain 시 signal dispose 순서 정리
- Modify: `src/Hps.Transport.Rio/RioTransport.cs`
  - `BindUdpAsync(...)`에서 `GetOrCreateCompletionPort()`를 endpoint 에 전달
  - `WaitForUdpCompletionAsync(...)` signature 와 호출부 수정
  - open 상태 `Task.Delay(1)` fallback 제거
- Modify: `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`
  - UDP endpoint notification resource shape Red test 추가
  - 기존 receive/send/close tests 로 behavior regression 검증
- Modify: `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`
  - Task별 결과와 D116 판단 기록

---

## Task 1: UDP endpoint notification resource shape

**Files:**
- Modify: `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`
- Modify: `src/Hps.Transport.Rio/RioUdpEndpoint.cs`
- Modify: `src/Hps.Transport.Rio/RioTransport.cs`

- [ ] **Step 1: Write the failing shape test**

Add `using System.Reflection;` if it is not already present.

Add this test near the existing `BindUdpAsync_WhenRioDatagramAvailable_ReturnsEndpointWithLocalEndPoint` test:

```csharp
// UDP RIO completion wait 를 polling 이 아니라 IOCP notification 으로 바꾸려면 endpoint 가 receive/send signal 을 소유해야 한다.
// 이 테스트는 native wait path 를 바꾸기 전에 endpoint resource shape 가 TCP RIO와 같은 notification 기반으로 열렸는지 먼저 고정한다.
[Fact]
public async Task BindUdpAsync_WhenRioDatagramAvailable_CreatesUdpCompletionSignals()
{
    if (!IsRioDatagramAvailable())
        return;

    IUdpEndpoint? endpoint = null;
    using (RioTransport transport = new RioTransport())
    {
        await transport.StartAsync();

        try
        {
            endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
            RioUdpEndpoint rioEndpoint = Assert.IsType<RioUdpEndpoint>(endpoint);

            PropertyInfo? receiveSignalProperty = typeof(RioUdpEndpoint).GetProperty(
                "ReceiveSignal",
                BindingFlags.Instance | BindingFlags.NonPublic);
            PropertyInfo? sendSignalProperty = typeof(RioUdpEndpoint).GetProperty(
                "SendSignal",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(receiveSignalProperty);
            Assert.NotNull(sendSignalProperty);
            Assert.NotNull(receiveSignalProperty!.GetValue(rioEndpoint));
            Assert.NotNull(sendSignalProperty!.GetValue(rioEndpoint));
        }
        finally
        {
            endpoint?.Close();
            await transport.StopAsync();
        }
    }
}
```

- [ ] **Step 2: Run the focused Red test**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~BindUdpAsync_WhenRioDatagramAvailable_CreatesUdpCompletionSignals"
```

Expected:

- On machines without RIO datagram support, the test returns early and cannot serve as Red evidence. In that case run without `--no-build` after adding the test and use compile success plus the next RIO-capable run for Red.
- On the current RIO-capable environment, fail with `Assert.NotNull()` because `RioUdpEndpoint.ReceiveSignal`/`SendSignal` do not exist.

- [ ] **Step 3: Add UDP endpoint signals and notification-backed CQs**

In `RioUdpEndpoint`:

1. Add two internal properties:

```csharp
internal RioCompletionSignal ReceiveSignal { get; }

internal RioCompletionSignal SendSignal { get; }
```

2. Change the constructor signature:

```csharp
internal RioUdpEndpoint(RioTransport transport, Socket socket, RioNative native, RioCompletionPort completionPort)
```

3. Validate `completionPort` and create signals before the native CQ creation:

```csharp
if (completionPort == null)
    throw new ArgumentNullException(nameof(completionPort));

ReceiveSignal = completionPort.CreateSignal();
SendSignal = completionPort.CreateSignal();
```

4. Change UDP CQ creation:

```csharp
ReceiveCompletionQueue = Native.CreateCompletionQueue(CompletionQueueSize, ReceiveSignal.NotificationCompletionPointer);
SendCompletionQueue = Native.CreateCompletionQueue(CompletionQueueSize, SendSignal.NotificationCompletionPointer);
```

5. In constructor failure cleanup, ensure created signals are disposed. The simplest shape is to initialize signals before the `try` block and let `DisposeAllNativeResourcesAfterConstructorFailure()` dispose them after CQ/resource cleanup.

6. In `CompleteReceiveDrain()` dispose `ReceiveSignal` after closing the receive CQ and releasing receive address resources.

7. In `CompleteSendDrain()` dispose `SendSignal` after closing the send CQ and disposing send-side native/cache resources.

In `RioTransport.BindUdpAsync(...)`, change endpoint construction:

```csharp
endpoint = new RioUdpEndpoint(this, socket, native, GetOrCreateCompletionPort());
```

- [ ] **Step 4: Run focused Green tests**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~RioTransportUdpTests"
```

Expected:

- All `RioTransportUdpTests` pass.
- No pool leak assertion failures.

- [ ] **Step 5: Commit Task 1**

Stage only the touched Task 1 files:

```powershell
git add src\Hps.Transport.Rio\RioUdpEndpoint.cs src\Hps.Transport.Rio\RioTransport.cs tests\Hps.Transport.Rio.Tests\RioTransportUdpTests.cs
git commit -m "fix: add rio udp completion signals"
```

---

## Task 2: UDP completion wait uses RIONotify

**Files:**
- Modify: `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`
- Modify: `src/Hps.Transport.Rio/RioUdpEndpoint.cs`
- Modify: `src/Hps.Transport.Rio/RioTransport.cs`

- [ ] **Step 1: Write the failing wait-shape test**

Add this test near the signal shape test:

```csharp
// UDP wait path 가 TCP RIO처럼 notification arm helper 를 가져야 hot path 에서 Task.Delay fallback 을 제거할 수 있다.
// 현재 구현은 bounded yield/delay polling 이므로 이 helper shape 가 없어 Red 단계에서 잡힌다.
[Fact]
public void RioUdpEndpoint_WhenNotificationWaitIsExpected_ExposesArmNotificationHelper()
{
    MethodInfo? method = typeof(RioUdpEndpoint).GetMethod(
        "ArmNotification",
        BindingFlags.Instance | BindingFlags.NonPublic);

    Assert.NotNull(method);
}
```

- [ ] **Step 2: Run the focused Red test**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~RioUdpEndpoint_WhenNotificationWaitIsExpected_ExposesArmNotificationHelper"
```

Expected:

- Fail with `Assert.NotNull()` because `RioUdpEndpoint.ArmNotification(...)` does not exist.

- [ ] **Step 3: Add UDP notification arm helper**

In `RioUdpEndpoint`, add:

```csharp
internal void ArmNotification(IntPtr completionQueue, RioCompletionSignal signal)
{
    if (signal == null)
        throw new ArgumentNullException(nameof(signal));

    lock (_completionGate)
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(RioUdpEndpoint));

        if (!signal.TryArmNotification())
            return;

        int notifyResult = Native.Notify(completionQueue);
        if (notifyResult == 0)
            return;

        const int WsaEAlready = 10037;
        if (notifyResult == WsaEAlready)
            return;

        signal.MarkNotificationArmFailed();
        throw new SocketException(notifyResult);
    }
}
```

This mirrors `RioConnectionResource.ArmNotification(...)`.

- [ ] **Step 4: Change `WaitForUdpCompletionAsync(...)`**

Change signature:

```csharp
private static async Task<RioResult> WaitForUdpCompletionAsync(
    RioUdpEndpoint endpoint,
    IntPtr completionQueue,
    RioCompletionSignal signal,
    bool allowAfterClose)
```

Change call sites:

```csharp
RioResult completion = await WaitForUdpCompletionAsync(
    endpoint,
    endpoint.ReceiveCompletionQueue,
    endpoint.ReceiveSignal,
    allowAfterClose: true).ConfigureAwait(false);
```

```csharp
RioResult completion = await WaitForUdpCompletionAsync(
    endpoint,
    endpoint.SendCompletionQueue,
    endpoint.SendSignal,
    allowAfterClose: false).ConfigureAwait(false);
```

Replace the open-state delay/yield loop with this shape:

```csharp
RioResult[] results = new RioResult[1];
int closeDelayAttempts = 0;

while (true)
{
    if (endpoint.IsDisposed)
        throw new ObjectDisposedException(nameof(RioUdpEndpoint));

    uint completed = endpoint.DequeueCompletion(completionQueue, results);
    if (completed != 0)
        return results[0];

    if (endpoint.IsClosed)
    {
        if (!allowAfterClose)
            throw new ObjectDisposedException(nameof(RioUdpEndpoint));

        if (closeDelayAttempts < UdpCloseDrainDelayBudget)
        {
            closeDelayAttempts++;
            await Task.Delay(1).ConfigureAwait(false);
            continue;
        }

        throw new ObjectDisposedException(nameof(RioUdpEndpoint));
    }

    endpoint.ArmNotification(completionQueue, signal);
    await signal.WaitAsync().ConfigureAwait(false);
}
```

Do not keep `Task.Delay(1)` in the endpoint-open path. The close fallback stays because D114 close-drain may need a bounded cleanup exit when socket close does not surface a completion.

- [ ] **Step 5: Run focused RIO UDP tests**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~RioTransportUdpTests"
```

Expected:

- All `RioTransportUdpTests` pass.

- [ ] **Step 6: Run full RIO tests**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore
```

Expected:

- All RIO tests pass.

- [ ] **Step 7: Run solution build/test**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
```

Expected:

- Build warning 0/error 0.
- All tests pass.

- [ ] **Step 8: Commit Task 2**

```powershell
git add src\Hps.Transport.Rio\RioUdpEndpoint.cs src\Hps.Transport.Rio\RioTransport.cs tests\Hps.Transport.Rio.Tests\RioTransportUdpTests.cs
git commit -m "fix: wait for rio udp completions with notifications"
```

---

## Task 3: Scratch benchmark and D116 decision

**Files:**
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/decisions/2026-06.md`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Ignored scratch output: `artifacts/benchmarks/rio-udp/2026-06-26/session-03/rio/`

- [ ] **Step 1: Run RIO UDP scratch baseline suite**

Run:

```powershell
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --baseline-suite artifacts\benchmarks\rio-udp\2026-06-26\session-03\rio --runs 1 --protocol udp --backend rio
```

Expected:

- Creates `load-01.json` and `open-loop-01.json`.
- Exit code may be 0 or 1. Do not treat exit code 1 as command failure if raw reports are written; it means hard gate failed and must be recorded.

- [ ] **Step 2: Generate summary artifacts**

Run:

```powershell
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --summarize-baseline artifacts\benchmarks\rio-udp\2026-06-26\session-03\rio --summary artifacts\benchmarks\rio-udp\2026-06-26\session-03\rio\summary.json --summary-md artifacts\benchmarks\rio-udp\2026-06-26\session-03\rio\summary.md
```

Expected:

- Creates `summary.json` and `summary.md`.
- Exit code follows hard-passed status.

- [ ] **Step 3: Extract metrics**

Run:

```powershell
$dir = 'artifacts\benchmarks\rio-udp\2026-06-26\session-03\rio'
foreach ($name in @('load-01.json','open-loop-01.json')) {
    $r = Get-Content -Raw (Join-Path $dir $name) | ConvertFrom-Json
    [pscustomobject]@{
        file = $name
        sent = $r.sent
        received = $r.received
        dropped = $r.dropped
        payloadErrors = $r.'payload-errors'
        poolRented = $r.'pool-rented'
        actualRateHz = $r.'actual-rate-hz'
        p50 = $r.'p50-latency-us'
        p99 = $r.'p99-latency-us'
        udpHwm = $r.'udp-pending-send-queue-high-watermark'
        passed = $r.passed
    } | Format-List
}
$s = Get-Content -Raw (Join-Path $dir 'summary.json') | ConvertFrom-Json
[pscustomobject]@{
    hardPassed = $s.'hard-passed'
    warningCount = $s.'warning-count'
    sourceReportCount = $s.'source-report-count'
} | Format-List
```

- [ ] **Step 4: Record D116 or follow-up**

Decision rule:

- If load/open-loop p99 tail drops materially below the old 16.7ms tail and open-loop receives all 3000 messages, add D116 accepted: UDP IOCP wait closes RIO UDP completion tail and open-loop hard gate.
- If p99 improves but open-loop still loses messages, add D116 partial: IOCP wait fixed wake tail, but receive depth or registration reuse remains.
- If p99 stays near 16.7ms, do not accept D116 as a fix. Record a P1 follow-up for additional trace instrumentation before more code changes.

- [ ] **Step 5: Update state docs**

Update:

- `DECISIONS.md`
- `docs/agent-state/decisions/2026-06.md`
- `CURRENT_PLAN.md`
- `TODOS.md`
- `CHANGELOG_AGENT.md`

Keep scratch artifacts under `artifacts/` ignored and do not stage them.

- [ ] **Step 6: Verify docs/build/test**

Run:

```powershell
git diff --check
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
```

Expected:

- `git diff --check` has no whitespace errors.
- Build warning 0/error 0.
- All tests pass.

- [ ] **Step 7: Commit Task 3**

```powershell
git add DECISIONS.md docs\agent-state\decisions\2026-06.md CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "docs: record rio udp completion wait benchmark"
```

---

## Self-review

- D115 coverage: endpoint signal ownership, notification-backed CQ creation, open-state wait path, close-drain fallback, scratch benchmark 재측정을 모두 task 로 분리했다.
- Scope control: receive depth, receive registration reuse, IPv6, default backend promotion, latency hard gate 는 제외했다.
- TDD: Task 1/2는 reflection shape Red 로 compile failure 대신 assertion failure 를 만든 뒤 구현한다.
- Commit boundary: Task 1 signal resource, Task 2 wait behavior, Task 3 benchmark/docs 를 별도 커밋으로 유지한다.
