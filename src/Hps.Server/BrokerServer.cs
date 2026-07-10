using System;
using System.Diagnostics;
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
        private const int SubscriberCountPollIntervalMilliseconds = 10;

        private readonly object _gate;
        private readonly ITransport _transport;
        private readonly PinnedBlockMemoryPool _pool;
        private readonly int _maxPayloadLength;
        private readonly BrokerServerOptions _options;
        private readonly SubscriptionTable _subscriptions;
        private readonly SubscriberRegistry? _subscriberRegistry;
        private readonly BrokerPublisher _publisher;
        private readonly BrokerTcpFrameHandler _brokerFrameHandler;
        private readonly BrokerUdpDatagramHandler _brokerDatagramHandler;
        private IConnectionListener? _tcpListener;
        private IUdpEndpoint? _udpEndpoint;
        private ITimer? _udpLeaseSweepTimer;
        private ITimer? _subscriberRetentionTimer;
        private CancellationTokenSource? _acceptLoopCancellation;
        private Task? _acceptLoopTask;
        private bool _transportStarted;
        private bool _tcpStarted;
        private bool _udpStarted;
        private bool _disposed;

        /// <summary>
        /// 테스트 가능한 서버 host 를 만든다.
        ///
        /// <paramref name="transport"/> 는 실제 socket backend 이고, <paramref name="pool"/> 은 TCP frame payload 를
        /// 담는 소유권 버퍼를 대여한다. <paramref name="maxPayloadLength"/> 는 Protocol 조립기의 DoS 방지 상한이다.
        /// </summary>
        public BrokerServer(ITransport transport, PinnedBlockMemoryPool pool, int maxPayloadLength)
            : this(transport, pool, maxPayloadLength, BrokerServerOptions.Default)
        {
        }

        /// <summary>
        /// 선택적 host 설정을 포함하는 테스트 가능한 서버 host 를 만든다.
        ///
        /// UDP lease sweep 같은 host 수명 기능은 Transport/Broker 내부가 아니라 Server 가 소유한다.
        /// 이 생성자는 테스트에서 가상 시간을 주입할 수 있게 하되, 기본 생성자와 동일한 초기화 경로를 사용한다.
        /// </summary>
        public BrokerServer(
            ITransport transport,
            PinnedBlockMemoryPool pool,
            int maxPayloadLength,
            BrokerServerOptions options)
        {
            if (transport == null)
                throw new ArgumentNullException(nameof(transport));
            if (pool == null)
                throw new ArgumentNullException(nameof(pool));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (maxPayloadLength < 0)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadLength));
            if (maxPayloadLength > pool.BlockSize)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadLength), "최대 payload 길이는 풀 블록 크기를 넘을 수 없다.");

            _transport = transport;
            _pool = pool;
            _maxPayloadLength = maxPayloadLength;
            _options = options;
            _gate = new object();
            _subscriptions = new SubscriptionTable();
            _subscriberRegistry = _options.StableSubscriberIdentityEnabled
                ? new SubscriberRegistry(_subscriptions)
                : null;
            _publisher = new BrokerPublisher(_subscriptions, _transport);
            _brokerFrameHandler = new BrokerTcpFrameHandler(_subscriptions, _publisher, _subscriberRegistry, _options.TimeProvider);
            _brokerDatagramHandler = new BrokerUdpDatagramHandler(
                _subscriptions,
                _publisher,
                CreateUdpLeaseOptions(_options),
                _options.TimeProvider,
                _subscriberRegistry);
        }

        /// <summary>
        /// 현재 TCP listener 가 실제로 bind 된 endpoint 이다. 아직 시작하지 않았으면 <c>null</c> 이다.
        /// </summary>
        public EndPoint? LocalEndPoint { get; private set; }

        /// <summary>
        /// 현재 UDP endpoint 가 실제로 bind 된 endpoint 이다. 아직 시작하지 않았으면 <c>null</c> 이다.
        /// </summary>
        public EndPoint? UdpLocalEndPoint { get; private set; }

        /// <summary>
        /// in-process host orchestration에서 지정 topic의 현재 구독자 수가 최소값에 도달할 때까지 기다린다.
        ///
        /// 이 완료는 일시적인 aggregate count 관측이며 wire SUBSCRIBE ACK, 특정 endpoint의 구독 유지,
        /// 이후 publish 전달을 보장하지 않는다. publish hot path가 아닌 smoke/benchmark setup 경계에서 사용한다.
        /// </summary>
        /// <param name="topic">현재 구독자 수를 관측할 topic이다.</param>
        /// <param name="minimumCount">완료에 필요한 최소 구독자 수다.</param>
        /// <param name="timeout">조건을 관측할 수 있는 최대 시간이다.</param>
        /// <param name="cancellationToken">대기 중단을 요청하는 cancellation token이다.</param>
        /// <returns>최소 구독자 수를 제한 시간 안에 관측하면 완료되는 task다.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="topic"/>이 <c>null</c>이다.</exception>
        /// <exception cref="ArgumentException"><paramref name="topic"/>이 비어 있다.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="minimumCount"/>가 음수이거나 <paramref name="timeout"/>이 0 이하이다.
        /// </exception>
        /// <exception cref="TimeoutException">제한 시간 안에 최소 구독자 수를 관측하지 못했다.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/>이 취소됐다.</exception>
        public Task WaitForSubscriberCountAsync(
            string topic,
            int minimumCount,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (minimumCount < 0)
                throw new ArgumentOutOfRangeException(nameof(minimumCount));
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout));

            cancellationToken.ThrowIfCancellationRequested();
            if (_subscriptions.CountSubscribers(topic) >= minimumCount)
                return Task.CompletedTask;

            return WaitForSubscriberCountCoreAsync(topic, minimumCount, timeout, cancellationToken);
        }

        private async Task WaitForSubscriberCountCoreAsync(
            string topic,
            int minimumCount,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            long startedAt = Stopwatch.GetTimestamp();
            TimeSpan pollInterval = TimeSpan.FromMilliseconds(SubscriberCountPollIntervalMilliseconds);

            while (true)
            {
                TimeSpan remaining = timeout - Stopwatch.GetElapsedTime(startedAt);
                if (remaining <= TimeSpan.Zero)
                    break;

                // 마지막 poll은 남은 제한 시간만 기다리고, 깨어난 뒤 deadline을 먼저 판정한다.
                // 그래야 scheduler 지연 중 deadline 뒤에 등록된 구독자를 성공으로 잘못 수락하지 않는다.
                TimeSpan delay = remaining < pollInterval ? remaining : pollInterval;
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                if (Stopwatch.GetElapsedTime(startedAt) >= timeout)
                    break;
                if (_subscriptions.CountSubscribers(topic) >= minimumCount)
                    return;
            }

            throw new TimeoutException("Broker subscriber count가 제한 시간 안에 목표 값에 도달하지 않았다.");
        }

        /// <summary>
        /// TCP broker 수신 대기를 시작한다.
        /// </summary>
        public async ValueTask StartTcpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException(nameof(localEndPoint));

            bool shouldStartTransport;

            lock (_gate)
            {
                ThrowIfDisposed();
                if (_tcpStarted)
                    throw new InvalidOperationException("BrokerServer TCP listener 는 이미 시작됐다.");

                shouldStartTransport = !_transportStarted;
                _transportStarted = true;
                _tcpStarted = true;
            }

            IConnectionListener? listener = null;
            CancellationTokenSource? acceptLoopCancellation = null;
            Task? acceptLoopTask = null;

            try
            {
                if (shouldStartTransport)
                    await _transport.StartAsync(cancellationToken).ConfigureAwait(false);

                _transport.SetReceiveHandler(CreateTcpReceiveHandler());
                listener = await _transport.ListenTcpAsync(localEndPoint, cancellationToken).ConfigureAwait(false);
                acceptLoopCancellation = new CancellationTokenSource();
                acceptLoopTask = Task.Run(() => AcceptLoopAsync(listener, acceptLoopCancellation.Token));

                lock (_gate)
                {
                    _tcpListener = listener;
                    _acceptLoopCancellation = acceptLoopCancellation;
                    _acceptLoopTask = acceptLoopTask;
                    LocalEndPoint = listener.LocalEndPoint;
                    EnsureSubscriberRetentionTimerStarted();
                }
            }
            catch
            {
                CleanupFailedStart(listener, acceptLoopCancellation);

                bool shouldStopTransport = false;

                lock (_gate)
                {
                    _tcpListener = null;
                    _acceptLoopCancellation = null;
                    _acceptLoopTask = null;
                    LocalEndPoint = null;
                    _tcpStarted = false;

                    if (!_udpStarted)
                    {
                        shouldStopTransport = _transportStarted;
                        _transportStarted = false;
                    }
                }

                if (shouldStopTransport)
                    await _transport.StopAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// UDP broker datagram 수신 대기를 시작한다.
        /// </summary>
        public async ValueTask StartUdpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException(nameof(localEndPoint));

            bool shouldStartTransport;

            lock (_gate)
            {
                ThrowIfDisposed();
                if (_udpStarted)
                    throw new InvalidOperationException("BrokerServer UDP endpoint 는 이미 시작됐다.");

                shouldStartTransport = !_transportStarted;
                _transportStarted = true;
                _udpStarted = true;
            }

            IUdpEndpoint? endpoint = null;
            ITimer? udpLeaseSweepTimer = null;

            try
            {
                _transport.SetDatagramHandler(_brokerDatagramHandler);
                if (shouldStartTransport)
                    await _transport.StartAsync(cancellationToken).ConfigureAwait(false);

                endpoint = await _transport.BindUdpAsync(localEndPoint, cancellationToken).ConfigureAwait(false);
                udpLeaseSweepTimer = CreateUdpLeaseSweepTimer();

                lock (_gate)
                {
                    _udpEndpoint = endpoint;
                    _udpLeaseSweepTimer = udpLeaseSweepTimer;
                    UdpLocalEndPoint = endpoint.LocalEndPoint;
                    EnsureSubscriberRetentionTimerStarted();
                }
            }
            catch
            {
                CleanupFailedUdpStart(endpoint, udpLeaseSweepTimer);

                bool shouldStopTransport = false;

                lock (_gate)
                {
                    _udpEndpoint = null;
                    UdpLocalEndPoint = null;
                    _udpStarted = false;

                    if (!_tcpStarted)
                    {
                        shouldStopTransport = _transportStarted;
                        _transportStarted = false;
                    }
                }

                if (shouldStopTransport)
                    await _transport.StopAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// TCP listener, UDP endpoint, Transport 를 중지한다.
        /// </summary>
        public async ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            IConnectionListener? listener;
            IUdpEndpoint? udpEndpoint;
            ITimer? udpLeaseSweepTimer;
            ITimer? subscriberRetentionTimer;
            CancellationTokenSource? acceptLoopCancellation;
            Task? acceptLoopTask;
            bool shouldStopTransport;

            lock (_gate)
            {
                listener = _tcpListener;
                udpEndpoint = _udpEndpoint;
                udpLeaseSweepTimer = _udpLeaseSweepTimer;
                subscriberRetentionTimer = _subscriberRetentionTimer;
                acceptLoopCancellation = _acceptLoopCancellation;
                acceptLoopTask = _acceptLoopTask;
                shouldStopTransport = _transportStarted;

                _tcpListener = null;
                _udpEndpoint = null;
                _udpLeaseSweepTimer = null;
                _subscriberRetentionTimer = null;
                _acceptLoopCancellation = null;
                _acceptLoopTask = null;
                LocalEndPoint = null;
                UdpLocalEndPoint = null;
                _transportStarted = false;
                _tcpStarted = false;
                _udpStarted = false;
            }

            udpLeaseSweepTimer?.Dispose();
            subscriberRetentionTimer?.Dispose();

            if (!shouldStopTransport)
                return;

            // accept loop 는 listener.AcceptAsync 에서 대기하므로, 먼저 취소 표식을 세우고 listener 를 닫아
            // backend 별 close 예외나 취소 예외를 정상 종료 경로로 수렴시킨다.
            acceptLoopCancellation?.Cancel();
            listener?.Close();
            listener?.Dispose();
            udpEndpoint?.Close();
            udpEndpoint?.Dispose();

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

        private static void CleanupFailedUdpStart(IUdpEndpoint? endpoint, ITimer? udpLeaseSweepTimer)
        {
            udpLeaseSweepTimer?.Dispose();
            // BindUdpAsync 이후 예외가 나면 endpoint 소유권은 Server 로 넘어온 상태일 수 있으므로 즉시 닫는다.
            endpoint?.Close();
            endpoint?.Dispose();
        }

        private static UdpLeaseOptions CreateUdpLeaseOptions(BrokerServerOptions options)
        {
            if (!options.UdpLeaseSweepEnabled)
                return UdpLeaseOptions.Disabled;

            return UdpLeaseOptions.CreateEnabled(options.UdpLeaseIdleTimeout, options.UdpLeaseSweepInterval);
        }

        private TcpFrameReceiveHandler CreateTcpReceiveHandler()
        {
            IRefCountedBufferSource source = _pool;
            ITransportPayloadBufferSourceProvider? provider = _transport as ITransportPayloadBufferSourceProvider;
            if (provider != null)
                source = provider.CreateTcpPayloadBufferSource(_pool);

            return new TcpFrameReceiveHandler(source, _maxPayloadLength, _brokerFrameHandler);
        }

        private ITimer? CreateUdpLeaseSweepTimer()
        {
            if (!_options.UdpLeaseSweepEnabled)
                return null;

            return _options.TimeProvider.CreateTimer(
                OnUdpLeaseSweepTimer,
                null,
                _options.UdpLeaseSweepInterval,
                _options.UdpLeaseSweepInterval);
        }

        private void OnUdpLeaseSweepTimer(object? state)
        {
            _brokerDatagramHandler.SweepExpiredUdpLeases(_options.TimeProvider.GetUtcNow());
        }

        private ITimer? CreateSubscriberRetentionTimer()
        {
            if (!_options.StableSubscriberIdentityEnabled || _subscriberRegistry == null)
                return null;

            // stable identity 는 disconnected topic metadata 만 보존하므로 별도 payload drain 이 없다.
            // retention timeout 과 sweep period 를 같은 값으로 두어, 설정된 보존 시간을 지난 항목만 주기적으로 정리한다.
            return _options.TimeProvider.CreateTimer(
                OnSubscriberRetentionTimer,
                null,
                _options.StableSubscriberRetentionTimeout,
                _options.StableSubscriberRetentionTimeout);
        }

        private void EnsureSubscriberRetentionTimerStarted()
        {
            // TCP와 UDP ingress 를 독립적으로 시작할 수 있으므로 양쪽 start 성공 경로에서 호출된다.
            // host 전체에 shared registry 는 하나뿐이라 timer 도 하나만 만들어야 한다.
            if (_subscriberRetentionTimer != null)
                return;

            _subscriberRetentionTimer = CreateSubscriberRetentionTimer();
        }

        private void OnSubscriberRetentionTimer(object? state)
        {
            if (_subscriberRegistry == null)
                return;

            // timer callback 은 transport thread 와 별개로 들어올 수 있다.
            // SubscriberRegistry 내부 lock 이 entry 제거와 routing cleanup 을 직렬화하므로 여기서는 현재 시간만 전달한다.
            _subscriberRegistry.SweepDisconnected(
                _options.TimeProvider.GetUtcNow(),
                _options.StableSubscriberRetentionTimeout);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BrokerServer));
        }
    }
}
