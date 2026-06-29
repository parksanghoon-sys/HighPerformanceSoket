using System.Runtime.InteropServices;

namespace Hps.Transport
{
    /// <summary>
    /// io_uring backend 사용 가능성을 부작용 없이 확인하는 진입점이다.
    ///
    /// 첫 boundary 에서는 Linux 여부만 판단한다. 실제 syscall probe 는 native wrapper task 에서
    /// 이 경계 뒤에 붙이며, 그 전까지 Linux 는 명시적으로 Unavailable 로 둔다.
    /// </summary>
    public static class IoUringCapabilityProbe
    {
        public static IoUringCapabilityStatus GetStatus()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return IoUringCapabilityStatus.UnsupportedOperatingSystem;

            return IoUringCapabilityStatus.Unavailable;
        }
    }
}
