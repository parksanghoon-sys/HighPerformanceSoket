# io_uring Benchmark Artifact Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Linux runner 에서 `--backend iouring` TCP/UDP benchmark raw report 를 생성하는 수동 GitHub Actions artifact workflow 를 추가한다.

**Architecture:** 기존 Windows `benchmark-artifacts.yml`의 artifact-only 정책을 유지하되, Linux/io_uring 전용 workflow 로 분리한다. 기존 `Hps.Benchmarks` CLI와 raw report/summary/history writer 를 재사용하고, latency hard gate 나 default backend promotion 은 추가하지 않는다.

**Tech Stack:** GitHub Actions YAML, `ubuntu-latest`, .NET 9 SDK, bash, `Hps.Benchmarks`, xUnit workflow static tests.

## Global Constraints

- TFM은 `net9.0`, LangVersion은 C# 8.0이다.
- workflow 는 `workflow_dispatch` 전용으로 시작한다.
- benchmark backend 는 항상 `--backend iouring`이다.
- TCP/UDP protocol root 를 분리하고, 각 protocol root 바로 아래에 날짜 directory 를 둔다.
  `BaselineHistoryReader`는 입력 root 바로 아래의 날짜 directory 만 session 묶음으로 읽는다.
- hard latency gate, warning-as-failure, default backend promotion, fixed registration, zero-copy send 는 제외한다.
- 테스트에는 무엇을 검증하는지 한국어 주석을 둔다.

---

### Task 1: Workflow Static Contract

**Files:**
- Modify: `tests/Hps.Benchmarks.Tests/BenchmarkArtifactWorkflowTests.cs`
- Create: `.github/workflows/iouring-benchmark-artifacts.yml`

**Interfaces:**
- Produces: `.github/workflows/iouring-benchmark-artifacts.yml`
- Produces: workflow markers `ci-linux-iouring-x64-01`, `--backend iouring`, `--protocol tcp`, `--protocol udp`

- [x] **Step 1: Add failing workflow static test**

Add tests that read `.github/workflows/iouring-benchmark-artifacts.yml` and assert:

```csharp
Assert.Contains("runs-on: ubuntu-latest", workflow);
Assert.Contains("workflow_dispatch:", workflow);
Assert.Contains("HPS_BENCHMARK_RUNNER_ID: ci-linux-iouring-x64-01", workflow);
Assert.Contains("--baseline-suite \"$BENCH_TCP_SESSION_DIR\" --runs 1 --protocol tcp --backend iouring", workflow);
Assert.Contains("--baseline-suite \"$BENCH_UDP_SESSION_DIR\" --runs 1 --protocol udp --backend iouring", workflow);
Assert.Contains("actions/upload-artifact@v7.0.1", workflow);
Assert.Contains("tcp_root=\"${runner_root}/tcp\"", workflow);
Assert.Contains("tcp_date_root=\"${tcp_root}/${date_root_name}\"", workflow);
```

- [x] **Step 2: Run focused Red test**

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BenchmarkArtifactWorkflowTests -v minimal
```

Expected: new test fails because `iouring-benchmark-artifacts.yml` is missing.

- [x] **Step 3: Add workflow**

Create `.github/workflows/iouring-benchmark-artifacts.yml` with:

- `workflow_dispatch`
- `runs-on: ubuntu-latest`
- .NET restore/build
- TCP baseline/summary/history sequence
- UDP baseline/summary/history sequence
- `summary.md` and `dotnet-info.txt`
- `actions/upload-artifact@v7.0.1`
- final exit-code gate after upload

- [x] **Step 4: Run focused Green test**

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BenchmarkArtifactWorkflowTests -v minimal
```

Expected: workflow tests pass.

### Task 2: State Docs And Verification

**Files:**
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/changelog/2026-07.md`
- Modify: `docs/agent-state/decisions/2026-07.md`

**Interfaces:**
- Consumes: workflow added in Task 1.
- Produces: D147 implementation state and next remote artifact review point.

- [x] **Step 1: Run full verification**

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore -v minimal
dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal
git diff --check
```

Expected: build warning 0/error 0, all tests pass, diff check clean.

- [x] **Step 2: Update state docs**

Record D147:

- `iouring-benchmark-artifacts.yml` is workflow_dispatch-only.
- It creates TCP/UDP `--backend iouring` artifacts on Linux.
- It does not promote io_uring as default and does not add hard latency gates.
- Next step is remote workflow dispatch artifact review after push.

- [x] **Step 3: Commit**

```powershell
git add .github\workflows\iouring-benchmark-artifacts.yml tests\Hps.Benchmarks.Tests\BenchmarkArtifactWorkflowTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs\agent-state\changelog\2026-07.md docs\agent-state\decisions\2026-07.md docs\superpowers\specs\2026-07-01-iouring-benchmark-artifact-workflow-design.md docs\superpowers\plans\2026-07-01-iouring-benchmark-artifact-workflow.md
git commit -m "ci: add iouring benchmark artifact workflow"
```

### Task 3: Remote Artifact Review

**Files:**
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/changelog/2026-07.md`
- Modify: `docs/agent-state/decisions/2026-07.md`
- Modify: `docs/superpowers/specs/2026-07-01-iouring-benchmark-artifact-workflow-design.md`

**Interfaces:**
- Consumes: GitHub Actions artifact `iouring-benchmark-artifacts-2026-07-01-github-28486254926-1`.
- Produces: D148 evidence acceptance and next candidate re-evaluation point.

- [x] **Step 1: Run workflow after push**

```powershell
gh workflow run iouring-benchmark-artifacts.yml --ref master
gh run watch 28486254926 --exit-status --interval 20
```

Expected: workflow succeeds and TCP/UDP baseline, summary, history steps return exit code 0.

- [x] **Step 2: Download and inspect artifact**

Expected artifact shape:

```text
tcp/2026-07-01/session-01/{load-01.json,open-loop-01.json,summary.json,summary.md}
tcp/history.json
tcp/history.md
udp/2026-07-01/session-01/{load-01.json,open-loop-01.json,summary.json,summary.md}
udp/history.json
udp/history.md
summary.md
dotnet-info.txt
```

- [x] **Step 3: Record D148 state**

Record that TCP/UDP hard gates passed, TCP p99 warnings remain report-only, and the next step is io_uring follow-up candidate re-evaluation rather than immediate optimization/default promotion.
