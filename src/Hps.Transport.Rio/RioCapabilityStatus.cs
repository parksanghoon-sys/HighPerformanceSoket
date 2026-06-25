namespace Hps.Transport
{
    /// <summary>
    /// 현재 process에서 RIO backend를 사용할 수 있는지 나타내는 probe 결과다.
    /// </summary>
    public enum RioCapabilityStatus
    {
        Available = 0,
        UnsupportedOperatingSystem = 1,
        Unavailable = 2
    }
}
