using System;
using System.Reflection;
using System.Runtime.InteropServices;
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
            // fixed-write pump 연결 전, lease 가 Transport 소유 payload ref 와 registration owner 를
            // dispose 중 정확히 한 번만 정리하는지 검증한다.
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
            Assert.Equal(0, lease.BufferIndex);
            Assert.Equal(1, lease.PayloadOffset);
            Assert.Equal(2, lease.PayloadLength);
            Assert.NotNull(lease.RegisteredArray);

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
            // WRITE_FIXED 에 넘길 pointer offset 은 RefCountedBuffer 전체 배열 기준 offset 이어야 하므로,
            // payload slice metadata 가 lease surface 에 그대로 드러나는지 검증한다.
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

        [Fact]
        public void LeaseFactory_WhenInspected_ExposesQueueBasedCreateMethod()
        {
            // 다음 native evidence task 가 production helper 를 우회하지 않도록 queue 기반 factory shape 를 고정한다.
            MethodInfo? method = typeof(IoUringFixedSendLease).GetMethod(
                "Create",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(IoUringQueue), typeof(TransportSendBuffer) },
                null);

            Assert.NotNull(method);
        }

        [Fact]
        public void LinuxSocketPair_HelperExistsForLeaseNativeEvidence()
        {
            // Windows/local 에서는 native body 가 capability guard 로 early-return 하므로,
            // test-only socketpair helper 존재를 별도 contract 로 고정해 Red 단계를 명확히 만든다.
            Type? helperType = typeof(IoUringFixedSendLeaseTests).GetNestedType(
                "LinuxSocketPair",
                BindingFlags.NonPublic);

            Assert.NotNull(helperType);
        }

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

        [Fact]
        public void Lease_WhenLinuxCapabilityAvailable_WritesRegisteredPayloadSliceToSocketPair()
        {
            // lease 가 registration lifetime 을 completion 이후까지 유지하고, dispose 에서 payload ref 를 반환하는지
            // Linux native WRITE_FIXED + stream socket fd 경로로 검증한다.
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
            using (IoUringFixedSendLease lease = IoUringFixedSendLease.Create(
                queue,
                new TransportSendBuffer(buffer, 1, 2)))
            {
                const ulong Token = 0x199UL;
                Assert.True(queue.TrySubmitWriteFixed(
                    socketPair.WriterFileDescriptor,
                    lease.RegisteredArray,
                    lease.PayloadOffset,
                    lease.PayloadLength,
                    lease.BufferIndex,
                    Token));

                IoUringNative.Enter(queue.FileDescriptor, 0, 1, IoUringNative.EnterGetEvents);

                IoUringCompletion completion;
                Assert.True(queue.TryDequeueCompletion(out completion));
                Assert.Equal(Token, completion.Token);
                Assert.Equal(2, completion.Result);
                Assert.Equal(new byte[] { 20, 30 }, socketPair.ReadExact(2));
            }

            Assert.Equal(1, pool.RentedCount);
            buffer.Release();
            Assert.Equal(0, pool.RentedCount);
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

        private sealed class LinuxSocketPair : IDisposable
        {
            private const int AddressFamilyUnix = 1;
            private const int SocketTypeStream = 1;

            private int _readerFileDescriptor;
            private int _writerFileDescriptor;

            private LinuxSocketPair(int readerFileDescriptor, int writerFileDescriptor)
            {
                _readerFileDescriptor = readerFileDescriptor;
                _writerFileDescriptor = writerFileDescriptor;
            }

            internal int WriterFileDescriptor
            {
                get { return _writerFileDescriptor; }
            }

            internal static LinuxSocketPair Create()
            {
                int[] fileDescriptors = new int[2];
                if (SocketPair(AddressFamilyUnix, SocketTypeStream, 0, fileDescriptors) != 0)
                    throw new InvalidOperationException("socketpair 생성에 실패했습니다.");

                return new LinuxSocketPair(fileDescriptors[0], fileDescriptors[1]);
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
                            _readerFileDescriptor,
                            new IntPtr(bufferPointer + offset),
                            new UIntPtr((uint)(length - offset)));
                        int read = result.ToInt32();
                        if (read <= 0)
                            throw new InvalidOperationException("socketpair 에서 expected payload 를 읽지 못했습니다.");

                        offset += read;
                    }
                }

                return buffer;
            }

            public void Dispose()
            {
                int readerFd = _readerFileDescriptor;
                int writerFd = _writerFileDescriptor;
                _readerFileDescriptor = -1;
                _writerFileDescriptor = -1;

                if (readerFd >= 0)
                    Close(readerFd);
                if (writerFd >= 0)
                    Close(writerFd);
            }

            [DllImport("libc", EntryPoint = "socketpair", SetLastError = true)]
            private static extern int SocketPair(int domain, int type, int protocol, [Out] int[] fileDescriptors);

            [DllImport("libc", EntryPoint = "read", SetLastError = true)]
            private static extern IntPtr Read(int fileDescriptor, IntPtr buffer, UIntPtr count);

            [DllImport("libc", EntryPoint = "close", SetLastError = true)]
            private static extern int Close(int fileDescriptor);
        }
    }
}
