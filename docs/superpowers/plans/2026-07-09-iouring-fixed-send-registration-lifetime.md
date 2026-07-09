# io_uring Fixed Send Registration Lifetime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** TCP payload `WRITE_FIXED` 재연결 전에 fixed send buffer registration lifetime 을 connection/resource 수명으로 분리한다.

**Architecture:** 새 `IoUringFixedSendBufferRegistry`는 registered payload block identity 를 fixed buffer index 로 조회하는 작은 owner 다. 이 owner 는 등록된 `RefCountedBuffer`에 guard ref 를 잡아 kernel fixed table 이 살아 있는 동안 pool return 을 막고, dispose 시 registration owner 와 guard ref 를 정확히 한 번 정리한다. Production TCP send path 는 이번 계획에서 기본 `WRITE_FIXED`로 바꾸지 않고, resource wiring 과 opt-in shape 만 고정한다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, Linux io_uring native contract tests, PowerShell verification.

## Global Constraints

- TFM 은 `net9.0`, C# `LangVersion`은 `8.0`이다.
- 모든 문서, 주석, 설명은 한국어로 작성한다.
- test method 바로 위에는 무엇을 검증하는지 한국어 주석을 작성한다.
- production TCP payload path 를 이번 계획에서 기본 `WRITE_FIXED`로 재연결하지 않는다.
- send hot path 에서 `RegisterBuffers`/`UnregisterBuffers`를 호출하는 구조를 만들지 않는다.
- 기존 `SendArrayAsync`/`TrySubmitSend` baseline 은 fallback 으로 유지한다.
- `.claude/review/*` 미추적 파일은 stage 하지 않는다.

---

## File Structure

- `src/Hps.Transport.IoUring/IoUringFixedSendBufferRegistry.cs`
  - 새 internal owner. registered `RefCountedBuffer` guard refs, byte[] identity lookup, fixed buffer slot metadata, registration owner dispose 를 담당한다.
- `src/Hps.Transport.IoUring/IoUringTcpConnectionResource.cs`
  - Task 3에서 registry owner 를 optional internal property 로 소유한다. Production send path 는 아직 사용하지 않는다.
- `src/Hps.Transport.IoUring/IoUringTransport.cs`
  - Task 4에서 opt-in fixed lookup/write helper shape 를 추가한다. 기본 `SendInFlightAsync` payload path 는 유지한다.
- `tests/Hps.Transport.IoUring.Tests/IoUringFixedSendBufferRegistryTests.cs`
  - Task 1~2 pure registry/lifetime tests.
- `tests/Hps.Transport.IoUring.Tests/IoUringTcpConnectionResourceTests.cs`
  - Task 3 resource ownership shape tests. 기존 `IoUringTransportTcpTests.cs`가 비대해지는 것을 피하기 위해 새 파일을 둔다.
- `tests/Hps.Transport.IoUring.Tests/IoUringSendPumpShapeTests.cs`
  - Task 4 opt-in helper shape tests.
- `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`, `docs/agent-state/decisions/2026-07.md`
  - 각 task 완료와 remote gate 결과를 기록한다.

---

### Task 1: Pure Fixed Send Buffer Registry Contract

**Files:**
- Create: `src/Hps.Transport.IoUring/IoUringFixedSendBufferRegistry.cs`
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringFixedSendBufferRegistryTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - `TransportSendBuffer`
  - `RefCountedBuffer`
  - `IIoUringFixedBufferRegistration`
- Produces:
  - `internal readonly struct IoUringFixedSendBufferSlot`
  - `internal sealed class IoUringFixedSendBufferRegistry`
  - `IoUringFixedSendBufferRegistry.CreateForRegisteredBuffers(IIoUringFixedBufferRegistration, TransportSendBuffer[], int)`
  - `bool IoUringFixedSendBufferRegistry.TryGetSlot(TransportSendBuffer, out IoUringFixedSendBufferSlot)`

- [ ] **Step 1: Write failing registry shape and lookup tests**

