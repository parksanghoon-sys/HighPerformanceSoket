using System.Collections.Generic;

namespace Hps.Benchmarks
{
    internal sealed class BaselineSummary
    {
        public BaselineSummary(
            string sourceDirectory,
            int sourceReportCount,
            bool hardPassed,
            int hardFailureCount,
            IReadOnlyList<BaselineWarning> warnings,
            BaselineKindSummary? load,
            BaselineKindSummary? openLoop,
            BaselineComparisonResult? comparison = null)
        {
            SourceDirectory = sourceDirectory;
            SourceReportCount = sourceReportCount;
            HardPassed = hardPassed;
            HardFailureCount = hardFailureCount;
            Warnings = warnings;
            Load = load;
            OpenLoop = openLoop;
            Comparison = comparison ?? new BaselineComparisonResult(false, null, 0, new BaselineComparisonMismatch[0]);
        }

        public string SourceDirectory { get; }

        public int SourceReportCount { get; }

        public bool HardPassed { get; }

        public int HardFailureCount { get; }

        public IReadOnlyList<BaselineWarning> Warnings { get; }

        public int WarningCount
        {
            get { return Warnings.Count; }
        }

        public BaselineKindSummary? Load { get; }

        public BaselineKindSummary? OpenLoop { get; }

        public BaselineComparisonResult Comparison { get; }
    }
}
