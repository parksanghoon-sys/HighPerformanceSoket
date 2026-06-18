namespace Hps.Benchmarks
{
    internal sealed class BaselineReport
    {
        public BaselineReport(
            string sourcePath,
            string resultName,
            string scenario,
            int plannedMessageCount,
            int sent,
            int received,
            long dropped,
            int payloadErrors,
            int poolRented,
            double actualRateHz,
            double p50LatencyMicroseconds,
            double p99LatencyMicroseconds,
            double p99LatencyGrowthRatio,
            int tcpPendingSendQueueHighWatermark,
            int udpPendingSendQueueHighWatermark)
        {
            SourcePath = sourcePath;
            ResultName = resultName;
            Scenario = scenario;
            PlannedMessageCount = plannedMessageCount;
            Sent = sent;
            Received = received;
            Dropped = dropped;
            PayloadErrors = payloadErrors;
            PoolRented = poolRented;
            ActualRateHz = actualRateHz;
            P50LatencyMicroseconds = p50LatencyMicroseconds;
            P99LatencyMicroseconds = p99LatencyMicroseconds;
            P99LatencyGrowthRatio = p99LatencyGrowthRatio;
            TcpPendingSendQueueHighWatermark = tcpPendingSendQueueHighWatermark;
            UdpPendingSendQueueHighWatermark = udpPendingSendQueueHighWatermark;
        }

        public string SourcePath { get; }

        public string ResultName { get; }

        public string Scenario { get; }

        public int PlannedMessageCount { get; }

        public int Sent { get; }

        public int Received { get; }

        public long Dropped { get; }

        public int PayloadErrors { get; }

        public int PoolRented { get; }

        public double ActualRateHz { get; }

        public double P50LatencyMicroseconds { get; }

        public double P99LatencyMicroseconds { get; }

        public double P99LatencyGrowthRatio { get; }

        public int TcpPendingSendQueueHighWatermark { get; }

        public int UdpPendingSendQueueHighWatermark { get; }

        public bool HardPassed
        {
            get
            {
                return Sent == PlannedMessageCount
                    && Sent == Received
                    && Dropped == 0
                    && PayloadErrors == 0
                    && PoolRented == 0;
            }
        }
    }
}