Create `tests/Hps.Transport.IoUring.Tests/IoUringFixedSendBufferRegistryTests.cs`.

```csharp
using System;
using Hps.Buffers;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringFixedSendBufferRegistryTests
    {
        [Fact]
        public void RegistryContract_WhenInspected_ExposesFixedSendLookupSurface()
        {
            // TCP send pump 가 per-send RegisterBuffers 를 호출하지 않도록,
            // fixed buffer index 를 조회하는 connection-scoped registry surface 를 먼저 고정한다.
            Type? registryType = typeof(IoUringQueue).Assembly.GetType("Hps.Transport.IoUringFixedSendBufferRegistry");
            Type? slotType = typeof(IoUringQueue).Assembly.GetType("Hps.Transport.IoUringFixedSendBufferSlot");

            Assert.NotNull(registryType);
            Assert.NotNull(slotType);
            Assert.NotNull(registryType!.GetMethod("CreateForRegisteredBuffers", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
            Assert.NotNull(registryType.GetMethod("TryGetSlot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
        }

        [Fact]
        public void Registry_WhenBufferIsRegistered_ReturnsStableBufferIndexAndPayloadRange()
        {
            // 같은 RefCountedBuffer block 은 send 마다 register/unregister 하지 않고
            // 이미 등록된 fixed buffer index 와 현재 payload slice 범위만 조회해야 한다.
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(8);
            RefCountedBuffer buffer = pool.RentCounted();
            buffer.SetLength(8);

            CountingRegistration registration = new CountingRegistration(1);
            using (IoUringFixedSendBufferRegistry registry = IoUringFixedSendBufferRegistry.CreateForRegisteredBuffers(
                registration,
                new TransportSendBuffer[] { new TransportSendBuffer(buffer, 0, 8) },
                1))
            {
                IoUringFixedSendBufferSlot slot;
                Assert.True(registry.TryGetSlot(new TransportSendBuffer(buffer, 2, 3), out slot));
                Assert.Equal(0, slot.BufferIndex);
                Assert.Equal(2, slot.PayloadOffset);
                Assert.Equal(3, slot.PayloadLength);
                Assert.NotNull(slot.RegisteredArray);
            }

            buffer.Release();
            Assert.Equal(0, pool.RentedCount);
        }

        [Fact]
        public void Registry_WhenCapacityIsExceeded_ReturnsMissWithoutEvictingExistingSlots()
        {
            // bounded registration window 가 가득 찼을 때는 active fixed table 을 교체하지 않고
            // miss 를 반환해 기존 SendArrayAsync fallback 으로 보낼 수 있어야 한다.
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(8);
            RefCountedBuffer first = pool.RentCounted();
            RefCountedBuffer second = pool.RentCounted();
            first.SetLength(8);
            second.SetLength(8);

            CountingRegistration registration = new CountingRegistration(1);
            using (IoUringFixedSendBufferRegistry registry = IoUringFixedSendBufferRegistry.CreateForRegisteredBuffers(
                registration,
                new TransportSendBuffer[]
                {
                    new TransportSendBuffer(first, 0, 8),
                    new TransportSendBuffer(second, 0, 8)
                },
                1))
            {
                IoUringFixedSendBufferSlot slot;
                Assert.True(registry.TryGetSlot(new TransportSendBuffer(first, 1, 2), out slot));
                Assert.Equal(0, slot.BufferIndex);
                Assert.False(registry.TryGetSlot(new TransportSendBuffer(second, 1, 2), out slot));
            }

            first.Release();
            second.Release();
            Assert.Equal(0, pool.RentedCount);
        }

        private sealed class CountingRegistration : IIoUringFixedBufferRegistration
        {
            public CountingRegistration(int registeredBufferCount)
            {
                RegisteredBufferCount = registeredBufferCount;
            }

            public int RegisteredBufferCount { get; private set; }

            public int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
            }
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringFixedSendBufferRegistryTests -v minimal
```

Expected: FAIL with `Assert.NotNull() Failure` for `IoUringFixedSendBufferRegistry`.

