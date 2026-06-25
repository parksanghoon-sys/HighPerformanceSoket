# Windows RIO Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Windows RIO backend 를 `ITransport` 뒤에 TCP-first opt-in backend 로 추가할 수 있는 구현 경로를 만든다.

**Architecture:** 기본 `TransportFactory.CreateDefault()`는 SAEA를 유지한다. RIO는 `Hps.Transport.Rio` project 안에서 capability probe, native function table, registered buffer owner, TCP queue owner, TCP pump 순서로 붙이고 기존 Transport/Broker/Server 테스트를 재사용한다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, Windows Winsock Registered I/O, `PinnedBlockMemoryPool`, 기존 `Hps.Transport` abstraction.

## Global Constraints

- TFM은 `net9.0`, LangVersion은 C# 8.0을 유지한다.
- public `ITransport`/`IConnection` 계약은 RIO 때문에 넓히지 않는다.
- 기본 factory 는 모든 RIO TCP/UDP parity 가 검증될 때까지 SAEA를 반환한다.
- RIO는 Windows-only opt-in/test path 로 먼저 검증한다.
- RIO 첫 구현 범위는 TCP다. UDP RIO, batching, automatic default backend selection 은 별도 단위다.
- 모든 코드 주석과 문서 설명은 한국어로 작성한다.
- 테스트에는 무엇을 검증하는지 한국어 주석을 남긴다.
- 코드 변경은 Red-Green-Refactor 순서로 진행하고 task 별 커밋을 만든다.

---

## File Structure

- Create: `src/Hps.Transport.Rio/Hps.Transport.Rio.csproj`
  - RIO backend project. `Hps.Transport`와 `Hps.Buffers`만 참조한다.
- Create: `src/Hps.Transport.Rio/RioTransport.cs`
  - `TransportBase`를 상속하는 opt-in backend root.
- Create: `src/Hps.Transport.Rio/RioCapabilityProbe.cs`
  - Windows/RIO availability 를 side effect 없이 표현하는 probe.
- Create: `src/Hps.Transport.Rio/RioCapabilityStatus.cs`
  - `Available`, `UnsupportedOperatingSystem`, `Unavailable` 상태 enum.
- Create: `src/Hps.Transport.Rio/RioNative.cs`
  - RIO function table load 와 native handle/function pointer 경계.
- Create: `src/Hps.Transport.Rio/RioRegisteredBufferPool.cs`
  - pinned block 과 RIO buffer id 수명 연결 owner.
- Create: `src/Hps.Transport.Rio/RioCompletionQueue.cs`
  - completion queue notification/dequeue owner.
- Create: `src/Hps.Transport.Rio/RioRequestQueue.cs`
  - socket 별 request queue 와 outstanding send/receive count owner.
- Create: `src/Hps.Transport.Rio/RioConnection.cs`
  - RIO socket/resource 와 `TransportConnection` adapter.
- Create: `tests/Hps.Transport.Rio.Tests/Hps.Transport.Rio.Tests.csproj`
  - Windows-only RIO unit/contract tests.
- Create: `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`
- Create: `tests/Hps.Transport.Rio.Tests/RioRegisteredBufferPoolTests.cs`
- Create: `tests/Hps.Transport.Rio.Tests/RioQueueOwnerTests.cs`
- Create: `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`
- Modify: `HighPerformanceSocket.slnx`
  - RIO source/test projects 를 solution 에 추가한다.
- Modify: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`
  - 각 task 완료 상태와 결정 변경을 기록한다.

---

### Task 1: RIO Project Skeleton And Capability Probe

**Files:**
- Create: `src/Hps.Transport.Rio/Hps.Transport.Rio.csproj`
- Create: `src/Hps.Transport.Rio/RioCapabilityStatus.cs`
- Create: `src/Hps.Transport.Rio/RioCapabilityProbe.cs`
- Create: `src/Hps.Transport.Rio/RioTransport.cs`
- Create: `tests/Hps.Transport.Rio.Tests/Hps.Transport.Rio.Tests.csproj`
- Create: `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`
- Modify: `HighPerformanceSocket.slnx`
- Modify: root state docs

**Interfaces:**
- Produces: `public enum RioCapabilityStatus`
- Produces: `public static class RioCapabilityProbe`
- Produces: `public static RioCapabilityStatus GetStatus()`
- Produces: `public sealed class RioTransport : TransportBase`

- [ ] **Step 1: Write the failing tests**

Create `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`:

```csharp
using System;
using System.Runtime.InteropServices;
using Hps.Transport;
using Xunit;

namespace Hps.Transport.Rio.Tests
{
    public sealed class RioCapabilityProbeTests
    {
        // 첫 Red는 production project 부재를 reflection assertion failure 로 잡는다.
        // 컴파일 실패가 아니라 "RIO capability probe type 이 아직 없다"는 요구사항 실패를 보여준다.
        [Fact]
        public void RioCapabilityProbe_TypeExists()
        {
            Type? type = Type.GetType("Hps.Transport.RioCapabilityProbe, Hps.Transport.Rio");

            Assert.NotNull(type);
        }

