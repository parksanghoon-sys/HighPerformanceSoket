using System;
using System.Diagnostics;

namespace Hps.Benchmarks
{
    /// <summary>
    /// mixed workload의 단일 stream에서 측정한 발행, 배달과 subscriber별 최악 지연값을 보관한다.
    /// aggregate latency로 느린 subscriber가 희석되지 않도록 runner가 subscriber별 계산을 끝낸 값을 받는다.
    /// </summary>
    internal sealed class MixedWorkloadStreamResult
    {
        public MixedWorkloadStreamResult(
            string name,
            string topic,
            int payloadBytes,
            int targetRateHz,
            int targetDurationSeconds,
            int plannedMessageCount,
            int sentMessageCount,
            int subscriberCount,
            int plannedDeliveryCount,
            int receivedDeliveryCount,
            int minimumReceivedPerSubscriber,
            int maximumReceivedPerSubscriber,
            int deliveryFailedSubscriberCount,
            int latencyFailedSubscriberCount,
            int sequenceErrorCount,
            int payloadErrorCount,
            double worstSubscriberP50LatencyMicroseconds,
            double worstSubscriberP99LatencyMicroseconds,
            double worstSubscriberP999LatencyMicroseconds,
            double worstSubscriberFirstHalfP99LatencyMicroseconds,
            double worstSubscriberSecondHalfP99LatencyMicroseconds,
            double worstSubscriberP99LatencyGrowthRatio,
            long publisherElapsedTicks)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));
            if (topic == null)
                throw new ArgumentNullException(nameof(topic));

            ThrowIfNegative(payloadBytes, nameof(payloadBytes));
            ThrowIfNegative(targetRateHz, nameof(targetRateHz));
            ThrowIfNegative(targetDurationSeconds, nameof(targetDurationSeconds));
            ThrowIfNegative(plannedMessageCount, nameof(plannedMessageCount));
            ThrowIfNegative(sentMessageCount, nameof(sentMessageCount));
            ThrowIfNegative(subscriberCount, nameof(subscriberCount));
            ThrowIfNegative(plannedDeliveryCount, nameof(plannedDeliveryCount));
            ThrowIfNegative(receivedDeliveryCount, nameof(receivedDeliveryCount));
            ThrowIfNegative(minimumReceivedPerSubscriber, nameof(minimumReceivedPerSubscriber));
            ThrowIfNegative(maximumReceivedPerSubscriber, nameof(maximumReceivedPerSubscriber));
            ThrowIfNegative(deliveryFailedSubscriberCount, nameof(deliveryFailedSubscriberCount));
            ThrowIfNegative(latencyFailedSubscriberCount, nameof(latencyFailedSubscriberCount));
            ThrowIfNegative(sequenceErrorCount, nameof(sequenceErrorCount));
            ThrowIfNegative(payloadErrorCount, nameof(payloadErrorCount));

            Name = name;
            Topic = topic;
            PayloadBytes = payloadBytes;
            TargetRateHz = targetRateHz;
            TargetDurationSeconds = targetDurationSeconds;
            PlannedMessageCount = plannedMessageCount;
            SentMessageCount = sentMessageCount;
            SubscriberCount = subscriberCount;
            PlannedDeliveryCount = plannedDeliveryCount;
            ReceivedDeliveryCount = receivedDeliveryCount;
            MinimumReceivedPerSubscriber = minimumReceivedPerSubscriber;
            MaximumReceivedPerSubscriber = maximumReceivedPerSubscriber;
            DeliveryFailedSubscriberCount = deliveryFailedSubscriberCount;
            LatencyFailedSubscriberCount = latencyFailedSubscriberCount;
            SequenceErrorCount = sequenceErrorCount;
            PayloadErrorCount = payloadErrorCount;
            WorstSubscriberP50LatencyMicroseconds = worstSubscriberP50LatencyMicroseconds;
            WorstSubscriberP99LatencyMicroseconds = worstSubscriberP99LatencyMicroseconds;
            WorstSubscriberP999LatencyMicroseconds = worstSubscriberP999LatencyMicroseconds;
            WorstSubscriberFirstHalfP99LatencyMicroseconds = worstSubscriberFirstHalfP99LatencyMicroseconds;
            WorstSubscriberSecondHalfP99LatencyMicroseconds = worstSubscriberSecondHalfP99LatencyMicroseconds;
            WorstSubscriberP99LatencyGrowthRatio = worstSubscriberP99LatencyGrowthRatio;
            PublisherElapsedTicks = publisherElapsedTicks;
        }

        public string Name { get; }
        public string Topic { get; }
        public int PayloadBytes { get; }
        public int TargetRateHz { get; }
        public int TargetDurationSeconds { get; }
        public int PlannedMessageCount { get; }
        public int SentMessageCount { get; }
        public int SubscriberCount { get; }
        public int PlannedDeliveryCount { get; }
        public int ReceivedDeliveryCount { get; }
        public int MinimumReceivedPerSubscriber { get; }
        public int MaximumReceivedPerSubscriber { get; }
        public int DeliveryFailedSubscriberCount { get; }
        public int LatencyFailedSubscriberCount { get; }
        public int SequenceErrorCount { get; }
        public int PayloadErrorCount { get; }
        public double WorstSubscriberP50LatencyMicroseconds { get; }
        public double WorstSubscriberP99LatencyMicroseconds { get; }
        public double WorstSubscriberP999LatencyMicroseconds { get; }
        public double WorstSubscriberFirstHalfP99LatencyMicroseconds { get; }
        public double WorstSubscriberSecondHalfP99LatencyMicroseconds { get; }
        public double WorstSubscriberP99LatencyGrowthRatio { get; }
        public long PublisherElapsedTicks { get; }

        public double PublisherElapsedMilliseconds
        {
            get
            {
                if (PublisherElapsedTicks <= 0)
                    return 0;

                return ((double)PublisherElapsedTicks * 1000.0) / Stopwatch.Frequency;
            }
        }

        public double ActualRateHz
        {
            get
            {
                if (SentMessageCount < 2 || PublisherElapsedTicks <= 0)
                    return 0;

                // 첫 completion과 마지막 completion 사이에는 N개 message가 아니라 N-1개 간격이 있다.
                // cast를 곱셈 전에 적용해 정수 나눗셈과 큰 tick 값의 중간 overflow를 피한다.
                return ((double)(SentMessageCount - 1) * Stopwatch.Frequency) / PublisherElapsedTicks;
            }
        }

        public bool DeliveryPassed
        {
            get
            {
                return SentMessageCount == PlannedMessageCount
                    && ReceivedDeliveryCount == PlannedDeliveryCount
                    && MinimumReceivedPerSubscriber == PlannedMessageCount
                    && MaximumReceivedPerSubscriber == PlannedMessageCount
                    && DeliveryFailedSubscriberCount == 0
                    && SequenceErrorCount == 0
                    && PayloadErrorCount == 0;
            }
        }

        public bool RatePassed
        {
            get { return ActualRateHz >= TargetRateHz * MixedWorkloadOptions.MinimumRateRatio; }
        }

        public bool LatencyBudgetPassed
        {
            get
            {
                return LatencyFailedSubscriberCount == 0
                    && WorstSubscriberP99LatencyMicroseconds <= MixedWorkloadOptions.P99LatencyBudgetMicroseconds
                    && WorstSubscriberP999LatencyMicroseconds <= MixedWorkloadOptions.P999LatencyBudgetMicroseconds;
            }
        }

        public bool Passed
        {
            get { return DeliveryPassed && RatePassed && LatencyBudgetPassed; }
        }

        private static void ThrowIfNegative(int value, string parameterName)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
