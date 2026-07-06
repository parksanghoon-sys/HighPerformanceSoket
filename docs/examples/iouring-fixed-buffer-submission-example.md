# io_uring fixed buffer submission 전체 예제

## 목적

이 문서는 `IoUringRegisteredBufferSet`과 `IoUringQueue.TrySubmitWriteFixed(...)`가 어떤 순서로 함께 쓰이는지 보여주는
내부 backend 예제다. 이 API들은 public `ITransport` 사용자가 직접 호출하는 표면이 아니라,
Linux `io_uring` backend 를 구현하거나 검증하는 사람이 보는 계약 예제다.

현재 production TCP/UDP pump 는 아직 fixed-buffer path 에 연결되어 있지 않다. 아래 예제는 pump 를 바꾸지 않고,
등록된 buffer index/range 가 SQE에 들어가고 kernel completion 으로 돌아오는 흐름만 분리해서 설명한다.

## 전체 흐름

1. Linux `io_uring` capability 를 확인한다.
2. `IoUringQueue.CreateForProbe(...)`로 작은 ring 을 만든다.
3. managed byte[] payload 를 준비한다.
4. `IoUringRegisteredBufferSet.Register(...)`로 payload block 을 fixed buffer table 에 등록한다.
5. `TrySubmitWriteFixed(...)`로 등록 buffer 의 일부 slice 를 fd 에 제출한다.
6. `io_uring_enter(..., IORING_ENTER_GETEVENTS)`로 completion 을 기다린다.
7. CQE `Token`과 `Result`를 확인한다.
8. target fd 에 실제로 기록된 payload 를 읽어 slice 가 맞는지 검증한다.
9. registration 을 dispose 해 kernel buffer registration 과 managed pin 을 해제한다.
10. queue 를 dispose 해 ring fd/mmap 자원을 정리한다.

## 단순화한 코드 예제

아래 코드는 `tests/Hps.Transport.IoUring.Tests/IoUringFixedBufferSubmissionTests.cs`의 핵심 흐름을 설명용으로 줄인 것이다.
anonymous pipe 는 TCP/UDP socket pump 와 분리된 local fd 역할만 한다.

```csharp
using System;
using System.Runtime.InteropServices;

namespace Hps.Transport.IoUring.Examples
{
    internal static class FixedBufferSubmissionExample
    {
        internal static void Run()
        {
            IoUringCapabilityStatus status = IoUringCapabilityProbe.GetStatus();
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

                    bool submitted = queue.TrySubmitWriteFixed(
                        pipe.WriteFileDescriptor,
                        registered,
                        offset: 1,
                        length: 2,
                        bufferIndex: 0,
                        token: token);
                    if (!submitted)
                        throw new InvalidOperationException("SQE를 확보하지 못했습니다.");

                    IoUringNative.Enter(queue.FileDescriptor, 0, 1, IoUringNative.EnterGetEvents);

                    IoUringCompletion completion;
                    if (!queue.TryDequeueCompletion(out completion))
                        throw new InvalidOperationException("completion 을 받지 못했습니다.");

                    if (completion.Token != token)
                        throw new InvalidOperationException("예상하지 않은 completion token 입니다.");
                    if (completion.Result != 2)
                        throw new InvalidOperationException("expected byte count 만큼 write 되지 않았습니다.");

                    byte[] received = pipe.ReadExact(2);
                    if (received[0] != 20 || received[1] != 30)
                        throw new InvalidOperationException("registered buffer slice 와 pipe payload 가 다릅니다.");
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

            internal unsafe byte[] ReadExact(int length)
            {
                byte[] buffer = new byte[length];
                int offset = 0;

                fixed (byte* bufferPointer = buffer)
                {
                    while (offset < length)
                    {
                        IntPtr result = Read(
                            _readFileDescriptor,
                            new IntPtr(bufferPointer + offset),
                            new UIntPtr((uint)(length - offset)));
                        int read = result.ToInt32();
                        if (read <= 0)
                            throw new InvalidOperationException("pipe 에서 expected payload 를 읽지 못했습니다.");

                        offset += read;
                    }
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
            private static extern IntPtr Read(int fileDescriptor, IntPtr buffer, UIntPtr count);

            [DllImport("libc", EntryPoint = "close", SetLastError = true)]
            private static extern int Close(int fileDescriptor);
        }
    }
}
```

## 각 객체의 책임

- `IoUringQueue`
  - ring fd, SQ/CQ mmap, SQE publish, CQE dequeue 를 소유한다.
  - `TrySubmitWriteFixed(...)`는 `OperationWriteFixed`, fd, registered buffer address, length, buffer index, token 을 SQE에 기록한다.

- `IoUringRegisteredBufferSet`
  - 등록 대상 managed byte[]를 pin 하고, `io_uring_register_buffers`로 kernel fixed buffer table 에 등록한다.
  - dispose 시 `io_uring_unregister_buffers`와 managed handle release 를 함께 수행한다.

- `LinuxPipe`
  - 이 예제에서만 쓰는 test-only fd target 이다.
  - TCP/UDP socket 대신 deterministic 하게 write completion 과 payload 를 확인하기 위해 사용한다.

## 소유권과 해제 순서

`IoUringRegisteredBufferSet`은 `IoUringQueue`보다 먼저 dispose 되어야 한다. registration 은 queue fd 에 묶여 있고,
registered buffer 는 unregister 전까지 같은 managed address 를 유지해야 한다. 따라서 예제처럼 nested `using`을 두어
registration 을 먼저 닫고, 그 다음 queue 를 닫는 순서가 안전하다.

```csharp
using (IoUringQueue queue = IoUringQueue.CreateForProbe(4))
{
    using (IoUringRegisteredBufferSet registration = IoUringRegisteredBufferSet.Register(queue, buffers))
    {
        // fixed-buffer SQE submit / completion wait
    } // unregister buffers + unpin
} // close ring fd + unmap rings
```

## production pump 에 바로 넣지 않는 이유

이 예제는 registered buffer submission 의 최소 계약만 보여준다. TCP/UDP pump 에 연결하려면 아직 다음 결정이 필요하다.

- `RefCountedBuffer`가 어떤 방식으로 registered buffer index 를 알 수 있는지
- length-prefix 4바이트 block 을 별도 fixed buffer 로 등록할지
- fan-out payload 를 여러 connection 이 공유할 때 registration lifetime 을 어디서 소유할지
- close drain 중 in-flight fixed-buffer send completion 을 어떻게 기다릴지
- zero-copy send 로 확장할 경우 notification CQE 전까지 payload release 를 금지하는 방법

따라서 현재 다음 검증은 이 예제와 대응되는 원격 `iouring-linux-contract.yml` artifact 에서
`WriteFixed_WhenLinuxCapabilityAvailable_WritesRegisteredBufferSliceToPipe`가 Linux capability available 상태로 통과하는지 확인하는 것이다.

## 관련 파일

- `src/Hps.Transport.IoUring/IoUringQueue.cs`
- `src/Hps.Transport.IoUring/IoUringRegisteredBufferSet.cs`
- `src/Hps.Transport.IoUring/IoUringNative.cs`
- `tests/Hps.Transport.IoUring.Tests/IoUringFixedBufferSubmissionTests.cs`
- `.github/workflows/iouring-linux-contract.yml`