        // RIO backend 는 Windows 전용 opt-in 경로다.
        // 이 테스트는 비 Windows 환경에서 RIO를 사용할 수 있다고 오판하지 않게 막는다.
        [Fact]
        public void GetStatus_WhenNotWindows_ReturnsUnsupportedOperatingSystem()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            Type? probeType = Type.GetType("Hps.Transport.RioCapabilityProbe, Hps.Transport.Rio");
            Type? statusType = Type.GetType("Hps.Transport.RioCapabilityStatus, Hps.Transport.Rio");
            if (probeType == null || statusType == null)
                return;

            object? status = probeType.GetMethod("GetStatus")!.Invoke(null, null);
            object expected = Enum.Parse(statusType, "UnsupportedOperatingSystem");

            Assert.Equal(expected, status);
        }

        // 기본 factory 는 Phase 5 초기에 SAEA를 유지해야 한다.
        // RIO가 일부 구현됐더라도 TCP/UDP parity 전까지 default backend 를 바꾸면 기존 통합 경로가 흔들린다.
        [Fact]
        public void CreateDefault_DuringRioOptInPhase_ReturnsSaeaTransport()
        {
            ITransport transport = TransportFactory.CreateDefault();

            Assert.IsType<SaeaTransport>(transport);
            transport.Dispose();
        }

        // skeleton transport 는 아직 opt-in construction 만 허용한다.
        // StartAsync가 예외 없이 끝나면 후속 task 가 같은 root type 위에 queue/resource 를 붙일 수 있다.
        [Fact]
        public async void RioTransport_WhenConstructed_StartStopDoesNotThrow()
        {
            Type? transportType = Type.GetType("Hps.Transport.RioTransport, Hps.Transport.Rio");
            if (transportType == null)
                return;

            using (ITransport transport = (ITransport)Activator.CreateInstance(transportType)!)
            {
                await transport.StartAsync();
                await transport.StopAsync();
            }
        }
    }
}
```

- [ ] **Step 2: Run the tests and verify Red**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore
```

Expected: `RioCapabilityProbe_TypeExists` fails with `Assert.NotNull() Failure` because the production assembly/type does not exist yet.

- [ ] **Step 3: Add the project skeleton**

Create `src/Hps.Transport.Rio/Hps.Transport.Rio.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- 공통 TFM/LangVersion/Unsafe 설정은 루트 Directory.Build.props 를 따른다. -->
  <ItemGroup>
    <ProjectReference Include="..\Hps.Buffers\Hps.Buffers.csproj" />
    <ProjectReference Include="..\Hps.Transport\Hps.Transport.csproj" />
  </ItemGroup>

</Project>
```

Create `tests/Hps.Transport.Rio.Tests/Hps.Transport.Rio.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- 공통 TFM/LangVersion 은 루트 Directory.Build.props 를 따른다. -->
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Hps.Transport\Hps.Transport.csproj" />
  </ItemGroup>

</Project>
```

After confirming Red, add `src/Hps.Transport.Rio/Hps.Transport.Rio.csproj` and add this project reference:

```xml
    <ProjectReference Include="..\..\src\Hps.Transport.Rio\Hps.Transport.Rio.csproj" />
```

Add both projects to `HighPerformanceSocket.slnx` under `/src/` and `/tests/`.

- [ ] **Step 4: Add the minimal implementation**

Create `src/Hps.Transport.Rio/RioCapabilityStatus.cs`:

```csharp
namespace Hps.Transport
{
    /// <summary>
    /// 현재 process 에서 RIO backend 를 사용할 수 있는지 나타내는 probe 결과다.
    /// </summary>
    public enum RioCapabilityStatus
    {
        Available = 0,
        UnsupportedOperatingSystem = 1,
        Unavailable = 2
    }
}
```

Create `src/Hps.Transport.Rio/RioCapabilityProbe.cs`:

```csharp
using System.Runtime.InteropServices;

namespace Hps.Transport
{
    /// <summary>
    /// RIO backend 사용 가능성을 부작용 없이 확인하는 진입점이다.
    /// 실제 function table load 는 Task 2에서 이 경계 뒤에 붙인다.
    /// </summary>
    public static class RioCapabilityProbe
    {
        public static RioCapabilityStatus GetStatus()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return RioCapabilityStatus.UnsupportedOperatingSystem;

            return RioCapabilityStatus.Unavailable;
        }
    }
}
```

Create `src/Hps.Transport.Rio/RioTransport.cs`:

```csharp
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Hps.Transport
{
    /// <summary>
    /// Windows RIO backend root 다.
    /// 초기 task 에서는 opt-in construction 과 수명 경계만 만들고 실제 socket pump 는 후속 task 에서 붙인다.
    /// </summary>
    public sealed class RioTransport : TransportBase
    {
        private bool _started;
        private bool _stopped;

        public override ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_stopped)
                throw new InvalidOperationException("이미 중지된 RIO Transport 는 다시 시작할 수 없습니다.");

            _started = true;
            return default(ValueTask);
        }

        public override ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _stopped = true;
            _started = false;
            return default(ValueTask);
        }

        public override ValueTask<IConnectionListener> ListenTcpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("RIO TCP listen 은 후속 task 에서 구현합니다.");
        }

        public override ValueTask<IConnection> ConnectTcpAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("RIO TCP connect 는 후속 task 에서 구현합니다.");
        }
    }
}
```

