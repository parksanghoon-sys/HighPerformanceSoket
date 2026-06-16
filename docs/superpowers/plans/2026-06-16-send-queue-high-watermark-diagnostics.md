# Send Queue High-Watermark Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** TCP/UDP pending send queue high-watermark 를 Transport diagnostics snapshot 과 benchmark report 에 기록한다.

**Architecture:** endpoint identity 는 아직 도입하지 않는다. enqueue 직후의 pending depth 를 `TransportBase`의 transport-wide max counter 로 기록해 connection/endpoint 가 닫힌 뒤에도 TCP/UDP transport kind 별 lifetime high-watermark 를 잃지 않는다. benchmark 는 기존 `TcpLoopbackRunResult`와 `TcpLoopbackReportWriter` schema 를 확장해 같은 값을 stdout 과 JSON 에 항상 출력한다.

**Tech Stack:** .NET 9, C# 8.0, xUnit, System.Text.Json, 기존 `Hps.Transport`/`Hps.Benchmarks` 프로젝트.

---

## 범위

이번 구현 계획은 두 개의 작은 커밋 단위로 나눈다.

1. Transport diagnostics snapshot 에 TCP/UDP pending send queue high-watermark 를 추가한다.
2. benchmark result/stdout/JSON report 에 high-watermark 값을 연결한다.

이번 계획에서 다루지 않는 범위는 EndpointId, endpoint별 current depth, UDP broker pub/sub 결선, backpressure 정책 변경, latency SLO 실패 gate, drop log/sampling, Server convenience diagnostics API 이다.

## 파일 구조

- Modify: `src/Hps.Transport/Abstractions/TransportDiagnosticsSnapshot.cs`
  - drop count snapshot 에 TCP/UDP pending send queue high-watermark 값을 추가한다.
- Modify: `src/Hps.Transport/Runtime/TransportBase.cs`
  - TCP/UDP high-watermark 누적 필드와 lock-free max update helper 를 추가한다.
- Modify: `src/Hps.Transport/Runtime/TransportConnection.cs`
  - TCP pending enqueue 직후 depth 를 `TransportBase` callback 으로 보고한다.
- Modify: `src/Hps.Transport/Saea/SaeaTransport.cs`
  - TCP connection 생성 시 high-watermark callback 을 넘긴다.
- Modify: `src/Hps.Transport/Saea/SaeaUdpEndpoint.cs`
  - UDP pending enqueue 직후 depth 를 `SaeaTransport`로 보고한다.
- Modify: `tests/Hps.Transport.Tests/Contracts/TransportContractTests.cs`
  - optional diagnostics capability 계약에 high-watermark 필드를 추가한다.
- Modify: `tests/Hps.Transport.Tests/Runtime/TransportSendQueueTests.cs`
  - TCP pending queue high-watermark 누적 테스트를 추가한다.
- Modify: `tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`
  - UDP pending queue high-watermark 누적 테스트를 추가한다.
- Modify: `tests/Hps.Benchmarks/TcpLoopbackRunResult.cs`
  - benchmark result model 에 high-watermark 값을 추가하고 stdout 에 출력한다.
- Modify: `tests/Hps.Benchmarks/TcpLoopbackScenarioRunner.cs`
  - diagnostics snapshot 에서 high-watermark 값을 읽어 result 로 전달한다.
- Modify: `tests/Hps.Benchmarks/TcpLoopbackReportWriter.cs`
  - JSON report schema 에 high-watermark key 를 추가한다.
