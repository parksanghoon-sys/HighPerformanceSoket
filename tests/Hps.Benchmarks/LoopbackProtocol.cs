namespace Hps.Benchmarks
{
    /// <summary>
    /// loopback benchmark runner 가 사용할 wire protocol selector 이다.
    ///
    /// 기본값은 기존 Phase 4 baseline 과 호환되는 TCP이며, UDP는 D112의 artifact 수집 경로에서
    /// 같은 load/open-loop command shape 를 재사용하기 위한 명시 선택값이다.
    /// </summary>
    internal enum LoopbackProtocol
    {
        Tcp,
        Udp
    }
}
