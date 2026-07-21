using System;
using System.Globalization;
using System.IO;

namespace Hps.Benchmarks
{
    /// <summary>
    /// data/control stream 결과와 transport 종료 상태를 결합한 mixed workload 실행 결과이다.
    /// stream gate와 전역 drop, pending, pool, timeout gate를 한곳에서 판정하는 경계로 사용한다.
    /// </summary>
    internal sealed class MixedWorkloadRunResult
    {
        public MixedWorkloadRunResult(
            string scenario,
            int durationSeconds,
            int subscriberCount,
            int clientConnectionCount,
            long estimatedLatencyStorageBytes,
            MixedWorkloadStreamResult data,
            MixedWorkloadStreamResult control,
            long droppedPendingSendCount,
            int tcpPendingSendQueueHighWatermark,
            int endPendingSendCount,
            int fallbackPoolRentedAfterStop,
            int timeoutCount,
            BenchmarkRunIdentity identity)
        {
            if (scenario == null)
                throw new ArgumentNullException(nameof(scenario));
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (control == null)
                throw new ArgumentNullException(nameof(control));
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));

            ThrowIfNegative(durationSeconds, nameof(durationSeconds));
            ThrowIfNegative(subscriberCount, nameof(subscriberCount));
            ThrowIfNegative(clientConnectionCount, nameof(clientConnectionCount));
            ThrowIfNegative(estimatedLatencyStorageBytes, nameof(estimatedLatencyStorageBytes));
            ThrowIfNegative(droppedPendingSendCount, nameof(droppedPendingSendCount));
            ThrowIfNegative(tcpPendingSendQueueHighWatermark, nameof(tcpPendingSendQueueHighWatermark));
            ThrowIfNegative(endPendingSendCount, nameof(endPendingSendCount));
            ThrowIfNegative(fallbackPoolRentedAfterStop, nameof(fallbackPoolRentedAfterStop));
            ThrowIfNegative(timeoutCount, nameof(timeoutCount));

            Scenario = scenario;
            DurationSeconds = durationSeconds;
            SubscriberCount = subscriberCount;
            ClientConnectionCount = clientConnectionCount;
            EstimatedLatencyStorageBytes = estimatedLatencyStorageBytes;
            Data = data;
            Control = control;
            DroppedPendingSendCount = droppedPendingSendCount;
            TcpPendingSendQueueHighWatermark = tcpPendingSendQueueHighWatermark;
            EndPendingSendCount = endPendingSendCount;
            FallbackPoolRentedAfterStop = fallbackPoolRentedAfterStop;
            TimeoutCount = timeoutCount;
            Identity = identity;
        }

        public string Scenario { get; }
        public int DurationSeconds { get; }
        public int SubscriberCount { get; }
        public int ClientConnectionCount { get; }
        public long EstimatedLatencyStorageBytes { get; }
        public MixedWorkloadStreamResult Data { get; }
        public MixedWorkloadStreamResult Control { get; }
        public long DroppedPendingSendCount { get; }
        public int TcpPendingSendQueueHighWatermark { get; }
        public int EndPendingSendCount { get; }
        public int FallbackPoolRentedAfterStop { get; }
        public int TimeoutCount { get; }
        public BenchmarkRunIdentity Identity { get; }
        public bool Passed
        {
            get
            {
                return Data.Passed
                    && Control.Passed
                    && DroppedPendingSendCount == 0
                    && EndPendingSendCount == 0
                    && FallbackPoolRentedAfterStop == 0
                    && TimeoutCount == 0;
            }
        }

        public void Print(TextWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            writer.WriteLine("mixed-load-open-loop-result: {0}", Passed ? "pass" : "fail");
            writer.WriteLine("scenario: {0}", Scenario);
            writer.WriteLine("duration-seconds: {0}", DurationSeconds);
            writer.WriteLine("subscriber-count: {0}", SubscriberCount);
            writer.WriteLine("client-connection-count: {0}", ClientConnectionCount);
            writer.WriteLine("estimated-latency-storage-bytes: {0}", EstimatedLatencyStorageBytes);
            writer.WriteLine("dropped-pending-send-count: {0}", DroppedPendingSendCount);
            writer.WriteLine("tcp-pending-send-queue-high-watermark: {0}", TcpPendingSendQueueHighWatermark);
            writer.WriteLine("end-pending-send-count: {0}", EndPendingSendCount);
            writer.WriteLine("fallback-pool-rented-after-stop: {0}", FallbackPoolRentedAfterStop);
            writer.WriteLine("timeout-count: {0}", TimeoutCount);
            PrintStream(writer, Data);
            PrintStream(writer, Control);
        }

        private static void PrintStream(TextWriter writer, MixedWorkloadStreamResult stream)
        {
            writer.WriteLine("{0}-result: {1}", stream.Name, stream.Passed ? "pass" : "fail");
            writer.WriteLine("{0}-topic: {1}", stream.Name, stream.Topic);
            writer.WriteLine("{0}-payload-bytes: {1}", stream.Name, stream.PayloadBytes);
            writer.WriteLine("{0}-target-rate-hz: {1}", stream.Name, stream.TargetRateHz);
            writer.WriteLine(
                "{0}-actual-rate-hz: {1}",
                stream.Name,
                stream.ActualRateHz.ToString("F1", CultureInfo.InvariantCulture));
            writer.WriteLine("{0}-planned-message-count: {1}", stream.Name, stream.PlannedMessageCount);
            writer.WriteLine("{0}-sent-message-count: {1}", stream.Name, stream.SentMessageCount);
            writer.WriteLine("{0}-planned-delivery-count: {1}", stream.Name, stream.PlannedDeliveryCount);
            writer.WriteLine("{0}-received-delivery-count: {1}", stream.Name, stream.ReceivedDeliveryCount);
            writer.WriteLine("{0}-delivery-failed-subscriber-count: {1}", stream.Name, stream.DeliveryFailedSubscriberCount);
            writer.WriteLine("{0}-latency-failed-subscriber-count: {1}", stream.Name, stream.LatencyFailedSubscriberCount);
            writer.WriteLine(
                "{0}-worst-subscriber-p99-latency-us: {1}",
                stream.Name,
                stream.WorstSubscriberP99LatencyMicroseconds.ToString("F1", CultureInfo.InvariantCulture));
            writer.WriteLine(
                "{0}-worst-subscriber-p999-latency-us: {1}",
                stream.Name,
                stream.WorstSubscriberP999LatencyMicroseconds.ToString("F1", CultureInfo.InvariantCulture));
        }

        private static void ThrowIfNegative(int value, string parameterName)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static void ThrowIfNegative(long value, string parameterName)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
