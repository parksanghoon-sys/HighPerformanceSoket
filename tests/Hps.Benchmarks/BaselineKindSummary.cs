namespace Hps.Benchmarks
{
    internal sealed class BaselineKindSummary
    {
        public BaselineKindSummary(
            string kind,
            int runCount,
            double p50Min,
            double p50Max,
            double p50Median,
            double p99Min,
            double p99Max,
            double p99Median,
            double p99GrowthRatioMin,
            double p99GrowthRatioMax,
            double actualRateMin,
            double actualRateMax,
            int tcpHighWatermarkMin,
            int tcpHighWatermarkMax,
            long droppedTotal,
            int payloadErrorTotal,
            int poolRentedMax)
        {
            Kind = kind;
            RunCount = runCount;
            P50Min = p50Min;
            P50Max = p50Max;
            P50Median = p50Median;
            P99Min = p99Min;
            P99Max = p99Max;
            P99Median = p99Median;
            P99GrowthRatioMin = p99GrowthRatioMin;
            P99GrowthRatioMax = p99GrowthRatioMax;
            ActualRateMin = actualRateMin;
            ActualRateMax = actualRateMax;
            TcpHighWatermarkMin = tcpHighWatermarkMin;
            TcpHighWatermarkMax = tcpHighWatermarkMax;
            DroppedTotal = droppedTotal;
            PayloadErrorTotal = payloadErrorTotal;
            PoolRentedMax = poolRentedMax;
        }

        public string Kind { get; }

        public int RunCount { get; }

        public double P50Min { get; }

        public double P50Max { get; }

        public double P50Median { get; }

        public double P99Min { get; }

        public double P99Max { get; }

        public double P99Median { get; }

        public double P99GrowthRatioMin { get; }

        public double P99GrowthRatioMax { get; }

        public double ActualRateMin { get; }

        public double ActualRateMax { get; }

        public int TcpHighWatermarkMin { get; }

        public int TcpHighWatermarkMax { get; }

        public long DroppedTotal { get; }

        public int PayloadErrorTotal { get; }

        public int PoolRentedMax { get; }
    }
}