- Modify: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`
  - 각 커밋 단위 종료 시 상태 문서를 해당 단위 범위만 갱신한다.

---

### Task 1: Transport Diagnostics Snapshot High-Watermark

**Commit boundary:** Transport public diagnostics snapshot 과 TCP/UDP runtime high-watermark 추적까지만 포함한다. benchmark report 는 다음 Task 로 남긴다.

**Files:**
- Modify: `tests/Hps.Transport.Tests/Contracts/TransportContractTests.cs`
- Modify: `tests/Hps.Transport.Tests/Runtime/TransportSendQueueTests.cs`
- Modify: `tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`
- Modify: `src/Hps.Transport/Abstractions/TransportDiagnosticsSnapshot.cs`
- Modify: `src/Hps.Transport/Runtime/TransportBase.cs`
- Modify: `src/Hps.Transport/Runtime/TransportConnection.cs`
- Modify: `src/Hps.Transport/Saea/SaeaTransport.cs`
- Modify: `src/Hps.Transport/Saea/SaeaUdpEndpoint.cs`
- Modify: state docs for this task only

- [ ] **Step 1: 계약 Red 테스트를 작성한다**

`tests/Hps.Transport.Tests/Contracts/TransportContractTests.cs`의 `TransportDiagnostics_Contract_UsesOptionalCapabilityWithoutExpandingITransport`에 high-watermark reflection 단언을 추가한다. 기존 direct constructor 사용은 유지하되, 새 4-인자 constructor 는 reflection 으로 확인해 컴파일 실패가 아니라 단언 실패 Red 가 나게 한다.

```csharp
PropertyInfo? tcpHighWatermark = snapshotType.GetProperty("TcpPendingSendQueueHighWatermark");
PropertyInfo? udpHighWatermark = snapshotType.GetProperty("UdpPendingSendQueueHighWatermark");
ConstructorInfo? extendedConstructor = snapshotType.GetConstructor(new Type[]
{
    typeof(long),
    typeof(long),
    typeof(int),
    typeof(int)
});

Assert.NotNull(tcpHighWatermark);
Assert.Equal(typeof(int), tcpHighWatermark!.PropertyType);
Assert.NotNull(udpHighWatermark);
Assert.Equal(typeof(int), udpHighWatermark!.PropertyType);
Assert.NotNull(extendedConstructor);

