# Linux io_uring Native Wrapper Shape Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Linux `io_uring_setup` 기반 native wrapper shape 를 `Hps.Transport.IoUring` 내부 타입으로 추가하고, capability probe 가 Linux 에서 작은 ring setup/close 를 시도할 수 있게 한다.

**Architecture:** `IoUringNative`는 syscall/mmap/munmap/close 호출과 ABI 구조체만 담당한다. `IoUringQueue`는 setup fd 와 SQ/CQ/SQE mmap 수명을 소유하고, `IoUringRegisteredBufferSet`은 fixed buffer registration 수명을 소유한다. `IoUringTransport`와 public `ITransport` 계약은 이번 계획에서 변경하지 않는다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, Linux `io_uring` syscall P/Invoke, `SafeHandle`, `PinnedBlockMemoryPool`.

## Global Constraints

- TFM은 `net9.0`, LangVersion은 C# 8.0을 유지한다.
- 모든 설명, 문서, 테스트 주석은 한국어로 작성한다.
- public `ITransport`/`IConnection` 계약은 넓히지 않는다.
- `TransportFactory.CreateDefault()`는 계속 `SaeaTransport`를 반환한다.
- 새 외부 NuGet/native dependency 를 추가하지 않는다.
- Windows/non-Linux 에서는 native syscall 을 호출하지 않는다.
- 실제 TCP/UDP pump, SQE submit/complete loop, Linux benchmark 는 이 계획 범위 밖이다.

---

## File Structure

- Create: `src/Hps.Transport.IoUring/Properties/AssemblyInfo.cs`
  - test assembly 에 internal wrapper shape 를 검증할 friend access 를 제공한다.
- Create: `src/Hps.Transport.IoUring/IoUringNative.cs`
  - platform/architecture guard, syscall/mmap/munmap/close P/Invoke, `io_uring_params` ABI struct 를 가진다.
- Create: `src/Hps.Transport.IoUring/IoUringSafeHandle.cs`
  - io_uring fd close owner.
- Create: `src/Hps.Transport.IoUring/IoUringMemoryMap.cs`
  - mmap pointer/length owner.
- Create: `src/Hps.Transport.IoUring/IoUringQueue.cs`
  - setup fd 와 SQ/CQ/SQE mmap owner.
- Create: `src/Hps.Transport.IoUring/IoUringRegisteredBufferSet.cs`
  - fixed buffer register/deregister owner.
- Modify: `src/Hps.Transport.IoUring/IoUringCapabilityProbe.cs`
  - Linux 에서 real setup/close probe 를 사용한다.
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringNativeShapeTests.cs`
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringQueueTests.cs`
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringRegisteredBufferSetTests.cs`
- Modify: root state documents at the end of each task.

---

### Task 1: Native ABI Shell And Platform Guard

**Files:**
- Create: `src/Hps.Transport.IoUring/Properties/AssemblyInfo.cs`
- Create: `src/Hps.Transport.IoUring/IoUringNative.cs`
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringNativeShapeTests.cs`
- Modify: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `docs/agent-state/changelog/2026-06.md`

**Interfaces:**
- Produces: `internal static class IoUringNative`
- Produces: `internal static IoUringCapabilityStatus GetPlatformStatus()`
- Produces: `internal static void ThrowIfUnsupportedPlatform()`

- [ ] **Step 1: Write the failing tests**

Create `tests/Hps.Transport.IoUring.Tests/IoUringNativeShapeTests.cs`.

