using System;
using System.IO;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BenchmarkArtifactWorkflowTests
    {
        // CI benchmark artifact 는 D125 envelope signal 을 report-only 산출물로 함께 업로드해야 한다.
        // 이 테스트는 workflow 가 summary/history 생성 뒤, upload 전에 envelope.json/envelope.md 를 만드는지 고정한다.
        [Fact]
        public void Workflow_WhenReferenceHistoryExists_WritesEnvelopeComparisonArtifactsBeforeUpload()
        {
            string workflow = ReadBenchmarkArtifactWorkflow();

            int historyIndex = workflow.IndexOf("name: Write baseline history", StringComparison.Ordinal);
            int envelopeIndex = workflow.IndexOf("name: Write baseline envelope comparison", StringComparison.Ordinal);
            int uploadIndex = workflow.IndexOf("name: Upload benchmark artifacts", StringComparison.Ordinal);

            Assert.True(historyIndex >= 0);
            Assert.True(envelopeIndex > historyIndex);
            Assert.True(uploadIndex > envelopeIndex);
            Assert.Contains("$referenceHistory = \"docs/benchmarks/baselines/runners/$env:HPS_BENCHMARK_RUNNER_ID/history.json\"", workflow);
            Assert.Contains("Test-Path $referenceHistory", workflow);
            Assert.Contains("--compare-baseline-envelope \"$env:BENCH_SESSION_DIR/summary.json\"", workflow);
            Assert.Contains("--reference-history \"$referenceHistory\"", workflow);
            Assert.Contains("--envelope \"$env:BENCH_DATE_ROOT/envelope.json\"", workflow);
            Assert.Contains("--envelope-md \"$env:BENCH_DATE_ROOT/envelope.md\"", workflow);
        }

        // CI benchmark hard gate 가 실패해도 raw report 만 업로드하고 멈추면 실패 원인을 요약 artifact 로 볼 수 없다.
        // workflow 는 각 report writer 의 exit code 를 저장해 마지막에 job failure 로 되돌리되,
        // summary/history/envelope 작성과 upload 는 계속 진행해야 한다.
        [Fact]
        public void Workflow_WhenBaselineHardGateFails_StillWritesAnalysisArtifactsBeforeFinalFailure()
        {
            string workflow = ReadBenchmarkArtifactWorkflow();

            int baselineIndex = workflow.IndexOf("name: Run baseline suite", StringComparison.Ordinal);
            int summaryIndex = workflow.IndexOf("name: Write baseline summary", StringComparison.Ordinal);
            int historyIndex = workflow.IndexOf("name: Write baseline history", StringComparison.Ordinal);
            int envelopeIndex = workflow.IndexOf("name: Write baseline envelope comparison", StringComparison.Ordinal);
            int uploadIndex = workflow.IndexOf("name: Upload benchmark artifacts", StringComparison.Ordinal);
            int finalGateIndex = workflow.IndexOf("name: Fail if benchmark hard gate failed", StringComparison.Ordinal);

            Assert.True(baselineIndex >= 0);
            Assert.True(summaryIndex > baselineIndex);
            Assert.True(historyIndex > summaryIndex);
            Assert.True(envelopeIndex > historyIndex);
            Assert.True(uploadIndex > envelopeIndex);
            Assert.True(finalGateIndex > uploadIndex);
            Assert.Contains("BENCH_BASELINE_EXIT=", workflow);
            Assert.Contains("BENCH_SUMMARY_EXIT=", workflow);
            Assert.Contains("BENCH_HISTORY_EXIT=", workflow);
            Assert.Contains("BENCH_ENVELOPE_EXIT=", workflow);
            Assert.Contains("exit 0", workflow);
            Assert.Contains("if: always()", workflow);
        }

        private static string ReadBenchmarkArtifactWorkflow()
        {
            string root = FindRepositoryRoot();
            string workflowPath = Path.Combine(root, ".github", "workflows", "benchmark-artifacts.yml");
            return File.ReadAllText(workflowPath);
        }

        private static string FindRepositoryRoot()
        {
            string? current = AppContext.BaseDirectory;
            while (current != null)
            {
                string candidate = Path.Combine(current, ".github", "workflows", "benchmark-artifacts.yml");
                if (File.Exists(candidate))
                {
                    return current;
                }

                current = Directory.GetParent(current)?.FullName;
            }

            throw new InvalidOperationException("benchmark-artifacts.yml 파일을 찾을 수 없습니다.");
        }
    }
}