object extendedSnapshot = extendedConstructor!.Invoke(new object[] { 2L, 3L, 4, 5 });
Assert.Equal(4, tcpHighWatermark.GetValue(extendedSnapshot));
Assert.Equal(5, udpHighWatermark.GetValue(extendedSnapshot));
Assert.Equal(0, tcpHighWatermark.GetValue(snapshot));
Assert.Equal(0, udpHighWatermark.GetValue(snapshot));
```

- [ ] **Step 2: 계약 Red 를 확인한다**

Run:

```powershell
dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --no-restore --filter "FullyQualifiedName~TransportDiagnostics_Contract_UsesOptionalCapabilityWithoutExpandingITransport"
```

Expected: 테스트 1개가 실패한다. 실패 지점은 `Assert.NotNull(tcpHighWatermark)` 또는 `Assert.NotNull(extendedConstructor)` 이어야 한다. 컴파일 실패가 나오면 Red 테스트를 reflection 기반으로 다시 고친다.

- [ ] **Step 3: snapshot 최소 구현을 추가한다**

`src/Hps.Transport/Abstractions/TransportDiagnosticsSnapshot.cs`를 다음 형태로 확장한다. 기존 2-인자 constructor 는 0 high-watermark 로 유지해 기존 호출부 의미를 깨지 않는다.

```csharp
public readonly struct TransportDiagnosticsSnapshot
{
    /// <summary>
    /// drop counter 만 가진 snapshot 을 만든다.
    /// high-watermark 관측값이 없는 기존 호출 경로는 0으로 기록한다.
    /// </summary>
    public TransportDiagnosticsSnapshot(long tcpDroppedPendingSendCount, long udpDroppedPendingSendCount)
        : this(tcpDroppedPendingSendCount, udpDroppedPendingSendCount, 0, 0)
    {
    }

    /// <summary>
    /// 누적 drop counter 와 Transport 수명 동안 관측한 pending send queue 최대 깊이를 가진 snapshot 을 만든다.
    /// </summary>
    public TransportDiagnosticsSnapshot(
        long tcpDroppedPendingSendCount,
        long udpDroppedPendingSendCount,
        int tcpPendingSendQueueHighWatermark,
        int udpPendingSendQueueHighWatermark)
    {
        if (tcpPendingSendQueueHighWatermark < 0)
            throw new ArgumentOutOfRangeException(nameof(tcpPendingSendQueueHighWatermark));

        if (udpPendingSendQueueHighWatermark < 0)
            throw new ArgumentOutOfRangeException(nameof(udpPendingSendQueueHighWatermark));

        TcpDroppedPendingSendCount = tcpDroppedPendingSendCount;
        UdpDroppedPendingSendCount = udpDroppedPendingSendCount;
        TcpPendingSendQueueHighWatermark = tcpPendingSendQueueHighWatermark;
        UdpPendingSendQueueHighWatermark = udpPendingSendQueueHighWatermark;
    }

    public long TcpDroppedPendingSendCount { get; }

    public long UdpDroppedPendingSendCount { get; }

    public int TcpPendingSendQueueHighWatermark { get; }

    public int UdpPendingSendQueueHighWatermark { get; }

    public long DroppedPendingSendCount => TcpDroppedPendingSendCount + UdpDroppedPendingSendCount;
}
```

파일 상단에 `using System;`이 없으면 추가한다.

- [ ] **Step 4: 계약 Green 을 확인한다**

Run:

```powershell
dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --no-restore --filter "FullyQualifiedName~TransportDiagnostics_Contract_UsesOptionalCapabilityWithoutExpandingITransport"
```

Expected: 대상 테스트 1개가 통과한다.

- [ ] **Step 5: TCP high-watermark Red 테스트를 작성한다**

`tests/Hps.Transport.Tests/Runtime/TransportSendQueueTests.cs`에 다음 테스트를 추가한다. 이 시점에는 snapshot 필드는 존재하지만 값 갱신이 없어 `0 != 5`로 실패해야 한다.

```csharp
// TCP send backlog 관측성 테스트: drop 이 발생하지 않아도 pending queue 가 어디까지 밀렸는지
// Transport 수명 snapshot 에 남아야 latency 증가 원인을 send-side backlog 와 구분할 수 있다.
[Fact]
public void TrySend_WhenPendingQueueGrows_UpdatesTransportPendingSendQueueHighWatermark()
{
    const int SendCount = 5;

    PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
    RefCountedBuffer[] buffers = RentNumberedBuffers(pool, SendCount);
    TestTransport transport = new TestTransport();
    ITransportDiagnostics diagnostics = transport;
    TransportConnection connection = transport.CreateConnection();
    bool publisherRefsReleased = false;

    try
    {
        for (int index = 0; index < buffers.Length; index++)
        {
            buffers[index].AddRef();
            Assert.True(transport.TrySend(connection, new TransportSendBuffer(buffers[index], 0, buffers[index].Length)));
        }

        TransportDiagnosticsSnapshot snapshot = diagnostics.GetDiagnosticsSnapshot();

        Assert.Equal(SendCount, snapshot.TcpPendingSendQueueHighWatermark);
        Assert.Equal(0, snapshot.UdpPendingSendQueueHighWatermark);
        Assert.Equal(0, snapshot.DroppedPendingSendCount);

        ReleasePublisherRefs(buffers);
        publisherRefsReleased = true;

        connection.Close();

        TransportDiagnosticsSnapshot afterCloseSnapshot = diagnostics.GetDiagnosticsSnapshot();
        Assert.Equal(SendCount, afterCloseSnapshot.TcpPendingSendQueueHighWatermark);
        Assert.Equal(0, pool.RentedCount);
    }
    finally
    {
        if (!publisherRefsReleased)
            ReleasePublisherRefs(buffers);

        connection.Close();
    }
}
```

- [ ] **Step 6: TCP Red 를 확인한다**

Run:

```powershell
dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --no-restore --filter "FullyQualifiedName~TrySend_WhenPendingQueueGrows_UpdatesTransportPendingSendQueueHighWatermark"
```

Expected: 테스트 1개가 실패하고 `Expected: 5`, `Actual: 0` 형태의 단언 실패가 나온다.

- [ ] **Step 7: TCP high-watermark 구현을 추가한다**

`src/Hps.Transport/Runtime/TransportBase.cs`에 high-watermark 필드, snapshot 연결, update helper 를 추가한다.

```csharp
private int _tcpPendingSendQueueHighWatermark;
private int _udpPendingSendQueueHighWatermark;
```

`CreateConnection`은 depth callback 을 넘긴다.

```csharp
internal TransportConnection CreateConnection()
{
    return new TransportConnection(null, null, RecordTcpPendingSendDrop, RecordTcpPendingSendDepth);
}
```

`GetDiagnosticsSnapshot`은 새 필드를 포함한다.

```csharp
public TransportDiagnosticsSnapshot GetDiagnosticsSnapshot()
{
    return new TransportDiagnosticsSnapshot(
        ReadTcpDroppedPendingSendCount(),
        ReadUdpDroppedPendingSendCount(),
        ReadTcpPendingSendQueueHighWatermark(),
        ReadUdpPendingSendQueueHighWatermark());
}
```

`TransportBase` 내부에 다음 메서드를 추가한다.

```csharp
internal void RecordTcpPendingSendDepth(int pendingDepth)
{
    UpdateMax(ref _tcpPendingSendQueueHighWatermark, pendingDepth);
}

