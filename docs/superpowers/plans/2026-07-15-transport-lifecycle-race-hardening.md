# Transport Lifecycle Race Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `BrokerServer`의 start/stop 경합과 RIO/io_uring의 Stop 이후 resource 등록 경합을 deterministic TDD로 제거해, 종료가 끝난 뒤 추적되지 않는 listener, connection, UDP endpoint 또는 native completion owner가 남지 않게 한다.

**Architecture:** `BrokerServer`의 setup/teardown control path만 단일 `SemaphoreSlim`으로 직렬화하고 기존 `_gate`의 짧은 상태 보호는 유지한다. `Dispose`는 `_disposed` 표식을 먼저 원자적으로 게시한 뒤 같은 직렬화된 Stop 경로를 사용한다. RIO/io_uring은 각 transport lock 안에서 `_stopped`를 다시 확인한 뒤에만 resource 목록에 등록하며, 등록 거부 시 기존 local-owner cleanup 경로가 생성된 resource를 정리한다. public API, transport one-shot 의미, data hot path, backend 선택 정책은 바꾸지 않는다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, `ValueTask`, `SemaphoreSlim`, Windows RIO, Linux io_uring

## Global Constraints

- 저장소 루트의 `AGENTS.md`, `AGENT_RULES.md`, D011, D013, D241을 따른다.
- production 변경 전에 assertion failure인 Red를 먼저 확인한다. 컴파일 실패는 Red로 인정하지 않는다.
- 한 cycle/commit에는 D241 lifecycle hardening만 포함한다. hot-path allocation, current-head io_uring 성능 evidence, `BipBuffer` 검증은 섞지 않는다.
- C# 8.0 문법만 사용하고 public API를 추가하거나 변경하지 않는다.
- 새 NuGet dependency를 추가하지 않는다.
- lifecycle semaphore는 start/stop control path에서만 사용한다. receive/send/publish/fan-out 경로에는 추가하지 않는다.
- RIO/io_uring registration guard는 `_started`가 아니라 `_stopped`만 확인한다. 기존 pure unit test가 transport 시작 전 내부 등록 seam을 사용하기 때문이다.
- 테스트 주석, production 주석, 상태 문서는 한국어로 작성한다.
- 중간 commit을 만들지 않는다. Red, Green, 전체 검증, 상태 문서 갱신을 모두 마친 뒤 task-owned 파일만 한 번 commit한다.
- 기존 untracked `.claude/review/*.md`와 `diff/`는 읽기/수정/stage 대상에서 제외한다.

---

## Task 1: 기준선과 작업 경계를 다시 고정한다

**Files:**
- Read: `AGENT_RULES.md`
- Read: `CURRENT_PLAN.md`
- Read: `TODOS.md`
- Read: `CHANGELOG_AGENT.md`
- Read: `DECISIONS.md`
- Read: `docs/superpowers/specs/2026-07-15-transport-lifecycle-race-hardening-design.md`
- Read: `src/Hps.Server/BrokerServer.cs`
- Read: `src/Hps.Transport.Rio/RioTransport.cs`
- Read: `src/Hps.Transport.IoUring/IoUringTransport.cs`

- [x] **Step 1: checkout과 작업 외 파일을 확인한다**

Run:

```powershell
git status --short --branch
git diff --check
```

Expected:

- `master`는 D241 설계 문서 commit 때문에 `origin/master`보다 앞설 수 있다.
- 기존 untracked `.claude/review/*.md`, `diff/` 외에 설명되지 않은 변경이 없어야 한다.
- 기존 tracked diff가 있으면 작업 소유권을 먼저 판별하고, 이 계획 파일과 겹치면 구현을 멈춰 재평가한다.

- [x] **Step 2: focused 기준선을 실행한다**

Run:

