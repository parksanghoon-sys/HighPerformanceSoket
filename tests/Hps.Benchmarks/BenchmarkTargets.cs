using System;
using System.Globalization;
using System.IO;

namespace Hps.Benchmarks
{
    /// <summary>
    /// Phase 4 벤치마크가 공통으로 사용하는 헤드라인 목표값이다.
    ///
    /// 수치를 코드에 먼저 고정해 두면 이후 BenchmarkDotNet microbench 와 TCP 부하 생성 하니스가
    /// 서로 다른 payload 크기나 publish rate 를 쓰는 실수를 막을 수 있다.
    /// </summary>
    internal static class BenchmarkTargets
    {
        public const string TcpLoopbackBaselineName = "tcp-loopback-saea-baseline";
        public const string DefaultTopic = "alpha";
        public const int PayloadBytes = 4096;
        public const int PublishRateHz = 100;
        public const int SubscriberCount = 1;
        public const int DurationSeconds = 30;

        // BrokerServer 의 maxPayloadLength 는 TCP frame payload 전체, 즉 command text 와 publish payload 를 함께 담는다.
        // topic 길이나 command prefix 를 바꿔도 4096B payload 기준 smoke/load 하니스가 바로 실패하지 않도록
        // 명령 envelope 여유분을 별도 상수로 둔다.
        public const int CommandEnvelopeBudgetBytes = 128;
        public const int MaxFramePayloadBytes = PayloadBytes + CommandEnvelopeBudgetBytes;

        public static long PayloadBytesPerSecond
        {
            get { return (long)PayloadBytes * PublishRateHz; }
        }

        public static int PlannedMessageCount
        {
            get { return PublishRateHz * DurationSeconds; }
        }

        public static void Print(TextWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            writer.WriteLine("Phase 4 기준 목표");
            writer.WriteLine("scenario: {0}", TcpLoopbackBaselineName);
            writer.WriteLine("transport: SaeaTransport loopback");
            writer.WriteLine("topic: {0}", DefaultTopic);
            writer.WriteLine("payload-bytes: {0}", PayloadBytes);
            writer.WriteLine("publish-rate-hz: {0}", PublishRateHz);
            writer.WriteLine("duration-seconds: {0}", DurationSeconds);
            writer.WriteLine("planned-message-count: {0}", PlannedMessageCount);
            writer.WriteLine("subscriber-count: {0}", SubscriberCount);
            writer.WriteLine("payload-rate-bytes-per-second: {0}", PayloadBytesPerSecond);
            writer.WriteLine(
                "payload-rate-kib-per-second: {0}",
                (PayloadBytesPerSecond / 1024.0).ToString("F1", CultureInfo.InvariantCulture));
            writer.WriteLine("max-frame-payload-bytes: {0}", MaxFramePayloadBytes);
            writer.WriteLine("gate: sent == received, dropped == 0, pool-rented == 0, p50/p99 report recorded");
        }
    }
}
