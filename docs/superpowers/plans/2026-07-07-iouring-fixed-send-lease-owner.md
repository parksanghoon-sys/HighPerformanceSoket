# io_uring Fixed Send Lease Owner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `io_uring` TCP fixed-write production 연결 전에 payload ref-count lifetime 과 fixed buffer registration lifetime 을 하나의 in-flight lease 계약으로 고정한다.

**Architecture:** 새 internal `IoUringFixedSendLease`는 `TransportSendBuffer`가 가리키는 `RefCountedBuffer` slice 와 fixed buffer registration owner 를 함께 소유한다. 첫 구현은 production `IoUringTransport.SendInFlightAsync`를 바꾸지 않고 pure ownership contract 와 Linux capability gated native lease evidence 만 닫는다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, Linux `io_uring`, 기존 `PinnedBlockMemoryPool`/`RefCountedBuffer`/`TransportSendBuffer`.

## Global Constraints

- TFM은 `net9.0`, LangVersion은 `8.0`이다. C# 9+ 문법을 쓰지 않는다.
- 모든 문서, 주석, 테스트 의도 설명은 한국어로 작성한다.
- production 코드는 Red assertion failure 를 먼저 확인한 뒤 최소 구현으로 통과시킨다.
- `TransportFactory.CreateDefault()`는 변경하지 않는다.
- production TCP/UDP pump fixed-write 연결, UDP fixed-buffer send, zero-copy send, latency hard gate 는 이번 계획 범위가 아니다.
- `.claude/review/*` untracked 검토 문서는 stage 하지 않는다.

---

## File Structure

- Create: `src/Hps.Transport.IoUring/IoUringFixedSendLease.cs`
  - `TransportSendBuffer` payload slice 와 registration owner 를 함께 보존하는 internal lease.
  - lease dispose 가 payload ref 와 registration owner 를 정확히 1회 정리하는지 담당한다.
- Modify: `src/Hps.Transport.IoUring/IoUringRegisteredBufferSet.cs`
  - `IIoUringFixedBufferRegistration` internal interface 를 구현한다.
  - production registration owner 를 lease 에 주입 가능한 최소 surface 로 노출한다.
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringFixedSendLeaseTests.cs`
  - Windows/local 에서도 실행 가능한 pure ownership/shape tests.
  - Linux capability available 일 때만 native `WRITE_FIXED` lease evidence 를 실행한다.
- Modify: `docs/superpowers/plans/2026-07-07-iouring-fixed-send-lease-owner.md`
  - 각 task 완료 시 checkbox 를 갱신한다.
- Modify after each task: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`
  - 완료 단위, 검증 결과, 다음 실행 지점을 기록한다.

---

### Task 1: Pure Lease Ownership Contract

