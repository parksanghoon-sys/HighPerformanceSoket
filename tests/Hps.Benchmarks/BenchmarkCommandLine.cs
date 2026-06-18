namespace Hps.Benchmarks
{
    internal sealed class BenchmarkCommandLine
    {
        public BenchmarkCommandLine(
            BenchmarkCommand command,
            string? reportPath,
            string? baselineOutputDirectory,
            int baselineRunCount,
            string? summaryInputDirectory,
            string? summaryOutputPath,
            string? summaryMarkdownOutputPath)
        {
            Command = command;
            ReportPath = reportPath;
            BaselineOutputDirectory = baselineOutputDirectory;
            BaselineRunCount = baselineRunCount;
            SummaryInputDirectory = summaryInputDirectory;
            SummaryOutputPath = summaryOutputPath;
            SummaryMarkdownOutputPath = summaryMarkdownOutputPath;
        }

        public BenchmarkCommand Command { get; }

        public string? ReportPath { get; }

        public string? BaselineOutputDirectory { get; }

        public int BaselineRunCount { get; }

        public string? SummaryInputDirectory { get; }

        public string? SummaryOutputPath { get; }

        public string? SummaryMarkdownOutputPath { get; }
    }
}
