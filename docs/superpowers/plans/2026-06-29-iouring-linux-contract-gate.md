# Linux io_uring Contract Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Linux host 에서 io_uring TCP pump contract 를 검증하고 capability evidence artifact 를 남기는 opt-in workflow 를 추가한다.

**Architecture:** production transport 코드는 바꾸지 않는다. `Hps.Transport.IoUring.Tests`에 capability evidence output test 를 추가하고, 별도 `workflow_dispatch` GitHub Actions workflow 가 Linux에서 build/test/TRX/summary artifact 를 생성한다. D138은 UDP/zero-copy 후속 구현 전에 이 contract evidence gate 를 먼저 둔다는 결정이다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, GitHub Actions `ubuntu-latest`, TRX test artifact.

---

## File Structure

- `tests/Hps.Transport.IoUring.Tests/IoUringCapabilityEvidenceTests.cs`
  - Linux contract workflow artifact 에 capability status 를 남기는 test-only evidence surface 다.
  - production behavior 는 변경하지 않는다.
- `.github/workflows/iouring-linux-contract.yml`
  - 수동 실행 전용 Linux contract workflow 다.
  - restore/build/test 를 실행하고 TRX, `dotnet-info.txt`, `summary.md`를 upload artifact 로 남긴다.
- `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`,
  `docs/agent-state/changelog/2026-06.md`, `docs/agent-state/decisions/2026-06.md`
  - D138과 다음 실행 지점을 기록한다.

---

### Task 1: Capability Evidence Test

**Files:**
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringCapabilityEvidenceTests.cs`
- Modify: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `docs/agent-state/changelog/2026-06.md`

- [ ] **Step 1: Write the evidence test**

Create `tests/Hps.Transport.IoUring.Tests/IoUringCapabilityEvidenceTests.cs`.

```csharp
using System;
using System.Runtime.InteropServices;
using Xunit;
using Xunit.Abstractions;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringCapabilityEvidenceTests
    {
        private readonly ITestOutputHelper _output;

        public IoUringCapabilityEvidenceTests(ITestOutputHelper output)
        {
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        // Linux contract workflow 의 TRX artifact 에 현재 host 의 io_uring capability 판정을 남긴다.
        // 이 테스트는 production 동작을 새로 요구하지 않고, 기존 probe 결과가 known status 로 수렴하는지와
        // 원격 실행 후 사람이 available/unavailable 상태를 구분할 수 있는 evidence 를 제공한다.
        [Fact]
        public void GetStatus_WritesCapabilityEvidenceForLinuxContractGate()
        {
            IoUringCapabilityStatus status = IoUringCapabilityProbe.GetStatus();

            _output.WriteLine("io_uring capability status: " + status);
            _output.WriteLine("os description: " + RuntimeInformation.OSDescription);
            _output.WriteLine("os architecture: " + RuntimeInformation.OSArchitecture);
            _output.WriteLine("process architecture: " + RuntimeInformation.ProcessArchitecture);

            Assert.True(
                status == IoUringCapabilityStatus.UnsupportedOperatingSystem ||
                status == IoUringCapabilityStatus.Unavailable ||
                status == IoUringCapabilityStatus.Available);
        }
    }
}
```

- [ ] **Step 2: Run focused test**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringCapabilityEvidenceTests -v minimal
```

Expected: 1 test passes. This is test-only evidence hardening; no production Red is required because no production behavior changes.

- [ ] **Step 3: Run io_uring project tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --no-build --no-restore -v minimal
```

Expected: io_uring test count increases by 1 and all tests pass.

- [ ] **Step 4: Update state docs**

Record Task 1 in root state docs and changelog. Mention that this is test-only evidence hardening and does not close the Linux actual loopback backlog by itself.

- [ ] **Step 5: Commit**

```powershell
git add tests\Hps.Transport.IoUring.Tests\IoUringCapabilityEvidenceTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md docs\agent-state\changelog\2026-06.md
git commit -m "test: add iouring capability evidence"
```

---

### Task 2: Linux Contract Workflow

**Files:**
- Create: `.github/workflows/iouring-linux-contract.yml`
- Modify: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `docs/agent-state/changelog/2026-06.md`

- [ ] **Step 1: Add workflow**

Create `.github/workflows/iouring-linux-contract.yml`.

```yaml
name: io_uring Linux Contract

on:
  workflow_dispatch:

permissions:
  contents: read

