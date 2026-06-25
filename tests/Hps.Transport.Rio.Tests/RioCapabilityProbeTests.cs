using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Hps.Transport;
using Xunit;

namespace Hps.Transport.Rio.Tests
{
    public sealed class RioCapabilityProbeTests
    {
        // 첫 Red는 production project 부재를 reflection assertion failure로 잡는다.
        // 컴파일 실패가 아니라 "RIO capability probe type이 아직 없다"는 요구사항 실패를 보여준다.
        [Fact]
        public void RioCapabilityProbe_TypeExists()
        {
            Type? type = Type.GetType("Hps.Transport.RioCapabilityProbe, Hps.Transport.Rio");

            Assert.NotNull(type);
        }

        // RIO backend는 Windows 전용 opt-in 경로다.
        // 이 테스트는 비 Windows 환경에서 RIO를 사용할 수 있다고 오판하지 않게 막는다.
        [Fact]
        public void GetStatus_WhenNotWindows_ReturnsUnsupportedOperatingSystem()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            Type? probeType = Type.GetType("Hps.Transport.RioCapabilityProbe, Hps.Transport.Rio");
            Type? statusType = Type.GetType("Hps.Transport.RioCapabilityStatus, Hps.Transport.Rio");
            if (probeType == null || statusType == null)
                return;

            object? status = probeType.GetMethod("GetStatus")!.Invoke(null, null);
            object expected = Enum.Parse(statusType, "UnsupportedOperatingSystem");

            Assert.Equal(expected, status);
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

        // skeleton transport는 아직 opt-in construction만 허용한다.
        // StartAsync가 예외 없이 끝나면 후속 task가 같은 root type 위에 queue/resource를 붙일 수 있다.
        [Fact]
        public async Task RioTransport_WhenConstructed_StartStopDoesNotThrow()
        {
            Type? transportType = Type.GetType("Hps.Transport.RioTransport, Hps.Transport.Rio");
            if (transportType == null)
                return;

            using (ITransport transport = (ITransport)Activator.CreateInstance(transportType)!)
            {
                await transport.StartAsync();
                await transport.StopAsync();
            }
        }
    }
}
