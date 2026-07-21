using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class MixedWorkloadReportWriterTests
    {
        // mixed report는 legacy schema v1 writer와 분리된 전용 타입에서 시작해야 한다.
        // reflection Red로 writer 부재를 먼저 고정해 기존 writer에 분기를 추가하는 범위 확장을 막는다.
        [Fact]
        public void Contract_MixedWorkloadReportWriterExposesTypedWriteMethod()
        {
            Assembly assembly = typeof(BenchmarkCommandParser).Assembly;
            Type? writerType = assembly.GetType("Hps.Benchmarks.MixedWorkloadReportWriter");
            Type? resultType = assembly.GetType("Hps.Benchmarks.MixedWorkloadRunResult");

            Assert.NotNull(writerType);
            Assert.NotNull(resultType);
            Assert.NotNull(writerType!.GetMethod(
                "Write",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), resultType! },
                null));
        }

        // mixed raw report는 문서 종류와 schema version으로 legacy baseline과 구조적으로 분리되어야 한다.
        // stream 배열에는 subscriber별 실패 수와 worst-subscriber latency가 보존되어 후속 검토가 hard gate 근거를 재구성할 수 있어야 한다.
        [Fact]
        public void Write_WhenPassingResultIsProvided_WritesStableMixedJsonShape()
        {
            string directory = CreateTempDirectory();
            string path = Path.Combine(directory, "mixed.json");
            MixedWorkloadRunResult result = CreatePassingRun();

            MixedWorkloadReportWriter.Write(path, result);

            Assert.True(File.Exists(path));
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement root = document.RootElement;
                Assert.Equal("mixed-tcp-workload", root.GetProperty("report-kind").GetString());
                Assert.Equal(2, root.GetProperty("schema-version").GetInt32());
                Assert.Equal("mixed-load-open-loop", root.GetProperty("result-name").GetString());
                Assert.True(root.GetProperty("passed").GetBoolean());
                Assert.Equal("mixed-tcp-loopback", root.GetProperty("scenario").GetString());
                Assert.Equal(2, root.GetProperty("subscriber-count").GetInt32());
                Assert.Equal(6, root.GetProperty("client-connection-count").GetInt32());
                Assert.Equal(6400, root.GetProperty("estimated-latency-storage-bytes").GetInt64());
                Assert.Equal(MixedWorkloadOptions.MaxFramePayloadBytes, root.GetProperty("max-frame-payload-bytes").GetInt32());
                Assert.Equal(0, root.GetProperty("dropped-pending-send-count").GetInt64());
                Assert.Equal(0, root.GetProperty("end-pending-send-count").GetInt32());

                JsonElement streams = root.GetProperty("streams");
                Assert.Equal(2, streams.GetArrayLength());
                Assert.Equal("data", streams[0].GetProperty("name").GetString());
                Assert.Equal(0, streams[0].GetProperty("delivery-failed-subscriber-count").GetInt32());
                Assert.Equal(0, streams[0].GetProperty("latency-failed-subscriber-count").GetInt32());
                Assert.Equal(9000.0, streams[0].GetProperty("worst-subscriber-p999-latency-us").GetDouble());
                Assert.Equal(100.0, streams[0].GetProperty("actual-rate-hz").GetDouble());
                Assert.Equal(990.0, streams[0].GetProperty("publisher-elapsed-ms").GetDouble());
                Assert.Equal("control", streams[1].GetProperty("name").GetString());
            }
        }

        // key 순서는 의미 해석에 필수는 아니지만 raw artifact diff의 불필요한 churn을 막는 안정성 계약이다.
        // 계획에 고정된 top-level과 stream 순서를 직접 단언해 writer 리팩터링이 보고서 가독성을 흔들지 않게 한다.
        [Fact]
        public void Write_WhenResultIsWritten_PreservesCanonicalPropertyOrder()
        {
            string directory = CreateTempDirectory();
            string path = Path.Combine(directory, "mixed.json");

            MixedWorkloadReportWriter.Write(path, CreatePassingRun());

            Assert.True(File.Exists(path));
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                string[] topLevelNames = document.RootElement
                    .EnumerateObject()
                    .Select(property => property.Name)
                    .ToArray();
                Assert.Equal(new[]
                {
                    "report-kind", "schema-version", "result-name", "passed", "scenario",
                    "benchmark-profile", "runner-id", "runner-kind", "transport-backend",
                    "os-description", "os-architecture", "process-architecture",
                    "framework-description", "processor-count", "duration-seconds",
                    "subscriber-count", "client-connection-count", "estimated-latency-storage-bytes",
                    "max-frame-payload-bytes", "dropped-pending-send-count",
                    "tcp-pending-send-queue-high-watermark", "end-pending-send-count",
                    "fallback-pool-rented-after-stop", "timeout-count", "streams"
                }, topLevelNames);

                string[] streamNames = document.RootElement.GetProperty("streams")[0]
                    .EnumerateObject()
                    .Select(property => property.Name)
                    .ToArray();
                Assert.Equal(new[]
                {
                    "name", "topic", "payload-bytes", "target-rate-hz", "target-duration-seconds",
                    "planned-message-count", "sent-message-count", "subscriber-count",
                    "planned-delivery-count", "received-delivery-count",
                    "minimum-received-per-subscriber", "maximum-received-per-subscriber",
                    "delivery-failed-subscriber-count", "latency-failed-subscriber-count",
                    "sequence-error-count", "payload-error-count",
                    "worst-subscriber-p50-latency-us", "worst-subscriber-p99-latency-us",
                    "worst-subscriber-p999-latency-us", "worst-subscriber-first-half-p99-latency-us",
                    "worst-subscriber-second-half-p99-latency-us",
                    "worst-subscriber-p99-latency-growth-ratio", "publisher-elapsed-ticks",
                    "actual-rate-hz", "delivery-passed", "rate-passed", "latency-budget-passed",
                    "passed", "publisher-elapsed-ms"
                }, streamNames);
            }
        }

        // schema version 2 mixed 문서는 기존 baseline summary/history 입력에 섞이면 안 된다.
        // 같은 directory를 legacy reader로 읽어도 결과가 0개여야 report kind 분리가 실제 aggregate 경계까지 유지된다.
        [Fact]
        public void Write_WhenMixedReportSharesDirectoryWithLegacyReader_IsIgnored()
        {
            string directory = CreateTempDirectory();
            string path = Path.Combine(directory, "mixed.json");

            MixedWorkloadReportWriter.Write(path, CreatePassingRun());

            Assert.True(File.Exists(path));
            Assert.Empty(BaselineReportReader.ReadDirectory(directory));
        }

        // path와 result는 파일 system side effect 전에 검증해야 호출부 오류가 빈 artifact로 남지 않는다.
        // 기존 report writer와 같은 null/blank 입력 계약을 유지한다.
        [Fact]
        public void Write_WhenInputIsInvalid_ThrowsBeforeCreatingAReport()
        {
            MixedWorkloadRunResult result = CreatePassingRun();

            Assert.Throws<ArgumentNullException>(delegate () { MixedWorkloadReportWriter.Write(null!, result); });
            Assert.Throws<ArgumentException>(delegate () { MixedWorkloadReportWriter.Write(" ", result); });
            Assert.Throws<ArgumentNullException>(delegate () { MixedWorkloadReportWriter.Write("mixed.json", null!); });
        }

        private static MixedWorkloadRunResult CreatePassingRun()
        {
            long elapsedTicks = Stopwatch.Frequency * 99L / 100L;
            MixedWorkloadStreamResult data = CreateStream("data", "data", 10240, elapsedTicks);
            MixedWorkloadStreamResult control = CreateStream("control", "control", 2560, elapsedTicks);
            BenchmarkRunIdentity identity = new BenchmarkRunIdentity(
                "tcp-mixed-load-saea-v1",
                "test-runner",
                "test",
                "SaeaTransport",
                "test-os",
                "X64",
                "X64",
                ".NET 9",
                8);

            return new MixedWorkloadRunResult(
                "mixed-tcp-loopback",
                1,
                2,
                6,
                6400,
                data,
                control,
                0,
                2,
                0,
                0,
                0,
                identity);
        }

        private static MixedWorkloadStreamResult CreateStream(
            string name,
            string topic,
            int payloadBytes,
            long elapsedTicks)
        {
            return new MixedWorkloadStreamResult(
                name,
                topic,
                payloadBytes,
                100,
                1,
                100,
                100,
                2,
                200,
                200,
                100,
                100,
                0,
                0,
                0,
                0,
                1000.04,
                4000.04,
                9000.04,
                3500.04,
                4000.04,
                1.144,
                elapsedTicks);
        }

        private static string CreateTempDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "hps-mixed-report-tests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
