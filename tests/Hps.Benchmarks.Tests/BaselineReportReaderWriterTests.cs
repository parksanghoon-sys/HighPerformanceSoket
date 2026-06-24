using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineReportReaderWriterTests
    {
        // reader 는 per-run schema v1 JSON만 summary 입력으로 삼고, summary.json 같은 다른 artifact 는 건너뛴다.
        // 이 경계가 없으면 summary command 를 같은 directory 에 반복 실행할 때 이전 summary 를 run 으로 잘못 집계할 수 있다.
        [Fact]
        public void ReadDirectory_WhenDirectoryHasRunReportsAndSummary_ReadsOnlyRunReports()
        {
            string directory = CreateTempDirectory();
            WriteRunJson(Path.Combine(directory, "load-01.json"), "load", 500.0, 1, 0, 3000);
            WriteRunJson(Path.Combine(directory, "open-loop-01.json"), "open-loop", 600.0, 2, 0, 3000);
            File.WriteAllText(Path.Combine(directory, "summary.json"), "{ \"summary-version\": 1 }");

            BaselineReport[] reports = BaselineReportReader.ReadDirectory(directory).ToArray();

            Assert.Equal(2, reports.Length);
            Assert.Contains(reports, report => report.ResultName == "load");
            Assert.Contains(reports, report => report.ResultName == "open-loop");
        }

        // writer 는 자동화가 읽을 수 있는 안정적인 key 집합을 만든다.
        // summary-version 과 by-kind 구조가 없으면 CI artifact 소비자가 schema 를 식별할 수 없다.
        [Fact]
        public void Write_WhenSummaryHasWarnings_WritesStableJsonShape()
        {
            string directory = CreateTempDirectory();
            string path = Path.Combine(directory, "summary.json");
            BaselineReport[] reports =
            {
                new BaselineReport(
                    "open-loop-01.json",
                    "open-loop",
                    "scenario",
                    4096,
                    100.0,
                    30,
                    3000,
                    3000,
                    3000,
                    0,
                    0,
                    0,
                    94.0,
                    240.0,
                    1600.0,
                    2.1,
                    8,
                    0)
            };
            BaselineSummary summary = BaselineSummaryGenerator.Generate(directory, reports);

            BaselineSummaryWriter.Write(path, summary);

            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement root = document.RootElement;
                Assert.Equal(1, root.GetProperty("summary-version").GetInt32());
                Assert.True(root.GetProperty("hard-passed").GetBoolean());
                Assert.True(root.GetProperty("warning-count").GetInt32() >= 1);
                Assert.True(root.GetProperty("by-kind").TryGetProperty("open-loop", out JsonElement openLoop));
                Assert.Equal(1, openLoop.GetProperty("run-count").GetInt32());
                Assert.Equal(1600.0, openLoop.GetProperty("p99-median-us").GetDouble());
                Assert.True(root.GetProperty("warnings").GetArrayLength() >= 1);
                Assert.Equal("open-loop-01.json", root.GetProperty("warnings")[0].GetProperty("source-path").GetString());
            }
        }

        // summary JSON 은 downstream script 가 읽는 canonical artifact 다.
        // comparison field 가 JSON 에 없으면 사람이 Markdown 을 봐야만 비교 가능성 문제를 알 수 있다.
        [Fact]
        public void Write_WhenSummaryHasComparison_WritesComparisonFields()
        {
            string directory = CreateTempDirectory();
            string path = Path.Combine(directory, "summary.json");
            BaselineReport[] reports =
            {
                new BaselineReport(
                    "load-01.json",
                    "load",
                    "tcp-loopback-saea-baseline",
                    4096,
                    100.0,
                    30,
                    3000,
                    3000,
                    3000,
                    0,
                    0,
                    0,
                    100.0,
                    240.0,
                    500.0,
                    1.0,
                    1,
                    0,
                    CreateIdentity("runner-a"))
            };
            BaselineSummary summary = BaselineSummaryGenerator.Generate(directory, reports);

            BaselineSummaryWriter.Write(path, summary);

            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement root = document.RootElement;
                Assert.True(root.GetProperty("comparison-compatible").GetBoolean());
                Assert.Equal(0, root.GetProperty("unknown-runner-count").GetInt32());
                Assert.Equal(0, root.GetProperty("comparison-mismatch-count").GetInt32());
                Assert.Equal(0, root.GetProperty("comparison-mismatches").GetArrayLength());

                JsonElement key = root.GetProperty("comparison-key");
                Assert.Equal("runner-a", key.GetProperty("runner-id").GetString());
                Assert.Equal(BenchmarkRunIdentity.DefaultBenchmarkProfile, key.GetProperty("benchmark-profile").GetString());
                JsonElement runCase = key.GetProperty("cases")[0];
                Assert.Equal("load", runCase.GetProperty("result-name").GetString());
                Assert.Equal("tcp-loopback-saea-baseline", runCase.GetProperty("scenario").GetString());
                Assert.Equal(4096, runCase.GetProperty("payload-bytes").GetInt32());
                Assert.Equal(100.0, runCase.GetProperty("target-rate-hz").GetDouble());
                Assert.Equal(30, runCase.GetProperty("target-duration-seconds").GetInt32());
            }
        }

        // raw report 가 runner identity 를 원천 artifact 로 남겨야 summary/history 단계가 비교 가능성을 판단할 수 있다.
        // schema-version 은 그대로 1이고 metadata 는 기존 reader 를 깨지 않는 top-level additive field 로만 추가한다.
        [Fact]
        public void Write_WhenRunResultIsWritten_IncludesRunnerIdentityMetadata()
        {
            string directory = CreateTempDirectory();
            string path = Path.Combine(directory, "load-01.json");
            TcpLoopbackRunResult result = new TcpLoopbackRunResult(
                "load",
                "tcp-loopback-saea-baseline",
                4096,
                100,
                30,
                3000,
                3000,
                3000,
                0,
                2,
                0,
                0,
                0,
                240.0,
                500.0,
                450.0,
                550.0,
                30000);

            TcpLoopbackReportWriter.Write(path, result);

            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement root = document.RootElement;
                JsonElement benchmarkProfile;
                JsonElement runnerId;
                JsonElement runnerKind;
                JsonElement transportBackend;
                JsonElement osDescription;
                JsonElement frameworkDescription;
                JsonElement processorCount;
                Assert.Equal(1, root.GetProperty("schema-version").GetInt32());
                Assert.True(root.TryGetProperty("benchmark-profile", out benchmarkProfile));
                Assert.True(root.TryGetProperty("runner-id", out runnerId));
                Assert.True(root.TryGetProperty("runner-kind", out runnerKind));
                Assert.True(root.TryGetProperty("transport-backend", out transportBackend));
                Assert.True(root.TryGetProperty("os-description", out osDescription));
                Assert.True(root.TryGetProperty("framework-description", out frameworkDescription));
                Assert.True(root.TryGetProperty("processor-count", out processorCount));
                Assert.Equal(BenchmarkRunIdentity.DefaultBenchmarkProfile, benchmarkProfile.GetString());
                Assert.False(string.IsNullOrWhiteSpace(runnerId.GetString()));
                Assert.False(string.IsNullOrWhiteSpace(runnerKind.GetString()));
                Assert.Equal(BenchmarkRunIdentity.DefaultTransportBackend, transportBackend.GetString());
                Assert.False(string.IsNullOrWhiteSpace(osDescription.GetString()));
                Assert.False(string.IsNullOrWhiteSpace(frameworkDescription.GetString()));
                Assert.True(processorCount.GetInt32() > 0);
            }
        }

        // summary/history 단계는 raw report reader 를 통해서만 runner identity 를 볼 수 있다.
        // BaselineReport 에 identity property 가 없으면 이후 comparison signal 이 runner 차이를 판단할 수 없다.
        [Fact]
        public void Contract_BaselineReportExposesIdentity()
        {
            System.Reflection.PropertyInfo? property = typeof(BaselineReport).GetProperty("Identity");

            Assert.NotNull(property);
            Assert.Equal(typeof(BenchmarkRunIdentity), property!.PropertyType);
        }

        // comparison key 는 payload size 와 target rate/duration 없이는 같은 부하 조건인지 판단할 수 없다.
        // BaselineReport 가 이 값을 노출해야 summary 단계가 raw JSON을 다시 열지 않고 comparison signal 을 계산할 수 있다.
        [Fact]
        public void Contract_BaselineReportExposesPayloadAndTargetSettings()
        {
            Assert.NotNull(typeof(BaselineReport).GetProperty("PayloadBytes"));
            Assert.NotNull(typeof(BaselineReport).GetProperty("TargetRateHz"));
            Assert.NotNull(typeof(BaselineReport).GetProperty("TargetDurationSeconds"));
        }

        // reader 는 신규 raw report 의 runner metadata 를 보존해야 한다.
        // 이 값이 사라지면 summary/history 단계에서 서로 다른 runner 의 baseline 을 같은 비교군으로 착각할 수 있다.
        [Fact]
        public void ReadDirectory_WhenRunReportHasRunnerIdentity_ReadsIdentityMetadata()
        {
            string directory = CreateTempDirectory();
            WriteRunJsonWithIdentity(Path.Combine(directory, "load-01.json"), "load", "dev-box-a", "self-hosted");

            BaselineReport report = BaselineReportReader.ReadDirectory(directory).Single();

            Assert.Equal(BenchmarkRunIdentity.DefaultBenchmarkProfile, report.Identity.BenchmarkProfile);
            Assert.Equal("dev-box-a", report.Identity.RunnerId);
            Assert.Equal("self-hosted", report.Identity.RunnerKind);
            Assert.Equal(BenchmarkRunIdentity.DefaultTransportBackend, report.Identity.TransportBackend);
            Assert.Equal("Windows", report.Identity.OsDescription);
            Assert.Equal("X64", report.Identity.OsArchitecture);
            Assert.Equal("X64", report.Identity.ProcessArchitecture);
            Assert.Equal(".NET 9.0", report.Identity.FrameworkDescription);
            Assert.Equal(16, report.Identity.ProcessorCount);
        }

        // raw report writer 는 이미 payload/target field 를 기록한다.
        // reader 가 값을 버리면 이후 summary comparison key 가 모든 run 을 같은 부하 조건으로 오판할 수 있다.
        [Fact]
        public void ReadDirectory_WhenRunReportHasPayloadAndTarget_ReadsSettings()
        {
            string directory = CreateTempDirectory();
            WriteRunJson(Path.Combine(directory, "load-01.json"), "load", 500.0, 1, 0, 3000);

            BaselineReport report = BaselineReportReader.ReadDirectory(directory).Single();

            Assert.Equal(4096, report.PayloadBytes);
            Assert.Equal(100.0, report.TargetRateHz);
            Assert.Equal(30, report.TargetDurationSeconds);
        }

        // 과거 baseline artifact 에는 metadata field 가 없다.
        // legacy report 를 읽을 때 crash 하거나 임의 값을 만들지 않고 Unknown identity 로 보존해야 재생성이 안전하다.
        [Fact]
        public void ReadDirectory_WhenLegacyRunReportHasNoRunnerIdentity_UsesUnknownIdentity()
        {
            string directory = CreateTempDirectory();
            WriteRunJson(Path.Combine(directory, "load-01.json"), "load", 500.0, 1, 0, 3000);

            BaselineReport report = BaselineReportReader.ReadDirectory(directory).Single();

            Assert.Equal("unknown", report.Identity.BenchmarkProfile);
            Assert.Equal("unknown", report.Identity.RunnerId);
            Assert.Equal("unknown", report.Identity.RunnerKind);
            Assert.Equal(0, report.Identity.ProcessorCount);
        }

        private static string CreateTempDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "hps-baseline-summary-tests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void WriteRunJson(string path, string resultName, double p99, int tcpHwm, long dropped, int received)
        {
            string json = "{"
                + "\"schema-version\":1,"
                + "\"result-name\":\"" + resultName + "\","
                + "\"passed\":true,"
                + "\"scenario\":\"tcp-loopback-saea-baseline\","
                + "\"payload-bytes\":4096,"
                + "\"target-rate-hz\":100,"
                + "\"target-duration-seconds\":30,"
                + "\"planned-message-count\":3000,"
                + "\"sent\":3000,"
                + "\"received\":" + received.ToString(CultureInfo.InvariantCulture) + ","
                + "\"dropped\":" + dropped.ToString(CultureInfo.InvariantCulture) + ","
                + "\"tcp-pending-send-queue-high-watermark\":" + tcpHwm.ToString(CultureInfo.InvariantCulture) + ","
                + "\"udp-pending-send-queue-high-watermark\":0,"
                + "\"payload-errors\":0,"
                + "\"pool-rented\":0,"
                + "\"actual-rate-hz\":99.9,"
                + "\"p50-latency-us\":240.0,"
                + "\"p99-latency-us\":" + p99.ToString(CultureInfo.InvariantCulture) + ","
                + "\"first-half-p99-latency-us\":500.0,"
                + "\"second-half-p99-latency-us\":500.0,"
                + "\"p99-latency-growth-ratio\":1.0,"
                + "\"elapsed-ms\":30000"
                + "}";
            File.WriteAllText(path, json);
        }

        private static void WriteRunJsonWithIdentity(string path, string resultName, string runnerId, string runnerKind)
        {
            string json = "{"
                + "\"schema-version\":1,"
                + "\"result-name\":\"" + resultName + "\","
                + "\"passed\":true,"
                + "\"scenario\":\"tcp-loopback-saea-baseline\","
                + "\"benchmark-profile\":\"tcp-loopback-saea-v1\","
                + "\"runner-id\":\"" + runnerId + "\","
                + "\"runner-kind\":\"" + runnerKind + "\","
                + "\"transport-backend\":\"SaeaTransport\","
                + "\"os-description\":\"Windows\","
                + "\"os-architecture\":\"X64\","
                + "\"process-architecture\":\"X64\","
                + "\"framework-description\":\".NET 9.0\","
                + "\"processor-count\":16,"
                + "\"payload-bytes\":4096,"
                + "\"target-rate-hz\":100,"
                + "\"target-duration-seconds\":30,"
                + "\"planned-message-count\":3000,"
                + "\"sent\":3000,"
                + "\"received\":3000,"
                + "\"dropped\":0,"
                + "\"tcp-pending-send-queue-high-watermark\":1,"
                + "\"udp-pending-send-queue-high-watermark\":0,"
                + "\"payload-errors\":0,"
                + "\"pool-rented\":0,"
                + "\"actual-rate-hz\":99.9,"
                + "\"p50-latency-us\":240.0,"
                + "\"p99-latency-us\":500.0,"
                + "\"first-half-p99-latency-us\":500.0,"
                + "\"second-half-p99-latency-us\":500.0,"
                + "\"p99-latency-growth-ratio\":1.0,"
                + "\"elapsed-ms\":30000"
                + "}";
            File.WriteAllText(path, json);
        }

        private static BenchmarkRunIdentity CreateIdentity(string runnerId)
        {
            return new BenchmarkRunIdentity(
                BenchmarkRunIdentity.DefaultBenchmarkProfile,
                runnerId,
                BenchmarkRunIdentity.DefaultRunnerKind,
                BenchmarkRunIdentity.DefaultTransportBackend,
                "Windows",
                "X64",
                "X64",
                ".NET 9.0",
                16);
        }
    }
}
