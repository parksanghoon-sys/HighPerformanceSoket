# CHANGELOG_AGENT.md

## Archive

긴 변경 이력 원문은 `docs/agent-state/changelog/2026-06.md`에 보존했다.
이 파일은 최근 작업 단위와 현재 진입점에 필요한 내용만 유지한다.

## 2026-06-25 (Codex - RIO Task 5.7 completion queue delegates)

### 작업 단위
- Windows RIO TCP pump 선행 하위 단위로 native completion queue delegate 를 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioNative.cs`:
  loaded RIO function table 의 `RIOCreateCompletionQueue`/`RIOCloseCompletionQueue` pointer 를 delegate 로 marshal 하고,
  `CreateCompletionQueue(...)`/`CloseCompletionQueue(...)` internal operation 으로 노출했다.
- `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`:
  RIO available 환경에서 null notification completion 기반 CQ 를 실제로 생성/해제하는 테스트를 추가했다.
- `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`:
  TCP pump 전에 CQ delegate 를 검증하는 Task 5.7을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 5.7 완료와 다음 RQ native delegate boundary 진입점을 반영했다.

### 검증
- Red: `CreateCompletionQueue` operation boundary 부재로 `Assert.NotNull() Failure: Value is null`을 확인했다.
- Green/refactor: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 13개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 282개 통과, 실패 0.
- `git diff --check`: 통과.

## 2026-06-25 (Codex - RIO Task 5.6 buffer registration delegates)

### 작업 단위
- Windows RIO TCP pump 선행 하위 단위로 native buffer registration delegate 를 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioNative.cs`:
  loaded RIO function table 의 `RIORegisterBuffer`/`RIODeregisterBuffer` pointer 를 delegate 로 marshal 하고,
  `RegisterBuffer(...)`/`DeregisterBuffer(...)` internal operation 으로 노출했다.
- `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`:
  RIO available 환경에서 `PinnedBlockMemoryPool` block 을 실제 RIO buffer 로 등록/해제하는 테스트를 추가했다.
- `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`:
  TCP pump 전에 buffer registration delegate 를 검증하는 Task 5.6을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 5.6 완료와 다음 CQ/RQ native delegate boundary 진입점을 반영했다.

### 검증
- Red: `RegisterBuffer` operation boundary 부재로 `Assert.NotNull() Failure: Value is null`을 확인했다.
- Green/refactor: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 12개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 281개 통과, 실패 0.
- `git diff --check`: 통과.

## 2026-06-25 (Codex - RIO Task 5.5 native loader hardening)

### 작업 단위
- Windows RIO backend Task 6 전 선행 보정으로 실제 native function table loader 를 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioNative.cs`:
  `WSAIoctl(SIO_GET_MULTIPLE_EXTENSION_FUNCTION_POINTER, WSAID_MULTIPLE_RIO)` 호출로
  `RIO_EXTENSION_FUNCTION_TABLE`을 얻고 필수 function pointer 를 검증한다.
- `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`:
  Windows 환경에서 `RioCapabilityProbe.GetStatus()`가 실제 `Available`로 수렴해야 함을 검증한다.
- `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`,
  `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D098과 Task 5.5를 기록해 TCP pump 전에 실제 native loader 를 완료하도록 순서를 보정했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 5.5 완료와 다음 Task 6 TCP pump/contract test reuse 진입점을 반영했다.

### 검증
- Red: Windows에서 `GetStatus_WhenWindows_LoadsRioFunctionTable`이 `Expected: Available`, `Actual: Unavailable`로 실패함을 확인했다.
- Green/refactor: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 11개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 280개 통과, 실패 0.
- `git diff --check`: 통과.

## 2026-06-25 (Codex - RIO Task 5 TCP opt-in guard)

### 작업 단위
- Windows RIO backend Task 5로 TCP opt-in transport guard 를 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioTransport.cs`:
  `ListenTcpAsync`/`ConnectTcpAsync`가 실행 중 lifecycle 확인 뒤 RIO capability 를 먼저 검사하도록 했다.
  현재 환경에서 Windows RIO function table 을 사용할 수 없으면 실제 TCP wiring 미구현 메시지보다 먼저
  명시적인 `NotSupportedException`으로 실패한다.
- `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`:
  RIO unavailable 환경에서 opt-in TCP listen 이 function table failure 를 노출하는지 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 5 완료와 Task 6 진입 전 native loader gap 재평가 필요성을 반영했다.

### 검증
- Red: unavailable guard 테스트가 기존 미구현 메시지 때문에 `Assert.Contains()` 실패함을 확인했다.
- Green/refactor: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 10개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 279개 통과, 실패 0.
- `git diff --check`: 통과.

## 2026-06-25 (Codex - RIO Task 4 queue owners)

### 작업 단위
- Windows RIO backend Task 4로 TCP queue owner skeleton 을 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioRequestQueue.cs`:
  receive/send outstanding quota reservation 과 completion accounting 을 추가했다.
- `src/Hps.Transport.Rio/RioCompletionQueue.cs`:
  native CQ 연결 전 수명 owner skeleton 을 추가했다.
- `tests/Hps.Transport.Rio.Tests/RioQueueOwnerTests.cs`:
  receive/send quota 초과와 completion 후 재예약 가능성을 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 4 완료와 다음 Task 5 TCP opt-in guard 진입점을 반영했다.

### 검증
- Red: queue owner 타입 부재로 `Assert.NotNull() Failure: Value is null` 2개를 확인했다.
- Green/refactor: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 9개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 278개 통과, 실패 0.
- `git diff --check`: 통과.

## 2026-06-25 (Codex - RIO Task 3 registered buffer owner)

### 작업 단위
- Windows RIO backend Task 3으로 registered buffer owner 수명 규칙을 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioRegisteredBufferPool.cs`:
  outstanding request 완료 전에는 pinned block 을 반환하지 않고, completion 중복 호출은 한 번만 release 하도록 했다.
- `src/Hps.Transport.Rio/Properties/AssemblyInfo.cs`:
  RIO test assembly 에 internal 접근을 허용했다.
- `tests/Hps.Transport.Rio.Tests/RioRegisteredBufferPoolTests.cs`,
  `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`:
  Red 확인 후 reflection 중심 테스트를 direct internal API 테스트로 정리했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 3 완료와 다음 Task 4 TCP queue owner 진입점을 반영했다.

### 검증
- Red: `RioRegisteredBufferPool_TypeExists`가 `Assert.NotNull() Failure: Value is null`로 실패함을 확인했다.
- Green/refactor: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 7개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 276개 통과, 실패 0.
- `git diff --check`: 통과.

## 2026-06-25 (Codex - RIO Task 2 native loader boundary)

### 작업 단위
- Windows RIO backend Task 2로 native function table loader 경계를 추가했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioNative.cs`:
  RIO native function table load 를 숨기는 internal boundary 를 추가했다.
- `src/Hps.Transport.Rio/RioCapabilityProbe.cs`:
  Windows probe 가 `RioNative.TryLoadFunctionTable(...)` 결과를 통해 `Available` 또는 `Unavailable`로 수렴하도록 연결했다.
- `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`:
  `RioNative` 타입 존재와 Windows probe non-throw 경로를 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 2 완료와 다음 Task 3 registered buffer owner 진입점을 반영했다.

### 검증
- Red: `RioNative_TypeExists`가 `Assert.NotNull() Failure: Value is null`로 실패함을 확인했다.
- Green: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 6개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 275개 통과, 실패 0.
- `git diff --check`: 통과.

## 2026-06-25 (Codex - RIO Task 1 skeleton/probe)

### 작업 단위
- Windows RIO backend Task 1로 project skeleton 과 capability probe public surface 를 추가했다.

### 변경 내용
- `src/Hps.Transport.Rio/`:
  `Hps.Transport.Rio.csproj`, `RioCapabilityStatus`, `RioCapabilityProbe`, `RioTransport` skeleton 을 추가했다.
- `tests/Hps.Transport.Rio.Tests/`:
  reflection 기반 Red를 사용하는 capability probe tests 를 추가했다.
- `HighPerformanceSocket.slnx`:
  RIO source/test projects 를 solution 에 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 1 완료와 다음 Task 2 native function table loader 진입점을 반영했다.

### 검증
- Red: `RioCapabilityProbe_TypeExists`가 `Assert.NotNull() Failure: Value is null`로 실패함을 확인했다.
- Green: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 4개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 273개 통과, 실패 0.
- `git diff --check`: 통과.

## 2026-06-25 (Codex - Windows RIO backend implementation plan)

### 작업 단위
- D097 Windows RIO backend boundary 설계를 구현 가능한 계획으로 분해했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`:
  RIO project skeleton/probe, native function table loader, registered buffer owner,
  TCP queue owner, TCP opt-in guard, TCP pump/contract test reuse 의 6개 task 를 작성했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  계획 작성 완료 상태와 다음 실행 단위인 RIO Task 1 skeleton/probe 구현을 반영했다.

### 검증
- 계획 self-review 로 D097 spec coverage, task boundary, type naming consistency 를 확인했다.
- placeholder scan 결과 신규 plan 에 미정 항목 없음. 검색에 잡힌 항목은 파일명 문자열 또는 기존 archive/changelog 문맥이다.
- `git diff --check`: 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - Windows RIO backend boundary design)

### 작업 단위
- Phase 5 Windows RIO backend 의 책임 경계와 첫 구현 순서를 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-25-windows-rio-backend-boundary-design.md`:
  RIO backend 를 TCP-first 로 진행하되, 첫 task 를 project skeleton, capability probe,
  native function table wrapper 로 분리하는 설계를 기록했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D097로 RIO TCP-first/probe-first 정책과 SAEA default 유지 방침을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  RIO 설계 완료 상태와 다음 실행 단위인 RIO 구현 계획 작성 진입점을 반영했다.

### 검증
- `TransportFactory`, `TransportBase`, `TransportConnection`, `SaeaTransport` 구조와 설계가 충돌하지 않는지 대조했다.
- Microsoft Learn RIO request queue, completion queue, buffer registration, notification/dequeue 문서를 확인했다.
- placeholder scan 결과 신규 spec/current state 에 미정 항목 없음. 검색에 잡힌 항목은 기존 archive/changelog 문맥이다.
- `git diff --check`: 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - after CI baseline adoption reassessment)

### 작업 단위
- 첫 CI repository baseline 채택 이후 Phase 4 다음 후보를 재평가했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-25-after-ci-baseline-adoption-reassessment-design.md`:
  CI baseline adoption 이후 gate 승격 보류와 Phase 5 RIO 설계 진입 판단을 기록했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D096으로 첫 CI baseline 이후에도 latency hard gate, warning-as-failure, CI artifact 자동 채택을 승격하지 않는다고 기록했다.
- `docs/benchmarks/baselines/index.md`:
  CI runner envelope 가 1-session reference signal 이며 gate 조건이 아님을 명시했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  완료된 Phase 4 재평가를 정리하고 다음 실행 단위를 Phase 5 Windows RIO backend 설계로 갱신했다.

### 검증
- CI runner root history 와 session summary 를 대조했다.
- D082/D090/D095와 D096 판단이 충돌하지 않는지 확인했다.
- `git diff --check`: 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - CI artifact baseline adoption)

### 작업 단위
- D095 절차에 따라 push-triggered run `28145025444` artifact 를 첫 CI repository baseline 으로 수동 채택했다.

### 변경 내용
- `docs/benchmarks/baselines/runners/ci-windows-x64-01/2026-06-25/session-01/`:
  raw report 6개를 보존하고 `summary.json`, `summary.md`를 repository 경로 기준으로 재생성했다.
- `docs/benchmarks/baselines/runners/ci-windows-x64-01/2026-06-25/history.json`,
  `history.md`, `docs/benchmarks/baselines/runners/ci-windows-x64-01/history.json`, `history.md`:
  date-level/runner-level history 를 생성했다.
- `docs/benchmarks/baselines/index.md`:
  CI runner group, date-level history, session row, CI runner reference envelope 를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  채택 완료 상태와 다음 Phase 4 재평가 진입점을 기록했다.

### 검증
- D095 checklist 를 통과했다: raw report 6개, hard-passed true, warning-count 0,
  comparison-compatible true, unknown-runner-count 0, runner metadata 일치.
- summary/history 재생성 결과: session-count 1, hard-passed true, warning-count 0,
  comparison-compatible true.
- absolute path scan 결과 없음.
- `git diff --check`: exit 0.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-build --no-restore`: 67개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - CI artifact adoption policy)