- [ ] **Step 3: Add minimal registry implementation**

Create `src/Hps.Transport.IoUring/IoUringFixedSendBufferRegistry.cs`.

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Hps.Buffers;

namespace Hps.Transport
{
    internal readonly struct IoUringFixedSendBufferSlot
    {
        internal IoUringFixedSendBufferSlot(byte[] registeredArray, int bufferIndex, int payloadOffset, int payloadLength)
        {
            RegisteredArray = registeredArray;
            BufferIndex = bufferIndex;
            PayloadOffset = payloadOffset;
            PayloadLength = payloadLength;
        }

        internal byte[] RegisteredArray { get; }

        internal int BufferIndex { get; }

        internal int PayloadOffset { get; }

        internal int PayloadLength { get; }
    }

    internal sealed class IoUringFixedSendBufferRegistry : IDisposable
    {
        private readonly IIoUringFixedBufferRegistration _registration;
        private readonly Dictionary<byte[], Entry> _entriesByArray;
        private Entry[]? _entries;
        private int _disposed;

        private IoUringFixedSendBufferRegistry(
            IIoUringFixedBufferRegistration registration,
            Dictionary<byte[], Entry> entriesByArray,
            Entry[] entries)
        {
            _registration = registration;
            _entriesByArray = entriesByArray;
            _entries = entries;
        }

        internal int RegisteredBufferCount
        {
            get { return _entries == null ? 0 : _entries.Length; }
        }

        internal static IoUringFixedSendBufferRegistry CreateForRegisteredBuffers(
            IIoUringFixedBufferRegistration registration,
            TransportSendBuffer[] sendBuffers,
            int maxRegisteredBufferCount)
        {
            if (registration == null)
                throw new ArgumentNullException(nameof(registration));
            if (sendBuffers == null)
                throw new ArgumentNullException(nameof(sendBuffers));
            if (maxRegisteredBufferCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxRegisteredBufferCount), "fixed send registry capacity 는 1 이상이어야 합니다.");

            Dictionary<byte[], Entry> entriesByArray = new Dictionary<byte[], Entry>(ReferenceEqualityComparer<byte[]>.Instance);
            List<Entry> entries = new List<Entry>();

            for (int index = 0; index < sendBuffers.Length && entries.Count < maxRegisteredBufferCount; index++)
            {
                TransportSendBuffer sendBuffer = sendBuffers[index];
                ArraySegment<byte> segment = GetPayloadSegment(sendBuffer);
                byte[] array = segment.Array!;

                if (entriesByArray.ContainsKey(array))
                    continue;

                sendBuffer.Buffer.AddRef();
                Entry entry = new Entry(sendBuffer.Buffer, array, entries.Count);
                entriesByArray.Add(array, entry);
                entries.Add(entry);
            }

            return new IoUringFixedSendBufferRegistry(registration, entriesByArray, entries.ToArray());
        }

        internal bool TryGetSlot(TransportSendBuffer sendBuffer, out IoUringFixedSendBufferSlot slot)
        {
            ThrowIfDisposed();

            ArraySegment<byte> segment = GetPayloadSegment(sendBuffer);
            Entry entry;
            if (segment.Array == null || !_entriesByArray.TryGetValue(segment.Array, out entry))
            {
                slot = default(IoUringFixedSendBufferSlot);
                return false;
            }

            slot = new IoUringFixedSendBufferSlot(entry.Array, entry.BufferIndex, segment.Offset, segment.Count);
            return true;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            Entry[]? entries = _entries;
            _entries = null;

            try
            {
                _registration.Dispose();
            }
            finally
            {
                if (entries != null)
                {
                    for (int index = 0; index < entries.Length; index++)
                        entries[index].Buffer.Release();
                }
            }
        }