```powershell
dotnet test tests/Hps.Server.Tests/Hps.Server.Tests.csproj -c Release --no-restore -p:NuGetAudit=false -v minimal
dotnet test tests/Hps.Transport.Rio.Tests/Hps.Transport.Rio.Tests.csproj -c Release --no-restore -p:NuGetAudit=false -v minimal
dotnet test tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj -c Release --no-restore -p:NuGetAudit=false -v minimal
dotnet test tests/Hps.Transport.Tests/Hps.Transport.Tests.csproj -c Release --no-restore -p:NuGetAudit=false -v minimal
```

Expected: 네 project 모두 green. 실패하면 D241 production 변경 전에 baseline 문제인지 먼저 분리한다.

---

## Task 2: BrokerServer start/stop 경합을 deterministic Red로 고정한다

**Files:**
- Modify: `tests/Hps.Server.Tests/BrokerServerTests.cs`
- Test: `tests/Hps.Server.Tests/Hps.Server.Tests.csproj`

- [x] **Step 1: `FakeTransport`에 TCP/UDP start 차단 seam을 추가한다**

`FakeTransport`에 다음 상태를 추가한다. `RunContinuationsAsynchronously`를 사용해 test signal 완료가 production call stack을 재진입하지 않게 한다.

```csharp
private TaskCompletionSource<bool>? _listenTcpEntered;
private TaskCompletionSource<bool>? _listenTcpRelease;
private TaskCompletionSource<bool>? _bindUdpEntered;
private TaskCompletionSource<bool>? _bindUdpRelease;
```

다음 test helper를 추가한다.

```csharp
internal void BlockNextTcpListen()
{
    _listenTcpEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    _listenTcpRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal Task WaitForTcpListenCallAsync()
{
    return WaitForSignalAsync(_listenTcpEntered, "TCP listen 진입");
}

internal void ReleaseTcpListen()
{
    _listenTcpRelease?.TrySetResult(true);
}
```

UDP도 `BlockNextUdpBind`, `WaitForUdpBindCallAsync`, `ReleaseUdpBind`로 같은 구조를 사용한다. `WaitForSignalAsync`는 null guard와 5초 timeout을 포함하고 `Assert.Same(signal.Task, completedTask)`로 timeout을 실패시킨다.

`ListenTcpAsync`와 `BindUdpAsync`는 `async ValueTask<T>`로 바꾸고 다음 순서를 지킨다.

1. call count 증가
2. entered signal 완료
3. release signal이 있으면 await
4. fake resource 생성 및 반환

```csharp
public async ValueTask<IConnectionListener> ListenTcpAsync(
    EndPoint localEndPoint,
    CancellationToken cancellationToken = default)
{
    ListenTcpCallCount++;
    _listenTcpEntered?.TrySetResult(true);

    if (_listenTcpRelease != null)
        await _listenTcpRelease.Task.ConfigureAwait(false);

    Listener = new FakeConnectionListener(new IPEndPoint(IPAddress.Loopback, 54321));
    return Listener;
}
```

- [x] **Step 2: TCP 경합 Red를 추가한다**

기존 `StopAsync_WhenStarted_ClosesListenerAndStopsTransport` 근처에 다음 계약을 추가한다.

```csharp
[Fact]
public async Task StopAsync_WhenTcpStartIsWaitingForListen_WaitsAndClosesPublishedListener()
{
    FakeTransport transport = new FakeTransport();
    transport.BlockNextTcpListen();
    PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);

    using (BrokerServer server = new BrokerServer(transport, pool, 64))
    {
        Task startTask = server.StartTcpAsync(new IPEndPoint(IPAddress.Loopback, 0)).AsTask();
        await transport.WaitForTcpListenCallAsync();

        Task stopTask = server.StopAsync().AsTask();
        bool stopCompletedBeforeListenRelease = stopTask.IsCompleted;

        transport.ReleaseTcpListen();
        await startTask;
        await stopTask;

        Assert.False(stopCompletedBeforeListenRelease);
        Assert.NotNull(transport.Listener);
        Assert.Equal(1, transport.Listener!.CloseCallCount);
        Assert.Equal(1, transport.Listener.DisposeCallCount);
        Assert.Equal(1, transport.StopCallCount);
        Assert.Null(server.LocalEndPoint);
    }
}
```