internal void RecordUdpPendingSendDepth(int pendingDepth)
{
    UpdateMax(ref _udpPendingSendQueueHighWatermark, pendingDepth);
}

private int ReadTcpPendingSendQueueHighWatermark()
{
    return Volatile.Read(ref _tcpPendingSendQueueHighWatermark);
}

private int ReadUdpPendingSendQueueHighWatermark()
{
    return Volatile.Read(ref _udpPendingSendQueueHighWatermark);
}

private static void UpdateMax(ref int target, int candidate)
{
    if (candidate < 0)
        throw new ArgumentOutOfRangeException(nameof(candidate));

    while (true)
    {
        int observed = Volatile.Read(ref target);
        if (candidate <= observed)
            return;

        int exchanged = Interlocked.CompareExchange(ref target, candidate, observed);
        if (exchanged == observed)
            return;
    }
}
```

`src/Hps.Transport/Runtime/TransportConnection.cs`에 depth callback 을 추가한다.

```csharp
private readonly Action<int>? _onPendingSendDepthObserved;
```

constructor overload 는 기존 호출부를 유지하면서 null 을 넘긴다.

```csharp
internal TransportConnection(IDisposable? transportResource, Action<TransportConnection>? onClosed, Action? onPendingSendDropped)
    : this(transportResource, onClosed, onPendingSendDropped, null, DefaultPendingSendCapacity)
{
}

internal TransportConnection(
    IDisposable? transportResource,
    Action<TransportConnection>? onClosed,
    Action? onPendingSendDropped,
    Action<int>? onPendingSendDepthObserved,
    int pendingSendCapacity)
{
    if (pendingSendCapacity <= 0)
        throw new ArgumentOutOfRangeException(nameof(pendingSendCapacity));

    _gate = new object();
    _pendingSends = new Queue<TransportSendBuffer>();
    _sendSignal = new SemaphoreSlim(0);
    _transportResource = transportResource;
    _onClosed = onClosed;
    _onPendingSendDropped = onPendingSendDropped;
    _onPendingSendDepthObserved = onPendingSendDepthObserved;
    _pendingSendCapacity = pendingSendCapacity;
}
```

`TryAcceptSend`는 enqueue 직후 depth 를 기록한다.

```csharp
int pendingDepthAfterEnqueue;

lock (_gate)
{
    if (_closed)
        return false;

    shouldWakePump = _pendingSends.Count == 0;

    if (_pendingSends.Count == _pendingSendCapacity)
        evictedSend = _pendingSends.Dequeue();

    _pendingSends.Enqueue(sendBuffer);
    pendingDepthAfterEnqueue = _pendingSends.Count;
}

