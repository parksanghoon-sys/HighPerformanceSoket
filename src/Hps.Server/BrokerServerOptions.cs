using System;

namespace Hps.Server
{
    /// <summary>
    /// BrokerServer host 수명에 묶이는 선택 기능 설정이다.
    /// </summary>
    public sealed class BrokerServerOptions
    {
        private static readonly BrokerServerOptions DefaultInstance =
            new BrokerServerOptions(false, TimeSpan.Zero, TimeSpan.Zero, false, TimeSpan.Zero, TimeProvider.System);

        private BrokerServerOptions(
            bool udpLeaseSweepEnabled,
            TimeSpan udpLeaseIdleTimeout,
            TimeSpan udpLeaseSweepInterval,
            bool stableSubscriberIdentityEnabled,
            TimeSpan stableSubscriberRetentionTimeout,
            TimeProvider timeProvider)
        {
            UdpLeaseSweepEnabled = udpLeaseSweepEnabled;
            UdpLeaseIdleTimeout = udpLeaseIdleTimeout;
            UdpLeaseSweepInterval = udpLeaseSweepInterval;
            StableSubscriberIdentityEnabled = stableSubscriberIdentityEnabled;
            StableSubscriberRetentionTimeout = stableSubscriberRetentionTimeout;
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
        /// REGISTER 기반 stable subscriber identity registry 를 사용할지 여부다.
        /// </summary>
        public bool StableSubscriberIdentityEnabled { get; }

        /// <summary>
        /// disconnected stable identity 의 topic metadata 를 보존할 최대 시간이다.
        /// </summary>
        public TimeSpan StableSubscriberRetentionTimeout { get; }

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
                false,
                TimeSpan.Zero,
                timeProvider ?? TimeProvider.System);
        }

        /// <summary>
        /// stable subscriber identity 를 명시적으로 활성화하는 설정을 만든다.
        /// UDP lease sweep 은 이 factory 에서 켜지지 않으므로, 두 기능을 함께 쓰려면
        /// <see cref="WithStableSubscriberIdentity(TimeSpan)"/> 를 기존 options 에 적용한다.
        /// </summary>
        public static BrokerServerOptions CreateWithStableSubscriberIdentity(
            TimeSpan retentionTimeout,
            TimeProvider? timeProvider)
        {
            if (retentionTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(retentionTimeout));

            return new BrokerServerOptions(
                false,
                TimeSpan.Zero,
                TimeSpan.Zero,
                true,
                retentionTimeout,
                timeProvider ?? TimeProvider.System);
        }

        /// <summary>
        /// 현재 options 의 다른 host 설정은 유지하면서 stable subscriber identity 만 활성화한다.
        /// </summary>
        public BrokerServerOptions WithStableSubscriberIdentity(TimeSpan retentionTimeout)
        {
            if (retentionTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(retentionTimeout));

            return new BrokerServerOptions(
                UdpLeaseSweepEnabled,
                UdpLeaseIdleTimeout,
                UdpLeaseSweepInterval,
                true,
                retentionTimeout,
                TimeProvider);
        }
    }
}
