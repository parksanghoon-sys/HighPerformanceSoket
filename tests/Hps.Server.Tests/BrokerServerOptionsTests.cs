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

        private sealed class ManualTimeProvider : TimeProvider
        {
        }
    }
}
