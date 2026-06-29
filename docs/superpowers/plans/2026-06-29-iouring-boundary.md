# Linux io_uring Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `Hps.Transport.IoUring`의 첫 경계를 skeleton/capability probe/non-Linux unsupported contract 로 추가한다.

**Architecture:** 새 backend 는 `ITransport` 뒤에 숨기는 opt-in project 로 시작한다. 첫 단위는 Linux native syscall 을 호출하지 않고, Windows에서도 검증 가능한 probe/status/default-factory-regression 과 unsupported boundary 만 고정한다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, `Hps.Transport.TransportBase`, Linux io_uring 후속 P/Invoke 경계.

## Global Constraints

- TFM은 `net9.0`, LangVersion은 C# 8.0을 유지한다.
- public `ITransport`/`IConnection` 계약은 io_uring 때문에 넓히지 않는다.
- `TransportFactory.CreateDefault()`는 계속 `SaeaTransport`를 반환한다.
- 첫 implementation boundary 에 실제 `io_uring_setup`/`io_uring_enter` P/Invoke 를 넣지 않는다.
- non-Linux 환경은 명시적 `UnsupportedOperatingSystem` 또는 `NotSupportedException`으로 수렴한다.
- 모든 코드 주석과 문서 설명은 한국어로 작성한다.
- 테스트에는 무엇을 검증하는지 한국어 주석을 남긴다.
- 코드 변경은 Red-Green-Refactor 순서로 진행하고 task 별 커밋을 만든다.

---

## File Structure

- Create: `src/Hps.Transport.IoUring/Hps.Transport.IoUring.csproj`
  - Linux io_uring backend project. 첫 task 에서는 `Hps.Transport`만 참조한다.
- Create: `src/Hps.Transport.IoUring/IoUringCapabilityStatus.cs`
  - `Available`, `UnsupportedOperatingSystem`, `Unavailable` probe 결과 enum.
- Create: `src/Hps.Transport.IoUring/IoUringCapabilityProbe.cs`
  - OS/capability probe. 첫 task 에서는 Linux면 `Unavailable`, non-Linux면 `UnsupportedOperatingSystem`을 반환한다.
- Create: `src/Hps.Transport.IoUring/IoUringTransport.cs`
  - `TransportBase`를 상속하는 opt-in root type. 첫 boundary 에서는 lifecycle no-op 과 unsupported operation boundary 만 제공한다.
- Create: `tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj`
  - io_uring boundary tests.
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringCapabilityProbeTests.cs`
  - probe/status/default factory regression tests.
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringTransportTests.cs`
  - transport lifecycle 와 non-Linux unsupported boundary tests.
- Modify: `HighPerformanceSocket.slnx`
  - source/test project 를 solution 에 추가한다.
- Modify: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`
  - 각 task 결과와 후속 native work 분리를 기록한다.

---

### Task 1: Project Skeleton And Capability Probe

**Files:**
- Create: `tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj`
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringCapabilityProbeTests.cs`
- Create: `src/Hps.Transport.IoUring/Hps.Transport.IoUring.csproj`
- Create: `src/Hps.Transport.IoUring/IoUringCapabilityStatus.cs`
- Create: `src/Hps.Transport.IoUring/IoUringCapabilityProbe.cs`
- Modify: `HighPerformanceSocket.slnx`

**Interfaces:**
- Produces: `public enum IoUringCapabilityStatus`
- Produces: `public static class IoUringCapabilityProbe`
- Produces: `public static IoUringCapabilityStatus GetStatus()`

- [ ] **Step 1: Write the failing tests**

Create `tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj` with no source project reference yet. This keeps the first Red as assertion failure instead of project-reference compile failure.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- 공통 TFM/LangVersion은 루트 Directory.Build.props를 따른다. -->
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

Create `tests/Hps.Transport.IoUring.Tests/IoUringCapabilityProbeTests.cs`.

