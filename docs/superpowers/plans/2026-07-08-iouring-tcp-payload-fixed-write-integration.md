# io_uring TCP Payload Fixed-Write Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** io_uring TCP send pump 의 payload 구간만 `IORING_OP_WRITE_FIXED` 경로로 연결하되, 기존 `InFlightSend` ref 와 fixed-write lease ref 를 분리해 double release 와 leak 을 막는다.

**Architecture:** TCP length prefix 는 기존 `SendArrayAsync`/`TrySubmitSend` scratch path 를 유지한다. Payload 는 send pump 전용 `IoUringFixedSendLease.CreateForSendPump(...)`가 lease-owned extra ref 를 획득한 뒤 `TrySubmitWriteFixed` completion loop 로 전송한다. Local Windows 에서는 shape/ownership tests 로 검증하고, Linux available 환경에서는 기존 TCP loopback remote contract gate 로 실제 native payload path 를 검증한다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, Linux `io_uring`, `IORING_OP_WRITE_FIXED`, existing `PinnedBlockMemoryPool`/`RefCountedBuffer` ownership model.

## Global Constraints

- TFM 은 `net9.0`이고 C# `LangVersion`은 `8.0`이다. file-scoped namespace, record, target-typed `new()`를 쓰지 않는다.
- 모든 문서·주석·설명은 한국어로 작성한다. 코드 식별자는 기존 영어 스타일을 유지한다.
- Production payload buffer 는 `PinnedBlockMemoryPool`에서 온 `RefCountedBuffer`만 사용한다.
- TCP framing 은 `4-byte big-endian length prefix + payload`를 유지한다.
- 이번 범위는 TCP payload fixed-write integration 까지만 포함한다. TCP prefix fixed-write, UDP fixed-buffer send, zero-copy send, registration cache, default backend promotion 은 제외한다.
- 구현은 Red -> Green -> Refactor 순서로 진행하고, 테스트 메서드 바로 위에는 무엇을 검증하는지 한국어 주석을 남긴다.
- 각 task 는 독립 커밋으로 남긴다. `.claude/review/*` 미추적 파일은 stage 하지 않는다.

---

## File Structure

- `src/Hps.Transport.IoUring/IoUringFixedSendLease.cs`
  - Task 1에서 send pump 전용 lease factory 를 추가한다.
  - 책임: lease-owned payload ref 획득, registration 실패 rollback, registration owner dispose, lease ref release.
- `src/Hps.Transport.IoUring/IoUringTransport.cs`
  - Task 2에서 payload 전송 구간을 fixed-write helper 로 바꾼다.
  - 책임: length prefix 는 기존 send path 유지, payload 는 fixed-write completion loop 로 전송.
- `tests/Hps.Transport.IoUring.Tests/IoUringFixedSendLeaseTests.cs`
  - Task 1 ownership/rollback tests 를 추가한다.
- `tests/Hps.Transport.IoUring.Tests/IoUringSendPumpShapeTests.cs`
  - Task 2 local shape tests 를 추가한다.
- `tests/Hps.Transport.IoUring.Tests/IoUringTransportTcpTests.cs`
  - Task 2/3에서 기존 Linux-gated loopback test 를 유지하고, 필요하면 fixed-write path marker 를 추가한다.
- `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`, `docs/agent-state/decisions/2026-07.md`
  - 각 task 완료 후 현재 실행 지점, 완료 이력, 결정 사항을 갱신한다.

---

### Task 1: Send Pump Lease Ref Acquisition

**Files:**
- Modify: `src/Hps.Transport.IoUring/IoUringFixedSendLease.cs`
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringFixedSendLeaseTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - `internal static IoUringFixedSendLease Create(IoUringQueue queue, TransportSendBuffer sendBuffer)`
  - `internal static IoUringFixedSendLease CreateForRegisteredBuffer(TransportSendBuffer sendBuffer, IIoUringFixedBufferRegistration registration)`
  - `RefCountedBuffer.AddRef()`
  - `RefCountedBuffer.Release()`
