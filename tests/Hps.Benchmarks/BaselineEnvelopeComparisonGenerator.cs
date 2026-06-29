using System;
using System.Collections.Generic;
using System.Globalization;

namespace Hps.Benchmarks
{
    internal static class BaselineEnvelopeComparisonGenerator
    {
        // 비교는 먼저 source/key compatibility 를 확정한 뒤 metric 계산으로 들어간다.
        // key 가 다른 artifact 를 숫자로 비교하면 빠른 candidate 도 잘못된 regression/noise 로 기록될 수 있다.
        public static BaselineEnvelopeComparison Generate(BaselineEnvelopeSource reference, BaselineEnvelopeSource candidate)
        {
            if (reference == null)
                throw new ArgumentNullException(nameof(reference));

            if (candidate == null)
                throw new ArgumentNullException(nameof(candidate));

            BaselineComparisonKey? referenceKey = reference.Comparison.Key;
            BaselineComparisonKey? candidateKey = candidate.Comparison.Key;
            List<BaselineEnvelopeMismatch> mismatches = new List<BaselineEnvelopeMismatch>();
            List<BaselineEnvelopeKindComparison> kinds = new List<BaselineEnvelopeKindComparison>();
            List<BaselineEnvelopeSignal> signals = new List<BaselineEnvelopeSignal>();

            if (reference.Kind != BaselineEnvelopeSourceKind.History)
                mismatches.Add(new BaselineEnvelopeMismatch("reference-not-history", "reference-kind", "History", reference.Kind.ToString()));

            if (!reference.Comparison.Compatible || referenceKey == null)
                mismatches.Add(new BaselineEnvelopeMismatch("reference-comparison-incompatible", "reference-comparison-compatible", "true", reference.Comparison.Compatible.ToString()));

            if (!candidate.Comparison.Compatible || candidateKey == null)
                mismatches.Add(new BaselineEnvelopeMismatch("candidate-comparison-incompatible", "candidate-comparison-compatible", "true", candidate.Comparison.Compatible.ToString()));

            if (mismatches.Count == 0)
            {
                BaselineEnvelopeMismatch? keyMismatch = FindFirstKeyMismatch(referenceKey!, candidateKey!);
                if (keyMismatch != null)
                    mismatches.Add(keyMismatch);
            }

            if (mismatches.Count == 0)
            {
                List<BaselineEnvelopeSummary> eligibleReferenceSummaries = GetEligibleReferenceSummaries(reference, referenceKey!);
                if (eligibleReferenceSummaries.Count == 0)
                {
                    mismatches.Add(new BaselineEnvelopeMismatch("reference-no-eligible-summaries", "reference-summaries", ">=1", "0"));
                }
                else
                {
                    AddKindComparison("load", eligibleReferenceSummaries, candidate.Summaries, mismatches, kinds, signals);
                    AddKindComparison("open-loop", eligibleReferenceSummaries, candidate.Summaries, mismatches, kinds, signals);
                }
            }

            return new BaselineEnvelopeComparison(
                reference.SourcePath,
                candidate.SourcePath,
                mismatches.Count == 0 && signals.Count == 0,
                referenceKey,
                candidateKey,
                candidate.Kind,
                reference.Summaries.Count,
                candidate.Summaries.Count,
                kinds,
                mismatches,
                signals);
        }

        private static List<BaselineEnvelopeSummary> GetEligibleReferenceSummaries(BaselineEnvelopeSource reference, BaselineComparisonKey referenceKey)
        {
            // reference envelope 는 hard gate 를 통과했고 같은 comparison key 를 가진 session summary 로만 만든다.
            // 실패 session 이나 legacy/incompatible summary 를 섞으면 limit 이 실제 기준보다 넓어져 regression signal 이 흐려진다.
            List<BaselineEnvelopeSummary> summaries = new List<BaselineEnvelopeSummary>();
            for (int i = 0; i < reference.Summaries.Count; i++)
            {
                BaselineEnvelopeSummary summary = reference.Summaries[i];
                if (!summary.HardPassed || !summary.Comparison.Compatible || summary.Comparison.Key == null)
                    continue;

                if (FindFirstKeyMismatch(referenceKey, summary.Comparison.Key) != null)
                    continue;

                summaries.Add(summary);
            }

            return summaries;
        }

