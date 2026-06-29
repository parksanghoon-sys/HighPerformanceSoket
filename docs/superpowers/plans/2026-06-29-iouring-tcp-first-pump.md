# TCP-first io_uring Queue/Pump Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `Hps.Transport.IoUring`에 TCP-first io_uring data pump 를 추가해 opt-in backend 가 실제 TCP receive/send 경로를 가질 수 있게 한다.

**Architecture:** `IoUringTransport`는 transport당 하나의 shared `IoUringQueue`와 completion loop 를 소유한다. TCP listener/connect control plane 은 첫 단계에서 .NET `Socket`을 사용하고, accepted/connected socket 의 receive/send data plane 만 io_uring SQE/CQE pump 로 보낸다. 공통 `TransportConnection` pending send queue, drop-oldest, close drain 계약은 그대로 재사용한다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, Linux `io_uring_enter`, shared SQ/CQ mmap, `PinnedBlockMemoryPool`, `TransportConnection`.

---

## File Structure

- Modify: `src/Hps.Transport.IoUring/IoUringNative.cs`
  - `io_uring_enter`, SQE/CQE ABI struct, TCP `RECV`/`SEND` opcode constants 를 추가한다.
- Modify: `src/Hps.Transport.IoUring/IoUringQueue.cs`
  - SQE allocation, SQ tail publish, CQE drain helper 를 추가한다.
- Create: `src/Hps.Transport.IoUring/IoUringOperationKind.cs`
  - receive/send/listener operation kind 를 구분한다.
- Create: `src/Hps.Transport.IoUring/IoUringCompletion.cs`
  - CQE result 를 managed value 로 보존한다.
- Create: `src/Hps.Transport.IoUring/IoUringOperationContext.cs`
  - reusable operation context 와 completion signal 을 소유한다.
- Create: `src/Hps.Transport.IoUring/IoUringOperationRegistry.cs`
  - SQE `user_data` token 과 operation context mapping 을 소유한다.
- Create: `src/Hps.Transport.IoUring/IoUringCompletionLoop.cs`
  - CQE drain 과 context completion dispatch 를 담당한다.
- Create: `src/Hps.Transport.IoUring/IoUringConnectionListener.cs`
  - TCP listen socket accept boundary 를 담당한다.
- Create: `src/Hps.Transport.IoUring/IoUringTcpConnectionResource.cs`
  - socket, receive block, prefix block, operation context, close resource 를 소유한다.
- Modify: `src/Hps.Transport.IoUring/IoUringTransport.cs`
  - shared queue/completion loop lifecycle, TCP listen/connect/accepted connection wiring, receive/send loop 를 연결한다.
- Create/Modify tests under `tests/Hps.Transport.IoUring.Tests/`
  - native ABI shape, operation registry, completion dispatch, TCP non-Linux boundary, Linux loopback smoke 를 검증한다.

---

### Task 1: Native SQE/CQE/Enter Shape

