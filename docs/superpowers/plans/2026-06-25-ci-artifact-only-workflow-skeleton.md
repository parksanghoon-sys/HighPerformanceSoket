# CI Artifact-Only Workflow Skeleton Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** GitHub Actions에서 benchmark raw report, summary, history를 artifact로 남기되 latency/HWM/warning을 CI 실패 조건으로 승격하지 않는 최소 workflow skeleton을 추가한다.

**Architecture:** workflow는 Windows runner 1개에서 restore/build/test를 먼저 수행한 뒤, 기존 `Hps.Benchmarks` CLI만 호출해 benchmark artifact를 만든다. 현재 `BaselineHistoryReader`는 date root 아래 `session-NN`만 읽으므로, GitHub run id는 디렉터리명이 아니라 upload artifact 이름에 넣고 workspace 내부는 `artifacts/benchmarks/runners/<runner-id>/<yyyy-mm-dd>/session-01/` 구조를 유지한다.

**Tech Stack:** GitHub Actions YAML, `actions/checkout@v7`, `actions/setup-dotnet@v5.3.0`, `actions/upload-artifact@v7.0.1`, .NET 9 SDK, PowerShell, existing `tests/Hps.Benchmarks` CLI.

---

## Scope

이번 plan은 D090 정책을 실제 workflow 구현 단위로 옮기기 위한 handoff 문서다. 다음 구현 단위는 workflow skeleton 추가와 그에 맞는 root 상태 문서 갱신까지 포함하되, benchmark CLI schema 변경이나 warning-as-failure 구현은 하지 않는다.

## File Structure

- Create: `.github/workflows/benchmark-artifacts.yml`
  - GitHub Actions workflow 정의. 기존 repo에는 `.github/workflows`가 없으므로 디렉터리와 파일을 새로 만든다.
  - `workflow_dispatch` 전용으로 시작한다. PR/push 자동 실행은 비용과 noise가 검토된 뒤 별도 단위에서 추가한다.
  - `HPS_BENCHMARK_RUNNER_ID=ci-windows-x64-01`, `HPS_BENCHMARK_RUNNER_KIND=ci`를 job env로 고정한다.
  - upload artifact 이름에는 GitHub run id/attempt를 포함한다.
- Modify: `CURRENT_PLAN.md`
  - workflow skeleton 구현 완료 여부와 다음 실행 지점을 반영한다.
- Modify: `TODOS.md`
  - plan 완료 항목을 Completed로 이동하고, 다음 Current TODO를 workflow skeleton 구현으로 둔다. workflow 구현 뒤에는 CI artifact smoke 또는 manual run 검토로 갱신한다.
- Modify: `CHANGELOG_AGENT.md`
  - 구현 결과와 검증 명령을 기록한다.
- Modify: `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`
  - D090 보조 결정으로, GitHub artifact의 run id는 upload artifact 이름에 두고 내부 history-compatible directory는 `session-01`로 유지한다는 결정을 기록한다.

## Task 1: Workflow Skeleton

**Files:**
- Create: `.github/workflows/benchmark-artifacts.yml`

- [ ] **Step 1: Verify there is no existing workflow to merge with**

Run:

```powershell
if (Test-Path .github\workflows) { Get-ChildItem .github\workflows } else { "no workflows" }
```

Expected:

```text
no workflows
```

If a workflow already exists, stop and merge this skeleton into the existing CI structure instead of creating a parallel workflow with overlapping restore/build/test responsibility.

- [ ] **Step 2: Create the workflow file**

Create `.github/workflows/benchmark-artifacts.yml` with exactly this content:

```yaml
name: Benchmark Artifacts

on:
  workflow_dispatch:

permissions:
  contents: read

jobs:
  benchmark-artifacts:
    name: benchmark artifacts (windows)
    runs-on: windows-latest
    timeout-minutes: 30
    env:
      DOTNET_CLI_TELEMETRY_OPTOUT: "1"
      DOTNET_NOLOGO: "1"
      HPS_BENCHMARK_RUNNER_ID: ci-windows-x64-01
      HPS_BENCHMARK_RUNNER_KIND: ci

    steps:
      - name: Checkout
        uses: actions/checkout@v7

      - name: Setup .NET
        uses: actions/setup-dotnet@v5.3.0
        with:
          dotnet-version: 9.0.x

      - name: Restore
        run: dotnet restore HighPerformanceSocket.slnx

      - name: Build
        run: dotnet build HighPerformanceSocket.slnx --no-restore

      - name: Test
        run: dotnet test HighPerformanceSocket.slnx --no-build --no-restore

      - name: Prepare benchmark artifact paths
        shell: pwsh
        run: |
          $dateRootName = Get-Date -Format "yyyy-MM-dd"
          $runnerRoot = "artifacts/benchmarks/runners/$env:HPS_BENCHMARK_RUNNER_ID"
          $dateRoot = "$runnerRoot/$dateRootName"
          $sessionDir = "$dateRoot/session-01"
          $artifactName = "benchmark-artifacts-$($env:HPS_BENCHMARK_RUNNER_ID)-$dateRootName-github-$($env:GITHUB_RUN_ID)-$($env:GITHUB_RUN_ATTEMPT)"

          New-Item -ItemType Directory -Path $sessionDir -Force | Out-Null

          "BENCH_DATE_ROOT=$dateRoot" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8
          "BENCH_SESSION_DIR=$sessionDir" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8
          "BENCH_ARTIFACT_NAME=$artifactName" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8

      - name: Run baseline suite
        shell: pwsh
        run: dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj --no-build --no-restore -- --baseline-suite "$env:BENCH_SESSION_DIR" --runs 3

      - name: Write baseline summary
        shell: pwsh
        run: dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj --no-build --no-restore -- --summarize-baseline "$env:BENCH_SESSION_DIR" --summary "$env:BENCH_SESSION_DIR/summary.json" --summary-md "$env:BENCH_SESSION_DIR/summary.md"

      - name: Write baseline history
        shell: pwsh
        run: dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj --no-build --no-restore -- --summarize-baseline-history "$env:BENCH_DATE_ROOT" --history "$env:BENCH_DATE_ROOT/history.json" --history-md "$env:BENCH_DATE_ROOT/history.md"

      - name: Upload benchmark artifacts
        if: always()
        uses: actions/upload-artifact@v7.0.1
        with:
          name: ${{ env.BENCH_ARTIFACT_NAME }}
          path: ${{ env.BENCH_DATE_ROOT }}
          if-no-files-found: error
          retention-days: 14
```

This skeleton intentionally uses `workflow_dispatch` only. That keeps benchmark cost under human control until the first manual artifact is reviewed.

- [ ] **Step 3: Run static workflow content checks**

Run:

```powershell
Select-String -Path .github\workflows\benchmark-artifacts.yml -Pattern "workflow_dispatch|ci-windows-x64-01|HPS_BENCHMARK_RUNNER_KIND|actions/upload-artifact@v7.0.1|warning-count|latency|push|pull_request"
```

Expected:

- Matches exist for `workflow_dispatch`, `ci-windows-x64-01`, `HPS_BENCHMARK_RUNNER_KIND`, and `actions/upload-artifact@v7.0.1`.
- No matches exist for `push`, `pull_request`, `warning-count`, or `latency`.

If `warning-count` or `latency` appears in the workflow, remove that logic. D090 keeps those values report-only.

- [ ] **Step 4: Run a lightweight static policy check**

Run:

```powershell
@'
from pathlib import Path
path = Path(".github/workflows/benchmark-artifacts.yml")
text = path.read_text(encoding="utf-8")
required = [
    "name: Benchmark Artifacts",
    "workflow_dispatch:",
    "HPS_BENCHMARK_RUNNER_ID: ci-windows-x64-01",
    "actions/upload-artifact@v7.0.1",
]
for item in required:
    if item not in text:
        raise SystemExit(f"missing: {item}")
if "pull_request:" in text or "push:" in text:
    raise SystemExit("automatic trigger is out of scope")
print("workflow-static-check: ok")
'@ | python -
```

Expected:

```text
workflow-static-check: ok
```

This check does not prove GitHub Actions accepts every expression. It catches missing required policy markers and out-of-scope automatic triggers before commit.

- [ ] **Step 5: Commit the workflow skeleton**

Run:

```powershell
git add .github/workflows/benchmark-artifacts.yml
git commit -m "ci: add benchmark artifact workflow skeleton"
```

Expected:

```text
[master <hash>] ci: add benchmark artifact workflow skeleton
```

## Task 2: State Documents

