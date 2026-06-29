using System;
using System.Runtime.InteropServices;
using Xunit;
using Xunit.Abstractions;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringCapabilityEvidenceTests
    {
        private readonly ITestOutputHelper _output;

        public IoUringCapabilityEvidenceTests(ITestOutputHelper output)
        {
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        // Linux contract workflow 의 TRX artifact 에 현재 host 의 io_uring capability 판정을 남긴다.
        // 이 테스트는 production 동작을 새로 요구하지 않고, 기존 probe 결과가 known status 로 수렴하는지와
        // 원격 실행 후 사람이 available/unavailable 상태를 구분할 수 있는 evidence 를 제공한다.
        [Fact]
        public void GetStatus_WritesCapabilityEvidenceForLinuxContractGate()
        {
            IoUringCapabilityStatus status = IoUringCapabilityProbe.GetStatus();

            _output.WriteLine("io_uring capability status: " + status);
            _output.WriteLine("os description: " + RuntimeInformation.OSDescription);
            _output.WriteLine("os architecture: " + RuntimeInformation.OSArchitecture);
            _output.WriteLine("process architecture: " + RuntimeInformation.ProcessArchitecture);

            Assert.True(
                status == IoUringCapabilityStatus.UnsupportedOperatingSystem ||
                status == IoUringCapabilityStatus.Unavailable ||
                status == IoUringCapabilityStatus.Available);
        }
    }
}