**Files:**
- Modify: `src/Hps.Transport.IoUring/IoUringNative.cs`
- Modify: `src/Hps.Transport.IoUring/IoUringQueue.cs`
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringSubmissionShapeTests.cs`
- Modify: root state documents

**Produced interfaces:**
- `internal const byte OperationReceive`
- `internal const byte OperationSend`
- `internal const uint EnterGetEvents`
- `internal struct IoUringSubmissionQueueEntry`
- `internal struct IoUringCompletionQueueEntry`
- `internal static int Enter(int fileDescriptor, uint toSubmit, uint minimumComplete, uint flags)`

- [ ] **Step 1: Write the failing test**

Create `tests/Hps.Transport.IoUring.Tests/IoUringSubmissionShapeTests.cs`.

```csharp
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringSubmissionShapeTests
    {
        // TCP pump 는 SQE opcode 와 CQE result layout 을 직접 사용한다.
        // ABI shape 가 없으면 transport 구현이 raw pointer 상수를 흩뿌리게 된다.
        [Fact]
        public void NativeSubmissionTypes_WhenInspected_ExposeTcpSendReceiveShape()
        {
            Type? sqeType = Type.GetType("Hps.Transport.IoUringSubmissionQueueEntry, Hps.Transport.IoUring");
            Type? cqeType = Type.GetType("Hps.Transport.IoUringCompletionQueueEntry, Hps.Transport.IoUring");
            Type? nativeType = Type.GetType("Hps.Transport.IoUringNative, Hps.Transport.IoUring");

            Assert.NotNull(sqeType);
            Assert.NotNull(cqeType);
            Assert.NotNull(nativeType);
            Assert.True(Marshal.SizeOf(sqeType!) >= 64);
            Assert.Equal(16, Marshal.SizeOf(cqeType!));
            Assert.NotNull(nativeType!.GetField("OperationReceive", BindingFlags.Static | BindingFlags.NonPublic));
            Assert.NotNull(nativeType.GetField("OperationSend", BindingFlags.Static | BindingFlags.NonPublic));
            Assert.NotNull(nativeType.GetMethod("Enter", BindingFlags.Static | BindingFlags.NonPublic));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringSubmissionShapeTests -v minimal
```

Expected: `Assert.NotNull()` failure for missing SQE/CQE shape.

- [ ] **Step 3: Write minimal implementation**

Add to `IoUringNative.cs`.

```csharp
internal const byte OperationReceive = 27;
internal const byte OperationSend = 26;
internal const uint EnterGetEvents = 0x1;
private const long IoUringEnterSyscallNumber = 426;

internal static int Enter(int fileDescriptor, uint toSubmit, uint minimumComplete, uint flags)
{
    if (fileDescriptor < 0)
        throw new ArgumentOutOfRangeException(nameof(fileDescriptor));

    ThrowIfUnsupportedPlatform();

    long result = SyscallIoUringEnter(
        IoUringEnterSyscallNumber,
        fileDescriptor,
        toSubmit,
        minimumComplete,
        flags,
        IntPtr.Zero,
        UIntPtr.Zero);
    if (result < 0)
        throw CreateNativeException("io_uring_enter");

    return checked((int)result);
}

[DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
private static extern long SyscallIoUringEnter(
    long number,
    int fileDescriptor,
    uint toSubmit,
    uint minimumComplete,
    uint flags,
    IntPtr sigset,
    UIntPtr sigsetSize);
```

Add ABI structs in the same namespace. Keep field names close to kernel ABI and add comments for user data ownership.

- [ ] **Step 4: Run test to verify it passes**

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringSubmissionShapeTests -v minimal
```

Expected: submission shape test passes.

- [ ] **Step 5: Commit**

```powershell
git add src\Hps.Transport.IoUring\IoUringNative.cs src\Hps.Transport.IoUring\IoUringQueue.cs tests\Hps.Transport.IoUring.Tests\IoUringSubmissionShapeTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md docs\agent-state\changelog\2026-06.md
git commit -m "feat: add iouring tcp submission shape"
```

---

### Task 2: Operation Registry And Completion Context

**Files:**
- Create: `src/Hps.Transport.IoUring/IoUringOperationKind.cs`
- Create: `src/Hps.Transport.IoUring/IoUringCompletion.cs`
- Create: `src/Hps.Transport.IoUring/IoUringOperationContext.cs`
- Create: `src/Hps.Transport.IoUring/IoUringOperationRegistry.cs`
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringOperationRegistryTests.cs`
- Modify: root state documents

**Produced interfaces:**
- `internal enum IoUringOperationKind`
- `internal readonly struct IoUringCompletion`
- `internal sealed class IoUringOperationContext`
- `internal sealed class IoUringOperationRegistry`

- [ ] **Step 1: Write the failing test**

Create `tests/Hps.Transport.IoUring.Tests/IoUringOperationRegistryTests.cs`.

```csharp
using System;
using System.Reflection;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringOperationRegistryTests
    {
        // SQE user_data 는 native completion 과 managed owner 를 연결하는 유일한 열쇠다.
        // token 이 재사용되거나 해제 후 resolve 되면 다른 connection 으로 completion 이 배달될 수 있다.
        [Fact]
        public void OperationRegistryTypes_WhenInspected_Exist()
        {
            Assert.NotNull(Type.GetType("Hps.Transport.IoUringOperationKind, Hps.Transport.IoUring"));
            Assert.NotNull(Type.GetType("Hps.Transport.IoUringCompletion, Hps.Transport.IoUring"));
            Assert.NotNull(Type.GetType("Hps.Transport.IoUringOperationContext, Hps.Transport.IoUring"));
            Assert.NotNull(Type.GetType("Hps.Transport.IoUringOperationRegistry, Hps.Transport.IoUring"));
        }

        // registry public shape 는 token register/resolve/unregister 경계를 제공해야 한다.
        // behavior test 는 타입이 생긴 뒤 direct internal API 로 보강한다.
        [Fact]
        public void OperationRegistry_WhenInspected_ExposesRequiredMethods()
        {
            Type? registryType = Type.GetType("Hps.Transport.IoUringOperationRegistry, Hps.Transport.IoUring");
            Assert.NotNull(registryType);

            Assert.NotNull(registryType!.GetMethod("Register", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(registryType.GetMethod("Resolve", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(registryType.GetMethod("TryResolve", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(registryType.GetMethod("Unregister", BindingFlags.Instance | BindingFlags.NonPublic));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringOperationRegistryTests -v minimal
```

Expected: `OperationRegistryTypes_WhenInspected_Exist` fails with `Assert.NotNull()` for missing production types.

- [ ] **Step 3: Write minimal implementation**

Implement a lock-protected dictionary and reusable context. `IoUringOperationContext.WaitAsync()` uses `TaskCompletionSource<IoUringCompletion>` in this first task; replace it with a reusable allocation-free source only after pump behavior is green.

Required behavior:
- tokens start at 1 and increment monotonically.
- `Resolve(long token)` throws `InvalidOperationException` when missing.
- `TryResolve` returns false when missing.
- `Complete` fails if no waiter is active or already completed.
- `Reset` prepares a context for the next operation.

- [ ] **Step 4: Run test to verify it passes**

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringOperationRegistryTests -v minimal
```

Expected: operation registry tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src\Hps.Transport.IoUring tests\Hps.Transport.IoUring.Tests CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md docs\agent-state\changelog\2026-06.md
git commit -m "feat: add iouring operation registry"
```

---

### Task 3: Shared Completion Loop Boundary

**Files:**
- Create: `src/Hps.Transport.IoUring/IoUringCompletionLoop.cs`
- Modify: `src/Hps.Transport.IoUring/IoUringQueue.cs`
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringCompletionLoopTests.cs`
- Modify: root state documents

**Produced interfaces:**
- `internal sealed class IoUringCompletionLoop : IDisposable`
- `internal void DispatchCompletion(IoUringCompletion completion)`
- `internal ValueTask StartAsync(CancellationToken cancellationToken)`
- `internal ValueTask StopAsync()`

- [ ] **Step 1: Write the failing test**

Create `tests/Hps.Transport.IoUring.Tests/IoUringCompletionLoopTests.cs`.

```csharp
using System.Threading.Tasks;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringCompletionLoopTests
    {
        // completion loop 는 CQE user_data 를 registry context 로 배달한다.
        // native syscall 없이 pure dispatch 를 먼저 고정하면 Linux 전용 pump 구현 전에도 routing bug 를 잡을 수 있다.
        [Fact]
        public async Task DispatchCompletion_WhenTokenMatches_CompletesRegisteredContext()
        {
            IoUringOperationRegistry registry = new IoUringOperationRegistry();
            IoUringOperationContext context = registry.Register(IoUringOperationKind.Receive);
            IoUringCompletionLoop loop = IoUringCompletionLoop.CreateForTests(registry);

            ValueTask<IoUringCompletion> wait = context.WaitAsync();
            loop.DispatchCompletion(new IoUringCompletion(context.Token, 12, 0));

            IoUringCompletion completion = await wait;

            Assert.Equal(12, completion.Result);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringCompletionLoopTests -v minimal
```

Expected: `IoUringCompletionLoop` type missing failure.

- [ ] **Step 3: Write minimal implementation**

Implement `CreateForTests` as an internal constructor path that does not open native resources. Production constructor receives `IoUringQueue`.
`DispatchCompletion` resolves the context and calls `Complete`. Missing token closes no transport yet; it throws `InvalidOperationException` so tests catch mapping bugs.

- [ ] **Step 4: Run test to verify it passes**

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringCompletionLoopTests -v minimal
```

Expected: completion loop pure dispatch test passes.

- [ ] **Step 5: Commit**

```powershell
git add src\Hps.Transport.IoUring tests\Hps.Transport.IoUring.Tests CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md docs\agent-state\changelog\2026-06.md
git commit -m "feat: add iouring completion loop"
```

---

### Task 4: TCP Resource And Listener Wiring

**Files:**
- Create: `src/Hps.Transport.IoUring/IoUringConnectionListener.cs`
- Create: `src/Hps.Transport.IoUring/IoUringTcpConnectionResource.cs`
- Modify: `src/Hps.Transport.IoUring/IoUringTransport.cs`
- Create/Modify: `tests/Hps.Transport.IoUring.Tests/IoUringTransportTcpTests.cs`
- Modify: root state documents

- [ ] **Step 1: Write the failing tests**

Create `tests/Hps.Transport.IoUring.Tests/IoUringTransportTcpTests.cs`.

```csharp
using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringTransportTcpTests
    {
        // TCP resource/listener 경계가 없으면 Listen/Accept wiring 이 IoUringTransport 한 파일에 섞인다.
        // 타입 경계를 먼저 Red 로 고정한다.
        [Fact]
        public void TcpResourceTypes_WhenInspected_Exist()
        {
            Assert.NotNull(Type.GetType("Hps.Transport.IoUringConnectionListener, Hps.Transport.IoUring"));
            Assert.NotNull(Type.GetType("Hps.Transport.IoUringTcpConnectionResource, Hps.Transport.IoUring"));
        }

        // explicit io_uring backend 는 지원되지 않는 OS에서 Socket bind/connect 를 시도하기 전에 실패해야 한다.
        [Fact]
        public async Task ListenTcpAsync_WhenNotLinux_ThrowsNotSupportedException()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            using (IoUringTransport transport = new IoUringTransport())
            {
                await transport.StartAsync();

                await Assert.ThrowsAsync<NotSupportedException>(async delegate()
                {
                    await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                });
            }
        }

        // default backend 는 Phase 6 중에도 SAEA 로 남아야 한다.
        [Fact]
        public void CreateDefault_DuringTcpPumpWork_ReturnsSaeaTransport()
        {
            using (ITransport transport = TransportFactory.CreateDefault())
            {
                Assert.IsType<SaeaTransport>(transport);
            }
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringTransportTcpTests -v minimal
```

Expected: `TcpResourceTypes_WhenInspected_Exist` fails with `Assert.NotNull()` for missing TCP resource/listener types.

- [ ] **Step 3: Implement resource/listener skeleton**

Implementation requirements:
- `IoUringTransport.StartAsync` creates queue/completion loop only when `IoUringCapabilityProbe.GetStatus() == Available`.
- non-Linux keeps explicit `NotSupportedException`.
- `IoUringConnectionListener` mirrors `RioConnectionListener` ownership: listen socket owner, accepted socket transferred to transport, listener unregisters on close.
- `IoUringTcpConnectionResource` owns socket, receive pool, receive/send operation contexts, and Dispose idempotently closes socket/context registrations.
- no receive/send SQE is submitted in this task.

- [ ] **Step 4: Run test to verify it passes**

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringTransportTcpTests -v minimal
```

Expected: TCP boundary tests pass on Windows. Linux may still throw pump-not-implemented until Task 5/6.

- [ ] **Step 5: Commit**

```powershell
git add src\Hps.Transport.IoUring tests\Hps.Transport.IoUring.Tests CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md docs\agent-state\changelog\2026-06.md
git commit -m "feat: wire iouring tcp resource boundary"
```

---

### Task 5: TCP Receive Pump

**Files:**
- Modify: `src/Hps.Transport.IoUring/IoUringTransport.cs`
- Modify: `src/Hps.Transport.IoUring/IoUringTcpConnectionResource.cs`
- Modify: `src/Hps.Transport.IoUring/IoUringQueue.cs`
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringTransportTcpTests.cs`
- Modify: root state documents

- [ ] **Step 1: Write the failing Linux-only test**

Add to `IoUringTransportTcpTests`.

```csharp
        // Linux 에서 capability 가 실제 available 일 때만 receive pump loopback 을 검증한다.
        // unavailable host 에서는 skip 성격의 return 으로 Windows/CI 기본 경로를 깨지 않는다.
        [Fact]
        public async Task TcpLoopback_WhenIoUringAvailable_DeliversReceivedBytes()
        {
            if (IoUringCapabilityProbe.GetStatus() != IoUringCapabilityStatus.Available)
                return;

            RecordingReceiveHandler handler = new RecordingReceiveHandler(expectedLength: 3);
            using (IoUringTransport transport = new IoUringTransport())
            {
                transport.SetReceiveHandler(handler);
                await transport.StartAsync();

                IConnectionListener listener = await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                Socket client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await client.ConnectAsync(listener.LocalEndPoint);
                IConnection server = await listener.AcceptAsync();

                byte[] payload = new byte[] { 1, 2, 3 };
                await client.SendAsync(payload, SocketFlags.None);

                byte[] received = await handler.ReceiveAsync();

                Assert.Equal(payload, received);
                client.Dispose();
                server.Close();
                listener.Close();
                await transport.StopAsync();
            }
        }
```

- [ ] **Step 2: Run test to verify it fails on Linux available hosts**

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~TcpLoopback_WhenIoUringAvailable_DeliversReceivedBytes -v minimal
```

Expected on Windows/unavailable Linux: pass by early return. Expected on io_uring available Linux before implementation: receive timeout or unsupported pump failure.

- [ ] **Step 3: Implement receive pump**

Implementation requirements:
- resource rents one receive block from `PinnedBlockMemoryPool`.
- submit `IORING_OP_RECV` with socket fd, buffer pointer, length, context token.
- completion result `0` or negative closes connection and notifies handler.
- positive completion dispatches `TransportReceiveBuffer` synchronously to current handler snapshot.
- handler exceptions notify close and stop receive loop.
- receive block is reused only after handler returns.

- [ ] **Step 4: Run receive tests**

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal
```

Expected: Windows shape tests pass; Linux available host receive loopback passes.

- [ ] **Step 5: Commit**

```powershell
git add src\Hps.Transport.IoUring tests\Hps.Transport.IoUring.Tests CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md docs\agent-state\changelog\2026-06.md
git commit -m "feat: add iouring tcp receive pump"
```

---

### Task 6: TCP Send Pump And Ownership

**Files:**
- Modify: `src/Hps.Transport.IoUring/IoUringTransport.cs`
- Modify: `src/Hps.Transport.IoUring/IoUringTcpConnectionResource.cs`
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringTransportTcpTests.cs`
- Modify: root state documents

- [ ] **Step 1: Write the failing Linux-only loopback tests**

Add tests for:
- small payload send/receive through two `IoUringTransport` connections.
- length-prefixed send using `TransportSendBuffer.WithLengthPrefix()`.
- large payload send with `PinnedBlockMemoryPool.RentedCount == 0` after close/stop.

Use existing `RioTransportTcpTests` helper shape as the reference, but keep the tests in `Hps.Transport.IoUring.Tests`.

- [ ] **Step 2: Run tests to verify they fail on available Linux**

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringTransportTcpTests -v minimal
```

Expected on Windows/unavailable Linux: unsupported/early-return tests pass. Expected on available Linux before send pump: loopback send tests fail.

- [ ] **Step 3: Implement send pump**

Implementation requirements:
- `StartSendLoop` mirrors RIO: wait on `TransportConnection.WaitForSendSignalAsync()`, drain `TryBeginInFlightSend`.
- length prefix uses pinned 4-byte scratch and sends before payload.
- payload send uses `MemoryMarshal.TryGetArray(sendBuffer.Buffer.Memory, out ArraySegment<byte>)`.
- each CQE may send fewer bytes; update offset/remaining until complete.
- `InFlightSend.Complete()` is called only after all bytes are sent.
- `using (inFlight)` handles exception/close unwind release.
- socket errors notify connection closed.

- [ ] **Step 4: Run tests**

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal
dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal
```

Expected: io_uring project passes; solution tests pass with non-zero discovered count.

- [ ] **Step 5: Commit**

```powershell
git add src\Hps.Transport.IoUring tests\Hps.Transport.IoUring.Tests CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md docs\agent-state\changelog\2026-06.md
git commit -m "feat: add iouring tcp send pump"
```

---

### Task 7: State Documents And Full Verification

**Files:**
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/changelog/2026-06.md`
- Modify: `docs/agent-state/decisions/2026-06.md`

- [ ] **Step 1: Update state documents**

Record the accepted TCP-first io_uring pump implementation boundary. Decision wording:

```markdown
- D137 — io_uring TCP-first pump 구현은 shared queue/completion loop 와 공통 TransportConnection send queue 로 수락한다.
```

- [ ] **Step 2: Run validation**

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
git commit -m "docs: record iouring tcp pump boundary"
```

## Self-Review

- Spec coverage: shared queue, operation registry, completion loop, listener/resource wiring, receive pump, send pump, close/ownership, non-Linux boundary 를 Task 1~7이 각각 다룬다.
- Placeholder scan: plan uses explicit task names, file paths, commands, expected outcomes, and contains no unresolved placeholder markers.
- Type consistency: `IoUringOperationContext`, `IoUringOperationRegistry`, `IoUringCompletionLoop`, `IoUringTcpConnectionResource`, `IoUringConnectionListener` names are used consistently.
- Scope: UDP pump, fixed payload registration cache, zero-copy send, Linux benchmark, default promotion are excluded.
