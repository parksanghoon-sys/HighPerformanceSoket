using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hps.Buffers;

namespace Hps.Transport
{
    /// <summary>
    /// Transport 구현들이 공유할 송신 소유권 처리 골격이다.
    ///
    /// 실제 SAEA/RIO/io_uring 송신은 파생 구현이 붙이지만, <see cref="ITransport.TrySend"/> 의
    /// "성공 시 Transport 소유, 실패 시 호출자 소유" 판정은 backend 마다 달라지면 안 된다.
    /// </summary>
    public abstract class TransportBase : ITransport, ITransportDiagnostics
    {
        private ITransportReceiveHandler? _receiveHandler;
        private ITransportDatagramHandler? _datagramHandler;
        private long _tcpDroppedPendingSendCount;
        private long _udpDroppedPendingSendCount;
        private int _tcpPendingSendQueueHighWatermark;
        private int _udpPendingSendQueueHighWatermark;
        private long _nextEndpointId;

        /// <summary>
        /// 새 내부 연결 상태를 만든다. 이후 listen/accept/connect 구현은 이 연결을 <see cref="IConnection"/>
        /// 으로 상위 계층에 넘긴다.
        /// </summary>
        internal TransportConnection CreateConnection()
        {
            return new TransportConnection(CreateEndpointId(), null, null, RecordTcpPendingSendDrop, RecordTcpPendingSendDepth);
        }

        /// <inheritdoc />
        public void SetReceiveHandler(ITransportReceiveHandler receiveHandler)
        {
            if (receiveHandler == null)
                throw new ArgumentNullException(nameof(receiveHandler));

            Volatile.Write(ref _receiveHandler, receiveHandler);
        }

        /// <inheritdoc />
        public void SetDatagramHandler(ITransportDatagramHandler datagramHandler)
        {
            if (datagramHandler == null)
                throw new ArgumentNullException(nameof(datagramHandler));

            Volatile.Write(ref _datagramHandler, datagramHandler);
        }

        /// <summary>
        /// receive pump 가 현재 등록된 handler 를 관측한다.
        /// handler 등록과 pump 시작은 서로 다른 스레드에서 일어날 수 있으므로 Volatile snapshot 으로 읽는다.
        /// </summary>
        internal ITransportReceiveHandler? ReadReceiveHandlerSnapshot()
        {
            return Volatile.Read(ref _receiveHandler);
        }

        /// <summary>
        /// UDP receive pump 가 현재 등록된 datagram handler 를 관측한다.
        /// handler 등록과 pump 시작은 서로 다른 스레드에서 일어날 수 있으므로 Volatile snapshot 으로 읽는다.
        /// </summary>
        internal ITransportDatagramHandler? ReadDatagramHandlerSnapshot()
        {
            return Volatile.Read(ref _datagramHandler);
        }

        /// <inheritdoc />
        public TransportDiagnosticsSnapshot GetDiagnosticsSnapshot()
        {
            return new TransportDiagnosticsSnapshot(
                ReadTcpDroppedPendingSendCount(),
                ReadUdpDroppedPendingSendCount(),
                ReadTcpPendingSendQueueHighWatermark(),
                ReadUdpPendingSendQueueHighWatermark());
        }

        /// <summary>
        /// 지정한 연결의 pending 송신 큐에 송신 요청을 넣는다.
        /// </summary>
        public bool TrySend(IConnection connection, TransportSendBuffer sendBuffer)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            TransportConnection? transportConnection = connection as TransportConnection;
            if (transportConnection == null)
                throw new ArgumentException("이 Transport 구현이 생성한 연결만 사용할 수 있다.", nameof(connection));

            // Transport 가 큐 소유권을 받기 전에 요청이 실제 live RefCountedBuffer 를 가리키는지 확인한다.
            // TransportSendBuffer 는 struct 이므로 default 값이 public API 로 들어올 수 있고, 이미 반환된 버퍼도
            // 생성 이후 잘못 Release 되었을 수 있다. 여기서 실패시키면 pending drain 시점의 늦은 예외와 누수를 피할 수 있다.
            RefCountedBuffer buffer = sendBuffer.Buffer;
            _ = buffer.Memory;

            return transportConnection.TryAcceptSend(sendBuffer);
        }

