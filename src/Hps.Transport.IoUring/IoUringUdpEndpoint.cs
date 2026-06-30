using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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
        internal const int ReceiveWindowSize = 4;
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
        private readonly IoUringUdpReceiveSlot[] _receiveSlots;
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
            SendMessage = new IoUringUdpMessageBuffer();
            _receiveSlots = CreateReceiveSlots(_registry, ReceiveWindowSize);
            ReceiveMessage = _receiveSlots[0].Message;

            try
            {
                _receiveContext = _receiveSlots[0].Context;
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

        internal IoUringUdpReceiveSlot[] ReceiveSlots
        {
            get { return _receiveSlots; }
        }

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
            _transport.UnregisterUdpEndpoint(this);
        }

        public void Dispose()
        {
            Close();

            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _receiveContext = null;
            DisposeReceiveSlots();

            IoUringOperationContext? sendContext = _sendContext;
            _sendContext = null;
            if (sendContext != null)
                _registry.Unregister(sendContext.Token);

            SendMessage.Dispose();
            _sendSignal.Dispose();
            GC.KeepAlive(CompletionLoop);
        }

        private static IoUringUdpReceiveSlot[] CreateReceiveSlots(IoUringOperationRegistry registry, int receiveWindowSize)
        {
            IoUringUdpReceiveSlot[] slots = new IoUringUdpReceiveSlot[receiveWindowSize];
            int created = 0;

            try
            {
                for (int index = 0; index < slots.Length; index++)
                {
                    slots[index] = new IoUringUdpReceiveSlot(registry);
                    created++;
                }
            }
            catch
            {
                for (int index = 0; index < created; index++)
                    slots[index].Dispose();

                throw;
            }

            return slots;
        }

        private void DisposeReceiveSlots()
        {
            for (int index = 0; index < _receiveSlots.Length; index++)
                _receiveSlots[index].Dispose();
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

        internal sealed class IoUringUdpReceiveSlot : IDisposable
        {
            private readonly IoUringOperationRegistry _registry;
            private RefCountedBuffer? _datagram;
            private Task<IoUringCompletion>? _completionTask;
            private int _receiveCapacity;
            private int _disposed;

            internal IoUringUdpReceiveSlot(IoUringOperationRegistry registry)
            {
                _registry = registry ?? throw new ArgumentNullException(nameof(registry));
                Context = registry.Register(IoUringOperationKind.UdpReceive);
                Message = new IoUringUdpMessageBuffer();
            }

            internal IoUringOperationContext Context { get; }

            internal IoUringUdpMessageBuffer Message { get; }

            internal Task<IoUringCompletion> CompletionTask
            {
                get
                {
                    Task<IoUringCompletion>? completionTask = _completionTask;
                    if (completionTask == null)
                        throw new InvalidOperationException("io_uring UDP receive slot 이 아직 post 되지 않았습니다.");

                    return completionTask;
                }
            }

            internal bool Post(IoUringUdpEndpoint endpoint)
            {
                if (endpoint == null)
                    throw new ArgumentNullException(nameof(endpoint));
                if (Volatile.Read(ref _disposed) != 0)
                    throw new ObjectDisposedException(nameof(IoUringUdpReceiveSlot));
                if (_datagram != null)
                    throw new InvalidOperationException("완료되지 않은 UDP receive slot 을 다시 post 할 수 없습니다.");
                if (endpoint.IsClosed || endpoint.IsDisposed)
                    return false;

                RefCountedBuffer datagram = endpoint.ReceivePool.RentCounted();
                try
                {
                    ArraySegment<byte> receiveSegment = GetRefCountedBlockSegment(datagram, 0, endpoint.ReceivePool.BlockSize);
                    if (receiveSegment.Array == null)
                        throw new InvalidOperationException("io_uring UDP receive 는 pinned byte[] 기반 RefCountedBuffer 만 지원합니다.");

                    Context.Reset(Context.Token, IoUringOperationKind.UdpReceive);
                    Message.PrepareReceive(receiveSegment.Array, receiveSegment.Offset, receiveSegment.Count);
                    _completionTask = Context.WaitAsync().AsTask();
                    _receiveCapacity = receiveSegment.Count;
                    _datagram = datagram;

                    bool submitted = endpoint.Queue.TrySubmitReceiveMessage(
                        endpoint.SocketFileDescriptor,
                        Message.MessageHeaderPointer,
                        Context.Token);
                    if (submitted)
                        return true;

                    ReleaseInFlightDatagram();
                    return false;
                }
                catch
                {
                    if (!ReferenceEquals(_datagram, datagram))
                        datagram.Release();
                    ReleaseInFlightDatagram();
                    throw;
                }
            }

            internal ReceivedUdpDatagram Complete(IoUringCompletion completion)
            {
                RefCountedBuffer? datagram = _datagram;
                if (datagram == null)
                    throw new InvalidOperationException("post 되지 않은 UDP receive slot completion 입니다.");
                if (completion.Token != Context.Token)
                    throw new InvalidOperationException("UDP receive completion token 이 slot token 과 일치하지 않습니다.");

                _datagram = null;
                _completionTask = null;

                if (completion.Result < 0)
                {
                    datagram.Release();
                    throw new SocketException(-completion.Result);
                }

                if (completion.Result > _receiveCapacity)
                {
                    datagram.Release();
                    throw new SocketException((int)SocketError.MessageSize);
                }

                datagram.SetLength(completion.Result);
                EndPoint remoteEndPoint = Message.DecodeRemoteEndPoint();
                return new ReceivedUdpDatagram(datagram, remoteEndPoint);
            }

            internal void ReleaseInFlightDatagram()
            {
                RefCountedBuffer? datagram = _datagram;
                _datagram = null;
                _completionTask = null;

                if (datagram != null)
                    datagram.Release();
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;

                ReleaseInFlightDatagram();
                _registry.Unregister(Context.Token);
                Message.Dispose();
            }

            private static ArraySegment<byte> GetRefCountedBlockSegment(RefCountedBuffer buffer, int offset, int length)
            {
                Memory<byte> memory = buffer.Memory.Slice(offset, length);
                ArraySegment<byte> segment;

                if (!MemoryMarshal.TryGetArray(memory, out segment))
                    throw new InvalidOperationException("io_uring UDP receive 는 pinned byte[] 기반 RefCountedBuffer 만 지원합니다.");

                return segment;
            }
        }

        internal sealed class ReceivedUdpDatagram
        {
            internal ReceivedUdpDatagram(RefCountedBuffer datagram, EndPoint remoteEndPoint)
            {
                Datagram = datagram ?? throw new ArgumentNullException(nameof(datagram));
                RemoteEndPoint = remoteEndPoint ?? throw new ArgumentNullException(nameof(remoteEndPoint));
            }

            internal RefCountedBuffer Datagram { get; }

            internal EndPoint RemoteEndPoint { get; }
        }
    }
}