핵심 Red는 `stopCompletedBeforeListenRelease`가 현재 구현에서 `true`가 되는 것이다. release와 await를 assertion 전에 수행해 실패해도 차단 task가 남지 않게 한다.

- [x] **Step 3: UDP 경합 Red를 추가한다**

TCP test와 같은 구조로 `StopAsync_WhenUdpStartIsWaitingForBind_WaitsAndClosesPublishedEndpoint`를 추가한다.

Expected assertions:

- bind release 전 Stop task는 완료되지 않는다.
- release 뒤 `FakeUdpEndpoint.CloseCallCount == 1`.
- `DisposeCallCount == 1`.
- `transport.StopCallCount == 1`.
- `server.UdpLocalEndPoint == null`.

- [x] **Step 4: Dispose stop-failure terminal-state Red를 추가한다**

fake transport가 Stop에서 의도한 예외를 던지게 한 뒤 `BrokerServer.Dispose`를 호출하고 같은 server의 Start를 다시 시도한다.
Dispose는 cleanup 예외와 무관한 terminal operation이므로 후속 Start가 `ObjectDisposedException`으로 거부되어야 한다.
기존 구현은 Stop 예외가 `_disposed = true` 대입을 건너뛰어 후속 Start가 성공하므로 assertion Red가 된다.

- [x] **Step 5: 세 server 테스트가 현재 production에서 assertion Red인지 확인한다**

Run:

```powershell
dotnet test tests/Hps.Server.Tests/Hps.Server.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~StopAsync_WhenTcpStartIsWaitingForListen_WaitsAndClosesPublishedListener|FullyQualifiedName~StopAsync_WhenUdpStartIsWaitingForBind_WaitsAndClosesPublishedEndpoint|FullyQualifiedName~Dispose_WhenTransportStopThrows_StillRejectsLaterStart" -v minimal
```

Expected: 3 tests fail. bind/listen release 전 Stop 조기 완료와 Dispose stop failure 뒤 재시작 허용이 각각 assertion failure로 보여야 한다. hang, timeout 또는 compile failure면 test seam부터 수정하고 production으로 넘어가지 않는다.

---

## Task 3: native transport의 Stop 이후 등록을 pure lifecycle Red로 고정한다

**Files:**
- Modify: `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringTransportTcpTests.cs`
- Test: `tests/Hps.Transport.Rio.Tests/Hps.Transport.Rio.Tests.csproj`
- Test: `tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj`

- [x] **Step 1: 각 test class에 standalone connection reflection helper를 추가한다**

`TransportConnection`은 `Hps.Transport` internal type이고 native test assembly의 직접 friend가 아니므로 생성자 호출을 소스에 직접 쓰지 않는다.

```csharp
private static IConnection CreateStandaloneTransportConnection()
{
    Type? connectionType = Type.GetType("Hps.Transport.TransportConnection, Hps.Transport");
    Assert.NotNull(connectionType);

    object? instance = Activator.CreateInstance(connectionType!, nonPublic: true);
    return Assert.IsAssignableFrom<IConnection>(instance);
}
```

- [x] **Step 2: RIO Stop 이후 registration Red를 추가한다**

native capability와 무관하게 실행되도록 `StartAsync`, socket, completion port를 사용하지 않는다.

```csharp
[Fact]
public async Task RegisterConnection_WhenTransportAlreadyStopped_ThrowsInvalidOperationException()
{
    using (RioTransport transport = new RioTransport())
    {
        await transport.StopAsync();
        IConnection connection = CreateStandaloneTransportConnection();

        try
        {
            MethodInfo? registerConnection = typeof(RioTransport).GetMethod(
                "RegisterConnection",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(registerConnection);

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(delegate()
            {
                registerConnection!.Invoke(transport, new object[] { connection });
            });

            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Empty(((ITransportEndpointDiagnostics)transport).GetEndpointSnapshots());
        }
        finally
        {
            connection.Close();
        }
    }
}
```

