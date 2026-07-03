# io_uring Fixed Buffer Submission Evidence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `IoUringQueue`가 registered fixed buffer index/range 를 SQE에 실어 실제 Linux completion 으로 검증할 수 있게 한다.

**Architecture:** production TCP/UDP pump 는 건드리지 않고, native boundary 에 `IORING_OP_WRITE_FIXED` opcode 와 focused submission helper 만 추가한다. Linux contract test 는 test-only pipe fd 를 사용해 registered buffer 일부가 kernel write completion 과 pipe read 결과로 확인되는지 검증한다.

**Tech Stack:** .NET 9, C# 8, xUnit, Linux `io_uring`, libc `pipe/read/close` P/Invoke(test-only), existing `iouring-linux-contract.yml`.

---

## 파일 구조

- Modify: `src/Hps.Transport.IoUring/IoUringNative.cs`
  - `OperationWriteFixed` opcode 를 추가한다.
- Modify: `src/Hps.Transport.IoUring/IoUringQueue.cs`
  - Task 2에서 fixed-buffer write SQE 를 채우는 `TrySubmitWriteFixed(...)` helper 를 추가한다.
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringSubmissionShapeTests.cs`
  - opcode/helper/SQE buffer index shape Red-Green 테스트를 추가한다.
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringFixedBufferSubmissionTests.cs`
  - Linux capability available 환경에서 registered buffer 를 pipe write fd 로 `WRITE_FIXED` 제출하고 completion/read payload 를 검증한다.
- Modify: root state docs after implementation
  - `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`, `docs/agent-state/changelog/2026-07.md`, `docs/agent-state/decisions/2026-07.md`

## Task 1: fixed-write SQE opcode shape

**Files:**
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringSubmissionShapeTests.cs`
- Modify: `src/Hps.Transport.IoUring/IoUringNative.cs`

- [ ] **Step 1: Write the failing shape test**

Add this test to `IoUringSubmissionShapeTests`:

```csharp
// fixed-buffer I/O는 SQE의 opcode, address, length, buffer index 가 함께 맞아야 한다.
// production pump 를 바꾸기 전에 raw SQE shape 와 queue helper 존재를 assertion failure 로 먼저 고정한다.
[Fact]
public void NativeSubmissionTypes_WhenInspected_ExposeFixedWriteShape()
{
    Type? sqeType = Type.GetType("Hps.Transport.IoUringSubmissionQueueEntry, Hps.Transport.IoUring");
    Type? nativeType = Type.GetType("Hps.Transport.IoUringNative, Hps.Transport.IoUring");

    Assert.NotNull(sqeType);
    Assert.NotNull(nativeType);
    Assert.NotNull(sqeType!.GetField("BufferIndex", BindingFlags.Instance | BindingFlags.NonPublic));
    Assert.NotNull(nativeType!.GetField("OperationWriteFixed", BindingFlags.Static | BindingFlags.NonPublic));
}
```

- [ ] **Step 2: Run Red**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~NativeSubmissionTypes_WhenInspected_ExposeFixedWriteShape -v minimal
```

Expected: assertion failure because `OperationWriteFixed` does not exist.

- [ ] **Step 3: Add minimal native opcode**

In `IoUringNative`, add the opcode near existing operation constants:

```csharp
internal const byte OperationWriteFixed = 5;
```

- [ ] **Step 4: Run Green**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~NativeSubmissionTypes_WhenInspected_ExposeFixedWriteShape -v minimal
```

Expected: test passes.

- [ ] **Step 5: Commit Task 1**

```powershell
git add tests\Hps.Transport.IoUring.Tests\IoUringSubmissionShapeTests.cs src\Hps.Transport.IoUring\IoUringNative.cs
git diff --cached --check
git commit -m "test(iouring): expose fixed write opcode shape"
```

## Task 2: fixed-write queue helper and native completion evidence

**Files:**
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringSubmissionShapeTests.cs`
- Modify: `src/Hps.Transport.IoUring/IoUringQueue.cs`
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringFixedBufferSubmissionTests.cs`
- Modify: state docs listed in the file structure section

- [ ] **Step 1: Write the failing helper shape test**

Add this test to `IoUringSubmissionShapeTests`:

```csharp
// fixed-write opcode 만 있어서는 production queue 가 registered buffer index 를 SQE에 실을 수 없다.
// helper 부재를 reflection assertion failure 로 먼저 고정해 컴파일 실패가 아닌 Red 를 만든다.
[Fact]
public void Queue_WhenInspected_ExposesFixedWriteSubmissionHelper()
{
    Type? queueType = Type.GetType("Hps.Transport.IoUringQueue, Hps.Transport.IoUring");

    Assert.NotNull(queueType);
    Assert.NotNull(queueType!.GetMethod("TrySubmitWriteFixed", BindingFlags.Instance | BindingFlags.NonPublic));
}
```

- [ ] **Step 2: Run Red**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~Queue_WhenInspected_ExposesFixedWriteSubmissionHelper -v minimal
```

