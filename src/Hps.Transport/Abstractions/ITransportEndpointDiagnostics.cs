namespace Hps.Transport
{
    /// <summary>
    /// Transport 가 현재 보유한 logical endpoint 목록을 값 snapshot 으로 노출하는 선택적 진단 capability 이다.
    ///
    /// 기본 <see cref="ITransport"/> 송수신 계약을 넓히지 않고, 운영/벤치마크/후속 Broker 전환 코드가 endpoint 단위 상태를
    /// 필요로 할 때만 이 인터페이스로 좁혀서 사용한다. 반환값은 socket, connection, UDP endpoint 같은 수명 있는 handle 을
    /// 포함하지 않는 불변 값이므로 닫힌 연결 참조를 붙잡지 않는다.
    /// </summary>
    public interface ITransportEndpointDiagnostics
    {
        /// <summary>
        /// Snapshot 생성 시점에 Transport 가 추적 중인 active endpoint 목록을 읽는다.
        ///
        /// 닫힌 endpoint 는 backend tracking 목록에서 제거되므로 일반적으로 반환되지 않는다. 단, close 와 snapshot 이 경합하면
        /// 해당 항목이 <see cref="EndpointState.Closed"/> 로 관측될 수 있으며, 호출자는 snapshot 을 일회성 관측값으로 다뤄야 한다.
        /// </summary>
        EndpointSnapshot[] GetEndpointSnapshots();
    }
}
