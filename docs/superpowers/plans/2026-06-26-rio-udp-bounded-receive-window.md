# 2026-06-26 RIO UDP Bounded Receive Window Implementation Plan

## Goal

RIO UDP open-loop delivery loss 를 줄이기 위해 D117의 bounded receive slot window 를 구현한다.
첫 구현은 receive depth 2, request-context slot mapping, slot-local remote address buffer 로 제한한다.

## Architecture

- `RioUdpEndpoint` request queue receive depth 를 2로 올린다.
- `UdpReceiveLoopAsync(...)`는 startup 에 두 receive slot 을 post 한다.
- 각 slot 은 stable slot id 를 갖고, `RIOReceiveEx(..., requestContext)`에 `slotId + 1` 값을 넣는다.
- completion 은 `RioResult.RequestContext`로 slot 에 매핑한다.
- slot-local remote address block 은 slot lifetime 동안 등록해 재사용한다.
- payload data buffer 는 D113대로 datagram 마다 등록하고 completion 직후 deregister 한 뒤 handler 로 넘긴다.
- handler dispatch 는 계속 직렬이다. 이 구현은 UDP reliability/ordering 보장이 아니라 posted receive window 확장이다.

## Current Evidence

- D116 RIO `session-03/load`: sent/received 3000/3000, p99 481 us.
- D116 RIO `session-03/open-loop`: sent/received 3000/2373, p99 647.6 us.
- D116 SAEA `session-01/open-loop`: sent/received 3000/3000, p99 852.2 us.
- RIO send queue HWM 2, dropped 0 이므로 send-side drop 보다 receive-side window 가 다음 후보다.

## Task 1: depth-2 receive behavior

**Files:**
- Modify: `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`
- Modify: `src/Hps.Transport.Rio/RioTransport.cs`
- Modify: `src/Hps.Transport.Rio/RioUdpEndpoint.cs`
- Modify: root state docs

- [x] **Step 1: Write blocked-handler burst Red test**

Add a focused test near `UdpReceive_WhenHandlerIsBlocked_PrePostsOneAdditionalReceive`:

```csharp
// RIO UDP bounded receive window 는 첫 handler 가 막힌 동안 두 개의 추가 datagram 을 outstanding receive 로 받아야 한다.
// one-deep 구현은 blocked handler 중 추가 한 개까지만 안정적으로 보존하므로 depth 2 전환의 Red 근거가 된다.
[Fact]
public async Task UdpReceive_WhenHandlerIsBlocked_PreservesTwoQueuedDatagramsWithBoundedWindow()
{
    if (!IsRioDatagramAvailable())
        return;

    using (RioTransport transport = new RioTransport())
    {
        BlockingFirstDatagramHandler datagramHandler = new BlockingFirstDatagramHandler();
        transport.SetDatagramHandler(datagramHandler);
        await transport.StartAsync();

        IUdpEndpoint? endpoint = null;
        Socket? sender = null;

        try
        {
            endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
            RioUdpEndpoint rioEndpoint = Assert.IsType<RioUdpEndpoint>(endpoint);
            IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(endpoint.LocalEndPoint);

            sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            await SendSingleByteDatagramAsync(sender, boundEndPoint, 101);
            await WaitForSignalAsync(datagramHandler.FirstReceivedTask);
            Assert.Equal(1, datagramHandler.ReceivedCount);

            await SendSingleByteDatagramAsync(sender, boundEndPoint, 102);
            await SendSingleByteDatagramAsync(sender, boundEndPoint, 103);

            await WaitForRentedCountAsync(rioEndpoint.ReceivePool, 3);
            Assert.Equal(1, datagramHandler.ReceivedCount);

            datagramHandler.AllowFirstDatagramToComplete();
            await WaitForReceivedCountAsync(datagramHandler, 3);

            endpoint.Close();
            endpoint = null;
            await WaitForRentedCountAsync(rioEndpoint.ReceivePool, 0);
        }
        finally
        {
            datagramHandler.AllowFirstDatagramToComplete();
            sender?.Dispose();
            endpoint?.Close();
            await transport.StopAsync();
        }
    }
}
```

If helpers do not exist, add small test-local helpers:

