using System;
using System.Net.Sockets;
using Hps.Transport;

namespace Hps.Sample.BrokerServer
{
    /// <summary>
    /// sample host composition 경계에서 concrete transport를 선택한다.
    /// capability probe와 factory를 주입받아 tests가 실제 OS/RIO/io_uring availability와 무관하게 선택 정책을 검증할 수 있게 한다.
    /// </summary>
    public static class SampleTransportSelector
    {
        public const int RuntimeFailureExitCode = 1;

        /// <summary>
        /// 명시 mode와 listen address family, 각 backend capability probe 및 factory를 기준으로 concrete transport를 선택한다.
        /// Saea는 두 capability probe를 모두 건너뛰고, explicit io_uring은 RIO probe를 건너뛰며 available일 때만 io_uring factory를 호출한다.
        /// explicit io_uring capability failure는 SAEA fallback 없이 runtime failure를 반환하고, RIO와 auto의 기존 IPv4 guard 및 fallback 정책은 그대로 유지한다.
        /// </summary>
        public static SampleTransportSelection Select(
            SampleTransportMode mode,
            AddressFamily listenAddressFamily,
            Func<RioCapabilityStatus> getRioStatus,
            Func<IoUringCapabilityStatus> getIoUringStatus,
            Func<ITransport> createSaea,
            Func<ITransport> createRio,
            Func<ITransport> createIoUring)
        {
            if (getRioStatus == null)
                throw new ArgumentNullException(nameof(getRioStatus));
            if (getIoUringStatus == null)
                throw new ArgumentNullException(nameof(getIoUringStatus));
            if (createSaea == null)
                throw new ArgumentNullException(nameof(createSaea));
            if (createRio == null)
                throw new ArgumentNullException(nameof(createRio));
            if (createIoUring == null)
                throw new ArgumentNullException(nameof(createIoUring));

            if (mode != SampleTransportMode.Saea &&
                mode != SampleTransportMode.Rio &&
                mode != SampleTransportMode.Auto &&
                mode != SampleTransportMode.IoUring)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            if (mode == SampleTransportMode.Saea)
                return SampleTransportSelection.Success(createSaea(), "SaeaTransport", null);

            if (mode == SampleTransportMode.IoUring)
            {
                IoUringCapabilityStatus ioUringStatus = getIoUringStatus();
                if (ioUringStatus == IoUringCapabilityStatus.Available)
                    return SampleTransportSelection.Success(createIoUring(), "IoUringTransport", null);

                if (ioUringStatus == IoUringCapabilityStatus.UnsupportedOperatingSystem)
                {
                    return SampleTransportSelection.Failure(
                        "io_uring transport는 Linux에서만 사용할 수 있습니다. status=" + ioUringStatus,
                        RuntimeFailureExitCode);
                }

                return SampleTransportSelection.Failure(
                    "io_uring transport를 사용할 수 없습니다. status=" + ioUringStatus,
                    RuntimeFailureExitCode);
            }

            bool rioCanListenOnAddressFamily = listenAddressFamily == AddressFamily.InterNetwork;
            if (!rioCanListenOnAddressFamily)
            {
                if (mode == SampleTransportMode.Rio)
                {
                    return SampleTransportSelection.Failure(
                        "RIO transport는 현재 IPv4 listen endpoint만 지원합니다. address-family=" + listenAddressFamily,
                        RuntimeFailureExitCode);
                }

                return SampleTransportSelection.Success(
                    createSaea(),
                    "SaeaTransport",
                    "RIO IPv4-only backend는 IPv6/non-IPv4 listen endpoint를 사용할 수 없어 SaeaTransport로 fallback 합니다. address-family=" +
                    listenAddressFamily);
            }

            RioCapabilityStatus rioStatus = getRioStatus();
            if (mode == SampleTransportMode.Rio)
            {
                if (rioStatus == RioCapabilityStatus.Available)
                    return SampleTransportSelection.Success(createRio(), "RioTransport", null);

                return SampleTransportSelection.Failure(
                    "RIO transport를 사용할 수 없습니다. status=" + rioStatus,
                    RuntimeFailureExitCode);
            }

            if (rioStatus == RioCapabilityStatus.Available)
                return SampleTransportSelection.Success(createRio(), "RioTransport", null);

            return SampleTransportSelection.Success(
                createSaea(),
                "SaeaTransport",
                "RIO unavailable; falling back to SaeaTransport. status=" + rioStatus);
        }
    }
}
