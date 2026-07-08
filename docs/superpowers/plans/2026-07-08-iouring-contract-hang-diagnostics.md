# io_uring Contract Hang Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `iouring-linux-contract.yml`에서 native test hang 이 발생하면 GitHub workflow 20분 timeout 전에 `dotnet test`가 자체 종료하고, summary/diag/blame evidence 를 artifact 로 남기게 한다.

**Architecture:** Production code 는 변경하지 않는다. 기존 workflow static tests 가 있는 `Hps.Benchmarks.Tests`에 contract assertion 을 추가하고, `.github/workflows/iouring-linux-contract.yml`의 `dotnet test` command 에 `--blame-hang`, `--blame-hang-timeout 2m`, `--blame-hang-dump-type none`, `--diag "$IOURING_CONTRACT_ROOT/vstest-diag.log"`를 붙인다. Remote gate 에서 정상 baseline success 와 artifact 내 diag log 존재를 확인한다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, GitHub Actions, VSTest `--blame-hang`, `--diag`.

## Global Constraints

- TFM 은 `net9.0`이고 C# `LangVersion`은 `8.0`이다.
- 모든 문서·주석·설명은 한국어로 작성한다.
- 이번 범위는 workflow diagnostics 변경만 포함한다.
- Production `src/Hps.Transport.IoUring` 코드는 변경하지 않는다.
- TCP payload fixed-write production 재연결, registration cache, zero-copy send, default backend promotion 은 제외한다.
- 구현은 Red -> Green -> Refactor 순서로 진행하고, 테스트 메서드 바로 위에는 무엇을 검증하는지 한국어 주석을 남긴다.
- 각 task 는 독립 커밋으로 남긴다. `.claude/review/*` 미추적 파일은 stage 하지 않는다.

---

## File Structure

- `.github/workflows/iouring-linux-contract.yml`
  - Task 1에서 `dotnet test` hang diagnostics 옵션과 summary evidence line 을 추가한다.
- `tests/Hps.Benchmarks.Tests/BenchmarkArtifactWorkflowTests.cs`
  - Task 1에서 Linux contract workflow static assertions 를 확장한다.
- `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`, `docs/agent-state/decisions/2026-07.md`
  - Task 완료 및 remote gate 결과를 기록한다.

---

### Task 1: Workflow Hang Diagnostics Contract

**Files:**
- Modify: `tests/Hps.Benchmarks.Tests/BenchmarkArtifactWorkflowTests.cs`
- Modify: `.github/workflows/iouring-linux-contract.yml`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - `private static string ReadIoUringLinuxContractWorkflow()`
  - `.github/workflows/iouring-linux-contract.yml`
- Produces:
  - Static test coverage for `--blame-hang`, `--blame-hang-timeout 2m`, `--blame-hang-dump-type none`, `--diag "$IOURING_CONTRACT_ROOT/vstest-diag.log"`
  - Workflow artifact summary line for hang diagnostics settings.

- [ ] **Step 1: Write failing static workflow test**

Modify `tests/Hps.Benchmarks.Tests/BenchmarkArtifactWorkflowTests.cs`.

Add this test after `IoUringLinuxContractWorkflow_WhenRunOnLinux_RestoresAndBuildsOnlyIoUringTestProject`.

```csharp
[Fact]
public void IoUringLinuxContractWorkflow_WhenTestsHang_WritesBlameHangDiagnostics()
{
    // D211 remote gate 는 test step 이 20분 workflow timeout 으로 cancelled 되어 TRX 없이 끝났다.
    // workflow 는 다음 native hang 을 짧게 실패시키고 diag/sequence evidence 를 artifact 에 남겨야 한다.
    string workflow = ReadIoUringLinuxContractWorkflow();

    Assert.Contains("--blame-hang", workflow);
    Assert.Contains("--blame-hang-timeout 2m", workflow);
    Assert.Contains("--blame-hang-dump-type none", workflow);
    Assert.Contains("--diag \"$IOURING_CONTRACT_ROOT/vstest-diag.log\"", workflow);
    Assert.Contains("Hang diagnostics: blame-hang timeout 2m, dump none", workflow);
    Assert.Contains("- VSTest diag: vstest-diag.log", workflow);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~IoUringLinuxContractWorkflow_WhenTestsHang_WritesBlameHangDiagnostics -v minimal
```

Expected: FAIL with `Assert.Contains() Failure` for `--blame-hang`.

- [ ] **Step 3: Add workflow diagnostics options**

Modify `.github/workflows/iouring-linux-contract.yml`.

Change the `dotnet test` command to:

```yaml
          dotnet test tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj \
            --no-build \
            --no-restore \
            --logger "trx;LogFileName=iouring-tests.trx" \
            --results-directory "$IOURING_CONTRACT_ROOT" \
            --blame-hang \
            --blame-hang-timeout 2m \
            --blame-hang-dump-type none \
            --diag "$IOURING_CONTRACT_ROOT/vstest-diag.log"
```

In the summary block, add these lines after `echo "- TRX: iouring-tests.trx"`:

```bash
            echo "- VSTest diag: vstest-diag.log"
            echo "- Hang diagnostics: blame-hang timeout 2m, dump none"
```

