namespace Hps.Transport
{
    /// <summary>
    /// 실행 환경에 맞는 <see cref="ITransport"/> 구현을 선택하는 진입점이다.
    ///
    /// RIO backend 는 현재 Windows TCP opt-in 경로로만 검증 중이다. 기본 backend 는 TCP/UDP parity 를
    /// 모두 만족해야 하므로, <see cref="CreateDefault"/>는 D108 기준으로 계속 크로스플랫폼 SAEA
    /// 기준선을 반환한다.
    /// </summary>
    public static class TransportFactory
    {
        /// <summary>
        /// 현재 실행 환경에서 사용할 기본 Transport 구현을 만든다.
        ///
        /// 반환된 instance 의 수명은 호출자가 소유하며, 사용이 끝나면 <see cref="ITransport.Dispose"/> 해야 한다.
        /// </summary>
        public static ITransport CreateDefault()
        {
            return new SaeaTransport();
        }
    }
}
