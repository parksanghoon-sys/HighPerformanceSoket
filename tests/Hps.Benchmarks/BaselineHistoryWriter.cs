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
