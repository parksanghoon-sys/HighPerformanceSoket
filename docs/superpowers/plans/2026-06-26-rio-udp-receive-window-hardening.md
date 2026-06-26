# RIO UDP Receive Window Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** RIO UDP receive loop 를 one-deep pre-post 로 바꿔 handler dispatch 중 receive-not-armed window 를 줄이고, close/handler exception 경로에서도 outstanding receive operation 을 누수 없이 정리한다.

**Architecture:** `RioUdpEndpoint.Close()`는 shutdown 요청만 수행하고 receive CQ/address registration 은 receive loop drain 이후 닫는다. receive loop 는 `RioUdpReceiveOperation` owner 를 통해 `RefCountedBuffer`와 data buffer id 를 정확히 1회 정리하고, remote address block 은 endpoint lifetime shared block 을 유지하되 completion 직후 managed `EndPoint`로 decode 한 뒤 다음 receive 를 post 한다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, Windows Registered I/O `RIOReceiveEx`/`RIOSendEx`, 기존 `PinnedBlockMemoryPool`/`RefCountedBuffer`.

## Global Constraints

- TFM 은 `net9.0`, LangVersion 은 C# 8.0 유지.
- 새 NuGet dependency 를 추가하지 않는다.
- 문서와 주석은 한국어로 작성한다.
- 테스트에는 무엇을 검증하는지와 왜 필요한지 한국어 주석을 남긴다.
- 구현은 Red-Green-Refactor 순서로 진행한다.
- `TransportFactory.CreateDefault()`는 계속 `SaeaTransport`를 반환한다.
- RIO unavailable fallback/default selection, IPv6 UDP, bounded receive depth, handler 병렬 dispatch 는 이번 범위에서 제외한다.
- `DECISIONS.md`의 D111은 구현 수락 뒤 D114로 supersede 한다. 구현 전에는 D111/D113을 유지한다.

---

## File Structure

- Modify: `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`
  - RIO UDP one-deep receive behavior, close cleanup, handler exception cleanup 을 검증한다.
  - 기존 no-prefetch 테스트는 D111용 의미가 사라지므로 one-deep pre-post 테스트로 교체한다.
- Modify: `src/Hps.Transport.Rio/RioTransport.cs`
  - `UdpReceiveLoopAsync(RioUdpEndpoint endpoint)`를 one-deep pre-post 흐름으로 바꾼다.
  - `RioUdpReceiveOperation` private owner 를 추가한다.
  - `WaitForUdpCompletionAsync(RioUdpEndpoint endpoint, IntPtr completionQueue, bool allowAfterClose)`에 close 이후 receive drain 을 허용하는 경로를 추가한다.
- Modify: `src/Hps.Transport.Rio/RioUdpEndpoint.cs`
  - public `Close()`를 shutdown requester 로 제한한다.
  - receive-side native resource 와 send-side native resource 정리 메서드를 분리한다.
  - constructor 실패 경로는 loop 가 없으므로 기존처럼 즉시 native resource 를 모두 정리하는 별도 메서드를 둔다.
- Modify after implementation acceptance: `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`
  - D114를 추가하고 D111 no-prefetch 정책 supersede 를 기록한다.
- Modify after implementation: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`
  - Task 1 구현 결과, Red/Green evidence, 남은 benchmark/doc step 을 기록한다.

---

## Task 1: Close-safe one-deep RIO UDP receive loop

**Files:**
- Modify: `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`
- Modify: `src/Hps.Transport.Rio/RioTransport.cs`
- Modify: `src/Hps.Transport.Rio/RioUdpEndpoint.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - `RioUdpEndpoint.ReceivePool`
  - `RioUdpEndpoint.RemoteAddressSegment`
  - `RioUdpEndpoint.RemoteAddressBlock`
  - `RioUdpEndpoint.ReceiveCompletionQueue`
  - `RioNative.ReceiveEx(IntPtr, RioBufferSegment?, RioBufferSegment?, RioBufferSegment?, IntPtr)`
  - `DispatchDatagramReceived(RioUdpEndpoint, EndPoint, RefCountedBuffer)`