- [ ] **Step 4: Run focused workflow tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~IoUringLinuxContractWorkflow -v minimal
```

Expected: PASS. This filter should include the existing restore/build-only contract and the new hang diagnostics contract.

- [ ] **Step 5: Run relevant project tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj -v minimal
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal
```

Expected: both PASS.

- [ ] **Step 6: Update state docs**

Update:

- `CURRENT_PLAN.md`: record D214 workflow diagnostics local implementation and next remote gate.
- `TODOS.md`: move Task 1 to Completed and make remote gate review current.
- `CHANGELOG_AGENT.md`: record Red/Green commands and result.

- [ ] **Step 7: Commit**

Run:

```powershell
git status --short
git add .github/workflows/iouring-linux-contract.yml tests/Hps.Benchmarks.Tests/BenchmarkArtifactWorkflowTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "test(iouring): add contract hang diagnostics"
```

---

### Task 2: Full Local Verification

**Files:**
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - Task 1 workflow diagnostics changes.
- Produces:
  - D215 local full verification state before remote gate.

- [ ] **Step 1: Run full build**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx -v minimal
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Run full tests**

Run:

```powershell
dotnet test HighPerformanceSocket.slnx -v minimal
```

Expected: all test projects PASS, no zero-test project completion claim.

- [ ] **Step 3: Run whitespace check**

Run:

```powershell
git diff --check
```

Expected: no whitespace errors.

- [ ] **Step 4: Update state docs**

Update:

- `CURRENT_PLAN.md`: record full local verification.
- `TODOS.md`: keep remote gate review current.
- `CHANGELOG_AGENT.md`: add full verification result.

- [ ] **Step 5: Commit**

Run:

```powershell
git status --short
git add CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "docs(iouring): record hang diagnostics local verification"
```

If no state doc change is necessary beyond Task 1, skip this commit and include full verification in Task 1 commit before committing.

---

### Task 3: Remote Contract Gate Review

**Files:**
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/decisions/2026-07.md`

**Interfaces:**
- Consumes:
  - GitHub Actions `iouring-linux-contract.yml`
  - pushed head SHA containing Task 1.
  - downloaded artifact directory.
- Produces:
  - D216 remote gate interpretation.

- [ ] **Step 1: Push or wait for user push**

Run:

```powershell
git push
```

If push is blocked by execution policy, stop this task and leave `TODOS.md` current item as remote gate review after user push.

- [ ] **Step 2: Trigger workflow**

Run:

```powershell
gh workflow run iouring-linux-contract.yml --ref master
```

Record the run URL/id.

- [ ] **Step 3: Watch workflow**

Run:

```powershell
gh run watch <run-id> --exit-status
```

Expected: success.

- [ ] **Step 4: Download artifact**

Run:

```powershell
gh run download <run-id> --dir artifacts\iouring\linux-contract\<date>\run-<run-id>-1
```

Expected artifact files:

```text
summary.md
dotnet-info.txt
iouring-tests.trx
vstest-diag.log
```

- [ ] **Step 5: Verify evidence**

Check:

```text
summary test exit code: 0
summary contains: VSTest diag: vstest-diag.log
summary contains: Hang diagnostics: blame-hang timeout 2m, dump none
TRX counters: failed 0
TcpLoopback_WhenIoUringAvailable_SendsQueuedPayloadToPeer: Passed
Lease_WhenLinuxCapabilityAvailable_WritesRegisteredPayloadSliceToSocketPair: Passed
WriteFixed_WhenLinuxCapabilityAvailable_WritesRegisteredBufferSliceToSocketPair: Passed
stdout contains: io_uring capability status: Available
stdout contains: fixed socket write completion result: 2
```

- [ ] **Step 6: Update state docs**

If pass:

- `CURRENT_PLAN.md`: record D216 remote success and next candidate reevaluation.
- `TODOS.md`: move remote gate to Completed and create post-D216 candidate reevaluation current TODO.
- `CHANGELOG_AGENT.md`: record run id, head SHA, artifact name, counters, diagnostics file presence.
- `DECISIONS.md`: add D216 active decision that contract diagnostics are accepted and production fixed-write remains deferred.
- `docs/agent-state/decisions/2026-07.md`: add detailed D216.

If fail:

- Record exact failing step, exit code, and artifact contents.
- Keep failure fix as current TODO.

- [ ] **Step 7: Commit documentation**

Run:

```powershell
git status --short
git add CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs/agent-state/decisions/2026-07.md
git commit -m "docs(iouring): record contract hang diagnostics gate"
```

---

## Validation Summary

Local validation:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~IoUringLinuxContractWorkflow -v minimal
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj -v minimal
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal
dotnet build HighPerformanceSocket.slnx -v minimal
dotnet test HighPerformanceSocket.slnx -v minimal
git diff --check
```

Remote validation:

```text
iouring-linux-contract.yml success
artifact includes summary.md, dotnet-info.txt, iouring-tests.trx, vstest-diag.log
TRX failed 0
```

## Excluded Follow-Up

- TCP payload fixed-write production 재연결
- queue/transport lifetime fixed buffer registration 구현
- registration cache
- zero-copy send
- UDP fixed-buffer send
- default backend promotion
