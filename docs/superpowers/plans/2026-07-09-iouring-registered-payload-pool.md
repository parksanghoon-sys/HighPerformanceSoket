# io_uring Registered Payload Pool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** TCP publish payload 를 io_uring queue lifetime 에 묶인 registered block source 에 담아, hit case 에서 payload `WRITE_FIXED`를 per-send registration 없이 제출할 수 있게 한다.

**Architecture:** 먼저 `Hps.Buffers`에서 `RefCountedBuffer` 반환 owner/source 경계를 일반화한다. 그 다음 `TcpFrameAssembler`가 concrete pool 대신 source 를 받게 하고, 마지막으로 `Hps.Transport.IoUring` 내부 registered payload pool 을 만들어 TCP receive/publish/send path 에 opt-in 으로 연결한다. UDP, default backend promotion, zero-copy 성능 주장, latency hard gate 는 제외한다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, Windows local tests, Linux `iouring-linux-contract.yml` capability-gated native tests.

---

## TDD Rule For This Plan

프로젝트 규칙상 Red 는 컴파일 실패가 아니라 assertion failure 여야 한다.
따라서 새 타입이나 새 생성자가 아직 없을 때는 먼저 `Type.GetType(...)`, `GetConstructor(...)`, `GetMethod(...)` 기반
shape test 로 assertion failure 를 확인한다. 그 shape 를 최소 구현한 뒤 behavior test 를 추가하고 다시 Red/Green 을 진행한다.

## File Structure

- Modify: `src/Hps.Buffers/RefCountedBuffer.cs`
  - concrete `PinnedBlockMemoryPool` 대신 `IRefCountedBufferOwner`로 반환한다.
- Modify: `src/Hps.Buffers/PinnedBlockMemoryPool.cs`
  - `IRefCountedBufferOwner`, `IRefCountedBufferSource` 구현체가 된다.
- Create: `src/Hps.Buffers/IRefCountedBufferOwner.cs`
  - 마지막 `Release()`에서 block 을 돌려받는 owner 계약이다.
- Create: `src/Hps.Buffers/IRefCountedBufferSource.cs`
  - frame assembler 가 counted buffer 를 대여하는 source 계약이다.
- Modify: `tests/Hps.Buffers.Tests/RefCountedBufferTests.cs`
  - owner interface 반환 경계와 기존 pool behavior 유지 테스트를 추가한다.
- Modify: `src/Hps.Protocol/TcpFrameAssembler.cs`
  - 생성자는 유지하되 내부 필드는 `IRefCountedBufferSource`로 바꾼다.
- Modify: `tests/Hps.Protocol.Tests/TcpFrameAssemblerTests.cs`
  - injected source 대여, fallback 생성자 유지, partial dispose release 를 검증한다.
- Create: `src/Hps.Transport.IoUring/IoUringRegisteredPayloadBlockPool.cs`
  - fixed capacity registered payload block pool, owner, source 후보, fixed index lookup 을 담당한다.
- Create: `src/Hps.Transport.IoUring/IoUringCompositePayloadBufferSource.cs`
  - registered pool 을 먼저 시도하고 miss 때 fallback source 를 명시적으로 사용하는 TCP source 다.
- Create: `src/Hps.Transport/Abstractions/ITransportPayloadBufferSourceProvider.cs`
  - Server 가 backend 타입을 직접 알지 않고 transport preferred TCP payload source 를 얻는 선택적 seam 이다.
- Modify: `src/Hps.Server/BrokerServer.cs`
  - `StartTcpAsync`에서 transport start 이후 receive handler 를 구성하고 provider source 를 사용한다.
- Modify: `src/Hps.Transport.IoUring/IoUringTransport.cs`
  - payload source provider 구현과 send helper 호출을 opt-in 으로 연결한다.
- Modify/Create tests under `tests/Hps.Transport.IoUring.Tests/`
  - pure pool, native registration, send helper path, Linux native loopback evidence 를 검증한다.

---

### Task 1: Buffer owner/source abstraction

**Files:**
- Create: `src/Hps.Buffers/IRefCountedBufferOwner.cs`
- Create: `src/Hps.Buffers/IRefCountedBufferSource.cs`
- Modify: `src/Hps.Buffers/PinnedBlockMemoryPool.cs`
- Modify: `src/Hps.Buffers/RefCountedBuffer.cs`
- Modify: `tests/Hps.Buffers.Tests/RefCountedBufferTests.cs`

- [ ] **Step 1: Write the failing owner/source contract tests**

Add these tests to `tests/Hps.Buffers.Tests/RefCountedBufferTests.cs`.

