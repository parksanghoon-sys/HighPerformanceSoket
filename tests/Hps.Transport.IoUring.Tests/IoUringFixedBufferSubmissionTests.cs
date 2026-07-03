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
                    Assert.Equal(token, completion.Token);
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