        private static void AddKindComparison(
            string kind,
            IReadOnlyList<BaselineEnvelopeSummary> referenceSummaries,
            IReadOnlyList<BaselineEnvelopeSummary> candidateSummaries,
            List<BaselineEnvelopeMismatch> mismatches,
            List<BaselineEnvelopeKindComparison> kinds,
            List<BaselineEnvelopeSignal> signals)
        {
            // kind 자체가 빠진 경우는 metric 0으로 대체하지 않는다.
            // 0은 "매우 좋은 값"처럼 해석될 수 있으므로 explicit mismatch 로 중단하는 편이 artifact 신뢰성이 높다.
            List<BaselineKindSummary> referenceKinds = GetKindSummaries(referenceSummaries, kind);
            List<BaselineKindSummary> candidateKinds = GetKindSummaries(candidateSummaries, kind);

            if (referenceKinds.Count == 0)
            {
                mismatches.Add(new BaselineEnvelopeMismatch("reference-kind-missing", "kind", kind, "missing"));
                return;
            }

            if (candidateKinds.Count == 0)
            {
                mismatches.Add(new BaselineEnvelopeMismatch("candidate-kind-missing", "kind", kind, "missing"));
                return;
            }

            List<BaselineEnvelopeMetricComparison> metrics = new List<BaselineEnvelopeMetricComparison>();
            AddUpperMetric(kind, "p50-max-us", Max(referenceKinds, GetP50Max), Max(candidateKinds, GetP50Max), CreateLatencyUpperLimit, metrics, signals);
            AddUpperMetric(kind, "p50-median-us", Max(referenceKinds, GetP50Median), Max(candidateKinds, GetP50Median), CreateLatencyUpperLimit, metrics, signals);
            AddUpperMetric(kind, "p99-max-us", Max(referenceKinds, GetP99Max), Max(candidateKinds, GetP99Max), CreateLatencyUpperLimit, metrics, signals);
            AddUpperMetric(kind, "p99-median-us", Max(referenceKinds, GetP99Median), Max(candidateKinds, GetP99Median), CreateLatencyUpperLimit, metrics, signals);
            AddUpperMetric(kind, "p99-growth-ratio-max", Max(referenceKinds, GetP99GrowthRatioMax), Max(candidateKinds, GetP99GrowthRatioMax), CreateP99GrowthUpperLimit, metrics, signals);
            AddLowerMetric(kind, "actual-rate-min-hz", Min(referenceKinds, GetActualRateMin), Min(candidateKinds, GetActualRateMin), CreateActualRateLowerLimit, metrics, signals);
            AddUpperMetric(kind, "tcp-hwm-max", Max(referenceKinds, GetTcpHighWatermarkMax), Max(candidateKinds, GetTcpHighWatermarkMax), CreateTcpHighWatermarkUpperLimit, metrics, signals);
            AddUpperMetric(kind, "dropped-total", Max(referenceKinds, GetDroppedTotal), Max(candidateKinds, GetDroppedTotal), CreateZeroUpperLimit, metrics, signals);
            AddUpperMetric(kind, "payload-error-total", Max(referenceKinds, GetPayloadErrorTotal), Max(candidateKinds, GetPayloadErrorTotal), CreateZeroUpperLimit, metrics, signals);
            AddUpperMetric(kind, "pool-rented-max", Max(referenceKinds, GetPoolRentedMax), Max(candidateKinds, GetPoolRentedMax), CreateZeroUpperLimit, metrics, signals);
            kinds.Add(new BaselineEnvelopeKindComparison(kind, metrics));
        }