NotifyPendingSendDepthObserved(pendingDepthAfterEnqueue);
```

helper 를 추가한다.

```csharp
private void NotifyPendingSendDepthObserved(int pendingDepth)
{
    _onPendingSendDepthObserved?.Invoke(pendingDepth);
}
```

`src/Hps.Transport/Saea/SaeaTransport.cs`의 connection 생성은 새 callback 을 넘긴다.

```csharp
TransportConnection connection = new TransportConnection(
    socket,
    UnregisterConnection,
    RecordTcpPendingSendDrop,
    RecordTcpPendingSendDepth);
```

- [ ] **Step 8: TCP Green 을 확인한다**

Run:

```powershell
dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --no-restore --filter "FullyQualifiedName~TrySend_WhenPendingQueueGrows_UpdatesTransportPendingSendQueueHighWatermark|FullyQualifiedName~TrySend_WhenPendingQueueDropsOldest_IncrementsTransportDiagnosticsSnapshot"
```

Expected: 대상 테스트들이 통과한다. 기존 drop snapshot 테스트도 깨지지 않아야 한다.

- [ ] **Step 9: UDP high-watermark Red 테스트를 작성한다**

`tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`에 다음 테스트를 `UdpSendTo_WhenPendingQueueDropsOldest_IncrementsTransportDiagnosticsSnapshot` 근처에 추가한다.

```csharp
// UDP send backlog 관측성 테스트: endpoint pending queue 가 drop 까지 가지 않아도
// Transport 수명 snapshot 에 UDP queue 최대 깊이가 남아야 막힌 remote 의 send-side 적체를 설명할 수 있다.
[Fact]
public void UdpSendTo_WhenPendingQueueGrows_UpdatesTransportPendingSendQueueHighWatermark()
{
    const int SendCount = 5;

    PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
    RefCountedBuffer[] buffers = RentNumberedUdpBuffers(pool, SendCount);
    bool publisherRefsReleased = false;
    SaeaUdpEndpoint? udpEndpoint = null;

    using (SaeaTransport transport = new SaeaTransport())
    {
        ITransportDiagnostics diagnostics = transport;
        Socket? socket = null;

        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            udpEndpoint = new SaeaUdpEndpoint(transport, socket);
            socket = null;

            EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 9);
            for (int index = 0; index < buffers.Length; index++)
            {
                buffers[index].AddRef();
                TransportSendBuffer sendBuffer = new TransportSendBuffer(buffers[index], 0, buffers[index].Length);

                Assert.True(transport.TrySendTo(udpEndpoint, remoteEndPoint, sendBuffer));
            }

            TransportDiagnosticsSnapshot snapshot = diagnostics.GetDiagnosticsSnapshot();

            Assert.Equal(0, snapshot.TcpPendingSendQueueHighWatermark);
            Assert.Equal(SendCount, snapshot.UdpPendingSendQueueHighWatermark);
            Assert.Equal(0, snapshot.DroppedPendingSendCount);

            ReleasePublisherRefs(buffers);
            publisherRefsReleased = true;

            udpEndpoint.Close();

            TransportDiagnosticsSnapshot afterCloseSnapshot = diagnostics.GetDiagnosticsSnapshot();
            Assert.Equal(SendCount, afterCloseSnapshot.UdpPendingSendQueueHighWatermark);
            Assert.Equal(0, pool.RentedCount);
        }
        finally
        {
            if (!publisherRefsReleased)
                ReleasePublisherRefs(buffers);

            udpEndpoint?.Close();
            socket?.Dispose();
        }
    }
}
```

- [ ] **Step 10: UDP Red 를 확인한다**

Run:

```powershell
dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --no-restore --filter "FullyQualifiedName~UdpSendTo_WhenPendingQueueGrows_UpdatesTransportPendingSendQueueHighWatermark"
```

Expected: 테스트 1개가 실패하고 `Expected: 5`, `Actual: 0` 형태의 단언 실패가 나온다.

- [ ] **Step 11: UDP high-watermark 구현을 추가한다**

`src/Hps.Transport/Saea/SaeaUdpEndpoint.cs`의 `TryAcceptSend`에서 enqueue 직후 pending depth 를 계산하고 transport 에 보고한다.

```csharp
int pendingDepthAfterEnqueue;

