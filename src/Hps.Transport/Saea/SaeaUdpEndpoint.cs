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
        private readonly SaeaTransport _transport;
        private readonly Socket _socket;
        private readonly EndPoint _localEndPoint;
        private readonly object _sendGate;
        private readonly Queue<UdpSendRequest> _pendingSends;
        private readonly SemaphoreSlim _sendSignal;
        private int _closed;

        internal SaeaUdpEndpoint(SaeaTransport transport, Socket socket)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _localEndPoint = socket.LocalEndPoint ?? throw new InvalidOperationException("UDP socket 의 LocalEndPoint 를 확인할 수 없다.");
            _sendGate = new object();
            _pendingSends = new Queue<UdpSendRequest>();
            _sendSignal = new SemaphoreSlim(0);
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

        internal bool TryAcceptSend(EndPoint remoteEndPoint, TransportSendBuffer sendBuffer)
        {
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));

            bool shouldWakePump;

            lock (_sendGate)
            {
                if (IsClosed)
                    return false;

                shouldWakePump = _pendingSends.Count == 0;
                _pendingSends.Enqueue(new UdpSendRequest(remoteEndPoint, sendBuffer));
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