- [ ] **Step 5: Run focused tests and commit**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore
dotnet build HighPerformanceSocket.slnx --no-restore
```

Expected: RIO tests pass, solution build passes with 0 warnings and 0 errors.

Commit:

```powershell
git add src/Hps.Transport.Rio tests/Hps.Transport.Rio.Tests HighPerformanceSocket.slnx CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs/agent-state/decisions/2026-06.md
git commit -m "feat: add rio transport skeleton"
```

---

### Task 2: Native Function Table Loader

**Files:**
- Create: `src/Hps.Transport.Rio/RioNative.cs`
- Modify: `src/Hps.Transport.Rio/RioCapabilityProbe.cs`
- Create/Modify: `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`
- Modify: root state docs

**Interfaces:**
- Produces: `internal sealed class RioNative`
- Produces: `internal static bool TryLoadFunctionTable(out RioNative? native)`
- Consumes: `RioCapabilityStatus`

- [ ] **Step 1: Write the failing tests**

Add to `RioCapabilityProbeTests.cs`:

```csharp
        // native function table loader 는 probe 뒤에 숨어야 한다.
        // reflection assertion 으로 타입 부재 Red 를 만들면 OS별 RIO 지원 여부와 무관하게 요구사항을 검증할 수 있다.
        [Fact]
        public void RioNative_TypeExists()
        {
            Type? type = Type.GetType("Hps.Transport.RioNative, Hps.Transport.Rio");

            Assert.NotNull(type);
        }

        // Windows에서 RIO function table load 결과는 Available 또는 Unavailable 로 수렴해야 한다.
        // 예외가 escape 하면 factory probe 가 fallback 대신 process failure 를 일으킬 수 있다.
        [Fact]
        public void GetStatus_WhenWindows_DoesNotThrow()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;
            RioCapabilityStatus status = RioCapabilityProbe.GetStatus();

            Assert.True(status == RioCapabilityStatus.Available || status == RioCapabilityStatus.Unavailable);
        }
```

- [ ] **Step 2: Run and verify Red**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-build --no-restore
```

Expected: `RioNative_TypeExists` fails with `Assert.NotNull() Failure` because `RioNative` does not exist yet.

- [ ] **Step 3: Add native wrapper**

Create `src/Hps.Transport.Rio/RioNative.cs` with explicit C# 8 style:

```csharp
using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Hps.Transport
{
    /// <summary>
    /// Winsock RIO extension function table 을 보관하는 native 경계다.
    /// 포인터 값은 이 타입 밖으로 흘리지 않아 잘못된 delegate 변환과 수명 혼선을 막는다.
    /// </summary>
    internal sealed class RioNative
    {
        private RioNative()
        {
        }

        internal static bool TryLoadFunctionTable(out RioNative? native)
        {
            native = null;

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                // 실제 WSAIoctl + WSAID_MULTIPLE_RIO load 는 이 method 안에서만 수행한다.
                // 실패는 fallback 가능한 capability miss 로 취급하고 예외를 밖으로 내보내지 않는다.
                return TryLoadFunctionTableCore(socket, out native);
            }
        }

        private static bool TryLoadFunctionTableCore(Socket socket, out RioNative? native)
        {
            native = null;

            try
            {
                // 후속 구현에서 SIO_GET_MULTIPLE_EXTENSION_FUNCTION_POINTER 호출과
                // RIO_EXTENSION_FUNCTION_TABLE marshalling 을 이 위치에 채운다.
                return false;
            }
            catch (SocketException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }
}
```

- [ ] **Step 4: Wire probe to native wrapper**

Modify `RioCapabilityProbe.GetStatus()`:

```csharp
        public static RioCapabilityStatus GetStatus()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return RioCapabilityStatus.UnsupportedOperatingSystem;

            RioNative? native;
            return RioNative.TryLoadFunctionTable(out native)
                ? RioCapabilityStatus.Available
                : RioCapabilityStatus.Unavailable;
        }
```

- [ ] **Step 5: Run focused tests and commit**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-build --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
```

Expected: all tests pass. On environments without RIO support, status is `Unavailable`, not an exception.

Commit:

```powershell
git add src/Hps.Transport.Rio tests/Hps.Transport.Rio.Tests CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: add rio capability probe"
```

---

### Task 3: Registered Buffer Owner

**Files:**
- Create: `src/Hps.Transport.Rio/RioRegisteredBufferPool.cs`
- Create: `tests/Hps.Transport.Rio.Tests/RioRegisteredBufferPoolTests.cs`
- Modify: root state docs

**Interfaces:**
- Produces: `internal sealed class RioRegisteredBufferPool : IDisposable`
- Produces: `internal RefCountedBuffer RentReceiveBlock()`
- Produces: `internal int RentedCount`
- Produces: `internal void CompleteRequest(RefCountedBuffer buffer)`

- [ ] **Step 1: Write the failing tests**

Create `tests/Hps.Transport.Rio.Tests/RioRegisteredBufferPoolTests.cs`:

```csharp
using Hps.Buffers;
using Xunit;

namespace Hps.Transport.Rio.Tests
{
    public sealed class RioRegisteredBufferPoolTests
    {
        // RIO는 completion dequeue 전까지 registered buffer association 이 살아 있어야 한다.
        // 이 테스트는 request 완료 신호가 들어오기 전 Dispose가 pool block 을 반납하지 않게 고정한다.
        [Fact]
        public void Dispose_WhenRequestIsOutstanding_DoesNotReturnBlockUntilCompletion()
        {
            RioRegisteredBufferPool pool = new RioRegisteredBufferPool(64);
            RefCountedBuffer buffer = pool.RentReceiveBlock();

            Assert.Equal(1, pool.RentedCount);

            pool.Dispose();
            Assert.Equal(1, pool.RentedCount);

            pool.CompleteRequest(buffer);
            Assert.Equal(0, pool.RentedCount);
        }