lock (_sendGate)
{
    if (IsClosed)
        return false;

    shouldWakePump = _pendingSends.Count == 0;

    if (_pendingSends.Count == _pendingSendCapacity)
        evictedSend = _pendingSends.Dequeue();

    _pendingSends.Enqueue(new UdpSendRequest(remoteEndPoint, sendBuffer));
    pendingDepthAfterEnqueue = _pendingSends.Count;
}

_transport.RecordUdpPendingSendDepth(pendingDepthAfterEnqueue);
```

- [ ] **Step 12: UDP Green 과 Transport 전체 회귀를 확인한다**

Run:

```powershell
dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --no-restore --filter "FullyQualifiedName~UdpSendTo_WhenPendingQueueGrows_UpdatesTransportPendingSendQueueHighWatermark|FullyQualifiedName~UdpSendTo_WhenPendingQueueDropsOldest_IncrementsTransportDiagnosticsSnapshot"
```

Expected: 대상 테스트들이 통과한다.

Run:

```powershell
dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --no-restore
```

Expected: `Hps.Transport.Tests` 전체가 실패 0으로 통과한다.

- [ ] **Step 13: Task 1 상태 문서를 갱신한다**

상태 문서는 사용자가 이미 수정한 내용과 충돌하지 않도록 현재 diff 를 먼저 확인한 뒤, 이 커밋 범위만 반영한다.

Run:

```powershell
git diff -- CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md
```

권장 갱신:

- `DECISIONS.md`: D054 추가. 내용은 "Transport diagnostics snapshot 은 TCP/UDP pending send queue lifetime high-watermark 를 포함한다."
- `CURRENT_PLAN.md`: 다음 단일 작업 단위를 benchmark report high-watermark 연결로 변경.
- `TODOS.md`: high-watermark diagnostics 항목에서 "Transport snapshot 완료, benchmark report 연결 남음"으로 쪼개기.
- `CHANGELOG_AGENT.md`: Task 1 검증 명령과 결과 기록.

- [ ] **Step 14: Task 1 전체 검증을 실행한다**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
```

Expected: 경고 0, 오류 0.

Run:

```powershell
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
```

Expected: 전체 테스트 실패 0.

Run:

```powershell
git diff --check
```

Expected: whitespace 오류 없음. LF/CRLF 안내 경고만 있으면 허용한다.

- [ ] **Step 15: Task 1 파일만 stage 하고 커밋한다**

Run:

```powershell
git status --short
```

Expected: 사용자가 만든 unrelated 변경이 보이면 stage 하지 않는다.

Run:

```powershell
git add -- src\Hps.Transport\Abstractions\TransportDiagnosticsSnapshot.cs src\Hps.Transport\Runtime\TransportBase.cs src\Hps.Transport\Runtime\TransportConnection.cs src\Hps.Transport\Saea\SaeaTransport.cs src\Hps.Transport\Saea\SaeaUdpEndpoint.cs tests\Hps.Transport.Tests\Contracts\TransportContractTests.cs tests\Hps.Transport.Tests\Runtime\TransportSendQueueTests.cs tests\Hps.Transport.Tests\Saea\SaeaTransportTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md
git commit -m "feat: track send queue high watermarks"
```

Expected: Task 1 파일만 포함한 커밋이 생성된다. 커밋 뒤 사용자 리뷰를 기다린다.

---

### Task 2: Benchmark Result And JSON Report High-Watermark

**Commit boundary:** benchmark stdout/JSON report 가 Task 1의 diagnostics 값을 소비하게 한다. Transport runtime 코드는 더 이상 바꾸지 않는다.

