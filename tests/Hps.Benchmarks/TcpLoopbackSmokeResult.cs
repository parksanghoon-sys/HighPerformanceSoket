using System;
using System.Globalization;
using System.IO;

namespace Hps.Benchmarks
{
    /// <summary>
    /// 짧은 TCP loopback smoke 실행 결과이다.
    ///
    /// 이 값은 아직 Phase 4 성능 합격/불합격 기준이 아니다. 실제 30초/100Hz runner 를 만들기 전에
    /// sent/received/drop/leak/latency 계측 경계가 한 프로세스 안에서 동작하는지 확인하기 위한 최소 리포트다.
    /// </summary>
    internal sealed class TcpLoopbackSmokeResult
    {
        public TcpLoopbackSmokeResult(
            string scenario,
            int payloadBytes,
            int sent,
            int received,
            long dropped,
            int poolRented,
            double p50LatencyMicroseconds,
            double p99LatencyMicroseconds,
            long elapsedMilliseconds)
        {
            Scenario = scenario;
            PayloadBytes = payloadBytes;
            Sent = sent;
            Received = received;
            Dropped = dropped;
            PoolRented = poolRented;
            P50LatencyMicroseconds = p50LatencyMicroseconds;
            P99LatencyMicroseconds = p99LatencyMicroseconds;
            ElapsedMilliseconds = elapsedMilliseconds;
        }

        public string Scenario { get; }

        public int PayloadBytes { get; }

        public int Sent { get; }

        public int Received { get; }

        public long Dropped { get; }

        public int PoolRented { get; }

        public double P50LatencyMicroseconds { get; }

        public double P99LatencyMicroseconds { get; }

        public long ElapsedMilliseconds { get; }

        public bool Passed
        {
            get { return Sent == Received && Dropped == 0 && PoolRented == 0; }
        }

        public void Print(TextWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            writer.WriteLine("smoke-result: {0}", Passed ? "pass" : "fail");
            writer.WriteLine("scenario: {0}", Scenario);
            writer.WriteLine("payload-bytes: {0}", PayloadBytes);
            writer.WriteLine("sent: {0}", Sent);
            writer.WriteLine("received: {0}", Received);
            writer.WriteLine("dropped: {0}", Dropped);
            writer.WriteLine("pool-rented: {0}", PoolRented);
            writer.WriteLine("p50-latency-us: {0}", P50LatencyMicroseconds.ToString("F1", CultureInfo.InvariantCulture));
            writer.WriteLine("p99-latency-us: {0}", P99LatencyMicroseconds.ToString("F1", CultureInfo.InvariantCulture));
            writer.WriteLine("elapsed-ms: {0}", ElapsedMilliseconds);
        }
    }
}