        // 완료가 먼저 오고 Dispose가 나중에 오면 block 은 정확히 한 번만 반납되어야 한다.
        // double completion 이 들어와도 RefCountedBuffer Release가 두 번 호출되면 안 된다.
        [Fact]
        public void CompleteRequest_WhenCalledTwice_ReleasesOnlyOnce()
        {
            RioRegisteredBufferPool pool = new RioRegisteredBufferPool(64);
            RefCountedBuffer buffer = pool.RentReceiveBlock();

            pool.CompleteRequest(buffer);
            pool.CompleteRequest(buffer);

            Assert.Equal(0, pool.RentedCount);
            pool.Dispose();
        }
    }
}
```

- [ ] **Step 2: Run and verify Red**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-build --no-restore
```

Expected: fails because `RioRegisteredBufferPool` does not exist.

- [ ] **Step 3: Implement minimal owner**

Create `src/Hps.Transport.Rio/RioRegisteredBufferPool.cs`:

```csharp
using System;
using System.Collections.Generic;
using Hps.Buffers;

namespace Hps.Transport
{
    /// <summary>
    /// RIO registered buffer id 와 pinned block 수명을 함께 소유하는 owner 다.
    /// 초기 구현은 native 등록 id 없이 outstanding request 수명 규칙을 먼저 고정한다.
    /// </summary>
    internal sealed class RioRegisteredBufferPool : IDisposable
    {
        private readonly object _gate;
        private readonly PinnedBlockMemoryPool _pool;
        private readonly HashSet<RefCountedBuffer> _outstanding;
        private bool _disposed;

        internal RioRegisteredBufferPool(int blockSize)
        {
            _gate = new object();
            _pool = new PinnedBlockMemoryPool(blockSize);
            _outstanding = new HashSet<RefCountedBuffer>();
        }

        internal int RentedCount => _pool.RentedCount;

        internal RefCountedBuffer RentReceiveBlock()
        {
            lock (_gate)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(RioRegisteredBufferPool));

                RefCountedBuffer buffer = _pool.RentCounted();
                _outstanding.Add(buffer);
                return buffer;
            }
        }

        internal void CompleteRequest(RefCountedBuffer buffer)
        {
            bool shouldRelease;

            lock (_gate)
            {
                shouldRelease = _outstanding.Remove(buffer);
            }

            if (shouldRelease)
                buffer.Release();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
            }
        }
    }
}
```

- [ ] **Step 4: Run focused tests and commit**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-build --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
```

Expected: all tests pass, no buffer leak in focused tests.

Commit:

```powershell
git add src/Hps.Transport.Rio/RioRegisteredBufferPool.cs tests/Hps.Transport.Rio.Tests/RioRegisteredBufferPoolTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: add rio registered buffer owner"
```

---

### Task 4: TCP Queue Owners

**Files:**
- Create: `src/Hps.Transport.Rio/RioCompletionQueue.cs`
- Create: `src/Hps.Transport.Rio/RioRequestQueue.cs`
- Create: `tests/Hps.Transport.Rio.Tests/RioQueueOwnerTests.cs`
- Modify: root state docs

**Interfaces:**
- Produces: `internal sealed class RioCompletionQueue : IDisposable`
- Produces: `internal sealed class RioRequestQueue : IDisposable`
- Produces: `internal bool TryReserveReceive()`
- Produces: `internal bool TryReserveSend()`
- Produces: `internal void CompleteReceive()`
- Produces: `internal void CompleteSend()`

- [ ] **Step 1: Write the failing tests**

Create `tests/Hps.Transport.Rio.Tests/RioQueueOwnerTests.cs`:

```csharp
using Xunit;

namespace Hps.Transport.Rio.Tests
{
    public sealed class RioQueueOwnerTests
    {
        // RIO request queue 는 MaxOutstandingReceive/Send 한도를 넘기면 안 된다.
        // native call 전에 owner 가 quota를 막아야 completion queue capacity 초과를 피할 수 있다.
        [Fact]
        public void TryReserveReceive_WhenLimitReached_ReturnsFalseUntilCompletion()
        {
            RioRequestQueue queue = new RioRequestQueue(1, 1);

            Assert.True(queue.TryReserveReceive());
            Assert.False(queue.TryReserveReceive());

            queue.CompleteReceive();
            Assert.True(queue.TryReserveReceive());
        }

        // send quota 도 receive 와 독립적으로 관리해야 한다.
        // fan-out send pump 가 quota 초과 상태에서 같은 request queue 로 추가 posting 하지 않게 만든다.
        [Fact]
        public void TryReserveSend_WhenLimitReached_ReturnsFalseUntilCompletion()
        {
            RioRequestQueue queue = new RioRequestQueue(1, 1);

            Assert.True(queue.TryReserveSend());
            Assert.False(queue.TryReserveSend());

            queue.CompleteSend();
            Assert.True(queue.TryReserveSend());
        }
    }
}
```

- [ ] **Step 2: Run and verify Red**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-build --no-restore
```