```csharp
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Hps.Transport;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringNativeShapeTests
    {
        // native syscall adapter 는 transport 에서 raw P/Invoke 를 직접 만지지 않게 하는 첫 경계다.
        // reflection 으로 시작해 production type 부재를 compile failure 가 아니라 요구사항 failure 로 확인한다.
        [Fact]
        public void IoUringNative_TypeExists()
        {
            Type? type = Type.GetType("Hps.Transport.IoUringNative, Hps.Transport.IoUring");

            Assert.NotNull(type);
        }

        // non-Linux 에서는 syscall 번호나 mmap wrapper 로 들어가면 안 된다.
        // capability probe 와 transport unsupported boundary 가 같은 platform 판단을 공유해야 한다.
        [Fact]
        public void GetPlatformStatus_WhenNotLinux_ReturnsUnsupportedOperatingSystem()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            Type? type = Type.GetType("Hps.Transport.IoUringNative, Hps.Transport.IoUring");
            Assert.NotNull(type);

            MethodInfo? method = type!.GetMethod("GetPlatformStatus", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            object? status = method!.Invoke(null, null);

            Assert.Equal(IoUringCapabilityStatus.UnsupportedOperatingSystem, status);
        }

        // unsupported platform 은 명시적 NotSupportedException 으로 드러나야 한다.
        // 그래야 host selector 나 explicit backend 선택자가 fallback/error 를 구분할 수 있다.
        [Fact]
        public void ThrowIfUnsupportedPlatform_WhenNotLinux_ThrowsNotSupportedException()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            Type? type = Type.GetType("Hps.Transport.IoUringNative, Hps.Transport.IoUring");
            Assert.NotNull(type);

            MethodInfo? method = type!.GetMethod("ThrowIfUnsupportedPlatform", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(delegate()
            {
                method!.Invoke(null, null);
            });

            Assert.IsType<NotSupportedException>(exception.InnerException);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringNativeShapeTests -v minimal
```

Expected: `IoUringNative_TypeExists` and dependent tests fail with `Assert.NotNull() Failure`.

- [ ] **Step 3: Write minimal implementation**

Create `src/Hps.Transport.IoUring/Properties/AssemblyInfo.cs`.

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Hps.Transport.IoUring.Tests")]
```

Create `src/Hps.Transport.IoUring/IoUringNative.cs`.

```csharp
using System;
using System.Runtime.InteropServices;

namespace Hps.Transport
{
    /// <summary>
    /// Linux io_uring syscall 과 mmap entry point 를 숨기는 internal native adapter 다.
    ///
    /// 이 타입은 pointer 수명을 소유하지 않는다. raw native 호출과 platform guard 만 담당하고,
    /// fd/mmap/registration 수명은 별도 owner 타입이 관리한다.
    /// </summary>
    internal static class IoUringNative
    {
        internal static IoUringCapabilityStatus GetPlatformStatus()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return IoUringCapabilityStatus.UnsupportedOperatingSystem;

            if (RuntimeInformation.ProcessArchitecture != Architecture.X64 &&
                RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
            {
                return IoUringCapabilityStatus.Unavailable;
            }

            return IoUringCapabilityStatus.Available;
        }

