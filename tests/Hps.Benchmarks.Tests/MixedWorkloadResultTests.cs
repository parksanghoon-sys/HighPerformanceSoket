using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class MixedWorkloadResultTests
    {
        // Task 3의 첫 경계는 stream 판정과 전체 실행 판정을 기존 baseline result와 분리된 타입으로 두는 것이다.
        // reflection Red를 사용해 production type 부재가 컴파일 오류가 아니라 계약 단언 실패로 드러나게 한다.
        [Fact]
        public void Contract_MixedWorkloadResultsExposeStreamAndGlobalGates()
        {
            Assembly assembly = typeof(BenchmarkCommandParser).Assembly;
            Type? streamType = assembly.GetType("Hps.Benchmarks.MixedWorkloadStreamResult");
            Type? runType = assembly.GetType("Hps.Benchmarks.MixedWorkloadRunResult");

            Assert.NotNull(streamType);
            Assert.NotNull(runType);
            Assert.NotNull(streamType!.GetConstructor(new[]
            {
                typeof(string), typeof(string), typeof(int), typeof(int), typeof(int), typeof(int),
                typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int),
                typeof(int), typeof(int), typeof(int), typeof(int), typeof(double), typeof(double),
                typeof(double), typeof(double), typeof(double), typeof(double), typeof(long)
            }));
            Assert.NotNull(streamType!.GetProperty("DeliveryPassed"));
            Assert.NotNull(streamType.GetProperty("RatePassed"));
            Assert.NotNull(streamType.GetProperty("LatencyBudgetPassed"));
            Assert.NotNull(streamType.GetProperty("DeliveryFailedSubscriberCount"));
            Assert.NotNull(streamType.GetProperty("LatencyFailedSubscriberCount"));
            Assert.NotNull(streamType.GetProperty("WorstSubscriberP99LatencyMicroseconds"));
            Assert.NotNull(streamType.GetProperty("WorstSubscriberP999LatencyMicroseconds"));
            Assert.NotNull(streamType.GetProperty("Passed"));
            Assert.NotNull(runType!.GetProperty("ClientConnectionCount"));
            Assert.NotNull(runType.GetProperty("EstimatedLatencyStorageBytes"));
            Assert.NotNull(runType.GetProperty("Passed"));
            Assert.NotNull(runType.GetMethod("Print", new[] { typeof(System.IO.TextWriter) }));
        }

        // 정상 stream은 subscriber별 exact delivery, target의 99% 이상 rate와 두 latency budget을 모두 만족해야 한다.
        // 개별 gate와 결합 gate를 함께 고정해 이후 조건 추가가 기존 통과 조건을 암묵적으로 바꾸지 못하게 한다.
        [Fact]
        public void Gates_WhenStreamSatisfiesEveryRequirement_Pass()
        {
            MixedWorkloadStreamResult result = CreatePassingStream();

            Assert.True(result.DeliveryPassed);
            Assert.True(result.RatePassed);
            Assert.True(result.LatencyBudgetPassed);
            Assert.True(result.Passed);
        }

        // publisher가 계획 수보다 적게 완료되면 subscriber 집계가 우연히 맞더라도 실행을 수락할 수 없다.
        // 발행 완료와 배달 완료를 독립적으로 확인해 drain 단계의 잘못된 집계가 누락을 가리지 못하게 한다.
        [Fact]
        public void DeliveryPassed_WhenSentCountIsBelowPlan_IsFalse()
        {
            MixedWorkloadStreamResult result = CreatePassingStream(sentMessageCount: 99);

            Assert.False(result.DeliveryPassed);
            Assert.False(result.Passed);
        }

        // aggregate received 수가 같아도 한 subscriber가 99개만 받았다면 fan-out exact delivery 실패다.
        // minimum count와 failed subscriber 수를 함께 판정해 다른 subscriber의 중복 수신이 누락을 상쇄하지 못하게 한다.
        [Fact]
        public void DeliveryPassed_WhenOneSubscriberMissesAMessage_IsFalse()
        {
            MixedWorkloadStreamResult result = CreatePassingStream(
                minimumReceivedPerSubscriber: 99,
                deliveryFailedSubscriberCount: 1);

            Assert.False(result.DeliveryPassed);
        }

        // sequence와 payload 오류는 received count가 정확해도 stream 무결성을 깨뜨린다.
        // 두 오류 counter가 각각 독립적으로 delivery gate를 닫는지 확인한다.
        [Theory]
        [InlineData(1, 0)]
        [InlineData(0, 1)]
        public void DeliveryPassed_WhenContentErrorExists_IsFalse(int sequenceErrorCount, int payloadErrorCount)
        {
            MixedWorkloadStreamResult result = CreatePassingStream(
                sequenceErrorCount: sequenceErrorCount,
                payloadErrorCount: payloadErrorCount);

            Assert.False(result.DeliveryPassed);
        }

        // 100개 completion에는 첫 완료부터 마지막 완료까지 99개 interval만 존재한다.
        // 1초 elapsed를 99Hz로 계산해 message count 자체를 interval 수로 쓰는 off-by-one 회귀를 막는다.
        [Fact]
        public void ActualRateHz_WhenOneHundredCompletionsSpanOneSecond_IsNinetyNineHertz()
        {
            MixedWorkloadStreamResult result = CreatePassingStream(publisherElapsedTicks: Stopwatch.Frequency);

            Assert.Equal(99.0, result.ActualRateHz, 6);
            Assert.True(result.RatePassed);
        }

        // 같은 99개 interval이 0.99초에 끝나면 실제 rate는 정확히 100Hz다.
        // 부동소수점 나눗셈과 Stopwatch tick 환산을 함께 고정한다.
        [Fact]
        public void ActualRateHz_WhenOneHundredCompletionsSpanPointNineNineSeconds_IsOneHundredHertz()
        {
            long elapsedTicks = Stopwatch.Frequency * 99L / 100L;
            MixedWorkloadStreamResult result = CreatePassingStream(publisherElapsedTicks: elapsedTicks);

            Assert.Equal(100.0, result.ActualRateHz, 6);
            Assert.True(result.RatePassed);
        }

        // completion이 둘 미만이면 first-to-last interval이 존재하지 않고 elapsed 0도 rate를 정의할 수 없다.
        // 두 경우 모두 0Hz로 안전하게 귀결되어 NaN이나 무한대가 report에 기록되지 않아야 한다.
        [Fact]
        public void ActualRateHz_WhenIntervalCannotBeFormed_IsZero()
        {
            MixedWorkloadStreamResult oneCompletion = CreatePassingStream(sentMessageCount: 1);
            MixedWorkloadStreamResult zeroElapsed = CreatePassingStream(publisherElapsedTicks: 0);

            Assert.Equal(0, oneCompletion.ActualRateHz);
            Assert.Equal(0, zeroElapsed.ActualRateHz);
            Assert.False(oneCompletion.RatePassed);
            Assert.False(zeroElapsed.RatePassed);
        }

        // target 100Hz의 99% 미만이면 전달과 latency가 정상이어도 rate hard gate는 실패해야 한다.
        // 1.001초 elapsed는 99개 interval 기준 약 98.9Hz로 threshold 바로 아래를 재현한다.
        [Fact]
        public void RatePassed_WhenActualRateIsBelowNinetyNinePercent_IsFalse()
        {
            long elapsedTicks = Stopwatch.Frequency + (Stopwatch.Frequency / 1000L);
            MixedWorkloadStreamResult result = CreatePassingStream(publisherElapsedTicks: elapsedTicks);

            Assert.True(result.ActualRateHz < 99.0);
            Assert.False(result.RatePassed);
            Assert.False(result.Passed);
        }

        // latency는 aggregate percentile이 아니라 가장 느린 subscriber의 p99/p999와 실패 subscriber 수로 판정한다.
        // 세 입력 중 하나만 위반해도 latency gate가 닫혀 느린 연결이 다른 연결의 정상 sample에 희석되지 않아야 한다.
        [Theory]
        [InlineData(5000.1, 9000.0, 0)]
        [InlineData(4000.0, 10000.1, 0)]
        [InlineData(4000.0, 9000.0, 1)]
        public void LatencyBudgetPassed_WhenWorstSubscriberViolatesBudget_IsFalse(
            double p99LatencyMicroseconds,
            double p999LatencyMicroseconds,
            int latencyFailedSubscriberCount)
        {
            MixedWorkloadStreamResult result = CreatePassingStream(
                latencyFailedSubscriberCount: latencyFailedSubscriberCount,
                p99LatencyMicroseconds: p99LatencyMicroseconds,
                p999LatencyMicroseconds: p999LatencyMicroseconds);

            Assert.False(result.LatencyBudgetPassed);
            Assert.False(result.Passed);
        }

        // latency 예산은 초과만 실패시키므로 p99 5ms와 p999 10ms의 정확한 경계값은 허용해야 한다.
        // 부동소수점 비교가 실수로 strict less-than으로 바뀌는 off-by-boundary 회귀를 막는다.
        [Fact]
        public void LatencyBudgetPassed_WhenWorstSubscriberEqualsBudgets_IsTrue()
        {
            MixedWorkloadStreamResult result = CreatePassingStream(
                p99LatencyMicroseconds: 5000.0,
                p999LatencyMicroseconds: 10000.0);

            Assert.True(result.LatencyBudgetPassed);
            Assert.True(result.Passed);
        }

        // 두 stream이 통과해도 drop, drain 후 pending, 종료 후 pool rented 또는 timeout이 하나라도 남으면 전체 실패다.
        // 전역 counter를 각각 독립 입력으로 바꿔 조건 하나가 누락된 AND 결합 회귀를 감지한다.
        [Theory]
        [InlineData("drop")]
        [InlineData("pending")]
        [InlineData("pool")]
        [InlineData("timeout")]
        public void Passed_WhenGlobalFailureCounterIsNonZero_IsFalse(string counterName)
        {
            long dropped = counterName == "drop" ? 1 : 0;
            int pending = counterName == "pending" ? 1 : 0;
            int pool = counterName == "pool" ? 1 : 0;
            int timeout = counterName == "timeout" ? 1 : 0;
            MixedWorkloadRunResult result = CreatePassingRun(dropped, pending, pool, timeout);

            Assert.False(result.Passed);
        }

        // stream gate와 네 전역 zero 조건이 모두 충족된 결과만 최종 hard pass다.
        // queue HWM은 관측값이므로 2여도 전체 판정을 실패시키지 않는다.
        [Fact]
        public void Passed_WhenStreamsAndGlobalGatesPass_IsTrue()
        {
            MixedWorkloadRunResult result = CreatePassingRun();

            Assert.True(result.Passed);
        }

        // 전역 counter가 0이어도 data 또는 control 중 하나가 실패하면 전체 mixed gate는 실패해야 한다.
        // 두 stream을 대칭으로 검증해 한쪽만 결합하거나 OR로 묶는 회귀를 차단한다.
        [Theory]
        [InlineData("data")]
        [InlineData("control")]
        public void Passed_WhenEitherStreamFails_IsFalse(string failedStreamName)
        {
            MixedWorkloadStreamResult passingData = CreatePassingStream();
            MixedWorkloadStreamResult passingControl = CreatePassingStream("control", "control");
            MixedWorkloadStreamResult failingData = CreatePassingStream(sentMessageCount: 99);
            MixedWorkloadStreamResult failingControl = CreatePassingStream("control", "control", sentMessageCount: 99);
            MixedWorkloadRunResult result = CreateRun(
                "mixed-tcp-loopback",
                failedStreamName == "data" ? failingData : passingData,
                failedStreamName == "control" ? failingControl : passingControl,
                CreateIdentity());

            Assert.False(result.Passed);
        }

        // result 생성 시 필수 문자열과 하위 result/identity가 null이면 report 단계까지 잘못된 상태를 전달하면 안 된다.
        // runner의 프로그래밍 오류를 생성자 경계에서 즉시 드러내도록 모든 참조 입력을 검증한다.
        [Fact]
        public void Constructors_WhenRequiredReferenceIsNull_ThrowArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(delegate () { CreatePassingStream(name: null!); });
            Assert.Throws<ArgumentNullException>(delegate () { CreatePassingStream(topic: null!); });

            MixedWorkloadStreamResult stream = CreatePassingStream();
            BenchmarkRunIdentity identity = CreateIdentity();
            Assert.Throws<ArgumentNullException>(delegate () { CreateRun(null!, stream, stream, identity); });
            Assert.Throws<ArgumentNullException>(delegate () { CreateRun("mixed", null!, stream, identity); });
            Assert.Throws<ArgumentNullException>(delegate () { CreateRun("mixed", stream, null!, identity); });
            Assert.Throws<ArgumentNullException>(delegate () { CreateRun("mixed", stream, stream, null!); });
        }

        // stream의 모든 count 입력은 실제 계측으로 성립할 수 없는 음수를 생성 단계에서 거부해야 한다.
        // parameter별 theory로 개별 검증 호출이 빠지는 회귀를 정확히 식별한다.
        [Theory]
        [InlineData("payloadBytes")]
        [InlineData("targetRateHz")]
        [InlineData("targetDurationSeconds")]
        [InlineData("plannedMessageCount")]
        [InlineData("sentMessageCount")]
        [InlineData("subscriberCount")]
        [InlineData("plannedDeliveryCount")]
        [InlineData("receivedDeliveryCount")]
        [InlineData("minimumReceivedPerSubscriber")]
        [InlineData("maximumReceivedPerSubscriber")]
        [InlineData("deliveryFailedSubscriberCount")]
        [InlineData("latencyFailedSubscriberCount")]
        [InlineData("sequenceErrorCount")]
        [InlineData("payloadErrorCount")]
        public void StreamConstructor_WhenCountIsNegative_ThrowsArgumentOutOfRange(string parameterName)
        {
            Assert.Throws<ArgumentOutOfRangeException>(delegate ()
            {
                CreateStreamWithNegativeCount(parameterName);
            });
        }

        // run의 계획·resource·transport counter도 음수를 허용하면 overall gate와 JSON 의미가 깨진다.
        // stream과 동일하게 모든 검증 parameter를 하나씩 음수로 바꿔 생성자 계약 전체를 고정한다.
        [Theory]
        [InlineData("durationSeconds")]
        [InlineData("subscriberCount")]
        [InlineData("clientConnectionCount")]
        [InlineData("estimatedLatencyStorageBytes")]
        [InlineData("droppedPendingSendCount")]
        [InlineData("tcpPendingSendQueueHighWatermark")]
        [InlineData("endPendingSendCount")]
        [InlineData("fallbackPoolRentedAfterStop")]
        [InlineData("timeoutCount")]
        public void RunConstructor_WhenCountIsNegative_ThrowsArgumentOutOfRange(string parameterName)
        {
            Assert.Throws<ArgumentOutOfRangeException>(delegate ()
            {
                CreateRunWithNegativeCount(parameterName);
            });
        }

        // console 출력은 raw JSON과 별도로 사람이 두 stream과 최종 판정을 즉시 확인하는 요약이다.
        // 고정 result name과 data/control 구분이 빠지지 않도록 최소 출력 계약을 검증한다.
        [Fact]
        public void Print_WhenRunPasses_WritesOverallAndStreamResults()
        {
            MixedWorkloadRunResult result = CreatePassingRun();
            StringWriter writer = new StringWriter();

            result.Print(writer);

            string output = writer.ToString();
            Assert.Contains("mixed-load-open-loop-result: pass", output);
            Assert.Contains("data-result: pass", output);
            Assert.Contains("control-result: pass", output);
        }

        // null writer는 정상 출력 대상이 아니며 빈 실행처럼 조용히 무시하면 호출부 결함을 숨긴다.
        // 기존 loopback result와 같은 프로그래밍 오류 계약을 mixed result에도 유지한다.
        [Fact]
        public void Print_WhenWriterIsNull_ThrowsArgumentNull()
        {
            MixedWorkloadRunResult result = CreatePassingRun();

            Assert.Throws<ArgumentNullException>(delegate () { result.Print(null!); });
        }

        private static MixedWorkloadStreamResult CreateStreamWithNegativeCount(string parameterName)
        {
            int payloadBytes = 10240;
            int targetRateHz = 100;
            int targetDurationSeconds = 1;
            int plannedMessageCount = 100;
            int sentMessageCount = 100;
            int subscriberCount = 2;
            int plannedDeliveryCount = 200;
            int receivedDeliveryCount = 200;
            int minimumReceivedPerSubscriber = 100;
            int maximumReceivedPerSubscriber = 100;
            int deliveryFailedSubscriberCount = 0;
            int latencyFailedSubscriberCount = 0;
            int sequenceErrorCount = 0;
            int payloadErrorCount = 0;

            switch (parameterName)
            {
                case "payloadBytes": payloadBytes = -1; break;
                case "targetRateHz": targetRateHz = -1; break;
                case "targetDurationSeconds": targetDurationSeconds = -1; break;
                case "plannedMessageCount": plannedMessageCount = -1; break;
                case "sentMessageCount": sentMessageCount = -1; break;
                case "subscriberCount": subscriberCount = -1; break;
                case "plannedDeliveryCount": plannedDeliveryCount = -1; break;
                case "receivedDeliveryCount": receivedDeliveryCount = -1; break;
                case "minimumReceivedPerSubscriber": minimumReceivedPerSubscriber = -1; break;
                case "maximumReceivedPerSubscriber": maximumReceivedPerSubscriber = -1; break;
                case "deliveryFailedSubscriberCount": deliveryFailedSubscriberCount = -1; break;
                case "latencyFailedSubscriberCount": latencyFailedSubscriberCount = -1; break;
                case "sequenceErrorCount": sequenceErrorCount = -1; break;
                case "payloadErrorCount": payloadErrorCount = -1; break;
                default: throw new ArgumentException("알 수 없는 stream count parameter입니다.", nameof(parameterName));
            }

            return new MixedWorkloadStreamResult(
                "data",
                "data",
                payloadBytes,
                targetRateHz,
                targetDurationSeconds,
                plannedMessageCount,
                sentMessageCount,
                subscriberCount,
                plannedDeliveryCount,
                receivedDeliveryCount,
                minimumReceivedPerSubscriber,
                maximumReceivedPerSubscriber,
                deliveryFailedSubscriberCount,
                latencyFailedSubscriberCount,
                sequenceErrorCount,
                payloadErrorCount,
                1000.0,
                4000.0,
                9000.0,
                3500.0,
                4000.0,
                1.14,
                Stopwatch.Frequency);
        }

        private static MixedWorkloadRunResult CreateRunWithNegativeCount(string parameterName)
        {
            int durationSeconds = 1;
            int subscriberCount = 2;
            int clientConnectionCount = 6;
            long estimatedLatencyStorageBytes = 6400;
            long droppedPendingSendCount = 0;
            int tcpPendingSendQueueHighWatermark = 2;
            int endPendingSendCount = 0;
            int fallbackPoolRentedAfterStop = 0;
            int timeoutCount = 0;

            switch (parameterName)
            {
                case "durationSeconds": durationSeconds = -1; break;
                case "subscriberCount": subscriberCount = -1; break;
                case "clientConnectionCount": clientConnectionCount = -1; break;
                case "estimatedLatencyStorageBytes": estimatedLatencyStorageBytes = -1; break;
                case "droppedPendingSendCount": droppedPendingSendCount = -1; break;
                case "tcpPendingSendQueueHighWatermark": tcpPendingSendQueueHighWatermark = -1; break;
                case "endPendingSendCount": endPendingSendCount = -1; break;
                case "fallbackPoolRentedAfterStop": fallbackPoolRentedAfterStop = -1; break;
                case "timeoutCount": timeoutCount = -1; break;
                default: throw new ArgumentException("알 수 없는 run count parameter입니다.", nameof(parameterName));
            }

            MixedWorkloadStreamResult stream = CreatePassingStream();
            return new MixedWorkloadRunResult(
                "mixed-tcp-loopback",
                durationSeconds,
                subscriberCount,
                clientConnectionCount,
                estimatedLatencyStorageBytes,
                stream,
                stream,
                droppedPendingSendCount,
                tcpPendingSendQueueHighWatermark,
                endPendingSendCount,
                fallbackPoolRentedAfterStop,
                timeoutCount,
                CreateIdentity());
        }

        private static MixedWorkloadStreamResult CreatePassingStream(
            string name = "data",
            string topic = "data",
            int sentMessageCount = 100,
            int minimumReceivedPerSubscriber = 100,
            int deliveryFailedSubscriberCount = 0,
            int latencyFailedSubscriberCount = 0,
            int sequenceErrorCount = 0,
            int payloadErrorCount = 0,
            double p99LatencyMicroseconds = 4000.0,
            double p999LatencyMicroseconds = 9000.0,
            long? publisherElapsedTicks = null)
        {
            long effectiveElapsedTicks = publisherElapsedTicks.HasValue
                ? publisherElapsedTicks.Value
                : Stopwatch.Frequency * 99L / 100L;

            return new MixedWorkloadStreamResult(
                name,
                topic,
                10240,
                100,
                1,
                100,
                sentMessageCount,
                2,
                200,
                200,
                minimumReceivedPerSubscriber,
                100,
                deliveryFailedSubscriberCount,
                latencyFailedSubscriberCount,
                sequenceErrorCount,
                payloadErrorCount,
                1000.0,
                p99LatencyMicroseconds,
                p999LatencyMicroseconds,
                3500.0,
                4000.0,
                1.14,
                effectiveElapsedTicks);
        }

        private static MixedWorkloadRunResult CreatePassingRun(
            long droppedPendingSendCount = 0,
            int endPendingSendCount = 0,
            int fallbackPoolRentedAfterStop = 0,
            int timeoutCount = 0)
        {
            MixedWorkloadStreamResult data = CreatePassingStream();
            MixedWorkloadStreamResult control = CreatePassingStream("control", "control");
            return CreateRun(
                "mixed-tcp-loopback",
                data,
                control,
                CreateIdentity(),
                droppedPendingSendCount,
                endPendingSendCount,
                fallbackPoolRentedAfterStop,
                timeoutCount);
        }

        private static MixedWorkloadRunResult CreateRun(
            string scenario,
            MixedWorkloadStreamResult data,
            MixedWorkloadStreamResult control,
            BenchmarkRunIdentity identity,
            long droppedPendingSendCount = 0,
            int endPendingSendCount = 0,
            int fallbackPoolRentedAfterStop = 0,
            int timeoutCount = 0)
        {
            return new MixedWorkloadRunResult(
                scenario,
                1,
                2,
                6,
                6400,
                data,
                control,
                droppedPendingSendCount,
                2,
                endPendingSendCount,
                fallbackPoolRentedAfterStop,
                timeoutCount,
                identity);
        }

        private static BenchmarkRunIdentity CreateIdentity()
        {
            return new BenchmarkRunIdentity(
                "tcp-mixed-load-saea-v1",
                "test-runner",
                "test",
                "SaeaTransport",
                "test-os",
                "X64",
                "X64",
                ".NET 9",
                8);
        }
    }
}