Expected: fails because `RioRequestQueue` does not exist.

- [ ] **Step 3: Implement queue owners**

Create `src/Hps.Transport.Rio/RioCompletionQueue.cs`:

```csharp
using System;

namespace Hps.Transport
{
    /// <summary>
    /// RIO completion queue 수명을 소유한다.
    /// 초기 구현은 native handle 없이 Dispose 경계만 만들고, native CQ는 pump task 에서 연결한다.
    /// </summary>
    internal sealed class RioCompletionQueue : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            _disposed = true;
        }

        internal bool IsDisposed => _disposed;
    }
}
```

Create `src/Hps.Transport.Rio/RioRequestQueue.cs`:

```csharp
using System;

namespace Hps.Transport
{
    /// <summary>
    /// socket 별 RIO_RQ outstanding quota 를 관리한다.
    /// native RIO queue 는 synchronization 을 제공하지 않으므로 이 owner 를 통해 posting 을 직렬화한다.
    /// </summary>
    internal sealed class RioRequestQueue : IDisposable
    {
        private readonly object _gate;
        private readonly int _maxOutstandingReceive;
        private readonly int _maxOutstandingSend;
        private int _outstandingReceive;
        private int _outstandingSend;
        private bool _disposed;

        internal RioRequestQueue(int maxOutstandingReceive, int maxOutstandingSend)
        {
            if (maxOutstandingReceive <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxOutstandingReceive));
            if (maxOutstandingSend <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxOutstandingSend));

            _gate = new object();
            _maxOutstandingReceive = maxOutstandingReceive;
            _maxOutstandingSend = maxOutstandingSend;
        }

        internal bool TryReserveReceive()
        {
            lock (_gate)
            {
                if (_disposed || _outstandingReceive == _maxOutstandingReceive)
                    return false;

                _outstandingReceive++;
                return true;
            }
        }

        internal bool TryReserveSend()
        {
            lock (_gate)
            {
                if (_disposed || _outstandingSend == _maxOutstandingSend)
                    return false;

                _outstandingSend++;
                return true;
            }
        }

        internal void CompleteReceive()
        {
            lock (_gate)
            {
                if (_outstandingReceive != 0)
                    _outstandingReceive--;
            }
        }

        internal void CompleteSend()
        {
            lock (_gate)
            {
                if (_outstandingSend != 0)
                    _outstandingSend--;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
            }
        }
    }
}
```

- [ ] **Step 4: Run focused tests and commit**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-build --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
```

Expected: all tests pass.

Commit:

```powershell
git add src/Hps.Transport.Rio tests/Hps.Transport.Rio.Tests CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: add rio queue owners"
```

---

### Task 5: TCP Opt-In Transport Wiring

**Files:**
- Create: `src/Hps.Transport.Rio/RioConnection.cs`
- Modify: `src/Hps.Transport.Rio/RioTransport.cs`
- Create: `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`
- Modify: root state docs

**Interfaces:**
- Produces: `internal sealed class RioConnection : IDisposable`
- Produces: `RioTransport.ListenTcpAsync(...)`
- Produces: `RioTransport.ConnectTcpAsync(...)`

- [ ] **Step 1: Write the failing tests**

Create `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`:

```csharp
using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Hps.Buffers;
using Hps.Transport;
using Xunit;

namespace Hps.Transport.Rio.Tests
{
    public sealed class RioTransportTcpTests
    {
        // RIO TCP wiring 은 Windows/RIO available 환경에서만 실제 loopback 으로 검증한다.
        // unavailable 환경에서는 명시 opt-in backend 가 NotSupported 로 실패해야 fallback 판단이 가능하다.
        [Fact]
        public async Task ListenTcpAsync_WhenRioUnavailable_ThrowsNotSupportedException()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                RioCapabilityProbe.GetStatus() == RioCapabilityStatus.Available)
            {
                return;
            }

            using (RioTransport transport = new RioTransport())
            {
                await transport.StartAsync();

                await Assert.ThrowsAsync<NotSupportedException>(async delegate()
                {
                    await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                });
            }
        }
    }
}
```

- [ ] **Step 2: Run and verify Red**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-build --no-restore
```

Expected: fails if `RioTransport.ListenTcpAsync` message/type does not match the contract or if capability check is not wired.

- [ ] **Step 3: Implement opt-in guard**

Modify `RioTransport.ListenTcpAsync` and `ConnectTcpAsync`:

```csharp
        public override ValueTask<IConnectionListener> ListenTcpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException(nameof(localEndPoint));
            cancellationToken.ThrowIfCancellationRequested();
            EnsureStarted();
            EnsureRioAvailable();

            throw new NotSupportedException("RIO TCP listen socket wiring 은 다음 task 에서 구현합니다.");
        }

        public override ValueTask<IConnection> ConnectTcpAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
        {
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));
            cancellationToken.ThrowIfCancellationRequested();
            EnsureStarted();
            EnsureRioAvailable();

            throw new NotSupportedException("RIO TCP connect socket wiring 은 다음 task 에서 구현합니다.");
        }

        private void EnsureStarted()
        {
            if (!_started || _stopped)
                throw new InvalidOperationException("RIO Transport 가 실행 중이 아닙니다.");
        }

        private static void EnsureRioAvailable()
        {
            if (RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
                throw new NotSupportedException("현재 환경에서 Windows RIO function table 을 사용할 수 없습니다.");
        }
```

