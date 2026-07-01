using System.Globalization;
using System.IO;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineSummaryMarkdownWriterTests
    {
        // Markdown writer 는 JSON summary 를 대체하지 않고 사람이 리뷰할 핵심 수치만 압축해 보여준다.
        // 종류별 run count, p99 중심값, send queue HWM, hard gate 상태가 없으면 raw JSON을 다시 열어야 하므로 리뷰 artifact 가치가 떨어진다.
        // Markdown 은 TCP/UDP artifact 를 모두 보여주므로 JSON 호환 필드명(tcp-hwm-max)보다 protocol-neutral label 을 써야 한다.
        [Fact]
        public void Write_WhenSummaryHasNoWarnings_WritesReviewSummary()
        {
            BaselineReport[] reports =
            {
                CreateReport("baseline/load-01.json", "load", 230.0, 500.0, 1.0, 99.9, 1),
                CreateReport("baseline/open-loop-01.json", "open-loop", 250.0, 600.0, 1.1, 100.0, 2)
            };
            BaselineSummary summary = BaselineSummaryGenerator.Generate("baseline", reports);

            string markdown = WriteMarkdown(summary);

            Assert.Contains("# Baseline Summary", markdown);
            Assert.Contains("- 입력 directory: `baseline`", markdown);
            Assert.Contains("- source report count: 2", markdown);
            Assert.Contains("- hard gate: PASS", markdown);
            Assert.Contains("| kind | runs | p50 median us | p99 median us | p99 max us | send queue HWM max | dropped total | pool rented max |", markdown);
            Assert.Contains("| load | 1 | 230 | 500 | 500 | 1 | 0 | 0 |", markdown);
            Assert.Contains("| open-loop | 1 | 250 | 600 | 600 | 2 | 0 | 0 |", markdown);
            Assert.Contains("- 없음", markdown);
        }

        // warning table 은 어떤 run 이 soft limit 을 넘겼는지 source-path 까지 보여줘야 한다.
        // 그렇지 않으면 summary.md를 보고도 다시 summary.json과 raw per-run JSON을 모두 추적해야 한다.
        [Fact]
        public void Write_WhenSummaryHasWarnings_WritesWarningRowsWithSourcePath()
        {
            BaselineReport[] reports =
            {
                CreateReport("baseline/open-loop-01.json", "open-loop", 250.0, 1600.0, 2.1, 94.0, 8)
            };
            BaselineSummary summary = BaselineSummaryGenerator.Generate("baseline", reports);

            string markdown = WriteMarkdown(summary);

            Assert.Contains("- warning count: 4", markdown);
            Assert.Contains("| code | kind | metric | value | threshold | source |", markdown);
            Assert.Contains("| open-loop-p99-latency-high | open-loop | p99-latency-us | 1600 | 1508.3 | `baseline/open-loop-01.json` |", markdown);
            Assert.Contains("| p99-growth-ratio-high | open-loop | p99-latency-growth-ratio | 2.1 | 2 | `baseline/open-loop-01.json` |", markdown);
            Assert.Contains("| actual-rate-low | open-loop | actual-rate-hz | 94 | 95 | `baseline/open-loop-01.json` |", markdown);
            Assert.Contains("| open-loop-tcp-hwm-high | open-loop | tcp-pending-send-queue-high-watermark | 8 | 8 | `baseline/open-loop-01.json` |", markdown);
        }

        // Markdown 은 리뷰 보조 artifact 이므로 comparison 여부와 기준 key 를 사람이 바로 볼 수 있어야 한다.
        // JSON 값과 달라지지 않도록 같은 BaselineSummary.Comparison 에서 출력한다.
        [Fact]
        public void Write_WhenSummaryHasComparison_WritesComparisonSection()
        {
            BaselineReport[] reports =
            {
                CreateReport("baseline/load-01.json", "load", 230.0, 500.0, 1.0, 99.9, 1, CreateIdentity("runner-a"))
            };
            BaselineSummary summary = BaselineSummaryGenerator.Generate("baseline", reports);

            string markdown = WriteMarkdown(summary);

            Assert.Contains("## Comparison", markdown);
            Assert.Contains("- compatible: true", markdown);
            Assert.Contains("- runner-id: runner-a", markdown);
            Assert.Contains("- runner-kind: local", markdown);
            Assert.Contains("| result | scenario | payload bytes | target rate hz | target duration seconds |", markdown);
            Assert.Contains("| load | tcp-loopback-saea-baseline | 4096 | 100 | 30 |", markdown);
            Assert.Contains("- mismatch: 없음", markdown);
        }

        // 실제 2026-06-18 baseline artifact 처럼 identity metadata 가 없는 summary 는 comparison key 를 만들 수 없다.
        // 이 경로가 NRE 없이 Markdown 에 `comparison-key: 없음`과 unknown-runner 원인을 남기는지 고정한다.
        [Fact]
        public void Write_WhenComparisonKeyIsNull_WritesNullKeyAndUnknownRunnerMismatch()
        {
            BaselineReport[] reports =
            {
                CreateReport("baseline/load-01.json", "load", 230.0, 500.0, 1.0, 99.9, 1)
            };
            BaselineSummary summary = BaselineSummaryGenerator.Generate("baseline", reports);

            string markdown = WriteMarkdown(summary);

            Assert.Contains("## Comparison", markdown);
            Assert.Contains("- compatible: false", markdown);
            Assert.Contains("- unknown-runner-count: 1", markdown);
            Assert.Contains("- comparison-key: 없음", markdown);
            Assert.Contains("| unknown-runner | runner-identity | known | unknown | `baseline/load-01.json` |", markdown);
        }

        private static string WriteMarkdown(BaselineSummary summary)
        {
            using (StringWriter writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                BaselineSummaryMarkdownWriter.Write(writer, summary);
                return writer.ToString();
            }
        }

        private static BaselineReport CreateReport(
            string sourcePath,
            string resultName,
            double p50,
            double p99,
            double growth,
            double actualRate,
            int tcpHwm,
            BenchmarkRunIdentity? identity = null)
        {
            return new BaselineReport(
                sourcePath,
                resultName,
                "tcp-loopback-saea-baseline",
                4096,
                100.0,
                30,
                3000,
                3000,
                3000,
                0,
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
