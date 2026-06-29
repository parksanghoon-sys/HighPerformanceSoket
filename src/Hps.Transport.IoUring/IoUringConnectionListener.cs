using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Hps.Transport
{
    /// <summary>
    /// io_uring TCP listen socket의 accept 수명 경계다.
    ///
    /// accept 자체는 첫 TCP-first 단계에서 .NET Socket control plane을 사용한다. 반환된 socket은 즉시
    /// <see cref="TransportConnection"/> resource로 이전해 listener가 accepted connection 수명을 직접 소유하지 않게 한다.
    /// </summary>
    internal sealed class IoUringConnectionListener : IConnectionListener
    {
        private readonly IoUringTransport _transport;
        private readonly Socket _listenSocket;
        private readonly EndPoint _localEndPoint;
        private int _closed;

        internal IoUringConnectionListener(IoUringTransport transport, Socket listenSocket)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _listenSocket = listenSocket ?? throw new ArgumentNullException(nameof(listenSocket));
            _localEndPoint = listenSocket.LocalEndPoint ?? throw new InvalidOperationException("listen socket의 LocalEndPoint를 확인할 수 없습니다.");
        }

        public EndPoint LocalEndPoint
        {
            get { return _localEndPoint; }
        }

        public async ValueTask<IConnection> AcceptAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _closed) != 0)
                throw new ObjectDisposedException(nameof(IoUringConnectionListener));

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