```csharp
using System;
using System.Runtime.InteropServices;
using Hps.Transport;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringCapabilityProbeTests
    {
        // 첫 Red는 production project 부재를 reflection assertion failure 로 잡는다.
        // compile failure 가 아니라 "io_uring capability probe type 이 아직 없다"는 요구사항 실패를 보여준다.
        [Fact]
        public void IoUringCapabilityProbe_TypeExists()
        {
            Type? type = Type.GetType("Hps.Transport.IoUringCapabilityProbe, Hps.Transport.IoUring");

            Assert.NotNull(type);
        }

        // io_uring backend 는 Linux 전용 opt-in 경로다.
        // Windows 개발 환경에서 사용할 수 있다고 오판하면 default backend promotion 판단이 흔들린다.
        [Fact]
        public void GetStatus_WhenNotLinux_ReturnsUnsupportedOperatingSystem()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            Type? probeType = Type.GetType("Hps.Transport.IoUringCapabilityProbe, Hps.Transport.IoUring");
            Type? statusType = Type.GetType("Hps.Transport.IoUringCapabilityStatus, Hps.Transport.IoUring");

            Assert.NotNull(probeType);
            Assert.NotNull(statusType);

            object? status = probeType!.GetMethod("GetStatus")!.Invoke(null, null);
            object expected = Enum.Parse(statusType!, "UnsupportedOperatingSystem");

            Assert.Equal(expected, status);
        }

        // Phase 6 skeleton 이 생겨도 기본 factory 는 SAEA 기준선을 유지해야 한다.
        // io_uring은 Linux native pump 와 TCP/UDP contract matrix 가 준비되기 전까지 opt-in backend 다.
        [Fact]
        public void CreateDefault_DuringIoUringBoundaryPhase_ReturnsSaeaTransport()
        {
            ITransport transport = TransportFactory.CreateDefault();

            Assert.IsType<SaeaTransport>(transport);
            transport.Dispose();
        }
    }
}
```

Add the test project only to `HighPerformanceSocket.slnx`.

```xml
<Project Path="tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj" />
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal
```

Expected: `IoUringCapabilityProbe_TypeExists` fails with `Assert.NotNull() Failure`.

- [ ] **Step 3: Write minimal implementation**

Create `src/Hps.Transport.IoUring/Hps.Transport.IoUring.csproj`.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- 공통 TFM/LangVersion/Unsafe 설정은 루트 Directory.Build.props를 따른다. -->
  <ItemGroup>
    <ProjectReference Include="..\Hps.Transport\Hps.Transport.csproj" />
  </ItemGroup>

</Project>
```

Create `src/Hps.Transport.IoUring/IoUringCapabilityStatus.cs`.

```csharp
namespace Hps.Transport
{
    /// <summary>
    /// 현재 process에서 Linux io_uring backend를 사용할 수 있는지 나타내는 probe 결과다.
    /// </summary>
    public enum IoUringCapabilityStatus
    {
        Available = 0,
        UnsupportedOperatingSystem = 1,
        Unavailable = 2
    }
}
```

Create `src/Hps.Transport.IoUring/IoUringCapabilityProbe.cs`.

```csharp
using System.Runtime.InteropServices;

namespace Hps.Transport
{
    /// <summary>
    /// io_uring backend 사용 가능성을 부작용 없이 확인하는 진입점이다.
    ///
    /// 첫 boundary 에서는 Linux 여부만 판단한다. 실제 syscall probe 는 native wrapper task 에서
    /// 이 경계 뒤에 붙이며, 그 전까지 Linux 는 명시적으로 Unavailable 로 둔다.
    /// </summary>
    public static class IoUringCapabilityProbe
    {
        public static IoUringCapabilityStatus GetStatus()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return IoUringCapabilityStatus.UnsupportedOperatingSystem;

            return IoUringCapabilityStatus.Unavailable;
        }
    }
}
```

Update `tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj` to reference the source project.

```xml
<ProjectReference Include="..\..\src\Hps.Transport.IoUring\Hps.Transport.IoUring.csproj" />
```

Add the source project to `HighPerformanceSocket.slnx`.

```xml
<Project Path="src/Hps.Transport.IoUring/Hps.Transport.IoUring.csproj" />
```

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal
```

Expected: all `IoUringCapabilityProbeTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add HighPerformanceSocket.slnx src\Hps.Transport.IoUring tests\Hps.Transport.IoUring.Tests
git commit -m "feat: add iouring capability probe"
```

---