        internal static void ThrowIfUnsupportedPlatform()
        {
            IoUringCapabilityStatus status = GetPlatformStatus();
            if (status == IoUringCapabilityStatus.UnsupportedOperatingSystem)
                throw new NotSupportedException("io_uring backend는 Linux에서만 사용할 수 있습니다.");

            if (status == IoUringCapabilityStatus.Unavailable)
                throw new NotSupportedException("현재 process architecture 에서는 io_uring syscall 번호가 정의되지 않았습니다.");
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringNativeShapeTests -v minimal
```

Expected: all `IoUringNativeShapeTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add src\Hps.Transport.IoUring\Properties\AssemblyInfo.cs src\Hps.Transport.IoUring\IoUringNative.cs tests\Hps.Transport.IoUring.Tests\IoUringNativeShapeTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md docs\agent-state\changelog\2026-06.md
git commit -m "feat: add iouring native guard"
```

---

### Task 2: Queue Setup Owner

**Files:**
- Create: `src/Hps.Transport.IoUring/IoUringSafeHandle.cs`
- Create: `src/Hps.Transport.IoUring/IoUringMemoryMap.cs`
- Create: `src/Hps.Transport.IoUring/IoUringQueue.cs`
- Modify: `src/Hps.Transport.IoUring/IoUringNative.cs`
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringQueueTests.cs`
- Modify: root state documents

**Interfaces:**
- Consumes: `IoUringNative.GetPlatformStatus()`
- Produces: `internal sealed class IoUringQueue : IDisposable`
- Produces: `internal static IoUringQueue CreateForProbe(uint entries)`
- Produces: `internal sealed class IoUringSafeHandle : SafeHandle`
- Produces: `internal sealed class IoUringMemoryMap : IDisposable`

- [ ] **Step 1: Write the failing tests**

Create `tests/Hps.Transport.IoUring.Tests/IoUringQueueTests.cs`.

```csharp
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringQueueTests
    {
        // queue owner 는 fd 와 mmap 수명을 transport 에서 분리하는 핵심 경계다.
        // 타입 부재를 먼저 assertion failure 로 고정한다.
        [Fact]
        public void IoUringQueue_TypeExists()
        {
            Type? type = Type.GetType("Hps.Transport.IoUringQueue, Hps.Transport.IoUring");

            Assert.NotNull(type);
        }

        // non-Linux 에서는 setup syscall 을 절대 호출하지 않아야 한다.
        // CreateForProbe 가 NotSupportedException 으로 수렴하면 Windows 개발 환경에서도 안전하게 테스트할 수 있다.
        [Fact]
        public void CreateForProbe_WhenNotLinux_ThrowsNotSupportedException()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            Type? type = Type.GetType("Hps.Transport.IoUringQueue, Hps.Transport.IoUring");
            Assert.NotNull(type);

            MethodInfo? method = type!.GetMethod("CreateForProbe", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(delegate()
            {
                method!.Invoke(null, new object[] { 2U });
            });

            Assert.IsType<NotSupportedException>(exception.InnerException);
        }

        // Linux 에서 kernel/seccomp 가 허용하면 작은 ring 을 만들고 즉시 닫을 수 있어야 한다.
        // unavailable 환경은 capability probe 가 처리하므로 이 테스트는 exception escape 만 막는다.
        [Fact]
        public void CreateForProbe_WhenLinux_DoesNotEscapeUnexpectedException()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            Type? type = Type.GetType("Hps.Transport.IoUringQueue, Hps.Transport.IoUring");
            Assert.NotNull(type);

            MethodInfo? method = type!.GetMethod("TryCreateForProbe", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            object? result = method!.Invoke(null, new object[] { 2U });

            Assert.NotNull(result);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringQueueTests -v minimal
```

Expected: `IoUringQueue_TypeExists` fails with `Assert.NotNull() Failure`.

- [ ] **Step 3: Implement queue owner**

Implementation requirements:

- `IoUringSafeHandle` derives from `SafeHandle` and calls `IoUringNative.CloseFileDescriptor(handle)`.
- `IoUringMemoryMap` stores pointer/length and calls `IoUringNative.Unmap(pointer, length)` once.
- `IoUringQueue.CreateForProbe(uint entries)` validates entries and calls `IoUringNative.ThrowIfUnsupportedPlatform()` before native setup.
- `IoUringQueue.TryCreateForProbe(uint entries)` returns a small result object rather than throwing for ordinary unavailable probe failures.
- `IoUringNative` adds syscall/mmap wrappers but keeps them internal.
- On Windows, only platform guard runs; no native syscall is called.

Key signatures:

```csharp
internal sealed class IoUringQueue : IDisposable
{
    internal static IoUringQueue CreateForProbe(uint entries);
    internal static IoUringQueueProbeResult TryCreateForProbe(uint entries);
    public void Dispose();
}

internal sealed class IoUringQueueProbeResult
{
    internal IoUringQueueProbeResult(IoUringCapabilityStatus status, int errorCode);
    internal IoUringCapabilityStatus Status { get; }
    internal int ErrorCode { get; }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringQueueTests -v minimal
```

Expected: all `IoUringQueueTests` pass on Windows. On Linux the probe test returns a non-null result.

- [ ] **Step 5: Commit**

```powershell
git add src\Hps.Transport.IoUring tests\Hps.Transport.IoUring.Tests CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md docs\agent-state\changelog\2026-06.md
git commit -m "feat: add iouring queue owner"
```

---

### Task 3: Capability Probe Uses Real Queue Probe

**Files:**
- Modify: `src/Hps.Transport.IoUring/IoUringCapabilityProbe.cs`
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringCapabilityProbeTests.cs`
- Modify: root state documents

**Interfaces:**
- Consumes: `IoUringQueue.TryCreateForProbe(uint entries)`
- Produces: real Linux setup/close capability probe path.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Hps.Transport.IoUring.Tests/IoUringCapabilityProbeTests.cs`.

```csharp
        // probe result mapping 을 별도 internal 경계로 고정한다.
        // 이 overload 가 없으면 public GetStatus 가 queue probe 결과를 쓰는 구조인지 테스트에서 확인할 수 없다.
        [Fact]
        public void GetStatus_WhenProbeResultIsAvailable_ReturnsAvailable()
        {
            Type? resultType = Type.GetType("Hps.Transport.IoUringQueueProbeResult, Hps.Transport.IoUring");
            Assert.NotNull(resultType);

            ConstructorInfo? constructor = resultType!.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(IoUringCapabilityStatus), typeof(int) },
                null);
            Assert.NotNull(constructor);

            object probeResult = constructor!.Invoke(new object[] { IoUringCapabilityStatus.Available, 0 });
            MethodInfo? method = typeof(IoUringCapabilityProbe).GetMethod(
                "GetStatus",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new Type[] { resultType },
                null);

            Assert.NotNull(method);

            object? status = method!.Invoke(null, new object[] { probeResult });

            Assert.Equal(IoUringCapabilityStatus.Available, status);
        }

        // Linux 에서는 실제 작은 ring setup probe 를 시도하되, kernel/seccomp 미지원은 Unavailable 로 수렴해야 한다.
        // 예외가 밖으로 나오면 host selector 가 capability 확인만으로 process failure 를 만들 수 있다.
        [Fact]
        public void GetStatus_WhenLinux_DoesNotThrowAndReturnsKnownStatus()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            IoUringCapabilityStatus status = IoUringCapabilityProbe.GetStatus();

            Assert.True(status == IoUringCapabilityStatus.Available || status == IoUringCapabilityStatus.Unavailable);
        }
```

Also add this using if it is missing:

```csharp
using System.Reflection;
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringCapabilityProbeTests -v minimal
```

Expected: `GetStatus_WhenProbeResultIsAvailable_ReturnsAvailable` fails with `Assert.NotNull()` for the missing internal overload.

- [ ] **Step 3: Implement probe wiring**

Change `IoUringCapabilityProbe.GetStatus()`:

```csharp
public static IoUringCapabilityStatus GetStatus()
{
    IoUringCapabilityStatus platformStatus = IoUringNative.GetPlatformStatus();
    if (platformStatus != IoUringCapabilityStatus.Available)
        return platformStatus;

    IoUringQueueProbeResult result = IoUringQueue.TryCreateForProbe(2);
    return result.Status;
}

internal static IoUringCapabilityStatus GetStatus(IoUringQueueProbeResult result)
{
    if (result == null)
        throw new ArgumentNullException(nameof(result));

    return result.Status;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringCapabilityProbeTests -v minimal
```

Expected: all capability probe tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src\Hps.Transport.IoUring\IoUringCapabilityProbe.cs tests\Hps.Transport.IoUring.Tests\IoUringCapabilityProbeTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md docs\agent-state\changelog\2026-06.md
git commit -m "feat: probe iouring setup"
```

---

### Task 4: Fixed Buffer Registration Owner Boundary

**Files:**
- Create: `src/Hps.Transport.IoUring/IoUringRegisteredBufferSet.cs`
- Modify: `src/Hps.Transport.IoUring/IoUringNative.cs`
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringRegisteredBufferSetTests.cs`
- Modify: root state documents

**Interfaces:**
- Consumes: `IoUringQueue`
- Produces: `internal sealed class IoUringRegisteredBufferSet : IDisposable`
- Produces: `internal static IoUringRegisteredBufferSet Register(IoUringQueue queue, byte[][] buffers)`

- [ ] **Step 1: Write the failing tests**

Create `tests/Hps.Transport.IoUring.Tests/IoUringRegisteredBufferSetTests.cs`.

```csharp
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringRegisteredBufferSetTests
    {
        // fixed buffer registration owner 는 pool block 수명과 kernel registration 수명을 분리한다.
        // 타입 부재를 먼저 assertion failure 로 고정한다.
        [Fact]
        public void IoUringRegisteredBufferSet_TypeExists()
        {
            Type? type = Type.GetType("Hps.Transport.IoUringRegisteredBufferSet, Hps.Transport.IoUring");

            Assert.NotNull(type);
        }

        // non-Linux 에서 registration 을 시도하면 syscall 로 들어가지 않고 명시적으로 막아야 한다.
        [Fact]
        public void Register_WhenNotLinux_ThrowsNotSupportedException()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            Type? type = Type.GetType("Hps.Transport.IoUringRegisteredBufferSet, Hps.Transport.IoUring");
            Assert.NotNull(type);

            MethodInfo? method = type!.GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(delegate()
            {
                method!.Invoke(null, new object?[] { null, Array.Empty<byte[]>() });
            });

            Assert.IsType<NotSupportedException>(exception.InnerException);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringRegisteredBufferSetTests -v minimal
```

Expected: type existence test fails with `Assert.NotNull() Failure`.

- [ ] **Step 3: Implement minimal owner boundary**

Implementation requirements:

- `Register` validates queue and buffer list.
- non-Linux calls `IoUringNative.ThrowIfUnsupportedPlatform()` and throws `NotSupportedException`.
- Linux path calls `io_uring_register` only when queue creation succeeded and buffers are non-empty.
- Dispose deregisters exactly once.
- This task does not wire registration into TCP/UDP send/recv pump.

- [ ] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringRegisteredBufferSetTests -v minimal
```

Expected: tests pass on Windows; Linux registration smoke can be added only when `IoUringCapabilityProbe.GetStatus()` is `Available`.

- [ ] **Step 5: Commit**

```powershell
git add src\Hps.Transport.IoUring tests\Hps.Transport.IoUring.Tests CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md docs\agent-state\changelog\2026-06.md
git commit -m "feat: add iouring fixed buffer registration owner"
```

---

### Task 5: State Documents And Full Verification

**Files:**
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/changelog/2026-06.md`
- Modify: `docs/agent-state/decisions/2026-06.md`

**Interfaces:**
- Consumes: Tasks 1-4 completion state.
- Produces: next execution point for TCP-first io_uring pump design.

- [ ] **Step 1: Update state documents**

Record that native wrapper shape is complete and that the next unit is TCP-first queue/pump design.

Decision wording:

```markdown
- D135 — io_uring native wrapper 는 syscall adapter, queue owner, fixed buffer registration owner 로 분리하고 TCP/UDP pump 는 후속 task 로 둔다.
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
- solution tests pass with non-zero discovered count.
- `git diff --check` exits 0. CRLF warnings are acceptable if no whitespace error is reported.

- [ ] **Step 3: Commit**

```powershell
git add CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs\agent-state\changelog\2026-06.md docs\agent-state\decisions\2026-06.md
git commit -m "docs: record iouring native wrapper boundary"
```

## Self-Review

- Spec coverage: D134의 native adapter, queue owner, fixed buffer registration owner, non-Linux guard, real Linux setup probe 요구를 Task 1~4가 각각 다룬다.
- Placeholder scan: 미완성 표식이나 임시 작성 문구는 없다.
- Type consistency: `IoUringNative`, `IoUringQueue`, `IoUringQueueProbeResult`, `IoUringRegisteredBufferSet` 이름을 task 간 동일하게 사용한다.
- Scope: TCP/UDP pump, zero-copy send, default backend promotion 은 계획 범위 밖으로 유지한다.
