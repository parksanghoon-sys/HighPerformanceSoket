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

        // backend 비교 raw report 는 같은 runner 에서 실행되어도 profile/backend 값이 달라야 summary/history 가 혼합을 감지한다.
        // reflection 으로 먼저 계약을 고정해 CaptureForBackend API 부재가 assertion failure 로 드러나게 한다.
        [Fact]
        public void CaptureForBackend_WhenRioIsSelected_UsesRioProfileAndTransportBackend()
        {
            Type? backendType = Type.GetType("Hps.Benchmarks.TcpLoopbackTransportBackend, Hps.Benchmarks");
            Assert.NotNull(backendType);

            object rioBackend = Enum.Parse(backendType!, "Rio");
            MethodInfo? method = typeof(BenchmarkRunIdentity).GetMethod("CaptureForBackend", BindingFlags.Static | BindingFlags.Public);
            Assert.NotNull(method);

            BenchmarkRunIdentity identity = Assert.IsType<BenchmarkRunIdentity>(method!.Invoke(null, new object[] { rioBackend }));

            Assert.Equal("tcp-loopback-rio-v1", identity.BenchmarkProfile);
            Assert.Equal("RioTransport", identity.TransportBackend);
        }

        // io_uring raw report 는 SAEA/RIO 와 같은 schema 를 쓰지만 비교 key 는 반드시 분리되어야 한다.
        // TCP/UDP profile 과 transport-backend 를 고정해 summary/history 단계가 backend 간 결과를 섞지 않도록 한다.
        [Theory]
        [InlineData("Tcp", "tcp-loopback-iouring-v1")]
        [InlineData("Udp", "udp-loopback-iouring-v1")]
        public void CaptureForBackendAndProtocol_WhenIoUringIsSelected_UsesIoUringProfileAndTransportBackend(
            string protocolName,
            string expectedProfile)
        {
            Type? backendType = Type.GetType("Hps.Benchmarks.TcpLoopbackTransportBackend, Hps.Benchmarks");
            Assert.NotNull(backendType);

            bool backendParsed = Enum.TryParse(backendType!, "IoUring", false, out object? ioUringBackend);
            Assert.True(backendParsed);

            Type? protocolType = Type.GetType("Hps.Benchmarks.LoopbackProtocol, Hps.Benchmarks");
            Assert.NotNull(protocolType);
            object protocol = Enum.Parse(protocolType!, protocolName);

            MethodInfo? method = typeof(BenchmarkRunIdentity).GetMethod("CaptureForBackendAndProtocol", BindingFlags.Static | BindingFlags.Public);
            Assert.NotNull(method);

            BenchmarkRunIdentity identity = Assert.IsType<BenchmarkRunIdentity>(
                method!.Invoke(null, new object[] { ioUringBackend!, protocol }));

            Assert.Equal(expectedProfile, identity.BenchmarkProfile);
            Assert.Equal("IoUringTransport", identity.TransportBackend);
        }

        // mixed TCP raw report는 legacy single-stream profile과 분리된 비교 identity를 사용해야 한다.
        // reflection Red로 전용 capture method 부재를 먼저 드러내 기존 CaptureForBackend 의미를 바꾸지 않게 한다.
        [Fact]
        public void Contract_CaptureForMixedTcpBackendExists()
        {
            MethodInfo? method = typeof(BenchmarkRunIdentity).GetMethod(
                "CaptureForMixedTcpBackend",
                BindingFlags.Static | BindingFlags.Public);

            Assert.NotNull(method);
        }

        // mixed workload는 backend 환경 metadata를 재사용하되 legacy single-stream 비교 profile과는 분리되어야 한다.
        // 세 backend를 모두 고정해 새 raw report가 서로 또는 기존 baseline과 잘못 집계되는 회귀를 막는다.
        [Theory]
        [InlineData("Saea", "tcp-mixed-load-saea-v1", "SaeaTransport")]
        [InlineData("Rio", "tcp-mixed-load-rio-v1", "RioTransport")]
        [InlineData("IoUring", "tcp-mixed-load-iouring-v1", "IoUringTransport")]
        public void CaptureForMixedTcpBackend_WhenBackendIsSelected_UsesMixedProfileAndExistingBackendName(
            string backendName,
            string expectedProfile,
            string expectedBackendName)
        {
            TcpLoopbackTransportBackend backend = (TcpLoopbackTransportBackend)Enum.Parse(
                typeof(TcpLoopbackTransportBackend),
                backendName);
            BenchmarkRunIdentity identity = BenchmarkRunIdentity.CaptureForMixedTcpBackend(backend);

            Assert.Equal(expectedProfile, identity.BenchmarkProfile);
            Assert.Equal(expectedBackendName, identity.TransportBackend);
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