- Produces:
  - `RioUdpEndpoint.RequestClose() : bool`
  - `RioUdpEndpoint.CompleteReceiveDrain() : void`
  - `RioUdpEndpoint.CompleteSendDrain() : void`
  - `RioUdpReceiveOperation.Post() : void`
  - `RioUdpReceiveOperation.Complete(RioResult) : ReceivedRioUdpDatagram`
  - `RioUdpReceiveOperation.Dispose() : void`

- [ ] **Step 1: Write the failing one-deep receive test**

Replace the current no-prefetch test in `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`.

```csharp
// RIO UDP one-deep pre-post 테스트: 첫 handler 가 막혀 있는 동안 receive loop 는 다음 ReceiveEx 를 미리 post 해야 한다.
// 그래야 handler dispatch 시간만큼 생기는 receive-not-armed window 가 줄고, blocked 중 들어온 두 번째 datagram 이 unblock 뒤 전달된다.
[Fact]
public async Task UdpReceive_WhenHandlerIsBlocked_PrePostsOneAdditionalReceive()
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

            byte[] firstPayload = new byte[] { 81 };
            int firstSent = await sender.SendToAsync(new ArraySegment<byte>(firstPayload), SocketFlags.None, boundEndPoint);
            Assert.Equal(firstPayload.Length, firstSent);

            await WaitForSignalAsync(datagramHandler.FirstReceivedTask);
            Assert.Equal(1, datagramHandler.ReceivedCount);

            byte[] secondPayload = new byte[] { 82 };
            int secondSent = await sender.SendToAsync(new ArraySegment<byte>(secondPayload), SocketFlags.None, boundEndPoint);
            Assert.Equal(secondPayload.Length, secondSent);

            await WaitForRentedCountAsync(rioEndpoint.ReceivePool, 2);
            Assert.Equal(1, datagramHandler.ReceivedCount);

            datagramHandler.AllowFirstDatagramToComplete();
            await WaitForSignalAsync(datagramHandler.SecondReceivedTask);

            Assert.Equal(2, datagramHandler.ReceivedCount);

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

- [ ] **Step 2: Write the failing close/handler cleanup tests**

Add these tests near the existing RIO UDP handler exception and close-drain tests.

```csharp
// RIO UDP close-drain 테스트: one-deep pre-post 상태에서는 handler 가 보유한 current datagram 과
// provider 에 post 된 next receive buffer 가 동시에 존재할 수 있다. Close 는 receive CQ를 즉시 닫지 말고
// receive loop owner 가 두 resource 를 모두 정리하게 해야 한다.
[Fact]
public async Task UdpReceive_WhenEndpointClosesWithPrePostedReceive_ReleasesOutstandingReceive()
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
            int sent = await sender.SendToAsync(new ArraySegment<byte>(new byte[] { 91 }), SocketFlags.None, boundEndPoint);
            Assert.Equal(1, sent);

            await WaitForSignalAsync(datagramHandler.FirstReceivedTask);
            await WaitForRentedCountAsync(rioEndpoint.ReceivePool, 2);

            endpoint.Close();
            endpoint = null;
            datagramHandler.AllowFirstDatagramToComplete();

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

// RIO UDP handler 예외 테스트: handler 호출 전에 이미 next receive 가 post 되었으므로,
// handler 예외로 endpoint close 로 수렴할 때 next operation 도 같은 receive-loop cleanup 경로에서 정리되어야 한다.
[Fact]
public async Task UdpReceive_WhenHandlerThrowsWithPrePostedReceive_ReleasesOutstandingReceiveAndNotifiesOnce()
{
    if (!IsRioDatagramAvailable())
        return;

    using (RioTransport transport = new RioTransport())
    {
        ThrowingAfterReleaseDatagramHandler datagramHandler = new ThrowingAfterReleaseDatagramHandler();
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
            int sent = await sender.SendToAsync(new ArraySegment<byte>(new byte[] { 92 }), SocketFlags.None, boundEndPoint);
            Assert.Equal(1, sent);

            IUdpEndpoint closedEndpoint = await WaitForClosedUdpEndpointAsync(datagramHandler.ClosedTask);

            Assert.Same(endpoint, closedEndpoint);
            await WaitForRioEndpointClosedAsync(rioEndpoint);
            await WaitForRentedCountAsync(rioEndpoint.ReceivePool, 0);
            Assert.Equal(1, datagramHandler.ClosedCallCount);
        }
        finally
        {
            sender?.Dispose();
            endpoint?.Close();
            await transport.StopAsync();
        }
    }
}
```

- [ ] **Step 3: Run Red**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~UdpReceive_WhenHandlerIsBlocked_PrePostsOneAdditionalReceive|FullyQualifiedName~UdpReceive_WhenEndpointClosesWithPrePostedReceive_ReleasesOutstandingReceive|FullyQualifiedName~UdpReceive_WhenHandlerThrowsWithPrePostedReceive_ReleasesOutstandingReceiveAndNotifiesOnce"
```