```csharp
// owner abstraction 테스트: RefCountedBuffer 가 concrete PinnedBlockMemoryPool 이 아니라
// IRefCountedBufferOwner 로 마지막 반환을 수행해야 io_uring registered slot owner 도 같은 수명 계약을 사용할 수 있다.
[Fact]
public void Release_WhenConstructedWithOwnerInterface_ReturnsBlockToOwnerExactlyOnce()
{
    TestBufferOwner owner = new TestBufferOwner(16);
    byte[] block = new byte[16];
    RefCountedBuffer buffer = new RefCountedBuffer(owner, block);

    buffer.AddRef();
    buffer.Release();
    Assert.Equal(0, owner.ReturnCount);

    buffer.Release();
    Assert.Equal(1, owner.ReturnCount);
    Assert.Same(block, owner.ReturnedBlock);
}

// source abstraction 테스트: 기존 pool 은 owner 뿐 아니라 source 로도 동작해야
// TcpFrameAssembler 의 기존 생성자와 새 source 생성자가 같은 대여 경계를 공유할 수 있다.
[Fact]
public void PinnedBlockMemoryPool_WhenUsedAsBufferSource_RentsCountedBuffer()
{
    IRefCountedBufferSource source = new PinnedBlockMemoryPool(32);

    RefCountedBuffer buffer = source.RentCounted();

    Assert.Equal(32, source.BlockSize);
    Assert.Equal(32, buffer.Memory.Length);
    buffer.Release();
}

private sealed class TestBufferOwner : IRefCountedBufferOwner
{
    internal TestBufferOwner(int blockSize)
    {
        BlockSize = blockSize;
    }

    public int BlockSize { get; private set; }

    internal int ReturnCount { get; private set; }

    internal byte[]? ReturnedBlock { get; private set; }

    public void Return(byte[] block)
    {
        ReturnCount++;
        ReturnedBlock = block;
    }
}
```

- [ ] **Step 2: Run the focused Red tests**

Run:

```powershell
dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~RefCountedBufferTests" -v minimal
```

Expected: assertion failure from the reflection shape part because `IRefCountedBufferOwner`, `IRefCountedBufferSource`, and the owner-based `RefCountedBuffer` constructor do not exist yet.

- [ ] **Step 3: Add the owner/source contracts**

Create `src/Hps.Buffers/IRefCountedBufferOwner.cs`.

```csharp
namespace Hps.Buffers
{
    /// <summary>
    /// RefCountedBuffer 의 마지막 Release 가 내부 block 을 돌려줄 owner 계약이다.
    /// 풀 구현뿐 아니라 registered slot owner 도 같은 반환 경계를 사용할 수 있다.
    /// </summary>
    public interface IRefCountedBufferOwner
    {
        int BlockSize { get; }

        void Return(byte[] block);
    }
}
```

Create `src/Hps.Buffers/IRefCountedBufferSource.cs`.

```csharp
namespace Hps.Buffers
{
    /// <summary>
    /// TCP frame assembler 같은 상위 조립기가 counted payload buffer 를 대여하는 source 계약이다.
    /// source 구현은 내부에서 일반 pool, registered pool, composite fallback 중 하나를 선택할 수 있다.
    /// </summary>
    public interface IRefCountedBufferSource
    {
        int BlockSize { get; }

        RefCountedBuffer RentCounted();
    }
}
```

- [ ] **Step 4: Update pool and buffer implementation**

Change `PinnedBlockMemoryPool` declaration:

```csharp
public sealed class PinnedBlockMemoryPool : IRefCountedBufferOwner, IRefCountedBufferSource
```

Change `RefCountedBuffer` fields/constructor:

```csharp
private readonly IRefCountedBufferOwner _owner;

public RefCountedBuffer(IRefCountedBufferOwner owner, byte[] block)
{
    if (owner == null)
        throw new ArgumentNullException(nameof(owner));
    if (block == null)
        throw new ArgumentNullException(nameof(block));
    if (block.Length != owner.BlockSize)
        throw new ArgumentException("버퍼 블록 길이가 owner BlockSize 와 일치해야 한다.", nameof(block));

    _owner = owner;
    _block = block;
    _refCount = 1;
}
```

In `ReturnToPoolOnce()` replace:

```csharp
_pool.Return(block);
```

with:

```csharp
_owner.Return(block);
```

Update XML comments so they refer to owner, not only `PinnedBlockMemoryPool`.

- [ ] **Step 5: Run focused Green tests**

Run:

```powershell
dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~RefCountedBufferTests" -v minimal
```

Expected: all `RefCountedBufferTests` pass.

- [ ] **Step 6: Run buffer project tests**

Run:

```powershell
dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj -v minimal
```

Expected: all buffer tests pass.

- [ ] **Step 7: Commit Task 1**

```powershell
git add src\Hps.Buffers\IRefCountedBufferOwner.cs src\Hps.Buffers\IRefCountedBufferSource.cs src\Hps.Buffers\PinnedBlockMemoryPool.cs src\Hps.Buffers\RefCountedBuffer.cs tests\Hps.Buffers.Tests\RefCountedBufferTests.cs
git commit -m "feat(buffers): abstract ref counted buffer owner"
```

---

### Task 2: TcpFrameAssembler source injection

**Files:**
- Modify: `src/Hps.Protocol/TcpFrameAssembler.cs`
- Modify: `tests/Hps.Protocol.Tests/TcpFrameAssemblerTests.cs`

- [ ] **Step 1: Write the failing injected-source tests**

Add to `tests/Hps.Protocol.Tests/TcpFrameAssemblerTests.cs`.