- `SendSingleByteDatagramAsync(Socket, EndPoint, byte)`
- `WaitForReceivedCountAsync(BlockingFirstDatagramHandler, int)`

- [x] **Step 2: Run focused Red**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~UdpReceive_WhenHandlerIsBlocked_PreservesTwoQueuedDatagramsWithBoundedWindow"
```

Expected:

- Fails by timeout or `Expected: 3` style assertion because current one-deep model cannot preserve two queued datagrams while handler is blocked.

Actual Red:

- Failed with `Assert.Equal() Failure: Values differ`, `Expected: 3`, `Actual: 2` at `WaitForRentedCountAsync(...)`.

- [x] **Step 3: Implement receive slot window**

Implementation shape:

```csharp
private const int MaxOutstandingReceive = 2;
```

Introduce receive slot owner inside `RioTransport` or as a nested type:

```csharp
private sealed class RioUdpReceiveSlot : IDisposable
{
    private readonly RioUdpEndpoint _endpoint;
    private readonly int _slotId;
    private byte[]? _remoteAddressBlock;
    private IntPtr _remoteAddressBufferId;
    private RefCountedBuffer? _datagram;
    private IntPtr _receiveBufferId;

    internal ulong RequestContext { get; }

    internal void Post();
    internal ReceivedRioUdpDatagram Complete(RioResult completion);
    public void Dispose();
}
```

Rules:

- `RequestContext = (ulong)(slotId + 1)`.
- `Post()` rents a fresh `RefCountedBuffer`, registers its backing data array, and calls `ReceiveEx` with slot request context.
- `Complete(...)` validates status/length, decodes from the slot-local remote address block, deregisters data buffer, clears `_datagram`, and returns ownership to the caller.
- Slot dispose deregisters current data buffer if still posted, releases current datagram if still owned, deregisters remote address buffer, and returns the address block to the pool.

Receive loop sketch:

```csharp
RioUdpReceiveSlot[] slots = CreateReceiveSlots(endpoint, MaxOutstandingReceive);
PostInitialSlots(slots);

while (true)
{
    RioResult completion = await WaitForUdpCompletionAsync(...);
    RioUdpReceiveSlot slot = FindSlot(slots, completion.RequestContext);
    ReceivedRioUdpDatagram received = slot.Complete(completion);

    if (!endpoint.IsClosed)
        slot.Post();

    DispatchDatagramReceived(...);
}
```

Do not use a shared endpoint remote address block for receive completions once depth is greater than 1.

- [x] **Step 4: Run focused Green tests**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~UdpReceive_WhenHandlerIsBlocked_PreservesTwoQueuedDatagramsWithBoundedWindow"
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~RioTransportUdpTests"
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore
```

Expected:

- New focused test passes.
- Existing one-deep tests may need wording/count updates from one-deep to bounded window.
- Full RIO tests pass.

Actual:

- `UdpReceive_WhenHandlerIsBlocked_PreservesTwoQueuedDatagramsWithBoundedWindow`: 1 passed.
- `RioTransportUdpTests`: 16 passed.
- `Hps.Transport.Rio.Tests`: 53 passed.

- [x] **Step 5: Update state docs and commit Task 1**

