using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineHistoryReaderTests
    {
        // history session 이 summary comparison 을 잃지 않으려면 model surface 가 먼저 있어야 한다.
        // direct property 접근은 컴파일 실패 Red가 되므로 reflection 으로 assertion-failure Red 를 만든다.
        [Fact]
        public void Contract_BaselineHistorySessionExposesComparison()
        {
            Assert.NotNull(typeof(BaselineHistorySession).GetProperty("Comparison"));
        }

        // 날짜 root 를 직접 입력하는 경로는 2026-06-18 baseline 호환에 필요하다.
        // root summary 는 과거 구조에서 session-01 역할을 하므로 `session-01(root)`로 표시해야 한다.
        [Fact]
        public void ReadSessions_WhenInputIsDateRoot_ReadsRootSummaryAndChildSessions()
        {
            string dateRoot = CreateTempDirectory("2026-06-18");
            WriteSummary(Path.Combine(dateRoot, "summary.json"), true, 0, 6, 924.1, 1005.5, 2);
            string sessionTwo = Path.Combine(dateRoot, "session-02");
            Directory.CreateDirectory(sessionTwo);
            WriteSummary(Path.Combine(sessionTwo, "summary.json"), true, 1, 6, 512.1, 643.3, 3);
            File.WriteAllText(Path.Combine(dateRoot, "history.json"), "{}");

            BaselineHistorySession[] sessions = BaselineHistoryReader.ReadSessions(dateRoot).ToArray();

            Assert.Equal(2, sessions.Length);
            Assert.Equal("2026-06-18", sessions[0].Date);
            Assert.Equal("session-01(root)", sessions[0].Session);
            Assert.Equal("summary.json", sessions[0].SummaryPath);
            Assert.Equal("session-02", sessions[1].Session);
            Assert.Equal("session-02/summary.json", sessions[1].SummaryPath);
            Assert.Equal(1, sessions[1].WarningCount);
            Assert.Equal(3, sessions[1].TcpHighWatermarkMax);
        }

        // parent baseline root 입력은 여러 날짜 directory 를 읽되 한 단계 아래 날짜 directory 만 본다.
        // 무제한 recursive scan 을 허용하면 generated history 나 임시 복사본이 중복 집계될 수 있다.
        [Fact]
        public void ReadSessions_WhenInputIsParentRoot_ReadsImmediateDateChildrenOnly()
        {
            string parentRoot = CreateTempDirectory("baselines");
            string dateRoot = Path.Combine(parentRoot, "2026-06-18");
            Directory.CreateDirectory(dateRoot);
            WriteSummary(Path.Combine(dateRoot, "summary.json"), true, 0, 6, 924.1, 1005.5, 2);
            string ignored = Path.Combine(parentRoot, "notes");
            Directory.CreateDirectory(ignored);
            WriteSummary(Path.Combine(ignored, "summary.json"), true, 0, 6, 1.0, 1.0, 1);

            BaselineHistorySession[] sessions = BaselineHistoryReader.ReadSessions(parentRoot).ToArray();

            Assert.Single(sessions);
            Assert.Equal("2026-06-18", sessions[0].Date);
            Assert.Equal("2026-06-18/summary.json", sessions[0].SummaryPath);
        }

        // history reader 는 summary 가 계산한 comparison signal 을 잃지 않아야 한다.
        // 이 값을 잃으면 history generator 가 legacy/mismatch session 을 구분할 수 없다.
        [Fact]
        public void ReadSessions_WhenSummaryHasComparison_ReadsComparisonSignal()
        {
            string dateRoot = CreateTempDirectory("2026-06-18");
            WriteSummaryWithComparison(Path.Combine(dateRoot, "summary.json"), "runner-a");

            BaselineHistorySession session = BaselineHistoryReader.ReadSessions(dateRoot).Single();

            Assert.True(session.Comparison.Compatible);
            Assert.Equal(0, session.Comparison.MismatchCount);
            BaselineComparisonKey? key = session.Comparison.Key;
            Assert.NotNull(key);
            Assert.Equal("runner-a", key!.RunnerId);
            Assert.Equal(4096, key.Cases[0].PayloadBytes);
        }

        // 과거 summary.json 은 comparison field 가 없다.
        // 이런 summary 를 compatible 로 추정하면 history 전체가 비교 가능한 것처럼 보이므로 명시 mismatch 로 바꾼다.
        [Fact]
        public void ReadSessions_WhenSummaryHasNoComparison_MarksSessionComparisonIncompatible()
        {
            string dateRoot = CreateTempDirectory("2026-06-18");
            WriteSummary(Path.Combine(dateRoot, "summary.json"), true, 0, 6, 924.1, 1005.5, 2);

            BaselineHistorySession session = BaselineHistoryReader.ReadSessions(dateRoot).Single();

            Assert.False(session.Comparison.Compatible);
            BaselineComparisonMismatch mismatch = Assert.Single(session.Comparison.Mismatches);
            Assert.Equal("legacy-summary-without-comparison", mismatch.Code);
            Assert.Equal("session-01(root)", mismatch.Session);
            Assert.Equal("summary.json", mismatch.SummaryPath);
            Assert.Equal("comparison-compatible", mismatch.Field);
            Assert.Equal("present", mismatch.Expected);
            Assert.Equal("missing", mismatch.Actual);
        }

        // 부분 summary 는 history command 를 중단시키지 않되, 누락된 p99 를 0으로 위장하지 않는다.
        // 0은 매우 빠른 정상 latency 로 읽힐 수 있으므로 null 로 노출해 artifact 결함을 분명히 드러낸다.
        [Fact]
        public void ReadSessions_WhenKindSummaryIsMissing_UsesNullP99AndZeroHwm()
        {
            string dateRoot = CreateTempDirectory("2026-06-18");
            WriteSummaryWithoutKinds(Path.Combine(dateRoot, "summary.json"), true, 0, 6);

            BaselineHistorySession session = BaselineHistoryReader.ReadSessions(dateRoot).Single();

            Assert.Null(session.LoadP99MaxMicroseconds);
            Assert.Null(session.OpenLoopP99MaxMicroseconds);
            Assert.Equal(0, session.TcpHighWatermarkMax);
        }

        // summary 가 하나도 없으면 성공한 빈 history 로 위장하지 않는다.
        // Program wiring 은 이 예외를 usage/data error exit code 2로 수렴시켜야 한다.
        [Fact]
        public void ReadSessions_WhenNoSummaryExists_ThrowsInvalidOperationException()
        {
            string parentRoot = CreateTempDirectory("baselines");

            Assert.Throws<InvalidOperationException>(
                delegate { BaselineHistoryReader.ReadSessions(parentRoot); });
        }

        private static string CreateTempDirectory(string leafName)
        {
            string directory = Path.Combine(Path.GetTempPath(), "hps-baseline-history-reader-tests", Path.GetRandomFileName(), leafName);
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void WriteSummary(string path, bool hardPassed, int warningCount, int reportCount, double loadP99, double openLoopP99, int tcpHwm)
        {
            string json = "{"
                + "\"summary-version\":1,"
                + "\"source-directory\":\"source\","
                + "\"source-report-count\":" + reportCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"hard-passed\":" + (hardPassed ? "true" : "false") + ","
                + "\"hard-failure-count\":" + (hardPassed ? "0" : "1") + ","
                + "\"warning-count\":" + warningCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"warnings\":[],"
                + "\"by-kind\":{"
                + "\"load\":{\"p99-max-us\":" + loadP99.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"tcp-hwm-max\":" + tcpHwm.ToString(System.Globalization.CultureInfo.InvariantCulture) + "},"
                + "\"open-loop\":{\"p99-max-us\":" + openLoopP99.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"tcp-hwm-max\":" + tcpHwm.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}"
                + "}"
                + "}";
            File.WriteAllText(path, json);
        }

        private static void WriteSummaryWithoutKinds(string path, bool hardPassed, int warningCount, int reportCount)
        {
            string json = "{"
                + "\"summary-version\":1,"
                + "\"source-directory\":\"source\","
                + "\"source-report-count\":" + reportCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"hard-passed\":" + (hardPassed ? "true" : "false") + ","
                + "\"hard-failure-count\":" + (hardPassed ? "0" : "1") + ","
                + "\"warning-count\":" + warningCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"warnings\":[],"
                + "\"by-kind\":{}"
                + "}";
            File.WriteAllText(path, json);
        }

        private static void WriteSummaryWithComparison(string path, string runnerId)
        {
            string json = "{"
                + "\"summary-version\":1,"
                + "\"source-directory\":\"source\","
                + "\"source-report-count\":6,"
                + "\"hard-passed\":true,"
                + "\"hard-failure-count\":0,"
                + "\"warning-count\":0,"
                + "\"comparison-compatible\":true,"
                + "\"comparison-key\":{"
                + "\"benchmark-profile\":\"tcp-loopback-saea-v1\","
                + "\"runner-id\":\"" + runnerId + "\","
                + "\"runner-kind\":\"local\","
                + "\"transport-backend\":\"SaeaTransport\","
                + "\"os-description\":\"Windows\","
                + "\"os-architecture\":\"X64\","
                + "\"process-architecture\":\"X64\","
                + "\"framework-description\":\".NET 9.0\","
                + "\"cases\":[{\"result-name\":\"load\",\"scenario\":\"tcp-loopback-saea-baseline\",\"payload-bytes\":4096,\"target-rate-hz\":100,\"target-duration-seconds\":30}]"
                + "},"
                + "\"unknown-runner-count\":0,"
                + "\"comparison-mismatch-count\":0,"
                + "\"comparison-mismatches\":[],"
                + "\"warnings\":[],"
                + "\"by-kind\":{"
                + "\"load\":{\"p99-max-us\":924.1,\"tcp-hwm-max\":2},"
                + "\"open-loop\":{\"p99-max-us\":1005.5,\"tcp-hwm-max\":2}"
                + "}"
                + "}";
            File.WriteAllText(path, json);
        }
    }
}
