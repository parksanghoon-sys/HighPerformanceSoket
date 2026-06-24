using System.Collections.Generic;

namespace Hps.Benchmarks
{
    internal sealed class BaselineHistory
    {
        public BaselineHistory(
            string sourceRoot,
            IReadOnlyList<BaselineHistorySession> sessions,
            bool hardPassed,
            int failedSessionCount,
            int warningCount,
            BaselineComparisonResult? comparison = null)
        {
            SourceRoot = sourceRoot;
            Sessions = sessions;
            HardPassed = hardPassed;
            FailedSessionCount = failedSessionCount;
            WarningCount = warningCount;
            Comparison = comparison ?? new BaselineComparisonResult(false, null, 0, new BaselineComparisonMismatch[0]);
        }

        public string SourceRoot { get; }

        public IReadOnlyList<BaselineHistorySession> Sessions { get; }

        public int SessionCount
        {
            get { return Sessions.Count; }
        }

        public bool HardPassed { get; }

        public int FailedSessionCount { get; }

        public int WarningCount { get; }

        public BaselineComparisonResult Comparison { get; }
    }
}
