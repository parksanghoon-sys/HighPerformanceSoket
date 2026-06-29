using System.Globalization;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineEnvelopeProgramTests
    {
        // Program wiring 은 parser, reader, generator, writer 를 실제 CLI 경로로 묶는다.
        // envelope signal 이 없어도 JSON/Markdown artifact 가 생성되어야 한다.
        [Fact]
        public void Main_WhenEnvelopeCommandHasCompatibleCandidate_WritesArtifactsAndReturnsSuccess()
        {
            string root = CreateTempDirectory();
            string referenceSummary = Path.Combine(root, "reference", "summary.json");
            string candidateSummary = Path.Combine(root, "candidate", "summary.json");
            Directory.CreateDirectory(Path.GetDirectoryName(referenceSummary)!);
            Directory.CreateDirectory(Path.GetDirectoryName(candidateSummary)!);
            WriteSummary(referenceSummary, "runner-a", 900.0, 990.0, 100.0);
            WriteSummary(candidateSummary, "runner-a", 950.0, 1000.0, 99.5);
            string referenceHistory = Path.Combine(root, "history.json");
            WriteHistory(referenceHistory, "reference/summary.json");
            string envelopeJson = Path.Combine(root, "envelope.json");
            string envelopeMarkdown = Path.Combine(root, "envelope.md");

            int exitCode = Program.Main(new[] { "--compare-baseline-envelope", candidateSummary, "--reference-history", referenceHistory, "--envelope", envelopeJson, "--envelope-md", envelopeMarkdown });

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(envelopeJson));
            Assert.True(File.Exists(envelopeMarkdown));
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(envelopeJson)))
            {
                Assert.True(document.RootElement.GetProperty("envelope-compatible").GetBoolean());
                Assert.Equal(0, document.RootElement.GetProperty("envelope-signal-count").GetInt32());
            }
        }

        // envelope signal 은 D125 기준 process failure 가 아니다.
        // candidate p99 가 limit 을 넘어도 command 는 artifact 를 남기고 exit code 0을 유지해야 한다.
        [Fact]
        public void Main_WhenEnvelopeCommandHasSignals_ReturnsSuccessAndWritesSignalCount()
        {
            string root = CreateTempDirectory();
            string referenceSummary = Path.Combine(root, "reference", "summary.json");
            string candidateSummary = Path.Combine(root, "candidate", "summary.json");
            Directory.CreateDirectory(Path.GetDirectoryName(referenceSummary)!);
            Directory.CreateDirectory(Path.GetDirectoryName(candidateSummary)!);
            WriteSummary(referenceSummary, "runner-a", 900.0, 990.0, 100.0);
            WriteSummary(candidateSummary, "runner-a", 1300.0, 1400.0, 100.0);
            string referenceHistory = Path.Combine(root, "history.json");
            WriteHistory(referenceHistory, "reference/summary.json");
            string envelopeJson = Path.Combine(root, "envelope.json");

            int exitCode = Program.Main(new[] { "--compare-baseline-envelope", candidateSummary, "--reference-history", referenceHistory, "--envelope", envelopeJson });

            Assert.Equal(0, exitCode);
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(envelopeJson)))
            {
                Assert.False(document.RootElement.GetProperty("envelope-compatible").GetBoolean());
                Assert.True(document.RootElement.GetProperty("envelope-signal-count").GetInt32() > 0);
            }
        }

        private static string CreateTempDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "hps-envelope-program-tests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void WriteHistory(string path, string summaryPath)
        {
            File.WriteAllText(
                path,
                "{"
                + "\"history-version\":1,"
                + "\"source-root\":\"source\","
                + "\"session-count\":1,"
                + "\"hard-passed\":true,"
                + "\"failed-session-count\":0,"
                + "\"warning-count\":0,"
                + "\"comparison-compatible\":true,"
                + WriteComparisonKey("runner-a") + ","
                + "\"unknown-runner-count\":0,"
                + "\"comparison-mismatch-count\":0,"
                + "\"comparison-mismatches\":[],"
                + "\"sessions\":[{"
                + "\"date\":\"2026-06-29\","
                + "\"session\":\"session-01\","
                + "\"summary-path\":\"" + summaryPath + "\","
                + "\"human-report-path\":null,"
                + "\"source-report-count\":6,"
                + "\"hard-passed\":true,"
                + "\"warning-count\":0,"
                + "\"load-p99-max-us\":900,"
                + "\"open-loop-p99-max-us\":990,"
                + "\"tcp-hwm-max\":2,"
                + "\"comparison-compatible\":true,"
                + "\"unknown-runner-count\":0,"
                + "\"comparison-mismatch-count\":0"
                + "}]"
                + "}");
        }

        private static void WriteSummary(string path, string runnerId, double loadP99, double openLoopP99, double actualRate)
        {
            File.WriteAllText(
                path,
                "{"
                + "\"summary-version\":1,"
                + "\"source-directory\":\"source\","
                + "\"source-report-count\":6,"
                + "\"hard-passed\":true,"
                + "\"hard-failure-count\":0,"
                + "\"warning-count\":0,"
                + "\"comparison-compatible\":true,"
                + WriteComparisonKey(runnerId) + ","
                + "\"unknown-runner-count\":0,"
                + "\"comparison-mismatch-count\":0,"
                + "\"comparison-mismatches\":[],"
                + "\"warnings\":[],"
                + "\"by-kind\":{"
                + "\"load\":" + WriteKind(loadP99, actualRate) + ","
                + "\"open-loop\":" + WriteKind(openLoopP99, actualRate)
                + "}"
                + "}");
        }

        private static string WriteComparisonKey(string runnerId)
        {
            return "\"comparison-key\":{"
                + "\"benchmark-profile\":\"tcp-loopback-saea-v1\","
                + "\"runner-id\":\"" + runnerId + "\","
                + "\"runner-kind\":\"local\","
                + "\"transport-backend\":\"SaeaTransport\","
                + "\"os-description\":\"Windows\","
                + "\"os-architecture\":\"X64\","
                + "\"process-architecture\":\"X64\","
                + "\"framework-description\":\".NET 9.0\","
                + "\"cases\":["
                + "{\"result-name\":\"load\",\"scenario\":\"tcp-loopback-saea-load\",\"payload-bytes\":4096,\"target-rate-hz\":100,\"target-duration-seconds\":30},"
                + "{\"result-name\":\"open-loop\",\"scenario\":\"tcp-loopback-saea-open-loop\",\"payload-bytes\":4096,\"target-rate-hz\":100,\"target-duration-seconds\":30}"
                + "]"
                + "}";
        }

        private static string WriteKind(double p99, double actualRate)
        {
            return "{"
                + "\"run-count\":3,"
                + "\"p50-min-us\":100,"
                + "\"p50-max-us\":200,"
                + "\"p50-median-us\":150,"
                + "\"p99-min-us\":" + (p99 - 10.0).ToString(CultureInfo.InvariantCulture) + ","
                + "\"p99-max-us\":" + p99.ToString(CultureInfo.InvariantCulture) + ","
                + "\"p99-median-us\":" + (p99 - 5.0).ToString(CultureInfo.InvariantCulture) + ","
                + "\"p99-growth-ratio-min\":1,"
                + "\"p99-growth-ratio-max\":1.1,"
                + "\"actual-rate-min-hz\":" + actualRate.ToString(CultureInfo.InvariantCulture) + ","
                + "\"actual-rate-max-hz\":" + actualRate.ToString(CultureInfo.InvariantCulture) + ","
                + "\"tcp-hwm-min\":1,"
                + "\"tcp-hwm-max\":2,"
                + "\"dropped-total\":0,"
                + "\"payload-error-total\":0,"
                + "\"pool-rented-max\":0"
                + "}";
        }
    }
}
