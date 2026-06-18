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
            string? summaryOutputPath)
        {
            Command = command;
            ReportPath = reportPath;
            BaselineOutputDirectory = baselineOutputDirectory;
            BaselineRunCount = baselineRunCount;
            SummaryInputDirectory = summaryInputDirectory;
            SummaryOutputPath = summaryOutputPath;
        }

        public BenchmarkCommand Command { get; }

        public string? ReportPath { get; }

        public string? BaselineOutputDirectory { get; }

        public int BaselineRunCount { get; }

        public string? SummaryInputDirectory { get; }

        public string? SummaryOutputPath { get; }
    }
}
