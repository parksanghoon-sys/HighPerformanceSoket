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

        // backend selector 는 benchmark 내부 비교용 옵션이다.
        // --load 에서 RIO를 명시하면 default factory 를 바꾸지 않고도 다음 runner 단계가 RioTransport 를 선택할 수 있어야 한다.
        [Fact]
        public void TryParse_WhenLoadHasRioBackendAndReport_ReturnsLoadCommandWithBackend()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--load", "--backend", "rio", "--report", "out/rio-load.json" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.Null(errorMessage);
            Assert.Equal(BenchmarkCommand.Load, commandLine.Command);
            Assert.Equal("out/rio-load.json", commandLine.ReportPath);
            Assert.Equal("Rio", GetRequiredProperty(commandLine, "TransportBackend")!.ToString());
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

        // baseline suite 도 backend 별 raw report 묶음을 만들 수 있어야 SAEA/RIO 반복 측정 directory 를 분리할 수 있다.
        // 여기서는 parser 가 선택한 backend 를 suite runner 까지 전달할 최소 상태를 보존하는지만 검증한다.
        [Fact]
        public void TryParse_WhenBaselineSuiteHasRioBackendAndRuns_ReturnsBaselineSuiteCommandWithBackend()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--baseline-suite", "out/rio-baseline", "--runs", "2", "--backend", "rio" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.Null(errorMessage);
            Assert.Equal(BenchmarkCommand.BaselineSuite, commandLine.Command);
            Assert.Equal("out/rio-baseline", commandLine.BaselineOutputDirectory);
            Assert.Equal(2, commandLine.BaselineRunCount);
            Assert.Equal("Rio", GetRequiredProperty(commandLine, "TransportBackend")!.ToString());
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

        // summary 는 이미 raw report 의 transport-backend 를 읽는 aggregate 단계다.
        // 여기서 --backend 를 다시 받으면 입력 artifact 와 실행 backend 개념이 섞이므로 usage error 로 막아야 한다.
        [Fact]
        public void TryParse_WhenSummarizeBaselineHasBackend_ReturnsUsageError()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--summarize-baseline", "docs/baseline", "--summary", "out/summary.json", "--backend", "rio" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.NotNull(errorMessage);
            Assert.Equal("SummarizeBaseline", commandLine.Command.ToString());
        }

        // history command 는 여러 session summary 를 읽는 별도 aggregate command 이다.
        // parser 가 input root 와 JSON output path 를 보존해야 후속 reader/writer 단계가 같은 계약에 붙을 수 있다.
        [Fact]
        public void TryParse_WhenSummarizeBaselineHistoryHasInputAndHistory_ReturnsHistoryCommand()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--summarize-baseline-history", "docs/baselines", "--history", "out/history.json" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.Null(errorMessage);
            Assert.Equal("SummarizeBaselineHistory", commandLine.Command.ToString());
            Assert.Equal("docs/baselines", GetStringProperty(commandLine, "HistoryInputRoot"));
            Assert.Equal("out/history.json", GetStringProperty(commandLine, "HistoryOutputPath"));
            Assert.Null(GetStringProperty(commandLine, "HistoryMarkdownOutputPath"));
            Assert.Null(commandLine.ReportPath);
            Assert.Null(commandLine.BaselineOutputDirectory);
        }

        // Markdown history 는 JSON history 를 대체하지 않는 선택 보조 artifact 다.
        // 두 output path 를 분리해 보존해야 Program wiring 때 같은 aggregate 결과로 두 파일을 쓸 수 있다.
        [Fact]
        public void TryParse_WhenSummarizeBaselineHistoryHasMarkdown_ReturnsHistoryCommandWithMarkdownPath()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--summarize-baseline-history", "docs/baselines", "--history", "out/history.json", "--history-md", "out/history.md" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.Null(errorMessage);
            Assert.Equal("SummarizeBaselineHistory", commandLine.Command.ToString());
            Assert.Equal("docs/baselines", GetStringProperty(commandLine, "HistoryInputRoot"));
            Assert.Equal("out/history.json", GetStringProperty(commandLine, "HistoryOutputPath"));
            Assert.Equal("out/history.md", GetStringProperty(commandLine, "HistoryMarkdownOutputPath"));
        }

        // history command 는 output directory command 가 아니므로 --history JSON 파일 경로가 반드시 필요하다.
        // 이 경계가 없으면 사용자가 history artifact 를 기대했는데 아무 파일도 생기지 않는 usage 오류가 생긴다.
        [Fact]
        public void TryParse_WhenSummarizeBaselineHistoryMissingHistory_ReturnsUsageError()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--summarize-baseline-history", "docs/baselines" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.NotNull(errorMessage);
            Assert.Equal("SummarizeBaselineHistory", commandLine.Command.ToString());
        }

        // --history-md 는 선택 옵션이지만 지정했다면 파일 경로가 반드시 필요하다.
        // 경로 없이 통과시키면 Markdown history 를 쓴다고 보고하면서 실제 출력 위치를 잃는다.
        [Fact]
        public void TryParse_WhenSummarizeBaselineHistoryMarkdownMissingPath_ReturnsUsageError()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--summarize-baseline-history", "docs/baselines", "--history", "out/history.json", "--history-md" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.NotNull(errorMessage);
            Assert.Equal("SummarizeBaselineHistory", commandLine.Command.ToString());
        }

        // --report 는 단일 runner raw JSON 출력용이고, history command 의 출력은 --history 로만 지정한다.
        // 두 옵션을 섞으면 raw report 와 aggregate history 의미가 충돌하므로 parser 단계에서 막는다.
        [Fact]
        public void TryParse_WhenSummarizeBaselineHistoryHasReport_ReturnsUsageError()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--summarize-baseline-history", "docs/baselines", "--report", "out/report.json" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.NotNull(errorMessage);
            Assert.Equal("SummarizeBaselineHistory", commandLine.Command.ToString());
        }

        private static string? GetStringProperty(BenchmarkCommandLine commandLine, string propertyName)
        {
            PropertyInfo? property = typeof(BenchmarkCommandLine).GetProperty(propertyName);
            Assert.NotNull(property);
            return (string?)property!.GetValue(commandLine, null);
        }

        private static object? GetRequiredProperty(BenchmarkCommandLine commandLine, string propertyName)
        {
            PropertyInfo? property = typeof(BenchmarkCommandLine).GetProperty(propertyName);
            Assert.NotNull(property);
            return property!.GetValue(commandLine, null);
        }
    }
}
