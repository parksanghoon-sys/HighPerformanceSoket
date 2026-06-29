using System;
using System.Net.Sockets;
using Hps.Transport;

namespace Hps.Sample.BrokerServer
{
    /// <summary>
    /// sample host composition 경계에서 concrete transport 를 선택한다.
    /// capability probe 와 factory 를 주입받아 tests 가 실제 OS/RIO availability 에 묶이지 않게 한다.
    /// </summary>
    public static class SampleTransportSelector
    {
        public const int RuntimeFailureExitCode = 1;

        /// <summary>
        /// 기존 호출 경로와 테스트 호환성을 위해 IPv4 listen endpoint 를 전제로 transport 를 선택한다.
        /// 실제 host 는 parsed address family 를 아는 overload 를 호출해 RIO IPv4-only 제약을 먼저 반영한다.
        /// </summary>
        public static SampleTransportSelection Select(
            SampleTransportMode mode,
            Func<RioCapabilityStatus> getRioStatus,
            Func<ITransport> createSaea,
            Func<ITransport> createRio)
        {
            return Select(mode, AddressFamily.InterNetwork, getRioStatus, createSaea, createRio);
        }

        /// <summary>
        /// sample host 의 listen address family 와 RIO capability 를 함께 고려해 concrete transport 를 선택한다.
        /// RIO backend 는 현재 IPv4 전용이므로 auto mode 는 non-IPv4 endpoint 에서 SAEA 로 fallback 하고,
        /// explicit rio mode 는 fallback 없이 runtime failure 를 반환한다.
        /// </summary>
        public static SampleTransportSelection Select(
            SampleTransportMode mode,
            AddressFamily listenAddressFamily,
            Func<RioCapabilityStatus> getRioStatus,
            Func<ITransport> createSaea,
            Func<ITransport> createRio)
        {
            if (getRioStatus == null)
                throw new ArgumentNullException(nameof(getRioStatus));
            if (createSaea == null)
                throw new ArgumentNullException(nameof(createSaea));
            if (createRio == null)
                throw new ArgumentNullException(nameof(createRio));

            if (mode != SampleTransportMode.Saea &&
                mode != SampleTransportMode.Rio &&
                mode != SampleTransportMode.Auto)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            if (mode == SampleTransportMode.Saea)
                return SampleTransportSelection.Success(createSaea(), "SaeaTransport", null);

            bool rioCanListenOnAddressFamily = listenAddressFamily == AddressFamily.InterNetwork;
            if (!rioCanListenOnAddressFamily)
            {
                if (mode == SampleTransportMode.Rio)
                {
                    return SampleTransportSelection.Failure(
                        "RIO transport는 현재 IPv4 listen endpoint 만 지원합니다. address-family=" + listenAddressFamily,
                        RuntimeFailureExitCode);
                }

                return SampleTransportSelection.Success(
                    createSaea(),
                    "SaeaTransport",
                    "RIO IPv4-only backend 는 IPv6/non-IPv4 listen endpoint 를 사용할 수 없어 SaeaTransport 로 fallback 합니다. address-family=" +
                    listenAddressFamily);
            }

            RioCapabilityStatus status = getRioStatus();
            if (mode == SampleTransportMode.Rio)
            {
                if (status == RioCapabilityStatus.Available)
                    return SampleTransportSelection.Success(createRio(), "RioTransport", null);

                return SampleTransportSelection.Failure(
                    "RIO transport를 사용할 수 없습니다. status=" + status,
                    RuntimeFailureExitCode);
            }

            if (status == RioCapabilityStatus.Available)
                return SampleTransportSelection.Success(createRio(), "RioTransport", null);

            return SampleTransportSelection.Success(
                createSaea(),
                "SaeaTransport",
                "RIO unavailable; falling back to SaeaTransport. status=" + status);
        }
    }
}
