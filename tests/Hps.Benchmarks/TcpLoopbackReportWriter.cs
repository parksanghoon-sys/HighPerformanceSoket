using System;
using System.IO;
using System.Text.Json;

namespace Hps.Benchmarks
{
    /// <summary>
    /// TCP loopback runner 결과를 재실행 간 비교 가능한 JSON 파일로 저장한다.
    ///
    /// stdout 출력은 사람이 바로 확인하기 위한 요약이고, 이 writer는 리뷰와 추세 비교를 위해
    /// 같은 계측값을 안정적인 key 집합으로 남긴다. smoke/load/open-loop 모두 같은 schema를 쓰며,
    /// 특정 runner에서 의미가 약한 latency trend 값도 누락하지 않고 현재 result 값 그대로 기록한다.
    /// </summary>
    internal static class TcpLoopbackReportWriter
    {
        public static void Write(string path, TcpLoopbackRunResult result)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            if (result == null)
                throw new ArgumentNullException(nameof(result));

            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("report 경로는 비어 있을 수 없습니다.", nameof(path));

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
                    writer.WriteNumber("schema-version", 1);
                    writer.WriteString("result-name", result.ResultName);
                    writer.WriteBoolean("passed", result.Passed);
                    writer.WriteString("scenario", result.Scenario);
                    writer.WriteNumber("payload-bytes", result.PayloadBytes);
                    writer.WriteNumber("target-rate-hz", result.TargetRateHz);
                    writer.WriteNumber("target-duration-seconds", result.TargetDurationSeconds);
                    writer.WriteNumber("planned-message-count", result.PlannedMessageCount);
                    writer.WriteNumber("sent", result.Sent);
                    writer.WriteNumber("received", result.Received);
                    writer.WriteNumber("dropped", result.Dropped);
                    writer.WriteNumber("payload-errors", result.PayloadErrors);
                    writer.WriteNumber("pool-rented", result.PoolRented);
                    writer.WriteNumber("actual-rate-hz", Round(result.ActualRateHz, 1));
                    writer.WriteNumber("p50-latency-us", Round(result.P50LatencyMicroseconds, 1));
                    writer.WriteNumber("p99-latency-us", Round(result.P99LatencyMicroseconds, 1));
                    writer.WriteNumber("first-half-p99-latency-us", Round(result.FirstHalfP99LatencyMicroseconds, 1));
                    writer.WriteNumber("second-half-p99-latency-us", Round(result.SecondHalfP99LatencyMicroseconds, 1));
                    writer.WriteNumber("p99-latency-growth-ratio", Round(result.P99LatencyGrowthRatio, 2));
                    writer.WriteNumber("elapsed-ms", result.ElapsedMilliseconds);
                    writer.WriteEndObject();
                }
            }
        }

        private static double Round(double value, int digits)
        {
            return Math.Round(value, digits);
        }
    }
}
