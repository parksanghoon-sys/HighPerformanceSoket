using System.Reflection;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BenchmarkCommandParserTests
    {
        // 기존 --load --report 계약을 먼저 고정한다.
        // baseline suite 추가 중 기존 단일 runner command 가 BenchmarkDotNet fallback 으로 밀려나면
        // report artifact 생성 경로가 조용히 깨질 수 있다.
        [Fact]
        public void TryParse_WhenLoadHasReport_ReturnsLoadCommandWithReportPath()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--load", "--report", "out/load.json" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.Null(errorMessage);
            Assert.Equal(BenchmarkCommand.Load, commandLine.Command);
            Assert.Equal("out/load.json", commandLine.ReportPath);
            Assert.Null(commandLine.BaselineOutputDirectory);
            Assert.Equal(0, commandLine.BaselineRunCount);
        }

        // --report 단독 사용은 실행할 runner 가 없으므로 usage error 로 남아야 한다.
        // 이 경계가 풀리면 사용자가 report 를 기대했는데 BenchmarkDotNet 인자로 해석될 수 있다.
        [Fact]
        public void TryParse_WhenReportHasNoRunner_ReturnsUsageError()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--report", "out/load.json" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.NotNull(errorMessage);
            Assert.Equal(BenchmarkCommand.None, commandLine.Command);
        }

        // 반복 baseline command 는 단일 --report 파일이 아니라 directory 단위 artifact 를 만든다.
        // run count 를 명시하면 다음 단계의 suite runner 가 같은 횟수로 load/open-loop 를 모두 실행해야 한다.
        [Fact]
        public void TryParse_WhenBaselineSuiteHasOutputAndRuns_ReturnsBaselineSuiteCommand()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--baseline-suite", "out/baseline", "--runs", "2" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.Null(errorMessage);
            Assert.Equal(BenchmarkCommand.BaselineSuite, commandLine.Command);
            Assert.Equal("out/baseline", commandLine.BaselineOutputDirectory);
            Assert.Equal(2, commandLine.BaselineRunCount);
            Assert.Null(commandLine.ReportPath);
        }

        // run count 를 생략하면 D069 기준 최소 session 수집 단위인 3회를 기본값으로 사용한다.
        // 이 기본값이 바뀌면 로컬/CI baseline 비교 단위가 흔들리므로 parser 에서 고정한다.
        [Fact]
        public void TryParse_WhenBaselineSuiteHasNoRuns_UsesDefaultRunCount()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--baseline-suite", "out/baseline" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.Null(errorMessage);
            Assert.Equal(BenchmarkCommand.BaselineSuite, commandLine.Command);
            Assert.Equal(3, commandLine.BaselineRunCount);
        }

        // baseline suite 는 directory 안에 per-run JSON을 직접 만든다.
        // --report 와 섞으면 단일 파일 report 인지 suite directory 인지 불명확해지므로 usage error 로 막는다.
        [Fact]
        public void TryParse_WhenBaselineSuiteHasReport_ReturnsUsageError()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--baseline-suite", "out/baseline", "--report", "out.json" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.NotNull(errorMessage);
            Assert.Equal(BenchmarkCommand.BaselineSuite, commandLine.Command);
        }

        // summary command 는 기존 per-run JSON directory 를 입력으로 받고 별도 summary JSON 파일을 출력한다.
        // 아직 Program wiring 전이라도 parser 가 command 와 두 경로를 정확히 보존해야 이후 실행 단위가 흔들리지 않는다.
        [Fact]
        public void TryParse_WhenSummarizeBaselineHasInputAndSummary_ReturnsSummaryCommand()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--summarize-baseline", "docs/baseline", "--summary", "out/summary.json" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.Null(errorMessage);
            Assert.Equal("SummarizeBaseline", commandLine.Command.ToString());
            Assert.Equal("docs/baseline", GetStringProperty(commandLine, "SummaryInputDirectory"));
            Assert.Equal("out/summary.json", GetStringProperty(commandLine, "SummaryOutputPath"));
            Assert.Null(commandLine.ReportPath);
            Assert.Null(commandLine.BaselineOutputDirectory);
            Assert.Equal(0, commandLine.BaselineRunCount);
        }

        // Markdown summary 는 JSON summary 를 대체하지 않는 선택 보조 artifact 다.
        // parser 가 markdown path 를 보존해야 Program wiring 이 JSON 생성 뒤 같은 summary object 로 .md 를 추가 출력할 수 있다.
        [Fact]
        public void TryParse_WhenSummarizeBaselineHasSummaryMarkdown_ReturnsSummaryCommandWithMarkdownPath()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--summarize-baseline", "docs/baseline", "--summary", "out/summary.json", "--summary-md", "out/summary.md" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.Null(errorMessage);
            Assert.Equal("SummarizeBaseline", commandLine.Command.ToString());
            Assert.Equal("docs/baseline", GetStringProperty(commandLine, "SummaryInputDirectory"));
            Assert.Equal("out/summary.json", GetStringProperty(commandLine, "SummaryOutputPath"));
            Assert.Equal("out/summary.md", GetStringProperty(commandLine, "SummaryMarkdownOutputPath"));
        }

        // --summary-md 는 선택 옵션이지만 지정했다면 파일 경로가 반드시 필요하다.
        // 경로 없이 통과시키면 사용자는 Markdown report 가 생긴다고 믿지만 Program 은 출력 위치를 알 수 없다.
        [Fact]
        public void TryParse_WhenSummarizeBaselineSummaryMarkdownMissingPath_ReturnsUsageError()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--summarize-baseline", "docs/baseline", "--summary", "out/summary.json", "--summary-md" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.NotNull(errorMessage);
            Assert.Equal("SummarizeBaseline", commandLine.Command.ToString());
        }

        // summary command 는 output directory command 가 아니므로 --summary 파일 경로가 반드시 필요하다.
        // 이 검증이 없으면 사용자는 summary 파일이 생겼다고 생각하지만 실제로는 usage error 없이 다른 경로로 흐를 수 있다.
        [Fact]
        public void TryParse_WhenSummarizeBaselineMissingSummary_ReturnsUsageError()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--summarize-baseline", "docs/baseline" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.NotNull(errorMessage);
            Assert.Equal("SummarizeBaseline", commandLine.Command.ToString());
        }

        // --report 는 단일 runner raw JSON 출력용이고, summary command 의 출력은 --summary 로만 지정한다.
        // 두 옵션을 섞으면 입력/출력 artifact 의 의미가 불명확해지므로 parser 단계에서 막는다.
        [Fact]
        public void TryParse_WhenSummarizeBaselineHasReport_ReturnsUsageError()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--summarize-baseline", "docs/baseline", "--report", "out/report.json" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.NotNull(errorMessage);
            Assert.Equal("SummarizeBaseline", commandLine.Command.ToString());
        }

        private static string? GetStringProperty(BenchmarkCommandLine commandLine, string propertyName)
        {
            PropertyInfo? property = typeof(BenchmarkCommandLine).GetProperty(propertyName);
            Assert.NotNull(property);
            return (string?)property!.GetValue(commandLine, null);
        }
    }
}
