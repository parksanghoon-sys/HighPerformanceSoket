using System.Runtime.InteropServices;

namespace Hps.Transport
{
    /// <summary>
    /// RIO backend 사용 가능성을 부작용 없이 확인하는 진입점이다.
    /// 실제 function table load는 다음 task에서 이 경계 뒤에 붙인다.
    /// </summary>
    public static class RioCapabilityProbe
    {
        public static RioCapabilityStatus GetStatus()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return RioCapabilityStatus.UnsupportedOperatingSystem;

            return RioCapabilityStatus.Unavailable;
        }
    }
}