Update `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.

Commit:

```powershell
git add src\Hps.Transport.Rio\RioTransport.cs src\Hps.Transport.Rio\RioUdpEndpoint.cs tests\Hps.Transport.Rio.Tests\RioTransportUdpTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md docs\superpowers\plans\2026-06-26-rio-udp-bounded-receive-window.md
git commit -m "fix: add rio udp bounded receive window"
```

Actual verification:

- `git diff --check`: passed.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: warning 0/error 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 334 passed.

## Task 2: close/drain cleanup hardening

**Files:**
- Modify: `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`
- Modify: `src/Hps.Transport.Rio/RioTransport.cs`
- Modify: root state docs

- [x] **Step 1: Add depth-aware cleanup Red tests**

Add/adjust tests:

- `UdpReceive_WhenEndpointClosesWithBoundedReceiveWindow_ReleasesOutstandingReceives`
- `UdpReceive_WhenHandlerThrowsWithBoundedReceiveWindow_ReleasesOutstandingReceivesAndNotifiesOnce`

Expected assertions:

- While first handler is blocked, `ReceivePool.RentedCount` can reach 3: handler-owned current datagram + two posted receive slots.
- After close or handler exception cleanup, `ReceivePool.RentedCount == 0`.
- close notification remains once.

Actual:

- Task 1에서 기존 close/handler-exception tests 의 rented count 기대값과 주석을 depth 2 정책으로 조정했다.
- 별도 production 변경 없이 focused cleanup tests 2개가 통과했다.

- [x] **Step 2: Implement explicit slot cleanup**

Rules:

- Dispose all receive slots in `finally`.
- If a completed datagram has been produced but not dispatched, release it in receive loop cleanup.
- If endpoint is closed, do not post replacement receives.
- Invalid or zero `RequestContext` should close endpoint via `SocketException` path, not corrupt slot ownership.

Actual:

- Task 1 `RioUdpReceiveSlot.Dispose()`가 data registration, slot-local remote address registration, outstanding datagram ref 를 모두 정리한다.
- receive loop `finally`가 slot 배열을 dispose 한 뒤 endpoint receive CQ를 닫는다.

- [x] **Step 3: Verify and commit Task 2**

Run focused tests, full RIO tests, solution build/test.

Actual:

- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~UdpReceive_WhenEndpointClosesWithPrePostedReceive|FullyQualifiedName~UdpReceive_WhenHandlerThrowsWithPrePostedReceive"`: 2 passed.
- 별도 commit 은 만들지 않고 Task 1 commit `0a03a17`에 포함된 cleanup 구현으로 닫는다.

Commit:

```powershell
git commit -m "test: harden rio udp bounded receive cleanup"
```

## Task 3: scratch benchmark and D118

**Files:**
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/decisions/2026-06.md`
- Modify: root state docs
- Ignored scratch output: `artifacts/benchmarks/rio-udp/2026-06-26/session-04/rio/`

- [x] **Step 1: Run benchmark**

```powershell
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --baseline-suite artifacts\benchmarks\rio-udp\2026-06-26\session-04\rio --runs 1 --protocol udp --backend rio
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --summarize-baseline artifacts\benchmarks\rio-udp\2026-06-26\session-04\rio --summary artifacts\benchmarks\rio-udp\2026-06-26\session-04\rio\summary.json --summary-md artifacts\benchmarks\rio-udp\2026-06-26\session-04\rio\summary.md
```

Actual:

- baseline suite exit 0, `baseline-suite-result: pass`.
- summary exit 0, `hard-passed: true`, `warning-count: 0`, `source-report-count: 2`.
- RIO `session-04/load`: sent/received 3000/3000, dropped 0, payload-errors 0, pool-rented 0, actual-rate 99.7 Hz, p50 245.5 us, p99 831.8 us, UDP HWM 1, passed true.
- RIO `session-04/open-loop`: sent/received 3000/3000, dropped 0, payload-errors 0, pool-rented 0, actual-rate 100 Hz, p50 250.4 us, p99 889.4 us, UDP HWM 2, passed true.

- [x] **Step 2: Decide D118**

Decision rule:

- accepted: open-loop sent/received 3000/3000 and p99 remains near 1ms or below.
- partial: delivery improves but remains below 3000.
- rejected as fix: delivery does not improve materially.

Actual:

- D118 accepted. Bounded receive window closes the RIO UDP open-loop delivery hard gate for the current 4096B x 100Hz scratch benchmark.

- [x] **Step 3: Verify and commit Task 3**

Run `git diff --check`, solution build/test, commit benchmark decision docs.

Actual:

- `git diff --check`: passed.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: warning 0/error 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 334 passed.

## Out of Scope

- UDP reliability/ordering/ack/retry.
- configurable public receive depth.
- payload receive registration reuse that keeps handler-owned datagram registered.
- `TransportFactory` default promotion.
- latency hard gate or CI warning-as-failure.

## Self-review Checklist

- D113 payload registration handoff is preserved.
- D114 close-safe drain is preserved.
- D116 notification wait path is preserved.
- No handler parallel dispatch is introduced.
- Scratch artifacts remain ignored.
- Each implementation task has Red evidence before production changes.
