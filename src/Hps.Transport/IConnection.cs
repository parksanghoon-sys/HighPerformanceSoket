using System;

namespace Hps.Transport
{
    /// <summary>
    /// Transport 계층이 관리하는 단일 연결의 송신 소유권 경계이다.
    ///
    /// 구현은 D007 계약에 따라 여러 발행자가 직접 송신 <c>BipBuffer</c> 에 쓰게 하지 않고,
    /// <see cref="TryQueueSend"/> 로 받은 항목을 연결별 MPSC 큐에 넣은 뒤 단일 송신 펌프가 SPSC
    /// 송신 버퍼를 채워야 한다.
    /// </summary>
    public interface IConnection : IDisposable
    {
        /// <summary>
        /// 송신할 버퍼 참조를 연결 송신 큐에 넣는다.
        ///
        /// 반환값이 <c>true</c> 이면 연결은 <paramref name="sendBuffer"/> 의 참조 1개를 소유한다.
        /// 이후 송신 완료, backpressure drop, 또는 <see cref="Close"/>/<see cref="Dispose"/> drain 중
        /// 정확히 한 경로에서 해당 버퍼를 Release 해야 한다.
        ///
        /// 반환값이 <c>false</c> 이면 연결은 소유권을 전혀 가져가지 않았다. 호출자는 D009 계약에 따라
        /// 자신이 추가했던 구독자 ref 를 즉시 Release 해야 한다. close 와 enqueue 경합에서도 이 결정은
        /// 원자적으로 보여야 한다.
        /// </summary>
        bool TryQueueSend(TransportSendBuffer sendBuffer);

        /// <summary>
        /// 연결을 닫고 이후 enqueue 를 거부한다. 구현은 pending 큐 항목, 송신 중 in-flight 항목,
        /// 조립 중 수신 버퍼를 모두 Release 해야 하며, 종료 후 풀 누수가 남아서는 안 된다.
        /// </summary>
        void Close();
    }
}