**Files:**
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/decisions/2026-06.md`

- [ ] **Step 1: Add the implementation decision**

Add a decision entry:

```markdown
D091 — CI benchmark workflow artifact upload name carries GitHub run identity, while the uploaded directory keeps history-compatible date/session layout.
```

Record the reason:

```markdown
`BaselineHistoryReader` currently discovers only date roots and `session-NN` children. Therefore the GitHub `run_id`/`run_attempt` must not replace the session directory name in the uploaded tree. The workflow uses `session-01` inside its ephemeral workspace and puts `github-<run_id>-<run_attempt>` in the artifact name.
```

- [ ] **Step 2: Update `CURRENT_PLAN.md`**

Replace the next execution point with:

```markdown
다음 작업은 CI artifact-only workflow skeleton 구현 검토 또는 첫 manual workflow run 결과 반영이다.
workflow 는 `workflow_dispatch` 전용으로 시작하며, GitHub run id 는 upload artifact 이름에만 넣고 내부 디렉터리는
`artifacts/benchmarks/runners/ci-windows-x64-01/<yyyy-mm-dd>/session-01/` 구조를 유지한다.
```

- [ ] **Step 3: Update `TODOS.md`**

Move the workflow skeleton plan item to Completed and add this Current TODO:

```markdown
- [ ] CI artifact-only workflow skeleton 을 구현한다.
  - 목적: D090/D091 정책대로 GitHub Actions에서 benchmark raw/summary/history artifact 를 생성하고 업로드한다.
  - 범위: `.github/workflows/benchmark-artifacts.yml`, root 상태 문서, D091 decision 기록.
  - 현재 판단: 자동 push/PR trigger 없이 `workflow_dispatch` 전용으로 시작한다.
  - 검증: workflow static marker scan, `git diff --check`, solution build/test.
```

- [ ] **Step 4: Update `CHANGELOG_AGENT.md`**

Add the completed plan entry:

```markdown
## 2026-06-25 (Codex - CI artifact-only workflow skeleton plan)

### 작업 단위
- D090 정책을 실제 GitHub Actions workflow skeleton 으로 옮기기 위한 구현 계획을 작성했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-25-ci-artifact-only-workflow-skeleton.md`:
  workflow trigger, runner identity, artifact path, benchmark CLI command sequence, upload policy를 구현 단계로 정리했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D091로 GitHub run id는 upload artifact 이름에 두고 내부 history-compatible directory는 `session-01`로 유지하는 결정을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 workflow skeleton 구현으로 갱신했다.
```

- [ ] **Step 5: Validate docs**

Run:

```powershell
$patterns = @(
  "TBD",
  "TODO_PLACEHOLDER",
  ([string]::Concat("미", "정")),
  ([string]::Concat("나중에 ", "정")),
  ([string]::Concat("warning-count", ".*", "failure"))
)
$files = @(
  "docs/superpowers/plans/2026-06-25-ci-artifact-only-workflow-skeleton.md",
  "CURRENT_PLAN.md",
  "TODOS.md",
  "DECISIONS.md",
  "docs/agent-state/decisions/2026-06.md",
  "CHANGELOG_AGENT.md"
)
foreach ($pattern in $patterns) {
  rg -n $pattern $files
}
git diff --check
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
```

Expected:

- Placeholder scan has no new actionable placeholder in the plan/current state.
- `git diff --check` exits 0.
- Build exits 0 with warning 0/error 0.
- Test exits 0 with 269 passing tests.

- [ ] **Step 6: Commit the state docs**

Run:

```powershell
git add CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs/agent-state/decisions/2026-06.md docs/superpowers/plans/2026-06-25-ci-artifact-only-workflow-skeleton.md
git commit -m "docs: plan ci benchmark workflow skeleton"
```

Expected:

```text
[master <hash>] docs: plan ci benchmark workflow skeleton
```

## Validation Notes

- Do not run `dotnet build` and `dotnet test --no-build` in parallel. A previous cycle showed that parallel execution can race on test output DLLs and produce false `FileNotFoundException` failures.
- A successful local validation of this plan does not prove the GitHub-hosted runner can execute the benchmark within the timeout. The first manual `workflow_dispatch` run remains the real CI environment validation.
- If the first manual run times out, do not raise latency/HWM/warning to failure. Reduce benchmark runs or split benchmark execution in a separate reviewed plan.

## Self-Review

- Spec coverage: D090 runner id/kind, artifact-only exit policy, local/CI separation, and upload artifact policy are covered by Task 1.
- Scope check: no benchmark CLI schema change, no warning-as-failure, no latency hard gate, no automatic PR/push trigger.
- Type/path consistency: workflow uses `ci-windows-x64-01`, `ci`, `artifacts/benchmarks/runners/<runner-id>/<yyyy-mm-dd>/session-01/`, and the existing benchmark CLI commands.
