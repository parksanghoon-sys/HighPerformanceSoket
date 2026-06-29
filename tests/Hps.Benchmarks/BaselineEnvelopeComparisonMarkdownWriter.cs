using System;
using System.Globalization;
using System.IO;

namespace Hps.Benchmarks
{
    internal static class BaselineEnvelopeComparisonMarkdownWriter
    {
        public static void Write(TextWriter writer, BaselineEnvelopeComparison comparison)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            if (comparison == null)
                throw new ArgumentNullException(nameof(comparison));

            writer.WriteLine("# Baseline Envelope Comparison");
            writer.WriteLine();
            writer.WriteLine("- reference: `{0}`", comparison.ReferenceSourcePath);
            writer.WriteLine("- candidate: `{0}`", comparison.CandidateSourcePath);
            writer.WriteLine("- envelope-compatible: {0}", comparison.EnvelopeCompatible ? "true" : "false");
            writer.WriteLine("- envelope-signal-count: {0}", comparison.SignalCount.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine();
            WriteKey(writer, "Reference Key", comparison.ReferenceKey);
            WriteKey(writer, "Candidate Key", comparison.CandidateKey);
            WriteMetrics(writer, comparison);
            WriteMismatches(writer, comparison);
            WriteSignals(writer, comparison);
        }

        private static void WriteKey(TextWriter writer, string title, BaselineComparisonKey? key)
        {
            writer.WriteLine("## {0}", title);
            writer.WriteLine();
            if (key == null)
            {
                writer.WriteLine("- 없음");
                writer.WriteLine();
                return;
            }

            writer.WriteLine("- benchmark-profile: {0}", key.BenchmarkProfile);
            writer.WriteLine("- runner-id: {0}", key.RunnerId);
            writer.WriteLine("- runner-kind: {0}", key.RunnerKind);
            writer.WriteLine("- transport-backend: {0}", key.TransportBackend);
            writer.WriteLine();
        }

        private static void WriteMetrics(TextWriter writer, BaselineEnvelopeComparison comparison)
        {
            writer.WriteLine("## Metrics");
            writer.WriteLine();
            writer.WriteLine("| kind | metric | direction | reference | limit | candidate | signaled |");
            writer.WriteLine("| --- | --- | --- | ---: | ---: | ---: | --- |");
            for (int i = 0; i < comparison.Kinds.Count; i++)
            {
                BaselineEnvelopeKindComparison kind = comparison.Kinds[i];
                for (int metricIndex = 0; metricIndex < kind.Metrics.Count; metricIndex++)
                {
                    BaselineEnvelopeMetricComparison metric = kind.Metrics[metricIndex];
                    writer.WriteLine(
                        "| {0} | {1} | {2} | {3} | {4} | {5} | {6} |",
                        kind.Kind,
                        metric.Metric,
                        metric.Direction,
                        Format(metric.Reference),
                        Format(metric.Limit),
                        Format(metric.Candidate),
                        metric.Signaled ? "true" : "false");
                }
            }
            writer.WriteLine();
        }

        private static void WriteMismatches(TextWriter writer, BaselineEnvelopeComparison comparison)
        {
            writer.WriteLine("## Mismatches");
            writer.WriteLine();
            if (comparison.Mismatches.Count == 0)
            {
                writer.WriteLine("- 없음");
                writer.WriteLine();
                return;
            }

            writer.WriteLine("| code | field | expected | actual |");
            writer.WriteLine("| --- | --- | --- | --- |");
            for (int i = 0; i < comparison.Mismatches.Count; i++)
            {
                BaselineEnvelopeMismatch mismatch = comparison.Mismatches[i];
                writer.WriteLine("| {0} | {1} | {2} | {3} |", mismatch.Code, mismatch.Field, mismatch.Expected, mismatch.Actual);
            }
            writer.WriteLine();
        }

        private static void WriteSignals(TextWriter writer, BaselineEnvelopeComparison comparison)
        {
            writer.WriteLine("## Signals");
            writer.WriteLine();
            if (comparison.Signals.Count == 0)
            {
                writer.WriteLine("- 없음");
                writer.WriteLine();
                return;
            }

            writer.WriteLine("| kind | metric | direction | limit | candidate |");
            writer.WriteLine("| --- | --- | --- | ---: | ---: |");
            for (int i = 0; i < comparison.Signals.Count; i++)
            {
                BaselineEnvelopeSignal signal = comparison.Signals[i];
                writer.WriteLine(
                    "| {0} | {1} | {2} | {3} | {4} |",
                    signal.Kind,
                    signal.Metric,
                    signal.Direction,
                    Format(signal.Limit),
                    Format(signal.Candidate));
            }
            writer.WriteLine();
        }

        private static string Format(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