- [x] **Step 3: io_uring Stop 이후 registration Red를 같은 계약으로 추가한다**

`RioTransport`만 `IoUringTransport`로 바꾼다. Linux syscall과 capability probe를 호출하지 않는 Windows 실행 가능 pure lifecycle test여야 한다.

- [x] **Step 4: 두 테스트가 현재 production에서 assertion Red인지 각각 확인한다**

Run:

```powershell
dotnet test tests/Hps.Transport.Rio.Tests/Hps.Transport.Rio.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~RegisterConnection_WhenTransportAlreadyStopped_ThrowsInvalidOperationException" -v minimal
dotnet test tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~RegisterConnection_WhenTransportAlreadyStopped_ThrowsInvalidOperationException" -v minimal
```

Expected: 각 project에서 1 test fail. 현재 `RegisterConnection`이 예외 없이 목록에 추가되므로 `Assert.Throws<TargetInvocationException>`이 실패해야 한다.

---

## Task 4: BrokerServer lifecycle operation을 최소 Green으로 직렬화한다

**Files:**
- Modify: `src/Hps.Server/BrokerServer.cs`
- Test: `tests/Hps.Server.Tests/Hps.Server.Tests.csproj`

- [x] **Step 1: control-path lifecycle gate를 추가한다**

field와 constructor 초기화를 추가한다.

```csharp
private readonly SemaphoreSlim _lifecycleGate;
```

```csharp
_gate = new object();
_lifecycleGate = new SemaphoreSlim(1, 1);
```

이 gate는 publish/receive 경로에서 참조하지 않는다. semaphore 자체는 waiting lifecycle call과 경합할 수 있으므로 `Dispose`하지 않는다.

- [x] **Step 2: `StartTcpAsync`를 gate wrapper와 기존 core로 분리한다**

public method는 null validation 뒤 lifecycle gate를 기다리고 `finally`에서 반드시 release한다.

```csharp
public async ValueTask StartTcpAsync(
    EndPoint localEndPoint,
    CancellationToken cancellationToken = default)
{
    if (localEndPoint == null)
        throw new ArgumentNullException(nameof(localEndPoint));

    await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
        await StartTcpCoreAsync(localEndPoint, cancellationToken).ConfigureAwait(false);
    }
    finally
    {
        _lifecycleGate.Release();
    }
}
```

`StartTcpCoreAsync`에는 현재 null validation 다음의 본문을 그대로 이동한다. 기존 `_gate`, flag 선점, start failure cleanup과 `_transport.StopAsync` 순서를 바꾸지 않는다.

- [x] **Step 3: `StartUdpAsync`를 같은 패턴으로 분리한다**

`StartUdpCoreAsync`에 기존 본문을 이동하고 handler 설정, transport start, bind, timer 생성과 failure cleanup 순서를 유지한다.

- [x] **Step 4: `StopAsync`를 같은 패턴으로 분리한다**

```csharp
public async ValueTask StopAsync(CancellationToken cancellationToken = default)
{
    await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
        await StopCoreAsync(cancellationToken).ConfigureAwait(false);
    }
    finally
    {
        _lifecycleGate.Release();
    }
}
```

`StopCoreAsync`에는 기존 resource snapshot, field clear, timer dispose, listener/endpoint close, accept loop drain, transport stop 본문을 그대로 이동한다. Start failure cleanup에서 public `StopAsync`를 재호출하지 않는다.

- [x] **Step 5: `Dispose`의 종료 표식을 Stop보다 먼저 게시한다**

현재 `Dispose`는 Stop이 끝난 뒤 `_disposed = true`를 기록한다. 이 순서는 Dispose의 Stop이 gate를 반납한 직후 concurrent Start가 진입할 수 있으므로 다음처럼 바꾼다.

```csharp
public void Dispose()
{
    lock (_gate)
    {
        if (_disposed)
            return;

        _disposed = true;
    }

    StopAsync().AsTask().GetAwaiter().GetResult();
}
```

