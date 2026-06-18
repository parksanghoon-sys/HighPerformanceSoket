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
            BaselineKindSummary? openLoop)
        {
            SourceDirectory = sourceDirectory;
            SourceReportCount = sourceReportCount;
            HardPassed = hardPassed;
            HardFailureCount = hardFailureCount;
            Warnings = warnings;
            Load = load;
            OpenLoop = openLoop;
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
    }
}
