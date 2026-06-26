using System.IO;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BenchmarkProgramProtocolTests
    {
        // parser 가 --protocol udp 를 인식한 뒤 Program 이 그 값을 무시하면 TCP smoke report 가 UDP evidence 로 잘못 저장된다.
        // UDP runner 가 붙기 전까지는 report 를 쓰지 않고 명시적으로 실패해 잘못된 artifact 생성을 막아야 한다.
        [Fact]
        public void Main_WhenUdpProtocolRunnerIsNotYetImplemented_ReturnsFailureWithoutWritingReport()
        {
            string directory = Path.Combine(Path.GetTempPath(), "hps-benchmark-program-protocol-tests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            string reportPath = Path.Combine(directory, "udp-smoke.json");

            int exitCode = Program.Main(new[] { "--smoke", "--protocol", "udp", "--report", reportPath });

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(reportPath));
        }
    }
}
