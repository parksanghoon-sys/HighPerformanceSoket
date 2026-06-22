using System;

namespace Hps.Broker
{
    /// <summary>
    /// UDP remote lease cleanup 의 내부 설정값이다.
    ///
    /// 이 타입은 BrokerServer 운영자용 public 설정 표면이 아니다. D073에 따라 기본값은 비활성이며,
    /// 구현 검증과 이후 host timer wiring 에서만 명시적으로 활성 옵션을 주입한다.
    /// </summary>
    internal sealed class UdpLeaseOptions
    {
        private static readonly UdpLeaseOptions DisabledInstance = new UdpLeaseOptions(false, TimeSpan.Zero, TimeSpan.Zero);

        private UdpLeaseOptions(bool enabled, TimeSpan idleTimeout, TimeSpan sweepInterval)
        {
            Enabled = enabled;
            IdleTimeout = idleTimeout;
            SweepInterval = sweepInterval;
        }

        internal static UdpLeaseOptions Disabled
        {
            get { return DisabledInstance; }
        }

        internal bool Enabled { get; }

        internal TimeSpan IdleTimeout { get; }

        internal TimeSpan SweepInterval { get; }

        internal static UdpLeaseOptions CreateEnabled(TimeSpan idleTimeout, TimeSpan sweepInterval)
        {
            if (idleTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(idleTimeout));
            if (sweepInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(sweepInterval));

            return new UdpLeaseOptions(true, idleTimeout, sweepInterval);
        }
    }
}
