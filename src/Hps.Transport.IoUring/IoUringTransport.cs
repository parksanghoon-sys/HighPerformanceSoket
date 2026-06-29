using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Hps.Transport
{
    /// <summary>
    /// Linux io_uring 기반 opt-in transport root다.
    ///
    /// Phase 6의 TCP-first 단계에서는 listen/connect/accept control plane은 .NET Socket을 사용하고,
    /// accepted/connected socket의 data plane을 후속 task에서 io_uring SQE/CQE pump로 연결한다.
    /// 기본 backend 승격은 하지 않으며, unsupported OS에서는 명시적 NotSupportedException으로 수렴한다.
    /// </summary>
    public sealed class IoUringTransport : TransportBase
    {
        private const int ListenBacklog = 512;
        private const uint QueueEntries = 64;

        private readonly object _gate;
        private readonly List<IoUringConnectionListener> _listeners;
        private readonly List<TransportConnection> _connections;
        private IoUringQueue? _queue;
        private IoUringOperationRegistry? _operationRegistry;
        private IoUringCompletionLoop? _completionLoop;
        private bool _started;
        private bool _stopped;

        /// <summary>
        /// io_uring transport root를 만든다.
        ///
        /// 생성자는 native 자원을 열지 않는다. 실제 queue setup은 StartAsync에서 capability가 Available일 때만 수행해
        /// Windows 개발/테스트 환경에서 opt-in type을 참조하는 것만으로 native syscall에 들어가지 않게 한다.
        /// </summary>
        public IoUringTransport()
        {
            _gate = new object();
            _listeners = new List<IoUringConnectionListener>();
            _connections = new List<TransportConnection>();
        }

        /// <inheritdoc />
        public override ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IoUringCompletionLoop? completionLoop;

            lock (_gate)
            {
                if (_stopped)
                    throw new InvalidOperationException("이미 중지된 io_uring Transport는 다시 시작할 수 없습니다.");
                if (_started)
                    return default(ValueTask);

                if (IoUringCapabilityProbe.GetStatus() == IoUringCapabilityStatus.Available)
                {
                    _queue = IoUringQueue.CreateForProbe(QueueEntries);
                    _operationRegistry = new IoUringOperationRegistry();
                    _completionLoop = new IoUringCompletionLoop(_queue, _operationRegistry);
                }

                completionLoop = _completionLoop;
                _started = true;
            }

            if (completionLoop != null)
                return completionLoop.StartAsync(cancellationToken);

            return default(ValueTask);
        }

        /// <inheritdoc />
        public override ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCore();
            return default(ValueTask);
        }

        /// <inheritdoc />
        public override ValueTask<IConnectionListener> ListenTcpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException(nameof(localEndPoint));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureRunning();
            EnsureTcpAvailable();

            Socket listenSocket = CreateTcpSocket(localEndPoint);
            IoUringConnectionListener? listener = null;

            try
            {
                listenSocket.NoDelay = true;
                listenSocket.Bind(localEndPoint);
                listenSocket.Listen(ListenBacklog);

                listener = new IoUringConnectionListener(this, listenSocket);
                RegisterListener(listener);
                listenSocket = null!;
                return new ValueTask<IConnectionListener>(listener);
            }
            finally
            {
                if (listenSocket != null)
                    listenSocket.Dispose();
            }
        }

        /// <inheritdoc />
        public override async ValueTask<IConnection> ConnectTcpAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
        {
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureRunning();
            EnsureTcpAvailable();

            Socket? socket = CreateTcpSocket(remoteEndPoint);

            try
            {
                socket.NoDelay = true;
                await socket.ConnectAsync(remoteEndPoint, cancellationToken).ConfigureAwait(false);

                TransportConnection connection = CreateIoUringConnection(socket);
                socket = null;
                return connection;
            }
            finally
            {
                socket?.Dispose();
            }
        }

        /// <inheritdoc />
        public override ValueTask<IUdpEndpoint> BindUdpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException(nameof(localEndPoint));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureRunning();
            throw CreateUnsupportedException();
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

            socket.NoDelay = true;
            return CreateIoUringConnection(socket);
        }

        internal void UnregisterListener(IoUringConnectionListener listener)
        {
            lock (_gate)
            {
                _listeners.Remove(listener);
            }
        }

        private TransportConnection CreateIoUringConnection(Socket socket)
        {
            IoUringOperationRegistry registry;
            IoUringCompletionLoop completionLoop;

            lock (_gate)
            {
                if (_operationRegistry == null || _completionLoop == null)
                    throw CreateUnsupportedException();

                registry = _operationRegistry;
                completionLoop = _completionLoop;
            }

            IoUringTcpConnectionResource resource = new IoUringTcpConnectionResource(socket, registry, completionLoop);
            TransportConnection connection = new TransportConnection(
                CreateEndpointId(),
                resource,
                UnregisterConnection,
                RecordTcpPendingSendDrop,
                RecordTcpPendingSendDepth);

            try
            {
                RegisterConnection(connection);
                return connection;
            }
            catch
            {
                connection.Close();
                throw;
            }
        }

        private void RegisterListener(IoUringConnectionListener listener)
        {
            lock (_gate)
            {
                _listeners.Add(listener);
            }
        }

        private void RegisterConnection(TransportConnection connection)
        {
            lock (_gate)
            {
                _connections.Add(connection);
            }
        }

        private void UnregisterConnection(TransportConnection connection)
        {
            lock (_gate)
            {
                _connections.Remove(connection);
            }
        }

        private void StopCore()
        {
            IoUringConnectionListener[] listeners;
            TransportConnection[] connections;
            IoUringCompletionLoop? completionLoop;
            IoUringQueue? queue;

            lock (_gate)
            {
                _stopped = true;
                _started = false;

                listeners = _listeners.ToArray();
                connections = _connections.ToArray();
                _listeners.Clear();
                _connections.Clear();

                completionLoop = _completionLoop;
                queue = _queue;
                _completionLoop = null;
                _operationRegistry = null;
                _queue = null;
            }

            for (int index = 0; index < listeners.Length; index++)
                listeners[index].Close();

            for (int index = 0; index < connections.Length; index++)
                connections[index].Close();

            completionLoop?.Dispose();
            queue?.Dispose();
        }

        private void EnsureRunning()
        {
            lock (_gate)
            {
                if (!_started || _stopped)
                    throw new InvalidOperationException("io_uring Transport가 실행 중이 아닙니다.");
            }
        }

        private void EnsureTcpAvailable()
        {
            if (IoUringCapabilityProbe.GetStatus() != IoUringCapabilityStatus.Available)
                throw CreateUnsupportedException();

            lock (_gate)
            {
                if (_queue == null || _operationRegistry == null || _completionLoop == null)
                    throw new NotSupportedException("io_uring TCP queue가 아직 초기화되지 않았습니다.");
            }
        }

        private static Socket CreateTcpSocket(EndPoint endPoint)
        {
            IPEndPoint? ipEndPoint = endPoint as IPEndPoint;
            if (ipEndPoint == null)
                throw new NotSupportedException("io_uring TCP v1은 IPEndPoint만 지원합니다.");

            return new Socket(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        }

        private static NotSupportedException CreateUnsupportedException()
        {
            IoUringCapabilityStatus status = IoUringCapabilityProbe.GetStatus();

            if (status == IoUringCapabilityStatus.UnsupportedOperatingSystem)
                return new NotSupportedException("io_uring backend는 Linux에서만 사용할 수 있습니다.");

            return new NotSupportedException("현재 환경에서 io_uring native TCP pump를 사용할 수 없습니다.");
        }
    }
}
