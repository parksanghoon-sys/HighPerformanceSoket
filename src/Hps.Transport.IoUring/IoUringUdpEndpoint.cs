using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Hps.Buffers;

namespace Hps.Transport
{
    /// <summary>
    /// io_uring backend 가 bind 한 UDP socket 과 endpoint 단위 send queue/resource 를 소유한다.
    ///
    /// UDP는 TCP처럼 connection resource 로 표현할 수 없으므로 endpoint 가 receive/send operation context,
    /// datagram receive pool, message metadata pinning, pending send backpressure 를 한 곳에서 관리한다.
    /// </summary>
    internal sealed class IoUringUdpEndpoint : IUdpEndpoint
    {
        private const int ReceiveBlockSize = 8192;
        private const int DefaultPendingSendCapacity = 16;

        private readonly IoUringTransport _transport;
        private readonly Socket _socket;
        private readonly IoUringOperationRegistry _registry;
        private readonly EndPoint _localEndPoint;
        private readonly object _sendGate;
        private readonly Queue<UdpSendRequest> _pendingSends;
        private readonly SemaphoreSlim _sendSignal;
        private readonly EndpointId _endpointId;
        private readonly int _pendingSendCapacity;
        private IoUringOperationContext? _receiveContext;
        private IoUringOperationContext? _sendContext;
        private long _droppedPendingSendCount;
        private int _pendingSendQueueHighWatermark;
        private int _closed;
        private int _disposed;

        internal IoUringUdpEndpoint(
            IoUringTransport transport,
            Socket socket,
            IoUringOperationRegistry registry,
            IoUringCompletionLoop completionLoop)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            CompletionLoop = completionLoop ?? throw new ArgumentNullException(nameof(completionLoop));
            _localEndPoint = socket.LocalEndPoint ?? throw new InvalidOperationException("UDP socket LocalEndPoint 를 확인할 수 없습니다.");
            _sendGate = new object();
            _pendingSends = new Queue<UdpSendRequest>();
            _sendSignal = new SemaphoreSlim(0);
            _endpointId = transport.CreateEndpointId();
            _pendingSendCapacity = DefaultPendingSendCapacity;
            ReceivePool = new PinnedBlockMemoryPool(ReceiveBlockSize);
            ReceiveMessage = new IoUringUdpMessageBuffer();
            SendMessage = new IoUringUdpMessageBuffer();

            try
            {
                _receiveContext = _registry.Register(IoUringOperationKind.UdpReceive);
                _sendContext = _registry.Register(IoUringOperationKind.UdpSend);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public EndPoint LocalEndPoint
        {
            get { return _localEndPoint; }
        }

        internal Socket Socket
        {
            get { return _socket; }
        }

        internal int SocketFileDescriptor
        {
            get
            {
                long handle = _socket.SafeHandle.DangerousGetHandle().ToInt64();
                if (handle < 0 || handle > int.MaxValue)
                    throw new InvalidOperationException("socket file descriptor 가 io_uring syscall 범위와 맞지 않습니다.");

                return checked((int)handle);
            }
        }

        internal PinnedBlockMemoryPool ReceivePool { get; }

        internal IoUringCompletionLoop CompletionLoop { get; }

        internal IoUringQueue Queue
        {
            get { return CompletionLoop.Queue; }
        }

        internal IoUringUdpMessageBuffer ReceiveMessage { get; }

        internal IoUringUdpMessageBuffer SendMessage { get; }

        internal bool IsClosed
        {
            get { return Volatile.Read(ref _closed) != 0; }
        }

        internal bool IsDisposed
        {
            get { return Volatile.Read(ref _disposed) != 0; }
        }

        internal IoUringOperationContext ReceiveContext
        {
            get
            {
                IoUringOperationContext? context = _receiveContext;
                if (context == null)
                    throw new ObjectDisposedException(nameof(IoUringUdpEndpoint));

                return context;
            }
        }

        internal IoUringOperationContext SendContext
        {
            get
            {
                IoUringOperationContext? context = _sendContext;
                if (context == null)
                    throw new ObjectDisposedException(nameof(IoUringUdpEndpoint));

                return context;
            }
        }

        internal int PendingSendCount
        {
            get
            {
                lock (_sendGate)
                {
                    return _pendingSends.Count;
                }
            }
        }

        internal long DroppedPendingSendCount
        {
            get { return Volatile.Read(ref _droppedPendingSendCount); }
        }

        internal bool TryAcceptSend(EndPoint remoteEndPoint, TransportSendBuffer sendBuffer)
        {
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));