- [ ] **Step 4: Run focused tests and commit**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-build --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
```

Expected: all tests pass. In RIO unavailable environments, opt-in calls fail explicitly and default SAEA path remains green.

Commit:

```powershell
git add src/Hps.Transport.Rio tests/Hps.Transport.Rio.Tests CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: guard rio tcp opt-in"
```

---

### Task 5.5: Native Function Table Loader Hardening

**Files:**
- Modify: `src/Hps.Transport.Rio/RioNative.cs`
- Modify: `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`
- Modify: root state docs and decision docs

**Interfaces:**
- Produces: actual `WSAIoctl(SIO_GET_MULTIPLE_EXTENSION_FUNCTION_POINTER, WSAID_MULTIPLE_RIO)` call path
- Produces: populated internal RIO function table owner when Windows RIO is available
- Keeps: default `TransportFactory.CreateDefault()` returning SAEA

- [ ] **Step 1: Write the failing test**

Add a Windows-only assertion to `RioCapabilityProbeTests`:

```csharp
        // Windows RIO backend 는 실제 function table 을 얻을 수 있어야 이후 TCP pump 로 진입할 수 있다.
        // 이 테스트는 placeholder 로더가 항상 Unavailable 을 반환하는 상태를 막는 회귀 방어선이다.
        [Fact]
        public void GetStatus_WhenWindows_LoadsRioFunctionTable()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            Assert.Equal(RioCapabilityStatus.Available, RioCapabilityProbe.GetStatus());
        }
```

- [ ] **Step 2: Run and verify Red**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore
```

Expected on current Windows development machine: fails with `Expected: Available`, `Actual: Unavailable`.
Non-Windows environments return before asserting.

- [ ] **Step 3: Implement actual native load**

Implement `RioNative.TryLoadFunctionTableCore(...)` using:

- `SIO_GET_MULTIPLE_EXTENSION_FUNCTION_POINTER = _WSAIORW(IOC_WS2, 36)` (`0xC8000024`)
- `WSAID_MULTIPLE_RIO = 8509e081-96dd-4005-b165-9e2ee8c79e3f`
- `RIO_EXTENSION_FUNCTION_TABLE` sequential struct with `cbSize` and the RIO function pointers
- `WSAIoctl` from `Ws2_32.dll`

Validate that required function pointers such as receive/send, completion queue, request queue,
dequeue, notify, register/deregister buffer are non-zero before returning `Available`.

- [ ] **Step 4: Run focused tests and commit**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
git diff --check
```

Expected on the current Windows development machine: RIO tests include the actual function table load and pass.

Commit:

```powershell
git add src/Hps.Transport.Rio/RioNative.cs tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs docs/superpowers/plans/2026-06-25-windows-rio-backend.md DECISIONS.md docs/agent-state/decisions/2026-06.md CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: load rio function table"
```

---

### Task 5.6: Native Buffer Registration Delegate

**Files:**
- Modify: `src/Hps.Transport.Rio/RioNative.cs`
- Modify: `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`
- Modify: root state docs

**Interfaces:**
- Produces: `internal IntPtr RegisterBuffer(IntPtr dataBuffer, int dataLength)`
- Produces: `internal void DeregisterBuffer(IntPtr bufferId)`

- [ ] **Step 1: Write the failing test**

Add a Windows/RIO-available test that pins a `PinnedBlockMemoryPool` block and expects `RioNative`
to expose register/deregister operations. Use reflection only for the initial Red so the absence of
the operation boundary is an assertion failure rather than a compile failure.

- [ ] **Step 2: Run and verify Red**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore
```

Expected on the current Windows development machine: `Assert.NotNull()` failure for the missing
`RegisterBuffer` operation boundary.

- [ ] **Step 3: Implement and refactor**

Marshal `RIORegisterBuffer` and `RIODeregisterBuffer` from the loaded function table using
`UnmanagedFunctionPointer(CallingConvention.StdCall)`.
After Green, refactor the test from reflection to direct internal API calls through `InternalsVisibleTo`.

