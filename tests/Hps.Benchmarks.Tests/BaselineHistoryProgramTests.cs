using System.Globalization;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineHistoryProgramTests
    {
        // Program wiring은 parser, reader, generator, writer를 실제 CLI 경로로 묶는다.
        // history JSON/Markdown 파일이 함께 생성되어야 수동 baseline index를 generated artifact로 대체할 수 있다.
        [Fact]
        public void Main_WhenHistoryCommandHasPassingSummaries_WritesJsonAndMarkdownAndReturnsSuccess()
        {
            string root = CreateTempDirectory("baselines");
            string dateRoot = Path.Combine(root, "2026-06-18");
            Directory.CreateDirectory(dateRoot);
            WriteSummary(Path.Combine(dateRoot, "summary.json"), true, 0);
            string historyJson = Path.Combine(root, "history.json");
            string historyMarkdown = Path.Combine(root, "history.md");

            int exitCode = Program.Main(new[] { "--summarize-baseline-history", root, "--history", historyJson, "--history-md", historyMarkdown });

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(historyJson));
            Assert.True(File.Exists(historyMarkdown));
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(historyJson)))
            {
                Assert.True(document.RootElement.GetProperty("hard-passed").GetBoolean());
                Assert.Equal(1, document.RootElement.GetProperty("session-count").GetInt32());
                Assert.Equal(0, document.RootElement.GetProperty("failed-session-count").GetInt32());
            }
        }

        // hard failure는 기존 summary의 delivery/drop/leak gate 결과를 history command exit code로 올려야 한다.
        // raw failure count가 아니라 session 단위 hard-passed false 하나가 전체 history 실패를 만든다.
        [Fact]
        public void Main_WhenHistoryCommandHasFailedSummary_ReturnsFailedRunExitCode()
        {
            string root = CreateTempDirectory("baselines");
            string dateRoot = Path.Combine(root, "2026-06-18");
            Directory.CreateDirectory(dateRoot);
            WriteSummary(Path.Combine(dateRoot, "summary.json"), false, 0);
            string historyJson = Path.Combine(root, "history.json");

            int exitCode = Program.Main(new[] { "--summarize-baseline-history", root, "--history", historyJson });

            Assert.Equal(1, exitCode);
            Assert.True(File.Exists(historyJson));
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(historyJson)))
            {
                Assert.False(document.RootElement.GetProperty("hard-passed").GetBoolean());
                Assert.Equal(1, document.RootElement.GetProperty("failed-session-count").GetInt32());
            }
        }

        // warning은 D078 기준 soft signal이다.
        // warning-count가 있어도 hard-passed가 true이면 command는 성공 exit code를 유지해야 한다.
        [Fact]
        public void Main_WhenHistoryCommandHasWarningsOnly_ReturnsSuccess()
        {
            string root = CreateTempDirectory("baselines");
            string dateRoot = Path.Combine(root, "2026-06-18");
            Directory.CreateDirectory(dateRoot);
            WriteSummary(Path.Combine(dateRoot, "summary.json"), true, 2);
            string historyJson = Path.Combine(root, "history.json");

            int exitCode = Program.Main(new[] { "--summarize-baseline-history", root, "--history", historyJson });

            Assert.Equal(0, exitCode);
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(historyJson)))
            {
                Assert.True(document.RootElement.GetProperty("hard-passed").GetBoolean());
                Assert.Equal(2, document.RootElement.GetProperty("warning-count").GetInt32());
            }
        }

        private static string CreateTempDirectory(string leafName)
        {
            string directory = Path.Combine(Path.GetTempPath(), "hps-baseline-history-program-tests", Path.GetRandomFileName(), leafName);
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void WriteSummary(string path, bool hardPassed, int warningCount)
        {
            string json = "{"
                + "\"summary-version\":1,"
                + "\"source-directory\":\"source\","
                + "\"source-report-count\":6,"
                + "\"hard-passed\":" + (hardPassed ? "true" : "false") + ","
                + "\"hard-failure-count\":" + (hardPassed ? "0" : "1") + ","
                + "\"warning-count\":" + warningCount.ToString(CultureInfo.InvariantCulture) + ","
                + "\"warnings\":[],"
                + "\"by-kind\":{"
                + "\"load\":{\"p99-max-us\":924.1,\"tcp-hwm-max\":2},"
                + "\"open-loop\":{\"p99-max-us\":1005.5,\"tcp-hwm-max\":3}"
                + "}"
                + "}";
            File.WriteAllText(path, json);
        }
    }
}
