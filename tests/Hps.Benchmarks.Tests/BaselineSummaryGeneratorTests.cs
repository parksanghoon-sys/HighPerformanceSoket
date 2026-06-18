using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineSummaryGeneratorTests
    {
        // hard gate 는 D070에서 유지하기로 한 delivery/drop/leak 조건만 집계한다.
        // latency 는 warning 후보일 뿐이므로 정상 latency/queue 조건에서는 hard pass 가 true 로 남아야 한다.
        [Fact]
        public void Generate_WhenReportsPassHardGate_ReturnsKindRangesWithoutWarnings()
        {
            BaselineReport[] reports =
            {
                CreateReport("a/load-01.json", "load", 221.6, 471.0, 0.93, 99.8, 1, 0, 3000),
                CreateReport("a/load-02.json", "load", 256.7, 924.1, 1.16, 100.0, 1, 0, 3000),
                CreateReport("a/open-loop-01.json", "open-loop", 229.0, 502.6, 0.65, 99.9, 2, 0, 3000),
                CreateReport("a/open-loop-02.json", "open-loop", 274.3, 1005.5, 1.15, 100.0, 3, 0, 3000)
            };

            BaselineSummary summary = BaselineSummaryGenerator.Generate("a", reports);

            Assert.True(summary.HardPassed);
            Assert.Equal(4, summary.SourceReportCount);
            Assert.Equal(0, summary.HardFailureCount);
            Assert.Equal(0, summary.WarningCount);
            Assert.NotNull(summary.Load);
            Assert.NotNull(summary.OpenLoop);
            Assert.Equal(221.6, summary.Load!.P50Min, 1);
            Assert.Equal(924.1, summary.Load.P99Max, 1);
            Assert.Equal(697.6, summary.Load.P99Median, 1);
            Assert.Equal(2, summary.OpenLoop!.RunCount);
            Assert.Equal(3, summary.OpenLoop.TcpHighWatermarkMax);
        }

        // sent/received/drop/pool 조건 중 하나라도 깨지면 latency 와 무관하게 hard failure 로 집계한다.
        // summary command 의 exit code 는 이 hardPassed 값을 통해 Program wiring 에서 결정된다.
        [Fact]
        public void Generate_WhenReportFailsHardGate_CountsHardFailure()
        {
            BaselineReport[] reports =
            {
                CreateReport("a/load-01.json", "load", 230.0, 500.0, 1.0, 100.0, 1, 0, 3000),
                CreateReport("a/load-02.json", "load", 230.0, 500.0, 1.0, 100.0, 1, 1, 3000)
            };

            BaselineSummary summary = BaselineSummaryGenerator.Generate("a", reports);

            Assert.False(summary.HardPassed);
            Assert.Equal(1, summary.HardFailureCount);
        }

        // D070의 p99/HWM/actual-rate 기준은 hard failure 가 아니라 warning artifact 로만 남긴다.
        // warning 은 per-run 으로 발생해야 source file 을 통해 어떤 run 이 튄 것인지 바로 추적할 수 있다.
        [Fact]
        public void Generate_WhenSoftLimitIsExceeded_EmitsWarningButKeepsHardPass()
        {
            BaselineReport[] reports =
            {
                CreateReport("a/open-loop-01.json", "open-loop", 240.0, 1600.0, 2.1, 94.9, 8, 0, 3000)
            };

            BaselineSummary summary = BaselineSummaryGenerator.Generate("a", reports);

            Assert.True(summary.HardPassed);
            Assert.True(summary.WarningCount >= 4);
            Assert.Contains(summary.Warnings, warning => warning.Code == "open-loop-p99-latency-high" && warning.SourcePath == "a/open-loop-01.json");
            Assert.Contains(summary.Warnings, warning => warning.Code == "p99-growth-ratio-high" && warning.SourcePath == "a/open-loop-01.json");
            Assert.Contains(summary.Warnings, warning => warning.Code == "actual-rate-low" && warning.SourcePath == "a/open-loop-01.json");
            Assert.Contains(summary.Warnings, warning => warning.Code == "open-loop-tcp-hwm-high" && warning.SourcePath == "a/open-loop-01.json");
        }

        private static BaselineReport CreateReport(
            string sourcePath,
            string resultName,
            double p50,
            double p99,
            double growth,
            double actualRate,
            int tcpHwm,
            long dropped,
            int received)
        {
            return new BaselineReport(
                sourcePath,
                resultName,
                "tcp-loopback-saea-baseline",
                3000,
                3000,
                received,
                dropped,
                0,
                0,
                actualRate,
                p50,
                p99,
                growth,
                tcpHwm,
                0);
        }
    }
}