Expected: assertion failure because `TrySubmitWriteFixed` does not exist.

- [ ] **Step 3: Add queue helper**

In `IoUringQueue`, add this helper near `TrySubmitSend`:

```csharp
internal unsafe bool TrySubmitWriteFixed(
    int fileDescriptor,
    byte[] buffer,
    int offset,
    int length,
    int bufferIndex,
    ulong token)
{
    if (fileDescriptor < 0)
        throw new ArgumentOutOfRangeException(nameof(fileDescriptor), "write file descriptor가 유효하지 않습니다.");
    if (buffer == null)
        throw new ArgumentNullException(nameof(buffer));
    if (offset < 0 || offset > buffer.Length)
        throw new ArgumentOutOfRangeException(nameof(offset), "write offset은 buffer 범위 안에 있어야 합니다.");
    if (length <= 0 || length > buffer.Length - offset)
        throw new ArgumentOutOfRangeException(nameof(length), "write length는 buffer 범위 안의 양수여야 합니다.");
    if (bufferIndex < 0 || bufferIndex > ushort.MaxValue)
        throw new ArgumentOutOfRangeException(nameof(bufferIndex), "fixed buffer index는 ushort 범위 안에 있어야 합니다.");
    if (token == 0)
        throw new ArgumentOutOfRangeException(nameof(token), "io_uring user_data token은 0을 사용할 수 없습니다.");

    ThrowIfDisposed();

    fixed (byte* bufferPointer = buffer)
    {
        lock (_submissionGate)
        {
            IoUringSubmissionQueueEntry* submission = TryAcquireSubmissionEntry();
            if (submission == null)
                return false;

            *submission = default(IoUringSubmissionQueueEntry);
            submission->Opcode = IoUringNative.OperationWriteFixed;
            submission->FileDescriptor = fileDescriptor;
            submission->Address = (ulong)(bufferPointer + offset);
            submission->Length = (uint)length;
            submission->BufferIndex = (ushort)bufferIndex;
            submission->UserData = token;
            PublishSubmissionEntry(submission);
        }
    }

    IoUringNative.Enter(FileDescriptor, 1, 0, 0);
    return true;
}
```

