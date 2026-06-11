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
    /// 크로스플랫폼 기준선 Transport 구현이다.
    ///
    /// 이름은 SAEA 기준선을 나타내지만, 현재 구현은 TCP listen/connect/accept 와 최소 receive chunk 전달만 다룬다.
    /// 명시적인 SocketAsyncEventArgs 기반 send/recv 펌프와 프레이밍은 후속 단위에서 붙인다.
    /// </summary>
    public sealed class SaeaTransport : TransportBase
    {
        private const int ListenBacklog = 512;
        private const int ReceiveBlockSize = 8192;

        private readonly object _gate;
        private readonly List<SaeaConnectionListener> _listeners;
        private readonly List<TransportConnection> _connections;
        private readonly PinnedBlockMemoryPool _receivePool;
        private bool _started;
        private bool _stopped;

        public SaeaTransport()
        {
            _gate = new object();
            _listeners = new List<SaeaConnectionListener>();
            _connections = new List<TransportConnection>();
            _receivePool = new PinnedBlockMemoryPool(ReceiveBlockSize);
        }

        /// <inheritdoc />
        public override ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (_stopped)
                    throw new InvalidOperationException("이미 중지된 Transport 는 다시 시작할 수 없다.");

                _started = true;
            }

            return default(ValueTask);
        }

        /// <inheritdoc />
        public override ValueTask<IConnectionListener> ListenTcpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException(nameof(localEndPoint));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureRunning();

            Socket listenSocket = CreateTcpSocket(localEndPoint);
            SaeaConnectionListener? listener = null;

            try
            {
                // 포트 0을 허용해야 테스트와 샘플이 OS가 고른 임시 포트를 안전하게 사용할 수 있다.
                // 실제 connect 대상은 요청 endpoint 가 아니라 listener.LocalEndPoint 로 다시 읽는다.
                listenSocket.Bind(localEndPoint);
                listenSocket.Listen(ListenBacklog);

                listener = new SaeaConnectionListener(this, listenSocket);
                RegisterListener(listener);

                return new ValueTask<IConnectionListener>(listener);
            }
            catch
            {
                listener?.Close();
                listenSocket.Dispose();
                throw;
            }
        }

        /// <inheritdoc />
        public override async ValueTask<IConnection> ConnectTcpAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
        {
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureRunning();

            Socket? socket = CreateTcpSocket(remoteEndPoint);

            try
            {
                ConfigureTcpConnectionSocket(socket);
                await socket.ConnectAsync(remoteEndPoint, cancellationToken).ConfigureAwait(false);

                TransportConnection connection = CreateSocketConnection(socket);
                socket = null;
                return connection;
            }
            finally
            {
                socket?.Dispose();
            }
        }

        /// <inheritdoc />
        public override ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCore();
            return default(ValueTask);
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            StopCore();
        }

        internal TransportConnection CreateAcceptedConnection(Socket socket)
        {
            if (socket == null)
                throw new ArgumentNullException(nameof(socket));

            ConfigureTcpConnectionSocket(socket);
            return CreateSocketConnection(socket);
        }

        internal void UnregisterListener(SaeaConnectionListener listener)
        {
            lock (_gate)
            {
                _listeners.Remove(listener);
            }
        }

        private TransportConnection CreateSocketConnection(Socket socket)
        {
            TransportConnection connection = new TransportConnection(socket);

            try
            {
                RegisterConnection(connection);
                StartReceiveLoop(connection, socket);
                return connection;
            }
            catch
            {
                connection.Close();
                throw;
            }
        }

        private void StartReceiveLoop(TransportConnection connection, Socket socket)
        {
            // 이번 단위의 SAEA 기준선은 실제 SocketAsyncEventArgs pump 가 아니라 raw Socket receive loop 이다.
            // 다만 I/O buffer 는 규칙대로 pinned pool 에서 대여해 이후 SAEA/RIO/io_uring 등록 버퍼 경계와 충돌하지 않게 한다.
            _ = Task.Run(delegate()
            {
                return ReceiveLoopAsync(connection, socket);
            });
        }

        private async Task ReceiveLoopAsync(TransportConnection connection, Socket socket)
        {
            byte[] receiveBlock = _receivePool.Rent();

            try
            {
                ArraySegment<byte> receiveSegment = new ArraySegment<byte>(receiveBlock);

                while (true)
                {
                    int received;

                    try
                    {
                        received = await socket.ReceiveAsync(receiveSegment, SocketFlags.None).ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (SocketException)
                    {
                        NotifyConnectionClosed(connection);
                        return;
                    }

                    if (received == 0)
                    {
                        NotifyConnectionClosed(connection);
                        return;
                    }

                    DispatchReceived(connection, receiveBlock, received);
                }
            }
            finally
            {
                _receivePool.Return(receiveBlock);
            }
        }

        private void DispatchReceived(TransportConnection connection, byte[] receiveBlock, int received)
        {
            ITransportReceiveHandler? receiveHandler = ReadReceiveHandlerSnapshot();
            if (receiveHandler == null)
                return;

            // TransportReceiveBuffer 는 ref struct 이므로 async 메서드 안에서 보관하지 않는다.
            // 이 동기 dispatch 범위 안에서만 span view 를 만들고 handler 반환 즉시 receive block 은 다시 재사용 가능해진다.
            receiveHandler.OnReceived(connection, new TransportReceiveBuffer(new ReadOnlySpan<byte>(receiveBlock, 0, received)));
        }

        private void NotifyConnectionClosed(TransportConnection connection)
        {
            ITransportReceiveHandler? receiveHandler = ReadReceiveHandlerSnapshot();
            if (receiveHandler != null)
                receiveHandler.OnConnectionClosed(connection);

            connection.Close();
        }

        private void RegisterListener(SaeaConnectionListener listener)
        {
            lock (_gate)
            {
                EnsureRunningLocked();
                _listeners.Add(listener);
            }
        }

        private void RegisterConnection(TransportConnection connection)
        {
            lock (_gate)
            {
                EnsureRunningLocked();
                _connections.Add(connection);
            }
        }

        private void StopCore()
        {
            SaeaConnectionListener[] listeners;
            TransportConnection[] connections;

            lock (_gate)
            {
                if (_stopped)
                    return;

                _stopped = true;
                listeners = _listeners.ToArray();
                connections = _connections.ToArray();
                _listeners.Clear();
                _connections.Clear();
            }

            // Close/Dispose 는 각 객체 내부에서 idempotent 하게 처리한다.
            // Transport lock 밖에서 닫아야 listener.Close() 의 unregister 와 socket dispose 가 재진입해도 교착되지 않는다.
            for (int index = 0; index < listeners.Length; index++)
            {
                listeners[index].Close();
            }

            for (int index = 0; index < connections.Length; index++)
            {
                connections[index].Close();
            }
        }

        private void EnsureRunning()
        {
            lock (_gate)
            {
                EnsureRunningLocked();
            }
        }

        private void EnsureRunningLocked()
        {
            if (!_started)
                throw new InvalidOperationException("Transport 를 시작한 뒤에 TCP 작업을 수행해야 한다.");

            if (_stopped)
                throw new ObjectDisposedException(nameof(SaeaTransport));
        }

        private static Socket CreateTcpSocket(EndPoint endPoint)
        {
            AddressFamily addressFamily = endPoint.AddressFamily;
            if (addressFamily == AddressFamily.Unspecified || addressFamily == AddressFamily.Unknown)
                throw new NotSupportedException("TCP endpoint 의 AddressFamily 를 확인할 수 없다.");

            return new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
        }

        private static void ConfigureTcpConnectionSocket(Socket socket)
        {
            // 브로커의 작은 메시지 지연을 줄이기 위한 기본값이다. 실제 튜닝은 Phase 7에서 다시 측정한다.
            socket.NoDelay = true;
        }
    }
}
