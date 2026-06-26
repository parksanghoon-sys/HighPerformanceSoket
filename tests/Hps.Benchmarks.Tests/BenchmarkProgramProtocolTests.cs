using System.IO;
using System.Text.Json;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BenchmarkProgramProtocolTests
    {
        // UDP smoke 는 D112 artifact 체인의 첫 실제 실행 경로다.
        // Program 이 --protocol udp 를 TCP runner 로 흘리거나 report schema identity 를 TCP 값으로 쓰면
        // RIO UDP readiness 판단에 잘못된 baseline 이 섞이므로 raw report 의 protocol/profile/scenario 를 함께 고정한다.
        [Fact]
        public void Main_WhenUdpSmokeProtocolSelected_WritesUdpSmokeReport()
        {
            string directory = Path.Combine(Path.GetTempPath(), "hps-benchmark-program-protocol-tests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            string reportPath = Path.Combine(directory, "udp-smoke.json");

            int exitCode = Program.Main(new[] { "--smoke", "--protocol", "udp", "--backend", "saea", "--report", reportPath });

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(reportPath));
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(reportPath)))
            {
                JsonElement root = document.RootElement;
                Assert.Equal("smoke", root.GetProperty("result-name").GetString());
                Assert.Equal("udp-loopback-saea-baseline-smoke", root.GetProperty("scenario").GetString());
                Assert.Equal("udp-loopback-saea-v1", root.GetProperty("benchmark-profile").GetString());
                Assert.Equal("SaeaTransport", root.GetProperty("transport-backend").GetString());
                Assert.True(root.GetProperty("passed").GetBoolean());
                Assert.Equal(8, root.GetProperty("sent").GetInt32());
                Assert.Equal(8, root.GetProperty("received").GetInt32());
                Assert.Equal(0, root.GetProperty("dropped").GetInt64());
                Assert.Equal(0, root.GetProperty("payload-errors").GetInt32());
                Assert.Equal(0, root.GetProperty("pool-rented").GetInt32());
            }
        }
    }
}
