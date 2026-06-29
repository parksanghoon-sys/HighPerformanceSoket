using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineEnvelopeComparisonGeneratorTests
    {
        // generator 는 D125의 핵심 정책 위치다.
        // writer 나 Program 에서 metric limit 을 계산하면 JSON/Markdown/CLI가 서로 다른 기준을 가질 수 있다.
        [Fact]
        public void Contract_BaselineEnvelopeComparisonGeneratorExists()
        {
            Assert.NotNull(typeof(BenchmarkCommandParser).Assembly.GetType("Hps.Benchmarks.BaselineEnvelopeComparisonGenerator"));
        }

        // 같은 comparison key 이고 candidate 가 reference 완충 limit 안에 있으면 signal 이 없어야 한다.
        // 이 경로가 정상 baseline review 의 noise-free 기본값이다.
        [Fact]
        public void Generate_WhenCandidateIsInsideReferenceEnvelope_ReturnsCompatibleWithoutSignals()
        {
            BaselineEnvelopeSource reference = CreateHistorySource(
                CreateSummary("ref/summary.json", true, "runner-a", CreateKind("load", 935.6, 100.0, 2), CreateKind("open-loop", 1077.4, 100.0, 2)));
            BaselineEnvelopeSource candidate = CreateSummarySource(
                CreateSummary("candidate/summary.json", true, "runner-a", CreateKind("load", 1000.0, 99.5, 3), CreateKind("open-loop", 1080.0, 99.2, 3)));

            BaselineEnvelopeComparison comparison = BaselineEnvelopeComparisonGenerator.Generate(reference, candidate);

            Assert.True(comparison.EnvelopeCompatible);
            Assert.Equal(0, comparison.SignalCount);
            Assert.Empty(comparison.Mismatches);
            BaselineEnvelopeMetricComparison loadP99 = FindMetric(comparison, "load", "p99-max-us");
            Assert.Equal(935.6, loadP99.Reference);
            Assert.Equal(1122.72, loadP99.Limit, 2);
            Assert.Equal(1000.0, loadP99.Candidate);
            Assert.False(loadP99.Signaled);
            Assert.NotNull(FindKind(comparison, "open-loop"));
        }

        // runner id 가 다르면 metric 값이 좋아도 같은 envelope 로 비교하지 않는다.
        // 이 mismatch 는 warning-count 가 아니라 envelope mismatch 로만 남는다.
        [Fact]
        public void Generate_WhenCandidateKeyDiffers_ReturnsMismatchWithoutMetricSignals()
        {
            BaselineEnvelopeSource reference = CreateHistorySource(
                CreateSummary("ref/summary.json", true, "runner-a", CreateKind("load", 935.6, 100.0, 2), CreateKind("open-loop", 1077.4, 100.0, 2)));
            BaselineEnvelopeSource candidate = CreateSummarySource(
                CreateSummary("candidate/summary.json", true, "runner-b", CreateKind("load", 900.0, 100.0, 1), CreateKind("open-loop", 1000.0, 100.0, 1)));

            BaselineEnvelopeComparison comparison = BaselineEnvelopeComparisonGenerator.Generate(reference, candidate);

            Assert.False(comparison.EnvelopeCompatible);
            Assert.Equal(0, comparison.SignalCount);
            BaselineEnvelopeMismatch mismatch = Assert.Single(comparison.Mismatches);
            Assert.Equal("envelope-key-mismatch", mismatch.Code);
            Assert.Equal("runner-id", mismatch.Field);
            Assert.Equal("runner-a", mismatch.Expected);
            Assert.Equal("runner-b", mismatch.Actual);
        }

        // upper-bound metric 이 limit 을 넘으면 envelope signal 로 기록한다.
        // 이 signal 은 process failure 가 아니라 review artifact 로만 남는다.
        [Fact]
        public void Generate_WhenCandidateP99ExceedsLimit_AddsUpperBoundSignal()
        {
            BaselineEnvelopeSource reference = CreateHistorySource(
                CreateSummary("ref/summary.json", true, "runner-a", CreateKind("load", 935.6, 100.0, 2), CreateKind("open-loop", 1077.4, 100.0, 2)));
            BaselineEnvelopeSource candidate = CreateSummarySource(
                CreateSummary("candidate/summary.json", true, "runner-a", CreateKind("load", 1200.0, 100.0, 2), CreateKind("open-loop", 1080.0, 100.0, 2)));

            BaselineEnvelopeComparison comparison = BaselineEnvelopeComparisonGenerator.Generate(reference, candidate);

            Assert.False(comparison.EnvelopeCompatible);
            BaselineEnvelopeSignal signal = comparison.Signals.Single(item => item.Kind == "load" && item.Metric == "p99-max-us");
            Assert.Equal("load", signal.Kind);
            Assert.Equal("p99-max-us", signal.Metric);
            Assert.Equal("upper", signal.Direction);
            Assert.Equal(1122.72, signal.Limit, 2);
            Assert.Equal(1200.0, signal.Candidate);
        }

        // actual rate 는 높을수록 좋은 lower-bound metric 이다.
        // reference min 에서 1Hz 완충을 둔 limit 아래로 내려가면 signal 을 남긴다.
        [Fact]
        public void Generate_WhenCandidateActualRateFallsBelowLimit_AddsLowerBoundSignal()
        {
            BaselineEnvelopeSource reference = CreateHistorySource(
                CreateSummary("ref/summary.json", true, "runner-a", CreateKind("load", 935.6, 100.0, 2), CreateKind("open-loop", 1077.4, 100.0, 2)));
            BaselineEnvelopeSource candidate = CreateSummarySource(
                CreateSummary("candidate/summary.json", true, "runner-a", CreateKind("load", 900.0, 93.5, 2), CreateKind("open-loop", 1000.0, 100.0, 2)));

            BaselineEnvelopeComparison comparison = BaselineEnvelopeComparisonGenerator.Generate(reference, candidate);

            Assert.False(comparison.EnvelopeCompatible);
            BaselineEnvelopeSignal signal = Assert.Single(comparison.Signals);
            Assert.Equal("load", signal.Kind);
            Assert.Equal("actual-rate-min-hz", signal.Metric);
            Assert.Equal("lower", signal.Direction);
            Assert.Equal(99.0, signal.Limit);
            Assert.Equal(93.5, signal.Candidate);
        }

        // reference history 에 compatible hard-passed summary 가 없으면 기준 envelope 를 만들 수 없다.
        // 빈 기준을 0으로 계산하면 모든 candidate 가 regression 으로 보이므로 명시 mismatch 로 중단한다.
        [Fact]
        public void Generate_WhenReferenceHasNoEligibleSummaries_ReturnsNoReferenceMismatch()
        {
            BaselineEnvelopeSource reference = CreateHistorySource(
                CreateSummary("ref/summary.json", false, "runner-a", CreateKind("load", 935.6, 100.0, 2), CreateKind("open-loop", 1077.4, 100.0, 2)));
            BaselineEnvelopeSource candidate = CreateSummarySource(
                CreateSummary("candidate/summary.json", true, "runner-a", CreateKind("load", 900.0, 100.0, 2), CreateKind("open-loop", 1000.0, 100.0, 2)));

            BaselineEnvelopeComparison comparison = BaselineEnvelopeComparisonGenerator.Generate(reference, candidate);

            Assert.False(comparison.EnvelopeCompatible);
            Assert.Equal(0, comparison.SignalCount);
            BaselineEnvelopeMismatch mismatch = Assert.Single(comparison.Mismatches);
            Assert.Equal("reference-no-eligible-summaries", mismatch.Code);
        }

        private static BaselineEnvelopeKindComparison FindKind(BaselineEnvelopeComparison comparison, string kind)
        {
            return comparison.Kinds.Single(item => item.Kind == kind);
        }

        private static BaselineEnvelopeMetricComparison FindMetric(BaselineEnvelopeComparison comparison, string kind, string metric)
        {
            return FindKind(comparison, kind).Metrics.Single(item => item.Metric == metric);
        }

        private static BaselineEnvelopeSource CreateHistorySource(params BaselineEnvelopeSummary[] summaries)
        {
            BaselineComparisonResult comparison = CreateComparison("runner-a");
            return new BaselineEnvelopeSource(BaselineEnvelopeSourceKind.History, "reference/history.json", summaries, comparison);
        }

        private static BaselineEnvelopeSource CreateSummarySource(BaselineEnvelopeSummary summary)
        {
            return new BaselineEnvelopeSource(BaselineEnvelopeSourceKind.Summary, summary.SummaryPath, new[] { summary }, summary.Comparison);
        }

        private static BaselineEnvelopeSummary CreateSummary(
            string path,
            bool hardPassed,
            string runnerId,
            BaselineKindSummary load,
            BaselineKindSummary openLoop)
        {
            return new BaselineEnvelopeSummary(
                path,
                6,
                hardPassed,
                0,
                load,
                openLoop,
                CreateComparison(runnerId));
        }

        private static BaselineKindSummary CreateKind(string kind, double p99Max, double actualRateMin, int tcpHwmMax)
        {
            return new BaselineKindSummary(
                kind,
                3,
                100.0,
                200.0,
                150.0,
                p99Max - 10.0,
                p99Max,
                p99Max - 5.0,
                1.0,
                1.1,
                actualRateMin,
                actualRateMin + 0.5,
                1,
                tcpHwmMax,
                0,
                0,
                0);
        }

        private static BaselineComparisonResult CreateComparison(string runnerId)
        {
            return new BaselineComparisonResult(
                true,
                new BaselineComparisonKey(
                    "tcp-loopback-saea-v1",
                    runnerId,
                    "local",
                    "SaeaTransport",
                    "Windows",
                    "X64",
                    "X64",
                    ".NET 9.0",
                    new List<BaselineComparisonCase>
                    {
                        new BaselineComparisonCase("load", "tcp-loopback-saea-load", 4096, 100.0, 30),
                        new BaselineComparisonCase("open-loop", "tcp-loopback-saea-open-loop", 4096, 100.0, 30)
                    }),
                0,
                new BaselineComparisonMismatch[0]);
        }
    }
}
