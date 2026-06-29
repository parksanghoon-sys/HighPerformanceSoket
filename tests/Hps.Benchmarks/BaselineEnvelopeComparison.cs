using System.Collections.Generic;

namespace Hps.Benchmarks
{
    internal sealed class BaselineEnvelopeComparison
    {
        public BaselineEnvelopeComparison(
            string referenceSourcePath,
            string candidateSourcePath,
            bool envelopeCompatible,
            BaselineComparisonKey? referenceKey,
            BaselineComparisonKey? candidateKey,
            IReadOnlyList<BaselineEnvelopeKindComparison> kinds,
            IReadOnlyList<BaselineEnvelopeMismatch> mismatches,
            IReadOnlyList<BaselineEnvelopeSignal> signals)
        {
            ReferenceSourcePath = referenceSourcePath;
            CandidateSourcePath = candidateSourcePath;
            EnvelopeCompatible = envelopeCompatible;
            ReferenceKey = referenceKey;
            CandidateKey = candidateKey;
            Kinds = kinds;
            Mismatches = mismatches;
            Signals = signals;
        }

        public string ReferenceSourcePath { get; }

        public string CandidateSourcePath { get; }

        public bool EnvelopeCompatible { get; }

        public BaselineComparisonKey? ReferenceKey { get; }

        public BaselineComparisonKey? CandidateKey { get; }

        public IReadOnlyList<BaselineEnvelopeKindComparison> Kinds { get; }

        public IReadOnlyList<BaselineEnvelopeMismatch> Mismatches { get; }

        public IReadOnlyList<BaselineEnvelopeSignal> Signals { get; }

        public int SignalCount
        {
            get { return Signals.Count; }
        }
    }
}
