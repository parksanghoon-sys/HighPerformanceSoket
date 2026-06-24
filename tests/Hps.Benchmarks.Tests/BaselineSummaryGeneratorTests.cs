using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineSummaryGeneratorTests
    {
        // summary comparison signal 은 hard gate 와 별도의 artifact 품질 신호다.
        // Summary model 이 comparison result 를 보존하지 않으면 writer/history 단계가 같은 계산을 중복 구현하게 된다.
        [Fact]
        public void Contract_BaselineSummaryExposesComparison()
        {
            Assert.NotNull(typeof(BaselineSummary).GetProperty("Comparison"));
        }

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

        // 같은 runner 와 같은 result-name별 case 구성이면 summary 는 comparison-compatible 이어야 한다.
        // load/open-loop scenario 는 서로 달라도 각각 별도 case 로 보존되어 정상 summary 를 mismatch 로 만들지 않는다.
        [Fact]
        public void Generate_WhenReportsShareRunnerAndEachKindHasStableCase_MarksComparisonCompatible()
        {
            BenchmarkRunIdentity identity = CreateIdentity("runner-a");
            BaselineReport[] reports =
            {
                CreateReport("a/load-01.json", "load", 230.0, 500.0, 1.0, 100.0, 1, 0, 3000, identity: identity),
                CreateReport("a/open-loop-01.json", "open-loop", 240.0, 600.0, 1.0, 100.0, 2, 0, 3000, "tcp-loopback-saea-baseline-open-loop", identity)
            };

            BaselineSummary summary = BaselineSummaryGenerator.Generate("a", reports);

            Assert.True(summary.Comparison.Compatible);
            Assert.Equal(0, summary.Comparison.UnknownRunnerCount);
            Assert.Equal(0, summary.Comparison.MismatchCount);
            Assert.NotNull(summary.Comparison.Key);
            Assert.Equal("runner-a", summary.Comparison.Key!.RunnerId);
            Assert.Equal(2, summary.Comparison.Key.Cases.Count);
            Assert.Equal("load", summary.Comparison.Key.Cases[0].ResultName);
            Assert.Equal("tcp-loopback-saea-baseline", summary.Comparison.Key.Cases[0].Scenario);
            Assert.Equal("open-loop", summary.Comparison.Key.Cases[1].ResultName);
            Assert.Equal("tcp-loopback-saea-baseline-open-loop", summary.Comparison.Key.Cases[1].Scenario);
            Assert.Equal(4096, summary.Comparison.Key.Cases[0].PayloadBytes);
            Assert.Equal(100.0, summary.Comparison.Key.Cases[0].TargetRateHz);
            Assert.Equal(30, summary.Comparison.Key.Cases[0].TargetDurationSeconds);
        }

        // legacy raw report 는 모든 metadata 가 unknown 으로 같아 보여도 비교 가능하다고 증명된 것이 아니다.
        // unknown-runner-count 와 mismatch 를 남겨 history 단계가 legacy artifact 를 compatible 로 오판하지 않게 한다.
        [Fact]
        public void Generate_WhenReportIdentityIsUnknown_MarksComparisonIncompatible()
        {
            BaselineReport[] reports =
            {
                CreateReport("a/load-01.json", "load", 230.0, 500.0, 1.0, 100.0, 1, 0, 3000)
            };

            BaselineSummary summary = BaselineSummaryGenerator.Generate("a", reports);

            Assert.True(summary.HardPassed);
            Assert.False(summary.Comparison.Compatible);
            Assert.Null(summary.Comparison.Key);
            Assert.Equal(1, summary.Comparison.UnknownRunnerCount);
            Assert.Contains(summary.Comparison.Mismatches, mismatch => mismatch.Code == "unknown-runner" && mismatch.SourcePath == "a/load-01.json");
        }

        // runner id 가 섞인 summary 는 같은 부하 수치라도 같은 비교군이 아니다.
        // 이 mismatch 는 warning-count 에 합산하지 않고 comparison mismatch 로만 남긴다.
        [Fact]
        public void Generate_WhenRunnerIdentityDiffers_RecordsComparisonMismatchWithoutWarning()
        {
            BaselineReport[] reports =
            {
                CreateReport("a/load-01.json", "load", 230.0, 500.0, 1.0, 100.0, 1, 0, 3000, identity: CreateIdentity("runner-a")),
                CreateReport("a/load-02.json", "load", 230.0, 500.0, 1.0, 100.0, 1, 0, 3000, identity: CreateIdentity("runner-b"))
            };

            BaselineSummary summary = BaselineSummaryGenerator.Generate("a", reports);

            Assert.True(summary.HardPassed);
            Assert.Equal(0, summary.WarningCount);
            Assert.False(summary.Comparison.Compatible);
            Assert.NotNull(summary.Comparison.Key);
            Assert.Contains(
                summary.Comparison.Mismatches,
                mismatch => mismatch.Code == "comparison-key-mismatch"
                    && mismatch.Field == "runner-id"
                    && mismatch.Expected == "runner-a"
                    && mismatch.Actual == "runner-b"
                    && mismatch.SourcePath == "a/load-02.json");
        }

        // source report 가 하나도 없으면 hard-passed=false 인 기존 정책과 함께 comparison 도 incompatible 이어야 한다.
        // 빈 summary 를 비교 가능한 baseline 으로 쓰면 이후 history trend 가 의미 없는 기준을 만들 수 있다.
        [Fact]
        public void Generate_WhenNoReports_MarksComparisonIncompatible()
        {
            BaselineSummary summary = BaselineSummaryGenerator.Generate("a", new BaselineReport[0]);

            Assert.False(summary.HardPassed);
            Assert.False(summary.Comparison.Compatible);
            Assert.Equal(0, summary.Comparison.UnknownRunnerCount);
            Assert.Contains(summary.Comparison.Mismatches, mismatch => mismatch.Code == "no-source-reports");
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
            int received,
            string? scenario = null,
            BenchmarkRunIdentity? identity = null,
            int payloadBytes = 4096,
            double targetRateHz = 100.0,
            int targetDurationSeconds = 30)
        {
            return new BaselineReport(
                sourcePath,
                resultName,
                scenario ?? "tcp-loopback-saea-baseline",
                payloadBytes,
                targetRateHz,
                targetDurationSeconds,
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
                0,
                identity);
        }

        private static BenchmarkRunIdentity CreateIdentity(string runnerId)
        {
            return new BenchmarkRunIdentity(
                BenchmarkRunIdentity.DefaultBenchmarkProfile,
                runnerId,
                BenchmarkRunIdentity.DefaultRunnerKind,
                BenchmarkRunIdentity.DefaultTransportBackend,
                "Windows",
                "X64",
                "X64",
                ".NET 9.0",
                16);
        }
    }
}
