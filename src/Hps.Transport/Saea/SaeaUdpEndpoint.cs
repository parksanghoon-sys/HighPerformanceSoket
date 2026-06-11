using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

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
        private int _closed;

        internal SaeaUdpEndpoint(SaeaTransport transport, Socket socket)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _localEndPoint = socket.LocalEndPoint ?? throw new InvalidOperationException("UDP socket 의 LocalEndPoint 를 확인할 수 없다.");
        }

        /// <inheritdoc />
        public EndPoint LocalEndPoint => _localEndPoint;

        internal Socket Socket => _socket;

        internal bool IsClosed => Volatile.Read(ref _closed) != 0;

        /// <inheritdoc />
        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
                return;

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
    }
}
