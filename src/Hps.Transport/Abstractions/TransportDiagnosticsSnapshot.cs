namespace Hps.Transport
{
    /// <summary>
    /// Transport 수명 동안 누적된 관측성 counter 의 불변 snapshot 이다.
    ///
    /// 현재는 drop-oldest backpressure 로 실제 socket 에 쓰이지 못하고 버려진 pending send 수만 포함한다.
    /// 이후 public metric 을 확장하더라도 기존 counter 의미를 바꾸지 않기 위해 TCP/UDP 값을 분리해 둔다.
    /// </summary>
    public readonly struct TransportDiagnosticsSnapshot
    {
        /// <summary>
        /// 누적 counter 값을 가진 snapshot 을 만든다.
        /// </summary>
        public TransportDiagnosticsSnapshot(long tcpDroppedPendingSendCount, long udpDroppedPendingSendCount)
        {
            TcpDroppedPendingSendCount = tcpDroppedPendingSendCount;
            UdpDroppedPendingSendCount = udpDroppedPendingSendCount;
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
        /// TCP와 UDP drop-oldest evict 수를 합산한 전체 pending send drop 수이다.
        /// </summary>
        public long DroppedPendingSendCount => TcpDroppedPendingSendCount + UdpDroppedPendingSendCount;
    }
}