- Produces:
  - `internal static IoUringFixedSendLease CreateForSendPump(IoUringQueue queue, TransportSendBuffer sendBuffer)`
  - `internal static IoUringFixedSendLease CreateForSendPump(TransportSendBuffer sendBuffer, Func<TransportSendBuffer, IIoUringFixedBufferRegistration> register)`

- [ ] **Step 1: Write failing shape and ownership tests**

Add these tests to `tests/Hps.Transport.IoUring.Tests/IoUringFixedSendLeaseTests.cs` before `Lease_WhenLinuxCapabilityAvailable_WritesRegisteredPayloadSliceToSocketPair`.

```csharp
[Fact]
public void LeaseFactory_WhenInspected_ExposesSendPumpCreateMethod()
{
    // production send pump 는 기존 InFlightSend ref 와 별도로 lease-owned ref 를 얻어야 하므로,
    // 전용 factory shape 를 먼저 고정해 direct Create 사용 회귀를 막는다.
    MethodInfo? method = typeof(IoUringFixedSendLease).GetMethod(
        "CreateForSendPump",
        BindingFlags.Static | BindingFlags.NonPublic,
        null,
        new Type[] { typeof(IoUringQueue), typeof(TransportSendBuffer) },
        null);

    Assert.NotNull(method);
}

[Fact]
public void LeaseForSendPump_WhenDisposed_ReleasesOnlyLeaseOwnedRef()
{
    // send pump factory 는 lease 전용 AddRef 를 내부에서 수행한다.
    // dispose 는 그 extra ref 만 반환하고, caller/transport 가 가진 기존 ref 는 그대로 남아야 한다.
    PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(8);
    RefCountedBuffer buffer = pool.RentCounted();
    buffer.Memory.Span.Slice(0, 4).Fill(3);
    buffer.SetLength(4);
    buffer.AddRef();

    CountingRegistration registration = new CountingRegistration();
    IoUringFixedSendLease lease = IoUringFixedSendLease.CreateForSendPump(
        new TransportSendBuffer(buffer, 1, 2),
        delegate(TransportSendBuffer ignored)
        {
            return registration;
        });

    Assert.Equal(1, pool.RentedCount);

    lease.Dispose();
    lease.Dispose();

    Assert.Equal(1, registration.DisposeCount);
    Assert.Equal(1, pool.RentedCount);

    buffer.Release();
    Assert.Equal(1, pool.RentedCount);

    buffer.Release();
    Assert.Equal(0, pool.RentedCount);
}

[Fact]
public void LeaseForSendPump_WhenRegistrationFails_RollsBackLeaseOwnedRef()
{
    // registration 실패는 submit 전 단계에서 발생할 수 있다.
    // 이때 factory 가 획득한 lease-owned ref 를 즉시 반환하지 않으면 close 이후 pool leak 으로 이어진다.
    PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(8);
    RefCountedBuffer buffer = pool.RentCounted();
    buffer.Memory.Span.Slice(0, 4).Fill(5);
    buffer.SetLength(4);
    buffer.AddRef();

    InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
        delegate
        {
            IoUringFixedSendLease.CreateForSendPump(
                new TransportSendBuffer(buffer, 0, 4),
                delegate(TransportSendBuffer ignored)
                {
                    throw new InvalidOperationException("registration failed");
                });
        });

    Assert.Equal("registration failed", failure.Message);
    Assert.Equal(1, pool.RentedCount);

    buffer.Release();
    Assert.Equal(1, pool.RentedCount);

    buffer.Release();
    Assert.Equal(0, pool.RentedCount);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringFixedSendLeaseTests -v minimal
```

Expected: FAIL with missing `CreateForSendPump` method or compile error for the new method references.

- [ ] **Step 3: Add the minimal factory implementation**

Modify `src/Hps.Transport.IoUring/IoUringFixedSendLease.cs`.