Expected Red:

- `UdpReceive_WhenHandlerIsBlocked_PrePostsOneAdditionalReceive` fails because current D111 no-prefetch loop keeps `ReceivePool.RentedCount == 1` and does not deliver the second datagram without sending a third datagram after unblock.

- [ ] **Step 4: Split endpoint close ownership**

Modify `src/Hps.Transport.Rio/RioUdpEndpoint.cs`.

Required field additions:

```csharp
private int _receiveResourcesDisposed;
private int _sendResourcesDisposed;
```

Replace constructor catch cleanup with a method that is only used before receive/send loops start:

```csharp
catch
{
    DisposeAllNativeResourcesAfterConstructorFailure();
    throw;
}
```

Replace public close with shutdown request semantics:

```csharp
public void Close()
{
    RequestClose();
}

internal bool RequestClose()
{
    if (Interlocked.Exchange(ref _closed, 1) != 0)
        return false;

    _socket.Dispose();
    DrainPendingSends();
    _sendSignal.Release();
    _transport.UnregisterUdpEndpoint(this);
    return true;
}
```

Add receive-side drain:

```csharp
internal void CompleteReceiveDrain()
{
    if (Interlocked.Exchange(ref _receiveResourcesDisposed, 1) != 0)
        return;

    lock (_completionGate)
    {
        IntPtr receiveCompletionQueue = ReceiveCompletionQueue;
        ReceiveCompletionQueue = IntPtr.Zero;
        if (receiveCompletionQueue != IntPtr.Zero)
            Native.CloseCompletionQueue(receiveCompletionQueue);
    }

    IntPtr remoteAddressBufferId = RemoteAddressBufferId;
    RemoteAddressBufferId = IntPtr.Zero;
    if (remoteAddressBufferId != IntPtr.Zero)
        Native.DeregisterBuffer(remoteAddressBufferId);

    byte[]? remoteAddressBlock = _remoteAddressBlock;
    _remoteAddressBlock = null;
    if (remoteAddressBlock != null)
        _remoteAddressPool.Return(remoteAddressBlock);

    TryMarkDisposed();
}
```

Add send-side drain:

```csharp
internal void CompleteSendDrain()
{
    if (Interlocked.Exchange(ref _sendResourcesDisposed, 1) != 0)
        return;

    lock (_completionGate)
    {
        IntPtr sendCompletionQueue = SendCompletionQueue;
        SendCompletionQueue = IntPtr.Zero;
        if (sendCompletionQueue != IntPtr.Zero)
            Native.CloseCompletionQueue(sendCompletionQueue);
    }

    IntPtr sendAddressBufferId = SendAddressBufferId;
    SendAddressBufferId = IntPtr.Zero;
    if (sendAddressBufferId != IntPtr.Zero)
        Native.DeregisterBuffer(sendAddressBufferId);

    byte[]? sendAddressBlock = _sendAddressBlock;
    _sendAddressBlock = null;
    if (sendAddressBlock != null)
        _sendAddressPool.Return(sendAddressBlock);

    PayloadRegistrationCache.Dispose();
    _sendSignal.Dispose();
    TryMarkDisposed();
}
```

Add final disposed marker:

```csharp
private void TryMarkDisposed()
{
    if (Volatile.Read(ref _receiveResourcesDisposed) == 0)
        return;

    if (Volatile.Read(ref _sendResourcesDisposed) == 0)
        return;

    Interlocked.Exchange(ref _disposed, 1);
}
```

Keep constructor-failure cleanup immediate:

```csharp
private void DisposeAllNativeResourcesAfterConstructorFailure()
{
    RequestClose();
    CompleteReceiveDrain();
    CompleteSendDrain();
}
```

