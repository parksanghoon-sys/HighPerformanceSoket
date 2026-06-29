using System.Collections.Generic;

namespace Hps.Benchmarks
{
    internal sealed class BaselineEnvelopeSource
    {
        public BaselineEnvelopeSource(
            BaselineEnvelopeSourceKind kind,
            string sourcePath,
            IReadOnlyList<BaselineEnvelopeSummary> summaries,
            BaselineComparisonResult comparison)
        {
            Kind = kind;
            SourcePath = sourcePath;
            Summaries = summaries;
            Comparison = comparison;
        }

        public BaselineEnvelopeSourceKind Kind { get; }

        public string SourcePath { get; }

        public IReadOnlyList<BaselineEnvelopeSummary> Summaries { get; }

        public BaselineComparisonResult Comparison { get; }
    }
}
