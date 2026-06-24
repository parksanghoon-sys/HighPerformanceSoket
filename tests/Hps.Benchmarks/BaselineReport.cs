namespace Hps.Benchmarks
{
    internal sealed class BaselineReport
    {
        public BaselineReport(
            string sourcePath,
            string resultName,
            string scenario,
            int payloadBytes,
            double targetRateHz,
            int targetDurationSeconds,
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
            int udpPendingSendQueueHighWatermark,
            BenchmarkRunIdentity? identity = null)
        {
            SourcePath = sourcePath;
            ResultName = resultName;
            Scenario = scenario;
            PayloadBytes = payloadBytes;
            TargetRateHz = targetRateHz;
            TargetDurationSeconds = targetDurationSeconds;
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
            Identity = identity ?? BenchmarkRunIdentity.Unknown;
        }

        public string SourcePath { get; }

        public string ResultName { get; }

        public string Scenario { get; }

        /// <summary>
        /// raw report 의 payload 크기다. comparison key 는 같은 runner 여도 payload 크기가 다르면 별도 부하 조건으로 본다.
        /// </summary>
        public int PayloadBytes { get; }

        /// <summary>
        /// raw report 의 목표 발행 rate 다. summary 비교 가능성 판단에서 같은 workload 인지 확인하는 입력값이다.
        /// </summary>
        public double TargetRateHz { get; }

        /// <summary>
        /// raw report 의 목표 지속 시간이다. planned count 만으로는 open-loop/closed-loop 설정 차이를 충분히 설명할 수 없다.
        /// </summary>
        public int TargetDurationSeconds { get; }

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

        /// <summary>
        /// raw report 에 기록된 runner/environment identity 다.
        /// 과거 artifact 처럼 metadata 가 없으면 `Unknown`을 사용해 legacy summary 재생성을 깨지 않는다.
        /// </summary>
        public BenchmarkRunIdentity Identity { get; }

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