- [ ] **Step 4: Verify and commit**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
git diff --check
```

Commit:

```powershell
git add src/Hps.Transport.Rio/RioNative.cs tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs docs/superpowers/plans/2026-06-25-windows-rio-backend.md CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: add rio buffer registration delegates"
```

---

### Task 5.7: Native Completion Queue Delegates

**Files:**
- Modify: `src/Hps.Transport.Rio/RioNative.cs`
- Modify: `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`
- Modify: root state docs

**Interfaces:**
- Produces: `internal IntPtr CreateCompletionQueue(int queueSize)`
- Produces: `internal void CloseCompletionQueue(IntPtr completionQueue)`

- [ ] **Step 1: Write the failing test**

Add a Windows/RIO-available test that expects `RioNative` to expose completion queue create/close
operations and creates a small CQ with null notification completion.

- [ ] **Step 2: Run and verify Red**

Expected: `Assert.NotNull()` failure for missing `CreateCompletionQueue` operation boundary.

- [ ] **Step 3: Implement and refactor**

Marshal `RIOCreateCompletionQueue` and `RIOCloseCompletionQueue` from the loaded function table.
Use `NotificationCompletion = null` for the first polling/dequeue based pump path.
After Green, refactor the test from reflection to direct internal API calls.

- [ ] **Step 4: Verify and commit**

Run focused RIO tests, solution build/test, and `git diff --check`.

Commit:

```powershell
git add src/Hps.Transport.Rio/RioNative.cs tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs docs/superpowers/plans/2026-06-25-windows-rio-backend.md CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: add rio completion queue delegates"
```

---

### Task 5.8: Native Request Queue Delegate

**Files:**
- Modify: `src/Hps.Transport.Rio/RioNative.cs`
- Modify: `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`
- Modify: root state docs

**Interfaces:**
- Produces: `internal static Socket CreateTcpSocket()`
- Produces: `internal IntPtr CreateRequestQueue(Socket socket, int maxOutstandingReceive, int maxReceiveDataBuffers, int maxOutstandingSend, int maxSendDataBuffers, IntPtr receiveCompletionQueue, IntPtr sendCompletionQueue)`

- [ ] **Step 1: Write the failing test**

Add a Windows/RIO-available test that creates a CQ and expects `RioNative` to expose a request queue
creation operation for a TCP socket.

- [ ] **Step 2: Run and verify Red**

Expected first Red: missing `CreateRequestQueue` operation boundary.
If RQ creation returns null with a regular .NET socket, correct the socket creation path to use
`WSASocketW` with `WSA_FLAG_OVERLAPPED | WSA_FLAG_REGISTERED_IO`.

- [ ] **Step 3: Implement and refactor**

Marshal `RIOCreateRequestQueue` and add a Windows-only `CreateTcpSocket()` helper that owns a
`WSASocketW` handle through `SafeSocketHandle`. After Green, refactor the test to direct internal API calls.

- [ ] **Step 4: Verify and commit**

Run focused RIO tests, solution build/test, and `git diff --check`.

Commit:

```powershell
git add src/Hps.Transport.Rio/RioNative.cs tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs docs/superpowers/plans/2026-06-25-windows-rio-backend.md CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: add rio request queue delegate"
```

---

### Task 5.9: Native Completion Dequeue Delegate

**Files:**
- Modify: `src/Hps.Transport.Rio/RioNative.cs`
- Modify: `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`
- Modify: root state docs

**Interfaces:**
- Produces: `internal uint DequeueCompletion(IntPtr completionQueue, RioResult[] results)`
- Produces: `internal struct RioResult`

- [ ] **Step 1: Write the failing test**

Add a Windows/RIO-available test that first asserts the dequeue operation boundary exists.

- [ ] **Step 2: Run and verify Red**

Expected: missing `DequeueCompletion` operation boundary assertion failure.

- [ ] **Step 3: Implement and refactor**

Marshal `RIODequeueCompletion` and add a blittable `RioResult` struct matching SDK `RIORESULT`.
After Green, refactor the test to directly create a CQ and assert an empty CQ returns 0 completions.

- [ ] **Step 4: Verify and commit**

Run focused RIO tests, solution build/test, and `git diff --check`.

Commit:

```powershell
git add src/Hps.Transport.Rio/RioNative.cs tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs docs/superpowers/plans/2026-06-25-windows-rio-backend.md CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: add rio completion dequeue delegate"
```

---

### Task 5.10: Native Receive/Send Posting Delegate Surface

**Files:**
- Modify: `src/Hps.Transport.Rio/RioNative.cs`
- Modify: `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`
- Modify: root state docs

**Interfaces:**
- Produces: `internal bool Receive(IntPtr requestQueue, RioBufferSegment[] buffers, IntPtr requestContext)`
- Produces: `internal bool Send(IntPtr requestQueue, RioBufferSegment[] buffers, IntPtr requestContext)`
- Produces: `internal struct RioBufferSegment`

- [ ] **Step 1: Write the failing test**

Add a test that detects missing `Receive`/`Send` operation boundaries before pump wiring.

- [ ] **Step 2: Run and verify Red**

Expected: missing operation boundary assertion failure.

- [ ] **Step 3: Implement and refactor**

Marshal `RIOReceive` and `RIOSend` through a shared `RIO_BUF` posting delegate.
Add `RioBufferSegment` matching SDK `RIO_BUF`.
After Green, refactor the test to direct internal API calls and validate argument failures.

- [ ] **Step 4: Verify and commit**

Run focused RIO tests, solution build/test, and `git diff --check`.

Commit:

```powershell
git add src/Hps.Transport.Rio/RioNative.cs tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs docs/superpowers/plans/2026-06-25-windows-rio-backend.md CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: add rio send receive delegates"
```

---

### Task 5.11: Connected Native Posting Completion Verification

**Files:**
- Modify: `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`
- Modify: root state docs

**Interfaces:**
- Verifies: `RIOReceive` post -> peer send -> CQ completion -> registered buffer write
- Verifies: `RIOSend` post -> CQ completion -> peer receive

- [ ] **Step 1: Add connected posting tests**

Use a registered-I/O TCP socket created by `RioNative.CreateTcpSocket()` and a normal peer socket
connected over loopback. Register a pinned pool block, create CQ/RQ, post receive/send, and poll
`DequeueCompletion(...)` until one completion appears.

- [ ] **Step 2: Verify**

Run focused RIO tests, solution build/test, and `git diff --check`.

Expected: tests pass without additional production code if Task 5.6~5.10 native operation boundaries are correct.

Commit:

```powershell
git add tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs docs/superpowers/plans/2026-06-25-windows-rio-backend.md CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "test: verify rio native posting completion"
```

---

### Task 6: TCP Pump And Contract Test Reuse

**Files:**
- Modify: `src/Hps.Transport.Rio/RioTransport.cs`
- Modify: `src/Hps.Transport.Rio/RioConnection.cs`
- Modify: `src/Hps.Transport.Rio/RioCompletionQueue.cs`
- Modify: `src/Hps.Transport.Rio/RioRequestQueue.cs`
- Modify: `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`
- Modify: root state docs

**Interfaces:**
- Consumes: `TransportBase.CreateConnection()`, `TransportConnection.TryBeginInFlightSend(...)`
- Produces: RIO TCP receive/send pump that can satisfy backend-agnostic TCP loopback tests when RIO is available.

- [ ] **Step 1: Write the loopback test**

Add to `RioTransportTcpTests.cs`:

```csharp
        // RIO backend 가 available 인 Windows 환경에서는 기존 Transport TCP 계약과 같은 loopback 의미를 가져야 한다.
        // unavailable 환경은 skip이 아니라 return 으로 빠져 SAEA 기본 회귀와 분리한다.
        [Fact]
        public async Task TcpLoopback_WhenRioAvailable_EchoesPayload()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
            {
                return;
            }

            using (RioTransport transport = new RioTransport())
            {
                RecordingReceiveHandler handler = new RecordingReceiveHandler();
                transport.SetReceiveHandler(handler);
                await transport.StartAsync();

                IConnectionListener listener = await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                IConnection client = await transport.ConnectTcpAsync(listener.LocalEndPoint);
                IConnection server = await listener.AcceptAsync();

                PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
                RefCountedBuffer buffer = pool.RentCounted();
                buffer.Span[0] = 1;
                buffer.Span[1] = 2;
                buffer.Span[2] = 3;
                buffer.SetLength(3);
                buffer.AddRef();

                Assert.True(transport.TrySend(client, new TransportSendBuffer(buffer, 0, 3)));
                buffer.Release();

                byte[] payload = await handler.ReceiveAsync();
                Assert.Equal(new byte[] { 1, 2, 3 }, payload);

                client.Close();
                server.Close();
                listener.Close();
                await transport.StopAsync();
                Assert.Equal(0, pool.RentedCount);
            }
        }