**Files:**
- Modify: `tests/Hps.Benchmarks/TcpLoopbackRunResult.cs`
- Modify: `tests/Hps.Benchmarks/TcpLoopbackScenarioRunner.cs`
- Modify: `tests/Hps.Benchmarks/TcpLoopbackReportWriter.cs`
- Modify: state docs for this task only

- [ ] **Step 1: report key Red 를 확인한다**

Task 1 커밋이 적용된 상태에서 smoke report 를 만들고 새 key 가 없음을 확인한다.

Run:

```powershell
$report = Join-Path $env:TEMP "hps-hwm-red-report.json"
Remove-Item -Force $report -ErrorAction SilentlyContinue
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build --no-restore -- --smoke --report $report
Select-String -Path $report -Pattern '"tcp-pending-send-queue-high-watermark"'
```

Expected: benchmark 자체는 `smoke-result: pass`로 끝나지만 `Select-String`은 match 를 찾지 못해 실패한다.

- [ ] **Step 2: `TcpLoopbackRunResult`를 확장한다**

`tests/Hps.Benchmarks/TcpLoopbackRunResult.cs` constructor 에 high-watermark 인자를 `dropped` 뒤에 추가한다.

```csharp
int tcpPendingSendQueueHighWatermark,
int udpPendingSendQueueHighWatermark,
```

constructor body 에 값을 저장한다.

```csharp
TcpPendingSendQueueHighWatermark = tcpPendingSendQueueHighWatermark;
UdpPendingSendQueueHighWatermark = udpPendingSendQueueHighWatermark;
```

property 를 추가한다.

```csharp
public int TcpPendingSendQueueHighWatermark { get; }

public int UdpPendingSendQueueHighWatermark { get; }
```

`Print`에 stdout key 를 추가한다.

```csharp
writer.WriteLine("tcp-pending-send-queue-high-watermark: {0}", TcpPendingSendQueueHighWatermark);
writer.WriteLine("udp-pending-send-queue-high-watermark: {0}", UdpPendingSendQueueHighWatermark);
```

이 두 줄은 `dropped` 출력 바로 뒤에 둔다.

- [ ] **Step 3: scenario runner 가 diagnostics 값을 넘긴다**

`tests/Hps.Benchmarks/TcpLoopbackScenarioRunner.cs`의 `CreateResult` 호출과 signature 를 확장한다.

호출부:

```csharp
return CreateResult(
    resultName,
    scenario,
    publishRateHz,
    targetDurationSeconds,
    messageCount,
    sent,
    received,
    diagnostics.DroppedPendingSendCount,
    diagnostics.TcpPendingSendQueueHighWatermark,
    diagnostics.UdpPendingSendQueueHighWatermark,
    payloadErrors,
    pool.RentedCount,
    latencyTicks,
    elapsed.ElapsedMilliseconds);
```

signature:

```csharp
private static TcpLoopbackRunResult CreateResult(
    string resultName,
    string scenario,
    int targetRateHz,
    int targetDurationSeconds,
    int plannedMessageCount,
    int sent,
    int received,
    long dropped,
    int tcpPendingSendQueueHighWatermark,
    int udpPendingSendQueueHighWatermark,
    int payloadErrors,
    int poolRented,
    long[] latencyTicks,
    long elapsedMilliseconds)
```

`new TcpLoopbackRunResult` 호출:

```csharp
return new TcpLoopbackRunResult(
    resultName,
    scenario,
    BenchmarkTargets.PayloadBytes,
    targetRateHz,
    targetDurationSeconds,
    plannedMessageCount,
    sent,
    received,
    dropped,
    tcpPendingSendQueueHighWatermark,
    udpPendingSendQueueHighWatermark,
    payloadErrors,
    poolRented,
    p50,
    p99,
    firstHalfP99,
    secondHalfP99,
    elapsedMilliseconds);
```

- [ ] **Step 4: JSON writer 를 확장한다**

`tests/Hps.Benchmarks/TcpLoopbackReportWriter.cs`에서 `dropped` 바로 뒤에 두 key 를 추가한다.

