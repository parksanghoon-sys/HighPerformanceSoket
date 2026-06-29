using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineEnvelopeSourceReaderTests
    {
        // envelope source reader 는 새 command 의 input boundary 다.
        // 타입이 없으면 reference history 와 candidate summary/history 를 같은 model 로 다룰 수 없다.
        [Fact]
        public void Contract_BaselineEnvelopeSourceReaderExists()
        {
            Assert.NotNull(typeof(BenchmarkCommandParser).Assembly.GetType("Hps.Benchmarks.BaselineEnvelopeSourceReader"));
        }

        // summary input 은 candidate 로 자주 쓰는 최소 단위다.
        // reader 가 by-kind aggregate 와 comparison key 를 보존해야 generator 가 raw report 를 다시 열지 않는다.
        [Fact]
        public void Read_WhenPathIsSummary_ReadsSingleSummarySource()
        {
            string directory = CreateTempDirectory();
            string summaryPath = Path.Combine(directory, "summary.json");
            WriteSummary(summaryPath, "runner-a", true, 0, 900.0, 1000.0, 2);

            BaselineEnvelopeSource source = BaselineEnvelopeSourceReader.Read(summaryPath);

            Assert.Equal(BaselineEnvelopeSourceKind.Summary, source.Kind);
            Assert.Equal(Path.GetFullPath(summaryPath), source.SourcePath);
            BaselineEnvelopeSummary summary = Assert.Single(source.Summaries);
            Assert.Equal(Path.GetFullPath(summaryPath), summary.SummaryPath);
            Assert.True(summary.HardPassed);
            Assert.True(summary.Comparison.Compatible);
            Assert.Equal("runner-a", summary.Comparison.Key!.RunnerId);
            Assert.NotNull(summary.Load);
            Assert.Equal(900.0, summary.Load!.P99Max);
            Assert.NotNull(summary.OpenLoop);
            Assert.Equal(2, summary.OpenLoop!.TcpHighWatermarkMax);
        }

        // reference 는 runner root history 를 입력으로 받는다.
        // history.json 에는 full metric 이 없으므로 session summary-path 를 history file directory 기준으로 다시 열어야 한다.
        [Fact]
        public void Read_WhenPathIsHistory_ResolvesSessionSummaryPathsRelativeToHistoryDirectory()
        {
            string root = CreateTempDirectory();
            string sessionOne = Path.Combine(root, "2026-06-29", "session-01");
            string sessionTwo = Path.Combine(root, "2026-06-29", "session-02");
            Directory.CreateDirectory(sessionOne);
            Directory.CreateDirectory(sessionTwo);
            WriteSummary(Path.Combine(sessionOne, "summary.json"), "runner-a", true, 0, 900.0, 980.0, 2);
            WriteSummary(Path.Combine(sessionTwo, "summary.json"), "runner-a", true, 0, 910.0, 1000.0, 3);
            string historyPath = Path.Combine(root, "history.json");
            WriteHistory(historyPath, "2026-06-29/session-01/summary.json", "2026-06-29/session-02/summary.json");

            BaselineEnvelopeSource source = BaselineEnvelopeSourceReader.Read(historyPath);

            Assert.Equal(BaselineEnvelopeSourceKind.History, source.Kind);
            Assert.True(source.Comparison.Compatible);
            Assert.Equal(2, source.Summaries.Count);
            Assert.Equal(Path.Combine(sessionOne, "summary.json"), source.Summaries[0].SummaryPath);
            Assert.Equal(900.0, source.Summaries[0].Load!.P99Max);
            Assert.Equal(3, source.Summaries[1].OpenLoop!.TcpHighWatermarkMax);
        }

        // history summary-path 가 깨졌으면 envelope 비교는 진행할 수 없다.
        // 조용히 summary 를 건너뛰면 reference envelope 가 실제보다 느슨하거나 좁아진다.
        [Fact]
        public void Read_WhenHistoryReferencesMissingSummary_ThrowsInvalidOperationException()
        {
            string root = CreateTempDirectory();
            string historyPath = Path.Combine(root, "history.json");
            WriteHistory(historyPath, "2026-06-29/session-01/summary.json");

            Assert.Throws<InvalidOperationException>(
                delegate { BaselineEnvelopeSourceReader.Read(historyPath); });
        }

        private static string CreateTempDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "hps-envelope-source-reader-tests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void WriteHistory(string path, params string[] summaryPaths)
        {
            string sessions = string.Join(
                ",",
                summaryPaths.Select(
                    delegate(string summaryPath)
                    {
                        return "{"
                            + "\"date\":\"2026-06-29\","
                            + "\"session\":\"session-01\","
                            + "\"summary-path\":\"" + summaryPath.Replace("\\", "\\\\") + "\","
                            + "\"human-report-path\":null,"
                            + "\"source-report-count\":6,"
                            + "\"hard-passed\":true,"
                            + "\"warning-count\":0,"
                            + "\"load-p99-max-us\":900,"
                            + "\"open-loop-p99-max-us\":1000,"
                            + "\"tcp-hwm-max\":2,"
                            + "\"comparison-compatible\":true,"
                            + "\"unknown-runner-count\":0,"
                            + "\"comparison-mismatch-count\":0"
                            + "}";
                    }));

            File.WriteAllText(
                path,
                "{"
                + "\"history-version\":1,"
                + "\"source-root\":\"source\","
                + "\"session-count\":" + summaryPaths.Length.ToString(CultureInfo.InvariantCulture) + ","
                + "\"hard-passed\":true,"
                + "\"failed-session-count\":0,"
                + "\"warning-count\":0,"
                + "\"comparison-compatible\":true,"
                + WriteComparisonKey("runner-a") + ","
                + "\"unknown-runner-count\":0,"
                + "\"comparison-mismatch-count\":0,"
                + "\"comparison-mismatches\":[],"
                + "\"sessions\":[" + sessions + "]"
                + "}");
        }

        private static void WriteSummary(string path, string runnerId, bool hardPassed, int warningCount, double loadP99, double openLoopP99, int tcpHwm)
        {
            File.WriteAllText(
                path,
                "{"
                + "\"summary-version\":1,"
                + "\"source-directory\":\"source\","
                + "\"source-report-count\":6,"
                + "\"hard-passed\":" + (hardPassed ? "true" : "false") + ","
                + "\"hard-failure-count\":" + (hardPassed ? "0" : "1") + ","
                + "\"warning-count\":" + warningCount.ToString(CultureInfo.InvariantCulture) + ","
                + "\"comparison-compatible\":true,"
                + WriteComparisonKey(runnerId) + ","
                + "\"unknown-runner-count\":0,"
                + "\"comparison-mismatch-count\":0,"
                + "\"comparison-mismatches\":[],"
                + "\"warnings\":[],"
                + "\"by-kind\":{"
                + "\"load\":" + WriteKind("load", loadP99, 100.0, tcpHwm) + ","
                + "\"open-loop\":" + WriteKind("open-loop", openLoopP99, 100.0, tcpHwm)
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

        private static string WriteKind(string kind, double p99Max, double actualRate, int tcpHwm)
        {
            return "{"
                + "\"run-count\":3,"
                + "\"p50-min-us\":100,"
                + "\"p50-max-us\":200,"
                + "\"p50-median-us\":150,"
                + "\"p99-min-us\":" + (p99Max - 10.0).ToString(CultureInfo.InvariantCulture) + ","
                + "\"p99-max-us\":" + p99Max.ToString(CultureInfo.InvariantCulture) + ","
                + "\"p99-median-us\":" + (p99Max - 5.0).ToString(CultureInfo.InvariantCulture) + ","
                + "\"p99-growth-ratio-min\":1,"
                + "\"p99-growth-ratio-max\":1.1,"
                + "\"actual-rate-min-hz\":" + actualRate.ToString(CultureInfo.InvariantCulture) + ","
                + "\"actual-rate-max-hz\":" + actualRate.ToString(CultureInfo.InvariantCulture) + ","
                + "\"tcp-hwm-min\":1,"
                + "\"tcp-hwm-max\":" + tcpHwm.ToString(CultureInfo.InvariantCulture) + ","
                + "\"dropped-total\":0,"
                + "\"payload-error-total\":0,"
                + "\"pool-rented-max\":0"
                + "}";
        }
    }
}
