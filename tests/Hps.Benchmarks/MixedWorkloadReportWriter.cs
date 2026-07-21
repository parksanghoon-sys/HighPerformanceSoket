using System;
using System.IO;
using System.Text.Json;

namespace Hps.Benchmarks
{
    /// <summary>
    /// mixed TCP workload 결과를 legacy baseline과 분리된 JSON 문서로 기록한다.
    /// report kind와 schema version을 별도로 사용해 기존 aggregate reader가 이 문서를 읽지 않게 한다.
    /// </summary>
    internal static class MixedWorkloadReportWriter
    {
        public static void Write(string path, MixedWorkloadRunResult result)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("mixed workload report 경로는 비어 있을 수 없습니다.", nameof(path));

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
                    writer.WriteString("report-kind", "mixed-tcp-workload");
                    writer.WriteNumber("schema-version", 2);
                    writer.WriteString("result-name", "mixed-load-open-loop");
                    writer.WriteBoolean("passed", result.Passed);
                    writer.WriteString("scenario", result.Scenario);
                    writer.WriteString("benchmark-profile", result.Identity.BenchmarkProfile);
                    writer.WriteString("runner-id", result.Identity.RunnerId);
                    writer.WriteString("runner-kind", result.Identity.RunnerKind);
                    writer.WriteString("transport-backend", result.Identity.TransportBackend);
                    writer.WriteString("os-description", result.Identity.OsDescription);
                    writer.WriteString("os-architecture", result.Identity.OsArchitecture);
                    writer.WriteString("process-architecture", result.Identity.ProcessArchitecture);
                    writer.WriteString("framework-description", result.Identity.FrameworkDescription);
                    writer.WriteNumber("processor-count", result.Identity.ProcessorCount);
                    writer.WriteNumber("duration-seconds", result.DurationSeconds);
                    writer.WriteNumber("subscriber-count", result.SubscriberCount);
                    writer.WriteNumber("client-connection-count", result.ClientConnectionCount);
                    writer.WriteNumber("estimated-latency-storage-bytes", result.EstimatedLatencyStorageBytes);
                    writer.WriteNumber("max-frame-payload-bytes", MixedWorkloadOptions.MaxFramePayloadBytes);
                    writer.WriteNumber("dropped-pending-send-count", result.DroppedPendingSendCount);
                    writer.WriteNumber("tcp-pending-send-queue-high-watermark", result.TcpPendingSendQueueHighWatermark);
                    writer.WriteNumber("end-pending-send-count", result.EndPendingSendCount);
                    writer.WriteNumber("fallback-pool-rented-after-stop", result.FallbackPoolRentedAfterStop);
                    writer.WriteNumber("timeout-count", result.TimeoutCount);
                    writer.WriteStartArray("streams");
                    WriteStream(writer, result.Data);
                    WriteStream(writer, result.Control);
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
            }
        }

        private static void WriteStream(Utf8JsonWriter writer, MixedWorkloadStreamResult result)
        {
            writer.WriteStartObject();
            writer.WriteString("name", result.Name);
            writer.WriteString("topic", result.Topic);
            writer.WriteNumber("payload-bytes", result.PayloadBytes);
            writer.WriteNumber("target-rate-hz", result.TargetRateHz);
            writer.WriteNumber("target-duration-seconds", result.TargetDurationSeconds);
            writer.WriteNumber("planned-message-count", result.PlannedMessageCount);
            writer.WriteNumber("sent-message-count", result.SentMessageCount);
            writer.WriteNumber("subscriber-count", result.SubscriberCount);
            writer.WriteNumber("planned-delivery-count", result.PlannedDeliveryCount);
            writer.WriteNumber("received-delivery-count", result.ReceivedDeliveryCount);
            writer.WriteNumber("minimum-received-per-subscriber", result.MinimumReceivedPerSubscriber);
            writer.WriteNumber("maximum-received-per-subscriber", result.MaximumReceivedPerSubscriber);
            writer.WriteNumber("delivery-failed-subscriber-count", result.DeliveryFailedSubscriberCount);
            writer.WriteNumber("latency-failed-subscriber-count", result.LatencyFailedSubscriberCount);
            writer.WriteNumber("sequence-error-count", result.SequenceErrorCount);
            writer.WriteNumber("payload-error-count", result.PayloadErrorCount);
            writer.WriteNumber("worst-subscriber-p50-latency-us", Round(result.WorstSubscriberP50LatencyMicroseconds, 1));
            writer.WriteNumber("worst-subscriber-p99-latency-us", Round(result.WorstSubscriberP99LatencyMicroseconds, 1));
            writer.WriteNumber("worst-subscriber-p999-latency-us", Round(result.WorstSubscriberP999LatencyMicroseconds, 1));
            writer.WriteNumber(
                "worst-subscriber-first-half-p99-latency-us",
                Round(result.WorstSubscriberFirstHalfP99LatencyMicroseconds, 1));
            writer.WriteNumber(
                "worst-subscriber-second-half-p99-latency-us",
                Round(result.WorstSubscriberSecondHalfP99LatencyMicroseconds, 1));
            writer.WriteNumber(
                "worst-subscriber-p99-latency-growth-ratio",
                Round(result.WorstSubscriberP99LatencyGrowthRatio, 2));
            writer.WriteNumber("publisher-elapsed-ticks", result.PublisherElapsedTicks);
            writer.WriteNumber("actual-rate-hz", Round(result.ActualRateHz, 1));
            writer.WriteBoolean("delivery-passed", result.DeliveryPassed);
            writer.WriteBoolean("rate-passed", result.RatePassed);
            writer.WriteBoolean("latency-budget-passed", result.LatencyBudgetPassed);
            writer.WriteBoolean("passed", result.Passed);
            writer.WriteNumber("publisher-elapsed-ms", Round(result.PublisherElapsedMilliseconds, 1));
            writer.WriteEndObject();
        }

        private static double Round(double value, int digits)
        {
            return Math.Round(value, digits);
        }
    }
}
