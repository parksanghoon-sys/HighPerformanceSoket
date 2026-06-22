using System;
using Xunit;

namespace Hps.Broker.Tests
{
    public sealed class UdpLeaseOptionsTests
    {
        // 기본 옵션 테스트: D073은 idle expiry 기본값을 비활성으로 고정했다.
        // disabled 상태에서는 lease 갱신과 sweep 이 모두 건너뛰어 기존 UDP broker 동작이 유지되어야 한다.
        [Fact]
        public void Disabled_WhenRead_ReturnsDisabledZeroIntervals()
        {
            UdpLeaseOptions options = UdpLeaseOptions.Disabled;

            Assert.False(options.Enabled);
            Assert.Equal(TimeSpan.Zero, options.IdleTimeout);
            Assert.Equal(TimeSpan.Zero, options.SweepInterval);
        }

        // enabled 옵션 검증 테스트: idle timeout 과 sweep interval 은 시간 비교의 기준이므로
        // 0 이하 값을 허용하면 remote 가 즉시 만료되거나 timer 가 busy loop 로 흐를 수 있다.
        [Fact]
        public void Enabled_WhenNonPositiveIntervalsAreUsed_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                delegate { UdpLeaseOptions.CreateEnabled(TimeSpan.Zero, TimeSpan.FromSeconds(1)); });
            Assert.Throws<ArgumentOutOfRangeException>(
                delegate { UdpLeaseOptions.CreateEnabled(TimeSpan.FromSeconds(1), TimeSpan.Zero); });
        }

        // enabled 옵션 생성 테스트: 이번 단계는 운영자용 public 설정을 열지 않고 내부 options 값만 확정한다.
        // 이후 handler/tracker 는 이 값을 주입받아 기본 비활성 경로와 선택 활성 경로를 분리한다.
        [Fact]
        public void Enabled_WhenPositiveIntervalsAreUsed_StoresValues()
        {
            UdpLeaseOptions options = UdpLeaseOptions.CreateEnabled(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

            Assert.True(options.Enabled);
            Assert.Equal(TimeSpan.FromSeconds(30), options.IdleTimeout);
            Assert.Equal(TimeSpan.FromSeconds(5), options.SweepInterval);
        }
    }
}
