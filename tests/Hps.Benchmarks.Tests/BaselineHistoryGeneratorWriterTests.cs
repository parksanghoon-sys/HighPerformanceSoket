using System.IO;
using System.Text.Json;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineHistoryGeneratorWriterTests
    {
        // history 전체 compatibility 를 output 으로 넘기려면 aggregate model 이 comparison result 를 보존해야 한다.
        // property 부재를 컴파일 실패가 아니라 assertion failure 로 확인하기 위해 reflection 을 사용한다.
        [Fact]
        public void Contract_BaselineHistoryExposesComparison()
        {
            Assert.NotNull(typeof(BaselineHistory).GetProperty("Comparison"));
        }

        // history aggregate 의 hard gate 는 모든 session summary 의 hard-passed AND 로 계산한다.
        // failed-session-count 는 raw-run 실패 합계가 아니라 실패한 session 수라서 raw 실패 2개인 session 하나도 1로 집계한다.
        [Fact]
        public void Generate_WhenSessionsContainFailureAndWarnings_AggregatesCounts()
        {
            BaselineHistory history = BaselineHistoryGenerator.Generate(
                "docs/baselines",
                new[]
                {
                    CreateSession("2026-06-18", "session-01(root)", true, 0, 0, 924.1, 1005.5, 2),
                    CreateSession("2026-06-19", "session-01", false, 2, 2, 1400.0, 1500.0, 16)
                });

            Assert.Equal("docs/baselines", history.SourceRoot);
            Assert.Equal(2, history.SessionCount);
            Assert.False(history.HardPassed);
            Assert.Equal(1, history.FailedSessionCount);
            Assert.Equal(2, history.WarningCount);
        }

        // 빈 summary 는 summary generator 기준 hard-passed=false 이면서 raw 실패 수가 0일 수 있다.
        // history hard gate 가 실패 카운터에서 파생되면 이 케이스를 PASS로 오판하므로 session flag 자체를 기준으로 삼는다.
        [Fact]
        public void Generate_WhenSessionHardPassedIsFalseWithZeroRawFailures_MarksHistoryFailed()
        {
            BaselineHistory history = BaselineHistoryGenerator.Generate(
                "docs/baselines",
                new[] { CreateSession("2026-06-19", "session-01", false, 0, 0, null, null, 0) });

            Assert.False(history.HardPassed);
            Assert.Equal(1, history.FailedSessionCount);
        }

        // 모든 session summary 가 같은 comparison key 를 가져야 history 전체가 compatible 이다.
        // hard gate 가 모두 PASS 여도 runner/case 가 다르면 history trend 로 비교하면 안 된다.
        [Fact]
        public void Generate_WhenSessionsHaveSameComparisonKey_MarksHistoryComparisonCompatible()
        {
            BaselineHistory history = BaselineHistoryGenerator.Generate(
                "docs/baselines",
                new[]
                {
                    CreateSession("2026-06-18", "session-01(root)", true, 0, 0, 924.1, 1005.5, 2, CreateCompatibleComparison("runner-a")),
                    CreateSession("2026-06-19", "session-01", true, 0, 0, 900.0, 1000.0, 3, CreateCompatibleComparison("runner-a"))
                });

            Assert.True(history.Comparison.Compatible);
            Assert.Equal(0, history.Comparison.MismatchCount);
            Assert.NotNull(history.Comparison.Key);
            Assert.Equal("runner-a", history.Comparison.Key!.RunnerId);
        }

        // 한 session 이 다른 runner/case 를 가지면 history comparison mismatch 로만 기록한다.
        // 이 mismatch 는 기존 warning-count 와 hard-passed 를 변경하지 않는다.
        [Fact]
        public void Generate_WhenSessionComparisonKeyDiffers_RecordsHistoryComparisonMismatchWithoutChangingWarningCount()
        {
            BaselineHistory history = BaselineHistoryGenerator.Generate(
                "docs/baselines",
                new[]
                {
                    CreateSession("2026-06-18", "session-01(root)", true, 0, 0, 924.1, 1005.5, 2, CreateCompatibleComparison("runner-a")),
                    CreateSession("2026-06-19", "session-01", true, 0, 0, 900.0, 1000.0, 3, CreateCompatibleComparison("runner-b"))
                });

            Assert.True(history.HardPassed);
            Assert.Equal(0, history.WarningCount);
            Assert.False(history.Comparison.Compatible);
            BaselineComparisonMismatch mismatch = Assert.Single(history.Comparison.Mismatches);
            Assert.Equal("history-comparison-key-mismatch", mismatch.Code);
            Assert.Equal("runner-id", mismatch.Field);
            Assert.Equal("runner-a", mismatch.Expected);
            Assert.Equal("runner-b", mismatch.Actual);
            Assert.Equal("session-01", mismatch.Session);
        }

        // legacy summary 가 하나라도 섞이면 history comparison 은 incompatible 이다.
        // 기존 history command exit code 는 hard gate 기준만 유지해야 하므로 comparison 은 별도 결과로 남긴다.
        [Fact]
        public void Generate_WhenSessionComparisonIsIncompatible_MarksHistoryComparisonIncompatible()
        {
            BaselineHistory history = BaselineHistoryGenerator.Generate(
                "docs/baselines",
                new[]
                {
                    CreateSession("2026-06-18", "session-01(root)", true, 0, 0, 924.1, 1005.5, 2, CreateCompatibleComparison("runner-a")),
                    CreateSession("2026-06-19", "session-01", true, 0, 0, 900.0, 1000.0, 3, CreateLegacyComparison("session-01", "2026-06-19/session-01/summary.json"))
                });

            Assert.True(history.HardPassed);
            Assert.False(history.Comparison.Compatible);
            BaselineComparisonMismatch mismatch = Assert.Single(history.Comparison.Mismatches);
            Assert.Equal("legacy-summary-without-comparison", mismatch.Code);
            Assert.Equal("session-01", mismatch.Session);
            Assert.Equal("2026-06-19/session-01/summary.json", mismatch.SummaryPath);
        }

        // JSON writer 는 CI/provider 에 묶이지 않는 stable key 집합을 만든다.
        // 이 shape 가 흔들리면 이후 generated index 나 local script 가 history 를 읽지 못한다.
        [Fact]
        public void Write_WhenHistoryHasSessions_WritesStableJsonShape()
        {
            string directory = CreateTempDirectory();
            string path = Path.Combine(directory, "history.json");
            BaselineHistory history = BaselineHistoryGenerator.Generate(
                "docs/baselines",
                new[] { CreateSession("2026-06-18", "session-01(root)", true, 0, 0, 924.1, 1005.5, 2) });

            BaselineHistoryWriter.Write(path, history);

            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement root = document.RootElement;
                Assert.Equal(1, root.GetProperty("history-version").GetInt32());
                Assert.Equal("docs/baselines", root.GetProperty("source-root").GetString());
                Assert.Equal(1, root.GetProperty("session-count").GetInt32());
                Assert.True(root.GetProperty("hard-passed").GetBoolean());
                Assert.Equal(0, root.GetProperty("warning-count").GetInt32());
                Assert.Equal(0, root.GetProperty("failed-session-count").GetInt32());
                JsonElement session = root.GetProperty("sessions")[0];
                Assert.Equal("2026-06-18", session.GetProperty("date").GetString());
                Assert.Equal("session-01(root)", session.GetProperty("session").GetString());
                Assert.Equal(924.1, session.GetProperty("load-p99-max-us").GetDouble());
                Assert.Equal(2, session.GetProperty("tcp-hwm-max").GetInt32());
            }
        }

        // history JSON 은 여러 summary session 을 비교할 때 쓰는 canonical artifact 다.
        // comparison field 가 없으면 session 간 runner/case mismatch 를 자동화가 감지할 수 없다.
        [Fact]
        public void Write_WhenHistoryHasComparison_WritesComparisonFields()
        {
            string directory = CreateTempDirectory();
            string path = Path.Combine(directory, "history.json");
            BaselineHistory history = BaselineHistoryGenerator.Generate(
                "docs/baselines",
                new[]
                {
                    CreateSession("2026-06-18", "session-01(root)", true, 0, 0, 924.1, 1005.5, 2, CreateCompatibleComparison("runner-a")),
                    CreateSession("2026-06-19", "session-01", true, 0, 0, 900.0, 1000.0, 3, CreateLegacyComparison("session-01", "2026-06-19/session-01/summary.json"))
                });

            BaselineHistoryWriter.Write(path, history);

            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement root = document.RootElement;
                Assert.False(root.GetProperty("comparison-compatible").GetBoolean());
                Assert.Equal("runner-a", root.GetProperty("comparison-key").GetProperty("runner-id").GetString());
                Assert.Equal(1, root.GetProperty("comparison-mismatch-count").GetInt32());
                Assert.Equal("legacy-summary-without-comparison", root.GetProperty("comparison-mismatches")[0].GetProperty("code").GetString());

                JsonElement sessions = root.GetProperty("sessions");
                Assert.True(sessions[0].GetProperty("comparison-compatible").GetBoolean());
                Assert.False(sessions[1].GetProperty("comparison-compatible").GetBoolean());
                Assert.Equal(1, sessions[1].GetProperty("comparison-mismatch-count").GetInt32());
            }
        }

        // p99 누락은 0이 아니라 JSON null 로 써야 한다.
        // 0은 정상 latency 값처럼 보이므로 부분 artifact 결함을 숨긴다.
        [Fact]
        public void Write_WhenP99IsMissing_WritesNullP99Values()
        {
            string directory = CreateTempDirectory();
            string path = Path.Combine(directory, "history.json");
            BaselineHistory history = BaselineHistoryGenerator.Generate(
                "docs/baselines",
                new[] { CreateSession("2026-06-18", "session-01(root)", true, 0, 0, null, null, 0) });

            BaselineHistoryWriter.Write(path, history);

            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement session = document.RootElement.GetProperty("sessions")[0];
                Assert.Equal(JsonValueKind.Null, session.GetProperty("load-p99-max-us").ValueKind);
                Assert.Equal(JsonValueKind.Null, session.GetProperty("open-loop-p99-max-us").ValueKind);
            }
        }

        // Markdown writer 는 사람이 현재 index 와 같은 정보를 빠르게 보는 보조 artifact 다.
        // 자동화의 canonical 입력은 JSON 이므로 Markdown 은 session table 과 warning row 존재만 고정한다.
        [Fact]
        public void MarkdownWriter_WhenHistoryHasWarnings_WritesSessionTableAndWarningList()
        {
            BaselineHistory history = BaselineHistoryGenerator.Generate(
                "docs/baselines",
                new[] { CreateSession("2026-06-19", "session-01", true, 0, 2, null, 1500.0, 16) });
            StringWriter writer = new StringWriter();

            BaselineHistoryMarkdownWriter.Write(writer, history);

            string markdown = writer.ToString();
            Assert.Contains("# Baseline History", markdown);
            Assert.Contains("| 2026-06-19 | session-01 |", markdown);
            Assert.Contains("| 2026-06-19 | session-01 | `2026-06-19/session-01/summary.json` | `2026-06-19/session-01/summary.md` | 6 | true | 2 | - | 1500 | 16 |", markdown);
            Assert.Contains("warning 이 있는 session", markdown);
        }

        // Markdown history 는 session table 을 열기 전에 전체 비교 가능성을 보여줘야 한다.
        // runner/case mismatch 가 있을 때 사람이 JSON을 열지 않아도 바로 확인할 수 있어야 한다.
        [Fact]
        public void MarkdownWriter_WhenHistoryHasComparison_WritesComparisonSection()
        {
            BaselineHistory history = BaselineHistoryGenerator.Generate(
                "docs/baselines",
                new[]
                {
                    CreateSession("2026-06-18", "session-01(root)", true, 0, 0, 924.1, 1005.5, 2, CreateCompatibleComparison("runner-a")),
                    CreateSession("2026-06-19", "session-01", true, 0, 0, 900.0, 1000.0, 3, CreateCompatibleComparison("runner-b"))
                });
            StringWriter writer = new StringWriter();

            BaselineHistoryMarkdownWriter.Write(writer, history);

            string markdown = writer.ToString();
            Assert.Contains("## Comparison", markdown);
            Assert.Contains("- compatible: false", markdown);
            Assert.Contains("- runner-id: runner-a", markdown);
            Assert.Contains("| result | scenario | payload bytes | target rate hz | target duration seconds |", markdown);
            Assert.Contains("| history-comparison-key-mismatch | runner-id | runner-a | runner-b | session-01 | `2026-06-19/session-01/summary.json` |", markdown);
        }

        private static BaselineHistorySession CreateSession(
            string date,
            string session,
            bool hardPassed,
            int hardFailureCount,
            int warningCount,
            double? loadP99,
            double? openLoopP99,
            int tcpHwm,
            BaselineComparisonResult? comparison = null)
        {
            return new BaselineHistorySession(
                date,
                session,
                date + "/" + session + "/summary.json",
                date + "/" + session + "/summary.md",
                6,
                hardPassed,
                hardFailureCount,
                warningCount,
                loadP99,
                openLoopP99,
                tcpHwm,
                comparison ?? CreateCompatibleComparison("runner-a"));
        }

        private static BaselineComparisonResult CreateCompatibleComparison(string runnerId)
        {
            return new BaselineComparisonResult(
                true,
                new BaselineComparisonKey(
                    "tcp-loopback-saea-v1",
                    runnerId,
                    "local",
                    "SaeaTransport",
                    "Windows",
                    "X64",
                    "X64",
                    ".NET 9.0",
                    new[]
                    {
                        new BaselineComparisonCase("load", "tcp-loopback-saea-baseline", 4096, 100.0, 30)
                    }),
                0,
                new BaselineComparisonMismatch[0]);
        }

        private static BaselineComparisonResult CreateLegacyComparison(string session, string summaryPath)
        {
            return new BaselineComparisonResult(
                false,
                null,
                0,
                new[]
                {
                    new BaselineComparisonMismatch(
                        "legacy-summary-without-comparison",
                        "comparison-compatible",
                        "present",
                        "missing",
                        null,
                        session,
                        summaryPath)
                });
        }

        private static string CreateTempDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "hps-baseline-history-writer-tests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