### Task 2: IoUringTransport Lifecycle And Unsupported Boundary

**Files:**
- Create: `src/Hps.Transport.IoUring/IoUringTransport.cs`
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringTransportTests.cs`
- Modify: `tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj`

**Interfaces:**
- Consumes: `IoUringCapabilityProbe.GetStatus()`
- Produces: `public sealed class IoUringTransport : TransportBase`

- [ ] **Step 1: Write the failing tests**

Create `tests/Hps.Transport.IoUring.Tests/IoUringTransportTests.cs`.

```csharp
using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Hps.Transport;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringTransportTests
    {
        // skeleton root 는 opt-in construction 과 Start/Stop 수명만 먼저 제공한다.
        // 이 경계가 안정적이어야 후속 native queue owner 를 같은 root type 에 붙일 수 있다.
        [Fact]
        public async Task IoUringTransport_WhenConstructed_StartStopDoesNotThrow()
        {
            Type? transportType = Type.GetType("Hps.Transport.IoUringTransport, Hps.Transport.IoUring");

            Assert.NotNull(transportType);

            using (ITransport transport = (ITransport)Activator.CreateInstance(transportType!)!)
            {
                await transport.StartAsync();
                await transport.StopAsync();
            }
        }

        // non-Linux 에서는 TCP listen 이 native 구현으로 진입하면 안 된다.
        // 명시적 NotSupportedException 으로 수렴해야 host selector 가 fallback 판단을 할 수 있다.
        [Fact]
        public async Task ListenTcpAsync_WhenNotLinux_ThrowsNotSupportedException()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            Type? transportType = Type.GetType("Hps.Transport.IoUringTransport, Hps.Transport.IoUring");

            Assert.NotNull(transportType);

            using (ITransport transport = (ITransport)Activator.CreateInstance(transportType!)!)
            {
                await transport.StartAsync();

                await Assert.ThrowsAsync<NotSupportedException>(async delegate()
                {
                    await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                });
            }
        }

        // non-Linux 에서는 TCP connect 도 같은 unsupported boundary 로 막는다.
        // listen/connect 가 서로 다른 예외 정책을 가지면 backend selector 의 오류 메시지가 흔들린다.
        [Fact]
        public async Task ConnectTcpAsync_WhenNotLinux_ThrowsNotSupportedException()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            Type? transportType = Type.GetType("Hps.Transport.IoUringTransport, Hps.Transport.IoUring");

            Assert.NotNull(transportType);

            using (ITransport transport = (ITransport)Activator.CreateInstance(transportType!)!)
            {
                await transport.StartAsync();

                await Assert.ThrowsAsync<NotSupportedException>(async delegate()
                {
                    await transport.ConnectTcpAsync(new IPEndPoint(IPAddress.Loopback, 9));
                });
            }
        }

        // UDP도 첫 boundary 에서는 지원한다고 보이면 안 된다.
        // 실제 ReceiveMsg/SendMsg owner 가 생기기 전까지 명시적으로 unsupported 로 남긴다.
        [Fact]
        public async Task BindUdpAsync_WhenNotLinux_ThrowsNotSupportedException()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            Type? transportType = Type.GetType("Hps.Transport.IoUringTransport, Hps.Transport.IoUring");

            Assert.NotNull(transportType);

            using (ITransport transport = (ITransport)Activator.CreateInstance(transportType!)!)
            {
                await transport.StartAsync();

                await Assert.ThrowsAsync<NotSupportedException>(async delegate()
                {
                    await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                });
            }
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringTransportTests -v minimal
```

Expected: tests fail with compile success and assertion/type failure because `IoUringTransport` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/Hps.Transport.IoUring/IoUringTransport.cs`.

