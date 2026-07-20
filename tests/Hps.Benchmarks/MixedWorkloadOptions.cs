using System;

namespace Hps.Benchmarks
{
    /// <summary>
    /// 데이터와 제어 stream을 동시에 실행하는 benchmark의 고정 profile과 계획 수를 보관한다.
    /// 실제 socket 또는 계측 배열을 만들기 전에 모든 입력과 자원 경계를 검증하는 것이 이 타입의 책임이다.
    /// </summary>
    internal sealed class MixedWorkloadOptions
    {
        public const int DataPayloadBytes = 10240;
        public const int ControlPayloadBytes = 2560;
        public const int DefaultDataRateHz = 100;
        public const int MinimumDataRateHz = 100;
        public const int ControlRateHz = 100;
        public const int DefaultDurationSeconds = 30;
        public const int DefaultSubscriberCount = 1;
        public const int MaximumSubscriberCount = 256;
        public const long MaximumLatencyStorageBytes = 128L * 1024L * 1024L;
        public const int MaxFramePayloadBytes = 16384;
        public const double MinimumRateRatio = 0.99;
        public const double P99LatencyBudgetMicroseconds = 5000.0;
        public const double P999LatencyBudgetMicroseconds = 10000.0;

        public MixedWorkloadOptions()
            : this(DefaultDataRateHz, DefaultDurationSeconds, DefaultSubscriberCount)
        {
        }

        public MixedWorkloadOptions(int dataRateHz, int durationSeconds, int subscriberCount)
        {
            if (dataRateHz < MinimumDataRateHz)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dataRateHz),
                    "데이터 전송률은 목표 하한인 100 Hz 이상이어야 합니다.");
            }

            if (durationSeconds < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(durationSeconds),
                    "측정 시간은 1초 이상이어야 합니다.");
            }

            // 256은 제품의 연결 상한이 아니라 benchmark가 한 프로세스에서 감당할 수 있는
            // 계측 배열과 loopback 연결 수를 제한하기 위한 실행 안전 경계다.
            if (subscriberCount < 1 || subscriberCount > MaximumSubscriberCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(subscriberCount),
                    "구독자 수는 1 이상 256 이하여야 합니다.");
            }

            long dataMessageCount;
            long controlMessageCount;
            long dataDeliveryCount;
            long controlDeliveryCount;
            long estimatedLatencyStorageBytes;

            try
            {
                // long checked 산술로 먼저 계산한 뒤 현재 runner가 사용하는 int 반복 횟수로
                // 안전하게 표현할 수 있는지 확인한다. 입력 곱셈의 묵시적 wrap은 허용하지 않는다.
                dataMessageCount = checked((long)dataRateHz * durationSeconds);
                controlMessageCount = checked((long)ControlRateHz * durationSeconds);
                dataDeliveryCount = checked(dataMessageCount * subscriberCount);
                controlDeliveryCount = checked(controlMessageCount * subscriberCount);

                // 각 배달 지연의 원본 배열 두 개와 percentile 계산 때 재사용할 가장 큰 stream의
                // scratch 배열 하나를 합산한다. 이 추정에는 socket 또는 payload 버퍼는 포함하지 않는다.
                long scratchSampleCount = Math.Max(dataMessageCount, controlMessageCount);
                long latencySampleCount = checked(
                    checked(dataDeliveryCount + controlDeliveryCount) + scratchSampleCount);
                estimatedLatencyStorageBytes = checked(latencySampleCount * sizeof(long));
            }
            catch (OverflowException)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(durationSeconds),
                    "mixed workload 계획 수 또는 계측 저장소 추정값이 지원 범위를 초과합니다.");
            }

            if (dataMessageCount > int.MaxValue
                || controlMessageCount > int.MaxValue
                || dataDeliveryCount > int.MaxValue
                || controlDeliveryCount > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(durationSeconds),
                    "mixed workload 계획 메시지 수가 Int32 범위를 초과합니다.");
            }

            // 128 MiB는 latency 샘플만을 위한 benchmark-local 상한이다. 실제 프로세스 메모리
            // 상한으로 해석하지 않으며, 향후 계측 구조가 바뀌면 이 계산도 함께 갱신해야 한다.
            if (estimatedLatencyStorageBytes > MaximumLatencyStorageBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(durationSeconds),
                    "mixed workload latency 계측 저장소가 128 MiB 안전 상한을 초과합니다.");
            }

            DataRateHz = dataRateHz;
            DurationSeconds = durationSeconds;
            SubscriberCount = subscriberCount;
            DataMessageCount = (int)dataMessageCount;
            ControlMessageCount = (int)controlMessageCount;
            DataDeliveryCount = (int)dataDeliveryCount;
            ControlDeliveryCount = (int)controlDeliveryCount;
            ClientConnectionCount = checked(2 + (subscriberCount * 2));
            EstimatedLatencyStorageBytes = estimatedLatencyStorageBytes;
        }

        public int DataRateHz { get; }

        public int DurationSeconds { get; }

        public int SubscriberCount { get; }

        public int DataMessageCount { get; }

        public int ControlMessageCount { get; }

        public int DataDeliveryCount { get; }

        public int ControlDeliveryCount { get; }

        public int ClientConnectionCount { get; }

        public long EstimatedLatencyStorageBytes { get; }
    }
}
