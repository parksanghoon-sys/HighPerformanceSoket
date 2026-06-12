using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hps.Broker;
using Hps.Buffers;
using Hps.Protocol;
using Hps.Transport;

namespace Hps.Server
{
    /// <summary>
    /// Transport, Protocol frame adapter, Broker command handler 를 묶는 TCP broker host 이다.
    ///
    /// 이 타입은 OS별 backend 를 직접 알지 않는다. 호출자가 주입한 <see cref="ITransport"/> 에
    /// receive handler 를 등록하고 listener 수명을 관리하는 얇은 조립 계층이다.
    /// </summary>
    public sealed class BrokerServer : IDisposable
    {
        private readonly object _gate;
        private readonly ITransport _transport;
        private readonly PinnedBlockMemoryPool _pool;
        private readonly int _maxPayloadLength;
        private readonly SubscriptionTable _subscriptions;
        private readonly BrokerPublisher _publisher;
        private readonly BrokerTcpFrameHandler _brokerFrameHandler;
        private readonly TcpFrameReceiveHandler _receiveHandler;
        private IConnectionListener? _tcpListener;
        private CancellationTokenSource? _acceptLoopCancellation;
        private Task? _acceptLoopTask;
        private bool _started;
        private bool _disposed;

        /// <summary>
        /// 테스트 가능한 서버 host 를 만든다.
        ///
        /// <paramref name="transport"/> 는 실제 socket backend 이고, <paramref name="pool"/> 은 TCP frame payload 를
        /// 담는 소유권 버퍼를 대여한다. <paramref name="maxPayloadLength"/> 는 Protocol 조립기의 DoS 방지 상한이다.
        /// </summary>
        public BrokerServer(ITransport transport, PinnedBlockMemoryPool pool, int maxPayloadLength)
        {
            if (transport == null)
                throw new ArgumentNullException(nameof(transport));
            if (pool == null)
                throw new ArgumentNullException(nameof(pool));
            if (maxPayloadLength < 0)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadLength));
            if (maxPayloadLength > pool.BlockSize)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadLength), "최대 payload 길이는 풀 블록 크기를 넘을 수 없다.");

            _transport = transport;
            _pool = pool;
            _maxPayloadLength = maxPayloadLength;
            _gate = new object();
            _subscriptions = new SubscriptionTable();
            _publisher = new BrokerPublisher(_subscriptions, _transport);
            _brokerFrameHandler = new BrokerTcpFrameHandler(_subscriptions, _publisher);
            _receiveHandler = new TcpFrameReceiveHandler(_pool, _maxPayloadLength, _brokerFrameHandler);
        }

        /// <summary>
        /// 현재 TCP listener 가 실제로 bind 된 endpoint 이다. 아직 시작하지 않았으면 <c>null</c> 이다.
        /// </summary>
        public EndPoint? LocalEndPoint { get; private set; }

        /// <summary>
        /// TCP broker 수신 대기를 시작한다.
        /// </summary>
        public async ValueTask StartTcpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException(nameof(localEndPoint));

            lock (_gate)
            {
                ThrowIfDisposed();
                if (_started)
                    throw new InvalidOperationException("BrokerServer 는 이미 시작됐다.");

                _started = true;
            }

            IConnectionListener? listener = null;
            CancellationTokenSource? acceptLoopCancellation = null;
            Task? acceptLoopTask = null;

            try
            {
                _transport.SetReceiveHandler(_receiveHandler);
                await _transport.StartAsync(cancellationToken).ConfigureAwait(false);

                listener = await _transport.ListenTcpAsync(localEndPoint, cancellationToken).ConfigureAwait(false);
                acceptLoopCancellation = new CancellationTokenSource();
                acceptLoopTask = Task.Run(() => AcceptLoopAsync(listener, acceptLoopCancellation.Token));

                lock (_gate)
                {
                    _tcpListener = listener;
                    _acceptLoopCancellation = acceptLoopCancellation;
                    _acceptLoopTask = acceptLoopTask;
                    LocalEndPoint = listener.LocalEndPoint;
                }
            }
            catch
            {
                CleanupFailedStart(listener, acceptLoopCancellation);

                lock (_gate)
                {
                    _tcpListener = null;
                    _acceptLoopCancellation = null;
                    _acceptLoopTask = null;
                    LocalEndPoint = null;
                    _started = false;
                }

                await _transport.StopAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// TCP listener 와 Transport 를 중지한다.
        /// </summary>
        public async ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            IConnectionListener? listener;
            CancellationTokenSource? acceptLoopCancellation;
            Task? acceptLoopTask;
            bool shouldStopTransport;

            lock (_gate)
            {
                listener = _tcpListener;
                acceptLoopCancellation = _acceptLoopCancellation;
                acceptLoopTask = _acceptLoopTask;
                shouldStopTransport = _started;

                _tcpListener = null;
                _acceptLoopCancellation = null;
                _acceptLoopTask = null;
                LocalEndPoint = null;
                _started = false;
            }

            if (!shouldStopTransport)
                return;

            // accept loop 는 listener.AcceptAsync 에서 대기하므로, 먼저 취소 표식을 세우고 listener 를 닫아
            // backend 별 close 예외나 취소 예외를 정상 종료 경로로 수렴시킨다.
            acceptLoopCancellation?.Cancel();
            listener?.Close();
            listener?.Dispose();

            if (acceptLoopTask != null)
                await WaitForAcceptLoopToStopAsync(acceptLoopTask).ConfigureAwait(false);

            acceptLoopCancellation?.Dispose();
            await _transport.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 서버 수명 종료 시 stop 과 같은 정리를 수행한다.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            StopAsync().AsTask().GetAwaiter().GetResult();
            _disposed = true;
        }

        private async Task AcceptLoopAsync(IConnectionListener listener, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // accepted connection 은 Transport 가 이미 추적하고 receive/send pump 를 관리한다.
                    // Server 는 accept 를 계속 걸어 새 연결이 들어올 수 있게 하는 수명 orchestration 만 맡는다.
                    await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (InvalidOperationException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }

        private static async Task WaitForAcceptLoopToStopAsync(Task acceptLoopTask)
        {
            try
            {
                await acceptLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        private static void CleanupFailedStart(IConnectionListener? listener, CancellationTokenSource? acceptLoopCancellation)
        {
            acceptLoopCancellation?.Cancel();
            listener?.Close();
            listener?.Dispose();
            acceptLoopCancellation?.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BrokerServer));
        }
    }
}