```csharp
writer.WriteNumber("tcp-pending-send-queue-high-watermark", result.TcpPendingSendQueueHighWatermark);
writer.WriteNumber("udp-pending-send-queue-high-watermark", result.UdpPendingSendQueueHighWatermark);
```

- [ ] **Step 5: benchmark Green 을 확인한다**

Run:

```powershell
dotnet build tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-restore
```

Expected: 경고 0, 오류 0.

Run:

```powershell
$report = Join-Path $env:TEMP "hps-hwm-smoke-report.json"
Remove-Item -Force $report -ErrorAction SilentlyContinue
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build --no-restore -- --smoke --report $report
Select-String -Path $report -Pattern '"tcp-pending-send-queue-high-watermark"'
Select-String -Path $report -Pattern '"udp-pending-send-queue-high-watermark"'
```

Expected: `smoke-result: pass`가 출력되고 두 `Select-String` 명령이 각각 match 를 찾는다.

- [ ] **Step 6: Task 2 상태 문서를 갱신한다**

권장 갱신:

- `DECISIONS.md`: D055 추가. 내용은 "benchmark report schema 는 send queue high-watermark key 를 항상 포함한다."
- `CURRENT_PLAN.md`: 다음 후보를 EndpointId/snapshot 최소 계약 또는 UDP broker v1 정책 결정으로 이동.
- `TODOS.md`: high-watermark diagnostics 항목을 Completed 로 이동하고 EndpointId 항목을 다음 P1로 유지.
- `CHANGELOG_AGENT.md`: benchmark report key, 검증 명령, 결과 기록.

- [ ] **Step 7: Task 2 전체 검증을 실행한다**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
```

Expected: 경고 0, 오류 0.

Run:

```powershell
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
```

Expected: 전체 테스트 실패 0.

Run:

```powershell
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build --no-restore -- --smoke --report $env:TEMP\hps-hwm-smoke-report.json
```

Expected: `smoke-result: pass`, `dropped: 0`, `pool-rented: 0`, high-watermark stdout key 출력.

Run:

```powershell
git diff --check
```

Expected: whitespace 오류 없음. LF/CRLF 안내 경고만 있으면 허용한다.

- [ ] **Step 8: Task 2 파일만 stage 하고 커밋한다**

Run:

```powershell
git status --short
```

Expected: 사용자가 만든 unrelated 변경이 보이면 stage 하지 않는다.

Run:

```powershell
git add -- tests\Hps.Benchmarks\TcpLoopbackRunResult.cs tests\Hps.Benchmarks\TcpLoopbackScenarioRunner.cs tests\Hps.Benchmarks\TcpLoopbackReportWriter.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md
git commit -m "feat: report send queue high watermarks"
```

Expected: Task 2 파일만 포함한 커밋이 생성된다. 커밋 뒤 사용자 리뷰를 기다린다.

---

## Self-Review

- Spec coverage: `docs/superpowers/specs/2026-06-16-interface-server-endpoint-model-design.md`의 1순위 구현인 TCP/UDP send queue high-watermark diagnostics 를 Task 1과 Task 2가 모두 다룬다. EndpointId, UDP broker, latency SLO gate 는 명시적으로 범위 밖이다.
- Placeholder scan: 이 계획은 결정되지 않은 placeholder 단어를 사용하지 않는다. 각 Red/Green 단계는 수정 파일, 코드 조각, 실행 명령, 기대 결과를 포함한다.
- Type consistency: 새 property 이름은 전 구간 `TcpPendingSendQueueHighWatermark`, `UdpPendingSendQueueHighWatermark` 로 통일한다. JSON/stdout key 는 kebab-case `tcp-pending-send-queue-high-watermark`, `udp-pending-send-queue-high-watermark` 로 통일한다.
- Worktree caution: 현재 작업 트리에 사용자가 수정한 state/design 문서가 있을 수 있다. 구현자는 각 commit 전에 `git status --short`를 확인하고 이번 task 파일만 stage 해야 한다.
