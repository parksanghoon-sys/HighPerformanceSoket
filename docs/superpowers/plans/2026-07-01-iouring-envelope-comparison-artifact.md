# io_uring Envelope Comparison Artifact Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Linux `io_uring` benchmark workflow 에 protocol별 baseline envelope comparison artifact 생성을 추가한다.

**Architecture:** 기존 `--compare-baseline-envelope` command 와 Windows benchmark workflow 의 report-only 정책을 재사용한다. TCP와 UDP는 protocol root 가 다르므로 reference history, output envelope, exit code env var 를 protocol별로 분리한다.

**Tech Stack:** GitHub Actions YAML, .NET 9.0 benchmark CLI, xUnit workflow static tests.

## Global Constraints

- C# 변경은 LangVersion 8.0 호환 문법만 사용한다.
- 기존 `warning-count`, summary/history schema, hard gate 정책은 변경하지 않는다.
- envelope signal 은 report-only 이며 workflow failure 로 승격하지 않는다.
- reference history 가 없으면 envelope comparison 은 skip 하고 exit code 0으로 기록한다.
- `.claude/review/*` untracked 파일은 stage 하지 않는다.

---

### Task 1: io_uring workflow envelope comparison

**Files:**
- Modify: `tests/Hps.Benchmarks.Tests/BenchmarkArtifactWorkflowTests.cs`
- Modify: `.github/workflows/iouring-benchmark-artifacts.yml`
- Create: `docs/superpowers/specs/2026-07-01-iouring-envelope-comparison-artifact-design.md`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `DECISIONS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify: `docs/agent-state/decisions/2026-07.md`
- Modify: `docs/agent-state/changelog/2026-07.md`
- Modify: `docs/benchmarks/baselines/index.md`

**Interfaces:**
- Consumes: `Hps.Benchmarks --compare-baseline-envelope <candidate-json> --reference-history <reference-history-json> --envelope <output-json> [--envelope-md <output-md>]`
- Produces: `IOURING_TCP_ENVELOPE_EXIT`, `IOURING_UDP_ENVELOPE_EXIT`, protocol root `envelope.json`/`envelope.md` when reference exists.

- [x] **Step 1: Write the failing workflow static test**

Add `IoUringWorkflow_WhenReferenceHistoryExists_WritesProtocolEnvelopeComparisonArtifactsBeforeUpload`.
The test checks:

```csharp
Assert.Contains("tcp_reference_history=\"docs/benchmarks/baselines/runners/${HPS_BENCHMARK_RUNNER_ID}/tcp/history.json\"", workflow);
Assert.Contains("--compare-baseline-envelope \"$BENCH_TCP_SESSION_DIR/summary.json\"", workflow);
Assert.Contains("--envelope \"$BENCH_TCP_ROOT/envelope.json\"", workflow);
Assert.Contains("IOURING_TCP_ENVELOPE_EXIT=", workflow);
```

- [x] **Step 2: Run the Red test**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BenchmarkArtifactWorkflowTests.IoUringWorkflow_WhenReferenceHistoryExists_WritesProtocolEnvelopeComparisonArtifactsBeforeUpload -v minimal
```

Expected: FAIL because `Write TCP io_uring envelope comparison` is missing.

- [x] **Step 3: Add protocol envelope steps**

Add TCP and UDP steps after each protocol history step.
Each step:

- builds protocol-specific reference history path,
- runs `--compare-baseline-envelope` if reference exists,
- writes protocol-specific exit env var,
- skips with exit 0 when reference does not exist.

- [x] **Step 4: Run focused workflow tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BenchmarkArtifactWorkflowTests -v minimal
```

Expected: PASS, 6 tests.

- [x] **Step 5: Final verification**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore -v minimal
dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal
git diff --check
```

Expected: build warning 0/error 0, all tests pass, diff check has no whitespace errors.

- [x] **Step 6: Commit**

Stage only the current unit files:

```powershell
git add .github/workflows/iouring-benchmark-artifacts.yml tests/Hps.Benchmarks.Tests/BenchmarkArtifactWorkflowTests.cs docs/superpowers/specs/2026-07-01-iouring-envelope-comparison-artifact-design.md docs/superpowers/plans/2026-07-01-iouring-envelope-comparison-artifact.md CURRENT_PLAN.md TODOS.md DECISIONS.md CHANGELOG_AGENT.md docs/agent-state/decisions/2026-07.md docs/agent-state/changelog/2026-07.md docs/benchmarks/baselines/index.md
git commit -m "ci: add iouring envelope artifact comparison"
```