```csharp
// source injection 테스트: io_uring registered payload pool 을 TCP assembler 에 주입하려면
// assembler 가 concrete PinnedBlockMemoryPool 이 아니라 IRefCountedBufferSource 에서 payload block 을 대여해야 한다.
[Fact]
public void TryReadFrame_WhenSourceConstructorIsUsed_RentsPayloadFromInjectedSource()
{
    CountingBufferSource source = new CountingBufferSource(16);
    TcpFrameAssembler assembler = new TcpFrameAssembler(source, 8);
    RefCountedBuffer? frame;
    int consumed;

    TcpFrameReadStatus status = assembler.TryReadFrame(CreateWireFrame(new byte[] { 1, 2, 3 }), out consumed, out frame);

    Assert.Equal(TcpFrameReadStatus.FrameReady, status);
    Assert.Equal(1, source.RentCount);
    AssertFramePayload(source.Pool, frame, new byte[] { 1, 2, 3 });
}

// 기존 생성자 호환성 테스트: SAEA/RIO/baseline 경로는 계속 PinnedBlockMemoryPool 생성자를 사용하므로
// source 생성자를 추가해도 기존 생성자 behavior 와 누수 0 계약이 유지되어야 한다.
[Fact]
public void TryReadFrame_WhenPoolConstructorIsUsed_StillReturnsPoolBackedFrame()
{
    PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
    TcpFrameAssembler assembler = new TcpFrameAssembler(pool, 8);
    RefCountedBuffer? frame;
    int consumed;

    TcpFrameReadStatus status = assembler.TryReadFrame(CreateWireFrame(new byte[] { 7, 8 }), out consumed, out frame);

    Assert.Equal(TcpFrameReadStatus.FrameReady, status);
    AssertFramePayload(pool, frame, new byte[] { 7, 8 });
    Assert.Equal(0, pool.RentedCount);
}

private sealed class CountingBufferSource : IRefCountedBufferSource
{
    internal CountingBufferSource(int blockSize)
    {
        Pool = new PinnedBlockMemoryPool(blockSize);
    }

    internal PinnedBlockMemoryPool Pool { get; private set; }

    public int BlockSize
    {
        get { return Pool.BlockSize; }
    }

    internal int RentCount { get; private set; }

    public RefCountedBuffer RentCounted()
    {
        RentCount++;
        return Pool.RentCounted();
    }
}
```

- [ ] **Step 2: Run focused Red tests**

Run:

```powershell
dotnet test tests\Hps.Protocol.Tests\Hps.Protocol.Tests.csproj --filter "FullyQualifiedName~TcpFrameAssemblerTests" -v minimal
```

Expected: assertion failure from a constructor shape check because `TcpFrameAssembler(IRefCountedBufferSource, int)` does not exist.

- [ ] **Step 3: Update assembler constructor and field**

In `src/Hps.Protocol/TcpFrameAssembler.cs`, replace:

```csharp
private readonly PinnedBlockMemoryPool _pool;
```

with:

```csharp
private readonly IRefCountedBufferSource _source;
```

Keep existing constructor and delegate to the new constructor:

```csharp
public TcpFrameAssembler(PinnedBlockMemoryPool pool, int maxPayloadLength)
    : this((IRefCountedBufferSource)pool, maxPayloadLength)
{
}

public TcpFrameAssembler(IRefCountedBufferSource source, int maxPayloadLength)
{
    if (source == null)
        throw new ArgumentNullException(nameof(source));
    if (maxPayloadLength < 0)
        throw new ArgumentOutOfRangeException(nameof(maxPayloadLength));
    if (maxPayloadLength > source.BlockSize)
        throw new ArgumentOutOfRangeException(nameof(maxPayloadLength), "최대 payload 길이는 source block 크기를 넘을 수 없다.");

    _source = source;
    _maxPayloadLength = maxPayloadLength;
    _header = new byte[HeaderLength];
    _expectedPayloadLength = -1;
}
```

In `ReadHeader`, replace:

```csharp
_payload = _pool.RentCounted();
```

with:

```csharp
_payload = _source.RentCounted();
```

- [ ] **Step 4: Run focused Green tests**

Run:

```powershell
dotnet test tests\Hps.Protocol.Tests\Hps.Protocol.Tests.csproj --filter "FullyQualifiedName~TcpFrameAssemblerTests" -v minimal
```

Expected: all assembler tests pass.

- [ ] **Step 5: Run protocol tests**

Run:

```powershell
dotnet test tests\Hps.Protocol.Tests\Hps.Protocol.Tests.csproj -v minimal
```

Expected: all protocol tests pass.

- [ ] **Step 6: Commit Task 2**

```powershell
git add src\Hps.Protocol\TcpFrameAssembler.cs tests\Hps.Protocol.Tests\TcpFrameAssemblerTests.cs
git commit -m "feat(protocol): inject frame payload buffer source"
```

---

### Task 3: io_uring registered payload pool pure contract

**Files:**
- Create: `src/Hps.Transport.IoUring/IoUringRegisteredPayloadBlockPool.cs`
- Modify/Create: `tests/Hps.Transport.IoUring.Tests/IoUringRegisteredPayloadBlockPoolTests.cs`

- [ ] **Step 1: Write failing pure pool tests**

Create `tests/Hps.Transport.IoUring.Tests/IoUringRegisteredPayloadBlockPoolTests.cs`.