Important adjustment:

- `RequestClose()` may call `_sendSignal.Release()` after `CompleteSendDrain()` disposed it during a constructor failure path. Guard constructor failure by calling `DisposeAllNativeResourcesAfterConstructorFailure()` only before the endpoint is published and before any external `Close()` can race.
- `IsDisposed` must not become true until both receive and send side drains complete. `WaitForUdpCompletionAsync(RioUdpEndpoint endpoint, IntPtr completionQueue, bool allowAfterClose)` uses this value as a native handle disposal guard.

- [ ] **Step 5: Add receive operation owner and one-deep loop**

Modify `src/Hps.Transport.Rio/RioTransport.cs`.

Add helper result type:

```csharp
private sealed class ReceivedRioUdpDatagram
{
    internal ReceivedRioUdpDatagram(RefCountedBuffer datagram, EndPoint remoteEndPoint)
    {
        Datagram = datagram;
        RemoteEndPoint = remoteEndPoint;
    }

    internal RefCountedBuffer Datagram { get; }

    internal EndPoint RemoteEndPoint { get; }
}
```

Add operation owner:

```csharp
private sealed class RioUdpReceiveOperation : IDisposable
{
    private readonly RioUdpEndpoint _endpoint;
    private RefCountedBuffer? _datagram;
    private IntPtr _receiveBufferId;
    private bool _posted;

    internal RioUdpReceiveOperation(RioUdpEndpoint endpoint)
    {
        _endpoint = endpoint;
        _datagram = endpoint.ReceivePool.RentCounted();
        _receiveBufferId = IntPtr.Zero;
        _posted = false;
    }

    internal void Post()
    {
        RefCountedBuffer datagram = RequireDatagram();
        ArraySegment<byte> receiveSegment = GetRefCountedBlockSegment(datagram, 0, _endpoint.ReceivePool.BlockSize);
        if (receiveSegment.Array == null)
            throw new InvalidOperationException("RIO UDP receive 는 pinned byte[] 기반 RefCountedBuffer 만 지원합니다.");

        _receiveBufferId = RegisterPinnedArray(_endpoint.Native, receiveSegment.Array);
        RioBufferSegment dataSegment = new RioBufferSegment(_receiveBufferId, receiveSegment.Offset, receiveSegment.Count);
        RioBufferSegment remoteAddressSegment = _endpoint.RemoteAddressSegment;

        if (!_endpoint.Native.ReceiveEx(_endpoint.RequestQueue, dataSegment, null, remoteAddressSegment, IntPtr.Zero))
            throw new SocketException((int)SocketError.ConnectionReset);

        _posted = true;
    }

    internal ReceivedRioUdpDatagram Complete(RioResult completion)
    {
        RefCountedBuffer datagram = RequireDatagram();

        if (completion.Status != 0 || completion.BytesTransferred > _endpoint.ReceivePool.BlockSize)
            throw new SocketException((int)SocketError.ConnectionReset);

        datagram.SetLength(checked((int)completion.BytesTransferred));
        EndPoint remoteEndPoint = DecodeSockaddrInet(_endpoint.RemoteAddressBlock);

        ReleaseRegistration();

        _datagram = null;
        return new ReceivedRioUdpDatagram(datagram, remoteEndPoint);
    }

    public void Dispose()
    {
        ReleaseRegistration();

        RefCountedBuffer? datagram = _datagram;
        _datagram = null;
        if (datagram != null)
            datagram.Release();
    }

    private RefCountedBuffer RequireDatagram()
    {
        RefCountedBuffer? datagram = _datagram;
        if (datagram == null)
            throw new ObjectDisposedException(nameof(RioUdpReceiveOperation));

        return datagram;
    }

    private void ReleaseRegistration()
    {
        IntPtr receiveBufferId = _receiveBufferId;
        _receiveBufferId = IntPtr.Zero;
        if (receiveBufferId != IntPtr.Zero)
            _endpoint.Native.DeregisterBuffer(receiveBufferId);
    }
}
```

The `_posted` field is allowed as a debugging/readability marker but must not drive ownership release. Ownership is controlled by `_datagram` and `_receiveBufferId`.

