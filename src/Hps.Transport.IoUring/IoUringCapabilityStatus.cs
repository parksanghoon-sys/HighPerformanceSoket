namespace Hps.Transport
{
    /// <summary>
    /// 현재 process에서 Linux io_uring backend를 사용할 수 있는지 나타내는 probe 결과다.
    /// </summary>
    public enum IoUringCapabilityStatus
    {
        Available = 0,
        UnsupportedOperatingSystem = 1,
        Unavailable = 2
    }
}