```csharp
using System;
using Hps.Buffers;
using Hps.Transport;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringRegisteredPayloadBlockPoolTests
    {
        // capacity 테스트: registered payload pool 은 고정 fixed table slot 수만큼만 block 을 대여해야 한다.
        // slot 이 없을 때 hidden allocation fallback 을 하면 send hot path registration miss 를 숨기므로 false 로 드러낸다.
        [Fact]
        public void TryRentCounted_WhenCapacityIsExhausted_ReturnsFalseWithoutAllocatingFallback()
        {
            FakeRegistration registration = new FakeRegistration(2);
            using (IoUringRegisteredPayloadBlockPool pool = IoUringRegisteredPayloadBlockPool.CreateForRegisteredBuffers(16, 2, registration))
            {
                RefCountedBuffer? first;
                RefCountedBuffer? second;
                RefCountedBuffer? third;

                Assert.True(pool.TryRentCounted(out first));
                Assert.True(pool.TryRentCounted(out second));
                Assert.False(pool.TryRentCounted(out third));
                Assert.Null(third);

                first!.Release();
                second!.Release();
            }
        }

        // slot 재사용 테스트: 마지막 Release 는 registered pool owner 로 돌아와 같은 slot 을 다시 free 로 만들어야 한다.
        [Fact]
        public void Release_WhenLastReferenceIsReleased_ReturnsSlotForReuse()
        {
            FakeRegistration registration = new FakeRegistration(1);
            using (IoUringRegisteredPayloadBlockPool pool = IoUringRegisteredPayloadBlockPool.CreateForRegisteredBuffers(8, 1, registration))
            {
                RefCountedBuffer? first;
                RefCountedBuffer? second;

                Assert.True(pool.TryRentCounted(out first));
                byte[] firstArray = first!.Memory.ToArray();
                first.Release();

                Assert.True(pool.TryRentCounted(out second));
                Assert.True(pool.TryGetBufferIndex(second!.Memory, out int index));
                Assert.Equal(0, index);
                second.Release();
            }
        }

        // fixed index lookup 테스트: send helper 는 backing array identity 로 fixed table index 를 찾아야 한다.
        [Fact]
        public void TryGetBufferIndex_WhenBufferBelongsToPool_ReturnsStableIndex()
        {
            FakeRegistration registration = new FakeRegistration(2);
            using (IoUringRegisteredPayloadBlockPool pool = IoUringRegisteredPayloadBlockPool.CreateForRegisteredBuffers(16, 2, registration))
            {
                RefCountedBuffer? buffer;
                Assert.True(pool.TryRentCounted(out buffer));

                Assert.True(pool.TryGetBufferIndex(buffer!.Memory, out int index));
                Assert.Equal(0, index);

                buffer.Release();
            }
        }

        private sealed class FakeRegistration : IIoUringFixedBufferRegistration
        {
            internal FakeRegistration(int count)
            {
                RegisteredBufferCount = count;
            }

            public int RegisteredBufferCount { get; private set; }

            internal int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
            }
        }
    }
}
```

Note: if `Memory<byte>` cannot be used directly for lookup, implement pool test helper around `RefCountedBuffer.Memory` using `MemoryMarshal.TryGetArray` in production. Do not expose backing arrays publicly.

- [ ] **Step 2: Run Red tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter "FullyQualifiedName~IoUringRegisteredPayloadBlockPoolTests" -v minimal
```

Expected: assertion failure from a type shape check because `IoUringRegisteredPayloadBlockPool` does not exist.

- [ ] **Step 3: Implement pure pool**

Create `src/Hps.Transport.IoUring/IoUringRegisteredPayloadBlockPool.cs`.

Required shape:

```csharp
internal sealed class IoUringRegisteredPayloadBlockPool : IRefCountedBufferOwner, IDisposable
{
    internal static IoUringRegisteredPayloadBlockPool CreateForRegisteredBuffers(
        int blockSize,
        int slotCount,
        IIoUringFixedBufferRegistration registration)
    {
        // allocate pinned byte[][], map each backing array to slot index, keep free slot queue
    }

    internal static IoUringRegisteredPayloadBlockPool Create(
        IoUringQueue queue,
        int blockSize,
        int slotCount)
    {
        // allocate pinned byte[][] first, register all arrays with IoUringRegisteredBufferSet.Register(queue, arrays)
    }

    public int BlockSize { get; }

    internal int SlotCount { get; }

    internal bool TryRentCounted(out RefCountedBuffer? buffer)
    {
        // dequeue free slot; on success return new RefCountedBuffer(this, slot.Block)
    }

    public void Return(byte[] block)
    {
        // validate owner, mark slot free exactly once
    }

    internal bool TryGetBufferIndex(Memory<byte> memory, out int bufferIndex)
    {
        // MemoryMarshal.TryGetArray -> backing array identity lookup
    }