Add this overload after `Create(IoUringQueue queue, TransportSendBuffer sendBuffer)`:

```csharp
internal static IoUringFixedSendLease CreateForSendPump(IoUringQueue queue, TransportSendBuffer sendBuffer)
{
    if (queue == null)
        throw new ArgumentNullException(nameof(queue));

    return CreateForSendPump(
        sendBuffer,
        delegate(TransportSendBuffer buffer)
        {
            ArraySegment<byte> segment = GetPayloadSegment(buffer);
            if (segment.Array == null)
                throw new InvalidOperationException("io_uring fixed send lease 는 pinned byte[] 기반 RefCountedBuffer 만 지원합니다.");

            return IoUringRegisteredBufferSet.Register(
                queue,
                new byte[][] { segment.Array });
        });
}

internal static IoUringFixedSendLease CreateForSendPump(
    TransportSendBuffer sendBuffer,
    Func<TransportSendBuffer, IIoUringFixedBufferRegistration> register)
{
    if (register == null)
        throw new ArgumentNullException(nameof(register));

    sendBuffer.Buffer.AddRef();

    try
    {
        IIoUringFixedBufferRegistration registration = register(sendBuffer);
        return CreateForRegisteredBuffer(sendBuffer, registration);
    }
    catch
    {
        sendBuffer.Buffer.Release();
        throw;
    }
}
```

The existing `Dispose()` method remains unchanged because it already releases the lease-owned ref.

- [ ] **Step 4: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringFixedSendLeaseTests -v minimal
```

Expected: PASS. Existing Linux native lease evidence is skipped locally when capability is unavailable.

- [ ] **Step 5: Run package-level tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal
```

Expected: PASS, with test count increased by 3.

- [ ] **Step 6: Update state docs**

Update:

- `CURRENT_PLAN.md`: add D208 summary and set next execution point to Task 2 payload fixed-write helper.
- `TODOS.md`: move Task 1 to Completed and make Task 2 current.
- `CHANGELOG_AGENT.md`: record Red/Green commands and result.

- [ ] **Step 7: Commit**

Run:

```powershell
git status --short
git add src/Hps.Transport.IoUring/IoUringFixedSendLease.cs tests/Hps.Transport.IoUring.Tests/IoUringFixedSendLeaseTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "test(iouring): guard send pump lease refs"
```

---

### Task 2: TCP Payload Fixed-Write Helper

