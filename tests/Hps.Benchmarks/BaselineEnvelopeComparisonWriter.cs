using System;
using System.IO;
using System.Text.Json;

namespace Hps.Benchmarks
{
    internal static class BaselineEnvelopeComparisonWriter
    {
        public static void Write(string path, BaselineEnvelopeComparison comparison)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("envelope output path 는 비어 있을 수 없습니다.", nameof(path));

            if (comparison == null)
                throw new ArgumentNullException(nameof(comparison));

            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (FileStream stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                JsonWriterOptions options = new JsonWriterOptions
                {
                    Indented = true
                };

                using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, options))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("envelope-version", 1);
                    writer.WriteString("reference-history-path", comparison.ReferenceSourcePath);
                    writer.WriteString("candidate-path", comparison.CandidateSourcePath);
                    writer.WriteString("candidate-kind", FormatSourceKind(comparison.CandidateKind));
                    writer.WriteNumber("reference-summary-count", comparison.ReferenceSummaryCount);
                    writer.WriteNumber("candidate-summary-count", comparison.CandidateSummaryCount);
                    writer.WriteBoolean("envelope-compatible", comparison.EnvelopeCompatible);
                    writer.WriteNumber("envelope-signal-count", comparison.SignalCount);
                    WriteComparisonKey(writer, "reference-key", comparison.ReferenceKey);
                    WriteComparisonKey(writer, "candidate-key", comparison.CandidateKey);
                    WriteMismatches(writer, comparison);
                    WriteByKind(writer, comparison);
                    WriteSignals(writer, comparison);
                    writer.WriteEndObject();
                }
            }
        }

        private static void WriteComparisonKey(Utf8JsonWriter writer, string propertyName, BaselineComparisonKey? key)
        {
            writer.WritePropertyName(propertyName);
            if (key == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();
            writer.WriteString("benchmark-profile", key.BenchmarkProfile);
            writer.WriteString("runner-id", key.RunnerId);
            writer.WriteString("runner-kind", key.RunnerKind);
            writer.WriteString("transport-backend", key.TransportBackend);
            writer.WriteString("os-description", key.OsDescription);
            writer.WriteString("os-architecture", key.OsArchitecture);
            writer.WriteString("process-architecture", key.ProcessArchitecture);
            writer.WriteString("framework-description", key.FrameworkDescription);
            writer.WritePropertyName("cases");
            writer.WriteStartArray();
            for (int i = 0; i < key.Cases.Count; i++)
            {
                BaselineComparisonCase runCase = key.Cases[i];
                writer.WriteStartObject();
                writer.WriteString("result-name", runCase.ResultName);
                writer.WriteString("scenario", runCase.Scenario);
                writer.WriteNumber("payload-bytes", runCase.PayloadBytes);
                writer.WriteNumber("target-rate-hz", runCase.TargetRateHz);
                writer.WriteNumber("target-duration-seconds", runCase.TargetDurationSeconds);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static void WriteMismatches(Utf8JsonWriter writer, BaselineEnvelopeComparison comparison)
        {
            writer.WritePropertyName("envelope-mismatches");
            writer.WriteStartArray();
            for (int i = 0; i < comparison.Mismatches.Count; i++)
            {
                BaselineEnvelopeMismatch mismatch = comparison.Mismatches[i];
                writer.WriteStartObject();
                writer.WriteString("code", mismatch.Code);
                writer.WriteString("field", mismatch.Field);
                writer.WriteString("expected", mismatch.Expected);
                writer.WriteString("actual", mismatch.Actual);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        private static void WriteByKind(Utf8JsonWriter writer, BaselineEnvelopeComparison comparison)
        {
            // metric 이름을 property key 로 써서 JSON 소비자가 kind/metric 조합을 dictionary 처럼 바로 조회할 수 있게 한다.
            writer.WritePropertyName("by-kind");
            writer.WriteStartObject();
            for (int i = 0; i < comparison.Kinds.Count; i++)
            {
                BaselineEnvelopeKindComparison kind = comparison.Kinds[i];
                writer.WritePropertyName(kind.Kind);
                writer.WriteStartObject();
                for (int metricIndex = 0; metricIndex < kind.Metrics.Count; metricIndex++)
                {
                    BaselineEnvelopeMetricComparison metric = kind.Metrics[metricIndex];
                    writer.WritePropertyName(metric.Metric);
                    writer.WriteStartObject();
                    writer.WriteString("direction", metric.Direction);
                    writer.WriteNumber("reference", metric.Reference);
                    writer.WriteNumber("limit", metric.Limit);
                    writer.WriteNumber("candidate", metric.Candidate);
                    writer.WriteBoolean("signaled", metric.Signaled);
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }

        private static void WriteSignals(Utf8JsonWriter writer, BaselineEnvelopeComparison comparison)
        {
            writer.WritePropertyName("signals");
            writer.WriteStartArray();
            for (int i = 0; i < comparison.Signals.Count; i++)
            {
                BaselineEnvelopeSignal signal = comparison.Signals[i];
                writer.WriteStartObject();
                writer.WriteString("code", CreateSignalCode(signal.Direction));
                writer.WriteString("kind", signal.Kind);
                writer.WriteString("metric", signal.Metric);
                writer.WriteString("direction", signal.Direction);
                writer.WriteNumber("reference", signal.Reference);
                writer.WriteNumber("limit", signal.Limit);
                writer.WriteNumber("candidate", signal.Candidate);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        private static string FormatSourceKind(BaselineEnvelopeSourceKind kind)
        {
            return kind == BaselineEnvelopeSourceKind.History ? "history" : "summary";
        }

        private static string CreateSignalCode(string direction)
        {
            return string.Equals(direction, "lower", StringComparison.Ordinal)
                ? "envelope-lower-bound-exceeded"
                : "envelope-upper-bound-exceeded";
        }
    }
}