    public void Dispose()
    {
        // dispose registration; reject future rent
    }
}
```

Implementation constraints:

- Use `GC.AllocateUninitializedArray<byte>(blockSize, pinned: true)` for each slot.
- Use reference identity dictionary for `byte[]` keys, same comparer pattern as `IoUringFixedSendBufferRegistry`.
- Do not allocate fallback blocks when full.
- `Return(byte[])` must reject foreign block and double return.
- Keep comments explaining slot reuse and fixed index lifetime.

- [ ] **Step 4: Run focused Green tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter "FullyQualifiedName~IoUringRegisteredPayloadBlockPoolTests" -v minimal
```

Expected: pool tests pass.

- [ ] **Step 5: Commit Task 3**

```powershell
git add src\Hps.Transport.IoUring\IoUringRegisteredPayloadBlockPool.cs tests\Hps.Transport.IoUring.Tests\IoUringRegisteredPayloadBlockPoolTests.cs
git commit -m "feat(iouring): add registered payload block pool"
```

---

### Task 4: Native registration and composite source wiring

**Files:**
- Create: `src/Hps.Transport.IoUring/IoUringCompositePayloadBufferSource.cs`
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringRegisteredPayloadBlockPoolTests.cs`

- [ ] **Step 1: Write failing composite source test**

Add a composite source test.

```csharp
// composite source 테스트: registered slot 이 있으면 registered pool 을 먼저 쓰고,
// 없을 때만 fallback pool 로 가야 miss fallback 이 명시적이고 관측 가능하다.
[Fact]
public void RentCounted_WhenRegisteredPoolIsFull_UsesFallbackSource()
{
    FakeRegistration registration = new FakeRegistration(1);
    using (IoUringRegisteredPayloadBlockPool registered = IoUringRegisteredPayloadBlockPool.CreateForRegisteredBuffers(16, 1, registration))
    {
        PinnedBlockMemoryPool fallback = new PinnedBlockMemoryPool(16);
        IoUringCompositePayloadBufferSource source = new IoUringCompositePayloadBufferSource(registered, fallback);

        RefCountedBuffer first = source.RentCounted();
        RefCountedBuffer second = source.RentCounted();

        Assert.True(registered.TryGetBufferIndex(first.Memory, out int firstIndex));
        Assert.Equal(0, firstIndex);
        Assert.False(registered.TryGetBufferIndex(second.Memory, out int secondIndex));
        Assert.Equal(1, fallback.RentedCount);

        first.Release();
        second.Release();
        Assert.Equal(0, fallback.RentedCount);
    }
}
```

- [ ] **Step 2: Run Red tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter "FullyQualifiedName~IoUringRegisteredPayloadBlockPoolTests" -v minimal
```

Expected: assertion failure from a type shape check because composite source does not exist.

- [ ] **Step 3: Implement composite source**

Create `src/Hps.Transport.IoUring/IoUringCompositePayloadBufferSource.cs`.

```csharp
internal sealed class IoUringCompositePayloadBufferSource : IRefCountedBufferSource
{
    private readonly IoUringRegisteredPayloadBlockPool _registeredPool;
    private readonly IRefCountedBufferSource _fallbackSource;

    internal IoUringCompositePayloadBufferSource(
        IoUringRegisteredPayloadBlockPool registeredPool,
        IRefCountedBufferSource fallbackSource)
    {
        if (registeredPool == null)
            throw new ArgumentNullException(nameof(registeredPool));
        if (fallbackSource == null)
            throw new ArgumentNullException(nameof(fallbackSource));
        if (registeredPool.BlockSize != fallbackSource.BlockSize)
            throw new ArgumentException("registered pool 과 fallback source 의 BlockSize 가 같아야 합니다.", nameof(fallbackSource));

        _registeredPool = registeredPool;
        _fallbackSource = fallbackSource;
    }

    public int BlockSize
    {
        get { return _fallbackSource.BlockSize; }
    }

    public RefCountedBuffer RentCounted()
    {
        RefCountedBuffer? buffer;
        if (_registeredPool.TryRentCounted(out buffer))
            return buffer!;

        return _fallbackSource.RentCounted();
    }
}
```

- [ ] **Step 4: Add native capability-gated registration test**

Add to `IoUringRegisteredPayloadBlockPoolTests`.

```csharp
// native registration evidence 테스트: Linux capability available 환경에서는 registered payload pool 이
// 모든 slot 을 io_uring fixed table 에 한 번 등록하고 dispose 에서 unregister 해야 한다.
[Fact]
public void Create_WhenLinuxCapabilityAvailable_RegistersAllPayloadBlocks()
{
    if (IoUringCapabilityProbe.GetStatus() != IoUringCapabilityStatus.Available)
        return;

    using (IoUringQueue queue = IoUringQueue.CreateForProbe(8))
    using (IoUringRegisteredPayloadBlockPool pool = IoUringRegisteredPayloadBlockPool.Create(queue, 16, 2))
    {
        Assert.Equal(2, pool.SlotCount);
        RefCountedBuffer? buffer;
        Assert.True(pool.TryRentCounted(out buffer));
        Assert.True(pool.TryGetBufferIndex(buffer!.Memory, out int index));
        Assert.InRange(index, 0, 1);
        buffer.Release();
    }
}
```