        private static ArraySegment<byte> GetPayloadSegment(TransportSendBuffer sendBuffer)
        {
            Memory<byte> memory = sendBuffer.Buffer.Memory.Slice(sendBuffer.Offset, sendBuffer.Length);
            ArraySegment<byte> segment;

            if (!MemoryMarshal.TryGetArray(memory, out segment) || segment.Array == null)
                throw new InvalidOperationException("io_uring fixed send registry 는 pinned byte[] 기반 RefCountedBuffer 만 지원합니다.");

            return segment;
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(IoUringFixedSendBufferRegistry));
        }

        private readonly struct Entry
        {
            internal Entry(RefCountedBuffer buffer, byte[] array, int bufferIndex)
            {
                Buffer = buffer;
                Array = array;
                BufferIndex = bufferIndex;
            }

            internal RefCountedBuffer Buffer { get; }

            internal byte[] Array { get; }

            internal int BufferIndex { get; }
        }

        private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
            where T : class
        {
            internal static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();

            public bool Equals(T? x, T? y)
            {
                return object.ReferenceEquals(x, y);
            }

            public int GetHashCode(T obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
```

- [ ] **Step 4: Run focused registry tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringFixedSendBufferRegistryTests -v minimal
```

Expected: PASS.

- [ ] **Step 5: Run relevant project tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal
git diff --check
```

Expected: `Hps.Transport.IoUring.Tests` PASS and no whitespace errors.

- [ ] **Step 6: Update state docs**

Update:

- `CURRENT_PLAN.md`: record D219 Task 1 pure registry contract.
- `TODOS.md`: move Task 1 to Completed and set Task 2 lifetime rollback/native factory as current.
- `CHANGELOG_AGENT.md`: record Red/Green commands and results.

- [ ] **Step 7: Commit**

Run:

```powershell
git status --short
git add src/Hps.Transport.IoUring/IoUringFixedSendBufferRegistry.cs tests/Hps.Transport.IoUring.Tests/IoUringFixedSendBufferRegistryTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "test(iouring): add fixed send buffer registry"
```

---

### Task 2: Native Registration Factory And Rollback

**Files:**
- Modify: `src/Hps.Transport.IoUring/IoUringFixedSendBufferRegistry.cs`
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringFixedSendBufferRegistryTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - `IoUringFixedSendBufferRegistry.CreateForRegisteredBuffers(...)`
  - `IoUringRegisteredBufferSet.Register(IoUringQueue, byte[][])`
- Produces:
  - `IoUringFixedSendBufferRegistry.Create(IoUringQueue, TransportSendBuffer[], int)`
  - rollback guarantee when native registration fails after guard refs are acquired.

- [ ] **Step 1: Write failing factory shape test**

Add to `IoUringFixedSendBufferRegistryTests`.

```csharp
[Fact]
public void RegistryFactory_WhenInspected_ExposesQueueBasedCreateMethod()
{
    // production resource wiring 이 raw RegisterBuffers 를 직접 호출하지 않도록,
    // queue 기반 factory shape 를 registry owner 쪽에 고정한다.
    System.Reflection.MethodInfo? method = typeof(IoUringFixedSendBufferRegistry).GetMethod(
        "Create",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
        null,
        new Type[] { typeof(IoUringQueue), typeof(TransportSendBuffer[]), typeof(int) },
        null);

    Assert.NotNull(method);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~RegistryFactory_WhenInspected_ExposesQueueBasedCreateMethod -v minimal
```

Expected: FAIL with `Assert.NotNull() Failure`.

- [ ] **Step 3: Add native factory**

Add this method to `IoUringFixedSendBufferRegistry`.

```csharp
internal static IoUringFixedSendBufferRegistry Create(
    IoUringQueue queue,
    TransportSendBuffer[] sendBuffers,
    int maxRegisteredBufferCount)
{
    if (queue == null)
        throw new ArgumentNullException(nameof(queue));
    if (sendBuffers == null)
        throw new ArgumentNullException(nameof(sendBuffers));
    if (maxRegisteredBufferCount <= 0)
        throw new ArgumentOutOfRangeException(nameof(maxRegisteredBufferCount), "fixed send registry capacity 는 1 이상이어야 합니다.");

    byte[][] arrays = SelectUniqueArrays(sendBuffers, maxRegisteredBufferCount);
    IoUringRegisteredBufferSet registration = IoUringRegisteredBufferSet.Register(queue, arrays);

    try
    {
        return CreateForRegisteredBuffers(registration, sendBuffers, maxRegisteredBufferCount);
    }
    catch
    {
        registration.Dispose();
        throw;
    }
}
```

Add this helper to the same class.

```csharp
private static byte[][] SelectUniqueArrays(TransportSendBuffer[] sendBuffers, int maxRegisteredBufferCount)
{
    Dictionary<byte[], byte[]> selected = new Dictionary<byte[], byte[]>(ReferenceEqualityComparer<byte[]>.Instance);
    List<byte[]> arrays = new List<byte[]>();

    for (int index = 0; index < sendBuffers.Length && arrays.Count < maxRegisteredBufferCount; index++)
    {
        ArraySegment<byte> segment = GetPayloadSegment(sendBuffers[index]);
        byte[] array = segment.Array!;

        if (selected.ContainsKey(array))
            continue;

        selected.Add(array, array);
        arrays.Add(array);
    }

    if (arrays.Count == 0)
        throw new ArgumentException("fixed send registry 에 등록할 payload block 이 없습니다.", nameof(sendBuffers));

    return arrays.ToArray();
}
```

- [ ] **Step 4: Run focused factory test**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~RegistryFactory_WhenInspected_ExposesQueueBasedCreateMethod -v minimal
```

Expected: PASS.

- [ ] **Step 5: Add Linux native registry evidence test**

Add to `IoUringFixedSendBufferRegistryTests`.

```csharp
[Fact]
public void Registry_WhenLinuxCapabilityAvailable_RegistersPayloadBlockAndReturnsFixedSlot()
{
    // Linux native path 에서 registry owner 가 queue-level fixed table 을 한 번 등록하고,
    // 이후 payload slice 를 fixed buffer index 로 조회할 수 있는지 검증한다.
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

    using (IoUringQueue queue = IoUringQueue.CreateForProbe(4))
    using (IoUringFixedSendBufferRegistry registry = IoUringFixedSendBufferRegistry.Create(
        queue,
        new TransportSendBuffer[] { new TransportSendBuffer(buffer, 0, 4) },
        1))
    {
        IoUringFixedSendBufferSlot slot;
        Assert.True(registry.TryGetSlot(new TransportSendBuffer(buffer, 1, 2), out slot));
        Assert.Equal(0, slot.BufferIndex);
        Assert.Equal(1, slot.PayloadOffset);
        Assert.Equal(2, slot.PayloadLength);
    }

    buffer.Release();
    Assert.Equal(0, pool.RentedCount);
}
```

- [ ] **Step 6: Run focused registry tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringFixedSendBufferRegistryTests -v minimal
```

Expected: PASS locally. On Windows the native body early-returns through capability guard.

- [ ] **Step 7: Run relevant project tests and whitespace check**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal
git diff --check
```

Expected: PASS and no whitespace errors.

- [ ] **Step 8: Update state docs**

Update:

- `CURRENT_PLAN.md`: record Task 2 native factory/guard.
- `TODOS.md`: move Task 2 to Completed and set resource wiring current.
- `CHANGELOG_AGENT.md`: record Red/Green commands.

- [ ] **Step 9: Commit**

Run:

```powershell
git status --short
git add src/Hps.Transport.IoUring/IoUringFixedSendBufferRegistry.cs tests/Hps.Transport.IoUring.Tests/IoUringFixedSendBufferRegistryTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "test(iouring): cover fixed send registry native owner"
```

---

### Task 3: TCP Connection Resource Ownership

**Files:**
- Modify: `src/Hps.Transport.IoUring/IoUringTcpConnectionResource.cs`
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringTcpConnectionResourceTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - `IoUringFixedSendBufferRegistry`
- Produces:
  - internal `IoUringTcpConnectionResource.FixedSendBufferRegistry` property
  - internal `IoUringTcpConnectionResource.SetFixedSendBufferRegistryForTests(IoUringFixedSendBufferRegistry registry)` test seam
  - resource dispose owns registry dispose.

- [ ] **Step 1: Write failing resource ownership test**

Create `tests/Hps.Transport.IoUring.Tests/IoUringTcpConnectionResourceTests.cs`.

```csharp
using System;
using System.Reflection;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringTcpConnectionResourceTests
    {
        [Fact]
        public void ResourceContract_WhenInspected_OwnsFixedSendRegistryInternally()
        {
            // fixed send registry 는 transport public surface 로 새지 않고
            // TCP connection resource 내부 수명 owner 로만 붙어야 한다.
            Type resourceType = typeof(IoUringQueue).Assembly.GetType("Hps.Transport.IoUringTcpConnectionResource")!;

            PropertyInfo? property = resourceType.GetProperty("FixedSendBufferRegistry", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo? testSeam = resourceType.GetMethod("SetFixedSendBufferRegistryForTests", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(property);
            Assert.NotNull(testSeam);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~ResourceContract_WhenInspected_OwnsFixedSendRegistryInternally -v minimal
```

Expected: FAIL with `Assert.NotNull() Failure`.

- [ ] **Step 3: Add resource ownership surface**

Modify `IoUringTcpConnectionResource`.

Add field:

```csharp
private IoUringFixedSendBufferRegistry? _fixedSendBufferRegistry;
```

Add property:

```csharp
internal IoUringFixedSendBufferRegistry? FixedSendBufferRegistry
{
    get { return _fixedSendBufferRegistry; }
}
```

Add test seam:

```csharp
internal void SetFixedSendBufferRegistryForTests(IoUringFixedSendBufferRegistry registry)
{
    if (registry == null)
        throw new ArgumentNullException(nameof(registry));

    IoUringFixedSendBufferRegistry? previous = Interlocked.Exchange(ref _fixedSendBufferRegistry, registry);
    if (previous != null)
        previous.Dispose();
}
```

In `Dispose()`, before `GC.KeepAlive(CompletionLoop);`, add:

```csharp
IoUringFixedSendBufferRegistry? fixedSendBufferRegistry = Interlocked.Exchange(ref _fixedSendBufferRegistry, null);
if (fixedSendBufferRegistry != null)
    fixedSendBufferRegistry.Dispose();
```

- [ ] **Step 4: Run focused resource tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringTcpConnectionResourceTests -v minimal
```

Expected: PASS.

- [ ] **Step 5: Run relevant project tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal
git diff --check
```

Expected: PASS and no whitespace errors.

- [ ] **Step 6: Update state docs**

Update:

- `CURRENT_PLAN.md`: record Task 3 resource ownership.
- `TODOS.md`: move Task 3 to Completed and set opt-in helper shape current.
- `CHANGELOG_AGENT.md`: record focused and project test results.

- [ ] **Step 7: Commit**

Run:

```powershell
git status --short
git add src/Hps.Transport.IoUring/IoUringTcpConnectionResource.cs tests/Hps.Transport.IoUring.Tests/IoUringTcpConnectionResourceTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat(iouring): own fixed send registry per tcp resource"
```

---

### Task 4: Opt-In Fixed Lookup/Write Shape Without Default Production Reconnect

**Files:**
- Modify: `src/Hps.Transport.IoUring/IoUringTransport.cs`
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringSendPumpShapeTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - `IoUringFixedSendBufferRegistry.TryGetSlot(...)`
  - `IoUringQueue.TrySubmitWriteFixed(...)`
- Produces:
  - private `SendFixedRegisteredPayloadAsync(IoUringTcpConnectionResource, TransportConnection, TransportSendBuffer)`
  - `SendInFlightAsync` remains on baseline `SendArrayAsync` by default.

- [ ] **Step 1: Write failing shape test**

Add to `IoUringSendPumpShapeTests`.

```csharp
[Fact]
public void SendPump_WhenInspected_ExposesOptInFixedRegisteredPayloadHelper()
{
    // D210처럼 production path 를 바로 바꾸지 않고,
    // registered buffer lookup 기반 WRITE_FIXED helper shape 만 먼저 고정한다.
    Type transportType = typeof(IoUringTransport);

    System.Reflection.MethodInfo? helper = transportType.GetMethod(
        "SendFixedRegisteredPayloadAsync",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    Assert.NotNull(helper);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~SendPump_WhenInspected_ExposesOptInFixedRegisteredPayloadHelper -v minimal
```

Expected: FAIL with `Assert.NotNull() Failure`.

- [ ] **Step 3: Add opt-in helper without changing default send path**

Add to `IoUringTransport`.

```csharp
private async Task<bool> SendFixedRegisteredPayloadAsync(
    IoUringTcpConnectionResource resource,
    TransportConnection connection,
    TransportSendBuffer sendBuffer)
{
    IoUringFixedSendBufferRegistry? registry = resource.FixedSendBufferRegistry;
    if (registry == null)
        return false;

    IoUringFixedSendBufferSlot slot;
    if (!registry.TryGetSlot(sendBuffer, out slot))
        return false;

    int currentOffset = slot.PayloadOffset;
    int remaining = slot.PayloadLength;

    while (remaining != 0)
    {
        if (connection.IsClosed || resource.IsDisposed)
            throw new ObjectDisposedException(nameof(TransportConnection));

        IoUringOperationContext context = resource.SendContext;
        context.Reset(context.Token, IoUringOperationKind.Send);
        ValueTask<IoUringCompletion> wait = context.WaitAsync();

        if (!resource.Queue.TrySubmitWriteFixed(
            resource.SocketFileDescriptor,
            slot.RegisteredArray,
            currentOffset,
            remaining,
            slot.BufferIndex,
            context.Token))
        {
            throw new SocketException((int)SocketError.NoBufferSpaceAvailable);
        }

        IoUringNative.Enter(resource.Queue.FileDescriptor, 1, 0, 0);
        IoUringCompletion completion = await wait.ConfigureAwait(false);
        if (completion.Result <= 0)
            throw new SocketException((int)SocketError.ConnectionReset);

        currentOffset += completion.Result;
        remaining -= completion.Result;
    }

    return true;
}
```

Do not call this helper from `SendInFlightAsync` in this task.

- [ ] **Step 4: Run focused shape tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringSendPumpShapeTests -v minimal
```

Expected: PASS.

- [ ] **Step 5: Run relevant project tests and build**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal
dotnet build HighPerformanceSocket.slnx -v minimal
git diff --check
```

Expected: PASS, build warning 0/error 0, no whitespace errors.

- [ ] **Step 6: Update state docs**

Update:

- `CURRENT_PLAN.md`: record opt-in helper shape and default baseline unchanged.
- `TODOS.md`: move Task 4 to Completed and set remote contract gate current.
- `CHANGELOG_AGENT.md`: record shape test and build/test results.
- `DECISIONS.md`: add D222 stating fixed registered payload helper exists but production send path remains baseline.
- `docs/agent-state/decisions/2026-07.md`: add detailed D222.

- [ ] **Step 7: Commit**

Run:

```powershell
git status --short
git add src/Hps.Transport.IoUring/IoUringTransport.cs tests/Hps.Transport.IoUring.Tests/IoUringSendPumpShapeTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs/agent-state/decisions/2026-07.md
git commit -m "feat(iouring): add opt-in fixed send helper shape"
```

---

### Task 5: Full Local Verification And Remote Contract Gate Documentation

**Files:**
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/decisions/2026-07.md`

**Interfaces:**
- Consumes:
  - Task 1~4 committed local changes.
  - `iouring-linux-contract.yml`.
- Produces:
  - D223 local verification and remote gate interpretation.

- [ ] **Step 1: Run full local build**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx -v minimal
```

Expected: warning 0, error 0.

- [ ] **Step 2: Run full local tests**

Run:

```powershell
dotnet test HighPerformanceSocket.slnx -v minimal
```

Expected: all test projects PASS. Do not treat zero discovered tests as success if it appears.

- [ ] **Step 3: Run whitespace check**

Run:

```powershell
git diff --check
```

Expected: no whitespace errors.

- [ ] **Step 4: Push or wait for user push**

Run:

```powershell
git push
```

If command policy blocks `git push`, record that remote gate is waiting for user push and stop this task before workflow execution.

- [ ] **Step 5: Trigger remote workflow**

Run after push:

```powershell
gh workflow run iouring-linux-contract.yml --ref master
```

Expected: a GitHub Actions run URL.

- [ ] **Step 6: Watch remote workflow**

Run:

```powershell
$runId = gh run list --workflow iouring-linux-contract.yml --branch master --limit 1 --json databaseId --jq '.[0].databaseId'
gh run watch $runId --exit-status
```

Expected: success.

- [ ] **Step 7: Download artifact**

Run:

```powershell
$runId = gh run list --workflow iouring-linux-contract.yml --branch master --limit 1 --json databaseId --jq '.[0].databaseId'
$dir = "artifacts\iouring\linux-contract\2026-07-09\run-$runId-1"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
gh run download $runId --dir $dir
```

Expected artifact files:

```text
summary.md
dotnet-info.txt
iouring-tests.trx
vstest-diag.log
```

- [ ] **Step 8: Verify TRX evidence**

Run:

```powershell
$runId = gh run list --workflow iouring-linux-contract.yml --branch master --limit 1 --json databaseId --jq '.[0].databaseId'
$root = "artifacts\iouring\linux-contract\2026-07-09\run-$runId-1"
$trx = (Get-ChildItem -Path $root -Recurse -Filter iouring-tests.trx | Select-Object -First 1).FullName
[xml]$x = Get-Content -Raw $trx
$ns = New-Object System.Xml.XmlNamespaceManager($x.NameTable)
$ns.AddNamespace('t','http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
$c = $x.SelectSingleNode('//t:ResultSummary/t:Counters',$ns)
"Counters total=$($c.total) executed=$($c.executed) passed=$($c.passed) failed=$($c.failed) notExecuted=$($c.notExecuted)"
```

Expected: failed 0, notExecuted 0.

- [ ] **Step 9: Update state docs**

If remote gate passes:

- `CURRENT_PLAN.md`: record D223 remote gate and next candidate.
- `TODOS.md`: move remote gate to Completed and set next follow-up current.
- `CHANGELOG_AGENT.md`: record run id, head SHA, artifact, counters, diag files.
- `DECISIONS.md`: add D223 accepted decision.
- `docs/agent-state/decisions/2026-07.md`: add detailed D223.

If remote gate fails:

- Record exact failing step, exit code, and artifact contents.
- Keep failure fix as Current TODO.

- [ ] **Step 10: Commit remote gate documentation**

Run:

```powershell
git status --short
git add CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs/agent-state/decisions/2026-07.md
git commit -m "docs(iouring): record fixed send registry gate"
```

---

## Validation Summary

Local validation across the plan:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringFixedSendBufferRegistryTests -v minimal
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringTcpConnectionResourceTests -v minimal
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringSendPumpShapeTests -v minimal
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal
dotnet build HighPerformanceSocket.slnx -v minimal
dotnet test HighPerformanceSocket.slnx -v minimal
git diff --check
```

Remote validation:

```text
iouring-linux-contract.yml success
artifact includes summary.md, dotnet-info.txt, iouring-tests.trx, vstest-diag.log
TRX failed 0
```

## Excluded Follow-Up

- Production TCP payload path 를 기본 `WRITE_FIXED`로 재연결
- TCP length prefix fixed-write 전환
- UDP fixed-buffer send
- zero-copy send 또는 `IORING_OP_SEND_ZC`
- default backend promotion
- latency hard gate 또는 warning-as-failure
- transport-wide global registration cache
