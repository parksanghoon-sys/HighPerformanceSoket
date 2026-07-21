using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class TcpMixedWorkloadScenarioRunnerTests
    {
        // Task 4의 첫 경계는 기존 단일 stream runner와 분리된 mixed TCP 진입점이다.
        // reflection Red를 사용해 production type 부재가 컴파일 오류가 아니라 계약 단언 실패로 드러나게 한다.
        [Fact]
        public void Contract_TcpMixedWorkloadScenarioRunnerExposesRunAsync()
        {
            Type? type = typeof(BenchmarkCommandParser).Assembly.GetType(
                "Hps.Benchmarks.TcpMixedWorkloadScenarioRunner");

            Assert.NotNull(type);
            MethodInfo? method = type!.GetMethod("RunAsync", BindingFlags.Static | BindingFlags.Public);
            Assert.NotNull(method);
            Assert.Equal(typeof(Task<MixedWorkloadRunResult>), method!.ReturnType);
        }

        // subscriber별 percentile을 먼저 계산하고 stream에는 그 최댓값만 올려야 느린 연결이 희석되지 않는다.
        // Task 5 fan-out 전에도 계산과 집계를 분리한 seam을 고정해 단일 구독자 구현이 aggregate sample로 굳지 않게 한다.
        [Fact]
        public void Contract_TcpMixedWorkloadScenarioRunnerExposesSubscriberLatencySeams()
        {
            Assembly assembly = typeof(BenchmarkCommandParser).Assembly;
            Type? runnerType = assembly.GetType("Hps.Benchmarks.TcpMixedWorkloadScenarioRunner");
            Type? summaryType = assembly.GetType("Hps.Benchmarks.SubscriberLatencySummary");

            Assert.NotNull(runnerType);
            Assert.NotNull(summaryType);
            Assert.NotNull(runnerType!.GetMethod(
                "CalculateSubscriberLatency",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(long[]), typeof(int), typeof(long[]) },
                null));
            Assert.NotNull(runnerType.GetMethod(
                "AggregateSubscriberLatencies",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { summaryType!.MakeArrayType() },
                null));
        }

        // 수신 timestamp가 0이거나 현재보다 미래면 latency 0으로 보정해서 정상 sample로 받아들일 수 없다.
        // timestamp 검증 seam을 먼저 고정해 손상 frame이 payload gate를 우회하는 회귀를 직접 검증할 수 있게 한다.
        [Fact]
        public void Contract_TcpMixedWorkloadScenarioRunnerExposesTimestampValidationSeam()
        {
            MethodInfo? method = typeof(TcpMixedWorkloadScenarioRunner).GetMethod(
                "TryCalculateLatency",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(method);
            Assert.Equal(typeof(bool), method!.ReturnType);
        }

        // publisher별 timer 생성 시점을 주기 기준으로 쓰면 공통 start tick과 무관한 위상 오차가 지속된다.
        // 재사용 waiter가 매 message의 absolute deadline을 직접 받아야 두 stream이 같은 시간축을 공유한다.
        [Fact]
        public void Contract_TcpMixedWorkloadScenarioRunnerUsesReusableAbsoluteDeadlineWaiter()
        {
            Type? waiterType = typeof(TcpMixedWorkloadScenarioRunner).GetNestedType(
                "AbsoluteDeadlineWaiter",
                BindingFlags.NonPublic);

            Assert.NotNull(waiterType);
            Assert.NotNull(waiterType!.GetMethod(
                "WaitUntilAsync",
                BindingFlags.Instance | BindingFlags.Public));
        }

        // 정상 monotonic timestamp는 수신 시각과의 양의 차이를 그대로 latency sample로 사용해야 한다.
        // 임의 보정 없이 tick 차이를 보존해야 percentile이 실제 wire 처리 시간을 반영한다.
        [Fact]
        public void TryCalculateLatency_WhenTimestampIsValid_ReturnsPositiveDelta()
        {
            bool valid = TcpMixedWorkloadScenarioRunner.TryCalculateLatency(100, 140, out long latencyTicks);

            Assert.True(valid);
            Assert.Equal(40, latencyTicks);
        }

        // 0 timestamp와 수신 시각보다 미래인 timestamp는 latency 0 sample이 아니라 payload 손상이다.
        // 두 경우 모두 false와 0을 반환해 caller가 payload error를 증가시키도록 계약을 고정한다.
        [Theory]
        [InlineData(0, 100)]
        [InlineData(101, 100)]
        public void TryCalculateLatency_WhenTimestampIsInvalid_RejectsSample(
            long embeddedTimestamp,
            long receivedTimestamp)
        {
            bool valid = TcpMixedWorkloadScenarioRunner.TryCalculateLatency(
                embeddedTimestamp,
                receivedTimestamp,
                out long latencyTicks);

            Assert.False(valid);
            Assert.Equal(0, latencyTicks);
        }

        // percentile은 입력 순서가 아니라 정렬된 sample의 ceil(count * percentile) - 1 위치를 사용한다.
        // 전후반은 sequence 순서를 유지해야 하므로 전체 percentile과 구간 percentile을 함께 검증한다.
        [Fact]
        public void CalculateSubscriberLatency_WhenFourSamplesProvided_CalculatesPercentilesAndGrowth()
        {
            long oneMillisecond = Stopwatch.Frequency / 1000;
            long[] latencyTicks = new[]
            {
                oneMillisecond,
                oneMillisecond * 2,
                oneMillisecond * 3,
                oneMillisecond * 4
            };
            long[] scratch = new long[latencyTicks.Length];
            double oneMillisecondMicroseconds = oneMillisecond * 1000000.0 / Stopwatch.Frequency;

            SubscriberLatencySummary summary = TcpMixedWorkloadScenarioRunner.CalculateSubscriberLatency(
                latencyTicks,
                latencyTicks.Length,
                scratch);

            Assert.Equal(oneMillisecondMicroseconds * 2, summary.P50, 6);
            Assert.Equal(oneMillisecondMicroseconds * 4, summary.P99, 6);
            Assert.Equal(oneMillisecondMicroseconds * 4, summary.P999, 6);
            Assert.Equal(oneMillisecondMicroseconds * 2, summary.FirstHalfP99, 6);
            Assert.Equal(oneMillisecondMicroseconds * 4, summary.SecondHalfP99, 6);
            Assert.Equal(2.0, summary.P99GrowthRatio, 6);
            Assert.Equal(0, summary.LatencyFailedSubscriberCount);
        }

        // sample이 없으면 percentile과 증가율은 정의 가능한 관측값 0으로 수렴해야 한다.
        // 빈 subscriber 상태가 NaN을 report에 전파하거나 latency 실패로 잘못 집계되는 회귀를 막는다.
        [Fact]
        public void CalculateSubscriberLatency_WhenNoSamplesProvided_ReturnsZeroSummary()
        {
            SubscriberLatencySummary summary = TcpMixedWorkloadScenarioRunner.CalculateSubscriberLatency(
                Array.Empty<long>(),
                0,
                Array.Empty<long>());

            Assert.Equal(0, summary.P50);
            Assert.Equal(0, summary.P99);
            Assert.Equal(0, summary.P999);
            Assert.Equal(0, summary.FirstHalfP99);
            Assert.Equal(0, summary.SecondHalfP99);
            Assert.Equal(0, summary.P99GrowthRatio);
            Assert.Equal(0, summary.LatencyFailedSubscriberCount);
        }

        // Task 4의 aggregate는 단일 subscriber summary를 변형하지 않고 그대로 stream 결과로 전달해야 한다.
        // 여기서 다시 percentile을 계산하면 향후 fan-out에서 subscriber별 worst 값이 aggregate sample에 희석될 수 있다.
        [Fact]
        public void AggregateSubscriberLatencies_WhenOneSummaryProvided_ReturnsSameValues()
        {
            SubscriberLatencySummary expected = new SubscriberLatencySummary(1, 2, 3, 4, 5, 6, 1);

            SubscriberLatencySummary actual = TcpMixedWorkloadScenarioRunner.AggregateSubscriberLatencies(
                new[] { expected });

            Assert.Equal(expected.P50, actual.P50);
            Assert.Equal(expected.P99, actual.P99);
            Assert.Equal(expected.P999, actual.P999);
            Assert.Equal(expected.FirstHalfP99, actual.FirstHalfP99);
            Assert.Equal(expected.SecondHalfP99, actual.SecondHalfP99);
            Assert.Equal(expected.P99GrowthRatio, actual.P99GrowthRatio);
            Assert.Equal(expected.LatencyFailedSubscriberCount, actual.LatencyFailedSubscriberCount);
        }

        // 단일 구독자 integration은 data/control이 독립 연결에서 동시에 진행되어도 각 100개를 정확히 전달해야 한다.
        // scheduler 변동이 큰 test 환경에서는 latency hard budget 자체를 단언하지 않고 무결성·자원 counter를 고정한다.
        [Fact]
        public async Task RunAsync_WhenOneSubscriberUsesSaea_DeliversBothStreamsWithoutDropOrLeak()
        {
            MixedWorkloadOptions options = new MixedWorkloadOptions(100, 1, 1);

            MixedWorkloadRunResult result = await TcpMixedWorkloadScenarioRunner.RunAsync(
                options,
                TcpLoopbackTransportBackend.Saea);

            Assert.Equal(100, result.Data.SentMessageCount);
            Assert.Equal(100, result.Data.ReceivedDeliveryCount);
            Assert.Equal(100, result.Control.SentMessageCount);
            Assert.Equal(100, result.Control.ReceivedDeliveryCount);
            Assert.Equal(0, result.Data.SequenceErrorCount);
            Assert.Equal(0, result.Control.SequenceErrorCount);
            Assert.Equal(0, result.Data.PayloadErrorCount);
            Assert.Equal(0, result.Control.PayloadErrorCount);
            Assert.Equal(0, result.Data.DeliveryFailedSubscriberCount);
            Assert.Equal(0, result.Control.DeliveryFailedSubscriberCount);
            Assert.Equal(0, result.DroppedPendingSendCount);
            Assert.Equal(0, result.EndPendingSendCount);
            Assert.Equal(0, result.FallbackPoolRentedAfterStop);
            Assert.Equal(0, result.TimeoutCount);
        }

        // fan-out collection은 다음 Task의 별도 Red/Green 범위다.
        // 현재 runner가 options 값을 조용히 무시하고 한 명만 실행해 잘못된 capacity 증거를 만들지 않게 한다.
        [Fact]
        public async Task RunAsync_WhenSubscriberCountIsNotOne_ThrowsNotSupported()
        {
            MixedWorkloadOptions options = new MixedWorkloadOptions(100, 1, 2);

            await Assert.ThrowsAsync<NotSupportedException>(async delegate
            {
                await TcpMixedWorkloadScenarioRunner.RunAsync(options, TcpLoopbackTransportBackend.Saea);
            });
        }
    }
}
