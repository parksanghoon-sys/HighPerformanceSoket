using System;
using System.IO;
using System.Reflection;
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

        // help text 는 사용자가 runner 실행 전 확인하는 public CLI 계약이다.
        // parser 가 iouring 을 받아도 usage 가 saea|rio 만 노출하면 Linux artifact 수집 절차가 잘못된 옵션으로 문서화된다.
        [Fact]
        public void Main_WhenHelpRequested_PrintsIoUringBackendUsage()
        {
            TextWriter originalOut = Console.Out;
            using (StringWriter writer = new StringWriter())
            {
                try
                {
                    Console.SetOut(writer);

                    int exitCode = Program.Main(new[] { "--help" });

                    Assert.Equal(0, exitCode);
                    string usage = writer.ToString();
                    Assert.Contains("[--backend <saea|rio|iouring>]", usage);
                    Assert.Contains("--baseline-suite", usage);
                }
                finally
                {
                    Console.SetOut(originalOut);
                }
            }
        }

        // mixed workload는 legacy protocol runner와 다른 입력 집합을 사용하므로 help에 독립 command line을 노출해야 한다.
        // 이 줄이 없으면 parser가 지원하는 rate/duration/subscriber 경계를 사용자가 실행 전에 확인할 수 없다.
        [Fact]
        public void Main_WhenHelpRequested_PrintsMixedWorkloadUsage()
        {
            TextWriter originalOut = Console.Out;
            using (StringWriter writer = new StringWriter())
            {
                try
                {
                    Console.SetOut(writer);

                    int exitCode = Program.Main(new[] { "--help" });

                    Assert.Equal(0, exitCode);
                    Assert.Contains(
                        "Hps.Benchmarks --mixed-load-open-loop [--backend <saea|rio|iouring>] [--data-rate-hz <100+>] [--duration-seconds <1+>] [--subscribers <1..256>] [--report <path>]",
                        writer.ToString());
                }
                finally
                {
                    Console.SetOut(originalOut);
                }
            }
        }

        // TCP io_uring benchmark 는 기존 raw report schema 를 유지하되 scenario key 를 별도로 가져야 한다.
        // 이 key 가 SAEA 와 같으면 summary/history 단계에서 backend 별 성능 결과가 섞인다.
        [Fact]
        public void TcpBuildScenarioName_WhenIoUringSelected_UsesIoUringBaselineName()
        {
            MethodInfo? method = typeof(TcpLoopbackScenarioRunner).GetMethod(
                "BuildScenarioName",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            string scenario = Assert.IsType<string>(
                method!.Invoke(null, new object[] { TcpLoopbackTransportBackend.IoUring, "-smoke" }));

            Assert.Equal("tcp-loopback-iouring-baseline-smoke", scenario);
        }
    }
}