        /// <inheritdoc />
        public abstract ValueTask<IConnectionListener> ListenTcpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default);

        /// <inheritdoc />
        public abstract ValueTask<IConnection> ConnectTcpAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken = default);

        /// <inheritdoc />
        public virtual ValueTask<IUdpEndpoint> BindUdpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public virtual bool TrySendTo(IUdpEndpoint endpoint, EndPoint remoteEndPoint, TransportSendBuffer sendBuffer)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// TCP connection pending queue 의 drop-oldest 발생을 Transport 수명 누적 counter 에 기록한다.
        /// connection 내부 counter 는 connection 수명에 묶이므로, public diagnostics 는 별도 누적 counter 를 유지한다.
        /// </summary>
        internal void RecordTcpPendingSendDrop()
        {
            Interlocked.Increment(ref _tcpDroppedPendingSendCount);
        }

        /// <summary>
        /// UDP endpoint pending queue 의 drop-oldest 발생을 Transport 수명 누적 counter 에 기록한다.
        /// endpoint 가 닫힌 뒤에도 운영자가 drop 발생 여부를 읽을 수 있게 endpoint 내부 counter 와 분리한다.
        /// </summary>
        internal void RecordUdpPendingSendDrop()
        {
            Interlocked.Increment(ref _udpDroppedPendingSendCount);
        }

        /// <summary>
        /// TCP pending send queue 가 enqueue 직후 관측한 깊이를 Transport lifetime high-watermark 에 반영한다.
        /// 여러 connection 의 send path 가 동시에 호출될 수 있으므로 lock 없이 CAS max update 로 합류시킨다.
        /// </summary>
        internal void RecordTcpPendingSendDepth(int pendingDepth)
        {
            UpdateMax(ref _tcpPendingSendQueueHighWatermark, pendingDepth);
        }

        /// <summary>
        /// UDP endpoint pending send queue 가 enqueue 직후 관측한 깊이를 Transport lifetime high-watermark 에 반영한다.
        /// endpoint 가 닫히면 queue 를 다시 볼 수 없으므로 enqueue 시점에만 누적 max 를 갱신한다.
        /// </summary>
        internal void RecordUdpPendingSendDepth(int pendingDepth)
        {
            UpdateMax(ref _udpPendingSendQueueHighWatermark, pendingDepth);
        }

        /// <summary>
        /// Transport 수명 안에서 TCP connection 과 UDP endpoint 에 공통으로 쓸 transient endpoint id 를 발급한다.
        /// id 는 외부 subscriber 의 stable id 가 아니라 현재 process/runtime 관측용 값이므로 reconnect binding 은 후속 registry 가 맡는다.
        /// </summary>
        internal EndpointId CreateEndpointId()
        {
            long value = Interlocked.Increment(ref _nextEndpointId);
            return new EndpointId(value);
        }

        /// <summary>
        /// diagnostics snapshot 생성 시 TCP drop 누적값을 원자적으로 관측한다.
        /// counter 는 drop hot path 에서 Interlocked 로 증가하므로 snapshot 도 같은 메모리 가시성 경계로 읽는다.
        /// </summary>
        private long ReadTcpDroppedPendingSendCount()
        {
            return Volatile.Read(ref _tcpDroppedPendingSendCount);
        }

        /// <summary>
        /// diagnostics snapshot 생성 시 UDP drop 누적값을 원자적으로 관측한다.
        /// endpoint 가 이미 닫혔더라도 Transport 수명 counter 는 유지되므로 endpoint 목록 lock 을 잡지 않는다.
        /// </summary>
        private long ReadUdpDroppedPendingSendCount()
        {
            return Volatile.Read(ref _udpDroppedPendingSendCount);
        }

        /// <summary>
        /// diagnostics snapshot 생성을 위해 TCP pending send queue lifetime high-watermark 를 읽는다.
        /// 값은 Interlocked.CompareExchange 로만 증가하므로 Volatile.Read 만으로 최신 관측값을 안전하게 얻는다.
        /// </summary>
        private int ReadTcpPendingSendQueueHighWatermark()
        {
            return Volatile.Read(ref _tcpPendingSendQueueHighWatermark);
        }

        /// <summary>
        /// diagnostics snapshot 생성을 위해 UDP pending send queue lifetime high-watermark 를 읽는다.
        /// endpoint 목록 lock 을 잡지 않고 Transport 단위 누적 max 만 읽어 close 된 endpoint 도 포함한다.
        /// </summary>
        private int ReadUdpPendingSendQueueHighWatermark()
        {
            return Volatile.Read(ref _udpPendingSendQueueHighWatermark);
        }

        private static void UpdateMax(ref int target, int candidate)
        {
            if (candidate < 0)
                throw new ArgumentOutOfRangeException(nameof(candidate));

            while (true)
            {
                int observed = Volatile.Read(ref target);
                if (candidate <= observed)
                    return;

                int exchanged = Interlocked.CompareExchange(ref target, candidate, observed);
                if (exchanged == observed)
                    return;
            }
        }

        /// <inheritdoc />
        public abstract ValueTask StartAsync(CancellationToken cancellationToken = default);

        /// <inheritdoc />
        public abstract ValueTask StopAsync(CancellationToken cancellationToken = default);

        /// <inheritdoc />
        public virtual void Dispose()
        {
        }
    }
}