Start가 먼저 lifecycle gate를 보유한 경우 Dispose의 Stop이 뒤에서 기다렸다가 생성 resource를 닫는다. Dispose가 먼저 표식을 게시한 경우 후속 Start는 core의 기존 `ThrowIfDisposed()`에서 거부된다. `StopCoreAsync` 자체는 disposed 상태에서도 cleanup을 수행해야 하므로 `ThrowIfDisposed()`를 추가하지 않는다.

- [x] **Step 6: server 경합 Red와 전체 Server tests를 Green으로 만든다**

Run:

```powershell
dotnet test tests/Hps.Server.Tests/Hps.Server.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~StopAsync_WhenTcpStartIsWaitingForListen_WaitsAndClosesPublishedListener|FullyQualifiedName~StopAsync_WhenUdpStartIsWaitingForBind_WaitsAndClosesPublishedEndpoint" -v minimal
dotnet test tests/Hps.Server.Tests/Hps.Server.Tests.csproj -c Release --no-restore -p:NuGetAudit=false -v minimal
```

Expected: focused 2/2와 Server project 전체 green. 기존 start failure, dual TCP/UDP start, repeated Stop, Dispose test도 유지되어야 한다.

---

## Task 5: RIO/io_uring registration과 RIO completion owner를 최소 Green으로 닫는다

**Files:**
- Modify: `src/Hps.Transport.Rio/RioTransport.cs`
- Modify: `src/Hps.Transport.IoUring/IoUringTransport.cs`
- Test: `tests/Hps.Transport.Rio.Tests/Hps.Transport.Rio.Tests.csproj`
- Test: `tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj`

- [x] **Step 1: RIO에 lock 내부 전용 stopped guard를 추가한다**

```csharp
private void ThrowIfStoppedLocked()
{
    if (_stopped)
        throw new InvalidOperationException("중지된 RIO Transport에는 새 resource를 등록할 수 없습니다.");
}
```

`RegisterListener`, `RegisterConnection`, `RegisterUdpEndpoint`의 각 `lock (_gate)` 안에서 collection add 바로 전에 호출한다. `_started`는 검사하지 않는다.

- [x] **Step 2: RIO completion port의 생성/종료 전환을 같은 lock으로 보호한다**

`GetOrCreateCompletionPort`는 `_completionPort` 접근 전에 `ThrowIfStoppedLocked()`를 호출한다.

`StopAsync`는 다음 값을 같은 `_gate` 임계구역에서 snapshot하고 field를 null로 바꾼다.

```csharp
RioCompletionPort? completionPort;

lock (_gate)
{
    _stopped = true;
    _started = false;
    // 기존 listener/connection/endpoint snapshots와 clear
    completionPort = _completionPort;
    _completionPort = null;
}
```

resource close 순서는 유지하고 마지막에 local `completionPort?.Dispose()`를 호출한다.

- [x] **Step 3: RIO UDP registration 실패 cleanup을 endpoint owner까지 확장한다**

`BindUdpAsync`의 `finally`를 io_uring의 기존 owner pattern과 맞춘다.

```csharp
finally
{
    if (socket != null)
    {
        endpoint?.Dispose();
        socket.Dispose();
    }
}
```

`RegisterUdpEndpoint`가 Stop 때문에 실패하면 constructor가 만든 RQ/CQ/registration owner도 endpoint dispose로 정리되어야 한다. 성공 경로는 기존처럼 `socket = null!` 뒤 ownership을 endpoint에 넘긴다.

- [x] **Step 4: io_uring에 lock 내부 전용 stopped guard를 추가한다**

```csharp
private void ThrowIfStoppedLocked()
{
    if (_stopped)
        throw new InvalidOperationException("중지된 io_uring Transport에는 새 resource를 등록할 수 없습니다.");
}
```

`RegisterListener`, `RegisterConnection`, `RegisterUdpEndpoint`의 collection add 직전에 호출한다. `StopCore`의 snapshot/drain/queue disposal 순서는 변경하지 않는다. `BindUdpAsync`의 기존 `udpEndpoint?.Dispose()` cleanup도 유지한다.

