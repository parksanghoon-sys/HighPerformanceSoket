using System.IO;
using System.Text.Json;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineHistoryGeneratorWriterTests
    {
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

        private static BaselineHistorySession CreateSession(
            string date,
            string session,
            bool hardPassed,
            int hardFailureCount,
            int warningCount,
            double? loadP99,
            double? openLoopP99,
            int tcpHwm)
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
                tcpHwm);
        }

        private static string CreateTempDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "hps-baseline-history-writer-tests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
