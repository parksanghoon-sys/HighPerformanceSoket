using System;

namespace Hps.Server
{
    /// <summary>
    /// BrokerServer host 수명에 묶이는 선택 기능 설정이다.
    /// </summary>
    public sealed class BrokerServerOptions
    {
        private static readonly BrokerServerOptions DefaultInstance =
            new BrokerServerOptions(false, TimeSpan.Zero, TimeSpan.Zero, TimeProvider.System);

        private BrokerServerOptions(
            bool udpLeaseSweepEnabled,
            TimeSpan udpLeaseIdleTimeout,
            TimeSpan udpLeaseSweepInterval,
            TimeProvider timeProvider)
        {
            UdpLeaseSweepEnabled = udpLeaseSweepEnabled;
            UdpLeaseIdleTimeout = udpLeaseIdleTimeout;
            UdpLeaseSweepInterval = udpLeaseSweepInterval;
            TimeProvider = timeProvider;
        }

        /// <summary>
        /// 기존 BrokerServer 동작과 같은 기본 설정이다. UDP remote idle expiry 는 비활성화된다.
        /// </summary>
        public static BrokerServerOptions Default
        {
            get { return DefaultInstance; }
        }

        /// <summary>
        /// UDP remote lease sweep 을 host timer 로 실행할지 여부다.
        /// </summary>
        public bool UdpLeaseSweepEnabled { get; }

        /// <summary>
        /// UDP remote 가 이 시간보다 오래 activity 를 보내지 않으면 sweep 대상이 된다.
        /// </summary>
        public TimeSpan UdpLeaseIdleTimeout { get; }

        /// <summary>
        /// Server host 가 UDP lease sweep 을 시도하는 주기다.
        /// </summary>
        public TimeSpan UdpLeaseSweepInterval { get; }

        /// <summary>
        /// lease activity 와 host timer 에 사용할 시간 소스다.
        /// </summary>
        public TimeProvider TimeProvider { get; }

        /// <summary>
        /// UDP remote lease sweep 을 명시적으로 활성화하는 설정을 만든다.
        /// </summary>
        public static BrokerServerOptions CreateWithUdpLeaseSweep(
            TimeSpan idleTimeout,
            TimeSpan sweepInterval,
            TimeProvider? timeProvider)
        {
            if (idleTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(idleTimeout));
            if (sweepInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(sweepInterval));

            return new BrokerServerOptions(
                true,
                idleTimeout,
                sweepInterval,
                timeProvider ?? TimeProvider.System);
        }
    }
}
