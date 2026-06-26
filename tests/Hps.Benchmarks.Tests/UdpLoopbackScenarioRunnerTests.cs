using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class UdpLoopbackScenarioRunnerTests
    {
        // UDP load runner 는 30초 CLI 목표를 그대로 unit test 에 넣으면 테스트 시간이 과도하게 늘어난다.
        // 같은 core scenario 를 작은 message count 로 호출해 closed-loop delivery/drop/leak/report identity 만 빠르게 고정한다.
        [Fact]
        public async Task RunScenarioForTestAsync_WhenClosedLoopLoadShapeIsUsed_ReturnsPassingUdpLoadResult()
        {
            TcpLoopbackRunResult result = await InvokeScenarioForTestAsync(
                "load",
                string.Empty,
                4,
                0,
                0,
                false,
                false);

            Assert.True(result.Passed);
            Assert.Equal("load", result.ResultName);
            Assert.Equal("udp-loopback-saea-baseline", result.Scenario);
            Assert.Equal(4, result.Sent);
            Assert.Equal(4, result.Received);
            Assert.Equal(0, result.Dropped);
            Assert.Equal(0, result.PoolRented);
            Assert.Equal(BenchmarkRunIdentity.UdpSaeaBenchmarkProfile, result.Identity.BenchmarkProfile);
        }

        // open-loop 는 publisher schedule 과 subscriber receive 를 분리하므로 closed-loop 와 다른 failure mode 를 가진다.
        // 짧은 smoke성 parameter 로 received/sent hard gate 와 payload sequence 검증 경로가 같은 result schema 에 남는지 확인한다.
        [Fact]
        public async Task RunScenarioForTestAsync_WhenOpenLoopShapeIsUsed_ReturnsPassingUdpOpenLoopResult()
        {
            TcpLoopbackRunResult result = await InvokeScenarioForTestAsync(
                "open-loop",
                "-open-loop",
                4,
                0,
                0,
                false,
                true);

            Assert.True(result.Passed);
            Assert.Equal("open-loop", result.ResultName);
            Assert.Equal("udp-loopback-saea-baseline-open-loop", result.Scenario);
            Assert.Equal(4, result.Sent);
            Assert.Equal(4, result.Received);
            Assert.Equal(0, result.PayloadErrors);
            Assert.Equal(0, result.PoolRented);
        }

        private static async Task<TcpLoopbackRunResult> InvokeScenarioForTestAsync(
            string resultName,
            string scenarioSuffix,
            int messageCount,
            int publishRateHz,
            int targetDurationSeconds,
            bool pacePublishes,
            bool openLoop)
        {
            MethodInfo? method = typeof(UdpLoopbackScenarioRunner).GetMethod(
                "RunScenarioForTestAsync",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            object? value = method!.Invoke(
                null,
                new object[]
                {
                    resultName,
                    scenarioSuffix,
                    messageCount,
                    publishRateHz,
                    targetDurationSeconds,
                    pacePublishes,
                    openLoop,
                    TcpLoopbackTransportBackend.Saea
                });
            Task<TcpLoopbackRunResult> task = Assert.IsAssignableFrom<Task<TcpLoopbackRunResult>>(value);
            return await task;
        }
    }
}