Replace `UdpReceiveLoopAsync(RioUdpEndpoint endpoint)` with this flow:

```csharp
private async Task UdpReceiveLoopAsync(RioUdpEndpoint endpoint)
{
    RioUdpReceiveOperation? current = null;
    RioUdpReceiveOperation? next = null;
    ReceivedRioUdpDatagram? received = null;

    try
    {
        current = new RioUdpReceiveOperation(endpoint);
        current.Post();

        while (true)
        {
            RioResult completion = await WaitForUdpCompletionAsync(
                endpoint,
                endpoint.ReceiveCompletionQueue,
                allowAfterClose: true).ConfigureAwait(false);

            received = current.Complete(completion);
            current.Dispose();
            current = null;

            if (!endpoint.IsClosed)
            {
                next = new RioUdpReceiveOperation(endpoint);
                next.Post();
            }

            try
            {
                RefCountedBuffer dispatchDatagram = received.Datagram;
                EndPoint dispatchRemoteEndPoint = received.RemoteEndPoint;
                received = null;
                DispatchDatagramReceived(endpoint, dispatchRemoteEndPoint, dispatchDatagram);
            }
            catch
            {
                next?.Dispose();
                next = null;
                NotifyUdpEndpointClosed(endpoint);
                return;
            }

            if (endpoint.IsClosed)
            {
                next?.Dispose();
                next = null;
                return;
            }

            current = next;
            next = null;
        }
    }
    catch (ObjectDisposedException)
    {
        if (received != null)
            received.Datagram.Release();

        current?.Dispose();
        next?.Dispose();
        return;
    }
    catch (SocketException)
    {
        if (received != null)
            received.Datagram.Release();

        current?.Dispose();
        next?.Dispose();
        NotifyUdpEndpointClosed(endpoint);
        return;
    }
    catch
    {
        if (received != null)
            received.Datagram.Release();

        current?.Dispose();
        next?.Dispose();
        NotifyUdpEndpointClosed(endpoint);
        return;
    }
    finally
    {
        if (received != null)
            received.Datagram.Release();

        current?.Dispose();
        next?.Dispose();
        endpoint.CompleteReceiveDrain();
    }
}
```

Update completion wait signature:

```csharp
private static async Task<RioResult> WaitForUdpCompletionAsync(
    RioUdpEndpoint endpoint,
    IntPtr completionQueue,
    bool allowAfterClose)
```

Rules:

- If `allowAfterClose` is false, keep the current send-loop behavior: closed endpoint throws.
- If `allowAfterClose` is true, do not throw solely because `endpoint.IsClosed` is true. Continue dequeue attempts until either a completion appears or endpoint native resources are disposed.
- The method must attempt `endpoint.DequeueCompletion(completionQueue, results)` before checking close state. That lets a socket-close-induced terminal completion drain normally.
- If `allowAfterClose` is true and the endpoint is closed but no completion appears after a bounded close-drain wait, throw `ObjectDisposedException`. The receive loop catch/finally then disposes the current `RioUdpReceiveOperation` and closes receive-side resources. Use the existing `UdpCompletionYieldBudget` for yield attempts and add a small `UdpCloseDrainDelayBudget` constant for delayed attempts.
- Keep `endpoint.IsDisposed` as a hard stop because the CQ handle may already be closed.

Update send loop to close send-side native resources after it exits:

```csharp
private async Task UdpSendLoopAsync(RioUdpEndpoint endpoint)
{
    try
    {
        while (true)
        {
            await endpoint.WaitForSendSignalAsync().ConfigureAwait(false);

            while (endpoint.TryBeginSend(out RioUdpEndpoint.UdpSendRequest sendRequest))
            {
                await SendUdpDatagramAsync(endpoint, sendRequest.RemoteEndPoint, sendRequest.SendBuffer).ConfigureAwait(false);
            }

            if (endpoint.IsClosed)
                return;
        }
    }
    finally
    {
        endpoint.CompleteSendDrain();
    }
}
```

Update send completion waits:

```csharp
RioResult completion = await WaitForUdpCompletionAsync(
    endpoint,
    endpoint.SendCompletionQueue,
    allowAfterClose: false).ConfigureAwait(false);
```