```

Also add a local `RecordingReceiveHandler` test helper in the same file:

```csharp
        private sealed class RecordingReceiveHandler : ITransportReceiveHandler
        {
            private readonly TaskCompletionSource<byte[]> _received;

            internal RecordingReceiveHandler()
            {
                _received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public void OnReceived(IConnection connection, TransportReceiveBuffer receiveBuffer)
            {
                byte[] payload = receiveBuffer.Span.ToArray();
                _received.TrySetResult(payload);
            }

            public void OnConnectionClosed(IConnection connection)
            {
            }

            internal Task<byte[]> ReceiveAsync()
            {
                return _received.Task;
            }
        }
```

- [ ] **Step 2: Run and verify Red on a RIO-capable Windows machine**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --filter "FullyQualifiedName~TcpLoopback_WhenRioAvailable_EchoesPayload" --no-build --no-restore
```

Expected on RIO-capable Windows: fails at listen/connect wiring. Expected elsewhere: test returns without asserting RIO behavior.

- [ ] **Step 3: Implement the smallest working TCP path**

Implementation guidance:

- Use regular Winsock socket setup for bind/listen/connect/accept.
- After socket creation, create RIO request queue and attach completion queues.
- Post one receive per connection initially.
- On receive completion, dispatch via `TransportBase.ReadReceiveHandlerSnapshot()`.
- On send completion, call `InFlightSend.Complete()`.
- On close/error, call `TransportConnection.Close()`.
- Keep one worker draining the completion queue to avoid queue synchronization issues.

Do not add UDP, batching, automatic factory selection, or latency gates in this task.

- [ ] **Step 4: Reuse existing contract tests**

Add RIO-specific test cases only under `tests/Hps.Transport.Rio.Tests`.
Do not modify existing SAEA assertions except to extract reusable helpers if the extraction is small and behavior-preserving.

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-build --no-restore
dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --no-build --no-restore
dotnet test tests\Hps.Server.Tests\Hps.Server.Tests.csproj --no-build --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
```

Expected: all available tests pass. RIO-specific live loopback assertions execute only when RIO capability is available.

- [ ] **Step 5: Commit**

```powershell
git add src/Hps.Transport.Rio tests/Hps.Transport.Rio.Tests CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: wire rio tcp transport path"
```

---

## Self-Review

- Spec coverage: D097의 TCP-first, probe-first, SAEA default 유지, UDP/batching/default selection defer, registered buffer owner, queue owner, test reuse가 Task 1-6에 매핑된다.
- Placeholder scan: plan 내에는 빈 구현 지시 대신 각 task 의 test, expected result, 최소 interface, commit 경계가 있다.
- Type consistency: `RioCapabilityStatus`, `RioCapabilityProbe.GetStatus()`, `RioTransport`, `RioRegisteredBufferPool`, `RioRequestQueue`, `RioCompletionQueue` 이름은 task 간 동일하게 유지된다.