        private static void AddUpperMetric(
            string kind,
            string metric,
            double reference,
            double candidate,
            Func<double, double> createLimit,
            List<BaselineEnvelopeMetricComparison> metrics,
            List<BaselineEnvelopeSignal> signals)
        {
            double limit = createLimit(reference);
            bool signaled = candidate > limit;
            metrics.Add(new BaselineEnvelopeMetricComparison(metric, "upper", reference, limit, candidate, signaled));
            if (signaled)
                signals.Add(new BaselineEnvelopeSignal(kind, metric, "upper", reference, limit, candidate));
        }

        private static void AddLowerMetric(
            string kind,
            string metric,
            double reference,
            double candidate,
            Func<double, double> createLimit,
            List<BaselineEnvelopeMetricComparison> metrics,
            List<BaselineEnvelopeSignal> signals)
        {
            double limit = createLimit(reference);
            bool signaled = candidate < limit;
            metrics.Add(new BaselineEnvelopeMetricComparison(metric, "lower", reference, limit, candidate, signaled));
            if (signaled)
                signals.Add(new BaselineEnvelopeSignal(kind, metric, "lower", reference, limit, candidate));
        }

        private static List<BaselineKindSummary> GetKindSummaries(IReadOnlyList<BaselineEnvelopeSummary> summaries, string kind)
        {
            List<BaselineKindSummary> kinds = new List<BaselineKindSummary>();
            for (int i = 0; i < summaries.Count; i++)
            {
                BaselineKindSummary? summary = string.Equals(kind, "load", StringComparison.Ordinal)
                    ? summaries[i].Load
                    : summaries[i].OpenLoop;
                if (summary != null)
                    kinds.Add(summary);
            }

            return kinds;
        }

        private static double Max(IReadOnlyList<BaselineKindSummary> summaries, Func<BaselineKindSummary, double> selector)
        {
            double value = selector(summaries[0]);
            for (int i = 1; i < summaries.Count; i++)
                value = Math.Max(value, selector(summaries[i]));

            return value;
        }

        private static double Min(IReadOnlyList<BaselineKindSummary> summaries, Func<BaselineKindSummary, double> selector)
        {
            double value = selector(summaries[0]);
            for (int i = 1; i < summaries.Count; i++)
                value = Math.Min(value, selector(summaries[i]));

            return value;
        }

        private static BaselineEnvelopeMismatch? FindFirstKeyMismatch(BaselineComparisonKey expected, BaselineComparisonKey actual)
        {
            // mismatch 는 첫 차이만 기록한다. 이 command 는 gate 가 아니라 review artifact 이므로
            // 가장 먼저 비교를 무효화한 field 를 안정적으로 보여주는 것이 다량의 중복 mismatch 보다 유용하다.
            BaselineEnvelopeMismatch? mismatch =
                CompareString("benchmark-profile", expected.BenchmarkProfile, actual.BenchmarkProfile)
                ?? CompareString("runner-id", expected.RunnerId, actual.RunnerId)
                ?? CompareString("runner-kind", expected.RunnerKind, actual.RunnerKind)
                ?? CompareString("transport-backend", expected.TransportBackend, actual.TransportBackend)
                ?? CompareString("os-description", expected.OsDescription, actual.OsDescription)
                ?? CompareString("os-architecture", expected.OsArchitecture, actual.OsArchitecture)
                ?? CompareString("process-architecture", expected.ProcessArchitecture, actual.ProcessArchitecture)
                ?? CompareString("framework-description", expected.FrameworkDescription, actual.FrameworkDescription);
            if (mismatch != null)
                return mismatch;

            if (expected.Cases.Count != actual.Cases.Count)
            {
                return new BaselineEnvelopeMismatch(
                    "envelope-key-mismatch",
                    "cases.count",
                    expected.Cases.Count.ToString(CultureInfo.InvariantCulture),
                    actual.Cases.Count.ToString(CultureInfo.InvariantCulture));
            }

            for (int i = 0; i < expected.Cases.Count; i++)
            {
                BaselineComparisonCase expectedCase = expected.Cases[i];
                BaselineComparisonCase actualCase = actual.Cases[i];
                mismatch =
                    CompareString("cases[" + i.ToString(CultureInfo.InvariantCulture) + "].result-name", expectedCase.ResultName, actualCase.ResultName)
                    ?? CompareString("cases[" + i.ToString(CultureInfo.InvariantCulture) + "].scenario", expectedCase.Scenario, actualCase.Scenario)
                    ?? CompareInt("cases[" + i.ToString(CultureInfo.InvariantCulture) + "].payload-bytes", expectedCase.PayloadBytes, actualCase.PayloadBytes)
                    ?? CompareDouble("cases[" + i.ToString(CultureInfo.InvariantCulture) + "].target-rate-hz", expectedCase.TargetRateHz, actualCase.TargetRateHz)
                    ?? CompareInt("cases[" + i.ToString(CultureInfo.InvariantCulture) + "].target-duration-seconds", expectedCase.TargetDurationSeconds, actualCase.TargetDurationSeconds);
                if (mismatch != null)
                    return mismatch;
            }

            return null;
        }

