using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Hps.Transport
{
    /// <summary>
    /// RIO backend 가 bind 한 UDP socket 의 수명 owner 이다.
    ///
    /// 현재 Task 2 skeleton 은 bind/close/unregister 경계만 제공한다. receive/send pump, pending queue,
    /// diagnostics parity 는 후속 task 에서 SAEA UDP 규칙과 맞춰 붙인다.
    /// </summary>
    internal sealed class RioUdpEndpoint : IUdpEndpoint
    {
        private readonly RioTransport _transport;
        private readonly Socket _socket;
        private readonly EndPoint _localEndPoint;
        private int _closed;

        internal RioUdpEndpoint(RioTransport transport, Socket socket)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _localEndPoint = socket.LocalEndPoint ?? throw new InvalidOperationException("UDP socket LocalEndPoint 를 확인할 수 없습니다.");
        }

        public EndPoint LocalEndPoint => _localEndPoint;

        internal bool IsClosed => Volatile.Read(ref _closed) != 0;

        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
                return;

            _socket.Dispose();
            _transport.UnregisterUdpEndpoint(this);
        }

        public void Dispose()
        {
            Close();
        }
    }
}
