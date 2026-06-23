using System;
using System.Reflection;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BenchmarkRunIdentityTests
    {
        // runner identity 는 raw report schema 확장의 원천 model 이다.
        // 새 타입을 먼저 계약으로 고정해야 writer/reader 단계가 문자열 상수 중복 없이 같은 field 를 사용할 수 있다.
        [Fact]
        public void Contract_BenchmarkRunIdentityTypeExists()
        {
            Type? type = Type.GetType("Hps.Benchmarks.BenchmarkRunIdentity, Hps.Benchmarks");

            Assert.NotNull(type);
            Assert.NotNull(type!.GetProperty("BenchmarkProfile", BindingFlags.Instance | BindingFlags.Public));
            Assert.NotNull(type.GetMethod("CaptureDefault", BindingFlags.Static | BindingFlags.Public));
        }

        // 기본 capture 는 privacy 를 우선한다.
        // host name/user name/IP 를 쓰지 않고 명시 runner id 가 없으면 local-unspecified 로 남겨야 한다.
        [Fact]
        public void CaptureDefault_WhenEnvironmentValuesAreMissing_UsesPrivacyPreservingDefaultsAndRuntimeInfo()
        {
            string? oldRunnerId = Environment.GetEnvironmentVariable("HPS_BENCHMARK_RUNNER_ID");
            string? oldRunnerKind = Environment.GetEnvironmentVariable("HPS_BENCHMARK_RUNNER_KIND");
            try
            {
                Environment.SetEnvironmentVariable("HPS_BENCHMARK_RUNNER_ID", null);
                Environment.SetEnvironmentVariable("HPS_BENCHMARK_RUNNER_KIND", null);

                BenchmarkRunIdentity identity = BenchmarkRunIdentity.CaptureDefault();

                Assert.Equal(BenchmarkRunIdentity.DefaultBenchmarkProfile, identity.BenchmarkProfile);
                Assert.Equal(BenchmarkRunIdentity.DefaultRunnerId, identity.RunnerId);
                Assert.Equal(BenchmarkRunIdentity.DefaultRunnerKind, identity.RunnerKind);
                Assert.Equal(BenchmarkRunIdentity.DefaultTransportBackend, identity.TransportBackend);
                Assert.False(string.IsNullOrWhiteSpace(identity.OsDescription));
                Assert.False(string.IsNullOrWhiteSpace(identity.OsArchitecture));
                Assert.False(string.IsNullOrWhiteSpace(identity.ProcessArchitecture));
                Assert.False(string.IsNullOrWhiteSpace(identity.FrameworkDescription));
                Assert.True(identity.ProcessorCount > 0);
            }
            finally
            {
                Environment.SetEnvironmentVariable("HPS_BENCHMARK_RUNNER_ID", oldRunnerId);
                Environment.SetEnvironmentVariable("HPS_BENCHMARK_RUNNER_KIND", oldRunnerKind);
            }
        }

        // 서로 다른 장비를 비교군에서 분리하려면 사용자가 runner id 를 명시해야 한다.
        // 자동 machine name 수집 대신 환경 변수만 허용해 로컬/사설 환경 정보 노출을 막는다.
        [Fact]
        public void CaptureDefault_WhenEnvironmentValuesExist_UsesExplicitRunnerIdentity()
        {
            string? oldRunnerId = Environment.GetEnvironmentVariable("HPS_BENCHMARK_RUNNER_ID");
            string? oldRunnerKind = Environment.GetEnvironmentVariable("HPS_BENCHMARK_RUNNER_KIND");
            try
            {
                Environment.SetEnvironmentVariable("HPS_BENCHMARK_RUNNER_ID", "dev-box-a");
                Environment.SetEnvironmentVariable("HPS_BENCHMARK_RUNNER_KIND", "self-hosted");

                BenchmarkRunIdentity identity = BenchmarkRunIdentity.CaptureDefault();

                Assert.Equal("dev-box-a", identity.RunnerId);
                Assert.Equal("self-hosted", identity.RunnerKind);
            }
            finally
            {
                Environment.SetEnvironmentVariable("HPS_BENCHMARK_RUNNER_ID", oldRunnerId);
                Environment.SetEnvironmentVariable("HPS_BENCHMARK_RUNNER_KIND", oldRunnerKind);
            }
        }
    }
}