### 작업 단위
- CI artifact 를 어떤 조건과 절차로 repository baseline 에 수동 채택할지 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-25-ci-artifact-adoption-policy-design.md`:
  채택 조건, 금지 항목, raw report 복사와 summary/history 재생성 절차를 정리했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D095로 CI artifact 수동 채택 정책을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 작업을 run `28145025444` artifact 의 repository baseline 채택으로 갱신했다.

### 검증
- D090/D093/D094, `docs/benchmarks/baselines/index.md`, downloaded artifact 구조를 대조했다.
- spec placeholder scan 에서 신규 미정 항목 없음.
- `git diff --check`: exit 0.

## 2026-06-25 (Codex - CI artifact push trigger verification)

### 작업 단위
- D094 `push` to `master` path trigger 가 원격에서 자동으로 `Benchmark Artifacts` run 을 생성하는지 확인했다.

### 변경 내용
- `CURRENT_PLAN.md`, `TODOS.md`:
  push-triggered run `28145025444` 결과와 다음 실행 후보를 기록했다.

### 검증
- `git status -sb`: local `master`와 `origin/master`가 일치함을 확인했다.
- `gh run list --workflow "Benchmark Artifacts" --limit 5`:
  push event run `28145025444`가 생성됐음을 확인했다.
- `gh run watch 28145025444 --exit-status`: 성공, job duration 약 4분 7초.
- 로그 확인:
  `actions/checkout@v7`, `actions/setup-dotnet@v5.3.0`, `actions/upload-artifact@v7.0.1` 다운로드 및 실행을 확인했다.
- Node annotation 확인:
  `deprecation`, `Node.js 20`, `node20`, 이전 `actions/*@v4` 문자열 검색 결과 없음.
- artifact upload:
  `benchmark-artifacts-ci-windows-x64-01-2026-06-25-github-28145025444-1`,
  artifact id `7868207312`, uploaded files 10개, final size 6407 bytes.
- downloaded artifact 확인:
  raw report 6개, `summary.json`, `summary.md`, `history.json`, `history.md`.
- `summary.json`: source-report-count 6, hard-passed true, warning-count 0,
  comparison-compatible true, unknown-runner-count 0.
- `history.json`: session-count 1, hard-passed true, warning-count 0, comparison-compatible true.

## 2026-06-25 (Codex - CI artifact trigger policy)

### 작업 단위
- `Benchmark Artifacts` workflow 의 자동 실행 trigger 정책을 설계하고 workflow 에 반영했다.

### 변경 내용
- `.github/workflows/benchmark-artifacts.yml`:
  `workflow_dispatch`를 유지하고, `push` to `master` + code/benchmark/build path filter 를 추가했다.
- `docs/superpowers/specs/2026-06-25-ci-artifact-trigger-policy-design.md`:
  PR/schedule 은 제외하고 master push path filter 를 채택하는 근거를 정리했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D094로 trigger 정책을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 검증 지점을 D094 workflow 변경 push 후 자동 run 확인으로 갱신했다.

### 검증
- workflow marker scan 으로 `workflow_dispatch`, `push`, `branches: master`, path filter 를 확인했다.
- workflow scan 에서 `pull_request`, `schedule`, warning-as-failure, latency failure logic 이 없음을 확인했다.
- `git diff --check`: exit 0.

## 2026-06-25 (Codex - CI artifact follow-up reassessment)

### 작업 단위
- `ci-windows-x64-01` artifact-only manual run 2회 결과를 기준으로 Phase 4 다음 후보를 재평가했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-25-ci-artifact-after-manual-runs-reassessment.md`:
  gate/trigger/baseline 채택 여부와 다음 후보를 정리했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D093으로 manual run 2회만으로는 gate/trigger 를 승격하지 않는다고 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 단위를 CI artifact trigger policy 설계로 갱신했다.

### 검증
- run `28143728630`, run `28144480160` log/artifact 값을 대조했다.
- D090/D091/D092와 `docs/benchmarks/baselines/index.md`를 대조했다.
- `git diff --check`로 문서 변경 상태를 검증한다.

## 2026-06-25 (Codex - CI workflow Node 24 manual run)

### 작업 단위
- `actions/*` Node 24 version 갱신 후 `Benchmark Artifacts` workflow 를 다시 manual `workflow_dispatch`로 실행하고 결과를 확인했다.

### 변경 내용
- `CURRENT_PLAN.md`, `TODOS.md`:
  run `28144480160` 결과, artifact 이름, summary/history 핵심 값, Node deprecation 제거 확인을 기록했다.

### 검증
- `gh workflow run "Benchmark Artifacts" --ref master`: run `28144480160` 생성.
- `gh run watch 28144480160 --exit-status`: 성공, job duration 약 4분 15초.
- 로그 확인:
  `actions/checkout@v7`, `actions/setup-dotnet@v5.3.0`, `actions/upload-artifact@v7.0.1` 다운로드 및 실행을 확인했다.
- Node annotation 확인:
  `deprecation`, `Node.js 20`, `node20`, 이전 `actions/*@v4` 문자열 검색 결과 없음.
- artifact upload:
  `benchmark-artifacts-ci-windows-x64-01-2026-06-25-github-28144480160-1`,
  artifact id `7868009214`, uploaded files 10개, final size 6399 bytes.
- downloaded artifact 확인:
  raw report 6개, `summary.json`, `summary.md`, `history.json`, `history.md`.
- `summary.json`: source-report-count 6, hard-passed true, warning-count 0,
  comparison-compatible true, unknown-runner-count 0.
- `history.json`: session-count 1, hard-passed true, warning-count 0, comparison-compatible true.
- 이번 결과도 D090 기준으로 docs baseline 에 자동 채택하지 않고 CI artifact evidence 로만 둔다.

## 2026-06-25 (Codex - CI workflow Node 24 action versions)

### 작업 단위
- 첫 GitHub Actions manual run 에서 확인된 Node.js 20 deprecation annotation 을 제거하기 위해 workflow action version 을 갱신했다.

### 변경 내용
- `.github/workflows/benchmark-artifacts.yml`:
  `actions/checkout@v7`, `actions/setup-dotnet@v5.3.0`, `actions/upload-artifact@v7.0.1`로 갱신했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D092로 Node 24 action runtime 갱신 결정을 기록했다.
- `docs/superpowers/plans/2026-06-25-ci-artifact-only-workflow-skeleton.md`,
  `docs/superpowers/specs/2026-06-25-ci-artifact-only-benchmark-policy-design.md`:
  action version 과 benchmark command sequence 문구를 현재 workflow 와 맞췄다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Node deprecation follow-up 을 처리된 상태로 정리하고, 다음 후보를 갱신된 workflow manual run 검증으로 좁혔다.

### 검증
- 공식 release/action metadata 확인 기준, 세 action version 은 `runs.using: node24`를 명시한다.
- `git diff --check`: exit 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 단독 재실행 기준 경고 0, 오류 0.
  비고: 최초 build/test 병렬 실행 때 테스트 프로세스와 DLL copy 가 겹쳐 MSB3026 copy retry 경고 1개가 발생했으나,
  테스트 종료 후 build 단독 재실행에서는 경고 없이 통과했다.

## 2026-06-25 (Codex - CI workflow first manual run)

### 작업 단위
- 원격 push 이후 `Benchmark Artifacts` workflow 를 manual `workflow_dispatch`로 실행하고 artifact 결과를 확인했다.

### 변경 내용
- `CURRENT_PLAN.md`, `TODOS.md`:
  첫 GitHub Actions run 결과, artifact 이름, summary/history 핵심 값, 남은 follow-up 을 기록했다.

### 검증
- `gh workflow list`: `Benchmark Artifacts` active, workflow id `301858085`.
- `gh workflow run "Benchmark Artifacts" --ref master`: run `28143728630` 생성.
- `gh run watch 28143728630 --exit-status`: 성공, job duration 약 4분 5초.
- artifact upload: `benchmark-artifacts-ci-windows-x64-01-2026-06-25-github-28143728630-1`,
  artifact id `7867724437`, uploaded files 10개, final size 6576 bytes.
- downloaded artifact 확인: raw report 6개, `summary.json`, `summary.md`, `history.json`, `history.md`.
- `summary.json`: source-report-count 6, hard-passed true, warning-count 1,
  comparison-compatible true, unknown-runner-count 0.
- `history.json`: session-count 1, hard-passed true, warning-count 1, comparison-compatible true.
- warning detail: `open-loop-01.json`의 `p99-growth-ratio-high`이며 D090 기준 report-only 다.
- non-blocking annotation: Node.js 20 deprecation 안내가 `actions/checkout@v4`, `actions/setup-dotnet@v4`,
  `actions/upload-artifact@v4`에 발생했다. workflow는 Node 24 강제 실행으로 성공했다.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - CI workflow command sequence smoke)

### 작업 단위
- CI artifact-only workflow 의 benchmark command sequence 를 로컬 임시 artifact root 에서 smoke 하고 no-restore 형태로 보정했다.

### 변경 내용
- `.github/workflows/benchmark-artifacts.yml`:
  benchmark CLI 실행 세 단계에 모두 `--no-build --no-restore`를 명시했다.
- `docs/superpowers/plans/2026-06-25-ci-artifact-only-workflow-skeleton.md`:
  workflow 예시 command 를 실제 구현과 같은 no-restore 형태로 맞췄다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  local command sequence smoke 결과와 남은 GitHub-hosted runner manual run 검증을 기록했다.

### 검증
- 최초 local full smoke: workflow command sequence 로 `--runs 3`을 실행해 raw report 6개,
  `summary.json`/`summary.md`, `history.json`/`history.md` 생성을 확인했다.
- 최초 smoke 관찰: 첫 benchmark `dotnet run`이 restore를 다시 시도해 `NU1900` package vulnerability data warning 을 냈다.
- 보정 후 local smoke: `--no-build --no-restore` command sequence 로 `--runs 1`을 실행해 raw report 2개,
  summary/history artifact 생성, `hard-passed=true`, `warning-count=0`을 확인했다.
- local smoke 후 sandbox NuGet cache 경로가 `project.assets.json`에 반영되어 최초 `dotnet build --no-restore`가
  누락 analyzer DLL로 실패했다. `dotnet restore HighPerformanceSocket.slnx`를 다시 실행해 실제 환경 cache 기준으로 복구했다.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: restore 후 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - CI artifact-only workflow skeleton)

### 작업 단위
- D090/D091 정책에 맞춰 GitHub Actions benchmark artifact-only workflow skeleton 을 추가했다.

### 변경 내용
- `.github/workflows/benchmark-artifacts.yml`:
  `workflow_dispatch` 전용 Windows workflow 를 추가했다.
- workflow job env:
  `HPS_BENCHMARK_RUNNER_ID=ci-windows-x64-01`, `HPS_BENCHMARK_RUNNER_KIND=ci`를 고정했다.
- workflow command sequence:
  restore, build, test, `--baseline-suite`, `--summarize-baseline`, `--summarize-baseline-history`, artifact upload 순서로 구성했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 workflow 검토 또는 첫 manual run 결과 반영으로 갱신했다.

### 검증
- workflow static marker scan: `workflow_dispatch`, `ci-windows-x64-01`, `HPS_BENCHMARK_RUNNER_KIND`,
  현재 workflow 기준 `actions/upload-artifact@v7.0.1` 존재를 확인했다.
- workflow out-of-scope scan: `push`, `pull_request`, `warning-count`, `latency` logic 이 workflow 에 없음을 확인했다.
- lightweight policy check: required marker 존재와 자동 trigger 부재를 확인했다.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - CI artifact-only workflow skeleton plan)

### 작업 단위
- D090 정책을 실제 GitHub Actions workflow skeleton 으로 옮기기 위한 구현 계획을 작성했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-25-ci-artifact-only-workflow-skeleton.md`:
  workflow trigger, runner identity, artifact path, benchmark CLI command sequence, upload policy 를 구현 단계로 정리했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D091로 GitHub run id 는 upload artifact 이름에 두고 내부 history-compatible directory 는 `session-01`로 유지하는 결정을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 workflow skeleton 구현으로 갱신했다.

### 검증
- D090 spec 의 artifact-only failure policy 와 runner identity 를 계획에 반영했다.
- `BaselineHistoryReader`가 date root 와 `session-NN` children 만 history source 로 읽는 현재 제약을 확인했다.
- `.github/workflows`가 아직 없음을 확인했다.
- placeholder scan 은 과거 archive 문구와 plan 내부 검증 스크립트 literal 만 잡았고, 신규 미정 항목은 없었다.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - CI artifact-only benchmark policy)

### 작업 단위
- CI workflow 구현 전에 CI artifact-only benchmark 정책을 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-25-ci-artifact-only-benchmark-policy-design.md`:
  CI runner id, artifact 저장 위치, local/CI baseline 분리, exit code 정책, report-only latency/HWM/warning 기준을 정리했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D090을 추가했다.
- `docs/benchmarks/baselines/index.md`:
  CI 매 실행 artifact 는 docs baseline 에 자동 추가하지 않고 artifact-only 영역에 둔다는 운영 원칙을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 CI artifact-only workflow skeleton 구현 계획으로 갱신했다.

### 검증
- `tests/Hps.Benchmarks/Program.cs`: `baseline-suite`, `summary`, `history`가 hard-passed 기반 exit code 를 쓰고
  `warning-count > 0`만으로 실패하지 않는 현재 규약을 대조했다.
- `tests/Hps.Benchmarks/BenchmarkRunIdentity.cs`: CI runner id/kind 를 환경 변수로 주입할 수 있고,
  host/user/IP를 자동 수집하지 않는 privacy 정책을 확인했다.
- `.github/workflows`가 아직 없음을 확인했다.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - Phase 4 gate promotion reassessment)

### 작업 단위
- explicit runner 2-date-root/6-session reference 이후 D082 warning-as-failure/CI latency gate 승격 후보를 재평가했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-25-phase4-gate-promotion-reassessment-design.md`:
  D082 조건 충족/미충족 상태, 선택지, gate 보류 결정, 다음 CI artifact-only 정책 설계 진입점을 정리했다.
- `docs/benchmarks/baselines/index.md`:
  `local-win-x64-01` 2-date-root reference 완료 후에도 D089 기준으로 gate 를 즉시 승격하지 않는다는 해석 메모를 추가했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D089를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 CI artifact-only benchmark 정책 설계로 갱신했다.

### 검증
- runner root `history.json`: session-count 6, hard-passed true, warning-count 0, comparison-compatible true 를 확인했다.
- `docs/benchmarks/baselines/index.md`: explicit runner envelope 수치와 D089 해석 메모를 대조했다.
- D082 조건 대조: 명시 runner id, 각 date root 3-session, hard/comparison pass 는 충족하지만 서로 다른 date root 3개 이상과
  별도 warning threshold 검토는 미충족임을 확인했다.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - explicit runner baseline date root 02 session 03)

### 작업 단위
- `local-win-x64-01/2026-06-25/session-03` explicit runner baseline 을 수집하고, 두 번째 explicit runner date root 를 3-session reference 로 완성했다.

### 변경 내용
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/session-03/`:
  `load-01..03.json`, `open-loop-01..03.json` raw report 6개를 생성했다.
- `session-03/summary.json`, `session-03/summary.md`:
  explicit runner summary artifact 를 생성했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/history.json`, `history.md`:
  2026-06-25 date-level history artifact 를 3-session 기준으로 재생성했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/history.json`, `history.md`:
  두 date root 를 묶는 runner root history artifact 를 6-session 기준으로 재생성했다.
- `docs/benchmarks/baselines/index.md`:
  runner date-level history, session row, explicit runner reference latency envelope 를 갱신했다.
- `DECISIONS.md`, `CURRENT_PLAN.md`, `TODOS.md`, `docs/agent-state/decisions/2026-06.md`:
  D088과 다음 Phase 4 gate 후보 재평가 진입점을 반영했다.

### 검증
- `--baseline-suite`: baseline-suite-result pass, raw report 6개 생성.
- `--summarize-baseline`: source-report-count 6, hard-passed true, warning-count 0.
- date root `--summarize-baseline-history`: session-count 3, hard-passed true, warning-count 0.
- runner root `--summarize-baseline-history`: session-count 6, hard-passed true, warning-count 0.
- `summary.json`/`history.json`: `comparison-compatible=true`, unknown runner 0, mismatch 0.
- explicit runner envelope: load p99 max 935.6 us, open-loop p99 max 1077.4 us, TCP HWM max 2,
  dropped total 0, payload error total 0, pool rented max 0.
- runner artifact local absolute path 검색 결과 없음.
- `Hps.Benchmarks.Tests`: 67개 통과, 실패 0.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 최초 실행은 stale restore asset 때문에
  `Microsoft.CodeAnalysis.Analyzers.dll` 경로 오류로 실패했고, `dotnet restore HighPerformanceSocket.slnx` 후 재실행해 경고 0, 오류 0으로 통과.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - explicit runner baseline date root 02 session 02)

### 작업 단위
- `local-win-x64-01/2026-06-25/session-02` explicit runner baseline 을 수집하고, 파생 문서를 갱신했다.

### 변경 내용
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/session-02/`:
  `load-01..03.json`, `open-loop-01..03.json` raw report 6개를 생성했다.
- `session-02/summary.json`, `session-02/summary.md`:
  explicit runner summary artifact 를 생성했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/history.json`, `history.md`:
  2026-06-25 date-level history artifact 를 2-session 기준으로 재생성했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/history.json`, `history.md`:
  두 date root 를 묶는 runner root history artifact 를 5-session 기준으로 재생성했다.
- `docs/benchmarks/baselines/index.md`:
  runner date-level history, session row, explicit runner reference latency envelope 를 갱신했다.
- `DECISIONS.md`, `CURRENT_PLAN.md`, `TODOS.md`, `docs/agent-state/decisions/2026-06.md`:
  D087과 다음 `session-03` 수집 진입점을 반영했다.

### 검증
- `--baseline-suite`: baseline-suite-result pass, raw report 6개 생성.
- `--summarize-baseline`: source-report-count 6, hard-passed true, warning-count 0.
- date root `--summarize-baseline-history`: session-count 2, hard-passed true, warning-count 0.
- runner root `--summarize-baseline-history`: session-count 5, hard-passed true, warning-count 0.
- `summary.json`/`history.json`: `comparison-compatible=true`, unknown runner 0, mismatch 0.
- runner artifact local absolute path 검색 결과 없음.
- `Hps.Benchmarks.Tests`: 67개 통과, 실패 0.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 최초 실행은 stale restore asset 때문에
  `Microsoft.CodeAnalysis.Analyzers.dll` 경로 오류로 실패했고, `dotnet restore HighPerformanceSocket.slnx` 후 재실행해 경고 0, 오류 0으로 통과.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - explicit runner baseline date root 02 session 01)

### 작업 단위
- `local-win-x64-01/2026-06-25/session-01` explicit runner baseline 을 수집하고, 파생 문서를 갱신했다.

### 변경 내용
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/session-01/`:
  `load-01..03.json`, `open-loop-01..03.json` raw report 6개를 생성했다.
- `session-01/summary.json`, `session-01/summary.md`:
  explicit runner summary artifact 를 생성했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/history.json`, `history.md`:
  2026-06-25 date-level history artifact 를 생성했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/history.json`, `history.md`:
  두 date root 를 묶는 runner root history artifact 를 생성했다.
- `docs/benchmarks/baselines/index.md`:
  runner group latest date root, runner date-level history, session row, explicit runner reference latency envelope 를 갱신했다.
- `DECISIONS.md`, `CURRENT_PLAN.md`, `TODOS.md`:
  D086과 다음 `session-02` 수집 진입점을 반영했다.

### 검증
- `--baseline-suite`: baseline-suite-result pass, raw report 6개 생성.
- `--summarize-baseline`: source-report-count 6, hard-passed true, warning-count 0.
- date root `--summarize-baseline-history`: session-count 1, hard-passed true, warning-count 0.
- runner root `--summarize-baseline-history`: session-count 4, hard-passed true, warning-count 0.
- `summary.json`/`history.json`: `comparison-compatible=true`, unknown runner 0, mismatch 0.
- runner artifact local absolute path 검색 결과 없음.
- `Hps.Benchmarks.Tests`: 67개 통과, 실패 0.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 최초 실행은 stale restore asset 때문에
  `Microsoft.CodeAnalysis.Analyzers.dll` 경로 오류로 실패했고, `dotnet restore HighPerformanceSocket.slnx` 후 재실행해 경고 0, 오류 0으로 통과.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - Phase 4 next candidate after explicit runner reference)

### 작업 단위
- explicit runner 3-session reference 이후 Phase 4 다음 실행 후보를 재평가했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-25-phase4-after-explicit-runner-reference-reassessment.md`:
  다음 date root 수집, CI/warning-as-failure 설계, RIO/io_uring 착수 후보를 비교했다.
- `DECISIONS.md`:
  D085를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 `local-win-x64-01/2026-06-25/session-01` 수집으로 갱신했다.

### 검증
- `local-win-x64-01/2026-06-24/history.json`: session-count 3, hard-passed true, warning-count 0,
  comparison-compatible true 를 확인했다.
- `docs/benchmarks/baselines/index.md`: explicit runner date root 가 아직 1개뿐임을 확인했다.
- D082/D084와 `.claude/review/`의 기존 benchmark 리뷰 의견을 대조했다.
- 신규 spec placeholder 검색 결과 없음.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-24 (Codex - explicit runner baseline 3-session reference)

### 작업 단위
- `local-win-x64-01/2026-06-24` explicit runner baseline 을 3-session reference 로 확장하고 문서 batch 를 완료했다.

### 변경 내용
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/session-02/`:
  `load-01..03.json`, `open-loop-01..03.json` raw report 6개를 생성했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/session-03/`:
  `load-01..03.json`, `open-loop-01..03.json` raw report 6개를 생성했다.
- `session-02/summary.json`, `session-02/summary.md`, `session-03/summary.json`, `session-03/summary.md`:
  explicit runner summary artifact 를 생성했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/history.json`, `history.md`:
  runner/date-level history artifact 를 3-session 기준으로 재생성했다.
- `docs/benchmarks/baselines/index.md`:
  runner date-level history, session row, explicit runner reference latency envelope 를 갱신했다.
- `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`:
  3-session reference 완료 상태와 다음 Phase 4 재평가 진입점을 반영했다.

### 검증
- `session-02 --baseline-suite`: baseline-suite-result pass, raw report 6개 생성.
- `session-03 --baseline-suite`: baseline-suite-result pass, raw report 6개 생성.
- `session-02 --summarize-baseline`: source-report-count 6, hard-passed true, warning-count 0.
- `session-03 --summarize-baseline`: source-report-count 6, hard-passed true, warning-count 0.
- `--summarize-baseline-history`: session-count 3, hard-passed true, warning-count 0.
- `history.json`: `comparison-compatible=true`, unknown runner 0, mismatch 0.
- explicit runner envelope: load p99 max 870.7 us, open-loop p99 max 1051.5 us, TCP HWM max 2,
  dropped total 0, payload error total 0, pool rented max 0.
- runner artifact local absolute path 검색 결과 없음.
- `Hps.Benchmarks.Tests`: 67개 통과, 실패 0.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-24 (Codex - explicit runner baseline session-01)

### 작업 단위
- 첫 explicit runner baseline 을 D084 runner group 구조에 수집했다.

### 변경 내용
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/session-01/`:
  `load-01..03.json`, `open-loop-01..03.json` raw report 6개를 생성했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/session-01/summary.json`, `summary.md`:
  explicit runner summary artifact 를 생성했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/history.json`, `history.md`:
  runner/date-level history artifact 를 생성했다.
- `docs/benchmarks/baselines/index.md`:
  runner group, runner date-level history, session row 를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  이번 artifact 수집 완료와 다음 `session-02` 수집 진입점을 반영했다.

### 검증
- `--baseline-suite`: baseline-suite-result pass, raw report 6개 생성.
- `--summarize-baseline`: source-report-count 6, hard-passed true, warning-count 0.
- `--summarize-baseline-history`: session-count 1, hard-passed true, warning-count 0.
- `summary.json`: `runner-id=local-win-x64-01`, `runner-kind=local`, `comparison-compatible=true`,
  unknown runner 0, mismatch 0.
- runner artifact local absolute path 검색 결과 없음.
- `Hps.Benchmarks.Tests`: 67개 통과, 실패 0.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-24 (Codex - explicit runner baseline storage policy)

### 작업 단위
- D083 이후 explicit runner baseline 저장 구조와 수집 정책을 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-24-explicit-runner-baseline-storage-policy-design.md`:
  runner group 아래 date/session 구조, runner id naming guide, history 입력 규칙, index 운영 정책을 작성했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D084를 추가했다.
- `docs/benchmarks/baselines/index.md`:
  명시 runner baseline 운영 원칙과 runner group 섹션을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 진입점을 첫 explicit runner baseline 수집으로 갱신했다.

### 검증
- D079/D080/D082/D083과 `BaselineHistoryReader` directory 규칙을 대조했다.
- 신규 설계/결정/index 문서 임시 표기 검색 결과 없음.
- `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

## 2026-06-24 (Codex - Phase 4 next candidate reassessment)

### 작업 단위
- D082 이후 Phase 4 다음 실행 후보를 재평가하고, 다음 단일 작업 단위를 explicit runner baseline 저장 구조 설계로 선정했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-24-phase4-next-candidate-reassessment.md`:
  후보 A~E를 비교하고, 기존 date root 에 explicit runner session 을 바로 섞지 않는 이유를 기록했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D083을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 진입점을 explicit runner baseline 저장 구조와 수집 정책 설계로 갱신했다.

### 검증
- D082/D079/D080, `BaselineHistoryReader` directory 규칙, `.claude/review/review-status-2026-06-18.md`의 남은 비차단 후속을 대조했다.
- 신규 설계/결정 문서 임시 표기 검색 결과 없음.
- `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

## 2026-06-24 (Codex - latency envelope design review response)

### 작업 단위
- D082 latency envelope/gate 보류 설계 리뷰의 Low 명확성 의견을 문서 batch 로 반영했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-24-latency-envelope-and-gate-deferral-design.md`:
  envelope 집계 방식, `local-unspecified` 표본의 gate 승격 표본 제외, envelope 초과 기록의 수동 리뷰 메모 성격을 명시했다.
- `docs/benchmarks/baselines/index.md`:
  reference envelope 표와 해석 메모에 같은 기준을 추가했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D082 판단 기준에 `local-unspecified` baseline 은 reference 전용이라는 문구를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  리뷰 반영 완료 상태와 다음 Phase 4 후보 재평가 진입점을 반영했다.

### 검증
- D082 review finding 1/2와 info 3 반영 여부를 대조했다.
- 신규 설계/결정/index 문서 임시 표기 검색 결과 없음.
- `git diff --check` 통과. whitespace 오류 없음.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 269개 통과/실패 0.

## 2026-06-24 (Codex - latency envelope and gate deferral design)

### 작업 단위
- 2026-06-24 compatible baseline 3개를 근거로 reference latency envelope 를 재산정하고, warning-as-failure/CI latency gate 보류 조건을 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-24-latency-envelope-and-gate-deferral-design.md`:
  D082 설계 문서를 추가했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D082를 추가했다.
- `docs/benchmarks/baselines/index.md`:
  2026-06-24 reference latency envelope 표와 해석 메모를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  이번 설계 완료 상태와 다음 검토 진입점을 반영했다.

### 검증
- 2026-06-24 `history.json`과 `session-01`/`session-02`/`session-03` `summary.json` 수치를 대조했다.
- 신규 설계/결정/index 문서 임시 표기 검색 결과 없음.
- `git diff --check` 통과. whitespace 오류 없음.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 269개 통과/실패 0.

## 2026-06-24 (Codex - document work batching rule)

### 작업 단위
- 문서 전용 작업은 관련 문서를 한 번에 정렬하고, 코드/테스트 구현 작업은 계속 작은 기능 단위로 유지한다는 실행 규칙을 명시했다.

### 변경 내용
- `AGENT_RULES.md`: 문서 전용 batch 예외와 코드/테스트 변경 분리 경계를 추가했다.
- `DECISIONS.md`: D081을 추가하고 현재 판단 기준에 문서 전용 작업 정렬 원칙을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 이번 문서 규칙 단위 완료 상태와 다음 작업 진입점을 유지했다.

### 검증
- 관련 root 문서에서 `문서 전용`, `D081`, `coherent documentation cycle` 용어 정합성을 대조했다.
- `git diff --check` 통과. whitespace 오류 없음.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 269개 통과/실패 0.

## 2026-06-24 (Codex - current schema baseline session-03)

### 작업 단위
- D079/D080 이후 schema 로 2026-06-24 세 번째 compatible baseline session 을 생성했다.
- 이 단위로 2026-06-24 date root 는 latency envelope 재산정 검토에 필요한 동일 runner compatible session 3개를 갖게 됐다.

### 변경 내용
- `docs/benchmarks/baselines/2026-06-24/session-03/`:
  `load-01..03.json`, `open-loop-01..03.json` raw report 6개를 생성했다.
- `docs/benchmarks/baselines/2026-06-24/session-03/summary.json`, `summary.md`:
  current schema summary artifact 를 생성했다.
- `docs/benchmarks/baselines/2026-06-24/history.json`, `history.md`:
  2026-06-24 date-level history artifact 를 3개 session 기준으로 재생성했다.
- `docs/benchmarks/baselines/index.md`:
  2026-06-24 history session count 와 `session-03` row 를 갱신했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 이번 artifact 단위 완료 상태와 다음 latency envelope 재산정 정책 설계 지점을 반영했다.

### 검증
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --baseline-suite docs\benchmarks\baselines\2026-06-24\session-03 --runs 3`
  결과: baseline-suite-result pass, raw report 6개 생성.
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build -- --summarize-baseline docs\benchmarks\baselines\2026-06-24\session-03 --summary docs\benchmarks\baselines\2026-06-24\session-03\summary.json --summary-md docs\benchmarks\baselines\2026-06-24\session-03\summary.md`
  결과: source-report-count 6, hard-passed true, warning-count 0.
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build -- --summarize-baseline-history docs\benchmarks\baselines\2026-06-24 --history docs\benchmarks\baselines\2026-06-24\history.json --history-md docs\benchmarks\baselines\2026-06-24\history.md`
  결과: session-count 3, hard-passed true, warning-count 0.
- `summary.json` 확인 결과: `comparison-compatible=true`, `unknown-runner-count=0`, `comparison-mismatch-count=0`.
- `docs/benchmarks/baselines/2026-06-24` 아래 local absolute path 검색은 매칭 없음.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 67개 통과/실패 0.
- `git diff --check` exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 269개 통과/실패 0.

## 2026-06-24 (Codex - current schema baseline session-02)

### 작업 단위
- D079/D080 이후 schema 로 2026-06-24 두 번째 compatible baseline session 을 생성했다.
- 이 단위는 새 기능 구현이 아니라, latency envelope 재산정 전 필요한 동일 runner baseline 표본을 하나 더 쌓는 artifact 수집이다.

### 변경 내용
- `docs/benchmarks/baselines/2026-06-24/session-02/`:
  `load-01..03.json`, `open-loop-01..03.json` raw report 6개를 생성했다.
- `docs/benchmarks/baselines/2026-06-24/session-02/summary.json`, `summary.md`:
  current schema summary artifact 를 생성했다.
- `docs/benchmarks/baselines/2026-06-24/history.json`, `history.md`:
  2026-06-24 date-level history artifact 를 2개 session 기준으로 재생성했다.
- `docs/benchmarks/baselines/index.md`:
  2026-06-24 history session count 와 `session-02` row 를 갱신했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 이번 artifact 단위 완료 상태와 다음 `session-03` 수집 지점을 반영했다.

### 검증
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --baseline-suite docs\benchmarks\baselines\2026-06-24\session-02 --runs 3`
  결과: baseline-suite-result pass, raw report 6개 생성.
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build -- --summarize-baseline docs\benchmarks\baselines\2026-06-24\session-02 --summary docs\benchmarks\baselines\2026-06-24\session-02\summary.json --summary-md docs\benchmarks\baselines\2026-06-24\session-02\summary.md`
  결과: source-report-count 6, hard-passed true, warning-count 0.
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build -- --summarize-baseline-history docs\benchmarks\baselines\2026-06-24 --history docs\benchmarks\baselines\2026-06-24\history.json --history-md docs\benchmarks\baselines\2026-06-24\history.md`
  결과: session-count 2, hard-passed true, warning-count 0.
- `summary.json` 확인 결과: `comparison-compatible=true`, `unknown-runner-count=0`, `comparison-mismatch-count=0`.
- `docs/benchmarks/baselines/2026-06-24` 아래 local absolute path 검색은 매칭 없음.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 67개 통과/실패 0.
- `git diff --check` exit 0.
- 첫 solution build 는 직전 testhost 파일 잠금으로 MSB3026 warning 이 있었고, testhost 종료 후 재실행한
  `dotnet build HighPerformanceSocket.slnx --no-restore`는 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 269개 통과/실패 0.

## 2026-06-24 (Codex - current schema baseline session)

### 작업 단위
- D079/D080 이후 schema 로 새 baseline session 을 실제 생성했다.
- 기존 2026-06-18 legacy raw report 는 수정하지 않고, 2026-06-24 date root 에 새 session 을 분리했다.

### 변경 내용
- `docs/benchmarks/baselines/2026-06-24/session-01/`:
  `load-01..03.json`, `open-loop-01..03.json` raw report 6개를 생성했다.
- `docs/benchmarks/baselines/2026-06-24/session-01/summary.json`, `summary.md`:
  current schema summary artifact 를 생성했다.
- `docs/benchmarks/baselines/2026-06-24/history.json`, `history.md`:
  2026-06-24 date-level history artifact 를 생성했다.
- `docs/benchmarks/baselines/index.md`:
  2026-06-24 history row 와 session row 를 추가하고, D079 metadata 이후 첫 comparison-compatible baseline 임을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 이번 artifact 단위 완료 상태와 다음 실행 지점을 반영했다.

### 검증
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --baseline-suite docs\benchmarks\baselines\2026-06-24\session-01 --runs 3`
  결과: baseline-suite-result pass, raw report 6개 생성.
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build -- --summarize-baseline docs\benchmarks\baselines\2026-06-24\session-01 --summary docs\benchmarks\baselines\2026-06-24\session-01\summary.json --summary-md docs\benchmarks\baselines\2026-06-24\session-01\summary.md`
  결과: source-report-count 6, hard-passed true, warning-count 0.
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build -- --summarize-baseline-history docs\benchmarks\baselines\2026-06-24 --history docs\benchmarks\baselines\2026-06-24\history.json --history-md docs\benchmarks\baselines\2026-06-24\history.md`
  결과: session-count 1, hard-passed true, warning-count 0.
- `summary.json` 확인 결과: `comparison-compatible=true`, `unknown-runner-count=0`, `comparison-mismatch-count=0`.
- `docs/benchmarks/baselines/2026-06-24` 아래 local absolute path 검색은 매칭 없음.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 67개 통과/실패 0.
- `git diff --check` exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 269개 통과/실패 0.

## 2026-06-24 (Codex - baseline generated artifact refresh)

### 작업 단위
- 2026-06-18 baseline 의 파생 summary/history artifact 를 현재 D079/D080 schema 로 재생성했다.
- raw 측정 JSON은 원본 artifact 로 보존하고, summary/history 산출물과 index 해석만 갱신했다.
- 재생성 중 발견한 local absolute `source-path` 출력은 reader 단계에서 입력 directory 기준 상대 경로로 보정했다.

### 변경 내용
- `tests/Hps.Benchmarks/BaselineReportReader.cs`:
  `BaselineReport.SourcePath`를 `ReadDirectory(...)` 입력 directory 기준 상대 경로로 보존하게 했다.
- `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`:
  reader 가 local absolute path 대신 상대 source path 를 반환하는지 검증했다.
- `docs/benchmarks/baselines/2026-06-18/summary.json`, `summary.md`:
  root session summary 를 현재 schema 로 재생성해 comparison field 를 포함했다.
- `docs/benchmarks/baselines/2026-06-18/session-02/summary.json`, `summary.md`,
  `docs/benchmarks/baselines/2026-06-18/session-03/summary.json`, `summary.md`:
  session summary artifact 를 현재 schema 로 재생성했다.
- `docs/benchmarks/baselines/2026-06-18/history.json`, `history.md`:
  세 session 을 묶는 date-level history artifact 를 새로 생성했다.
- `docs/benchmarks/baselines/index.md`:
  date-level history 링크와 D079 이전 raw report 의 `unknown-runner` comparison mismatch 해석을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`: artifact 재생성 완료 상태와 다음 실행 지점을 반영했다.

### 검증
- Red: 기존 reader 에서 `ReadDirectory_WhenRunReportIsRead_UsesRelativeSourcePath`가
  `Expected: "load-01.json"` / `Actual: "C:/Users/ADMIN/.../load-01.json"` assertion failure 로 실패함을 확인했다.
- Green: reader 를 상대 path 기준으로 보정한 뒤 focused test 1개 통과.
- root summary CLI: source-report-count 6, hard-passed true, warning-count 0.
- session-02 summary CLI: source-report-count 6, hard-passed true, warning-count 0.
- session-03 summary CLI: source-report-count 6, hard-passed true, warning-count 0.
- history CLI: session-count 3, hard-passed true, warning-count 0.
- `docs/benchmarks/baselines/2026-06-18` 아래 local absolute path 검색(`D:/`, `D:\`, `C:/`, `C:\Users`)은 매칭 없음.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 67개 통과/실패 0.
- `git diff --check` exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 269개 통과/실패 0.

## 2026-06-24 (Codex - benchmark writer metadata roundtrip test hardening)

### 작업 단위
- `TODOS.md`에 남아 있던 P3_NICE benchmark writer metadata roundtrip test gap 을 해소했다.
- 기능 동작 변경은 없고, writer/reader schema drift 를 더 빨리 잡는 테스트만 보강했다.

### 변경 내용
- `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`:
  `TcpLoopbackReportWriter.Write(...)`가 만든 raw report 를 `BaselineReportReader.ReadDirectory(...)`로 다시 읽어
  runner/environment metadata 전체가 roundtrip 되는지 검증했다.
- test identity 는 `os-architecture=Arm64`, `process-architecture=X64`를 서로 다르게 둬 두 field 가 누락되거나
  잘못된 key 로 기록되는 회귀를 구분해서 잡는다.
- `CURRENT_PLAN.md`, `TODOS.md`: deferred test-hardening 항목 완료와 다음 실행 지점을 반영했다.

### 검증
- Red: `TcpLoopbackReportWriter`의 `process-architecture` field 이름을 임시로 바꿨을 때
  `Write_WhenRunResultIsReadBack_PreservesFullRunnerIdentityMetadata`가
  `Expected: "X64", Actual: "unknown"` assertion failure 로 실패함을 확인했다.
- Green: 임시 mutation 을 되돌린 뒤 focused roundtrip test 1개 통과.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 66개 통과/실패 0.
- `git diff --check` exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 268개 통과/실패 0.

## 2026-06-24 (Codex - summary/history comparison signal review hardening)

### 작업 단위
- `.claude/review/2026-06-24-summary-history-comparison-signal-plan-review.md`의 High/Medium 지적을 현재 구현과 대조하고,
  test-hardening 과 판정 술어 문서화로 반영했다.
- 기능 동작 변경은 없고, 이미 구현된 null-key/unknown-runner 경로를 회귀 테스트로 고정했다.

### 변경 내용
- `tests/Hps.Benchmarks.Tests/BaselineSummaryMarkdownWriterTests.cs`:
  legacy/unknown identity summary 에서 `Comparison.Key == null`이어도 Markdown 이 NRE 없이
  `comparison-key: 없음`, `unknown-runner-count`, mismatch row 를 쓰는지 검증했다.
- `tests/Hps.Benchmarks.Tests/BaselineSummaryGeneratorTests.cs`:
  hard comparison identity field 일부만 `unknown`인 report 도 `unknown-runner`로 격리하는지 검증했다.
- `docs/superpowers/plans/2026-06-24-summary-history-comparison-signal.md`:
  partial unknown 판정 기준과 null-key Markdown test/출력 규칙을 보강했다.
- `DECISIONS.md`: hard comparison identity field 중 하나라도 `unknown`이면 compatible 로 추정하지 않는다고 명시했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 리뷰 보강 완료 상태와 검증 근거를 반영했다.

### 검증
- Red 1: `BaselineSummaryMarkdownWriter`의 null-key guard 를 임시 제거했을 때
  `Write_WhenComparisonKeyIsNull_WritesNullKeyAndUnknownRunnerMismatch`가 `NullReferenceException`으로 실패함을 확인했다.
- Red 2: `BaselineSummaryGenerator.IsUnknownIdentity(...)`를 benchmark-profile-only 판정으로 임시 약화했을 때
  `Generate_WhenIdentityHasPartialUnknownField_MarksComparisonIncompatible`가 `Assert.False()` failure 로 실패함을 확인했다.
- Green: 위 임시 mutation 을 되돌린 뒤 focused 보강 tests 2개 통과.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 65개 통과/실패 0.

## 2026-06-24 (Codex - summary/history comparison signal Task 5)

### 작업 단위
- D080 구현 계획의 마지막 단위로 history JSON/Markdown output 과 CLI smoke 에 comparison signal 을 연결했다.
- summary/history comparison signal Task 1~5 구현은 이 단위로 완료됐다.

### 변경 내용
- `tests/Hps.Benchmarks/BaselineHistoryWriter.cs`: history JSON top-level 에 comparison-compatible/key/mismatch field 를 쓰고,
  session entry 에 comparison-compatible, unknown-runner-count, comparison-mismatch-count 를 기록한다.
- `tests/Hps.Benchmarks/BaselineHistoryMarkdownWriter.cs`: history Markdown 에 `## Comparison` section,
  기준 key 요약, workload case table, mismatch table 을 출력한다.
- `tests/Hps.Benchmarks.Tests/BaselineHistoryGeneratorWriterTests.cs`: history JSON comparison field 와
  Markdown comparison section 을 검증했다.
- `tests/Hps.Benchmarks.Tests/BaselineHistoryProgramTests.cs`: runner mismatch-only history 가 hard gate success exit code 를
  유지하면서 comparison mismatch field 를 쓰는지 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 1~5 완료와 현재 실행 작업 없음 상태를 반영했다.

### 검증
- Red 1: history JSON writer test 가 comparison field 부재로 `KeyNotFoundException`을 냄을 확인했다.
- Red 2: Markdown writer test 가 `## Comparison` section 부재로 `Assert.Contains()` 실패함을 확인했다.
- Red 3: Program smoke test 가 history JSON comparison field 부재로 `KeyNotFoundException`을 냄을 확인했다.
- Green: focused Task 5 tests 3개 통과.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 63개 통과/실패 0.

## 2026-06-24 (Codex - summary/history comparison signal Task 4)

### 작업 단위
- D080 구현 계획의 네 번째 단위로 history reader/model/generator 가 comparison signal 을 보존·집계하게 했다.
- history JSON/Markdown output 과 CLI smoke 는 다음 Task 로 분리했다.

### 변경 내용
- `tests/Hps.Benchmarks/BaselineHistorySession.cs`: session 단위 `Comparison` property 를 추가했다.
- `tests/Hps.Benchmarks/BaselineHistory.cs`: history aggregate 단위 `Comparison` property 를 추가했다.
- `tests/Hps.Benchmarks/BaselineHistoryReader.cs`: summary JSON의 comparison field/key/mismatch 를 읽고,
  comparison field 가 없는 legacy summary 는 `legacy-summary-without-comparison` mismatch 로 변환한다.
- `tests/Hps.Benchmarks/BaselineHistoryGenerator.cs`: session comparison key 를 비교해 history-level compatible 여부와
  `history-comparison-key-mismatch`를 계산한다. 기존 hard gate/warning-count 계산은 변경하지 않았다.
- `tests/Hps.Benchmarks.Tests/BaselineHistoryReaderTests.cs`: comparison property contract, summary comparison read,
  legacy summary fallback 을 검증했다.
- `tests/Hps.Benchmarks.Tests/BaselineHistoryGeneratorWriterTests.cs`: compatible sessions, key mismatch,
  incompatible session aggregate 를 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 4 완료와 다음 Task 5 history output/CLI smoke 진입점을 반영했다.

### 검증
- Red 1: `BaselineHistorySession.Comparison`, `BaselineHistory.Comparison` property 부재로 contract tests 2개가
  `Assert.NotNull()` 실패함을 확인했다.
- Contract Green: model property 추가 후 focused contract tests 2개 통과.
- Red 2: reader/generator behavior tests 5개가 stub comparison 에서 `Assert.True()`/`Assert.Single()` 실패함을 확인했다.
- Green 2: focused history reader/generator tests 12개 통과.

## 2026-06-24 (Codex - summary/history comparison signal Task 3)

### 작업 단위
- D080 구현 계획의 세 번째 단위로 summary JSON/Markdown output 에 comparison signal 을 기록했다.
- history reader/generator aggregate 와 history output 은 다음 Task 로 분리했다.

### 변경 내용
- `tests/Hps.Benchmarks/BaselineSummaryWriter.cs`: summary JSON top-level 에 `comparison-compatible`,
  `comparison-key`, `unknown-runner-count`, `comparison-mismatch-count`, `comparison-mismatches`를 기록한다.
- `tests/Hps.Benchmarks/BaselineSummaryMarkdownWriter.cs`: 사람이 runner/case 기준과 mismatch 를 바로 볼 수 있도록
  `## Comparison` section 과 workload case table 을 출력한다.
- `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`: JSON writer comparison field shape 를 검증했다.
- `tests/Hps.Benchmarks.Tests/BaselineSummaryMarkdownWriterTests.cs`: Markdown comparison section 출력과 핵심 key field 를 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 3 완료와 다음 Task 4 history reader/generator 진입점을 반영했다.

### 검증
- Red 1: summary JSON writer test 가 `comparison-compatible` field 부재로 `KeyNotFoundException`을 냄을 확인했다.
- Green 1: focused JSON writer test 1개 통과.
- Red 2: Markdown writer test 가 `## Comparison` section 부재로 `Assert.Contains()` 실패함을 확인했다.
- Green 2: focused Markdown writer tests 3개 통과.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 53개 통과/실패 0.

## 2026-06-24 (Codex - summary/history comparison signal Task 2)

### 작업 단위
- D080 구현 계획의 두 번째 단위로 summary comparison model/generator 를 추가했다.
- summary JSON/Markdown output 과 history aggregation 은 다음 Task 로 분리했다.

### 변경 내용
- `tests/Hps.Benchmarks/BaselineComparisonCase.cs`: `result-name`별 scenario/payload/target case model 을 추가했다.
- `tests/Hps.Benchmarks/BaselineComparisonKey.cs`: runner/environment key 와 case 목록 model 을 추가했다.
- `tests/Hps.Benchmarks/BaselineComparisonMismatch.cs`: summary/history 공용 mismatch entry model 을 추가했다.
- `tests/Hps.Benchmarks/BaselineComparisonResult.cs`: compatible 여부, key, unknown runner count, mismatch 목록 aggregate model 을 추가했다.
- `tests/Hps.Benchmarks/BaselineSummary.cs`: `Comparison` property 를 추가했다.
- `tests/Hps.Benchmarks/BaselineSummaryGenerator.cs`: source report 목록에서 no-source, unknown-runner,
  runner/case mismatch, compatible key 를 계산한다. `processor-count`는 comparison key 에 포함하지 않는다.
- `tests/Hps.Benchmarks.Tests/BaselineSummaryGeneratorTests.cs`: compatible, unknown identity, runner mismatch,
  empty report comparison behavior 를 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 2 완료와 다음 Task 3 summary output 진입점을 반영했다.

### 검증
- Red 1: `BaselineSummary.Comparison` property 부재로 contract test 가 `Assert.NotNull()` 실패함을 확인했다.
- Contract Green: comparison model stubs 와 summary property 추가 후 focused contract test 1개 통과.
- Red 2: compatible behavior test 가 stub comparison 에서 `Expected: True, Actual: False`로 실패함을 확인했다.
- Green: focused `BaselineSummaryGeneratorTests` 8개 통과.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 51개 통과/실패 0.

## 2026-06-24 (Codex - summary/history comparison signal Task 1)

### 작업 단위
- D080 구현 계획의 첫 번째 단위로 raw report payload/target settings 를 `BaselineReport`까지 전파했다.
- summary comparison model/generator 와 JSON/Markdown 출력은 다음 Task 로 분리했다.

### 변경 내용
- `tests/Hps.Benchmarks/BaselineReport.cs`: `PayloadBytes`, `TargetRateHz`, `TargetDurationSeconds` property 를 추가했다.
- `tests/Hps.Benchmarks/BaselineReportReader.cs`: raw report 의 `payload-bytes`, `target-rate-hz`,
  `target-duration-seconds` field 를 읽어 `BaselineReport` 생성자로 전달한다.
- `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`: payload/target property contract 와 reader behavior test 를 추가했다.
- `tests/Hps.Benchmarks.Tests/BaselineSummaryGeneratorTests.cs`,
  `tests/Hps.Benchmarks.Tests/BaselineSummaryMarkdownWriterTests.cs`: direct `BaselineReport` helper 생성자 호출에
  현재 benchmark 기본값 `4096`, `100.0`, `30`을 명시했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 1 완료와 다음 Task 2 summary comparison model/generator 진입점을 반영했다.

### 검증
- Red 1: focused contract test 가 `BaselineReport` payload/target property 부재로 `Assert.NotNull()` 실패함을 확인했다.
- Contract Green: property surface 추가 후 focused contract test 1개 통과.
- Red 2: reader behavior test 가 `Expected: 4096, Actual: 0`으로 실패함을 확인했다.
- Green: focused reader behavior test 1개 통과.
- Refactor 검증: focused `BaselineReportReaderWriterTests` 8개 통과, focused `BaselineSummary*` 6개 통과.
  같은 csproj 대상 focused tests 를 병렬 실행했을 때 DLL lock 이 발생해, 두 번째 focused test 는 `--no-build --no-restore`로 순차 재실행했다.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 46개 통과/실패 0.

## 2026-06-24 (Codex - summary/history comparison signal implementation plan)

### 작업 단위
- D080 summary/history comparison signal 설계를 실제 구현 가능한 5개 작은 커밋 단위로 분해했다.
- 코드 구현과 generated baseline artifact 재생성은 이번 범위에서 제외했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-24-summary-history-comparison-signal.md`:
  `BaselineReport` payload/target settings, summary comparison model/generator, summary output,
  history reader/generator, history output/CLI smoke Task 를 작성했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 실행 지점을 Task 1 `BaselineReport` payload/target settings 구현으로 갱신했다.

### 검증
- D080 설계와 현재 `BaselineReport`, `BaselineReportReader`, `BaselineSummary*`, `BaselineHistory*`,
  관련 benchmark tests 구조를 대조했다.
- 각 Task 의 touched files, assertion-failure Red, focused test, 커밋 경계, 테스트 주석 요구를 계획에 명시했다.
- 전체 repository 검증은 계획 문서 placeholder scan, `git diff --check`, solution build/test 로 수행한다.

## 2026-06-23 (Codex - summary/history comparison signal design)

### 작업 단위
- D079 raw report metadata 이후 summary/history 가 비교 가능성을 어떻게 보존·표현할지 설계했다.
- 코드 구현, generated baseline artifact 재생성, warning-as-failure 정책은 이번 범위에서 제외했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-23-summary-history-comparison-signal-design.md`:
  summary/history comparison signal schema, mismatch 표현, Markdown 출력, 후속 구현 단위를 정리했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`: D080을 추가했다.
  comparison signal 은 hard gate, 기존 `warning-count`, CLI exit code 와 분리된 non-failing compatibility artifact 로 둔다.
- `CURRENT_PLAN.md`, `TODOS.md`: 설계 완료와 다음 구현 계획 작성 진입점을 반영했다.

### 검증
- current `BaselineReport`, `BaselineSummary*`, `BaselineHistory*`, D079 raw writer/reader 구조를 대조했다.
- summary 안의 `load`와 `open-loop`이 서로 다른 `scenario`를 가질 수 있어, comparison key 를 단일 scenario 가 아니라
  `result-name`별 `cases` 배열로 설계했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 246개 통과/실패 0.

## 2026-06-23 (Codex - benchmark runner identity implementation review)

### 작업 단위
- benchmark runner identity Task 1~3 구현을 D079 설계와 구현 계획 기준으로 검토했다.

### 변경 내용
- `docs/agent-state/reviews/2026-06-23-benchmark-runner-identity-implementation-review.md`:
  구현 검토 결과, Minor testing 관찰, deferred item, unresolved decision 을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 구현 검토 완료와 다음 summary/history comparison signal 설계 진입점을 반영했다.

### 검증
- D079 raw metadata field, privacy 기본값, writer/reader field name, legacy fallback, focused tests 를 소스와 문서로 대조했다.
- 새 Blocker/Major finding 은 없다.
- Minor testing 관찰: writer shape test 가 실제 writer output 의 architecture field 2개를 직접 assert하지 않아,
  future field drift 를 더 강하게 잡으려면 writer-to-reader roundtrip test 가 유용하다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 246개 통과/실패 0.

## 2026-06-23 (Codex - benchmark runner identity Task 3 raw report reader)

### 작업 단위
- benchmark runner identity 구현 계획의 세 번째 단위로 raw report reader 와 legacy compatibility 를 연결했다.

### 변경 내용
- `tests/Hps.Benchmarks/BaselineReport.cs`: raw report reader 결과가 `BenchmarkRunIdentity`를 보존하도록 `Identity` property 를 추가했다.
  metadata 가 없는 경우 기본값은 `BenchmarkRunIdentity.Unknown`이다.
- `tests/Hps.Benchmarks/BaselineReportReader.cs`: 신규 raw report 의 runner/environment metadata 를 optional field 로 읽고,
  legacy raw report 는 `Unknown` identity 로 유지한다.
- `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`: `BaselineReport.Identity` contract, metadata read,
  legacy fallback 을 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 3 완료와 다음 구현 검토 진입점을 반영했다.

### 검증
- Red 1: `BaselineReport.Identity` property 부재로 focused contract test 가 `Assert.NotNull()` 실패함을 확인했다.
- Contract Green: `BaselineReport.Identity` 추가 후 focused contract test 1개 통과.
- Red 2: metadata 포함 raw report reader test 가 `Expected: tcp-loopback-saea-v1, Actual: unknown`으로 실패함을 확인했다.
- Green: focused `BaselineReportReaderWriterTests` 6개 통과.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 44개 통과/실패 0.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 246개 통과/실패 0.

## 2026-06-23 (Codex - benchmark runner identity Task 2 raw report writer metadata)

### 작업 단위
- benchmark runner identity 구현 계획의 두 번째 단위로 raw report writer metadata 를 연결했다.

### 변경 내용
- `tests/Hps.Benchmarks/TcpLoopbackRunResult.cs`: optional `BenchmarkRunIdentity`를 보존하는 `Identity` property 를 추가했다.
  명시 identity 가 없으면 privacy 우선 `BenchmarkRunIdentity.CaptureDefault()`를 사용한다.
- `tests/Hps.Benchmarks/TcpLoopbackReportWriter.cs`: raw report schema v1 top-level 에 runner/environment metadata field 를 additive 로 기록한다.
- `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`: writer output 이 identity metadata 를 포함하는지 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 2 완료와 다음 Task 3(raw report reader/legacy compatibility) 진입점을 반영했다.

### 검증
- Red: focused writer metadata test 가 `benchmark-profile` 미기록으로 `Assert.True()` 실패함을 확인했다.
- Green: focused writer metadata test 1개 통과.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 41개 통과/실패 0.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 243개 통과/실패 0.

## 2026-06-23 (Codex - benchmark runner identity Task 1 model)

### 작업 단위
- benchmark runner identity 구현 계획의 첫 번째 단위로 `BenchmarkRunIdentity` model 을 추가했다.

### 변경 내용
- `tests/Hps.Benchmarks/BenchmarkRunIdentity.cs`: raw report metadata 에 사용할 benchmark profile, runner id/kind,
  transport backend, runtime OS/framework/architecture 정보를 보존하는 내부 model 을 추가했다.
- `tests/Hps.Benchmarks.Tests/BenchmarkRunIdentityTests.cs`: 타입 계약, privacy 우선 기본값, 환경 변수 override 를 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 1 완료와 다음 Task 2(raw report writer metadata) 진입점을 반영했다.

### 검증
- Red 1: focused contract test 가 타입 부재로 `Assert.NotNull()` 실패함을 확인했다.
- Stub Green: stub type 추가 후 focused contract test 1개 통과.
- Red 2: behavior tests 2개가 stub `unknown` 반환으로 실패함을 확인했다.
- Green: focused `BenchmarkRunIdentityTests` 3개 통과.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 40개 통과/실패 0.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 242개 통과/실패 0.

## 2026-06-23 (Codex - benchmark runner identity implementation plan)

### 작업 단위
- D079 benchmark runner identity 설계를 구현 가능한 3개 커밋 단위로 분해했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-23-benchmark-runner-identity.md`: identity model, raw report writer metadata,
  raw report reader/legacy compatibility Task 를 Red-Green-Refactor 경로와 커밋 단위로 작성했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 실행 지점을 Task 1 `BenchmarkRunIdentity` model 구현으로 갱신했다.

### 검증
- D079 설계 문서와 실제 `tests/Hps.Benchmarks` writer/reader/source model, 기존 benchmark test 패턴을 대조했다.
- 계획 self-review 로 D079 coverage, type consistency, commit boundary 를 확인했다.
- 계획 문서 placeholder scan 결과 없음.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 239개 통과/실패 0.

## 2026-06-23 (Codex - benchmark runner identity design)

### 작업 단위
- baseline history command 이후 남은 Phase 4 backlog 를 재평가하고, 다음 구현 후보를 benchmark runner identity/environment metadata 로 좁혔다.

### 변경 내용
- `docs/superpowers/specs/2026-06-23-benchmark-runner-identity-design.md`: raw report schema v1 additive metadata, privacy 우선 기본값,
  summary/history comparison signal 방향, 범위 밖 항목을 정리했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`: D079를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`: backlog 재평가 완료와 다음 구현 계획 진입점을 반영했다.

### 검증
- `PLAN.md`, `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`, baseline 관련 spec/review 와 benchmark writer/reader/source model 을 대조했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 239개 통과/실패 0.

## 2026-06-23 (Codex - baseline history command implementation review)

### 작업 단위
- baseline history report command Task 1~4 전체 구현을 D078 계약과 대조해 검토했다.

### 변경 내용
- `docs/agent-state/reviews/2026-06-23-baseline-history-command-implementation-review.md`: parser, reader, aggregate writer,
  Program wiring, tests, 실제 baseline root CLI smoke 를 기준으로 구현 검토 결과를 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 구현 검토 완료와 다음 Phase 4 backlog 재평가/설계 진입점을 반영했다.

### 검증
- 실제 CLI smoke 로 `--summarize-baseline-history docs\benchmarks\baselines` 실행 결과 `session-count: 3`,
  `hard-passed: true`, `warning-count: 0`을 확인했다.
- 생성 JSON에서 `history-version: 1`, `failed-session-count: 0`, `/` separator relative summary path 를 확인했다.
- 생성 Markdown은 `Get-Content -Encoding UTF8` 기준 한글 header 와 session table 이 정상 표시됨을 확인했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 239개 통과/실패 0.

## 2026-06-23 (Codex - baseline history report command Task 4 Program wiring)

### 작업 단위
- baseline history report command 의 네 번째 구현 단위로 `Program.Main` 실행 경로와 CLI smoke coverage 를 추가했다.

### 변경 내용
- `tests/Hps.Benchmarks/Program.cs`: `BenchmarkCommand.SummarizeBaselineHistory` branch 를 추가하고,
  `BaselineHistoryReader` → `BaselineHistoryGenerator` → `BaselineHistoryWriter`/`BaselineHistoryMarkdownWriter` 경로를 연결했다.
- `tests/Hps.Benchmarks.Tests/BaselineHistoryProgramTests.cs`: passing summary, failed summary, warning-only summary 의
  CLI exit code 와 artifact 생성을 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 4 완료와 다음 구현 검토 게이트를 반영했다.

### 검증
- Red: `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineHistoryProgramTests`
  에서 Program tests 3개가 usage error exit code 2 반환으로 실패함을 확인했다.
- Green: 같은 focused Program tests 3개 통과.
- CLI smoke: 첫 `dotnet run`은 restore 네트워크 접근 때문에 sandbox 에서 실패했고,
  `dotnet run --no-build --no-restore --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --summarize-baseline-history docs\benchmarks\baselines ...`
  로 재실행해 session-count 3, hard-passed true, warning-count 0 출력을 확인했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
  비고: Benchmark 프로젝트 assets 가 sandbox package folder 를 가리켜 처음에는 실패했으므로,
  로컬 NuGet cache 를 `--source`/`--packages`로 명시한 restore 후 재검증했다.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 239개 통과/실패 0.

## 2026-06-23 (Codex - baseline history report command Task 3 history writer)

### 작업 단위
- baseline history report command 의 세 번째 구현 단위로 history aggregate 와 JSON/Markdown writer 를 추가했다.

### 변경 내용
- `tests/Hps.Benchmarks/BaselineHistory.cs`: history root aggregate model 을 추가했다.
- `tests/Hps.Benchmarks/BaselineHistoryGenerator.cs`: session `hard-passed` AND, `failed-session-count`, warning count 집계를 구현했다.
- `tests/Hps.Benchmarks/BaselineHistoryWriter.cs`: stable JSON schema writer 를 추가했다.
- `tests/Hps.Benchmarks/BaselineHistoryMarkdownWriter.cs`: 사람이 읽는 session table/warning session list writer 를 추가했다.
- `tests/Hps.Benchmarks.Tests/BaselineHistoryGeneratorWriterTests.cs`: aggregate count, zero raw failure hard fail, JSON shape, null p99, Markdown table 을 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 3 완료와 다음 Task 4(Program wiring/smoke) 진입점을 반영했다.

### 검증
- Red 1: focused generator/writer contract test 에서 `BaselineHistoryGenerator` 타입 미존재로 `Assert.NotNull()` 실패를 확인했다.
- Stub Green: aggregate/writer stub 추가 후 focused contract test 1개 통과.
- Red 2: behavior tests 5개가 aggregate/writer stub 에서 실패함을 확인했다.
- Green: focused generator/writer tests 5개 통과.
- `git diff --check` 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 236개 통과/실패 0.

## 2026-06-23 (Codex - baseline history report command Task 2 history reader)

### 작업 단위
- baseline history report command 의 두 번째 구현 단위로 session domain model 과 summary reader 를 추가했다.

### 변경 내용
- `tests/Hps.Benchmarks/BaselineHistorySession.cs`: history session 의 date/session/path/count/pass/warning/p99/HWM 값을 보존하는 immutable model 을 추가했다.
- `tests/Hps.Benchmarks/BaselineHistoryReader.cs`: date root 와 parent baseline root 를 bounded discovery 로 읽고, summary JSON schema v1을 `BaselineHistorySession`으로 변환한다.
- `tests/Hps.Benchmarks.Tests/BaselineHistoryReaderTests.cs`: date root, parent root, by-kind 누락, summary 없음 경계를 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 2 완료와 다음 Task 3(history aggregate/writer) 진입점을 반영했다.

### 검증
- Red 1: focused reader contract test 에서 `BaselineHistoryReader` 타입 미존재로 `Assert.NotNull()` 실패를 확인했다.
- Stub Green: 타입/메서드 stub 추가 후 focused contract test 1개 통과.
- Red 2: behavior tests 4개가 stub `NotSupportedException`으로 실패함을 확인했다.
- Green: focused reader tests 4개 통과.
- `git diff --check` 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 231개 통과/실패 0.

## 2026-06-23 (Codex - baseline history report command Task 1 parser contract)

### 작업 단위
- baseline history report command 의 첫 구현 단위로 parser/usage contract 만 추가했다.

### 변경 내용
- `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`: history command 성공/Markdown 선택/필수 `--history`/`--history-md` 경계/`--report` 혼용 거부 테스트 5개를 추가했다.
- `tests/Hps.Benchmarks/BenchmarkCommand.cs`: `SummarizeBaselineHistory` command 값을 추가했다.
- `tests/Hps.Benchmarks/BenchmarkCommandLine.cs`: history input root, JSON output path, Markdown output path 보존 필드를 추가했다.
- `tests/Hps.Benchmarks/BenchmarkCommandParser.cs`: `--summarize-baseline-history <baseline-root> --history <output-json> [--history-md <output-md>]` parsing 을 추가했다.
- `tests/Hps.Benchmarks/Program.cs`: usage text 에 history command 를 추가했다. 실행 switch wiring 은 Task 4 범위로 남겼다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 1 완료와 다음 Task 2(history domain/reader) 진입점을 반영했다.

### 검증
- Red: `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BenchmarkCommandParserTests`
  에서 새 history command 테스트 5개 실패를 확인했다.
- Green: 같은 focused parser tests 15개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 227개 통과/실패 0.

## 2026-06-23 (Codex - baseline history report command 계획 리뷰 보정)

### 작업 단위
- baseline history report command 구현 계획에 대한 리뷰 의견을 구현 전 계약 보정으로 반영했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-23-baseline-history-report-command-design.md`: history root 실패 카운터를
  `failed-session-count`로 고정하고, 누락 p99 를 JSON `null`/Markdown `-`로 표현하도록 보정했다.
- `docs/superpowers/plans/2026-06-23-baseline-history-report-command.md`: reader/generator/writer/test 계획을
  session `hard-passed` AND, `failed-session-count`, nullable p99 계약에 맞춰 갱신했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`: D078 영향 범위에 위 계약을 명시했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 실행점은 Task 1(parser contract)로 유지하고, 보정된 history 계약을 추가했다.

### 검증
- `rg`로 output root 의 옛 `hard-failure-count`와 p99 `0` fallback 이 설계/계획에 남지 않았는지 확인했다.
  남은 `hard-failure-count`는 입력 summary schema 와 session raw field 읽기 용도다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 222개 통과/실패 0.

## 2026-06-23 (Codex - baseline history report command 구현 계획)

### 작업 단위
- D078 baseline history report command 설계를 실제 구현 가능한 Task 1~4 계획으로 분해했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-23-baseline-history-report-command.md`: parser contract, history reader, aggregate writer,
  Program wiring/smoke 의 4개 커밋 단위 계획을 추가했다.
- Task 1은 `BenchmarkCommandParser`와 usage text 만 다루고, 실행 wiring 은 Task 4로 분리했다.
- Task 2/3은 새 타입 도입 시 컴파일 실패 Red 를 피하기 위해 reflection contract Red → stub → behavior Red 순서를 명시했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 진입점을 Task 1(parser contract) 구현으로 갱신했다.

### 검증
- `docs/superpowers/specs/2026-06-23-baseline-history-report-command-design.md`, D078, 설계 리뷰 문서, benchmark parser/source,
  summary reader/writer/test 패턴을 대조했다.
- 계획 self-review 로 spec coverage, placeholder scan, type consistency, commit boundary 를 확인했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 222개 통과/실패 0.

## 2026-06-23 (Codex - baseline history report command 설계 리뷰)

### 작업 단위
- baseline history report command 설계를 구현 전 리뷰 게이트로 검토하고, 구현자가 흔들릴 수 있는 모호성을 닫았다.

### 변경 내용
- `docs/superpowers/specs/2026-06-23-baseline-history-report-command-design.md`: command enum 이름을
  `BenchmarkCommand.SummarizeBaselineHistory`로 고정하고, parent baseline root/date root 입력 discovery 규칙을 분리했다.
- `docs/agent-state/reviews/2026-06-23-baseline-history-report-command-design-review.md`: 설계 리뷰 결과와 보정한 finding 2건을 기록했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`: D078로 history command 를 provider-independent aggregate artifact 로 두고,
  warning 은 계속 soft signal 로 유지한다고 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 진입점을 baseline history report command 구현 계획 작성으로 갱신했다.

### 검증
- `BenchmarkCommand`, `BenchmarkCommandLine`, `BenchmarkCommandParser`, `Program`, summary writer/generator, baseline summary artifact 구조를 대조했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 222개 통과/실패 0.

## 2026-06-23 (Codex - Phase 4 backlog 재평가)

### 작업 단위
- stable identity / UDP lease sweep must-fix 체인 종료 후 Phase 4 backlog 를 재평가하고 다음 구현 후보를 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-23-baseline-history-report-command-design.md`: 여러 baseline session `summary.json`을 읽어
  `history.json`과 선택적 `history.md`를 생성하는 provider-independent command 설계를 추가했다.
- 다음 구현 후보는 `--summarize-baseline-history <baseline-root> --history <output-json> [--history-md <output-md>]`로 좁혔다.
- CI workflow, warning-as-failure, latency hard gate, 기존 `index.md` 자동 덮어쓰기는 범위 밖으로 남겼다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 진입점을 baseline history report command 설계 리뷰로 갱신했다.

### 검증
- `PLAN.md`, `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`를 읽고 현재 Phase 4 진입점을 대조했다.
- `docs/superpowers/specs/2026-06-18-repeat-baseline-policy-design.md`,
  `docs/superpowers/specs/2026-06-18-ci-repeat-baseline-policy-design.md`,
  `docs/superpowers/specs/2026-06-18-baseline-report-history-warning-policy-design.md`,
  `.claude/review/2026-06-18-repeat-baseline-policy-review.md`를 확인했다.
- `tests/Hps.Benchmarks`와 `tests/Hps.Benchmarks.Tests`의 현재 CLI/parser/summary 구조를 확인해 설계가 기존 경로를 재사용하는지 검토했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 222개 통과/실패 0.

## 2026-06-23 (Codex - UDP lease sweep registry race guard review gate)

### 작업 단위
- 직전 `a817c6e` UDP lease sweep registry race guard 수정분을 다음 구현 전 리뷰 게이트로 검토했다.

### 변경 내용
- `docs/agent-state/reviews/2026-06-23-udp-lease-sweep-race-guard-review.md`: handler gate 직렬화, PUBLISH fan-out lock 범위, race regression test 를 검토한 문서를 추가했다.
- Blocker/Major correctness finding 은 발견하지 못했다.
- race regression test 의 250ms scheduling window 는 fixed path 판단을 막지 않는 Minor 관찰로 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`: stable identity / UDP lease sweep must-fix 체인이 닫힌 상태와 다음 Phase 4 backlog 재평가 진입점을 반영했다.

### 검증
- `git show --stat --oneline a817c6e`와 `git show -- src\Hps.Broker\BrokerUdpDatagramHandler.cs`,
  `git show -- tests\Hps.Broker.Tests\BrokerUdpDatagramHandlerTests.cs`로 수정 범위를 확인했다.
- `rg`로 D077, handler gate, sweep/register race test, PUBLISH lock-outside 경계를 대조했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 222개 통과/실패 0.

## 2026-06-23 (Codex - UDP lease sweep registry race guard)

### 작업 단위
- F1 후속 must-fix 로 UDP lease sweep registry cleanup 의 stale snapshot race 를 막았다.

### 변경 내용
- `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`: sweep 이 expired target snapshot 을 만든 뒤 같은 stable target 이
  다시 `REGISTER`되는 interleave 를 deterministic 하게 재현하는 회귀 테스트를 추가했다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: UDP receive command, endpoint close cleanup, lease sweep state mutation 을
  handler-local gate 로 직렬화했다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: `PUBLISH`는 lease activity 만 gate 안에서 갱신하고, 실제 fan-out 은 lock 밖에서 수행한다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`: D077로 handler gate 선형화 결정을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`: race guard 완료와 다음 review gate 를 반영했다.

### 검증
- Red: focused race test 에서 `Assert.True()` failure 를 확인했다.
- Green: 같은 focused race test 통과.
- Focused regression: `BrokerUdpDatagramHandlerTests` 17개 통과, `Hps.Broker.Tests` 73개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 222개 통과/실패 0.

## 2026-06-23 (Codex - UDP stable identity F1/F2 review gate)

### 작업 단위
- 직전 UDP stable identity F1/F2 수정 커밋을 다음 구현 전 리뷰 게이트로 검토했다.

### 변경 내용
- `docs/agent-state/reviews/2026-06-23-udp-stable-identity-f1-f2-review.md`: F1/F2 수정분 리뷰 문서를 추가했다.
- F2 invalid identity datagram isolation 은 UDP shared endpoint close 를 막는 방향으로 정합하다고 판단했다.
- F1 lease sweep registry cleanup 에 stale snapshot race 가 남아 있음을 Major finding 으로 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 단일 작업을 stale snapshot race must-fix 로 갱신했다.

### 검증
- `rg`로 `BrokerServer` timer callback, `BrokerUdpDatagramHandler.SweepExpiredUdpLeases(...)`,
  `UdpRemoteLeaseTracker.SweepExpired(...)`, UDP `OnDatagramReceived(...)`/`RegisterUdpTarget(...)` 경계를 대조했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 221개 통과/실패 0.

## 2026-06-23 (Codex - UDP invalid stable identity datagram isolation)

### 작업 단위
- Stable subscriber identity 교차검증 F2 must-fix 를 처리했다.
- UDP `REGISTER`/`UNREGISTER` identity validation 실패가 handler 밖으로 escape 하지 않게 했다.

### 변경 내용
- `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`: tab 이 포함된 invalid identity token 을 가진
  `REGISTER`/`UNREGISTER` datagram 이 예외 없이 drop 되고, endpoint close 없이 기존 subscription 을 보존하는지 검증했다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: `REGISTER`/`UNREGISTER` 처리 전에 stable identity token 을
  비예외 방식으로 검사하는 `TryDecodeIdentity(...)`를 추가했다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: 검증된 `SubscriberIdentity`만 registry 경로로 넘기도록
  `RegisterUdpTarget(...)` 경계를 정리했다.
- `CURRENT_PLAN.md`, `TODOS.md`: F2 완료와 다음 review gate 를 반영했다.

### 검증
- Red: `dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter FullyQualifiedName~OnDatagramReceived_WhenStableIdentityTokenIsInvalid_DropsDatagramWithoutThrowingOrClosingEndpoint`
  에서 `REGISTER`/`UNREGISTER` 두 케이스 모두 `Assert.Null()` failure 를 확인했다.
- Green: 같은 focused invalid identity test 2개 통과.
- Focused regression: `dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter FullyQualifiedName~BrokerUdpDatagramHandlerTests`
  통과, 16개 통과/실패 0.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 221개 통과/실패 0.

## 2026-06-23 (Codex - UDP stable identity lease sweep registry cleanup)

### 작업 단위
- Stable subscriber identity 교차검증 F1 must-fix 를 처리했다.
- UDP lease sweep 이 만료 remote target 을 stable registry 에도 disconnected 로 반영하게 했다.

### 변경 내용
- `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`: registered UDP remote 가 idle sweep 으로 만료된 뒤
  retention sweep 대상이 되는지 검증하는 회귀 테스트를 추가했다.
- `src/Hps.Broker/UdpRemoteLeaseTracker.cs`: 기존 `SweepExpired(DateTimeOffset)` 반환값은 routing 제거 수로 유지하고,
  registry cleanup 용 expired target snapshot 을 선택적으로 채우는 overload 를 추가했다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: registry 주입 경로에서 만료 target snapshot 을 받아
  `SubscriberRegistry.RemoveTarget(...)`으로 current target 을 disconnected 상태로 전환한다.
- `CURRENT_PLAN.md`, `TODOS.md`: F1 완료와 다음 F2(UDP invalid identity datagram 격리) 진입점을 반영했다.

### 검증
- Red: `dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter FullyQualifiedName~SweepExpiredUdpLeases_WhenRegisteredRemoteExpires_MarksRegistryTargetDisconnected`
  에서 `Expected: 1, Actual: 0` assertion failure 를 확인했다.
- Green: 같은 focused test 1개 통과.
- Focused regression: `dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter FullyQualifiedName~BrokerUdpDatagramHandlerTests`
  통과, 14개 통과/실패 0.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 219개 통과/실패 0.

## 2026-06-23 (Codex - Stable subscriber identity post-implementation cross-verification)

### 작업 단위
- D075/D076 stable subscriber identity 구현 전체를 설계/코드/테스트 기준으로 교차검증했다.

### 변경 내용
- `docs/agent-state/reviews/2026-06-23-stable-subscriber-identity-cross-check.md`: post-implementation review 문서를 추가했다.
- UDP stable identity lease sweep 이 `SubscriberRegistry`를 disconnected 상태로 바꾸지 않는 must-fix 를 기록했다.
- UDP invalid stable identity command 예외가 shared UDP endpoint close 로 이어질 수 있는 must-fix 를 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 실행 단위를 F1 수정으로 갱신하고 F2를 그 다음 단위로 기록했다.

### 검증
- `rg`와 줄 번호 확인으로 stable identity 설계, 구현, 테스트 경계를 대조했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 218개 통과/실패 0.

## 2026-06-22 (Codex - Stable subscriber identity UDP loopback coverage)

### 작업 단위
- Stable subscriber identity UDP rebind 가 실제 UDP datagram loopback 에서도 유지되는지 coverage 를 추가했다.

### 변경 내용
- `tests/Hps.Server.Tests/BrokerServerTests.cs`: `BrokerServerOptions.CreateWithStableSubscriberIdentity(...)`를 켠
  실제 `BrokerServer` + `SaeaTransport` UDP loopback 테스트를 추가했다.
- 테스트는 old remote 가 `REGISTER device-a` 후 `SUBSCRIBE alpha`를 보내고, new remote 가 같은 id 로 `REGISTER`만 했을 때
  retained topic set 이 new remote 로 재바인딩되어 이후 publish payload 를 받는지 검증한다.
- UDP는 old remote 를 transport 차원에서 close 할 수 없으므로, routing table 에서 old remote target 만 제거하는 정책을
  실제 datagram 송수신 경로로 고정한다.
- `CURRENT_PLAN.md`, `TODOS.md`: 이번 coverage 단위와 다음 리뷰 대기 상태를 반영했다.

### 검증
- Focused: `dotnet test tests\Hps.Server.Tests\Hps.Server.Tests.csproj --filter FullyQualifiedName~UdpCommandLoopback_WhenStableSubscriberRemoteRebinds_RoutesPayloadToNewRemote` 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 218개 통과/실패 0.

## 2026-06-22 (Codex - Stable subscriber identity TCP loopback coverage)

### 작업 단위
- Stable subscriber identity 구현 완료 게이트를 강화하기 위해 실제 TCP loopback coverage 를 추가했다.

### 변경 내용
- `tests/Hps.Server.Tests/BrokerServerTests.cs`: `BrokerServerOptions.CreateWithStableSubscriberIdentity(...)`를 켠
  실제 `BrokerServer` + `SaeaTransport` loopback 테스트를 추가했다.
- 테스트는 old subscriber 가 `REGISTER device-a` 후 `SUBSCRIBE alpha`를 보내고, new subscriber 가 같은 id 로 `REGISTER`만 했을 때
  old socket 이 닫히고 new socket 이 이후 publish payload 를 받는지 검증한다.
- old socket close helper 는 Windows loopback 에서 FIN 대신 `ConnectionReset`이 올 수 있어 두 관측값을 close 완료로 처리한다.
- `CURRENT_PLAN.md`, `TODOS.md`: 이번 coverage 단위와 다음 리뷰 대기 상태를 반영했다.

### 검증
- Focused: `dotnet test tests\Hps.Server.Tests\Hps.Server.Tests.csproj --filter FullyQualifiedName~TcpCommandLoopback_WhenStableSubscriberReconnects_RebindsTopicToNewSocket` 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 217개 통과/실패 0.

## 2026-06-22 (Codex - Stable subscriber identity UDP late REGISTER lease cleanup)

### 작업 단위
- Stable subscriber identity self-review 중 발견한 UDP late `REGISTER` lease metadata 누수를 단일 TDD 보강으로 처리했다.

### 변경 내용
- `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`: UDP remote 가 `SUBSCRIBE` 후 `REGISTER`하면
  pre-register runtime lease 가 제거되는지 검증하는 회귀 테스트를 추가했다.
- `src/Hps.Broker/UdpRemoteLeaseTracker.cs`: 같은 remote 의 lease metadata 를 registry rebound topic set 으로
  완전히 교체하는 `ReplaceSubscribedTopics(...)`를 추가했다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: `REGISTER` 성공 후 UDP lease metadata 를 stable topic set 으로 교체한다.
- `docs/superpowers/specs/2026-06-22-stable-subscriber-identity-reconnect-policy-design.md`: D076 late `REGISTER` 정책에
  UDP lease metadata cleanup 기준을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 이번 보강 단위와 다음 리뷰 대기 상태를 반영했다.

### 검증
- Red: focused `BrokerUdpDatagramHandlerTests`에서 late `REGISTER` 이후 pre-register runtime lease 가 남는 assertion failure 1개 확인.
- Green/Refactor: focused `BrokerUdpDatagramHandlerTests` 13개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 216개 통과/실패 0.

## 2026-06-22 (Codex - Stable subscriber identity late REGISTER cleanup)

### 작업 단위
- Stable subscriber identity 구현분 self-review 중 발견한 late `REGISTER` stale subscription 결함을 단일 TDD 보강으로 처리했다.

### 변경 내용
- `tests/Hps.Broker.Tests/SubscriberRegistryTests.cs`: `SUBSCRIBE` 후 `REGISTER` 순서에서 기존 runtime 구독이 제거되는지 검증하는 회귀 테스트를 추가했다.
- `src/Hps.Broker/SubscriberRegistry.cs`: 새 target 을 stable identity 에 매핑하기 전, 같은 runtime target 의 기존 routing 구독을 제거한다.
- `docs/superpowers/specs/2026-06-22-stable-subscriber-identity-reconnect-policy-design.md`: late `REGISTER`는 기존 runtime 구독을 stable metadata 로 이관하지 않는다고 명시했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`: D076을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 이번 보강 단위와 다음 리뷰 대기 상태를 반영했다.

### 검증
- Red: focused `SubscriberRegistryTests`에서 late `REGISTER` 이후 pre-register runtime 구독이 남는 assertion failure 1개 확인.
- Green: focused `SubscriberRegistryTests` 10개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 215개 통과/실패 0.

## 2026-06-22 (Codex - Stable subscriber identity BrokerServer opt-in wiring)

### 작업 단위
- Stable subscriber identity 구현 계획 Task 5로 Server public options 와 host retention timer wiring 을 연결했다.

### 변경 내용
- `src/Hps.Server/BrokerServerOptions.cs`: stable identity enabled/retention timeout 속성,
  `CreateWithStableSubscriberIdentity(...)`, `WithStableSubscriberIdentity(...)`를 추가했다.
- `src/Hps.Server/BrokerServer.cs`: enabled options 일 때 shared `SubscriberRegistry`를 만들고 TCP/UDP handler 에 같은 registry 를 주입한다.
- `src/Hps.Server/BrokerServer.cs`: TCP 또는 UDP start 성공 후 stable identity retention timer 를 한 번만 생성하고,
  `StopAsync`에서 UDP lease sweep timer 와 함께 dispose 한다.
- `tests/Hps.Server.Tests/BrokerServerOptionsTests.cs`: 기본 disabled, retention timeout 검증, explicit values,
  UDP lease sweep 설정 보존을 검증했다.
- `tests/Hps.Server.Tests/BrokerServerTests.cs`: TCP handler registry wiring, expired disconnected identity sweep,
  retention timer dispose 를 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 5 완료와 stable identity 구현 계획 완료 후 리뷰 대기 상태를 반영했다.

### 검증
- Red: stable identity options/factory/timer wiring 부재로 focused Server/Options tests assertion failure 7개 확인.
- Green: focused stable Server/Options tests 7개 통과.
- Refactor: reflection bootstrap 테스트를 direct public API 호출로 정리한 뒤 focused stable Server/Options tests 7개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 214개 통과/실패 0.

## 2026-06-22 (Codex - Stable subscriber identity UDP handler wiring)

### 작업 단위
- Stable subscriber identity 구현 계획 Task 4로 UDP datagram handler 에 optional registry 경로를 연결했다.

### 변경 내용
- `src/Hps.Broker/UdpRemoteLeaseTracker.cs`: stable rebind 에 필요한 `RemoveRemote(...)`와 `MarkSubscribedTopics(...)`를 추가했다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: 기존 public/internal constructor 는 유지하고, registry 선택 주입 constructor 를 추가했다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: UDP `REGISTER`/`UNREGISTER` command 처리와 registered remote subscribe/unsubscribe 를 `SubscriberRegistry`와 lease tracker 로 연결했다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: same-id remote rebind 시 old remote lease/subscription 을 제거하고 rebound topic lease 를 새 remote 에 복구한다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: duplicate target different-id 는 UDP 정책대로 endpoint close 없이 datagram drop 으로 처리한다.
- `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`: remote rebind, duplicate registration drop, explicit unregister,
  endpoint close 후 reconnect topic restore 를 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 4 완료와 다음 Task 5 Server opt-in wiring 진입점을 반영했다.

### 검증
- Red: registry 주입 internal constructor 부재로 focused UDP handler tests assertion failure 4개 확인.
- Green/Refactor: focused UDP handler tests 12개 통과.

## 2026-06-22 (Codex - Stable subscriber identity TCP handler wiring)

### 작업 단위
- Stable subscriber identity 구현 계획 Task 3으로 TCP frame handler 에 optional registry 경로를 연결했다.

### 변경 내용
- `src/Hps.Broker/BrokerTcpFrameHandler.cs`: 기존 public constructor 는 유지하고, registry/time provider internal constructor 를 추가했다.
- `src/Hps.Broker/BrokerTcpFrameHandler.cs`: `REGISTER`/`UNREGISTER` command 처리와 registered target 의 subscribe/unsubscribe 를 `SubscriberRegistry`로 위임했다.
- `src/Hps.Broker/BrokerTcpFrameHandler.cs`: same-id reconnect 시 old TCP target 을 close 하고, duplicate target different-id 는 protocol error close 로 수렴한다.
- `src/Hps.Broker/BrokerTcpFrameHandler.cs`: close cleanup 은 registry 가 있으면 `RemoveTarget(..., now)`로, 없으면 기존 `UnsubscribeAll(connection)`으로 처리한다.
- `tests/Hps.Broker.Tests/BrokerTcpFrameHandlerTests.cs`: reconnect rebind, duplicate registration close, connection close retention,
  explicit unregister metadata 제거를 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 3 완료와 다음 Task 4 UDP handler wiring 진입점을 반영했다.

### 검증
- Red: registry 주입 internal constructor 부재로 focused TCP handler tests assertion failure 4개 확인.
- Green/Refactor: focused TCP handler tests 11개 통과.

## 2026-06-22 (Codex - Stable subscriber identity pure registry)

### 작업 단위
- Stable subscriber identity 구현 계획 Task 2로 Broker 내부 identity/registry pure model 을 구현했다.

### 변경 내용
- `src/Hps.Broker/SubscriberIdentity.cs`: non-empty/no-whitespace identity token validation 과 ordinal equality 를 추가했다.
- `src/Hps.Broker/SubscriberRegistrationResult.cs`: REGISTER 결과 enum 을 추가했다.
- `src/Hps.Broker/SubscriberRegistry.cs`: identity별 topic metadata, current target mapping, same-id rebind,
  same-target different-id conflict, disconnect retention, explicit unregister, disconnected sweep, UDP endpoint cleanup 을 구현했다.
- `tests/Hps.Broker.Tests/SubscriberIdentityTests.cs`, `tests/Hps.Broker.Tests/SubscriberRegistryTests.cs`:
  contract, validation, rebind, metadata retention, unregister, sweep, UDP endpoint cleanup 을 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 2 완료와 다음 Task 3 TCP handler wiring 진입점을 반영했다.

### 검증
- Red 1: 타입 부재 reflection contract assertion failure 2개 확인.
- Red 2: 스텁 추가 후 behavior assertion failure 10개 확인.
- Green/Refactor: focused broker identity/registry tests 15개 통과.

## 2026-06-22 (Codex - Stable subscriber identity protocol decode)

### 작업 단위
- Stable subscriber identity 구현 계획 Task 1로 protocol `REGISTER` / `UNREGISTER` command decode 를 구현했다.

### 변경 내용
- `src/Hps.Protocol/TcpCommandKind.cs`: `Register = 4`, `Unregister = 5` command kind 를 추가했다.
- `src/Hps.Protocol/TcpCommandDecoder.cs`: `REGISTER <subscriber-id>`와 `UNREGISTER <subscriber-id>`를 기존 token-only command 문법으로 decode 하도록 분기했다.
- `tests/Hps.Protocol.Tests/TcpCommandDecoderTests.cs`: command kind 계약, 정상 decode, malformed token 경계를 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 1 완료와 다음 Task 2 pure registry 진입점을 반영했다.

### 검증
- Red: `dotnet test tests\Hps.Protocol.Tests\Hps.Protocol.Tests.csproj --filter FullyQualifiedName~TcpCommandDecoderTests`에서
  enum 부재와 decoder 미지원으로 assertion failure 9개를 확인했다.
- Green/Refactor: 같은 focused protocol tests 24개 통과.

## 2026-06-22 (Codex - Stable subscriber identity implementation plan)

### 작업 단위
- D075 stable subscriber identity / reconnect rebinding 정책을 구현 가능한 Task 단위로 분해했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-22-stable-subscriber-identity.md`: protocol decode, pure registry, TCP handler,
  UDP handler, Server opt-in wiring 의 5개 작업 단위와 각 Red-Green-Refactor 검증/커밋 경계를 작성했다.
- `CURRENT_PLAN.md`: 다음 실행 지점을 구현 계획 리뷰로 갱신했다.
- `TODOS.md`: 구현 계획 작성 완료와 다음 Task 1 후보를 반영했다.

### 검증
- 계획 self-review: D075 spec coverage, placeholder, type consistency 를 확인했다.
- 기존 `TcpCommandDecoderTests`, `BrokerTcpFrameHandlerTests`, `BrokerUdpDatagramHandlerTests`, `BrokerServerTests` 구조를 기준으로 Task 경계를 맞췄다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 175개 통과/실패 0.

## 2026-06-22 (Codex - Stable subscriber identity policy)

### 작업 단위
- D058/D059 이후 deferred 상태였던 stable subscriber identity / reconnect rebinding 정책을 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-22-stable-subscriber-identity-reconnect-policy-design.md`: 기본 runtime target subscription 유지,
  opt-in `REGISTER <subscriber-id>` 기반 Broker registry, duplicate/rebind, disconnect retention, 테스트 순서를 정리했다.
- `DECISIONS.md`: D075를 추가하고 stable identity 를 후속 opt-in registry 로 구현한다는 기준을 active decision index 에 반영했다.
- `TODOS.md`: stable identity 설계 backlog 를 완료로 이동하고, 다음 current gate 를 설계 리뷰 대기로 갱신했다.
- `CURRENT_PLAN.md`: 현재 상태 요약, 최근 완료 단위, 다음 실행 지점, 검증 경로를 이번 설계 단위 기준으로 갱신했다.

### 검증
- 실제 `BrokerSubscriber`, `SubscriptionTable`, `BrokerTcpFrameHandler`, `BrokerUdpDatagramHandler`, `TcpCommandDecoder` 구조와 설계가 충돌하지 않는지 확인했다.
- 기존 `docs/superpowers/specs/2026-06-16-endpoint-identity-policy.md`와 D058/D059/D060 정책을 유지하는지 확인했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 175개 통과/실패 0.

## 2026-06-22 (Codex - BrokerServer UDP lease host timer wiring)

### 작업 단위
- D074 구현 두 번째 단위로 `BrokerServerOptions` enabled 설정을 실제 `BrokerServer` UDP 수명에 연결했다.

### 변경 내용
- `src/Hps.Broker/Properties/AssemblyInfo.cs`: `Hps.Server`가 내부 `BrokerUdpDatagramHandler` lease 생성자와 `UdpLeaseOptions`를 사용할 수 있도록 friend assembly 경계를 추가했다.
- `src/Hps.Server/BrokerServer.cs`: options 생성자를 추가하고 기본 생성자는 이 경로로 위임했다.
- `src/Hps.Server/BrokerServer.cs`: UDP start 성공 후 `TimeProvider.CreateTimer`로 sweep timer 를 만들고, timer callback 에서 `SweepExpiredUdpLeases(...)`를 호출한다.
- `src/Hps.Server/BrokerServer.cs`: `StopAsync`와 UDP start 실패 cleanup 에서 sweep timer 를 dispose 하도록 수명 경계를 맞췄다.
- `tests/Hps.Server.Tests/BrokerServerTests.cs`: enabled options 에서 timer 생성/만료 sweep, stop 시 timer dispose 를 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: host timer wiring 완료와 다음 리뷰 게이트를 반영했다.

### 검증
- Red: reflection 기반 `BrokerServerTests`가 options 생성자 부재로 `Assert.NotNull` 2개 실패.
- Green: focused `FullyQualifiedName~UdpLeaseSweepEnabled` tests 2개 통과.
- Refactor: 기본 생성자 위임과 direct public API 테스트로 정리한 뒤 focused tests 2개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 175개 통과/실패 0.

## 2026-06-22 (Codex - BrokerServerOptions)

### 작업 단위
- D074 구현 첫 단위로 `BrokerServerOptions` public 설정 타입을 추가했다.

### 변경 내용
- `src/Hps.Server/BrokerServerOptions.cs`: 기본 disabled options 와 UDP lease sweep 활성 options factory 를 추가했다.
- `tests/Hps.Server.Tests/BrokerServerOptionsTests.cs`: 기본 disabled, 0 이하 timeout/interval 거부, explicit 값과 `TimeProvider` 저장을 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 실행 지점을 실제 host timer wiring 으로 갱신했다.

### 검증
- Red: reflection 기반 `BrokerServerOptionsTests`가 타입 부재로 `Assert.NotNull` 3개 실패.
- Green: focused `BrokerServerOptionsTests` 3개 통과.
- Refactor: reflection 테스트를 direct public API 호출로 정리한 뒤 focused `BrokerServerOptionsTests` 3개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 173개 통과/실패 0.

## 2026-06-22 (Codex - BrokerServer UDP lease host timer design)

### 작업 단위
- UDP lease tracker/sweep core 이후 남은 `BrokerServer` host timer/public settings 설계를 작성했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-22-broker-server-udp-lease-host-timer-design.md`: `BrokerServerOptions`,
  기본 disabled 정책, explicit timeout/interval, `TimeProvider.CreateTimer`, `Hps.Broker` friend assembly 경계를 정리했다.
- `DECISIONS.md`: D074를 active decision index 에 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 실행 지점을 host timer 구현으로 갱신했다.

### 검증
- 설계 self-review: 기본값 미정 문제를 "활성화 시 explicit timeout/interval 요구"로 닫았고, Broker public lease options 를 늘리지 않는 방향으로 정리했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 170개 통과/실패 0.

## 2026-06-22 (Codex - UDP lease tracker handler wiring)

### 작업 단위
- UDP optional lease sweep 구현 계획의 Task 4를 수행했다.
- `BrokerUdpDatagramHandler`가 UDP command activity 를 `UdpRemoteLeaseTracker`로 위임하게 했다.

### 변경 내용
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: public constructor 는 disabled lease options 를 사용하는 기존 경로로 유지하고, internal constructor 에서 options/time provider 를 주입받아 tracker 를 생성한다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: SUBSCRIBE/UNSUBSCRIBE/PUBLISH/endpoint-close 처리를 tracker 로 위임하고 `SweepExpiredUdpLeases(DateTimeOffset)` 내부 entry point 를 추가했다.
- `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`: command 로 생성된 lease 가 sweep 으로 제거되는지, PUBLISH activity 가 기존 lease 를 갱신해 sweep 에서 보존하는지 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 1~4 core 완료와 host timer/public settings 후속 범위를 갱신했다.

### 검증
- Red: reflection 기반 handler wiring tests 가 internal constructor 부재로 `Assert.NotNull` 2개 실패.
- Green: focused `BrokerUdpDatagramHandlerTests` 8개 통과.
- Refactor: reflection helper 를 direct internal API 호출로 정리한 뒤 focused `BrokerUdpDatagramHandlerTests` 8개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 170개 통과/실패 0.

## 2026-06-22 (Codex - UDP remote lease pure sweep)

### 작업 단위
- UDP optional lease sweep 구현 계획의 Task 3을 수행했다.
- `UdpRemoteLeaseTracker.SweepExpired(DateTimeOffset)`로 만료된 UDP remote lease 를 routing table 에서 정리한다.

### 변경 내용
- `src/Hps.Broker/UdpRemoteLeaseTracker.cs`: idle timeout 을 초과한 `(IUdpEndpoint, EndPoint)` lease 를 찾아 `SubscriptionTable.UnsubscribeAll(IUdpEndpoint, EndPoint)`로 제거하는 순수 sweep 메서드를 추가했다.
- `tests/Hps.Broker.Tests/UdpRemoteLeaseTrackerTests.cs`: 만료 remote 제거, publish activity 갱신 보존, disabled options no-op 을 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 완료 단위와 다음 Task 4 handler wiring 진입점을 갱신했다.

### 검증
- Red: reflection 기반 sweep tests 가 `SweepExpired` 메서드 부재로 `Assert.NotNull` 3개 실패.
- Green: focused `UdpRemoteLeaseTrackerTests` 8개 통과.
- Refactor: reflection helper 를 direct internal API 호출로 정리한 뒤 focused `UdpRemoteLeaseTrackerTests` 8개 통과.
- 계획 보정: plan 예시의 survivor remote 는 expired remote 와 같은 시점에 구독하면 함께 만료되므로, survivor를 늦게 구독하도록 테스트 setup 을 보정했다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 168개 통과/실패 0.

## 2026-06-22 (Codex - UDP remote lease tracker activity)

### 작업 단위
- UDP optional lease sweep 구현 계획의 Task 2를 수행했다.
- 내부 `UdpRemoteLeaseTracker`로 UDP remote subscription activity 와 endpoint cleanup lease state 를 추적한다.

### 변경 내용
- `src/Hps.Broker/UdpRemoteLeaseTracker.cs`: `(IUdpEndpoint, EndPoint)` key 기반 lease table 을 추가하고 subscribe/unsubscribe/publish activity, endpoint close cleanup 을 처리한다.
- `tests/Hps.Broker.Tests/UdpRemoteLeaseTrackerTests.cs`: disabled options 보존, enabled remote당 lease 1개, 마지막 topic unsubscribe 시 lease 제거, publisher-only remote 미생성, endpoint close cleanup 을 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 완료 단위와 다음 Task 3 순수 sweep 진입점을 갱신했다.

### 검증
- Red: reflection 기반 `UdpRemoteLeaseTrackerTests`가 타입 부재로 `Assert.NotNull` 5개 실패. 계획서의 compile-failure Red는 AGENTS의 assertion-failure Red 규칙에 맞춰 보정했다.
- Green: focused `UdpRemoteLeaseTrackerTests` 5개 통과.
- Refactor: reflection 테스트를 direct internal API 호출로 정리한 뒤 focused `UdpRemoteLeaseTrackerTests` 5개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 165개 통과/실패 0.

## 2026-06-22 (Codex - UDP lease options)

### 작업 단위
- UDP optional lease sweep 구현 계획의 Task 1을 수행했다.
- 내부 `UdpLeaseOptions` 타입과 테스트 assembly internal 접근 경계를 추가했다.

### 변경 내용
- `src/Hps.Broker/UdpLeaseOptions.cs`: 기본 비활성 options 와 양수 idle timeout/sweep interval 을 받는 활성 options factory 를 추가했다.
- `src/Hps.Broker/Properties/AssemblyInfo.cs`: `Hps.Broker.Tests`에 internal 접근을 허용했다.
- `tests/Hps.Broker.Tests/UdpLeaseOptionsTests.cs`: 기본 비활성, 0 이하 interval 거부, 양수 interval 저장을 검증했다.
- `docs/superpowers/plans/2026-06-22-udp-optional-lease-sweep.md`: `Enabled` property 와 C# 멤버 이름이 충돌하는 factory 이름을 `CreateEnabled(...)`로 정정했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 완료 단위와 다음 Task 2 진입점을 갱신했다.

### 검증
- Red: reflection 기반 `UdpLeaseOptionsTests`가 타입 부재로 `Assert.NotNull` 3개 실패.
- Green: focused `UdpLeaseOptionsTests` 3개 통과.
- Refactor: reflection 테스트를 direct internal API 호출로 정리한 뒤 focused `UdpLeaseOptionsTests` 3개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 160개 통과/실패 0.

## 2026-06-22 (Codex - UDP optional lease sweep implementation plan)

### 작업 단위
- D073 설계를 구현 가능한 작은 Task 로 분해했다.
- 코드 변경 없이 내부 options, lease tracker activity, 순수 sweep, handler wiring 의 커밋 경계를 정했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-22-udp-optional-lease-sweep.md`: 각 Task 의 touched files, produced interfaces, Red-Green 테스트, 검증/커밋 명령을 작성했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 실행 지점을 구현 계획 리뷰와 Task 1 시작으로 갱신했다.

### 검증
- 실제 `BrokerUdpDatagramHandler`, `SubscriptionTable`, `BrokerServer`, `BrokerSubscriber` 구조와 계획의 시그니처가 맞는지 확인했다.
- 계획 self-review 로 D073 coverage, placeholder, type consistency 를 확인했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 157개 통과/실패 0.

## 2026-06-22 (Codex - UDP optional lease tracker / sweep owner design)

### 작업 단위
- UDP idle expiry 의 lease tracker/sweep owner, key, 설정 표면, clock/timer 추상화, sweep 의 `UnsubscribeAll` 사용 방식을 설계했다.
- 코드 변경 없이 owner 계층(Broker 소유·Server 트리거), 설정(내부 options·기본 비활성), 시간 소스(`TimeProvider`)를 확정했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-22-udp-optional-lease-sweep-design.md`: lease 모델, options 타입, sweep 정책, 다음 최소 구현 단위, 범위 밖을 정리했다.
- `DECISIONS.md`: D073을 active decision index 에 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 완료 단위와 다음 구현 후보(UDP lease tracker/sweep 구현)를 갱신하고, 해결된 결정과 남은 open question 을 분리했다.

### 검증
- 실제 `BrokerUdpDatagramHandler`, `SubscriptionTable`, `BrokerServer`, `BrokerSubscriber` 구조와 충돌하지 않음, D061/D067/D068/D072 정합성을 확인했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 157개 통과/실패 0.

## 2026-06-22 (Codex - UDP remote-wide unsubscribe primitive)

### 작업 단위
- D072 idle sweep 의 선행 API로 `SubscriptionTable.UnsubscribeAll(IUdpEndpoint, EndPoint)`를 구현했다.
- timer, idle timeout 설정, BrokerServer public API 는 추가하지 않았다.

### 변경 내용
- `SubscriptionTable`: 특정 UDP local endpoint/remote endpoint 조합만 모든 topic 에서 제거하는 overload 를 추가했다.
- `BrokerRoutingTests`: 같은 endpoint 의 다른 remote, 다른 endpoint 의 같은 remote, TCP subscriber 가 보존되는지 검증하는 Red-Green 테스트를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 실행 지점과 deferred 항목을 갱신했다.

### 검증
- Red: focused test 가 API 부재로 `Assert.NotNull` 실패.
- Green/Refactor: focused test 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 157개 통과/실패 0.

## 2026-06-19 (Codex - UDP stale remote idle expiry design)

### 작업 단위
- UDP remote subscription 이 `UNSUBSCRIBE` 없이 stale 로 남는 경우의 cleanup owner 와 정책을 설계했다.
- Transport 계층에 idle 판단을 넣지 않고 Broker/Server 소유의 선택적 lease cleanup 으로 분리했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-19-udp-stale-remote-idle-expiry-design.md`: UDP stale remote cleanup key, activity 갱신 규칙, sweep 범위, 다음 최소 구현 단위를 정리했다.
- `DECISIONS.md`: D072를 active decision index 에 추가했다.
- `TODOS.md`: 기존 설계 backlog 를 완료로 이동하고 다음 구현 후보를 `UDP remote-wide unsubscribe primitive` 로 좁혔다.
- `CURRENT_PLAN.md`: 다음 리뷰 게이트를 UDP stale remote idle expiry 설계로 갱신했다.

### 검증
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 156개 통과/실패 0.

## 2026-06-18 (Codex - baseline history index)

### 작업 단위
- 반복 baseline session 을 한곳에서 찾기 위한 전역 history index 를 추가했다.
- 코드, benchmark schema, CI workflow 는 변경하지 않았다.

### 변경 내용
- `docs/benchmarks/baselines/index.md`: 2026-06-18 root/session-02/session-03 summary artifact 와 hard/warning 상태를 연결했다.
- `docs/superpowers/specs/2026-06-18-baseline-report-history-warning-policy-design.md`: 상태를 Accepted 로 갱신했다.
- `DECISIONS.md`: D071을 active decision index 에 추가했다.
- `CURRENT_PLAN.md`: 다음 리뷰 게이트를 baseline history index 로 갱신했다.
- `TODOS.md`: baseline history index P1 항목을 완료로 이동했다.

### 검증
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 156개 통과/실패 0.

## 2026-06-18 (Codex - baseline report history/warning policy design)

### 작업 단위
- baseline summary JSON/Markdown artifact 이후의 report history 단위와 warning 승격 정책을 설계했다.
- CI provider workflow, warning-as-failure 구현, latency hard gate 는 이번 범위에서 제외하고 provider-independent 정책만 정리했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-18-baseline-report-history-warning-policy-design.md`:
  baseline session directory 를 history 단위로 보고, raw JSON/summary JSON/summary Markdown 역할을 분리했다.
- `TODOS.md`: 기존 P1 설계 항목을 완료로 이동하고, 승인 이후의 다음 후보로 baseline history index 작업을 남겼다.
- `CURRENT_PLAN.md`: 다음 게이트를 새 설계 문서 리뷰로 갱신했다.

### 검증
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 156개 통과/실패 0.

## 2026-06-18 (Codex - state document compaction)

### 작업 단위
- root 상태 문서가 빠른 진입점 역할을 잃을 정도로 커져 `docs/agent-state/` archive 를 만들고 문서를 축약했다.
- 원문은 `docs/agent-state/snapshots/2026-06-18-pre-compaction/`와 domain archive 에 보존했다.

### 변경 내용
- `CURRENT_PLAN.md`: 현재 목표, 최신 완료 단위, 다음 실행 지점, 검증 경로만 남겼다.
- `TODOS.md`: current TODO, handoff-ready deferred backlog, 최근 완료 항목만 남겼다.
- `CHANGELOG_AGENT.md`: 최근 작업 단위 중심으로 축약하고 전체 원문 archive 링크를 추가했다.
- `DECISIONS.md`: active decision index 로 축약하고 상세 원문 archive 링크를 추가했다.

### 검증
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 156개 통과/실패 0.

## 2026-06-18 (Codex - baseline summary markdown artifacts)

### 작업 단위
- 이미 구현된 `--summarize-baseline <input-dir> --summary <output-json> --summary-md <output-md>` command 로
  2026-06-18 baseline root, `session-02`, `session-03` directory 의 `summary.md` 보조 artifact 를 생성했다.
- 코드 변경 없이 benchmark artifact 와 상태 문서만 갱신했다.

### 검증
- 세 directory 에 대해 `--summary-md` 포함 summary command 를 실행해 모두 exit-code 0,
  `source-report-count=6`, `hard-passed=true`, `warning-count=0`을 확인했다.
- 생성된 세 `summary.md`가 `# Baseline Summary`, load/open-loop row, `Warnings`, `- 없음`을 포함하는지 확인했다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 156개 통과/실패 0.
- `git diff --check` 통과.

## 2026-06-18 (Codex - baseline summary markdown cli)

### 작업 단위
- `--summarize-baseline <input-dir> --summary <output-json>` command 에 선택 옵션
  `--summary-md <output-md>`를 연결했다.
- JSON summary 는 계속 필수 canonical artifact 로 유지하고, Markdown 은 같은 `BaselineSummary`에서 파생되는
  사람 리뷰용 보조 artifact 로만 생성한다.

### 검증
- parser Red-Green, CLI Red-Green 을 수행했다.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-build --no-restore` 통과, 20개 통과/실패 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 156개 통과/실패 0.
- `git diff --check` 통과.

## 2026-06-18 (Codex - baseline summary markdown writer)

### 작업 단위
- `BaselineSummary`를 사람이 빠르게 리뷰할 Markdown 표로 쓰는 writer 를 추가했다.

### 검증
- writer bootstrap Red-Green 과 Markdown 내용 Red-Green 을 수행했다.
- focused writer tests 통과.

## 2026-06-18 (Codex - baseline summary artifacts)

### 작업 단위
- 이미 구현된 `--summarize-baseline <input-dir> --summary <output-json>` command 로
  2026-06-18 baseline root, `session-02`, `session-03` directory 의 canonical `summary.json`을 생성했다.

### 검증
- 세 directory 에 대해 summary command 를 실행해 모두 exit-code 0,
  `source-report-count=6`, `hard-passed=true`, `warning-count=0`을 확인했다.
- 생성된 세 `summary.json`을 `ConvertFrom-Json`으로 읽어 summary schema 와 run count 를 확인했다.

## 2026-06-18 (Codex - baseline summary artifact implementation)

### 작업 단위
- baseline summary parser, generator, reader/writer, Program wiring 을 4개 작은 단위로 구현했다.
- D070에 따라 latency hard gate 는 추가하지 않고 summary JSON + non-failing soft warning 을 먼저 만들었다.

### 검증
- 각 Task 별 Red-Green 을 수행했다.
- 마지막 Program wiring 후 root/session-02/session-03 CLI smoke 를 모두 통과했다.
