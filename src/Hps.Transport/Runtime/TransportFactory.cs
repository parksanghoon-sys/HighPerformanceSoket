namespace Hps.Transport
{
    /// <summary>
    /// 실행 환경에 맞는 <see cref="ITransport"/> 구현을 선택하는 진입점이다.
    ///
    /// 현재 Phase 2 기준선에서는 RIO/io_uring capability probe 가 아직 없으므로 항상
    /// 크로스플랫폼 기준선인 <see cref="SaeaTransport"/> 로 fallback 한다. 상위 계층은 이 factory 를 통해
    /// <see cref="ITransport"/> 만 받아야 하며, concrete backend 타입 선택은 이후 이 위치에서 확장한다.
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
