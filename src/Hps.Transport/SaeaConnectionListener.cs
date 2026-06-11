using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Hps.Transport
{
    /// <summary>
    /// <see cref="SaeaTransport"/> 가 만든 TCP listen socket 의 내부 구현이다.
    /// public 표면은 <see cref="IConnectionListener"/> 로 제한해 Socket 타입이 상위 계층으로 새지 않게 한다.
    /// </summary>
    internal sealed class SaeaConnectionListener : IConnectionListener
    {
        private readonly SaeaTransport _transport;
        private readonly Socket _listenSocket;
        private readonly EndPoint _localEndPoint;
        private int _closed;

        internal SaeaConnectionListener(SaeaTransport transport, Socket listenSocket)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _listenSocket = listenSocket ?? throw new ArgumentNullException(nameof(listenSocket));
            _localEndPoint = listenSocket.LocalEndPoint ?? throw new InvalidOperationException("listen socket 의 LocalEndPoint 를 확인할 수 없다.");
        }

        /// <inheritdoc />
        public EndPoint LocalEndPoint => _localEndPoint;

        /// <inheritdoc />
        public async ValueTask<IConnection> AcceptAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _closed) != 0)
                throw new ObjectDisposedException(nameof(SaeaConnectionListener));

            Socket? acceptedSocket = null;

            try
            {
                acceptedSocket = await _listenSocket.AcceptAsync(cancellationToken).ConfigureAwait(false);
                TransportConnection connection = _transport.CreateAcceptedConnection(acceptedSocket);
                acceptedSocket = null;
                return connection;
            }
            finally
            {
                // transport 가 register 하기 전에 취소/예외가 나면 accept 된 socket 을 잃어버리지 않고 닫는다.
                // 정상 경로에서는 TransportConnection.Close()가 socket 수명을 책임지므로 null 로 넘긴다.
                acceptedSocket?.Dispose();
            }
        }

        /// <inheritdoc />
        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
                return;

            _listenSocket.Dispose();
            _transport.UnregisterListener(this);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Close();
        }
    }
}