**Files:**
- Create: `src/Hps.Transport.IoUring/IoUringFixedSendLease.cs`
- Modify: `src/Hps.Transport.IoUring/IoUringRegisteredBufferSet.cs`
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringFixedSendLeaseTests.cs`
- Modify: `docs/superpowers/plans/2026-07-07-iouring-fixed-send-lease-owner.md`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes: `TransportSendBuffer`, `RefCountedBuffer`, `MemoryMarshal.TryGetArray`, `IoUringRegisteredBufferSet`.
- Produces:
  - `internal interface IIoUringFixedBufferRegistration : IDisposable`
  - `internal sealed class IoUringFixedSendLease : IDisposable`
  - `internal static IoUringFixedSendLease CreateForRegisteredBuffer(TransportSendBuffer sendBuffer, IIoUringFixedBufferRegistration registration)`
  - `internal byte[] RegisteredArray { get; }`
  - `internal int BufferIndex { get; }`
  - `internal int PayloadOffset { get; }`
  - `internal int PayloadLength { get; }`

- [x] **Step 1: Write the failing tests**

Add `tests/Hps.Transport.IoUring.Tests/IoUringFixedSendLeaseTests.cs`:

```csharp
using System;
using System.Reflection;
using Hps.Buffers;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringFixedSendLeaseTests
    {
        [Fact]
        public void LeaseContract_WhenInspected_ExposesPureOwnershipSurface()
        {
            // Red 단계가 컴파일 실패가 아니라 명확한 assertion failure 가 되도록 reflection 으로 contract surface 를 먼저 고정한다.
            Type? leaseType = typeof(IoUringQueue).Assembly.GetType("Hps.Transport.IoUringFixedSendLease");
            Type? registrationType = typeof(IoUringQueue).Assembly.GetType("Hps.Transport.IIoUringFixedBufferRegistration");

            Assert.NotNull(leaseType);
            Assert.NotNull(registrationType);
            Assert.NotNull(leaseType!.GetMethod("CreateForRegisteredBuffer", BindingFlags.Static | BindingFlags.NonPublic));
            Assert.NotNull(leaseType.GetProperty("RegisteredArray", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(leaseType.GetProperty("BufferIndex", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(leaseType.GetProperty("PayloadOffset", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(leaseType.GetProperty("PayloadLength", BindingFlags.Instance | BindingFlags.NonPublic));
        }

        [Fact]
        public void Lease_WhenDisposed_ReleasesPayloadRefAndRegistrationOnce()
        {
            // 이 테스트는 fixed-write pump 연결 전에 lease 가 Transport 소유 payload ref 와
            // native registration owner 를 정확히 한 번만 정리하는지 검증한다.
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(8);
            RefCountedBuffer buffer = pool.RentCounted();
            buffer.Memory.Span.Slice(0, 4).Fill(7);
            buffer.SetLength(4);
            buffer.AddRef();

            CountingRegistration registration = new CountingRegistration();
            IoUringFixedSendLease lease = IoUringFixedSendLease.CreateForRegisteredBuffer(
                new TransportSendBuffer(buffer, 1, 2),
                registration);

            Assert.Equal(1, pool.RentedCount);
            Assert.Equal(1, lease.BufferIndex);
            Assert.Equal(1, lease.PayloadOffset);
            Assert.Equal(2, lease.PayloadLength);

            lease.Dispose();
            lease.Dispose();

            Assert.Equal(1, registration.DisposeCount);
            Assert.Equal(1, pool.RentedCount);

            buffer.Release();
            Assert.Equal(0, pool.RentedCount);
        }

        [Fact]
        public void Lease_WhenSendBufferUsesSlice_ExposesUnderlyingArrayAndRange()
        {
            // 이 테스트는 WRITE_FIXED 에 넘길 pointer offset 이 RefCountedBuffer 전체 배열 기준 offset 이어야 함을 고정한다.
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(8);
            RefCountedBuffer buffer = pool.RentCounted();
            buffer.Memory.Span[0] = 10;
            buffer.Memory.Span[1] = 20;
            buffer.Memory.Span[2] = 30;
            buffer.Memory.Span[3] = 40;
            buffer.SetLength(4);
            buffer.AddRef();

            CountingRegistration registration = new CountingRegistration();
            IoUringFixedSendLease lease = IoUringFixedSendLease.CreateForRegisteredBuffer(
                new TransportSendBuffer(buffer, 1, 2),
                registration);

            Assert.NotNull(lease.RegisteredArray);
            Assert.Equal(1, lease.PayloadOffset);
            Assert.Equal(2, lease.PayloadLength);

            lease.Dispose();
            buffer.Release();
        }

        private sealed class CountingRegistration : IIoUringFixedBufferRegistration
        {
            public int DisposeCount { get; private set; }

            public int RegisteredBufferCount
            {
                get { return 1; }
            }

            public void Dispose()
            {
                DisposeCount++;
            }
        }
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter LeaseContract_WhenInspected_ExposesPureOwnershipSurface -v minimal
```

Expected: FAIL with `Assert.NotNull() Failure` because `IoUringFixedSendLease` and `IIoUringFixedBufferRegistration` do not exist.

Observed: `LeaseContract_WhenInspected_ExposesPureOwnershipSurface` failed with `Assert.NotNull() Failure`.
After the surface skeleton passed, the ownership behavior tests failed with `NotImplementedException`, then Green implementation closed them.

- [x] **Step 3: Write minimal implementation**

Add `src/Hps.Transport.IoUring/IoUringFixedSendLease.cs`:

```csharp
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Hps.Buffers;

namespace Hps.Transport
{
    internal interface IIoUringFixedBufferRegistration : IDisposable
    {
        int RegisteredBufferCount { get; }
    }

    internal sealed class IoUringFixedSendLease : IDisposable
    {
        private readonly TransportSendBuffer _sendBuffer;
        private readonly IIoUringFixedBufferRegistration _registration;
        private int _disposed;

        private IoUringFixedSendLease(
            TransportSendBuffer sendBuffer,
            IIoUringFixedBufferRegistration registration,
            byte[] registeredArray,
            int payloadOffset,
            int payloadLength)
        {
            _sendBuffer = sendBuffer;
            _registration = registration;
            RegisteredArray = registeredArray;
            BufferIndex = 0;
            PayloadOffset = payloadOffset;
            PayloadLength = payloadLength;
        }

        internal byte[] RegisteredArray { get; private set; }

        internal int BufferIndex { get; private set; }

        internal int PayloadOffset { get; private set; }

        internal int PayloadLength { get; private set; }

        internal static IoUringFixedSendLease CreateForRegisteredBuffer(
            TransportSendBuffer sendBuffer,
            IIoUringFixedBufferRegistration registration)
        {
            if (registration == null)
                throw new ArgumentNullException(nameof(registration));

            ArraySegment<byte> segment = GetPayloadSegment(sendBuffer);
            if (segment.Array == null)
                throw new InvalidOperationException("io_uring fixed send lease 는 pinned byte[] 기반 RefCountedBuffer 만 지원합니다.");

            return new IoUringFixedSendLease(sendBuffer, registration, segment.Array, segment.Offset, segment.Count);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try
            {
                _registration.Dispose();
            }
            finally
            {
                _sendBuffer.Buffer.Release();
            }
        }

        private static ArraySegment<byte> GetPayloadSegment(TransportSendBuffer sendBuffer)
        {
            RefCountedBuffer buffer = sendBuffer.Buffer;
            Memory<byte> memory = buffer.Memory.Slice(sendBuffer.Offset, sendBuffer.Length);
            ArraySegment<byte> segment;

            if (!MemoryMarshal.TryGetArray(memory, out segment))
                throw new InvalidOperationException("io_uring fixed send lease 는 pinned byte[] 기반 RefCountedBuffer 만 지원합니다.");

            return segment;
        }
    }
}
```

Modify the class declaration in `src/Hps.Transport.IoUring/IoUringRegisteredBufferSet.cs`:

```csharp
internal sealed class IoUringRegisteredBufferSet : IIoUringFixedBufferRegistration
```

- [x] **Step 4: Run tests to verify they pass**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter IoUringFixedSendLeaseTests -v minimal
```

Expected: PASS, 2 tests.

Observed: focused `IoUringFixedSendLeaseTests` passed, 3 tests.

- [x] **Step 5: Run focused project tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal
```

Expected: PASS, existing io_uring test count plus 2.

Observed: `Hps.Transport.IoUring.Tests` passed, 66 tests.

- [x] **Step 6: Commit**

```powershell
git add src/Hps.Transport.IoUring/IoUringFixedSendLease.cs src/Hps.Transport.IoUring/IoUringRegisteredBufferSet.cs tests/Hps.Transport.IoUring.Tests/IoUringFixedSendLeaseTests.cs docs/superpowers/plans/2026-07-07-iouring-fixed-send-lease-owner.md CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "test(iouring): cover fixed send lease ownership"
```

---

### Task 2: Lease Factory For Real Registration

**Files:**
- Modify: `src/Hps.Transport.IoUring/IoUringFixedSendLease.cs`
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringFixedSendLeaseTests.cs`
- Modify: `docs/superpowers/plans/2026-07-07-iouring-fixed-send-lease-owner.md`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - Task 1 `IoUringFixedSendLease`
  - `IoUringRegisteredBufferSet.Register(IoUringQueue queue, byte[][] buffers)`
- Produces:
  - `internal static IoUringFixedSendLease Create(IoUringQueue queue, TransportSendBuffer sendBuffer)`

- [x] **Step 1: Write the failing tests**

Append to `IoUringFixedSendLeaseTests`:

```csharp
[Fact]
public void LeaseFactory_WhenInspected_ExposesQueueBasedCreateMethod()
{
    // 다음 native evidence task 가 production helper 를 우회하지 않도록 queue 기반 factory shape 를 고정한다.
    System.Reflection.MethodInfo? method = typeof(IoUringFixedSendLease).GetMethod(
        "Create",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
        null,
        new Type[] { typeof(IoUringQueue), typeof(TransportSendBuffer) },
        null);

    Assert.NotNull(method);
}
```

- [x] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter LeaseFactory_WhenInspected_ExposesQueueBasedCreateMethod -v minimal
```

Expected: FAIL with `Assert.NotNull() Failure`.

Observed: `LeaseFactory_WhenInspected_ExposesQueueBasedCreateMethod` failed with `Assert.NotNull() Failure`.

- [x] **Step 3: Write minimal implementation**

Add to `IoUringFixedSendLease`:

```csharp
internal static IoUringFixedSendLease Create(IoUringQueue queue, TransportSendBuffer sendBuffer)
{
    if (queue == null)
        throw new ArgumentNullException(nameof(queue));

    ArraySegment<byte> segment = GetPayloadSegment(sendBuffer);
    if (segment.Array == null)
        throw new InvalidOperationException("io_uring fixed send lease 는 pinned byte[] 기반 RefCountedBuffer 만 지원합니다.");

    IoUringRegisteredBufferSet registration = IoUringRegisteredBufferSet.Register(
        queue,
        new byte[][] { segment.Array });

    return new IoUringFixedSendLease(sendBuffer, registration, segment.Array, segment.Offset, segment.Count);
}
```

- [x] **Step 4: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter IoUringFixedSendLeaseTests -v minimal
```

Expected: PASS, 3 tests.

Observed: focused `IoUringFixedSendLeaseTests` passed, 4 tests. `Hps.Transport.IoUring.Tests` passed, 67 tests.

- [x] **Step 5: Commit**

```powershell
git add src/Hps.Transport.IoUring/IoUringFixedSendLease.cs tests/Hps.Transport.IoUring.Tests/IoUringFixedSendLeaseTests.cs docs/superpowers/plans/2026-07-07-iouring-fixed-send-lease-owner.md CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat(iouring): add fixed send lease factory"
```

---

### Task 3: Linux Native Lease Evidence

**Files:**
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringFixedSendLeaseTests.cs`
- Modify: `docs/superpowers/plans/2026-07-07-iouring-fixed-send-lease-owner.md`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify if remote gate requires test project count updates only in docs.

**Interfaces:**
- Consumes:
  - Task 2 `IoUringFixedSendLease.Create(IoUringQueue, TransportSendBuffer)`
  - `IoUringQueue.TrySubmitWriteFixed(...)`
  - existing D197 socketpair helper pattern.
- Produces:
  - Linux capability gated test proving lease-owned registered buffer can be written to stream socket fd and disposed after completion.

- [x] **Step 1: Write the failing native evidence test**

Append to `IoUringFixedSendLeaseTests`:

```csharp
[Fact]
public void Lease_WhenLinuxCapabilityAvailable_WritesRegisteredPayloadSliceToSocketPair()
{
    // 이 테스트는 lease owner 가 registration lifetime 을 completion 이후까지 유지하고,
    // dispose 시 payload ref 와 registration owner 를 함께 정리하는지 Linux native path 로 검증한다.
    IoUringCapabilityStatus status = IoUringCapabilityProbe.GetStatus();
    if (status != IoUringCapabilityStatus.Available)
        return;

    PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(4);
    RefCountedBuffer buffer = pool.RentCounted();
    buffer.Memory.Span[0] = 10;
    buffer.Memory.Span[1] = 20;
    buffer.Memory.Span[2] = 30;
    buffer.Memory.Span[3] = 40;
    buffer.SetLength(4);
    buffer.AddRef();

    using (LinuxSocketPair socketPair = LinuxSocketPair.Create())
    using (IoUringQueue queue = IoUringQueue.CreateForProbe(4))
    {
        IoUringFixedSendLease lease = IoUringFixedSendLease.Create(
            queue,
            new TransportSendBuffer(buffer, 1, 2));

        const ulong token = 0x199UL;
        Assert.True(queue.TrySubmitWriteFixed(
            socketPair.WriterFileDescriptor,
            lease.RegisteredArray,
            lease.PayloadOffset,
            lease.PayloadLength,
            lease.BufferIndex,
            token));

        IoUringNative.Enter(queue.FileDescriptor, 0, 1, IoUringNative.EnterGetEvents);

        IoUringCompletion completion;
        Assert.True(queue.TryDequeueCompletion(out completion));
        Assert.Equal(token, completion.Token);
        Assert.Equal(2, completion.Result);
        Assert.Equal(new byte[] { 20, 30 }, socketPair.ReadExact(2));

        lease.Dispose();
    }

    Assert.Equal(1, pool.RentedCount);
    buffer.Release();
    Assert.Equal(0, pool.RentedCount);
}
```

Add a private `LinuxSocketPair` helper by copying the D197 helper shape from `IoUringFixedBufferSubmissionTests`, including `socketpair`, `read`, `close`, and `Dispose` members. Keep it private to this test file unless a later cleanup task deliberately extracts a shared helper.

Observed Red: `LinuxSocketPair_HelperExistsForLeaseNativeEvidence` failed with `Assert.NotNull() Failure`.

- [x] **Step 2: Run local focused test**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter Lease_WhenLinuxCapabilityAvailable_WritesRegisteredPayloadSliceToSocketPair -v minimal
```

Expected on Windows/local unavailable: PASS via capability guard early-return.
Expected on Linux available: PASS with completion result 2 and payload `{20,30}`.

Observed: local Windows focused native evidence test passed via capability guard.

- [x] **Step 3: Run full io_uring project tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal
```

Expected: PASS.

Observed: `Hps.Transport.IoUring.Tests` passed, 69 tests.

- [x] **Step 4: Run solution tests**

Run:

```powershell
dotnet test HighPerformanceSocket.slnx -v minimal
```

Expected: PASS. If Windows WPF restore/build constraints appear, use the same repository-supported command path already used by current state docs and record the exact limitation.

Observed: `dotnet test HighPerformanceSocket.slnx -v minimal` passed.

- [x] **Step 5: Commit**

```powershell
git add tests/Hps.Transport.IoUring.Tests/IoUringFixedSendLeaseTests.cs docs/superpowers/plans/2026-07-07-iouring-fixed-send-lease-owner.md CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "test(iouring): cover fixed send lease native write"
```

---

### Task 4: Remote Contract Gate Documentation

**Files:**
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/decisions/2026-07.md`

**Interfaces:**
- Consumes: Task 3 local implementation commit after user push.
- Produces: D20x remote interpretation decision.

- [ ] **Step 1: Run remote workflow after push**

Run after the implementation commits are pushed:

```powershell
gh workflow run iouring-linux-contract.yml --ref master
gh run watch <run-id> --exit-status
```

Expected: workflow conclusion success.

- [ ] **Step 2: Inspect artifact**

Download and inspect:

```powershell
gh run download <run-id> --name iouring-linux-contract-<date>-github-<run-id>-1 --dir <temp-dir>
```

Expected:

- `iouring-tests.trx` exists.
- `Lease_WhenLinuxCapabilityAvailable_WritesRegisteredPayloadSliceToSocketPair` outcome is Passed.
- stdout or TRX evidence shows capability `Available` and completion result `2`, or the test body asserts payload `{20,30}`.

- [ ] **Step 3: Record interpretation**

Update state docs:

- `DECISIONS.md`: add a decision that fixed-send lease owner native evidence passed but still does not imply production pump fixed-write promotion.
- `CURRENT_PLAN.md`: next candidate becomes production TCP pump integration design, unless remote gate failed.
- `TODOS.md`: move Task 3/remote gate to Completed and add the next current item.
- `CHANGELOG_AGENT.md`: record run id, artifact name, TRX counters, test outcome.

- [ ] **Step 4: Commit**

```powershell
git add CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs/agent-state/decisions/2026-07.md
git commit -m "docs(iouring): record fixed send lease gate"
```

---

## Self-Review

### Spec coverage

- D199의 핵심 gap 인 fixed buffer registration lifetime 과 `RefCountedBuffer` payload ref lifetime 결합은 Task 1에서 pure contract 로 다룬다.
- queue 기반 real registration factory 는 Task 2에서 다룬다.
- Linux native `WRITE_FIXED` evidence 는 Task 3에서 다룬다.
- remote artifact interpretation 은 Task 4에서 다룬다.
- production TCP pump 변경, UDP fixed-buffer send, zero-copy send, default promotion, latency hard gate 는 모든 task 에서 제외했다.

### Placeholder scan

이 계획에는 `TBD`, `TODO`, `implement later`, `fill in details`를 남기지 않았다.
각 task 는 실패 테스트, 실행 명령, 최소 구현, 검증 명령, 커밋 명령을 포함한다.

### Type consistency

모든 task 는 `IoUringFixedSendLease`, `IIoUringFixedBufferRegistration`,
`CreateForRegisteredBuffer`, `Create`, `RegisteredArray`, `BufferIndex`, `PayloadOffset`, `PayloadLength` 이름을 일관되게 사용한다.
