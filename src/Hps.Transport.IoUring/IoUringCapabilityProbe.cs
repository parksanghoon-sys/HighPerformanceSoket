using System;

namespace Hps.Transport
{
    /// <summary>
    /// io_uring backend 사용 가능성을 부작용 없이 확인하는 진입점이다.
    ///
    /// non-Linux 는 syscall 로 들어가지 않고 즉시 unsupported 로 수렴한다. Linux 에서는 작은 ring 을
    /// setup/close 해보는 probe 까지만 수행하며, 실제 TCP/UDP pump 는 열지 않는다.
    /// </summary>
    public static class IoUringCapabilityProbe
    {
        /// <summary>
        /// 현재 process 에서 io_uring backend 를 사용할 수 있는지 확인한다.
        /// </summary>
        public static IoUringCapabilityStatus GetStatus()
        {
            IoUringCapabilityStatus platformStatus = IoUringNative.GetPlatformStatus();
            if (platformStatus != IoUringCapabilityStatus.Available)
                return platformStatus;

            IoUringQueueProbeResult result = IoUringQueue.TryCreateForProbe(2);
            return GetStatus(result);
        }

        internal static IoUringCapabilityStatus GetStatus(IoUringQueueProbeResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            return result.Status;
        }
    }
}
