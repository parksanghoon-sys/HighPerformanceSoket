using System;
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

        public static SampleTransportSelection Select(
            SampleTransportMode mode,
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
