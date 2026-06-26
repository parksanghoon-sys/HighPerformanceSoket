using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Hps.Buffers;

namespace Hps.Transport
{
    /// <summary>
    /// RIO backend 가 bind 한 UDP socket 과 UDP 전용 RQ/CQ resource 를 함께 소유하는 endpoint 이다.
    ///
    /// UDP는 TCP connection 과 달리 remote peer 수명이 없고 datagram 마다 remote address 가 달라질 수 있으므로,
    /// TCP <c>RioConnectionResource</c>에 끼워 넣지 않고 endpoint 단위 owner 를 둔다.
    /// </summary>
    internal sealed class RioUdpEndpoint : IUdpEndpoint
    {
        private const int CompletionQueueSize = 64;
        private const int MaxOutstandingReceive = 1;
        private const int MaxOutstandingSend = 1;
        private const int SingleDataBufferPerRequest = 1;
        private const int ReceiveBlockSize = 4096;
        private const int SockaddrInetBlockSize = 32;

        private readonly RioTransport _transport;
        private readonly Socket _socket;
        private readonly EndPoint _localEndPoint;
        private readonly object _completionGate;
        private readonly PinnedBlockMemoryPool _remoteAddressPool;
        private byte[]? _remoteAddressBlock;
        private int _closed;
        private int _disposed;

        internal RioUdpEndpoint(RioTransport transport, Socket socket, RioNative native)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            Native = native ?? throw new ArgumentNullException(nameof(native));

            _localEndPoint = socket.LocalEndPoint ?? throw new InvalidOperationException("UDP socket LocalEndPoint 를 확인할 수 없습니다.");
            _completionGate = new object();
            ReceivePool = new PinnedBlockMemoryPool(ReceiveBlockSize);
            _remoteAddressPool = new PinnedBlockMemoryPool(SockaddrInetBlockSize);
            _remoteAddressBlock = null;
            RemoteAddressBufferId = IntPtr.Zero;
            ReceiveCompletionQueue = IntPtr.Zero;
            SendCompletionQueue = IntPtr.Zero;
            RequestQueue = IntPtr.Zero;

            try
            {
                // ReceiveEx 는 completion 때 remote SOCKADDR_INET 을 caller 제공 registered buffer 에 쓴다.
                // no-prefetch receive 라서 endpoint lifetime scratch block 하나를 안전하게 재사용할 수 있다.
                _remoteAddressBlock = _remoteAddressPool.Rent();
                RemoteAddressBufferId = RegisterPinnedArray(Native, _remoteAddressBlock);

                ReceiveCompletionQueue = Native.CreateCompletionQueue(CompletionQueueSize);
                SendCompletionQueue = Native.CreateCompletionQueue(CompletionQueueSize);
                RequestQueue = Native.CreateRequestQueue(
                    _socket,
                    MaxOutstandingReceive,
                    SingleDataBufferPerRequest,
                    MaxOutstandingSend,
                    SingleDataBufferPerRequest,
                    ReceiveCompletionQueue,
                    SendCompletionQueue);

                if (RequestQueue == IntPtr.Zero)
                    throw new InvalidOperationException("RIO UDP request queue 를 생성하지 못했습니다.");
            }
            catch
            {
                DisposeNativeResources();
                throw;
            }
        }

        /// <inheritdoc />
        public EndPoint LocalEndPoint => _localEndPoint;

        internal RioNative Native { get; }

        internal PinnedBlockMemoryPool ReceivePool { get; }

        internal byte[] RemoteAddressBlock
        {
            get
            {
                byte[]? block = _remoteAddressBlock;
                if (block == null)
                    throw new ObjectDisposedException(nameof(RioUdpEndpoint));

                return block;
            }
        }

        internal RioBufferSegment RemoteAddressSegment
        {
            get { return new RioBufferSegment(RemoteAddressBufferId, 0, SockaddrInetBlockSize); }
        }

        internal IntPtr ReceiveCompletionQueue { get; private set; }

        internal IntPtr SendCompletionQueue { get; private set; }

        internal IntPtr RequestQueue { get; private set; }

        internal IntPtr RemoteAddressBufferId { get; private set; }

        internal bool IsClosed => Volatile.Read(ref _closed) != 0;

        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        internal uint DequeueCompletion(IntPtr completionQueue, RioResult[] results)
        {
            lock (_completionGate)
            {
                if (IsDisposed)
                    throw new ObjectDisposedException(nameof(RioUdpEndpoint));

                return Native.DequeueCompletion(completionQueue, results);
            }
        }

        /// <inheritdoc />
        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
                return;

            DisposeNativeResources();
            _transport.UnregisterUdpEndpoint(this);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Close();
        }

        private void DisposeNativeResources()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _socket.Dispose();

            lock (_completionGate)
            {
                // RIO completion queue close 와 dequeue/notify 는 같은 native handle 을 만지므로 직렬화한다.
                IntPtr receiveCompletionQueue = ReceiveCompletionQueue;
                ReceiveCompletionQueue = IntPtr.Zero;
                if (receiveCompletionQueue != IntPtr.Zero)
                    Native.CloseCompletionQueue(receiveCompletionQueue);

                IntPtr sendCompletionQueue = SendCompletionQueue;
                SendCompletionQueue = IntPtr.Zero;
                if (sendCompletionQueue != IntPtr.Zero)
                    Native.CloseCompletionQueue(sendCompletionQueue);
            }

            IntPtr remoteAddressBufferId = RemoteAddressBufferId;
            RemoteAddressBufferId = IntPtr.Zero;
            if (remoteAddressBufferId != IntPtr.Zero)
                Native.DeregisterBuffer(remoteAddressBufferId);

            byte[]? remoteAddressBlock = _remoteAddressBlock;
            _remoteAddressBlock = null;
            if (remoteAddressBlock != null)
                _remoteAddressPool.Return(remoteAddressBlock);

        }

        private static unsafe IntPtr RegisterPinnedArray(RioNative native, byte[] block)
        {
            fixed (byte* pointer = block)
            {
                return native.RegisterBuffer((IntPtr)pointer, block.Length);
            }
        }
    }
}
