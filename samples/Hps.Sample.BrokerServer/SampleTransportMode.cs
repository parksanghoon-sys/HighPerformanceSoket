namespace Hps.Sample.BrokerServer
{
    /// <summary>
    /// sample broker host 가 사용할 transport 선택 모드다.
    /// 기본 SAEA, 명시 RIO, preferred auto fallback 의 의미를 parser 단계에서 보존한다.
    /// </summary>
    public enum SampleTransportMode
    {
        Saea,
        Rio,
        Auto,
        IoUring
    }
}