        private static BaselineEnvelopeMismatch? CompareString(string field, string expected, string actual)
        {
            if (string.Equals(expected, actual, StringComparison.Ordinal))
                return null;

            return new BaselineEnvelopeMismatch("envelope-key-mismatch", field, expected, actual);
        }

        private static BaselineEnvelopeMismatch? CompareInt(string field, int expected, int actual)
        {
            if (expected == actual)
                return null;

            return new BaselineEnvelopeMismatch(
                "envelope-key-mismatch",
                field,
                expected.ToString(CultureInfo.InvariantCulture),
                actual.ToString(CultureInfo.InvariantCulture));
        }

        private static BaselineEnvelopeMismatch? CompareDouble(string field, double expected, double actual)
        {
            if (Math.Abs(expected - actual) < 0.000001)
                return null;

            return new BaselineEnvelopeMismatch(
                "envelope-key-mismatch",
                field,
                expected.ToString(CultureInfo.InvariantCulture),
                actual.ToString(CultureInfo.InvariantCulture));
        }

        private static double CreateLatencyUpperLimit(double reference)
        {
            return Math.Max(reference * 1.20, reference + 100.0);
        }

        private static double CreateP99GrowthUpperLimit(double reference)
        {
            return reference + 0.25;
        }

        private static double CreateActualRateLowerLimit(double reference)
        {
            return Math.Max(95.0, reference - 1.0);
        }

        private static double CreateTcpHighWatermarkUpperLimit(double reference)
        {
            return reference + 2.0;
        }

        private static double CreateZeroUpperLimit(double reference)
        {
            return 0.0;
        }

        private static double GetP50Max(BaselineKindSummary summary)
        {
            return summary.P50Max;
        }

        private static double GetP50Median(BaselineKindSummary summary)
        {
            return summary.P50Median;
        }

        private static double GetP99Max(BaselineKindSummary summary)
        {
            return summary.P99Max;
        }

        private static double GetP99Median(BaselineKindSummary summary)
        {
            return summary.P99Median;
        }

        private static double GetP99GrowthRatioMax(BaselineKindSummary summary)
        {
            return summary.P99GrowthRatioMax;
        }

        private static double GetActualRateMin(BaselineKindSummary summary)
        {
            return summary.ActualRateMin;
        }

        private static double GetTcpHighWatermarkMax(BaselineKindSummary summary)
        {
            return summary.TcpHighWatermarkMax;
        }

        private static double GetDroppedTotal(BaselineKindSummary summary)
        {
            return summary.DroppedTotal;
        }

        private static double GetPayloadErrorTotal(BaselineKindSummary summary)
        {
            return summary.PayloadErrorTotal;
        }

        private static double GetPoolRentedMax(BaselineKindSummary summary)
        {
            return summary.PoolRentedMax;
        }
    }
}
