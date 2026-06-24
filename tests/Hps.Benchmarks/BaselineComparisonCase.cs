namespace Hps.Benchmarks
{
    internal sealed class BaselineComparisonCase
    {
        public BaselineComparisonCase(
            string resultName,
            string scenario,
            int payloadBytes,
            double targetRateHz,
            int targetDurationSeconds)
        {
            ResultName = resultName;
            Scenario = scenario;
            PayloadBytes = payloadBytes;
            TargetRateHz = targetRateHz;
            TargetDurationSeconds = targetDurationSeconds;
        }

        public string ResultName { get; }

        public string Scenario { get; }

        public int PayloadBytes { get; }

        public double TargetRateHz { get; }

        public int TargetDurationSeconds { get; }
    }
}
