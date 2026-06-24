using System.Collections.Generic;

namespace Hps.Benchmarks
{
    internal sealed class BaselineComparisonResult
    {
        public BaselineComparisonResult(
            bool compatible,
            BaselineComparisonKey? key,
            int unknownRunnerCount,
            IReadOnlyList<BaselineComparisonMismatch> mismatches)
        {
            Compatible = compatible;
            Key = key;
            UnknownRunnerCount = unknownRunnerCount;
            Mismatches = mismatches;
        }

        public bool Compatible { get; }

        public BaselineComparisonKey? Key { get; }

        public int UnknownRunnerCount { get; }

        public IReadOnlyList<BaselineComparisonMismatch> Mismatches { get; }

        public int MismatchCount
        {
            get { return Mismatches.Count; }
        }
    }
}
