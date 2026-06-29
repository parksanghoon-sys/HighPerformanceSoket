namespace Hps.Benchmarks
{
    internal sealed class BaselineEnvelopeSummary
    {
        public BaselineEnvelopeSummary(
            string summaryPath,
            int sourceReportCount,
            bool hardPassed,
            int warningCount,
            BaselineKindSummary? load,
            BaselineKindSummary? openLoop,
            BaselineComparisonResult comparison)
        {
            SummaryPath = summaryPath;
            SourceReportCount = sourceReportCount;
            HardPassed = hardPassed;
            WarningCount = warningCount;
            Load = load;
            OpenLoop = openLoop;
            Comparison = comparison;
        }

        public string SummaryPath { get; }

        public int SourceReportCount { get; }

        public bool HardPassed { get; }

        public int WarningCount { get; }

        public BaselineKindSummary? Load { get; }

        public BaselineKindSummary? OpenLoop { get; }

        public BaselineComparisonResult Comparison { get; }
    }
}
