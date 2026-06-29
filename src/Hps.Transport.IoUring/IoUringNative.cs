using System;
using System.Runtime.InteropServices;

namespace Hps.Transport
{
    /// <summary>
    /// Linux io_uring syscall 과 mmap entry point 를 숨기는 internal native adapter 다.
    ///
    /// 이 타입은 pointer 수명을 소유하지 않는다. raw native 호출과 platform guard 만 담당하고,
    /// fd/mmap/registration 수명은 별도 owner 타입이 관리한다.
    /// </summary>
    internal static class IoUringNative
    {
        internal static IoUringCapabilityStatus GetPlatformStatus()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return IoUringCapabilityStatus.UnsupportedOperatingSystem;

            if (RuntimeInformation.ProcessArchitecture != Architecture.X64 &&
                RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
            {
                return IoUringCapabilityStatus.Unavailable;
            }

            return IoUringCapabilityStatus.Available;
        }

        internal static void ThrowIfUnsupportedPlatform()
        {
            IoUringCapabilityStatus status = GetPlatformStatus();
            if (status == IoUringCapabilityStatus.UnsupportedOperatingSystem)
                throw new NotSupportedException("io_uring backend는 Linux에서만 사용할 수 있습니다.");

            if (status == IoUringCapabilityStatus.Unavailable)
                throw new NotSupportedException("현재 process architecture 에서는 io_uring syscall 번호가 정의되지 않았습니다.");
        }
    }
}