- [ ] **Step 5: Run Green tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter "FullyQualifiedName~IoUringRegisteredPayloadBlockPoolTests" -v minimal
```

Expected: Windows/local unavailable path skips native body; pure tests pass.

- [ ] **Step 6: Commit Task 4**

```powershell
git add src\Hps.Transport.IoUring\IoUringCompositePayloadBufferSource.cs tests\Hps.Transport.IoUring.Tests\IoUringRegisteredPayloadBlockPoolTests.cs
git commit -m "feat(iouring): add registered payload source owner"
```

---

### Task 5: Backend-neutral TCP payload source provider seam

**Files:**
- Create: `src/Hps.Transport/Abstractions/ITransportPayloadBufferSourceProvider.cs`
- Modify: `src/Hps.Server/BrokerServer.cs`
- Modify: `tests/Hps.Server.Tests/BrokerServerTests.cs`

- [ ] **Step 1: Write failing server source-provider test**

Add to `tests/Hps.Server.Tests/BrokerServerTests.cs`.

```csharp
// transport payload source provider 테스트: Server 가 IoUringTransport concrete type 을 알면 backend boundary 가 깨진다.
// 대신 transport 가 선택적으로 payload source provider 를 구현하면 StartTcpAsync 시 receive handler 가 그 source 를 사용해야 한다.
[Fact]
public async Task StartTcpAsync_WhenTransportProvidesPayloadSource_UsesProvidedSourceForTcpFrames()
{
    ProviderTransport transport = new ProviderTransport();
    PinnedBlockMemoryPool fallbackPool = new PinnedBlockMemoryPool(16);

    using (BrokerServer server = new BrokerServer(transport, fallbackPool, 8))
    {
        await server.StartTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
    }

    Assert.Equal(1, transport.SourceRequestCount);
    Assert.Same(fallbackPool, transport.FallbackPool);
}

private sealed class ProviderTransport : FakeTransport, ITransportPayloadBufferSourceProvider
{
    internal int SourceRequestCount { get; private set; }

    internal PinnedBlockMemoryPool? FallbackPool { get; private set; }

    public IRefCountedBufferSource CreateTcpPayloadBufferSource(PinnedBlockMemoryPool fallbackPool)
    {
        SourceRequestCount++;
        FallbackPool = fallbackPool;
        return fallbackPool;
    }
}
```

Use the existing fake transport/helper style in `BrokerServerTests`. If the file uses a different fake transport name, extend that helper rather than creating a second full fake.

- [ ] **Step 2: Run Red test**

Run:

```powershell
dotnet test tests\Hps.Server.Tests\Hps.Server.Tests.csproj --filter "FullyQualifiedName~StartTcpAsync_WhenTransportProvidesPayloadSource_UsesProvidedSourceForTcpFrames" -v minimal
```

Expected: assertion failure from a type shape check because `ITransportPayloadBufferSourceProvider` does not exist.

- [ ] **Step 3: Add provider contract**

Create `src/Hps.Transport/Abstractions/ITransportPayloadBufferSourceProvider.cs`.

```csharp
using Hps.Buffers;

namespace Hps.Transport
{
    /// <summary>
    /// Transport backend 이 TCP frame payload 조립에 사용할 buffer source 를 선택적으로 제공하는 계약이다.
    ///
    /// Server 는 backend concrete type 을 몰라야 하므로 이 계약만 보고 source 를 요청한다.
    /// 구현체는 fallback pool 을 그대로 반환하거나, backend native resource 에 묶인 source 와 fallback 을 합성할 수 있다.
    /// </summary>
    public interface ITransportPayloadBufferSourceProvider
    {
        IRefCountedBufferSource CreateTcpPayloadBufferSource(PinnedBlockMemoryPool fallbackPool);
    }
}
```

- [ ] **Step 4: Defer receive handler creation in BrokerServer**

In `src/Hps.Server/BrokerServer.cs`:

- remove readonly `_receiveHandler` field initialization from constructor,
- add a private method:

```csharp
private TcpFrameReceiveHandler CreateTcpReceiveHandler()
{
    IRefCountedBufferSource source = _pool;
    ITransportPayloadBufferSourceProvider? provider = _transport as ITransportPayloadBufferSourceProvider;
    if (provider != null)
        source = provider.CreateTcpPayloadBufferSource(_pool);

    return new TcpFrameReceiveHandler(source, _maxPayloadLength, _brokerFrameHandler);
}
```

Update `StartTcpAsync` order:

```csharp
if (shouldStartTransport)
    await _transport.StartAsync(cancellationToken).ConfigureAwait(false);