- [x] **Step 5: native Red와 각 project 전체를 Green으로 만든다**

Run:

```powershell
dotnet test tests/Hps.Transport.Rio.Tests/Hps.Transport.Rio.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~RegisterConnection_WhenTransportAlreadyStopped_ThrowsInvalidOperationException" -v minimal
dotnet test tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~RegisterConnection_WhenTransportAlreadyStopped_ThrowsInvalidOperationException" -v minimal
dotnet test tests/Hps.Transport.Rio.Tests/Hps.Transport.Rio.Tests.csproj -c Release --no-restore -p:NuGetAudit=false -v minimal
dotnet test tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj -c Release --no-restore -p:NuGetAudit=false -v minimal
```

Expected: focused 각 1/1, RIO 전체와 io_uring 전체 green. unsupported OS skip/early-return 정책은 기존과 같아야 한다.

---

## Task 6: Refactor 후 계층 회귀와 4096B x 100Hz target gate를 검증한다

**Files:**
- Review: `src/Hps.Server/BrokerServer.cs`
- Review: `src/Hps.Transport.Rio/RioTransport.cs`
- Review: `src/Hps.Transport.IoUring/IoUringTransport.cs`
- Review: four modified test files

- [x] **Step 1: Green 상태에서 중복과 범위를 점검한다**

확인 항목:

- 세 lifecycle public method가 같은 acquire/release 규칙을 사용한다.
- gate 대기 전에 argument validation을 유지한다.
- gate acquire가 취소되면 acquire 뒤 `finally`만 실행되는 잘못된 구조가 없다.
- backend helper 이름과 메시지가 각 파일에서 하나로 모였다.
- `Register*`는 guard 뒤 add하고, unregister/close 경로는 바꾸지 않았다.
- receive/send/publish 코드에는 semaphore 또는 새 allocation이 들어가지 않았다.
- public API와 csproj는 바뀌지 않았다.

Refactor를 했다면 Task 4/5 focused tests를 다시 실행한다.

- [x] **Step 2: SAEA lifecycle 대조와 solution 전체를 검증한다**

Run:

```powershell
dotnet test tests/Hps.Transport.Tests/Hps.Transport.Tests.csproj -c Release --no-restore -p:NuGetAudit=false -v minimal
dotnet build HighPerformanceSocket.slnx -c Release --no-restore -p:NuGetAudit=false -v minimal
dotnet test HighPerformanceSocket.slnx -c Release --no-build --no-restore -p:NuGetAudit=false -v minimal
```

Expected:

- SAEA tests green.
- Release build 오류 0, 새 경고 0.
- solution tests 전체 green. 기존 기준은 520/520이며 이번 신규 test 5개를 포함한 실제 결과는 525/525다.

- [x] **Step 3: Windows SAEA TCP/UDP 4096B x 100Hz target gate를 실행한다**

Run:

```powershell
dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj -c Release --no-build --no-restore -- --load --protocol tcp --backend saea
dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj -c Release --no-build --no-restore -- --load-open-loop --protocol tcp --backend saea
dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj -c Release --no-build --no-restore -- --load --protocol udp --backend saea
dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj -c Release --no-build --no-restore -- --load-open-loop --protocol udp --backend saea
```

Expected: 네 run 모두 payload 4096B, target rate 100Hz, duration 30초이며 sent/received 3000/3000, transport drop 0, payload error 0, pool rented 0. actual rate와 p50/p99은 기록하되 이번 lifecycle 변경의 hard pass/fail은 기존 correctness gate를 따른다.

- [x] **Step 4: 최종 diff를 검토한다**

Run:

```powershell
git diff --check
git diff --stat
git diff -- src/Hps.Server/BrokerServer.cs src/Hps.Transport.Rio/RioTransport.cs src/Hps.Transport.IoUring/IoUringTransport.cs tests/Hps.Server.Tests/BrokerServerTests.cs tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs tests/Hps.Transport.IoUring.Tests/IoUringTransportTcpTests.cs
```