```csharp
using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Hps.Transport
{
    /// <summary>
    /// Linux io_uring 기반 transport 의 opt-in root type 이다.
    ///
    /// 첫 boundary 에서는 native queue/pump 를 만들지 않는다. 대신 상위 public 계약을 넓히지 않고
    /// non-Linux 환경과 아직 구현되지 않은 native path 를 명시적 unsupported 로 수렴시킨다.
    /// </summary>
    public sealed class IoUringTransport : TransportBase
    {
        private readonly object _gate;
        private bool _started;
        private bool _stopped;

        public IoUringTransport()
        {
            _gate = new object();
        }

        public override ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (_stopped)
                    throw new InvalidOperationException("이미 중지된 io_uring Transport는 다시 시작할 수 없습니다.");

                _started = true;
            }

            return default(ValueTask);
        }

        public override ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                _stopped = true;
                _started = false;
            }

            return default(ValueTask);
        }

        public override ValueTask<IConnectionListener> ListenTcpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException(nameof(localEndPoint));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureStarted();
            ThrowUnsupported();
            throw new InvalidOperationException("도달할 수 없는 io_uring listen 경로입니다.");
        }

        public override ValueTask<IConnection> ConnectTcpAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
        {
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureStarted();
            ThrowUnsupported();
            throw new InvalidOperationException("도달할 수 없는 io_uring connect 경로입니다.");
        }

        public override ValueTask<IUdpEndpoint> BindUdpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException(nameof(localEndPoint));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureStarted();
            ThrowUnsupported();
            throw new InvalidOperationException("도달할 수 없는 io_uring UDP bind 경로입니다.");
        }

        private void EnsureStarted()
        {
            lock (_gate)
            {
                if (!_started || _stopped)
                    throw new InvalidOperationException("io_uring Transport가 시작되지 않았습니다.");
            }
        }

        private static void ThrowUnsupported()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                throw new NotSupportedException("io_uring backend는 Linux에서만 사용할 수 있습니다.");

            throw new NotSupportedException("io_uring native pump 는 아직 구현되지 않았습니다.");
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringTransportTests -v minimal
```

Expected: all `IoUringTransportTests` pass on Windows. On Linux the non-Linux tests return early and lifecycle test passes.

- [ ] **Step 5: Run project-level tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal
```

Expected: all io_uring boundary tests pass.

- [ ] **Step 6: Commit**

```powershell
git add src\Hps.Transport.IoUring\IoUringTransport.cs tests\Hps.Transport.IoUring.Tests\IoUringTransportTests.cs
git commit -m "feat: add iouring transport boundary"
```

---

### Task 3: State Documents And Full Verification

**Files:**
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/decisions/2026-06.md`
- Modify: `docs/agent-state/changelog/2026-06.md`

**Interfaces:**
- Consumes: Task 1 and Task 2 completion state.
- Produces: next execution point for native io_uring wrapper design.

- [ ] **Step 1: Update state documents**

Record that the Phase 6 boundary skeleton is complete and that the next unit is native io_uring syscall shape design.

Use this decision wording in `DECISIONS.md` and `docs/agent-state/decisions/2026-06.md`:

```markdown
- D133 — Phase 6 io_uring 첫 구현은 project skeleton/probe/unsupported boundary 로 제한하고 native syscall wrapper 는 후속 task 로 분리한다.
```

Use this next execution point in `CURRENT_PLAN.md`:

```markdown
다음 cycle 은 Linux io_uring native syscall wrapper shape 를 설계한다.
첫 skeleton/probe boundary 는 완료됐지만 실제 `io_uring_setup`, SQ/CQ mmap, fixed buffer registration,
TCP/UDP pump 는 아직 구현하지 않았다.
```

- [ ] **Step 2: Run validation**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore -v minimal
dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal
git diff --check
```

Expected:

- build exits 0.
- solution tests pass with a non-zero discovered test count.
- `git diff --check` exits 0. CRLF warnings are acceptable if no whitespace error is reported.

- [ ] **Step 3: Commit**

```powershell
git add CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs\agent-state\decisions\2026-06.md docs\agent-state\changelog\2026-06.md
git commit -m "docs: record iouring boundary skeleton"
```

## Self-Review

- Spec coverage: D132 requires skeleton/probe/unsupported boundary, default SAEA regression, and native work deferral. Task 1 covers project/probe/default regression. Task 2 covers transport root and non-Linux unsupported boundary. Task 3 covers state documents and full verification.
- Placeholder scan: this plan intentionally has no open placeholder markers.
- Type consistency: `IoUringCapabilityStatus`, `IoUringCapabilityProbe.GetStatus()`, and `IoUringTransport` are introduced before later tasks consume them.
