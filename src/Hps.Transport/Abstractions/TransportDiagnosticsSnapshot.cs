using System;

namespace Hps.Transport
{
    /// <summary>
    /// Transport 수명 동안 누적된 관측성 counter 값을 담는 불변 snapshot 이다.
    ///
    /// drop-oldest 로 버려진 pending send 수와, enqueue 직후 관측한 pending send queue 최대 깊이를
    /// TCP/UDP 별로 분리한다. high-watermark 는 endpoint identity 가 아니라 transport kind 별 lifetime
    /// 최대값이므로, 닫힌 connection/endpoint 의 queue 깊이를 다시 스캔하지 않는다.
    /// </summary>
    public readonly struct TransportDiagnosticsSnapshot
    {
        /// <summary>
        /// drop counter 만 가진 snapshot 을 만든다.
        /// 기존 호출자는 high-watermark 관측값이 없으므로 기본값 0으로 기록한다.
        /// </summary>
        public TransportDiagnosticsSnapshot(long tcpDroppedPendingSendCount, long udpDroppedPendingSendCount)
            : this(tcpDroppedPendingSendCount, udpDroppedPendingSendCount, 0, 0)
        {
        }

        /// <summary>
        /// 누적 drop counter 와 Transport 수명 동안 관측한 pending send queue 최대 깊이를 가진 snapshot 을 만든다.
        /// high-watermark 는 drop-oldest 의 capacity 로 포화될 수 있으므로 drop counter 와 함께 해석해야 한다.
        /// </summary>
        public TransportDiagnosticsSnapshot(
            long tcpDroppedPendingSendCount,
            long udpDroppedPendingSendCount,
            int tcpPendingSendQueueHighWatermark,
            int udpPendingSendQueueHighWatermark)
        {
            if (tcpPendingSendQueueHighWatermark < 0)
                throw new ArgumentOutOfRangeException(nameof(tcpPendingSendQueueHighWatermark));

            if (udpPendingSendQueueHighWatermark < 0)
                throw new ArgumentOutOfRangeException(nameof(udpPendingSendQueueHighWatermark));

            TcpDroppedPendingSendCount = tcpDroppedPendingSendCount;
            UdpDroppedPendingSendCount = udpDroppedPendingSendCount;
            TcpPendingSendQueueHighWatermark = tcpPendingSendQueueHighWatermark;
            UdpPendingSendQueueHighWatermark = udpPendingSendQueueHighWatermark;
        }

        /// <summary>
        /// TCP connection pending send queue 에서 drop-oldest 로 evict 된 누적 send 수이다.
        /// </summary>
        public long TcpDroppedPendingSendCount { get; }

        /// <summary>
        /// UDP endpoint pending send queue 에서 drop-oldest 로 evict 된 누적 datagram 수이다.
        /// </summary>
        public long UdpDroppedPendingSendCount { get; }

        /// <summary>
        /// Transport 수명 동안 TCP pending send queue 가 enqueue 직후 도달한 최대 깊이이다.
        /// </summary>
        public int TcpPendingSendQueueHighWatermark { get; }

        /// <summary>
        /// Transport 수명 동안 UDP pending send queue 가 enqueue 직후 도달한 최대 깊이이다.
        /// </summary>
        public int UdpPendingSendQueueHighWatermark { get; }

        /// <summary>
        /// TCP 와 UDP drop-oldest evict 수를 합산한 전체 pending send drop 수이다.
        /// </summary>
        public long DroppedPendingSendCount => TcpDroppedPendingSendCount + UdpDroppedPendingSendCount;
    }
}
