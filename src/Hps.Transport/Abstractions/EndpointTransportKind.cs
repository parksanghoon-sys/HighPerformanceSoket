namespace Hps.Transport
{
    /// <summary>
    /// Logical endpoint 가 현재 사용하는 transport 종류이다.
    ///
    /// TCP connection 과 UDP remote endpoint 는 송신 방식과 오류 정책이 다르지만, Interface Server 의
    /// subscription/snapshot 계층에서는 같은 endpoint 모델 아래에서 구분되어야 한다.
    /// </summary>
    public enum EndpointTransportKind
    {
        /// <summary>
        /// Length-prefixed TCP stream 을 사용하는 endpoint 이다.
        /// </summary>
        Tcp = 1,

        /// <summary>
        /// Datagram 단위 UDP 송신을 사용하는 endpoint 이다.
        /// </summary>
        Udp = 2
    }
}
