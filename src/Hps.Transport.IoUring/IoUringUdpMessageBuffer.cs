using System;
using System.Net;
using System.Runtime.InteropServices;
using Hps.Buffers;

namespace Hps.Transport
{
    /// <summary>
    /// io_uring UDP `recvmsg`/`sendmsg`가 completion 될 때까지 필요한 native message metadata 를 고정한다.
    ///
    /// payload byte[] 자체는 `PinnedBlockMemoryPool`/`RefCountedBuffer`가 이미 고정했다고 보고,
    /// 이 타입은 kernel 이 비동기 completion 전까지 참조하는 `msghdr`, `iovec`, sockaddr scratch 의
    /// managed 이동을 막는 역할만 맡는다.
    /// </summary>
    internal sealed class IoUringUdpMessageBuffer : IDisposable
    {
        private const int IovecCount = 1;

        private readonly PinnedBlockMemoryPool _sockaddrPool;
        private readonly IoUringMessageHeader[] _headers;
        private readonly IoUringIovec[] _iovecs;
        private readonly GCHandle _headerHandle;
        private readonly GCHandle _iovecHandle;
        private byte[]? _sockaddrBlock;
        private int _disposed;

        internal IoUringUdpMessageBuffer()
        {
            _sockaddrPool = new PinnedBlockMemoryPool(IoUringSockaddr.Ipv4SockaddrLength);
            _headers = new IoUringMessageHeader[1];
            _iovecs = new IoUringIovec[IovecCount];
            _headerHandle = GCHandle.Alloc(_headers, GCHandleType.Pinned);
            _iovecHandle = GCHandle.Alloc(_iovecs, GCHandleType.Pinned);
            _sockaddrBlock = _sockaddrPool.Rent();
        }

        internal IntPtr MessageHeaderPointer
        {
            get
            {
                ThrowIfDisposed();
                return _headerHandle.AddrOfPinnedObject();
            }
        }

        internal unsafe void PrepareReceive(byte[] payloadBlock, int offset, int length)
        {
            if (payloadBlock == null)
                throw new ArgumentNullException(nameof(payloadBlock));
            if (offset < 0 || offset > payloadBlock.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (length <= 0 || length > payloadBlock.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(length));

            ThrowIfDisposed();

            byte[] sockaddrBlock = RequireSockaddrBlock();
            Array.Clear(sockaddrBlock, 0, IoUringSockaddr.Ipv4SockaddrLength);

            fixed (byte* payloadPointer = payloadBlock)
            {
                _iovecs[0].BaseAddress = (IntPtr)(payloadPointer + offset);
                _iovecs[0].Length = new UIntPtr((uint)length);
            }

            _headers[0] = new IoUringMessageHeader
            {
                Name = GetPinnedSockaddrPointer(),
                NameLength = IoUringSockaddr.Ipv4SockaddrLength,
                Iov = _iovecHandle.AddrOfPinnedObject(),
                IovLength = new UIntPtr(IovecCount),
                Control = IntPtr.Zero,
                ControlLength = UIntPtr.Zero,
                Flags = 0
            };
        }

        internal unsafe void PrepareSend(byte[] payloadBlock, int offset, int length, IPEndPoint remoteEndPoint)
        {
            if (payloadBlock == null)
                throw new ArgumentNullException(nameof(payloadBlock));
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));
            if (offset < 0 || offset > payloadBlock.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (length <= 0 || length > payloadBlock.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(length));

            ThrowIfDisposed();

            byte[] sockaddrBlock = RequireSockaddrBlock();
            IoUringSockaddr.EncodeIPv4(remoteEndPoint, sockaddrBlock);

            fixed (byte* payloadPointer = payloadBlock)
            {
                _iovecs[0].BaseAddress = (IntPtr)(payloadPointer + offset);
                _iovecs[0].Length = new UIntPtr((uint)length);
            }

            _headers[0] = new IoUringMessageHeader
            {
                Name = GetPinnedSockaddrPointer(),
                NameLength = IoUringSockaddr.Ipv4SockaddrLength,
                Iov = _iovecHandle.AddrOfPinnedObject(),
                IovLength = new UIntPtr(IovecCount),
                Control = IntPtr.Zero,
                ControlLength = UIntPtr.Zero,
                Flags = 0
            };
        }

        internal IPEndPoint DecodeRemoteEndPoint()
        {
            ThrowIfDisposed();
            return IoUringSockaddr.DecodeIPv4(RequireSockaddrBlock(), checked((int)_headers[0].NameLength));
        }

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            byte[]? sockaddrBlock = _sockaddrBlock;
            _sockaddrBlock = null;
            if (sockaddrBlock != null)
                _sockaddrPool.Return(sockaddrBlock);

            if (_iovecHandle.IsAllocated)
                _iovecHandle.Free();
            if (_headerHandle.IsAllocated)
                _headerHandle.Free();
        }

        private IntPtr GetPinnedSockaddrPointer()
        {
            byte[] sockaddrBlock = RequireSockaddrBlock();
            Memory<byte> memory = new Memory<byte>(sockaddrBlock);
            ArraySegment<byte> segment;
            if (!MemoryMarshal.TryGetArray(memory, out segment) || segment.Array == null)
                throw new InvalidOperationException("io_uring UDP sockaddr block 은 pinned byte[]여야 합니다.");

            unsafe
            {
                fixed (byte* pointer = segment.Array)
                {
                    return (IntPtr)(pointer + segment.Offset);
                }
            }
        }

        private byte[] RequireSockaddrBlock()
        {
            byte[]? block = _sockaddrBlock;
            if (block == null || _disposed != 0)
                throw new ObjectDisposedException(nameof(IoUringUdpMessageBuffer));

            return block;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed != 0)
                throw new ObjectDisposedException(nameof(IoUringUdpMessageBuffer));
        }
    }
}