- [ ] **Step 4: Run helper Green**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~Queue_WhenInspected_ExposesFixedWriteSubmissionHelper -v minimal
```

Expected: test passes.

- [ ] **Step 5: Add native evidence test**

Create `IoUringFixedBufferSubmissionTests.cs`:

```csharp
using System;
using System.Runtime.InteropServices;
using Xunit;
using Xunit.Abstractions;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringFixedBufferSubmissionTests
    {
        private readonly ITestOutputHelper _output;

        public IoUringFixedBufferSubmissionTests(ITestOutputHelper output)
        {
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        // Linux contract artifact 에서 registered buffer index/range 가 실제 WRITE_FIXED completion 으로 이어지는지 검증한다.
        // pump 를 바꾸지 않고 anonymous pipe 를 사용해 SQE field mapping 과 kernel completion 경계를 분리한다.
        [Fact]
        public void WriteFixed_WhenLinuxCapabilityAvailable_WritesRegisteredBufferSliceToPipe()
        {
            IoUringCapabilityStatus status = IoUringCapabilityProbe.GetStatus();
            _output.WriteLine("io_uring capability status: " + status);

            if (status != IoUringCapabilityStatus.Available)
                return;

            using (LinuxPipe pipe = LinuxPipe.Create())
            using (IoUringQueue queue = IoUringQueue.CreateForProbe(4))
            {
                byte[] registered = new byte[] { 10, 20, 30, 40 };
                using (IoUringRegisteredBufferSet registration = IoUringRegisteredBufferSet.Register(
                    queue,
                    new byte[][] { registered }))
                {
                    const ulong token = 0x179UL;

                    Assert.True(queue.TrySubmitWriteFixed(pipe.WriteFileDescriptor, registered, 1, 2, 0, token));

                    IoUringNative.Enter(queue.FileDescriptor, 0, 1, IoUringNative.EnterGetEvents);

                    IoUringCompletion completion;
                    Assert.True(queue.TryDequeueCompletion(out completion));
                    Assert.Equal(token, completion.UserData);
                    Assert.Equal(2, completion.Result);

                    byte[] received = pipe.ReadExact(2);
                    _output.WriteLine("fixed write completion result: " + completion.Result);

                    Assert.Equal(new byte[] { 20, 30 }, received);
                }
            }
        }

        private sealed class LinuxPipe : IDisposable
        {
            private int _readFileDescriptor;
            private int _writeFileDescriptor;

            private LinuxPipe(int readFileDescriptor, int writeFileDescriptor)
            {
                _readFileDescriptor = readFileDescriptor;
                _writeFileDescriptor = writeFileDescriptor;
            }

            internal int WriteFileDescriptor
            {
                get { return _writeFileDescriptor; }
            }

            internal static LinuxPipe Create()
            {
                int[] fileDescriptors = new int[2];
                if (Pipe(fileDescriptors) != 0)
                    throw new InvalidOperationException("pipe 생성에 실패했습니다.");

                return new LinuxPipe(fileDescriptors[0], fileDescriptors[1]);
            }

            internal byte[] ReadExact(int length)
            {
                byte[] buffer = new byte[length];
                int offset = 0;
                while (offset < length)
                {
                    IntPtr result = Read(_readFileDescriptor, buffer, new UIntPtr((uint)(length - offset)));
                    int read = result.ToInt32();
                    if (read <= 0)
                        throw new InvalidOperationException("pipe 에서 expected payload 를 읽지 못했습니다.");

                    offset += read;
                }

                return buffer;
            }

            public void Dispose()
            {
                int readFd = _readFileDescriptor;
                int writeFd = _writeFileDescriptor;
                _readFileDescriptor = -1;
                _writeFileDescriptor = -1;

                if (readFd >= 0)
                    Close(readFd);
                if (writeFd >= 0)
                    Close(writeFd);
            }

            [DllImport("libc", EntryPoint = "pipe", SetLastError = true)]
            private static extern int Pipe([Out] int[] fileDescriptors);

            [DllImport("libc", EntryPoint = "read", SetLastError = true)]
            private static extern IntPtr Read(int fileDescriptor, [Out] byte[] buffer, UIntPtr count);

            [DllImport("libc", EntryPoint = "close", SetLastError = true)]
            private static extern int Close(int fileDescriptor);
        }
    }
}
```

- [ ] **Step 6: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringFixedBufferSubmissionTests -v minimal
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal
```

Expected: focused test passes or early-returns on unavailable platform; full io_uring test project passes.

- [ ] **Step 7: Run solution and whitespace verification**

Run:

```powershell
dotnet test HighPerformanceSocket.slnx -v minimal
git diff --check
```

Expected: solution tests pass; `git diff --check` has no whitespace errors.

- [ ] **Step 8: Update state docs**

Update:
- `CURRENT_PLAN.md`: D180 implementation result and next remote `iouring-linux-contract.yml` gate.
- `TODOS.md`: move D179 implementation current item to Completed; add current remote artifact gate.
- `CHANGELOG_AGENT.md`: record Red/Green/verification.
- `DECISIONS.md`: add D180 if the implementation confirms a meaningful contract decision.
- `docs/agent-state/changelog/2026-07.md` and `docs/agent-state/decisions/2026-07.md`: mirror the root state.

- [ ] **Step 9: Commit Task 2**

```powershell
git add tests\Hps.Transport.IoUring.Tests\IoUringSubmissionShapeTests.cs tests\Hps.Transport.IoUring.Tests\IoUringFixedBufferSubmissionTests.cs src\Hps.Transport.IoUring\IoUringQueue.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs\agent-state\changelog\2026-07.md docs\agent-state\decisions\2026-07.md
git diff --cached --check
git commit -m "test(iouring): cover fixed buffer write submission"
```

## Plan self-review

- Spec coverage: D179의 fixed-write opcode/helper, shape test, Linux gated native completion test, workflow unchanged, pump/zero-copy/default promotion exclusion 을 Task 1~2가 모두 덮는다.
- Placeholder scan: 빈 결정값, 모호한 예외 처리 지시, 지정되지 않은 테스트 없음.
- Type consistency: `OperationWriteFixed`, `TrySubmitWriteFixed`, `IoUringFixedBufferSubmissionTests`, `LinuxPipe` 이름이 Task 간 일치한다.
