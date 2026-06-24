using System;
using System.IO;
using System.Text.Json;

namespace Hps.Benchmarks
{
    internal static class BaselineHistoryWriter
    {
        public static void Write(string path, BaselineHistory history)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("history output path 는 비어 있을 수 없습니다.", nameof(path));

            if (history == null)
                throw new ArgumentNullException(nameof(history));

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
                    writer.WriteNumber("history-version", 1);
                    writer.WriteString("source-root", history.SourceRoot);
                    writer.WriteNumber("session-count", history.SessionCount);
                    writer.WriteBoolean("hard-passed", history.HardPassed);
                    writer.WriteNumber("failed-session-count", history.FailedSessionCount);
                    writer.WriteNumber("warning-count", history.WarningCount);
                    WriteComparison(writer, history.Comparison);
                    writer.WritePropertyName("sessions");
                    writer.WriteStartArray();
                    for (int i = 0; i < history.Sessions.Count; i++)
                        WriteSession(writer, history.Sessions[i]);
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
            }
        }

        private static void WriteSession(Utf8JsonWriter writer, BaselineHistorySession session)
        {
            writer.WriteStartObject();
            writer.WriteString("date", session.Date);
            writer.WriteString("session", session.Session);
            writer.WriteString("summary-path", session.SummaryPath);
            if (session.HumanReportPath == null)
                writer.WriteNull("human-report-path");
            else
                writer.WriteString("human-report-path", session.HumanReportPath);

            writer.WriteNumber("source-report-count", session.SourceReportCount);
            writer.WriteBoolean("hard-passed", session.HardPassed);
            writer.WriteNumber("warning-count", session.WarningCount);
            WriteNullableDouble(writer, "load-p99-max-us", session.LoadP99MaxMicroseconds);
            WriteNullableDouble(writer, "open-loop-p99-max-us", session.OpenLoopP99MaxMicroseconds);
            writer.WriteNumber("tcp-hwm-max", session.TcpHighWatermarkMax);
            writer.WriteBoolean("comparison-compatible", session.Comparison.Compatible);
            writer.WriteNumber("unknown-runner-count", session.Comparison.UnknownRunnerCount);
            writer.WriteNumber("comparison-mismatch-count", session.Comparison.MismatchCount);
            writer.WriteEndObject();
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

        private static void WriteNullableDouble(Utf8JsonWriter writer, string propertyName, double? value)
        {
            if (value.HasValue)
                writer.WriteNumber(propertyName, value.Value);
            else
                writer.WriteNull(propertyName);
        }
    }
}