jobs:
  iouring-linux-contract:
    name: io_uring contract (linux)
    runs-on: ubuntu-latest
    timeout-minutes: 20
    env:
      DOTNET_CLI_TELEMETRY_OPTOUT: "1"
      DOTNET_NOLOGO: "1"

    steps:
      - name: Checkout
        uses: actions/checkout@v7

      - name: Setup .NET
        uses: actions/setup-dotnet@v5.3.0
        with:
          dotnet-version: 9.0.x

      - name: Prepare artifact paths
        shell: bash
        run: |
          date_root="$(date -u +%F)"
          artifact_root="artifacts/iouring/linux-contract/${date_root}/run-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}"
          artifact_name="iouring-linux-contract-${date_root}-github-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}"

          mkdir -p "$artifact_root"
          {
            echo "IOURING_CONTRACT_ROOT=$artifact_root"
            echo "IOURING_ARTIFACT_NAME=$artifact_name"
          } >> "$GITHUB_ENV"

      - name: Restore
        run: dotnet restore HighPerformanceSocket.slnx

      - name: Build
        run: dotnet build HighPerformanceSocket.slnx --no-restore

      - name: Capture dotnet info
        shell: bash
        run: dotnet --info > "$IOURING_CONTRACT_ROOT/dotnet-info.txt"

      - name: Run io_uring tests
        shell: bash
        run: |
          set +e
          dotnet test tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj \
            --no-build \
            --no-restore \
            --logger "trx;LogFileName=iouring-tests.trx" \
            --results-directory "$IOURING_CONTRACT_ROOT"
          exit_code=$?
          echo "IOURING_TEST_EXIT=$exit_code" >> "$GITHUB_ENV"
          exit 0

      - name: Write contract summary
        if: always()
        shell: bash
        run: |
          {
            echo "# io_uring Linux contract"
            echo
            echo "- Runner OS: $(uname -a)"
            echo "- .NET SDK: $(dotnet --version)"
            echo "- Test command: dotnet test tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj --no-build --no-restore"
            echo "- Test exit code: ${IOURING_TEST_EXIT:-not-run}"
            echo "- TRX: iouring-tests.trx"
            echo
            echo "Capability unavailable is not a workflow failure. Inspect the TRX output from IoUringCapabilityEvidenceTests to determine whether TCP loopback tests used the real io_uring syscall path."
          } > "$IOURING_CONTRACT_ROOT/summary.md"

      - name: Upload io_uring contract artifact
        if: always()
        uses: actions/upload-artifact@v7.0.1
        with:
          name: ${{ env.IOURING_ARTIFACT_NAME }}
          path: ${{ env.IOURING_CONTRACT_ROOT }}
          if-no-files-found: error
          retention-days: 14

      - name: Fail if io_uring tests failed
        if: always()
        shell: bash
        run: |
          if [ "${IOURING_TEST_EXIT:-1}" != "0" ]; then
            echo "io_uring tests failed with exit code ${IOURING_TEST_EXIT:-not-run}"
            exit 1
          fi

          echo "io_uring Linux contract completed successfully."
```

- [ ] **Step 2: Validate workflow text locally**

Run:

```powershell
rg -n "workflow_dispatch|ubuntu-latest|IOURING_CONTRACT_ROOT|iouring-tests.trx|upload-artifact" .github\workflows\iouring-linux-contract.yml
git diff --check
```

Expected: required workflow markers are present and diff check passes.

- [ ] **Step 3: Run local build/tests**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore -v minimal
dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal
```

Expected: warning 0/error 0 and all tests pass.

- [ ] **Step 4: Update state docs**

Record that the workflow is local-validated only. Remote Linux run still requires user push/manual workflow execution.

- [ ] **Step 5: Commit**

```powershell
git add .github\workflows\iouring-linux-contract.yml CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md docs\agent-state\changelog\2026-06.md
git commit -m "ci: add iouring linux contract workflow"
```

---

### Task 3: State Documents And Decision

**Files:**
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/changelog/2026-06.md`
- Modify: `docs/agent-state/decisions/2026-06.md`

- [ ] **Step 1: Record D138**

Decision wording:

```markdown
- D138 — io_uring 후속 구현은 UDP/zero-copy 최적화 전에 Linux contract evidence gate 를 먼저 둔다.
```

Archive detail:

```markdown
## D138 — io_uring 후속 구현은 UDP/zero-copy 최적화 전에 Linux contract evidence gate 를 먼저 둔다

- 날짜: 2026-06-29
- 상태: Accepted
- 결정: D137 TCP-first pump 이후 다음 단계는 UDP pump 나 fixed-buffer/zero-copy 최적화가 아니라,
  Linux host 에서 capability status 와 TCP receive/send loopback 결과를 artifact 로 남기는 contract gate 로 둔다.
- 근거: 현재 Windows 검증은 shape 와 early-return 까지만 확인하므로 native syscall evidence 없이 backend 범위를
  넓히면 결함 위치를 분리하기 어렵다.
- 영향: `.github/workflows/iouring-linux-contract.yml`은 workflow_dispatch 전용으로 시작하고,
  capability unavailable 은 failure 가 아니라 evidence 상태로 취급한다.
```

- [ ] **Step 2: Update current execution point**

Set `CURRENT_PLAN.md` and `TODOS.md` next execution point to remote workflow verification after user push/manual run, or to io_uring UDP pump design if remote Linux execution remains unavailable.

- [ ] **Step 3: Run final validation**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore -v minimal
dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal
git diff --check
```

Expected: warning 0/error 0, all tests pass, diff check passes.

- [ ] **Step 4: Commit**

```powershell
git add CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs\agent-state\changelog\2026-06.md docs\agent-state\decisions\2026-06.md
git commit -m "docs: record iouring linux contract gate"
```

## Self-Review

- Spec coverage: D138 gate, capability evidence, Linux workflow artifact, non-failure unavailable policy, state docs are covered by Tasks 1-3.
- Placeholder scan: no TBD/TODO placeholders; every command and file path is explicit.
- Type consistency: `IoUringCapabilityEvidenceTests`, `IoUringCapabilityProbe`, `IoUringCapabilityStatus`, and workflow env names are consistent across tasks.
- Scope: no production transport code, UDP pump, zero-copy, default promotion, or latency gate is included.
