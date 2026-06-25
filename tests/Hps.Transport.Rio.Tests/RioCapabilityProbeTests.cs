using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Hps.Transport;
using Xunit;

namespace Hps.Transport.Rio.Tests
{
    public sealed class RioCapabilityProbeTests
    {
        // RIO backend는 Windows 전용 opt-in 경로다.
        // 이 테스트는 비 Windows 환경에서 RIO를 사용할 수 있다고 오판하지 않게 막는다.
        [Fact]
        public void GetStatus_WhenNotWindows_ReturnsUnsupportedOperatingSystem()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            Assert.Equal(RioCapabilityStatus.UnsupportedOperatingSystem, RioCapabilityProbe.GetStatus());
        }

        // 기본 factory는 Phase 5 초기에 SAEA를 유지해야 한다.
        // RIO가 일부 구현됐더라도 TCP/UDP parity 전까지 default backend를 바꾸면 기존 통합 경로가 흔들린다.
        [Fact]
        public void CreateDefault_DuringRioOptInPhase_ReturnsSaeaTransport()
        {
            ITransport transport = TransportFactory.CreateDefault();

            Assert.IsType<SaeaTransport>(transport);
            transport.Dispose();
        }

        // Windows에서 RIO function table load 결과는 Available 또는 Unavailable로 수렴해야 한다.
        // 예외가 escape하면 factory probe가 fallback 대신 process failure를 일으킬 수 있다.
        [Fact]
        public void GetStatus_WhenWindows_DoesNotThrow()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            RioCapabilityStatus status = RioCapabilityProbe.GetStatus();

            Assert.True(status == RioCapabilityStatus.Available || status == RioCapabilityStatus.Unavailable);
        }

        // native loader 자체도 fallback 가능한 bool 결과로 수렴해야 한다.
        // 호출자가 SocketException 같은 native 실패를 직접 처리하지 않게 하는 방어선이다.
        [Fact]
        public void TryLoadFunctionTable_DoesNotThrow()
        {
            RioNative? native;

            bool loaded = RioNative.TryLoadFunctionTable(out native);

            Assert.True(loaded || native == null);
        }

        // skeleton transport는 아직 opt-in construction만 허용한다.
        // StartAsync가 예외 없이 끝나면 후속 task가 같은 root type 위에 queue/resource를 붙일 수 있다.
        [Fact]
        public async Task RioTransport_WhenConstructed_StartStopDoesNotThrow()
        {
            using (ITransport transport = new RioTransport())
            {
                await transport.StartAsync();
                await transport.StopAsync();
            }
        }
    }
}