Expected: D241의 6개 code/test 파일과 상태 문서만 변경된다. 예상 밖 파일 또는 scope 확장이 있으면 commit 전에 중단한다.

---

## Task 7: 결과를 상태 문서에 남기고 단일 reviewable commit을 만든다

**Files:**
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `PLAN.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify: `DECISIONS.md` only if D241 contract changed
- Modify: `docs/agent-state/changelog/2026-07.md`
- Modify: `docs/agent-state/decisions/2026-07.md`
- Modify: `docs/superpowers/specs/2026-07-15-transport-lifecycle-race-hardening-design.md`

- [x] **Step 1: 상태 문서를 실제 결과로 갱신한다**

기록할 내용:

- 각 Red의 정확한 failure assertion.
- focused/project/solution test의 실제 passed/skipped/failed 수.
- Release build warning/error 수.
- TCP/UDP load/open-loop의 actual rate, p50/p99, sent/received/drop/payload error/pool rented 결과.
- D241 implementation review stop과 다음 진입점.
- 실패 또는 미검증 항목은 Completed로 쓰지 않고 `TODOS.md` Deferred Backlog에 handoff-ready하게 남긴다.

- [ ] **Step 2: task-owned 파일만 stage한다**

Run:

```powershell
git add -- src/Hps.Server/BrokerServer.cs src/Hps.Transport.Rio/RioTransport.cs src/Hps.Transport.IoUring/IoUringTransport.cs tests/Hps.Server.Tests/BrokerServerTests.cs tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs tests/Hps.Transport.IoUring.Tests/IoUringTransportTcpTests.cs CURRENT_PLAN.md TODOS.md PLAN.md CHANGELOG_AGENT.md DECISIONS.md docs/agent-state/changelog/2026-07.md docs/agent-state/decisions/2026-07.md docs/superpowers/specs/2026-07-15-transport-lifecycle-race-hardening-design.md docs/superpowers/plans/2026-07-15-transport-lifecycle-race-hardening.md
git diff --cached --check
git diff --cached --stat
```

Expected: 기존 untracked `.claude/review/*.md`와 `diff/`는 staged되지 않는다. 변경되지 않은 allow-list 파일은 `git add`가 무시해도 된다.

- [ ] **Step 3: 검증 성공 뒤 한 번만 commit한다**

Run:

```powershell
git commit -m "fix(lifecycle): serialize start and stop registration"
```

Expected: commit 1개 생성. push는 사용자가 별도로 수행하므로 실행하지 않는다.

- [ ] **Step 4: review stop 상태를 확인한다**

Run:

```powershell
git status --short --branch
git log -1 --oneline
```

Expected: task-owned tracked 변경은 없고 기존 untracked 항목만 남는다. 구현 결과와 검증값을 사용자에게 보고하고 다음 작업을 자동으로 시작하지 않는다.

## Failure Branches

- server Red가 hang 또는 timeout이면 production을 수정하지 말고 fake release/cleanup seam을 먼저 고친다.
- native Red가 capability probe나 syscall을 호출하면 test가 pure lifecycle 경계를 벗어난 것이다. reflection 대상과 test setup을 수정한다.
- lifecycle gate 적용 후 기존 start failure test가 멈추면 public `StopAsync` 재진입 여부를 확인한다. nested public lifecycle call을 추가하지 않는다.
- registration guard 뒤 RIO/io_uring cleanup test가 실패하면 guard를 완화하지 말고 local resource owner의 catch/finally를 보강한다.
- 전체 test에서 기존 unrelated failure가 나오면 D241 diff를 유지한 채 baseline 재현을 먼저 수행하고 결과를 분리한다.
- 4096B x 100Hz target gate가 correctness hard gate를 실패하면 commit하지 않는다. 새 retry, queue depth, timeout 조정으로 범위를 넓히지 말고 artifact와 failure location을 기록한 뒤 설계 재검토로 돌아간다.