            bool shouldWakePump;
            UdpSendRequest? evictedSend = null;
            int pendingDepthAfterEnqueue;

            lock (_sendGate)
            {
                if (IsClosed)
                    return false;

                shouldWakePump = _pendingSends.Count == 0;

                if (_pendingSends.Count == _pendingSendCapacity)
                    evictedSend = _pendingSends.Dequeue();

                _pendingSends.Enqueue(new UdpSendRequest(remoteEndPoint, sendBuffer));
                pendingDepthAfterEnqueue = _pendingSends.Count;
                if (pendingDepthAfterEnqueue > _pendingSendQueueHighWatermark)
                    _pendingSendQueueHighWatermark = pendingDepthAfterEnqueue;
            }

            _transport.RecordUdpPendingSendDepth(pendingDepthAfterEnqueue);

            if (evictedSend.HasValue)
            {
                Interlocked.Increment(ref _droppedPendingSendCount);
                _transport.RecordUdpPendingSendDrop();
                evictedSend.Value.SendBuffer.Buffer.Release();
            }

            if (shouldWakePump)
                _sendSignal.Release();

            return true;
        }

        internal bool TryBeginSend(out UdpSendRequest sendRequest)
        {
            lock (_sendGate)
            {
                if (IsClosed || _pendingSends.Count == 0)
                {
                    sendRequest = default(UdpSendRequest);
                    return false;
                }

                sendRequest = _pendingSends.Dequeue();
                return true;
            }
        }

        internal Task WaitForSendSignalAsync()
        {
            return _sendSignal.WaitAsync();
        }

        internal EndpointSnapshot CreateSnapshot()
        {
            int pendingSendCount;
            int pendingSendQueueHighWatermark;
            EndpointState state;

            lock (_sendGate)
            {
                pendingSendCount = _pendingSends.Count;
                pendingSendQueueHighWatermark = _pendingSendQueueHighWatermark;
                state = IsClosed ? EndpointState.Closed : EndpointState.Open;
            }

            return new EndpointSnapshot(
                _endpointId,
                EndpointTransportKind.Udp,
                state,
                pendingSendCount,
                pendingSendQueueHighWatermark,
                DroppedPendingSendCount);
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
                return;

            DrainPendingSends();
            _sendSignal.Release();
            _socket.Dispose();
        }

        public void Dispose()
        {
            Close();

            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            IoUringOperationContext? receiveContext = _receiveContext;
            _receiveContext = null;
            if (receiveContext != null)
                _registry.Unregister(receiveContext.Token);

            IoUringOperationContext? sendContext = _sendContext;
            _sendContext = null;
            if (sendContext != null)
                _registry.Unregister(sendContext.Token);

            ReceiveMessage.Dispose();
            SendMessage.Dispose();
            _sendSignal.Dispose();
            GC.KeepAlive(CompletionLoop);
        }

        private void DrainPendingSends()
        {
            lock (_sendGate)
            {
                while (_pendingSends.Count != 0)
                {
                    UdpSendRequest pending = _pendingSends.Dequeue();
                    pending.SendBuffer.Buffer.Release();
                }
            }
        }

        internal readonly struct UdpSendRequest
        {
            internal UdpSendRequest(EndPoint remoteEndPoint, TransportSendBuffer sendBuffer)
            {
                RemoteEndPoint = remoteEndPoint;
                SendBuffer = sendBuffer;
            }

            internal EndPoint RemoteEndPoint { get; }

            internal TransportSendBuffer SendBuffer { get; }
        }
    }
}
