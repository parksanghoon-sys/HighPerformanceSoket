using System;
using System.Runtime.InteropServices;
using Hps.Transport;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringCapabilityProbeTests
    {
        // 첫 Red는 production project 부재를 reflection assertion failure 로 잡는다.
        // compile failure 가 아니라 "io_uring capability probe type 이 아직 없다"는 요구사항 실패를 보여준다.
        [Fact]
        public void IoUringCapabilityProbe_TypeExists()
        {
            Type? type = Type.GetType("Hps.Transport.IoUringCapabilityProbe, Hps.Transport.IoUring");

            Assert.NotNull(type);
        }

        // io_uring backend 는 Linux 전용 opt-in 경로다.
        // Windows 개발 환경에서 사용할 수 있다고 오판하면 default backend promotion 판단이 흔들린다.
        [Fact]
        public void GetStatus_WhenNotLinux_ReturnsUnsupportedOperatingSystem()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            Type? probeType = Type.GetType("Hps.Transport.IoUringCapabilityProbe, Hps.Transport.IoUring");
            Type? statusType = Type.GetType("Hps.Transport.IoUringCapabilityStatus, Hps.Transport.IoUring");

            Assert.NotNull(probeType);
            Assert.NotNull(statusType);

            object? status = probeType!.GetMethod("GetStatus")!.Invoke(null, null);
            object expected = Enum.Parse(statusType!, "UnsupportedOperatingSystem");

            Assert.Equal(expected, status);
        }

        // Phase 6 skeleton 이 생겨도 기본 factory 는 SAEA 기준선을 유지해야 한다.
        // io_uring은 Linux native pump 와 TCP/UDP contract matrix 가 준비되기 전까지 opt-in backend 다.
        [Fact]
        public void CreateDefault_DuringIoUringBoundaryPhase_ReturnsSaeaTransport()
        {
            ITransport transport = TransportFactory.CreateDefault();

            Assert.IsType<SaeaTransport>(transport);
            transport.Dispose();
        }
    }
}
