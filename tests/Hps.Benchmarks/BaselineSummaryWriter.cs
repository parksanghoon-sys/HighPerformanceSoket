using System;
using System.IO;
using System.Text.Json;

namespace Hps.Benchmarks
{
    internal static class BaselineSummaryWriter
    {
        public static void Write(string path, BaselineSummary summary)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("summary output path 는 비어 있을 수 없습니다.", nameof(path));

            if (summary == null)
                throw new ArgumentNullException(nameof(summary));

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
                    writer.WriteNumber("summary-version", 1);
                    writer.WriteString("source-directory", summary.SourceDirectory);
                    writer.WriteNumber("source-report-count", summary.SourceReportCount);
                    writer.WriteBoolean("hard-passed", summary.HardPassed);
                    writer.WriteNumber("hard-failure-count", summary.HardFailureCount);
                    writer.WriteNumber("warning-count", summary.WarningCount);
                    WriteComparison(writer, summary.Comparison);
                    WriteWarnings(writer, summary);
                    WriteByKind(writer, summary);
                    writer.WriteEndObject();
                }
            }
        }

        private static void WriteComparison(Utf8JsonWriter writer, BaselineComparisonResult comparison)
        {
            writer.WriteBoolean("comparison-compatible", comparison.Compatible);
            WriteComparisonKey(writer, comparison.Key);
            writer.WriteNumber("unknown-runner-count", comparison.UnknownRunnerCount);
            writer.WriteNumber("comparison-mismatch-count", comparison.MismatchCount);
            writer.WritePropertyName("comparison-mismatches");
            writer.WriteStartArray();
            for (int i = 0; i < comparison.Mismatches.Count; i++)
                WriteComparisonMismatch(writer, comparison.Mismatches[i]);
            writer.WriteEndArray();
        }

        private static void WriteComparisonKey(Utf8JsonWriter writer, BaselineComparisonKey? key)
        {
            writer.WritePropertyName("comparison-key");
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
                WriteComparisonCase(writer, key.Cases[i]);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private static void WriteComparisonCase(Utf8JsonWriter writer, BaselineComparisonCase runCase)
        {
            writer.WriteStartObject();
            writer.WriteString("result-name", runCase.ResultName);
            writer.WriteString("scenario", runCase.Scenario);
            writer.WriteNumber("payload-bytes", runCase.PayloadBytes);
            writer.WriteNumber("target-rate-hz", runCase.TargetRateHz);
            writer.WriteNumber("target-duration-seconds", runCase.TargetDurationSeconds);
            writer.WriteEndObject();
        }

        private static void WriteComparisonMismatch(Utf8JsonWriter writer, BaselineComparisonMismatch mismatch)
        {
            writer.WriteStartObject();
            writer.WriteString("code", mismatch.Code);
            writer.WriteString("field", mismatch.Field);
            writer.WriteString("expected", mismatch.Expected);
            writer.WriteString("actual", mismatch.Actual);
            if (mismatch.SourcePath != null)
                writer.WriteString("source-path", mismatch.SourcePath);
            if (mismatch.Session != null)
                writer.WriteString("session", mismatch.Session);
            if (mismatch.SummaryPath != null)
                writer.WriteString("summary-path", mismatch.SummaryPath);
            writer.WriteEndObject();
        }

        private static void WriteWarnings(Utf8JsonWriter writer, BaselineSummary summary)
        {
            writer.WritePropertyName("warnings");
            writer.WriteStartArray();
            for (int i = 0; i < summary.Warnings.Count; i++)
            {
                BaselineWarning warning = summary.Warnings[i];
                writer.WriteStartObject();
                writer.WriteString("code", warning.Code);
                writer.WriteString("kind", warning.Kind);
                writer.WriteString("metric", warning.Metric);
                writer.WriteNumber("value", warning.Value);
                writer.WriteNumber("threshold", warning.Threshold);
                writer.WriteString("source-path", warning.SourcePath);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        private static void WriteByKind(Utf8JsonWriter writer, BaselineSummary summary)
        {
            writer.WritePropertyName("by-kind");
            writer.WriteStartObject();
            WriteKind(writer, "load", summary.Load);
            WriteKind(writer, "open-loop", summary.OpenLoop);
            writer.WriteEndObject();
        }

        private static void WriteKind(Utf8JsonWriter writer, string propertyName, BaselineKindSummary? kind)
        {
            writer.WritePropertyName(propertyName);
            if (kind == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();
            writer.WriteNumber("run-count", kind.RunCount);
            writer.WriteNumber("p50-min-us", kind.P50Min);
            writer.WriteNumber("p50-max-us", kind.P50Max);
            writer.WriteNumber("p50-median-us", kind.P50Median);
            writer.WriteNumber("p99-min-us", kind.P99Min);
            writer.WriteNumber("p99-max-us", kind.P99Max);
            writer.WriteNumber("p99-median-us", kind.P99Median);
            writer.WriteNumber("p99-growth-ratio-min", kind.P99GrowthRatioMin);
            writer.WriteNumber("p99-growth-ratio-max", kind.P99GrowthRatioMax);
            writer.WriteNumber("actual-rate-min-hz", kind.ActualRateMin);
            writer.WriteNumber("actual-rate-max-hz", kind.ActualRateMax);
            writer.WriteNumber("tcp-hwm-min", kind.TcpHighWatermarkMin);
            writer.WriteNumber("tcp-hwm-max", kind.TcpHighWatermarkMax);
            writer.WriteNumber("dropped-total", kind.DroppedTotal);
            writer.WriteNumber("payload-error-total", kind.PayloadErrorTotal);
            writer.WriteNumber("pool-rented-max", kind.PoolRentedMax);
            writer.WriteEndObject();
        }
    }
}