_transport.SetReceiveHandler(CreateTcpReceiveHandler());
listener = await _transport.ListenTcpAsync(localEndPoint, cancellationToken).ConfigureAwait(false);
```

This keeps handler set before listen/accept, while allowing provider implementations that need started native resources.

- [ ] **Step 5: Update TcpFrameReceiveHandler if needed**

If `TcpFrameReceiveHandler` only accepts `PinnedBlockMemoryPool`, add an overload:

```csharp
public TcpFrameReceiveHandler(IRefCountedBufferSource source, int maxPayloadLength, ITcpFrameHandler frameHandler)
```

Keep the existing pool constructor as a delegating compatibility constructor.

- [ ] **Step 6: Run focused Green tests**

Run:

```powershell
dotnet test tests\Hps.Server.Tests\Hps.Server.Tests.csproj --filter "FullyQualifiedName~StartTcpAsync_WhenTransportProvidesPayloadSource_UsesProvidedSourceForTcpFrames" -v minimal
dotnet test tests\Hps.Protocol.Tests\Hps.Protocol.Tests.csproj --filter "FullyQualifiedName~TcpFrameReceiveHandlerTests" -v minimal
```

Expected: provider test and receive handler tests pass.

- [ ] **Step 7: Commit Task 5**

```powershell
git add src\Hps.Transport\Abstractions\ITransportPayloadBufferSourceProvider.cs src\Hps.Server\BrokerServer.cs src\Hps.Protocol\TcpFrameReceiveHandler.cs tests\Hps.Server.Tests\BrokerServerTests.cs
git commit -m "feat(server): allow transport tcp payload source"
```

---

### Task 6: TCP fixed payload send opt-in integration

**Files:**
- Modify: `src/Hps.Transport.IoUring/IoUringTransport.cs`
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringSendPumpShapeTests.cs`
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringTransportTcpTests.cs`

- [ ] **Step 1: Write failing send path shape test**

Add to `IoUringSendPumpShapeTests`.

```csharp
// fixed payload helper 연결 테스트: helper shape 만 있어도 SendInFlightAsync 가 호출하지 않으면
// registered payload pool hit 이 production send path 에 반영되지 않는다.
[Fact]
public void SendInFlightAsync_WhenInspected_CallsFixedRegisteredPayloadHelperBeforeBaselinePayloadSend()
{
    MethodInfo? sendMethod = typeof(IoUringTransport).GetMethod(
        "SendInFlightAsync",
        BindingFlags.Instance | BindingFlags.NonPublic);
    MethodInfo? helperMethod = typeof(IoUringTransport).GetMethod(
        "SendFixedRegisteredPayloadAsync",
        BindingFlags.Instance | BindingFlags.NonPublic);

    Assert.NotNull(sendMethod);
    Assert.NotNull(helperMethod);

    Assert.True(ContainsCall(sendMethod!, helperMethod!), "SendInFlightAsync 가 fixed registered payload helper 를 호출해야 한다.");
}

private static bool ContainsCall(MethodInfo caller, MethodInfo callee)
{
    MethodBody? body = caller.GetMethodBody();
    Assert.NotNull(body);

    byte[] il = body!.GetILAsByteArray();
    int expectedToken = callee.MetadataToken;

    for (int index = 0; index <= il.Length - 5; index++)
    {
        byte opCode = il[index];
        if (opCode != 0x28 && opCode != 0x6F)
            continue;

        int token = BitConverter.ToInt32(il, index + 1);
        if (token == expectedToken)
            return true;
    }

    return false;
}
```

- [ ] **Step 2: Write TCP loopback native evidence expectation**

Extend existing `TcpLoopback_WhenIoUringAvailable_SendsQueuedPayloadToPeer` or add a sibling test that:

- starts `IoUringTransport`,
- sends a TCP `PUBLISH` payload through broker loopback,
- confirms received payload,
- confirms pool leak 0,
- leaves fixed-write hit evidence as stdout text when capability is available.

Expected stdout text after Green:

```text
registered payload fixed send path: hit
```

- [ ] **Step 3: Run Red tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter "FullyQualifiedName~IoUringSendPumpShapeTests|FullyQualifiedName~IoUringTransportTcpTests" -v minimal
```

Expected: shape test fails because `SendInFlightAsync` still always uses baseline payload `SendArrayAsync`.

- [ ] **Step 4: Implement io_uring payload source provider**

Make `IoUringTransport` implement `ITransportPayloadBufferSourceProvider`.

Add constants:

```csharp
private const int RegisteredPayloadSlotCount = 16;
```

Add a transport-owned list for source-created registered pools:

```csharp
private readonly List<IoUringRegisteredPayloadBlockPool> _registeredPayloadPools;
```

Initialize it in the constructor.

Implement:

```csharp
public IRefCountedBufferSource CreateTcpPayloadBufferSource(PinnedBlockMemoryPool fallbackPool)
{
    if (fallbackPool == null)
        throw new ArgumentNullException(nameof(fallbackPool));

    IoUringQueue? queue;
    lock (_gate)
    {
        queue = _queue;
    }

    if (queue == null)
        return fallbackPool;

    IoUringRegisteredPayloadBlockPool registeredPool = IoUringRegisteredPayloadBlockPool.Create(
        queue,
        fallbackPool.BlockSize,
        RegisteredPayloadSlotCount);

    lock (_gate)
    {
        _registeredPayloadPools.Add(registeredPool);
    }

    return new IoUringCompositePayloadBufferSource(registeredPool, fallbackPool);
}
```

Update `StopCore` snapshot to include `_registeredPayloadPools.ToArray()`, clear the list under `_gate`, and dispose those pools after connection send pumps drain. This keeps the registered buffer table alive while in-flight fan-out refs can still send.

- [ ] **Step 5: Connect fixed helper with fallback**

In `SendInFlightAsync`, after length prefix and zero-length check, call helper first:

```csharp
if (await SendFixedRegisteredPayloadAsync(resource, connection, sendBuffer).ConfigureAwait(false))
    return;

ArraySegment<byte> segment = GetRefCountedBlockSegment(sendBuffer.Buffer, sendBuffer.Offset, sendBuffer.Length);
if (segment.Array == null)
    throw new InvalidOperationException("io_uring TCP send는 pinned byte[] 기반 RefCountedBuffer만 지원합니다.");

await SendArrayAsync(resource, connection, segment.Array, segment.Offset, segment.Count).ConfigureAwait(false);
```

This preserves baseline fallback for registry miss and fallback pool payloads.

Add a transport helper that searches the registered payload pools created by `CreateTcpPayloadBufferSource`:

```csharp
private bool TryGetRegisteredPayloadIndex(Memory<byte> memory, out int bufferIndex)
{
    IoUringRegisteredPayloadBlockPool[] pools;
    lock (_gate)
    {
        pools = _registeredPayloadPools.ToArray();
    }

    for (int index = 0; index < pools.Length; index++)
    {
        if (pools[index].TryGetBufferIndex(memory, out bufferIndex))
            return true;
    }

    bufferIndex = -1;
    return false;
}
```

Update `SendFixedRegisteredPayloadAsync` to use that lookup:

```csharp
Memory<byte> payloadMemory = sendBuffer.Buffer.Memory.Slice(sendBuffer.Offset, sendBuffer.Length);

ArraySegment<byte> segment = GetRefCountedBlockSegment(sendBuffer.Buffer, sendBuffer.Offset, sendBuffer.Length);
if (segment.Array == null)
    throw new InvalidOperationException("io_uring TCP send는 pinned byte[] 기반 RefCountedBuffer만 지원합니다.");

int bufferIndex;
if (!TryGetRegisteredPayloadIndex(payloadMemory, out bufferIndex))
    return false;

int currentOffset = segment.Offset;
int remaining = segment.Count;
```

Then use `segment.Array`, `currentOffset`, `remaining`, and `bufferIndex` in the existing `TrySubmitWriteFixed` loop.

- [ ] **Step 6: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter "FullyQualifiedName~IoUringSendPumpShapeTests|FullyQualifiedName~IoUringTransportTcpTests" -v minimal
```

Expected: local tests pass; native hit evidence only executes when capability is available.

- [ ] **Step 7: Run full io_uring tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal
```

Expected: all io_uring tests pass on local environment.

- [ ] **Step 8: Commit Task 6**

```powershell
git add src\Hps.Transport.IoUring\IoUringTransport.cs tests\Hps.Transport.IoUring.Tests\IoUringSendPumpShapeTests.cs tests\Hps.Transport.IoUring.Tests\IoUringTransportTcpTests.cs
git commit -m "feat(iouring): try registered payload fixed send"
```

---

### Task 7: Full verification and remote gate handoff

**Files:**
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/decisions/2026-07.md`

- [ ] **Step 1: Run local full verification**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx -v minimal
dotnet test HighPerformanceSocket.slnx --no-build -v minimal
git diff --check
```

Expected:

- build exits 0,
- tests pass with non-zero discovered tests,
- `git diff --check` has no whitespace errors. CRLF warnings are acceptable if no error line is reported.

- [ ] **Step 2: Update state docs**

Record:

- completed task numbers,
- focused and full verification output,
- whether Task 6 reached actual fixed payload helper hit locally,
- next step as user push followed by `iouring-linux-contract.yml` remote artifact gate.

If native fixed payload hit is not proven locally, do not claim production fixed-write success. Use this wording:

```text
local gate 는 shape/ownership/fallback 을 검증했다.
Linux native payload WRITE_FIXED hit 여부는 push 이후 iouring-linux-contract.yml artifact 에서 확인한다.
```

- [ ] **Step 3: Commit verification docs**

```powershell
git add CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs\agent-state\decisions\2026-07.md
git commit -m "docs(iouring): record registered payload pool local gate"
```

- [ ] **Step 4: Remote gate after user push**

After user push, run:

```powershell
gh workflow run iouring-linux-contract.yml --ref master
gh run list --workflow iouring-linux-contract.yml --limit 5
```

Wait for the new run. Then download artifact and inspect:

- workflow/job conclusion is success,
- TRX counters have failed 0,
- registered payload pool native registration test passed with capability `Available`,
- TCP loopback still passed,
- any fixed payload hit evidence stdout is present if implemented.

Record result in state docs and commit a remote-gate documentation commit.

---

## Self-Review

### Spec coverage

- Owner abstraction: Task 1.
- Source abstraction: Task 2.
- Registered payload pool pure contract: Task 3.
- Native registration owner: Task 4.
- TCP assembler/source integration: Task 2 and Task 5.
- Send path fixed helper with fallback: Task 6.
- UDP exclusion: file structure and Task 6 explicitly avoid UDP path changes.
- Default promotion, zero-copy claim, latency hard gate exclusion: Task 7 wording prevents overclaiming.

### Execution order

The plan must be executed in order. Task 3 depends on Task 1 owner abstraction. Task 5 depends on Task 2 source injection and creates the backend-neutral server seam. Task 6 depends on Task 4 registered pool ownership and Task 5 source provider wiring.
