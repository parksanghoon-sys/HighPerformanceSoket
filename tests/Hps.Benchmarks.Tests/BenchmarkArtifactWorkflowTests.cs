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
