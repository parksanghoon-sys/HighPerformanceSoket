using System;
using System.Runtime.InteropServices;

namespace Hps.Benchmarks
{
    /// <summary>
    /// benchmark raw report 에 기록할 runner/environment identity 이다.
    /// 같은 코드라도 장비, OS, 런타임이 다르면 latency 비교가 왜곡될 수 있으므로,
    /// raw report 단계에서 비교 가능성을 판단할 최소 metadata 를 보존한다.
    /// </summary>
    internal sealed class BenchmarkRunIdentity
    {
        public const string DefaultBenchmarkProfile = "tcp-loopback-saea-v1";
        public const string DefaultRunnerId = "local-unspecified";
        public const string DefaultRunnerKind = "local";
        public const string DefaultTransportBackend = "SaeaTransport";

        private const string RunnerIdEnvironmentVariable = "HPS_BENCHMARK_RUNNER_ID";
        private const string RunnerKindEnvironmentVariable = "HPS_BENCHMARK_RUNNER_KIND";

        public BenchmarkRunIdentity(
            string benchmarkProfile,
            string runnerId,
            string runnerKind,
            string transportBackend,
            string osDescription,
            string osArchitecture,
            string processArchitecture,
            string frameworkDescription,
            int processorCount)
        {
            BenchmarkProfile = NormalizeRequired(benchmarkProfile, nameof(benchmarkProfile));
            RunnerId = NormalizeRequired(runnerId, nameof(runnerId));
            RunnerKind = NormalizeRequired(runnerKind, nameof(runnerKind));
            TransportBackend = NormalizeRequired(transportBackend, nameof(transportBackend));
            OsDescription = NormalizeRequired(osDescription, nameof(osDescription));
            OsArchitecture = NormalizeRequired(osArchitecture, nameof(osArchitecture));
            ProcessArchitecture = NormalizeRequired(processArchitecture, nameof(processArchitecture));
            FrameworkDescription = NormalizeRequired(frameworkDescription, nameof(frameworkDescription));
            ProcessorCount = processorCount;
        }

        public static BenchmarkRunIdentity Unknown
        {
            get
            {
                return new BenchmarkRunIdentity("unknown", "unknown", "unknown", "unknown", "unknown", "unknown", "unknown", "unknown", 0);
            }
        }

        public string BenchmarkProfile { get; }

        public string RunnerId { get; }

        public string RunnerKind { get; }

        public string TransportBackend { get; }

        public string OsDescription { get; }

        public string OsArchitecture { get; }

        public string ProcessArchitecture { get; }

        public string FrameworkDescription { get; }

        public int ProcessorCount { get; }

        /// <summary>
        /// 현재 process 의 기본 benchmark identity 를 만든다.
        ///
        /// privacy 를 위해 host name, user name, IP address 는 자동 수집하지 않는다.
        /// runner 를 명시적으로 구분해야 하는 환경은 정해진 환경 변수로만 식별자를 주입한다.
        /// </summary>
        public static BenchmarkRunIdentity CaptureDefault()
        {
            return new BenchmarkRunIdentity(
                DefaultBenchmarkProfile,
                GetEnvironmentOrDefault(RunnerIdEnvironmentVariable, DefaultRunnerId),
                GetEnvironmentOrDefault(RunnerKindEnvironmentVariable, DefaultRunnerKind),
                DefaultTransportBackend,
                RuntimeInformation.OSDescription,
                RuntimeInformation.OSArchitecture.ToString(),
                RuntimeInformation.ProcessArchitecture.ToString(),
                RuntimeInformation.FrameworkDescription,
                Environment.ProcessorCount);
        }

        private static string GetEnvironmentOrDefault(string variable, string fallback)
        {
            string? value = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            return value.Trim();
        }

        private static string NormalizeRequired(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("benchmark identity 값은 비어 있을 수 없습니다.", parameterName);

            return value.Trim();
        }
    }
}
