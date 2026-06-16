using System;

namespace Hps.Transport
{
    /// <summary>
    /// Interface Server 가 외부에 보여줄 logical endpoint 관측값의 최소 불변 snapshot 이다.
    ///
    /// 이 타입은 endpoint registry 와 broker subscription 전환보다 먼저 public 계약을 고정하기 위한 값 객체이다.
    /// transport handle, socket, buffer view 는 포함하지 않는다. snapshot 이 객체 참조를 붙잡으면 닫힌 connection 이
    /// GC 되지 않거나, 상위 계층이 Transport 내부 수명 경계를 우회할 수 있기 때문이다.
    /// </summary>
    public readonly struct EndpointSnapshot
    {
        /// <summary>
        /// Endpoint identity, transport 종류, 수명 상태, 송신 큐 관측값을 가진 snapshot 을 만든다.
        /// </summary>
        public EndpointSnapshot(
            EndpointId id,
            EndpointTransportKind transportKind,
            EndpointState state,
            int pendingSendCount,
            int pendingSendQueueHighWatermark,
            long droppedPendingSendCount)
        {
            if (pendingSendCount < 0)
                throw new ArgumentOutOfRangeException(nameof(pendingSendCount));

            if (pendingSendQueueHighWatermark < 0)
                throw new ArgumentOutOfRangeException(nameof(pendingSendQueueHighWatermark));

            if (droppedPendingSendCount < 0)
                throw new ArgumentOutOfRangeException(nameof(droppedPendingSendCount));

            Id = id;
            TransportKind = transportKind;
            State = state;
            PendingSendCount = pendingSendCount;
            PendingSendQueueHighWatermark = pendingSendQueueHighWatermark;
            DroppedPendingSendCount = droppedPendingSendCount;
        }

        /// <summary>
        /// Connection 객체나 UDP endpoint 객체와 분리된 logical endpoint id 이다.
        /// </summary>
        public EndpointId Id { get; }

        /// <summary>
        /// 이 snapshot 이 설명하는 endpoint 의 transport 종류이다.
        /// </summary>
        public EndpointTransportKind TransportKind { get; }

        /// <summary>
        /// Snapshot 생성 시점의 logical endpoint 수명 상태이다.
        /// </summary>
        public EndpointState State { get; }

        /// <summary>
        /// Snapshot 생성 시점에 아직 송신되지 않은 pending send 수이다.
        /// 닫힌 endpoint 에서는 0일 수 있으며, 장기 추세는 high-watermark 와 drop count 를 함께 본다.
        /// </summary>
        public int PendingSendCount { get; }

        /// <summary>
        /// 해당 endpoint 가 수명 동안 enqueue 직후 도달한 최대 pending send depth 이다.
        /// </summary>
        public int PendingSendQueueHighWatermark { get; }

        /// <summary>
        /// 해당 endpoint 에서 drop-oldest 로 버려진 누적 pending send 수이다.
        /// </summary>
        public long DroppedPendingSendCount { get; }
    }
}
