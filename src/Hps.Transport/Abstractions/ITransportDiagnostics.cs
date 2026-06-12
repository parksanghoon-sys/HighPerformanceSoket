namespace Hps.Transport
{
    /// <summary>
    /// Transport 구현이 선택적으로 제공하는 관측성 snapshot 계약이다.
    ///
    /// 이 인터페이스는 <see cref="ITransport"/> 필수 수명/송수신 계약을 넓히지 않기 위해 별도 capability 로 둔다.
    /// 운영자나 상위 host 는 구현이 이 인터페이스를 제공할 때만 drop-oldest 누적값을 읽을 수 있다.
    /// </summary>
    public interface ITransportDiagnostics
    {
        /// <summary>
        /// 현재 Transport 수명 동안 누적된 진단 counter 를 읽는다.
        ///
        /// 반환값은 reset 되지 않는 누적 snapshot 이며, connection 또는 UDP endpoint 가 닫힌 뒤에도 이미 발생한 drop 은
        /// 유지된다. 이 메서드는 관측용이므로 hot path 에서 lock 을 요구하지 않는 원자적 counter snapshot 만 제공한다.
        /// </summary>
        TransportDiagnosticsSnapshot GetDiagnosticsSnapshot();
    }
}
