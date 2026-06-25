using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Hps.Transport
{
    /// <summary>
    /// RIO listen socket 의 accept 경계를 숨기는 TCP listener 구현이다.
    ///
    /// listener 는 listen socket 수명만 소유하고, accept 된 socket 은 즉시
    /// <see cref="TransportConnection"/> resource 로 이전한다.
    /// </summary>
    internal sealed class RioConnectionListener : IConnectionListener
    {
        private readonly RioTransport _transport;
        private readonly Socket _listenSocket;
        private readonly EndPoint _localEndPoint;
        private int _closed;

        internal RioConnectionListener(RioTransport transport, Socket listenSocket)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _listenSocket = listenSocket ?? throw new ArgumentNullException(nameof(listenSocket));
            _localEndPoint = listenSocket.LocalEndPoint ?? throw new InvalidOperationException("listen socket의 LocalEndPoint를 확인할 수 없습니다.");
        }

        public EndPoint LocalEndPoint => _localEndPoint;

        public async ValueTask<IConnection> AcceptAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _closed) != 0)
                throw new ObjectDisposedException(nameof(RioConnectionListener));

            Socket? acceptedSocket = null;

            try
            {
                // RIO RQ는 WSA_FLAG_REGISTERED_IO 로 만든 socket 에만 붙는다.
                // 따라서 OS가 새 socket을 만들게 두지 않고, accept 대상 socket을 RIO factory로 먼저 만든 뒤 넘긴다.
                acceptedSocket = RioNative.CreateTcpSocket();
                acceptedSocket = await _listenSocket.AcceptAsync(acceptedSocket, cancellationToken).ConfigureAwait(false);
                TransportConnection connection = _transport.CreateAcceptedConnection(acceptedSocket);
                acceptedSocket = null;
                return connection;
            }
            finally
            {
                // connection resource 로 넘기기 전 예외가 나면 accepted socket 이 떠돌지 않게 listener 경계에서 닫는다.
                acceptedSocket?.Dispose();
            }
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
                return;

            _listenSocket.Dispose();
            _transport.UnregisterListener(this);
        }

        public void Dispose()
        {
            Close();
        }
    }
}
