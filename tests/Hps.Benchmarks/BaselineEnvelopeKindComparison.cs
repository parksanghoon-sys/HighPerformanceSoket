using System.Collections.Generic;

namespace Hps.Benchmarks
{
    internal sealed class BaselineEnvelopeKindComparison
    {
        public BaselineEnvelopeKindComparison(string kind, IReadOnlyList<BaselineEnvelopeMetricComparison> metrics)
        {
            Kind = kind;
            Metrics = metrics;
        }

        public string Kind { get; }

        public IReadOnlyList<BaselineEnvelopeMetricComparison> Metrics { get; }
    }
}