**Files:**
- Modify: `src/Hps.Transport.IoUring/IoUringTransport.cs`
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringSendPumpShapeTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/decisions/2026-07.md`

**Interfaces:**
- Consumes:
  - `internal static IoUringFixedSendLease CreateForSendPump(IoUringQueue queue, TransportSendBuffer sendBuffer)`
  - `internal bool TrySubmitWriteFixed(int fileDescriptor, byte[] buffer, int offset, int length, int bufferIndex, ulong token)`
  - existing `private static Task SendArrayAsync(...)`
- Produces:
  - `private static Task SendFixedPayloadAsync(IoUringTcpConnectionResource resource, TransportConnection connection, TransportSendBuffer sendBuffer)`
  - `SendInFlightAsync(...)` calls `SendFixedPayloadAsync(...)` for non-empty payload only.

- [ ] **Step 1: Write failing shape tests**

Add this test to `tests/Hps.Transport.IoUring.Tests/IoUringSendPumpShapeTests.cs` after `QueueAndTransport_WhenInspected_ExposeSendPumpShape`.

```csharp
[Fact]
public void Transport_WhenInspected_ExposesFixedPayloadSendHelper()
{
    // Windows/local 환경에서는 Linux send pump native body 가 skip 될 수 있다.
    // 그래서 payload fixed-write helper 존재와 WRITE_FIXED queue surface 를 reflection 으로 고정한다.
    MethodInfo? helper = typeof(IoUringTransport).GetMethod(
        "SendFixedPayloadAsync",
        BindingFlags.Static | BindingFlags.NonPublic);

    MethodInfo? writeFixed = typeof(IoUringQueue).GetMethod(
        "TrySubmitWriteFixed",
        BindingFlags.Instance | BindingFlags.NonPublic);

    Assert.NotNull(helper);
    Assert.NotNull(writeFixed);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringSendPumpShapeTests -v minimal
```

Expected: FAIL with `Assert.NotNull() Failure` for `SendFixedPayloadAsync`.

- [ ] **Step 3: Replace only the payload path in `SendInFlightAsync`**

In `src/Hps.Transport.IoUring/IoUringTransport.cs`, replace the non-empty payload segment in `SendInFlightAsync`:

```csharp
if (sendBuffer.Length == 0)
    return;

await SendFixedPayloadAsync(resource, connection, sendBuffer).ConfigureAwait(false);
```

Do not change this length prefix block:

```csharp
if (sendBuffer.PrependLengthPrefix)
{
    WriteBigEndianLength(resource.LengthPrefixBlock, sendBuffer.Length);
    await SendArrayAsync(resource, connection, resource.LengthPrefixBlock, 0, 4).ConfigureAwait(false);
}
```

- [ ] **Step 4: Add `SendFixedPayloadAsync`**

Add this helper before `SendArrayAsync`:

```csharp
private static async Task SendFixedPayloadAsync(
    IoUringTcpConnectionResource resource,
    TransportConnection connection,
    TransportSendBuffer sendBuffer)
{
    using (IoUringFixedSendLease lease = IoUringFixedSendLease.CreateForSendPump(resource.Queue, sendBuffer))
    {
        int currentOffset = lease.PayloadOffset;
        int remaining = lease.PayloadLength;

        while (remaining != 0)
        {
            if (connection.IsClosed || resource.IsDisposed)
                throw new ObjectDisposedException(nameof(TransportConnection));

            IoUringOperationContext context = resource.SendContext;
            context.Reset(context.Token, IoUringOperationKind.Send);
            ValueTask<IoUringCompletion> wait = context.WaitAsync();

            bool submitted = resource.Queue.TrySubmitWriteFixed(
                resource.SocketFileDescriptor,
                lease.RegisteredArray,
                currentOffset,
                remaining,
                lease.BufferIndex,
                context.Token);
            if (!submitted)
                throw new SocketException((int)SocketError.NoBufferSpaceAvailable);

            IoUringCompletion completion = await wait.ConfigureAwait(false);
            if (completion.Result <= 0 || completion.Result > remaining)
                throw new SocketException((int)SocketError.ConnectionReset);

            currentOffset += completion.Result;
            remaining -= completion.Result;
        }
    }
}
```

The operation kind can remain `IoUringOperationKind.Send` because the existing completion context is a send completion waiter. Do not add a new public operation kind unless failure diagnosis later requires it.

- [ ] **Step 5: Run focused shape tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringSendPumpShapeTests -v minimal
```

Expected: PASS.

- [ ] **Step 6: Run io_uring tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal
```

Expected: PASS locally. On Windows, Linux native body remains guarded.

- [ ] **Step 7: Run full build and tests**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx -v minimal
dotnet test HighPerformanceSocket.slnx -v minimal
git diff --check
```

Expected: build has 0 errors, tests pass, whitespace check has no errors.

- [ ] **Step 8: Update state docs**

Update:

- `CURRENT_PLAN.md`: add D209 summary and set next execution point to remote Linux contract gate after push.
- `TODOS.md`: move Task 2 to Completed and make Task 3 remote gate review current.
- `CHANGELOG_AGENT.md`: record focused/full validation.
- `DECISIONS.md`: add active D209 entry that payload path now uses fixed-write while prefix remains `TrySubmitSend`.
- `docs/agent-state/decisions/2026-07.md`: add D209 detail with consequences.

- [ ] **Step 9: Commit**

Run:

```powershell
git status --short
git add src/Hps.Transport.IoUring/IoUringTransport.cs tests/Hps.Transport.IoUring.Tests/IoUringSendPumpShapeTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs/agent-state/decisions/2026-07.md
git commit -m "feat(iouring): send tcp payloads with fixed buffers"
```

---

### Task 3: Remote Linux Contract Gate Documentation

**Files:**
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/decisions/2026-07.md`

**Interfaces:**
- Consumes:
  - GitHub Actions workflow `iouring-linux-contract.yml`
  - `TcpLoopback_WhenIoUringAvailable_SendsQueuedPayloadToPeer`
  - `Lease_WhenLinuxCapabilityAvailable_WritesRegisteredPayloadSliceToSocketPair`
  - `WriteFixed_WhenLinuxCapabilityAvailable_WritesRegisteredBufferSliceToSocketPair`
- Produces:
  - D210 remote gate interpretation in state docs.

- [ ] **Step 1: Push or wait for user push**

If local push is available and the user has allowed it, run:

```powershell
git push
```

If push is not performed in this session, stop this task and leave `TODOS.md` current item as remote gate review after user push.

- [ ] **Step 2: Run or inspect `iouring-linux-contract.yml`**

Use the existing workflow path and inspect the artifact for the pushed head SHA.

Expected artifact contents:

```text
summary.md
dotnet-info.txt
iouring-tests.trx
```

- [ ] **Step 3: Verify required evidence**

Check these conditions in the artifact:

```text
workflow conclusion: success
job conclusion: success
test exit code: 0
TRX failed: 0
TcpLoopback_WhenIoUringAvailable_SendsQueuedPayloadToPeer: Passed
Lease_WhenLinuxCapabilityAvailable_WritesRegisteredPayloadSliceToSocketPair: Passed
WriteFixed_WhenLinuxCapabilityAvailable_WritesRegisteredBufferSliceToSocketPair: Passed
stdout contains: io_uring capability status: Available
stdout contains: fixed socket write completion result: 2
```

- [ ] **Step 4: Update state docs**

If the gate passes, update:

- `CURRENT_PLAN.md`: record D210 remote gate success and next candidate selection point.
- `TODOS.md`: move remote gate review to Completed and create the next current TODO as post-D210 candidate reevaluation.
- `CHANGELOG_AGENT.md`: record run id, head SHA, TRX counters, required test outcomes.
- `DECISIONS.md`: add D210 active decision that D209 payload fixed-write production path passed remote Linux contract gate but is not zero-copy/default promotion evidence.
- `docs/agent-state/decisions/2026-07.md`: add D210 detailed interpretation.

If the gate fails, update:

- `CURRENT_PLAN.md`: record the failing run id and exact failed test.
- `TODOS.md`: keep the failure fix as current item with failing evidence.
- `CHANGELOG_AGENT.md`: record failure trigger, impact, and first investigation file.

- [ ] **Step 5: Commit documentation**

Run:

```powershell
git status --short
git add CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs/agent-state/decisions/2026-07.md
git commit -m "docs(iouring): record tcp fixed payload gate"
```

---

## Validation Summary

Local validation for the full implementation after Task 2:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringFixedSendLeaseTests -v minimal
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringSendPumpShapeTests -v minimal
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal
dotnet build HighPerformanceSocket.slnx -v minimal
dotnet test HighPerformanceSocket.slnx -v minimal
git diff --check
```

Remote validation after Task 2 is mandatory before using this as production fixed-write evidence:

```text
iouring-linux-contract.yml
required failed count: 0
required capability: Available
required TCP loopback: Passed
```

## Excluded Follow-Up

- TCP length prefix fixed-write 전환은 payload path remote gate 이후 별도 설계로 판단한다.
- UDP send pump fixed-buffer 전환은 TCP payload gate 이후 별도 설계로 판단한다.
- Registration cache 는 per-send register/unregister correctness evidence 이후 benchmark 와 함께 설계한다.
- Zero-copy send, default backend promotion, latency hard gate 는 이번 계획의 결과로 자동 승격하지 않는다.
