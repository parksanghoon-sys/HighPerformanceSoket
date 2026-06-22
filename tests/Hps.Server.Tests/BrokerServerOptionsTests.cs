using System;
using Xunit;

namespace Hps.Server.Tests
{
    public sealed class BrokerServerOptionsTests
    {
        // 기본 options 테스트는 기존 BrokerServer 생성자가 UDP idle expiry 를 켜지 않는다는 v1 호환성 계약을 고정한다.
        // 기본값이 sweep 을 만들면 운영자가 명시하지 않은 cleanup 이 발생해 기존 UDP subscriber 수명 의미가 바뀐다.
        [Fact]
        public void Default_WhenRead_DisablesUdpLeaseSweep()
        {
            BrokerServerOptions options = BrokerServerOptions.Default;

            Assert.False(options.UdpLeaseSweepEnabled);
            Assert.Equal(TimeSpan.Zero, options.UdpLeaseIdleTimeout);
            Assert.Equal(TimeSpan.Zero, options.UdpLeaseSweepInterval);
        }

        // enabled factory 검증은 idle timeout 과 sweep interval 이 모두 양수여야 한다는 timer 안전 조건을 고정한다.
        // 0 이하 값은 즉시 만료나 busy-loop timer 로 이어질 수 있으므로 server public boundary 에서 막아야 한다.
        [Fact]
        public void CreateWithUdpLeaseSweep_WhenNonPositiveIntervalsAreUsed_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                delegate { BrokerServerOptions.CreateWithUdpLeaseSweep(TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeProvider.System); });
            Assert.Throws<ArgumentOutOfRangeException>(
                delegate { BrokerServerOptions.CreateWithUdpLeaseSweep(TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeProvider.System); });
        }

        // enabled factory 저장 테스트는 운영자가 명시한 값만 host timer 에 전달되는지 검증한다.
        // 이번 설계에서는 임의 기본 timeout 을 정하지 않으므로 활성화 경로는 항상 explicit 값과 TimeProvider 를 보존해야 한다.
        [Fact]
        public void CreateWithUdpLeaseSweep_WhenValuesAreValid_StoresExplicitValuesAndTimeProvider()
        {
            ManualTimeProvider timeProvider = new ManualTimeProvider();

            BrokerServerOptions options = BrokerServerOptions.CreateWithUdpLeaseSweep(
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(5),
                timeProvider);

            Assert.True(options.UdpLeaseSweepEnabled);
            Assert.Equal(TimeSpan.FromSeconds(30), options.UdpLeaseIdleTimeout);
            Assert.Equal(TimeSpan.FromSeconds(5), options.UdpLeaseSweepInterval);
            Assert.Same(timeProvider, options.TimeProvider);
        }

        // stable identity 기본값 테스트는 기존 runtime target 기반 동작이 opt-in 없이 바뀌지 않는지 보호한다.
        // 기본값에서 retention timer 가 켜지면 reconnect 정책을 쓰지 않는 기존 subscriber 수명 경계가 달라질 수 있다.
        [Fact]
        public void Default_WhenRead_DisablesStableSubscriberIdentity()
        {
            BrokerServerOptions options = BrokerServerOptions.Default;

            Assert.False(options.StableSubscriberIdentityEnabled);
            Assert.Equal(TimeSpan.Zero, options.StableSubscriberRetentionTimeout);
        }

        // retention timeout 검증 테스트는 disconnected identity sweep timer 의 busy-loop 와 즉시 만료를 public 경계에서 막는다.
        // 0 이하 값은 registry 에 남긴 topic metadata 를 보존할 실제 시간을 표현하지 못하므로 명시적으로 거부해야 한다.
        [Fact]
        public void CreateWithStableSubscriberIdentity_WhenRetentionIsNonPositive_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                delegate { BrokerServerOptions.CreateWithStableSubscriberIdentity(TimeSpan.Zero, TimeProvider.System); });
            Assert.Throws<ArgumentOutOfRangeException>(
                delegate { BrokerServerOptions.CreateWithStableSubscriberIdentity(TimeSpan.FromSeconds(-1), TimeProvider.System); });
        }

        // stable identity factory 테스트는 명시한 retention 값과 TimeProvider 가 Server timer 로 그대로 전달되는지 확인한다.
        // 이 값이 손실되면 테스트나 운영에서 reconnect metadata 보존 시간을 재현할 수 없다.
        [Fact]
        public void CreateWithStableSubscriberIdentity_WhenValuesAreValid_StoresExplicitValuesAndTimeProvider()
        {
            ManualTimeProvider timeProvider = new ManualTimeProvider();

            BrokerServerOptions options = BrokerServerOptions.CreateWithStableSubscriberIdentity(
                TimeSpan.FromMinutes(5),
                timeProvider);

            Assert.True(options.StableSubscriberIdentityEnabled);
            Assert.Equal(TimeSpan.FromMinutes(5), options.StableSubscriberRetentionTimeout);
            Assert.False(options.UdpLeaseSweepEnabled);
            Assert.Same(timeProvider, options.TimeProvider);
        }

        // option 조합 테스트는 UDP lease sweep 설정과 stable identity 설정을 같은 Server host 에서 함께 쓸 수 있는지 검증한다.
        // WithStableSubscriberIdentity 는 기존 UDP 설정을 잃지 않고 stable identity 만 추가해야 한다.
        [Fact]
        public void WithStableSubscriberIdentity_WhenCalled_PreservesUdpLeaseSettingsAndTimeProvider()
        {
            ManualTimeProvider timeProvider = new ManualTimeProvider();
            BrokerServerOptions udpOptions = BrokerServerOptions.CreateWithUdpLeaseSweep(
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(5),
                timeProvider);

            BrokerServerOptions options = udpOptions.WithStableSubscriberIdentity(TimeSpan.FromMinutes(10));

            Assert.True(options.UdpLeaseSweepEnabled);
            Assert.Equal(TimeSpan.FromSeconds(30), options.UdpLeaseIdleTimeout);
            Assert.Equal(TimeSpan.FromSeconds(5), options.UdpLeaseSweepInterval);
            Assert.True(options.StableSubscriberIdentityEnabled);
            Assert.Equal(TimeSpan.FromMinutes(10), options.StableSubscriberRetentionTimeout);
            Assert.Same(timeProvider, options.TimeProvider);
        }

        private sealed class ManualTimeProvider : TimeProvider
        {
        }
    }
}
