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

        // io_uring benchmark workflow 는 Windows CI benchmark 와 분리된 Linux 수동 evidence 경로여야 한다.
        // 이 테스트는 runner identity, trigger, upload action, backend selector 가 의도한 계약에서 벗어나지 않도록 고정한다.
        [Fact]
        public void IoUringWorkflow_WhenCreated_UsesLinuxManualRunnerAndIoUringBackend()
        {
            string workflow = ReadIoUringBenchmarkArtifactWorkflow();

            Assert.Contains("workflow_dispatch:", workflow);
            Assert.Contains("runs-on: ubuntu-latest", workflow);
            Assert.Contains("HPS_BENCHMARK_RUNNER_ID: ci-linux-iouring-x64-01", workflow);
            Assert.Contains("HPS_BENCHMARK_RUNNER_KIND: ci", workflow);
            Assert.Contains("actions/upload-artifact@v7.0.1", workflow);
            Assert.DoesNotContain("pull_request:", workflow);
            Assert.DoesNotContain("schedule:", workflow);
        }

        // Linux io_uring benchmark evidence 는 같은 workflow 안에서 TCP/UDP를 분리된 root 로 만들어야 한다.
        // protocol 별 summary/history 가 섞이면 BaselineHistoryReader 가 서로 다른 profile/backend 결과를 같은 비교군으로 볼 수 있다.
        [Fact]
        public void IoUringWorkflow_WhenRun_WritesTcpAndUdpArtifactsBeforeFinalFailureGate()
        {
            string workflow = ReadIoUringBenchmarkArtifactWorkflow();

            int tcpBaselineIndex = workflow.IndexOf("name: Run TCP io_uring baseline suite", StringComparison.Ordinal);
            int tcpSummaryIndex = workflow.IndexOf("name: Write TCP io_uring summary", StringComparison.Ordinal);
            int tcpHistoryIndex = workflow.IndexOf("name: Write TCP io_uring history", StringComparison.Ordinal);
            int udpBaselineIndex = workflow.IndexOf("name: Run UDP io_uring baseline suite", StringComparison.Ordinal);
            int udpSummaryIndex = workflow.IndexOf("name: Write UDP io_uring summary", StringComparison.Ordinal);
            int udpHistoryIndex = workflow.IndexOf("name: Write UDP io_uring history", StringComparison.Ordinal);
            int uploadIndex = workflow.IndexOf("name: Upload io_uring benchmark artifacts", StringComparison.Ordinal);
            int finalGateIndex = workflow.IndexOf("name: Fail if io_uring benchmark artifact generation failed", StringComparison.Ordinal);

            Assert.True(tcpBaselineIndex >= 0);
            Assert.True(tcpSummaryIndex > tcpBaselineIndex);
            Assert.True(tcpHistoryIndex > tcpSummaryIndex);
            Assert.True(udpBaselineIndex > tcpHistoryIndex);
            Assert.True(udpSummaryIndex > udpBaselineIndex);
            Assert.True(udpHistoryIndex > udpSummaryIndex);
            Assert.True(uploadIndex > udpHistoryIndex);
            Assert.True(finalGateIndex > uploadIndex);
            Assert.Contains("--baseline-suite \"$BENCH_TCP_SESSION_DIR\" --runs 3 --protocol tcp --backend iouring", workflow);
            Assert.Contains("--baseline-suite \"$BENCH_UDP_SESSION_DIR\" --runs 3 --protocol udp --backend iouring", workflow);
            Assert.Contains("--summarize-baseline \"$BENCH_TCP_SESSION_DIR\" --summary \"$BENCH_TCP_SESSION_DIR/summary.json\"", workflow);
            Assert.Contains("--summarize-baseline \"$BENCH_UDP_SESSION_DIR\" --summary \"$BENCH_UDP_SESSION_DIR/summary.json\"", workflow);
            Assert.Contains("--summarize-baseline-history \"$BENCH_TCP_ROOT\" --history \"$BENCH_TCP_ROOT/history.json\"", workflow);
            Assert.Contains("--summarize-baseline-history \"$BENCH_UDP_ROOT\" --history \"$BENCH_UDP_ROOT/history.json\"", workflow);
            Assert.Contains("IOURING_TCP_BASELINE_EXIT=", workflow);
            Assert.Contains("IOURING_UDP_BASELINE_EXIT=", workflow);
            Assert.Contains("if: always()", workflow);
        }

        // D150의 p99 warning은 기존 전역 soft threshold 기준이므로 runner/profile scoped envelope artifact로 별도 해석해야 한다.
        // io_uring workflow도 Windows benchmark workflow처럼 reference history가 있을 때 protocol별 envelope를 생성해야 한다.
        [Fact]
        public void IoUringWorkflow_WhenReferenceHistoryExists_WritesProtocolEnvelopeComparisonArtifactsBeforeUpload()
        {
            string workflow = ReadIoUringBenchmarkArtifactWorkflow();

            int tcpHistoryIndex = workflow.IndexOf("name: Write TCP io_uring history", StringComparison.Ordinal);
            int tcpEnvelopeIndex = workflow.IndexOf("name: Write TCP io_uring envelope comparison", StringComparison.Ordinal);
            int udpHistoryIndex = workflow.IndexOf("name: Write UDP io_uring history", StringComparison.Ordinal);
            int udpEnvelopeIndex = workflow.IndexOf("name: Write UDP io_uring envelope comparison", StringComparison.Ordinal);
            int uploadIndex = workflow.IndexOf("name: Upload io_uring benchmark artifacts", StringComparison.Ordinal);

            Assert.True(tcpHistoryIndex >= 0);
            Assert.True(tcpEnvelopeIndex > tcpHistoryIndex);
            Assert.True(udpHistoryIndex > tcpEnvelopeIndex);
            Assert.True(udpEnvelopeIndex > udpHistoryIndex);
            Assert.True(uploadIndex > udpEnvelopeIndex);
            Assert.Contains("tcp_reference_history=\"docs/benchmarks/baselines/runners/${HPS_BENCHMARK_RUNNER_ID}/tcp/history.json\"", workflow);
            Assert.Contains("udp_reference_history=\"docs/benchmarks/baselines/runners/${HPS_BENCHMARK_RUNNER_ID}/udp/history.json\"", workflow);
            Assert.Contains("--compare-baseline-envelope \"$BENCH_TCP_SESSION_DIR/summary.json\"", workflow);
            Assert.Contains("--reference-history \"$tcp_reference_history\"", workflow);
            Assert.Contains("--envelope \"$BENCH_TCP_ROOT/envelope.json\"", workflow);
            Assert.Contains("--envelope-md \"$BENCH_TCP_ROOT/envelope.md\"", workflow);
            Assert.Contains("--compare-baseline-envelope \"$BENCH_UDP_SESSION_DIR/summary.json\"", workflow);
            Assert.Contains("--reference-history \"$udp_reference_history\"", workflow);
            Assert.Contains("--envelope \"$BENCH_UDP_ROOT/envelope.json\"", workflow);
            Assert.Contains("--envelope-md \"$BENCH_UDP_ROOT/envelope.md\"", workflow);
            Assert.Contains("IOURING_TCP_ENVELOPE_EXIT=", workflow);
            Assert.Contains("IOURING_UDP_ENVELOPE_EXIT=", workflow);
        }

        // BaselineHistoryReader는 입력 root 바로 아래의 날짜 directory만 session 묶음으로 읽는다.
        // protocol root가 날짜 root 안쪽에 있으면 history command가 session-01/summary.json을 발견하지 못하므로,
        // workflow의 artifact 구조는 protocol root 아래 날짜 child를 두는 형태로 고정한다.
        [Fact]
        public void IoUringWorkflow_WhenPreparingHistoryInputs_UsesProtocolRootWithDateChildren()
        {
            string workflow = ReadIoUringBenchmarkArtifactWorkflow();

            Assert.Contains("tcp_root=\"${runner_root}/tcp\"", workflow);
            Assert.Contains("udp_root=\"${runner_root}/udp\"", workflow);
            Assert.Contains("tcp_date_root=\"${tcp_root}/${date_root_name}\"", workflow);
            Assert.Contains("udp_date_root=\"${udp_root}/${date_root_name}\"", workflow);
            Assert.Contains("tcp_session_dir=\"${tcp_date_root}/session-01\"", workflow);
            Assert.Contains("udp_session_dir=\"${udp_date_root}/session-01\"", workflow);
            Assert.Contains("BENCH_TCP_ROOT=$tcp_root", workflow);
            Assert.Contains("BENCH_UDP_ROOT=$udp_root", workflow);
            Assert.DoesNotContain("tcp_root=\"${bench_root}/tcp\"", workflow);
            Assert.DoesNotContain("udp_root=\"${bench_root}/udp\"", workflow);
        }

        // mixed report는 schema v2 hard gate이므로 legacy TCP/UDP baseline aggregate와 별도 root에 보존한다.
        // 세 실행 중 하나가 실패해도 나머지 raw report를 수집하고 마지막 gate에서 누적 실패를 반환해야 한다.
        [Fact]
        public void IoUringBenchmarkWorkflow_WhenMixedGateRuns_WritesThreeIndependentMixedReports()
        {
            string workflow = ReadIoUringBenchmarkArtifactWorkflow();
            int udpEnvelopeIndex = workflow.IndexOf("name: Write UDP io_uring envelope comparison", StringComparison.Ordinal);
            int mixedGateIndex = workflow.IndexOf("name: Run io_uring mixed workload gate", StringComparison.Ordinal);
            int summaryIndex = workflow.IndexOf("name: Write io_uring benchmark summary", StringComparison.Ordinal);

            Assert.True(mixedGateIndex > udpEnvelopeIndex);
            Assert.True(summaryIndex > mixedGateIndex);

            string mixedGate = workflow.Substring(mixedGateIndex, summaryIndex - mixedGateIndex);
            string mixedCommand = "dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj --no-build --no-restore -- --mixed-load-open-loop --backend iouring --data-rate-hz 100 --duration-seconds 30 --subscribers 1 --report \"$BENCH_MIXED_SESSION_DIR/mixed-${run}.json\"";
            int initializeIndex = mixedGate.IndexOf("mixed_exit=0", StringComparison.Ordinal);
            int loopIndex = mixedGate.IndexOf("for run in 01 02 03", StringComparison.Ordinal);
            int commandIndex = mixedGate.IndexOf(mixedCommand, StringComparison.Ordinal);
            int failureIndex = mixedGate.IndexOf("mixed_exit=1", StringComparison.Ordinal);
            int exportIndex = mixedGate.IndexOf("IOURING_MIXED_EXIT=$mixed_exit", StringComparison.Ordinal);
            int continueIndex = mixedGate.IndexOf("exit 0", StringComparison.Ordinal);

            Assert.True(initializeIndex >= 0);
            Assert.True(loopIndex > initializeIndex);
            Assert.True(commandIndex > loopIndex);
            Assert.True(failureIndex > commandIndex);
            Assert.True(exportIndex > failureIndex);
            Assert.True(continueIndex > exportIndex);

            Assert.Contains("mixed_root=\"${runner_root}/mixed\"", workflow);
            Assert.Contains("mixed_date_root=\"${mixed_root}/${date_root_name}\"", workflow);
            Assert.Contains("mixed_session_dir=\"${mixed_date_root}/session-01\"", workflow);
            Assert.Contains("BENCH_MIXED_ROOT=$mixed_root", workflow);
            Assert.Contains("BENCH_MIXED_DATE_ROOT=$mixed_date_root", workflow);
            Assert.Contains("BENCH_MIXED_SESSION_DIR=$mixed_session_dir", workflow);
            Assert.Contains("for run in 01 02 03", mixedGate);
            Assert.Contains(mixedCommand, mixedGate);
            Assert.Contains("mixed_exit=1", mixedGate);
            Assert.Contains("continuing to collect remaining raw reports", mixedGate);
            Assert.Contains("IOURING_MIXED_EXIT=$mixed_exit", mixedGate);
            Assert.Contains("exit 0", mixedGate);
            Assert.DoesNotContain("exit 1", mixedGate);
            Assert.Contains("\"${IOURING_MIXED_EXIT:-1}\"", workflow);
            Assert.Contains("- Mixed report count: 3", workflow);
            Assert.Contains("- Mixed hard gate exit: ${IOURING_MIXED_EXIT:-not-run}", workflow);
            Assert.DoesNotContain("--summarize-baseline \"$BENCH_MIXED", workflow);
            Assert.DoesNotContain("--summarize-baseline-history \"$BENCH_MIXED", workflow);
        }

        // Linux contract workflow는 native tests와 실제 sample composition을 함께 빌드하되 solution/WPF로 범위를 넓히면 안 된다.
        // runtime test는 기존 io_uring test project에만 남겨 장기 실행 broker process 없이 backend 계약을 검증한다.
        [Fact]
        public void IoUringLinuxContractWorkflow_WhenRunOnLinux_RestoresAndBuildsOnlyExplicitLinuxSafeProjects()
        {
            string workflow = ReadIoUringLinuxContractWorkflow();

            Assert.Contains("dotnet restore tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj", workflow);
            Assert.Contains("dotnet restore samples/Hps.Sample.BrokerServer/Hps.Sample.BrokerServer.csproj", workflow);
            Assert.Contains("dotnet build tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj --no-restore", workflow);
            Assert.Contains("dotnet build samples/Hps.Sample.BrokerServer/Hps.Sample.BrokerServer.csproj --no-restore", workflow);
            Assert.Contains("dotnet test tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj", workflow);
            Assert.DoesNotContain("dotnet test samples/Hps.Sample.BrokerServer/Hps.Sample.BrokerServer.csproj", workflow);
            Assert.DoesNotContain("dotnet restore HighPerformanceSocket.slnx", workflow);
            Assert.DoesNotContain("dotnet build HighPerformanceSocket.slnx", workflow);
            Assert.DoesNotContain("EnableWindowsTargeting", workflow);
        }

        // D211 remote gate 는 test step 이 20분 workflow timeout 으로 cancelled 되어 TRX 없이 끝났다.
        // workflow 는 다음 native hang 을 짧게 실패시키고 diag/sequence evidence 를 artifact 에 남겨야 한다.
        [Fact]
        public void IoUringLinuxContractWorkflow_WhenTestsHang_WritesBlameHangDiagnostics()
        {
            string workflow = ReadIoUringLinuxContractWorkflow();

            Assert.Contains("--blame-hang", workflow);
            Assert.Contains("--blame-hang-timeout 2m", workflow);
            Assert.Contains("--blame-hang-dump-type none", workflow);
            Assert.Contains("--diag \"$IOURING_CONTRACT_ROOT/vstest-diag.log\"", workflow);
            Assert.Contains("Hang diagnostics: blame-hang timeout 2m, dump none", workflow);
            Assert.Contains("- VSTest diag: vstest-diag.log", workflow);
        }

        private static string ReadBenchmarkArtifactWorkflow()
        {
            string root = FindRepositoryRoot();
            string workflowPath = Path.Combine(root, ".github", "workflows", "benchmark-artifacts.yml");
            return File.ReadAllText(workflowPath);
        }

        private static string ReadIoUringBenchmarkArtifactWorkflow()
        {
            string root = FindRepositoryRoot();
            string workflowPath = Path.Combine(root, ".github", "workflows", "iouring-benchmark-artifacts.yml");
            if (!File.Exists(workflowPath))
                throw new InvalidOperationException("iouring-benchmark-artifacts.yml 파일을 찾을 수 없습니다.");

            return File.ReadAllText(workflowPath);
        }

        private static string ReadIoUringLinuxContractWorkflow()
        {
            string root = FindRepositoryRoot();
            string workflowPath = Path.Combine(root, ".github", "workflows", "iouring-linux-contract.yml");
            if (!File.Exists(workflowPath))
                throw new InvalidOperationException("iouring-linux-contract.yml 파일을 찾을 수 없습니다.");

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
