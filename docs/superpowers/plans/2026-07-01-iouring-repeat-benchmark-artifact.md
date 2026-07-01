# io_uring Repeat Benchmark Artifact Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `iouring-benchmark-artifacts.yml`이 TCP/UDP `--backend iouring` baseline suite 를 protocol 별 `--runs 3`으로 실행하게 한다.

**Architecture:** 기존 D147 workflow 구조, runner identity, artifact layout, summary/history command 를 그대로 유지한다. 변경은 workflow static contract test 와 YAML command 의 runs 값, root summary 표시, 상태 문서 기록으로 제한한다.

**Tech Stack:** GitHub Actions YAML, bash, .NET 9, `Hps.Benchmarks`, xUnit static workflow tests.

## Global Constraints

- TFM은 `net9.0`, LangVersion은 C# 8.0이다.
- workflow 는 `workflow_dispatch` 전용으로 유지한다.
- artifact 구조는 `tcp/<yyyy-mm-dd>/session-01`, `udp/<yyyy-mm-dd>/session-01`로 유지한다.
- latency hard gate, warning-as-failure, default backend promotion, fixed registration, zero-copy send 는 제외한다.
- 테스트에는 무엇을 검증하는지 한국어 주석을 유지한다.

---

### Task 1: io_uring Workflow Runs 3 Contract

**Files:**
- Modify: `tests/Hps.Benchmarks.Tests/BenchmarkArtifactWorkflowTests.cs`
- Modify: `.github/workflows/iouring-benchmark-artifacts.yml`

**Interfaces:**
- Consumes: existing workflow path `.github/workflows/iouring-benchmark-artifacts.yml`.
- Produces: TCP/UDP baseline commands with `--runs 3`.

- [x] **Step 1: Write the failing static test expectation**

In `IoUringWorkflow_WhenRun_WritesTcpAndUdpArtifactsBeforeFinalFailureGate`, replace the two `--runs 1` assertions with:

```csharp
Assert.Contains("--baseline-suite \"$BENCH_TCP_SESSION_DIR\" --runs 3 --protocol tcp --backend iouring", workflow);
Assert.Contains("--baseline-suite \"$BENCH_UDP_SESSION_DIR\" --runs 3 --protocol udp --backend iouring", workflow);
```

- [x] **Step 2: Run focused Red test**

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~IoUringWorkflow_WhenRun_WritesTcpAndUdpArtifactsBeforeFinalFailureGate -v minimal
```

Expected: assertion failure because the workflow still contains `--runs 1`.

- [x] **Step 3: Update workflow commands**

In `.github/workflows/iouring-benchmark-artifacts.yml`, change TCP and UDP baseline suite commands to:

```bash
dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj --no-build --no-restore -- --baseline-suite "$BENCH_TCP_SESSION_DIR" --runs 3 --protocol tcp --backend iouring
```

```bash
dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj --no-build --no-restore -- --baseline-suite "$BENCH_UDP_SESSION_DIR" --runs 3 --protocol udp --backend iouring
```

Also add this line to the root summary block after the backend line:

```bash
echo "- Runs per protocol: 3"
```

- [x] **Step 4: Run focused Green tests**

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BenchmarkArtifactWorkflowTests -v minimal
```

Expected: all workflow static tests pass.

### Task 2: State Docs And Verification

**Files:**
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/changelog/2026-07.md`
- Modify: `docs/agent-state/decisions/2026-07.md`
- Modify: `docs/superpowers/plans/2026-07-01-iouring-repeat-benchmark-artifact.md`

**Interfaces:**
- Consumes: Task 1 workflow behavior.
- Produces: D149 state, next remote artifact review point.

- [x] **Step 1: Update state docs**

Record D149:

- D148 artifact gate was path proof, not optimization proof.
- `iouring-benchmark-artifacts.yml` now uses `--runs 3` for TCP and UDP.
- Artifact layout and failure policy remain unchanged.
- Next step is user push followed by remote artifact review for source-report-count 6, hard-passed true, drop/leak 0.

- [x] **Step 2: Run full verification**

```powershell
git diff --check
dotnet build HighPerformanceSocket.slnx --no-restore -v minimal
dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal
```

Expected: diff check clean, build warning 0/error 0, all tests pass.

- [x] **Step 3: Commit**

```powershell
git add .github\workflows\iouring-benchmark-artifacts.yml tests\Hps.Benchmarks.Tests\BenchmarkArtifactWorkflowTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs\agent-state\changelog\2026-07.md docs\agent-state\decisions\2026-07.md docs\superpowers\specs\2026-07-01-iouring-repeat-benchmark-artifact-design.md docs\superpowers\plans\2026-07-01-iouring-repeat-benchmark-artifact.md
git commit -m "ci: repeat iouring benchmark artifact runs"
```
