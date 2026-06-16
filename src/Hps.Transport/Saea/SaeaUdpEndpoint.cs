using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Hps.Transport
{
    /// <summary>
    /// <see cref="SaeaTransport"/> 가 bind 한 UDP socket 의 내부 수명 핸들이다.
    ///
    /// UDP 는 연결별 pending queue 가 없으므로 TCP <see cref="TransportConnection"/> 과 분리한다.
    /// 이 타입은 socket 세부사항을 public API 로 노출하지 않고, bind 된 endpoint 의 close/unregister 대칭만 담당한다.
    /// </summary>
    internal sealed class SaeaUdpEndpoint : IUdpEndpoint
    {
        private const int DefaultPendingSendCapacity = 16;

        private readonly SaeaTransport _transport;
        private readonly Socket _socket;
        private readonly EndPoint _localEndPoint;
        private readonly object _sendGate;
        private readonly Queue<UdpSendRequest> _pendingSends;
        private readonly SemaphoreSlim _sendSignal;
        private readonly EndpointId _endpointId;
        private readonly int _pendingSendCapacity;
        private long _droppedPendingSendCount;
        private int _pendingSendQueueHighWatermark;
        private int _closed;

        internal SaeaUdpEndpoint(SaeaTransport transport, Socket socket)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _localEndPoint = socket.LocalEndPoint ?? throw new InvalidOperationException("UDP socket 의 LocalEndPoint 를 확인할 수 없다.");
            _sendGate = new object();
            _pendingSends = new Queue<UdpSendRequest>();
            _sendSignal = new SemaphoreSlim(0);
            _endpointId = transport.CreateEndpointId();
            _pendingSendCapacity = DefaultPendingSendCapacity;
        }

        /// <inheritdoc />
        public EndPoint LocalEndPoint => _localEndPoint;

        internal Socket Socket => _socket;

        internal bool IsClosed => Volatile.Read(ref _closed) != 0;

        /// <summary>
        /// 테스트와 후속 배압 정책에서 endpoint 단위 UDP send queue 경계를 확인하기 위한 현재 대기 datagram 수이다.
        /// </summary>
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

        /// <summary>
        /// drop-oldest backpressure 로 실제 UDP socket 에 쓰이지 못하고 evict 된 pending datagram 수다.
        /// 아직 public metric 계약은 없으므로 endpoint 내부 진단과 회귀 테스트에서만 읽는다.
        /// </summary>
        internal long DroppedPendingSendCount => ReadDroppedPendingSendCount();

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

            // drop-oldest 는 queue mutation 을 _sendGate 안에서 끝낸 뒤, 실제 ref 반환만 lock 밖에서 수행한다.
            // Release 가 pool 반환까지 이어져도 producer, pump, close 가 같은 endpoint queue lock 에 묶이지 않게 하기 위함이다.
            _transport.RecordUdpPendingSendDepth(pendingDepthAfterEnqueue);

            if (evictedSend.HasValue)
            {
                IncrementDroppedPendingSendCount();
                _transport.RecordUdpPendingSendDrop();
                evictedSend.Value.SendBuffer.Buffer.Release();
            }

            // UDP 도 endpoint 당 단일 pump 가 큐를 drain 하므로 빈 큐에서 첫 항목이 들어올 때만 깨운다.
            // datagram 마다 별도 작업을 만들지 않아 고빈도 송신에서 스레드 풀 폭주를 피한다.
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

        /// <summary>
        /// bind 된 UDP endpoint 의 현재 send queue 상태를 logical endpoint snapshot 으로 만든다.
        /// UDP socket 참조나 remote endpoint 목록은 포함하지 않고, 운영자가 endpoint 단위 backlog/drop 상태만 읽게 한다.
        /// </summary>
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
                ReadDroppedPendingSendCount());
        }

        /// <inheritdoc />
        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
                return;

            DrainPendingSends();
            _sendSignal.Release();
            _socket.Dispose();
            _transport.UnregisterUdpEndpoint(this);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Close();
        }

        internal EndPoint CreateReceiveRemoteEndPoint()
        {
            AddressFamily addressFamily = _localEndPoint.AddressFamily;
            if (addressFamily == AddressFamily.InterNetworkV6)
                return new IPEndPoint(IPAddress.IPv6Any, 0);

            return new IPEndPoint(IPAddress.Any, 0);
        }

        private long ReadDroppedPendingSendCount()
        {
            return Volatile.Read(ref _droppedPendingSendCount);
        }

        private void IncrementDroppedPendingSendCount()
        {
            Interlocked.Increment(ref _droppedPendingSendCount);
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
