using System;
using System.Globalization;
using System.IO;

namespace Hps.Benchmarks
{
    /// <summary>
    /// TCP loopback benchmark 계열 실행 결과이다.
    ///
    /// 이 값은 smoke 와 30초/100Hz load runner 가 같은 출력 형식을 쓰게 하여,
    /// sent/received/drop/leak/latency 계측 경계를 사람과 자동화가 동일하게 확인할 수 있게 한다.
    /// 현재 pass/fail 은 전달 완결성, drop 없음, pool leak 없음만 판정하며 latency 는 관측값으로만 출력한다.
    /// </summary>
    internal sealed class TcpLoopbackRunResult
    {
        public TcpLoopbackRunResult(
            string resultName,
            string scenario,
            int payloadBytes,
            int targetRateHz,
            int targetDurationSeconds,
            int plannedMessageCount,
            int sent,
            int received,
            long dropped,
            int poolRented,
            double p50LatencyMicroseconds,
            double p99LatencyMicroseconds,
            long elapsedMilliseconds)
        {
            ResultName = resultName;
            Scenario = scenario;
            PayloadBytes = payloadBytes;
            TargetRateHz = targetRateHz;
            TargetDurationSeconds = targetDurationSeconds;
            PlannedMessageCount = plannedMessageCount;
            Sent = sent;
            Received = received;
            Dropped = dropped;
            PoolRented = poolRented;
            P50LatencyMicroseconds = p50LatencyMicroseconds;
            P99LatencyMicroseconds = p99LatencyMicroseconds;
            ElapsedMilliseconds = elapsedMilliseconds;
        }

        public string ResultName { get; }

        public string Scenario { get; }

        public int PayloadBytes { get; }

        public int TargetRateHz { get; }

        public int TargetDurationSeconds { get; }

        public int PlannedMessageCount { get; }

        public int Sent { get; }

        public int Received { get; }

        public long Dropped { get; }

        public int PoolRented { get; }

        public double P50LatencyMicroseconds { get; }

        public double P99LatencyMicroseconds { get; }

        public long ElapsedMilliseconds { get; }

        public double ActualRateHz
        {
            get
            {
                if (ElapsedMilliseconds <= 0)
                    return 0;

                return Sent * 1000.0 / ElapsedMilliseconds;
            }
        }

        public bool Passed
        {
            get { return Sent == PlannedMessageCount && Sent == Received && Dropped == 0 && PoolRented == 0; }
        }

        public void Print(TextWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            writer.WriteLine("{0}-result: {1}", ResultName, Passed ? "pass" : "fail");
            writer.WriteLine("scenario: {0}", Scenario);
            writer.WriteLine("payload-bytes: {0}", PayloadBytes);
            writer.WriteLine("target-rate-hz: {0}", TargetRateHz);
            writer.WriteLine("target-duration-seconds: {0}", TargetDurationSeconds);
            writer.WriteLine("planned-message-count: {0}", PlannedMessageCount);
            writer.WriteLine("sent: {0}", Sent);
            writer.WriteLine("received: {0}", Received);
            writer.WriteLine("dropped: {0}", Dropped);
            writer.WriteLine("pool-rented: {0}", PoolRented);
            writer.WriteLine("actual-rate-hz: {0}", ActualRateHz.ToString("F1", CultureInfo.InvariantCulture));
            writer.WriteLine("p50-latency-us: {0}", P50LatencyMicroseconds.ToString("F1", CultureInfo.InvariantCulture));
            writer.WriteLine("p99-latency-us: {0}", P99LatencyMicroseconds.ToString("F1", CultureInfo.InvariantCulture));
            writer.WriteLine("elapsed-ms: {0}", ElapsedMilliseconds);
        }
    }
}
