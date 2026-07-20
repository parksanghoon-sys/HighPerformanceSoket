using System;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class MixedWorkloadOptionsTests
    {
        // mixed workload 구현의 첫 경계는 고정 profile과 파생 계획 수를 한 객체가 소유하는 것이다.
        // reflection Red로 시작해 production type 부재가 컴파일 오류가 아닌 명시적 계약 실패로 드러나게 한다.
        [Fact]
        public void Contract_MixedWorkloadOptionsExposesFixedProfileAndDerivedCounts()
        {
            Type? type = typeof(BenchmarkCommandParser).Assembly.GetType("Hps.Benchmarks.MixedWorkloadOptions");

            Assert.NotNull(type);
            Assert.NotNull(type!.GetConstructor(Type.EmptyTypes));
            Assert.NotNull(type.GetConstructor(new[] { typeof(int), typeof(int), typeof(int) }));
            Assert.NotNull(type.GetProperty("DataMessageCount"));
            Assert.NotNull(type.GetProperty("ControlMessageCount"));
            Assert.NotNull(type.GetProperty("DataDeliveryCount"));
            Assert.NotNull(type.GetProperty("ControlDeliveryCount"));
            Assert.NotNull(type.GetProperty("ClientConnectionCount"));
            Assert.NotNull(type.GetProperty("EstimatedLatencyStorageBytes"));
        }

        // 이 값들은 runner가 생성할 wire payload와 hard gate를 결정하므로 단순 기본값이 아니라 D243의 고정 계약이다.
        // 일부 상수만 검증하면 workload 크기나 SLO가 바뀌어도 options 테스트가 통과할 수 있어 전체를 한곳에서 고정한다.
        [Fact]
        public void Constants_DefineAcceptedMixedProfileAndHardGates()
        {
            Assert.Equal(10240, MixedWorkloadOptions.DataPayloadBytes);
            Assert.Equal(2560, MixedWorkloadOptions.ControlPayloadBytes);
            Assert.Equal(100, MixedWorkloadOptions.DefaultDataRateHz);
            Assert.Equal(100, MixedWorkloadOptions.MinimumDataRateHz);
            Assert.Equal(100, MixedWorkloadOptions.ControlRateHz);
            Assert.Equal(30, MixedWorkloadOptions.DefaultDurationSeconds);
            Assert.Equal(1, MixedWorkloadOptions.DefaultSubscriberCount);
            Assert.Equal(256, MixedWorkloadOptions.MaximumSubscriberCount);
            Assert.Equal(128L * 1024L * 1024L, MixedWorkloadOptions.MaximumLatencyStorageBytes);
            Assert.Equal(16384, MixedWorkloadOptions.MaxFramePayloadBytes);
            Assert.Equal(0.99, MixedWorkloadOptions.MinimumRateRatio);
            Assert.Equal(5000.0, MixedWorkloadOptions.P99LatencyBudgetMicroseconds);
            Assert.Equal(10000.0, MixedWorkloadOptions.P999LatencyBudgetMicroseconds);
        }

        // 기본 profile은 실제 수락 gate인 두 stream 100Hz x 30초와 subscriber 한 명을 정확히 표현해야 한다.
        // 원본 latency 배열 두 개와 가장 긴 stream용 scratch 하나까지 포함해야 실행 전 메모리 추정이 실제 할당 경계와 일치한다.
        [Fact]
        public void Constructor_WhenDefaultProfileIsUsed_CalculatesMessageAndResourceCounts()
        {
            MixedWorkloadOptions options = new MixedWorkloadOptions();

            Assert.Equal(100, options.DataRateHz);
            Assert.Equal(100, MixedWorkloadOptions.ControlRateHz);
            Assert.Equal(30, options.DurationSeconds);
            Assert.Equal(1, options.SubscriberCount);
            Assert.Equal(3000, options.DataMessageCount);
            Assert.Equal(3000, options.ControlMessageCount);
            Assert.Equal(3000, options.DataDeliveryCount);
            Assert.Equal(3000, options.ControlDeliveryCount);
            Assert.Equal(4, options.ClientConnectionCount);
            Assert.Equal(72000L, options.EstimatedLatencyStorageBytes);
        }

        // data rate와 fan-out은 독립적으로 늘어나지만 control rate는 항상 100Hz로 고정된다.
        // delivery 수와 scratch를 포함한 byte 계산을 함께 단언해 이후 runner가 다른 수식으로 배열을 예약하지 못하게 한다.
        [Fact]
        public void Constructor_WhenRateDurationAndFanoutAreProvided_CalculatesIndependentCounts()
        {
            MixedWorkloadOptions options = new MixedWorkloadOptions(250, 4, 3);

            Assert.Equal(1000, options.DataMessageCount);
            Assert.Equal(400, options.ControlMessageCount);
            Assert.Equal(3000, options.DataDeliveryCount);
            Assert.Equal(1200, options.ControlDeliveryCount);
            Assert.Equal(8, options.ClientConnectionCount);
            Assert.Equal(41600L, options.EstimatedLatencyStorageBytes);
        }

        // 100Hz 미만 data와 비양수 duration/subscriber는 목표를 축소하거나 무의미한 run을 만들므로 즉시 거부한다.
        // parser가 같은 options 타입을 재사용하므로 이 경계는 CLI와 직접 생성 경로에 동일하게 적용된다.
        [Theory]
        [InlineData(99, 1, 1)]
        [InlineData(100, 0, 1)]
        [InlineData(100, 1, 0)]
        public void Constructor_WhenInputIsBelowMinimum_ThrowsArgumentOutOfRange(
            int dataRateHz,
            int durationSeconds,
            int subscriberCount)
        {
            Assert.Throws<ArgumentOutOfRangeException>(delegate()
            {
                new MixedWorkloadOptions(dataRateHz, durationSeconds, subscriberCount);
            });
        }

        // 256명은 514개 client connection을 만드는 benchmark-local 안전 상한이다.
        // 상한 자체는 허용해 off-by-one으로 실제 fan-out 범위를 줄이는 회귀를 방지한다.
        [Fact]
        public void Constructor_WhenMaximumSubscriberCountIsUsed_AcceptsBoundary()
        {
            MixedWorkloadOptions options = new MixedWorkloadOptions(100, 1, 256);

            Assert.Equal(514, options.ClientConnectionCount);
            Assert.Equal(410400L, options.EstimatedLatencyStorageBytes);
        }

        // subscriber 상한을 넘는 입력은 수백 개 socket을 만들기 전에 options 단계에서 종료되어야 한다.
        // 이 값은 production capacity가 아니라 benchmark process의 handle 고갈을 막는 안전 경계다.
        [Fact]
        public void Constructor_WhenSubscriberCountExceedsHarnessLimit_ThrowsArgumentOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(delegate()
            {
                new MixedWorkloadOptions(100, 1, 257);
            });
        }

        // 계획 수는 배열 길이와 result의 Int32 계약으로 이어지므로 Int32를 넘는 값을 보관해서는 안 된다.
        // 계산은 long으로 수행하되 최종 지원 범위를 넘으면 allocation 전에 명시적 입력 오류로 변환한다.
        [Fact]
        public void Constructor_WhenPlannedCountExceedsInt32_ThrowsArgumentOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(delegate()
            {
                new MixedWorkloadOptions(int.MaxValue, 2, 1);
            });
        }

        // Int32 범위 초과와 달리 이 입력은 subscriber fan-out 곱셈 자체가 Int64를 넘겨 checked 산술을 실행한다.
        // OverflowException이 외부로 새지 않고 options의 안정적인 입력 오류 계약으로 변환되는지 별도로 검증한다.
        [Fact]
        public void Constructor_WhenPlannedCountExceedsInt64_ThrowsArgumentOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(delegate()
            {
                new MixedWorkloadOptions(int.MaxValue, int.MaxValue, 256);
            });
        }

        // 30분 x 47명은 원본 latency 배열과 scratch payload만으로 128MiB를 넘는다.
        // 유효한 Int32 입력이어도 OOM으로 process가 끝나지 않도록 deterministic preflight가 거부해야 한다.
        [Fact]
        public void Constructor_WhenLatencyStorageExceedsHarnessLimit_ThrowsArgumentOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(delegate()
            {
                new MixedWorkloadOptions(100, 1800, 47);
            });
        }

        // rate 32,718Hz와 256초는 data/control 원본 및 최대 stream scratch 합계가 정확히 128MiB다.
        // 비교 연산은 이 정확한 상한을 허용하고 측정 시간이 1초 늘어난 입력부터 거부해야 한다.
        [Fact]
        public void Constructor_WhenLatencyStorageEqualsHarnessLimit_AcceptsBoundary()
        {
            MixedWorkloadOptions options = new MixedWorkloadOptions(32718, 256, 1);

            Assert.Equal(MixedWorkloadOptions.MaximumLatencyStorageBytes, options.EstimatedLatencyStorageBytes);
        }

        [Fact]
        public void Constructor_WhenLatencyStorageIsImmediatelyAboveHarnessLimit_ThrowsArgumentOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(delegate()
            {
                new MixedWorkloadOptions(32718, 257, 1);
            });
        }

        // 기본 수락 절차의 30분 single-subscriber soak는 안전 상한 안에 있어야 한다.
        // 설계된 운영 검증 자체가 preflight에 막히는 잘못된 상한 변경을 이 값으로 감지한다.
        [Fact]
        public void Constructor_WhenThirtyMinuteSingleSubscriberSoakIsUsed_RemainsWithinHarnessLimit()
        {
            MixedWorkloadOptions options = new MixedWorkloadOptions(100, 1800, 1);

            Assert.Equal(4320000L, options.EstimatedLatencyStorageBytes);
            Assert.True(options.EstimatedLatencyStorageBytes <= MixedWorkloadOptions.MaximumLatencyStorageBytes);
        }
    }
}
