namespace Hps.Transport
{
    /// <summary>
    /// Logical endpoint 의 수명 상태이다.
    ///
    /// 현재 Transport 는 TCP connection 과 UDP endpoint 수명을 별도 타입으로 관리하지만, Interface Server 의
    /// snapshot 은 두 transport 를 같은 상태 모델로 보여줘야 한다. 이 enum 은 관측용 계약이며, 실제 close/drain
    /// 순서는 각 transport 구현의 수명 규칙을 따른다.
    /// </summary>
    public enum EndpointState
    {
        /// <summary>
        /// 송신을 수락할 수 있는 상태이다.
        /// </summary>
        Open = 1,

        /// <summary>
        /// 닫힘이 시작되어 새 송신은 거부되거나 곧 거부될 수 있는 상태이다.
        /// </summary>
        Closing = 2,

        /// <summary>
        /// 정상적으로 닫혀 더 이상 송신하지 않는 상태이다.
        /// </summary>
        Closed = 3,

        /// <summary>
        /// handler 예외, socket error 등으로 비정상 종료된 상태이다.
        /// </summary>
        Faulted = 4
    }
}