Update `NotifyUdpEndpointClosed(RioUdpEndpoint endpoint)`:

```csharp
private void NotifyUdpEndpointClosed(RioUdpEndpoint endpoint)
{
    if (!endpoint.RequestClose())
        return;

    ITransportDatagramHandler? datagramHandler = ReadDatagramHandlerSnapshot();
    if (datagramHandler != null)
        datagramHandler.OnDatagramEndpointClosed(endpoint);
}
```

- [ ] **Step 6: Run Green**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~UdpReceive_WhenHandlerIsBlocked_PrePostsOneAdditionalReceive|FullyQualifiedName~UdpReceive_WhenEndpointClosesWithPrePostedReceive_ReleasesOutstandingReceive|FullyQualifiedName~UdpReceive_WhenHandlerThrowsWithPrePostedReceive_ReleasesOutstandingReceiveAndNotifiesOnce"
```

Expected Green:

- Focused tests pass.
- `RioUdpEndpoint.ReceivePool.RentedCount` reaches 0 after close and handler exception.
- Handler exception close notification count is 1.

- [ ] **Step 7: Refactor and run broader RIO UDP tests**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~RioTransportUdpTests"
```

Expected:

- All RIO UDP tests pass.

Refactor checks:

- Keep `RioUdpReceiveOperation` close to `UdpReceiveLoopAsync(RioUdpEndpoint endpoint)` in `RioTransport.cs`.
- Remove stale comments that still describe RIO UDP receive as no-prefetch.
- Keep comments focused on ownership, close drain, and decode-before-next-post.

- [ ] **Step 8: Verify and commit Task 1**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build
git diff --check
```

Update state docs:

- `CURRENT_PLAN.md`: Task 1 complete, next step benchmark scratch rerun and D114 docs.
- `TODOS.md`: move Task 1 to Completed and set Current TODO to benchmark/docs acceptance step.
- `CHANGELOG_AGENT.md`: record Red evidence, focused test count, solution build/test result.

Commit:

```powershell
git add src/Hps.Transport.Rio/RioTransport.cs src/Hps.Transport.Rio/RioUdpEndpoint.cs tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "fix: prepost rio udp receives safely"
```

---

## Task 2: RIO UDP benchmark rerun and D114 documentation

**Files:**
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/decisions/2026-06.md`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify or create ignored scratch artifacts under `artifacts/benchmarks/rio-udp/2026-06-26/session-02/`

**Interfaces:**
- Consumes:
  - Task 1 one-deep receive implementation.
  - Existing benchmark CLI `--baseline-suite <dir> --runs 1 --protocol udp --backend rio`.
- Produces:
  - D114 active decision entry.
  - Scratch benchmark evidence comparing RIO UDP after one-deep pre-post.

- [ ] **Step 1: Run RIO UDP scratch benchmark**

Run:

```powershell
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --baseline-suite artifacts\benchmarks\rio-udp\2026-06-26\session-02\rio --runs 1 --protocol udp --backend rio
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --summarize-baseline artifacts\benchmarks\rio-udp\2026-06-26\session-02\rio --summary artifacts\benchmarks\rio-udp\2026-06-26\session-02\rio\summary.json --summary-md artifacts\benchmarks\rio-udp\2026-06-26\session-02\rio\summary.md
```

Expected:

- `load-01.json` and `open-loop-01.json` are created.
- `summary.json` records `hard-passed` according to delivery/drop/leak gates.
- If open-loop still fails, do not force another code change in this task. Record exact sent/received/drop/p99 evidence and move completion wait or bounded receive depth to Deferred Backlog.

- [ ] **Step 2: Add D114 decision after implementation acceptance**

Add to `DECISIONS.md` active index near D113:

```markdown
- D114 — RIO UDP receive window 는 one-deep pre-post 로 줄이고, receive CQ/address resource 는 receive loop drain 이후 닫는다.
```

Add detailed D114 to `docs/agent-state/decisions/2026-06.md`:

```markdown
## D114 — RIO UDP receive window one-deep pre-post

- 날짜: 2026-06-26
- 상태: Accepted
- 결정: RIO UDP receive loop 는 handler dispatch 전에 다음 `RIOReceiveEx`를 1개만 pre-post 한다.
- 근거: D112 scratch artifact 에서 RIO UDP open-loop sent/received 3000/2263, payload-errors 0으로 delivery loss 가 확인됐다. D111 no-prefetch 는 pool ownership 은 단순하지만 handler dispatch 동안 receive-not-armed window 를 만든다.
- 소유권: `RioUdpReceiveOperation`이 `RefCountedBuffer`와 receive data buffer registration id 를 단일 소유하고, cleanup 은 receive loop task 가 수행한다. `RioUdpEndpoint.Close()`는 shutdown 요청만 수행하고 receive CQ/address registration 은 receive loop drain 이후 닫는다.
- remote address: endpoint lifetime shared remote address block 을 유지한다. completion 직후 managed `EndPoint`로 decode 하고 나서 next receive 를 post 하므로 depth 1에서는 shared block overwrite 가 dispatch 대상 remote endpoint 를 바꾸지 않는다.
- supersedes: D111의 no-prefetch receive 정책. D111의 blocked-window datagram retention 비보장 기록은 과거 기준으로 archive 에 유지한다.
- 유지: D113 receive registration 해제 시점과 8192B receive block size 는 유지한다.
```

- [ ] **Step 3: Update state docs**

Update:

- `CURRENT_PLAN.md`: Task 2 benchmark and D114 docs complete. Next candidate is either RIO UDP benchmark evidence review or follow-up hardening if open-loop still fails.
- `TODOS.md`: mark receive window hardening complete if hard gate passes. If not, add Deferred Backlog item:

```markdown
- [ ] `P1_SOON` RIO UDP open-loop residual loss/tail after one-deep pre-post 를 재평가한다.
  - 무엇이 남았는지: one-deep pre-post 이후에도 RIO UDP open-loop delivery 또는 p99 tail 이 목표에 미달하면 bounded receive depth 또는 UDP completion wait 개선을 별도 설계해야 한다.
  - 왜 defer 되었는지: first implementation 은 handler 병렬 호출 없이 receive-not-armed window 만 줄이는 범위로 제한했다.
  - objective: 4096B x 100Hz UDP open-loop에서 sent/received gap 과 p99 tail 을 재측정하고 다음 병목을 분리한다.
  - relevant context: D112, D113, D114, `artifacts/benchmarks/rio-udp/2026-06-26/session-01/`, `session-02/`.
  - 관련 파일/범위: `src/Hps.Transport.Rio/RioTransport.cs`, `RioUdpEndpoint.cs`, `tests/Hps.Benchmarks/UdpLoopbackScenarioRunner.cs`.
  - 현재 상태 또는 이미 시도한 접근: D111 no-prefetch에서 one-deep pre-post 로 전환했다.
  - known blockers 또는 open questions: RIO UDP completion notification path, bounded receive depth request-context mapping 필요 여부.
  - 가장 자연스러운 next step: session-02 scratch 결과를 기준으로 bounded depth 설계 필요성을 판단한다.
```

- `CHANGELOG_AGENT.md`: benchmark command, summary result, D114 decision 기록.

- [ ] **Step 4: Verify and commit Task 2**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build
git diff --check
```

Commit:

```powershell
git add DECISIONS.md docs/agent-state/decisions/2026-06.md CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "docs: accept rio udp receive prepost policy"
```

Do not stage `.claude/review/*` unless the user explicitly asks.

---

## Self-Review

- Spec coverage:
  - one-deep pre-post: Task 1 Step 1 and Step 5.
  - close-drain blocker B1: Task 1 Step 4 and Step 5.
  - receive operation single owner B2: Task 1 Step 5.
  - handler exception cleanup B3: Task 1 Step 2 and Step 5.
  - shared remote address block B4: Task 1 Step 5 and existing two-remote fan-out coverage.
  - registration churn B5: Task 1 keeps endpoint lifetime remote address block and per-datagram data registration only.
  - D114 docs: Task 2 Step 2.
- Placeholder scan:
  - 금지 placeholder 패턴이나 막연한 "테스트 추가" 단계는 남기지 않았다.
- Type consistency:
  - `RequestClose()`, `CompleteReceiveDrain()`, `CompleteSendDrain()`, `RioUdpReceiveOperation`, and `ReceivedRioUdpDatagram` are introduced before later steps reference them.
