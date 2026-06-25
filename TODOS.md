# TODOS.md

## Archive

이 파일은 현재 실행 가능한 항목과 소수의 deferred backlog 만 유지한다. 긴 완료 이력은 archive 를 본다.

- 완료 이력 원문: `docs/agent-state/backlog/completed-history-2026-06-18.md`
- 전체 pre-compaction snapshot: `docs/agent-state/snapshots/2026-06-18-pre-compaction/`

## Current TODOs

- [ ] RIO TCP pump 선행 하위 단위로 receive/send/dequeue native delegate boundary 를 구현한다.
  - 목적: 실제 TCP pump 전에 `RIOReceive`/`RIOSend` posting 과 `RIODequeueCompletion` 결과 해석을
    호출 가능한 internal operation 으로 좁혀 검증한다.
  - 범위: `src/Hps.Transport.Rio/RioNative.cs`,
    `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`, root 상태 문서.
  - 현재 판단: Task 5.6에서 buffer registration, Task 5.7에서 CQ, Task 5.8에서 registered I/O socket + RQ 생성은
    실제 native 호출로 검증됐다.
  - 다음 자연스러운 step: RIO_BUF/RIORESULT marshalling 과 receive/send/dequeue delegate 를 Red-Green으로 고정한다.
  - 검증: focused RIO tests, solution build/test, `git diff --check`.

## Deferred Backlog

- [ ] `P3_NICE` 실제 host/metrics surface 가 생기면 server-level diagnostics model 을 설계한다.
  - 무엇이 남았는지: D068로 `BrokerServer` 단순 pass-through diagnostics API 는 v1에 추가하지 않기로 했다.
  - 왜 defer 되었는지: 현재 서버는 단일 injected `ITransport` 를 감싼 얇은 host 이며, diagnostics 소비자는 테스트/benchmark 중심이다.
  - objective: 실제 host/운영 API가 구체화된 뒤 server-level diagnostics model 이 필요한지 결정한다.
  - relevant context: D041, D042, D056, D062, D066, D068, `docs/superpowers/specs/2026-06-18-server-diagnostics-surface-design.md`.
  - 관련 파일/범위: `src/Hps.Server/`, `src/Hps.Transport/`, host/sample 코드, 관련 tests.
  - next step: metrics/exporter 또는 server-only consumer 요구가 나오면 별도 설계로 승격한다.

## Completed

최근 완료 항목만 유지한다. 전체 완료 이력은 `docs/agent-state/backlog/completed-history-2026-06-18.md`를 본다.

- [x] CI baseline adoption 이후 Phase 4 다음 후보를 재평가했다.
  - 범위: `docs/superpowers/specs/2026-06-25-after-ci-baseline-adoption-reassessment-design.md`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: `ci-windows-x64-01/2026-06-25/session-01`은 hard-passed true, warning-count 0,
    comparison-compatible true 이지만 date root 1개/session 1개뿐이므로 latency hard gate 또는
    warning-as-failure 로 승격하지 않는다(D096).
  - 비고: CI runner evidence 는 future push-triggered run 이 더 쌓이면 D095 checklist 로 수동 채택 여부를 다시 판단한다.
    다음 실행 가능한 큰 흐름은 Phase 5 Windows RIO backend 설계다.
  - 검증: CI runner root history, session summary, baseline index, D082/D090/D095를 대조했다.

- [x] Phase 5 Windows RIO backend boundary 를 설계했다.
  - 범위: `docs/superpowers/specs/2026-06-25-windows-rio-backend-boundary-design.md`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: RIO backend 는 TCP-first 로 진행하되, 첫 구현 task 를 project skeleton,
    Windows capability probe, native function table wrapper 로 분리했다(D097).
  - 비고: 기본 `TransportFactory.CreateDefault()`는 SAEA를 유지하고, RIO는 명시 opt-in/test path 로 먼저 검증한다.
    UDP RIO, batching, automatic default backend selection 은 후속으로 둔다.
  - 검증: current transport 구조, 빈 RIO project 상태, Microsoft RIO 문서를 대조했다.

- [x] Phase 5 Windows RIO backend 구현 계획을 작성했다.
  - 범위: `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`, root 상태 문서.
  - 결과: D097 설계를 project skeleton/capability probe, native function table loader,
    registered buffer owner, TCP queue owner, TCP opt-in guard, TCP pump/contract test reuse 의 6개 task 로 나눴다.
  - 비고: Task 1 Red는 production type 부재를 reflection assertion failure 로 검증하도록 보정했다.
  - 검증: plan self-review, placeholder scan, current transport 구조 대조.

- [x] RIO Task 1 project skeleton 과 capability probe 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/`, `tests/Hps.Transport.Rio.Tests/`, `HighPerformanceSocket.slnx`, root 상태 문서.
  - 결과: `RioCapabilityStatus`, `RioCapabilityProbe.GetStatus()`, `RioTransport` skeleton 을 추가했다.
    non-Windows 는 `UnsupportedOperatingSystem`, Windows 는 native loader 구현 전까지 `Unavailable`로 보고한다.
  - 비고: 기본 `TransportFactory.CreateDefault()`는 계속 `SaeaTransport`를 반환한다.
  - 검증: Red assertion failure 1개 확인(`Assert.NotNull() Failure: Value is null`),
    focused RIO tests 4개 통과, solution build 경고 0/오류 0.

- [x] RIO Task 2 native function table loader 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioNative.cs`, `src/Hps.Transport.Rio/RioCapabilityProbe.cs`,
    `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`, root 상태 문서.
  - 결과: `RioNative.TryLoadFunctionTable(out RioNative?)` 경계를 추가하고,
    `RioCapabilityProbe.GetStatus()`가 해당 경계를 통해 `Available` 또는 `Unavailable`을 반환하도록 연결했다.
  - 비고: 실제 `WSAIoctl`/`WSAID_MULTIPLE_RIO` marshalling 은 아직 넣지 않고, 예외 없는 fallback 경계를 먼저 고정했다.
  - 검증: Red assertion failure 1개 확인(`Assert.NotNull() Failure: Value is null`),
    focused RIO tests 6개 통과, solution build 경고 0/오류 0.

- [x] RIO Task 3 registered buffer owner 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioRegisteredBufferPool.cs`,
    `tests/Hps.Transport.Rio.Tests/RioRegisteredBufferPoolTests.cs`,
    `src/Hps.Transport.Rio/Properties/AssemblyInfo.cs`, root 상태 문서.
  - 결과: outstanding request 완료 전에는 block 을 반환하지 않고, 중복 completion 에서는 release 를 한 번만 수행한다.
  - 비고: Red 용 reflection 테스트는 Green 이후 `InternalsVisibleTo` 기반 direct internal API 테스트로 정리했다.
  - 검증: Red assertion failure 1개 확인(`Assert.NotNull() Failure: Value is null`),
    focused RIO tests 7개 통과, solution build 경고 0/오류 0.

- [x] RIO Task 4 TCP queue owners 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioCompletionQueue.cs`, `src/Hps.Transport.Rio/RioRequestQueue.cs`,
    `tests/Hps.Transport.Rio.Tests/RioQueueOwnerTests.cs`, root 상태 문서.
  - 결과: receive/send quota reservation 을 독립적으로 제한하고 completion 후 quota 를 다시 열 수 있게 했다.
  - 비고: Red 용 reflection 테스트는 Green 이후 `InternalsVisibleTo` 기반 direct internal API 테스트로 정리했다.
  - 검증: Red assertion failure 2개 확인(`Assert.NotNull() Failure: Value is null`),
    focused RIO tests 9개 통과, solution build 경고 0/오류 0.

- [x] RIO Task 5 TCP opt-in transport guard 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioTransport.cs`,
    `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`, root 상태 문서.
  - 결과: RIO unavailable 환경에서 `ListenTcpAsync`/`ConnectTcpAsync`가 actual TCP wiring 미구현 메시지보다 먼저
    Windows RIO function table 사용 불가를 명시하는 `NotSupportedException`으로 실패한다.
  - 비고: 기본 `TransportFactory.CreateDefault()`/SAEA 경로와 실제 RIO socket pump 는 건드리지 않았다.
  - 검증: Red assertion failure 1개 확인(`Sub-string not found`),
    focused RIO tests 10개 통과, solution build 경고 0/오류 0, solution tests 279개 통과.

- [x] RIO Task 5.5 native function table loader hardening 을 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioNative.cs`,
    `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`,
    `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`, decision/root 상태 문서.
  - 결과: `RioNative`가 Windows에서 `WSAIoctl(SIO_GET_MULTIPLE_EXTENSION_FUNCTION_POINTER, WSAID_MULTIPLE_RIO)`로
    실제 `RIO_EXTENSION_FUNCTION_TABLE`을 얻고 필수 pointer 를 검증한다.
  - 비고: D098로 Task 6 전에 실제 native loader 를 완료해야 한다는 순서 보정을 기록했다.
  - 검증: Red assertion failure 1개 확인(`Expected: Available`, `Actual: Unavailable`),
    focused RIO tests 11개 통과, solution build 경고 0/오류 0, solution tests 280개 통과.

- [x] RIO Task 5.6 native buffer registration delegate 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioNative.cs`,
    `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`,
    `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`, root 상태 문서.
  - 결과: loaded RIO function table 에서 `RIORegisterBuffer`/`RIODeregisterBuffer`를 delegate 로 marshal 하고,
    `RioNative.RegisterBuffer(...)`/`DeregisterBuffer(...)` internal operation 으로 노출했다.
  - 비고: Red는 reflection assertion failure 로 시작했고, Green 이후 direct internal API 테스트로 정리했다.
  - 검증: Red assertion failure 1개 확인(`Assert.NotNull() Failure: Value is null`),
    focused RIO tests 12개 통과, solution build 경고 0/오류 0, solution tests 281개 통과.

- [x] RIO Task 5.7 native completion queue delegate 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioNative.cs`,
    `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`,
    `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`, root 상태 문서.
  - 결과: loaded RIO function table 에서 `RIOCreateCompletionQueue`/`RIOCloseCompletionQueue`를 delegate 로 marshal 하고,
    `RioNative.CreateCompletionQueue(...)`/`CloseCompletionQueue(...)` internal operation 으로 노출했다.
  - 비고: 초기 pump 는 null notification completion 기반 polling/dequeue 모델로 검증한다.
  - 검증: Red assertion failure 1개 확인(`Assert.NotNull() Failure: Value is null`),
    focused RIO tests 13개 통과, solution build 경고 0/오류 0, solution tests 282개 통과.

- [x] RIO Task 5.8 native request queue delegate 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioNative.cs`,
    `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`,
    `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`, root 상태 문서.
  - 결과: `WSASocketW` + `WSA_FLAG_OVERLAPPED | WSA_FLAG_REGISTERED_IO` 기반 TCP socket factory 와
    `RIOCreateRequestQueue` delegate operation 을 추가했다.
  - 비고: 일반 .NET `Socket`으로 RQ 생성 시 null handle 이 반환되어 registered I/O socket 생성 경계가 필요함을 확인했다.
  - 검증: Red assertion failure 1개 확인(`Assert.NotNull() Failure: Value is null`),
    Green 중 일반 socket RQ null handle 실패를 확인한 뒤 registered I/O socket 으로 보정,
    focused RIO tests 14개 통과, solution build 경고 0/오류 0, solution tests 283개 통과.

- [x] CI push-triggered artifact `28145025444`를 repository baseline 으로 수동 채택했다.
  - 범위: `docs/benchmarks/baselines/runners/ci-windows-x64-01/2026-06-25/session-01/`,
    date-level history, runner root history, `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: artifact zip/root directory 는 커밋하지 않고 raw report 6개만 복사했다.
    summary/history 는 repository 경로 기준으로 재생성했다.
  - 비고: CI runner root history 는 session-count 1, hard-passed true, warning-count 0,
    comparison-compatible true 다. CI runner first reference envelope 는 load p99 max 275.3 us,
    open-loop p99 max 322.9 us, TCP HWM max 2 다.
  - 검증: D095 checklist, summary/history 재생성, absolute path scan 결과 없음,
    `git diff --check` exit 0, benchmark tests 67개 통과, solution build 경고 0/오류 0,
    solution tests 269개 통과.

- [x] CI artifact adoption 절차를 설계했다.
  - 범위: `docs/superpowers/specs/2026-06-25-ci-artifact-adoption-policy-design.md`, D095, root 상태 문서.
  - 결과: CI artifact 는 자동 채택하지 않고, checklist 통과 artifact 의 raw report 6개만 repository baseline 구조로 수동 채택한다.
  - 비고: warning-count > 0 artifact 는 repository reference baseline 으로 채택하지 않는다.
    첫 채택 후보는 D094 push trigger 로 생성된 run `28145025444`다.
  - 검증: D090/D093/D094, `docs/benchmarks/baselines/index.md`, downloaded artifact 구조를 대조했다.

- [x] D094 trigger policy push 후 자동 CI artifact run 을 검증했다.
  - 범위: GitHub Actions run `28145025444`, artifact
    `benchmark-artifacts-ci-windows-x64-01-2026-06-25-github-28145025444-1`, root 상태 문서.
  - 결과: `push` event 로 `Benchmark Artifacts` run 이 자동 생성됐고 성공했다.
  - 비고: 로그에서 `actions/checkout@v7`, `actions/setup-dotnet@v5.3.0`, `actions/upload-artifact@v7.0.1` 다운로드/실행을 확인했다.
    `deprecation`, `Node.js 20`, `node20`, 이전 `actions/*@v4` 문자열 검색 결과는 없었다.
    artifact 는 raw report 6개, `summary.json`, `summary.md`, `history.json`, `history.md` 총 10개 파일을 포함한다.
    `summary.json`은 `source-report-count=6`, `hard-passed=true`, `warning-count=0`,
    `comparison-compatible=true`, `unknown-runner-count=0`이다.
    `history.json`은 `session-count=1`, `hard-passed=true`, `warning-count=0`, `comparison-compatible=true`다.
  - 검증: `gh run list`, `gh run watch --exit-status`, `gh run view --log`, `gh run download`로
    push-triggered run 성공과 artifact 내용을 확인했다.

- [x] CI artifact trigger policy 를 설계하고 workflow 에 반영했다.
  - 범위: `.github/workflows/benchmark-artifacts.yml`,
    `docs/superpowers/specs/2026-06-25-ci-artifact-trigger-policy-design.md`, D094, root 상태 문서.
  - 결과: `workflow_dispatch`는 유지하고, `push` to `master` 중 code/benchmark/build 관련 path 변경에만 자동 실행하도록 했다.
  - 비고: `pull_request`와 `schedule`은 아직 추가하지 않는다. docs-only 변경은 benchmark artifact 를 만들지 않는다.
  - 검증: workflow marker scan, trigger out-of-scope scan, `git diff --check`로 확인한다.

- [x] CI artifact-only manual run 2회 결과 이후 Phase 4 다음 후보를 재평가했다.
  - 범위: run `28143728630`, run `28144480160`, D090/D091/D092, baseline index, root 상태 문서.
  - 결과: latency gate, warning-as-failure, docs baseline 자동 채택, push/PR 자동 trigger 는 승격하지 않는다(D093).
  - 비고: 두 run 모두 성공했지만 같은 날짜의 GitHub-hosted Windows evidence 이며, 첫 run 은 warning-count 1,
    두 번째 run 은 warning-count 0이었다. 이 상태는 CI runner scheduling noise 가능성을 보여주므로
    gate 승격에는 부족하다.
  - 다음: CI artifact trigger policy 를 설계한다.
  - 검증: run log/artifact, D090/D091/D092, `docs/benchmarks/baselines/index.md`, current backlog 를 대조했다.

- [x] Node 24 action 갱신 후 CI artifact-only workflow manual run 을 재검증했다.
  - 범위: GitHub Actions run `28144480160`, artifact
    `benchmark-artifacts-ci-windows-x64-01-2026-06-25-github-28144480160-1`, root 상태 문서.
  - 결과: workflow 는 성공했다. restore/build/test, `baseline-suite`, `summary`, `history`, artifact upload 단계가 모두 통과했다.
  - 비고: 로그에서 `actions/checkout@v7`, `actions/setup-dotnet@v5.3.0`, `actions/upload-artifact@v7.0.1` 다운로드/실행을 확인했다.
    `deprecation`, `Node.js 20`, `node20`, 이전 `actions/*@v4` 문자열 검색 결과는 없었다.
    artifact 는 raw report 6개, `summary.json`, `summary.md`, `history.json`, `history.md` 총 10개 파일을 포함한다.
    `summary.json`은 `source-report-count=6`, `hard-passed=true`, `warning-count=0`,
    `comparison-compatible=true`, `unknown-runner-count=0`이다.
    `history.json`은 `session-count=1`, `hard-passed=true`, `warning-count=0`, `comparison-compatible=true`다.
  - 검증: `gh workflow run`, `gh run watch --exit-status`, `gh run view --log`, `gh run download`로
    run 성공과 artifact 내용을 확인했다.

- [x] GitHub Actions Node 20 deprecation annotation 대응을 처리했다.
  - 범위: `.github/workflows/benchmark-artifacts.yml`, D092 decision, CI workflow plan/policy 문서, root 상태 문서.
  - 결과: 첫 manual run `28143728630`에서 확인된 `actions/*@v4` Node.js 20 annotation 에 대응해
    `actions/checkout@v7`, `actions/setup-dotnet@v5.3.0`, `actions/upload-artifact@v7.0.1`로 갱신했다.
  - 비고: 2026-06-25 공식 release/action metadata 확인 기준 세 action version 은 `runs.using: node24`를 명시한다.
    benchmark command, artifact path, D090 report-only warning policy 는 바꾸지 않았다.
  - 검증: workflow static marker scan, `git diff --check` exit 0, solution tests 269개 통과,
    solution build 단독 재실행 경고 0/오류 0.

- [x] CI artifact-only workflow 첫 manual run 결과를 확인했다.
  - 범위: GitHub Actions run `28143728630`, artifact
    `benchmark-artifacts-ci-windows-x64-01-2026-06-25-github-28143728630-1`, root 상태 문서.
  - 결과: workflow 는 성공했다. restore/build/test, `baseline-suite`, `summary`, `history`, artifact upload 단계가 모두 통과했다.
  - 비고: artifact 는 raw report 6개, `summary.json`, `summary.md`, `history.json`, `history.md` 총 10개 파일을 포함한다.
    `summary.json`은 `source-report-count=6`, `hard-passed=true`, `warning-count=1`,
    `comparison-compatible=true`, `unknown-runner-count=0`이다.
    `history.json`은 `session-count=1`, `hard-passed=true`, `warning-count=1`, `comparison-compatible=true`다.
    warning 은 `open-loop-01.json`의 `p99-growth-ratio-high`이며 D090 기준 report-only 다.
  - 검증: `gh workflow run`, `gh run watch --exit-status`, `gh run download`로 run 성공과 artifact 내용을 확인했다.
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] CI workflow benchmark command sequence 를 local smoke 로 검증하고 no-restore 로 보정했다.
  - 범위: `.github/workflows/benchmark-artifacts.yml`,
    `docs/superpowers/plans/2026-06-25-ci-artifact-only-workflow-skeleton.md`, root 상태 문서.
  - 결과: workflow 의 benchmark CLI 세 단계에 모두 `--no-build --no-restore`를 명시했다.
  - 비고: 최초 full smoke 는 workflow command sequence 로 `--runs 3`을 실행해 raw report 6개,
    `summary.json`/`summary.md`, `history.json`/`history.md` 생성을 확인했다. 이때 첫 benchmark `dotnet run`이
    restore를 다시 시도하며 `NU1900` 경고를 냈으므로, 이미 완료된 restore/build/test 를 재사용하도록 no-restore 형태로 보정했다.
  - 검증: 보정 후 `--runs 1` local smoke 에서 raw report 2개, summary/history artifact 생성,
    hard-passed true, warning-count 0을 확인했다. local smoke 후 sandbox NuGet cache 경로로 바뀐 restore asset 은
    `dotnet restore HighPerformanceSocket.slnx`로 복구했다. `git diff --check` exit 0,
    solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] CI artifact-only workflow skeleton 을 구현했다.
  - 범위: `.github/workflows/benchmark-artifacts.yml`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 결과: `workflow_dispatch` 전용 GitHub Actions workflow 를 추가했다.
    job env 는 `HPS_BENCHMARK_RUNNER_ID=ci-windows-x64-01`, `HPS_BENCHMARK_RUNNER_KIND=ci`로 고정한다.
  - 비고: workflow 는 restore/build/test 이후 `baseline-suite`, `summary`, `history`를 실행하고
    date root 를 `actions/upload-artifact@v7.0.1`로 업로드한다. 자동 push/PR trigger 와 warning/latency failure logic 은 넣지 않았다.
  - 검증: workflow static marker scan 과 lightweight policy check 를 통과했다.
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] CI artifact-only workflow skeleton 구현 계획을 작성했다.
  - 범위: `docs/superpowers/plans/2026-06-25-ci-artifact-only-workflow-skeleton.md`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: workflow trigger 는 `workflow_dispatch` 전용으로 시작하고,
    `HPS_BENCHMARK_RUNNER_ID=ci-windows-x64-01`, `HPS_BENCHMARK_RUNNER_KIND=ci`를 job env 로 둔다.
  - 비고: 현재 `BaselineHistoryReader`가 `session-NN`만 history session 으로 읽기 때문에,
    GitHub run id 는 upload artifact 이름에만 넣고 내부 디렉터리는 `<yyyy-mm-dd>/session-01/`로 유지한다(D091).
  - 검증: D090 spec, benchmark CLI, `BaselineHistoryReader`, `.github/workflows` 부재를 대조했다.
    placeholder scan 신규 미정 항목 없음, `git diff --check` exit 0, solution build 경고 0/오류 0,
    solution tests 269개 통과.

- [x] CI artifact-only benchmark 정책을 설계했다.
  - 범위: `docs/superpowers/specs/2026-06-25-ci-artifact-only-benchmark-policy-design.md`,
    `docs/benchmarks/baselines/index.md`, `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: CI runner id 는 `ci-windows-x64-01`, runner kind 는 `ci`를 권장하고,
    매 실행 artifact 는 docs baseline 과 섞지 않는 `artifacts/benchmarks/runners/<ci-runner-id>/...` 영역으로 분리한다.
  - 비고: CI 실패 조건은 build/test, command usage/write failure, delivery/drop/leak hard gate 실패로 제한한다.
    latency/HWM/warning 은 report-only 이며 `warning-count > 0`만으로 실패하지 않는다.
  - 검증: benchmark `Program` exit code 규칙, `BenchmarkRunIdentity` 환경 변수 규칙, `.github/workflows` 부재를 대조했다.
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] explicit runner 2-date-root reference 이후 Phase 4 gate 승격 후보를 재평가했다.
  - 범위: `docs/superpowers/specs/2026-06-25-phase4-gate-promotion-reassessment-design.md`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: `local-win-x64-01`은 두 date root, 6-session compatible reference 를 갖췄지만
    D082의 서로 다른 date root 3개 이상 조건과 별도 warning threshold 검토 조건은 아직 충족하지 못했다.
  - 비고: warning-as-failure 와 CI latency hard gate 는 계속 보류한다. 세 번째 date root 는 실제 다음 측정 날짜에 수집한다.
    다음 실행 가능한 문서 단위는 CI artifact-only benchmark 정책 설계다.
  - 검증: runner root history 와 `docs/benchmarks/baselines/index.md` 수치를 대조했다.
    D082 조건 충족/미충족 상태를 설계 문서에 명시했다. `git diff --check` exit 0,
    solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] `local-win-x64-01/2026-06-25/session-03` explicit runner baseline 을 수집했다.
  - 범위: `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/session-03/`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/history.json`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/history.json`,
    `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: raw report 6개, `summary.json`, `summary.md`를 생성하고,
    date-level `history.json`/`history.md`, runner root `history.json`/`history.md`를 재생성했다.
  - 비고: 2026-06-25 date root 는 session-count 3, hard-passed true, warning-count 0,
    comparison-compatible true 다. runner root 는 session-count 6, hard-passed true,
    warning-count 0, comparison-compatible true 다. explicit runner envelope 는 load p99 max 935.6 us,
    open-loop p99 max 1077.4 us 이다. 같은 runner 의 두 date root 가 각각 3-session reference 를 갖췄으므로
    다음 단위는 D082 gate 승격 후보 재평가다.
  - 검증: baseline suite pass, summary CLI source-report-count 6/hard-passed true/warning-count 0,
    date history CLI session-count 3/hard-passed true/warning-count 0,
    runner history CLI session-count 6/hard-passed true/warning-count 0.
    runner artifact local absolute path 검색 결과 없음. `Hps.Benchmarks.Tests` 67개 통과,
    `git diff --check` exit 0, restore asset 재생성 후 solution build 경고 0/오류 0,
    solution tests 269개 통과.

- [x] `local-win-x64-01/2026-06-25/session-02` explicit runner baseline 을 수집했다.
  - 범위: `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/session-02/`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/history.json`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/history.json`,
    `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: raw report 6개, `summary.json`, `summary.md`를 생성하고,
    date-level `history.json`/`history.md`, runner root `history.json`/`history.md`를 재생성했다.
  - 비고: 2026-06-25 date root 는 session-count 2, hard-passed true, warning-count 0,
    comparison-compatible true 다. runner root 는 session-count 5, hard-passed true,
    warning-count 0, comparison-compatible true 다. explicit runner envelope 는 load p99 max 935.6 us,
    open-loop p99 max 1077.4 us 이다. 두 번째 date root 가 아직 2-session 이므로 gate 승격은 보류한다.
  - 검증: baseline suite pass, summary CLI source-report-count 6/hard-passed true/warning-count 0,
    date history CLI session-count 2/hard-passed true/warning-count 0,
    runner history CLI session-count 5/hard-passed true/warning-count 0.
    runner artifact local absolute path 검색 결과 없음. `Hps.Benchmarks.Tests` 67개 통과,
    `git diff --check` exit 0, restore asset 재생성 후 solution build 경고 0/오류 0,
    solution tests 269개 통과.

- [x] `local-win-x64-01/2026-06-25/session-01` explicit runner baseline 을 수집했다.
  - 범위: `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/session-01/`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/history.json`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/history.json`,
    `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: raw report 6개, `summary.json`, `summary.md`, date-level `history.json`/`history.md`,
    runner root `history.json`/`history.md`를 생성했다.
  - 비고: 2026-06-25 date root 는 session-count 1, hard-passed true, warning-count 0,
    comparison-compatible true 다. runner root 는 session-count 4, hard-passed true,
    warning-count 0, comparison-compatible true 다. explicit runner envelope 는 load p99 max 921.1 us,
    open-loop p99 max 1077.4 us 이다. 두 번째 date root 가 아직 1-session 이므로 gate 승격은 보류한다.
  - 검증: baseline suite pass, summary CLI source-report-count 6/hard-passed true/warning-count 0,
    date history CLI session-count 1/hard-passed true/warning-count 0,
    runner history CLI session-count 4/hard-passed true/warning-count 0.
    runner artifact local absolute path 검색 결과 없음. `Hps.Benchmarks.Tests` 67개 통과,
    `git diff --check` exit 0, restore asset 재생성 후 solution build 경고 0/오류 0,
    solution tests 269개 통과.

- [x] explicit runner 3-session 이후 Phase 4 다음 후보를 재평가했다.
  - 범위: `docs/superpowers/specs/2026-06-25-phase4-after-explicit-runner-reference-reassessment.md`,
    `DECISIONS.md`, root 상태 문서.
  - 결과: 다음 단위를 `local-win-x64-01/2026-06-25/session-01` explicit runner baseline 수집으로 정했다(D085).
  - 비고: 같은 runner 의 date root 가 아직 1개뿐이므로 CI/warning-as-failure 설계는 다음 date root 표본을 추가한 뒤 다시 평가한다.
  - 검증: `local-win-x64-01/2026-06-24/history.json`, `docs/benchmarks/baselines/index.md`,
    D082/D084, `.claude/review/`의 기존 benchmark 리뷰 의견을 대조했다.
    신규 spec placeholder 검색 결과 없음. `git diff --check` exit 0,
    solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] explicit runner baseline 을 3-session reference 로 확장하고 문서 batch 를 완료했다.
  - 범위: `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/session-02/`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/session-03/`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/history.json`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/history.md`,
    `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: `session-02`, `session-03` raw report 를 각각 6개씩 생성하고, 각 summary artifact 와
    3-session 기준 date-level history artifact 를 재생성했다.
  - 비고: history 는 `session-count=3`, `hard-passed=true`, `warning-count=0`,
    `comparison-compatible=true`, unknown runner 0, mismatch 0 이다.
    explicit runner envelope 는 load p99 max 870.7 us, open-loop p99 max 1051.5 us 이다.
    같은 runner 의 date root 가 아직 1개뿐이므로 D082 warning-as-failure 승격 조건에는 산입하지 않는다.
  - 검증: session-02/session-03 baseline suite pass, 각 summary CLI source-report-count 6/hard-passed true/warning-count 0,
    history CLI session-count 3/hard-passed true/warning-count 0.
    runner artifact local absolute path 검색 결과 없음. `Hps.Benchmarks.Tests` 67개 통과,
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] 첫 explicit runner baseline 을 새 runner group 구조에 수집했다.
  - 범위: `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/session-01/`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/history.json`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/history.md`,
    `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: raw report 6개, `summary.json`, `summary.md`, date-level `history.json`, `history.md`를 생성했다.
  - 비고: `runner-id=local-win-x64-01`, `runner-kind=local`, `comparison-compatible=true`, unknown runner 0, mismatch 0 이다.
    첫 explicit runner baseline 은 저장 구조 검증 표본이며, 아직 D082 warning-as-failure 승격 표본은 아니다.
  - 검증: baseline suite pass, summary CLI source-report-count 6/hard-passed true/warning-count 0,
    history CLI session-count 1/hard-passed true/warning-count 0.
    runner artifact local absolute path 검색 결과 없음. `Hps.Benchmarks.Tests` 67개 통과,
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] explicit runner baseline 저장 구조와 수집 정책을 설계했다.
  - 범위: `docs/superpowers/specs/2026-06-24-explicit-runner-baseline-storage-policy-design.md`,
    `docs/benchmarks/baselines/index.md`, `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: 명시적 runner baseline 은 `docs/benchmarks/baselines/runners/<runner-id>/YYYY-MM-DD/session-NN/`
    구조에 저장하기로 했다(D084).
  - 비고: 현재 `BaselineHistoryReader`는 runner root 를 parent root 로 받아 바로 아래 `YYYY-MM-DD` directories 를 읽을 수 있다.
    기존 top-level date roots 는 legacy/local-unspecified baseline 으로 보존한다.
  - 검증: D079/D080/D082/D083과 `BaselineHistoryReader` directory 규칙 대조 완료.
    신규 설계/결정/index 문서 임시 표기 검색 결과 없음.
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] D082 이후 Phase 4 다음 실행 후보를 재평가하고 단일 작업 단위를 선정했다.
  - 범위: `docs/superpowers/specs/2026-06-24-phase4-next-candidate-reassessment.md`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: 명시적 runner id baseline 을 기존 `2026-06-24/session-04`처럼 바로 추가하지 않고,
    explicit runner baseline 저장 구조와 수집 정책을 먼저 설계하기로 했다(D083).
  - 비고: `BaselineHistoryReader`는 현재 `YYYY-MM-DD` date root 와 `session-NN`만 읽으므로, 같은 date root 에
    `local-unspecified`와 explicit runner id session 을 섞으면 intentional comparison mismatch 가 된다.
  - 검증: D082/D079/D080 및 `BaselineHistoryReader` directory 규칙 대조 완료.
    신규 설계/결정 문서 임시 표기 검색 결과 없음.
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] D082 latency envelope/gate 보류 설계 검토 의견을 반영했다.
  - 범위: `docs/superpowers/specs/2026-06-24-latency-envelope-and-gate-deferral-design.md`,
    `docs/benchmarks/baselines/index.md`, `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: 집계 방식은 세 session summary 의 `by-kind` aggregate 를 세션 간 max/min 으로 다시 집계한다고 명시했다.
    2026-06-24 `runner-id=local-unspecified` baseline 은 gate 승격 표본 count 에 산입하지 않고 reference 로만 쓴다고 명시했다.
    envelope 초과 기록은 자동 failure 나 schema field 가 아니라 수동 리뷰 메모라고 명시했다.
  - 비고: 검토서는 승인 수준이며 must-fix는 없었다.
  - 검증: D082 리뷰 finding 1/2와 info 3 반영 여부 대조 완료.
    신규 설계/결정/index 문서 임시 표기 검색 결과 없음.
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] 2026-06-24 compatible baseline 3개를 근거로 latency envelope 재산정과 warning-as-failure/CI gate 보류 조건을 설계했다.
  - 범위: `docs/superpowers/specs/2026-06-24-latency-envelope-and-gate-deferral-design.md`,
    `docs/benchmarks/baselines/index.md`, `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: D082로 2026-06-24 compatible baseline 3개를 reference latency envelope 로 채택하되,
    hard latency gate, warning-as-failure, CI latency failure 는 계속 보류한다고 정리했다.
  - 비고: 현 envelope 는 load p99 max 1020.4 us, open-loop p99 max 1006.5 us 이므로 1 ms hard SLO 는 현 baseline 과 맞지 않는다.
  - 검증: 2026-06-24 history/session summary 수치 대조 완료.
    신규 설계/결정/index 문서 임시 표기 검색 결과 없음.
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] 2026-06-24 문서 전용 작업 batch 규칙을 명시했다.
  - 범위: `AGENT_RULES.md`, `DECISIONS.md`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 결과: 구현/테스트/리팩터링은 계속 작은 기능 단위로 유지하고, 문서 전용 작업은 관련 설계/상태/결정/검토 문서를
    한 coherent documentation cycle 에서 같이 정렬하는 기준을 추가했다.
  - 비고: 문서 batch 에 코드/테스트 구현 변경을 섞지 않는 경계도 함께 기록했다.
  - 검증: 관련 root 문서 용어 대조, `git diff --check` exit 0, solution build 경고 0/오류 0,
    solution tests 269개 통과.

- [x] 2026-06-24 current-schema baseline session-03 을 추가했다.
  - 범위: `docs/benchmarks/baselines/2026-06-24/session-03/*.json`,
    `docs/benchmarks/baselines/2026-06-24/session-03/summary.md`,
    `docs/benchmarks/baselines/2026-06-24/history.json`,
    `docs/benchmarks/baselines/2026-06-24/history.md`,
    `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: D079 runner identity/environment metadata 를 포함한 raw report 6개(load 3회/open-loop 3회)와
    D080 comparison field 를 포함한 summary/history artifact 를 추가했다.
  - 비고: 2026-06-24 history 는 session-count 3이며 `comparison-compatible=true`, unknown runner 0, mismatch 0 이다.
  - 검증: baseline suite pass, summary CLI source-report-count 6/hard-passed true/warning-count 0,
    history CLI session-count 3/hard-passed true/warning-count 0.
    `docs/benchmarks/baselines/2026-06-24` 아래 local absolute path 검색은 매칭 없음이다.
    `Hps.Benchmarks.Tests` 67개 통과, `git diff --check` exit 0, solution build 경고 0/오류 0,
    solution tests 269개 통과.

- [x] 2026-06-24 current-schema baseline session-02 를 추가했다.
  - 범위: `docs/benchmarks/baselines/2026-06-24/session-02/*.json`,
    `docs/benchmarks/baselines/2026-06-24/session-02/summary.md`,
    `docs/benchmarks/baselines/2026-06-24/history.json`,
    `docs/benchmarks/baselines/2026-06-24/history.md`,
    `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: D079 runner identity/environment metadata 를 포함한 raw report 6개(load 3회/open-loop 3회)와
    D080 comparison field 를 포함한 summary/history artifact 를 추가했다.
  - 비고: 2026-06-24 history 는 session-count 2이며 `comparison-compatible=true`, unknown runner 0, mismatch 0 이다.
  - 검증: baseline suite pass, summary CLI source-report-count 6/hard-passed true/warning-count 0,
    history CLI session-count 2/hard-passed true/warning-count 0.
    `docs/benchmarks/baselines/2026-06-24` 아래 local absolute path 검색은 매칭 없음이다.
    `Hps.Benchmarks.Tests` 67개 통과, `git diff --check` exit 0, solution build 경고 0/오류 0,
    solution tests 269개 통과.

- [x] 2026-06-24 current-schema baseline session 을 추가했다.
  - 범위: `docs/benchmarks/baselines/2026-06-24/session-01/*.json`,
    `docs/benchmarks/baselines/2026-06-24/session-01/summary.md`,
    `docs/benchmarks/baselines/2026-06-24/history.json`,
    `docs/benchmarks/baselines/2026-06-24/history.md`,
    `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: D079 runner identity/environment metadata 를 포함한 raw report 6개(load 3회/open-loop 3회)와
    D080 comparison field 를 포함한 summary/history artifact 를 추가했다.
  - 비고: 이번 session 의 summary/history comparison 은 `comparison-compatible=true`, unknown runner 0, mismatch 0 이다.
  - 검증: baseline suite pass, summary CLI source-report-count 6/hard-passed true/warning-count 0,
    history CLI session-count 1/hard-passed true/warning-count 0.
    `docs/benchmarks/baselines/2026-06-24` 아래 local absolute path 검색은 매칭 없음이다.
    `Hps.Benchmarks.Tests` 67개 통과, `git diff --check` exit 0, solution build 경고 0/오류 0,
    solution tests 269개 통과.

- [x] 2026-06-24 2026-06-18 baseline summary/history artifact 를 현재 schema 로 재생성했다.
  - 범위: `docs/benchmarks/baselines/2026-06-18/summary.json`,
    `docs/benchmarks/baselines/2026-06-18/summary.md`,
    `docs/benchmarks/baselines/2026-06-18/session-02/summary.json`,
    `docs/benchmarks/baselines/2026-06-18/session-02/summary.md`,
    `docs/benchmarks/baselines/2026-06-18/session-03/summary.json`,
    `docs/benchmarks/baselines/2026-06-18/session-03/summary.md`,
    `docs/benchmarks/baselines/2026-06-18/history.json`,
    `docs/benchmarks/baselines/2026-06-18/history.md`,
    `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: root/session-02/session-03 summary artifact 가 D080 comparison field 를 포함하고,
    date-level history artifact 가 세 session 을 집계한다.
  - 추가 보정: `BaselineReportReader`가 `SourcePath`를 입력 directory 기준 상대 경로로 보존하게 해
    committed artifact 에 local workspace 절대 경로가 들어가지 않게 했다.
  - 비고: 기존 raw report 는 D079 이전 artifact 라서 comparison 은 `unknown-runner` mismatch 로 false 이며,
    이는 hard failure 나 warning 이 아니라 비교 가능성 신호다.
  - 검증: summary CLI 3회 모두 source-report-count 6, hard-passed true, warning-count 0.
    history CLI 1회는 session-count 3, hard-passed true, warning-count 0.
    relative source-path focused test 는 Red/Green 을 확인했고, artifact 절대 경로 검색은 매칭 없음이다.
    `Hps.Benchmarks.Tests` 67개 통과, `git diff --check` exit 0, solution build 경고 0/오류 0,
    solution tests 269개 통과.

- [x] 2026-06-24 benchmark writer metadata roundtrip test 를 보강했다.
  - 범위: `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`, root 상태 문서.
  - 결과: `TcpLoopbackReportWriter`가 쓴 raw report 를 `BaselineReportReader`로 다시 읽어 D079 runner/environment metadata 전체를 검증한다.
  - 비고: `os-architecture=Arm64`, `process-architecture=X64`를 의도적으로 다르게 둬 architecture field name drift 와 혼동을 잡는다.
  - Red: `TcpLoopbackReportWriter`의 `process-architecture` field 이름을 임시로 바꿨을 때 새 roundtrip test 가
    `Expected: "X64", Actual: "unknown"` assertion failure 로 실패함을 확인했다.
  - Green/검증: focused roundtrip test 1개 통과, `Hps.Benchmarks.Tests` 66개 통과, `git diff --check` exit 0,
    solution build 경고 0/오류 0, solution tests 268개 통과.

- [x] 2026-06-24 summary/history comparison signal 계획 리뷰 보강을 완료했다.
  - 범위: `.claude/review/2026-06-24-summary-history-comparison-signal-plan-review.md`,
    `docs/superpowers/plans/2026-06-24-summary-history-comparison-signal.md`, `DECISIONS.md`,
    `tests/Hps.Benchmarks.Tests/BaselineSummaryGeneratorTests.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineSummaryMarkdownWriterTests.cs`, root 상태 문서.
  - 결과: Summary Markdown null-key/legacy unknown 경로와 partial unknown identity 판정을 테스트로 고정했다.
  - 비고: hard comparison identity field 중 하나라도 `unknown`이면 partial metadata 라도 `unknown-runner`로 본다.
  - Red: null-key guard 제거 mutation 에서 Markdown test 가 `NullReferenceException`으로 실패함을 확인했다.
    partial unknown predicate 약화 mutation 에서 generator test 가 `Assert.False()` failure 로 실패함을 확인했다.
  - Green/검증: focused 보강 tests 2개 통과, `Hps.Benchmarks.Tests` 65개 통과.

- [x] 2026-06-24 summary/history comparison signal Task 5를 구현했다.
  - 범위: `tests/Hps.Benchmarks/BaselineHistoryWriter.cs`,
    `tests/Hps.Benchmarks/BaselineHistoryMarkdownWriter.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineHistoryGeneratorWriterTests.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineHistoryProgramTests.cs`, root 상태 문서.
  - 결과: history JSON top-level/session entry 에 comparison field 를 출력하고,
    history Markdown 에 `## Comparison` section 을 출력한다.
  - 비고: comparison mismatch-only history 는 hard gate/warning-count 를 바꾸지 않고 Program exit code 0을 유지한다.
  - Red: JSON writer/Program tests 가 comparison field 부재로 `KeyNotFoundException`을 냄을 확인했다.
    Markdown writer test 는 `## Comparison` section 부재로 `Assert.Contains()` 실패함을 확인했다.
  - Green/검증: focused Task 5 tests 3개 통과, `Hps.Benchmarks.Tests` 63개 통과.

- [x] 2026-06-24 summary/history comparison signal Task 4를 구현했다.
  - 범위: `tests/Hps.Benchmarks/BaselineHistorySession.cs`, `tests/Hps.Benchmarks/BaselineHistory.cs`,
    `tests/Hps.Benchmarks/BaselineHistoryReader.cs`, `tests/Hps.Benchmarks/BaselineHistoryGenerator.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineHistoryReaderTests.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineHistoryGeneratorWriterTests.cs`, root 상태 문서.
  - 결과: history session/history model 이 comparison result 를 보존하고,
    reader 는 summary comparison field 와 legacy fallback 을 읽으며,
    generator 는 session comparison key 를 history-level compatibility 로 집계한다.
  - 비고: comparison mismatch 는 hard gate, failed-session-count, warning-count 를 바꾸지 않는 별도 result 로 유지한다.
  - Red: comparison property contract tests 2개가 `Assert.NotNull()` 실패함을 확인했다.
    reader/generator behavior tests 5개는 stub comparison 에서 `Assert.True()`/`Assert.Single()` 실패함을 확인했다.
  - Green/검증: focused history reader/generator tests 12개 통과.

- [x] 2026-06-24 summary/history comparison signal Task 3을 구현했다.
  - 범위: `tests/Hps.Benchmarks/BaselineSummaryWriter.cs`,
    `tests/Hps.Benchmarks/BaselineSummaryMarkdownWriter.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineSummaryMarkdownWriterTests.cs`, root 상태 문서.
  - 결과: summary JSON top-level 에 comparison-compatible/key/mismatch field 를 쓰고,
    summary Markdown 에 `## Comparison` section 과 workload case table 을 출력한다.
  - 비고: output 은 Task 2에서 계산한 `BaselineSummary.Comparison`을 그대로 사용하며 writer 에서 재계산하지 않는다.
  - Red: JSON writer test 가 `comparison-compatible` field 부재로 `KeyNotFoundException`을 냄을 확인했다.
    Markdown writer test 는 `## Comparison` section 부재로 `Assert.Contains()` 실패함을 확인했다.
  - Green/검증: focused JSON writer test 1개 통과, focused Markdown writer tests 3개 통과,
    `Hps.Benchmarks.Tests` 53개 통과.

- [x] 2026-06-24 summary/history comparison signal Task 2를 구현했다.
  - 범위: `tests/Hps.Benchmarks/BaselineComparisonCase.cs`, `BaselineComparisonKey.cs`,
    `BaselineComparisonMismatch.cs`, `BaselineComparisonResult.cs`, `BaselineSummary.cs`, `BaselineSummaryGenerator.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineSummaryGeneratorTests.cs`, root 상태 문서.
  - 결과: `BaselineSummary.Comparison`과 내부 comparison model 을 추가했고, summary generator 가 compatible 여부,
    key, unknown runner count, mismatch 목록을 계산한다.
  - 비고: `processor-count`는 D080대로 comparison key 에 넣지 않고, `load`/`open-loop`은 `result-name`별 case 로 분리한다.
  - Red: `BaselineSummary.Comparison` property 부재 contract test 가 `Assert.NotNull()` 실패함을 확인했다.
    compatible behavior test 는 stub comparison 에서 `Expected: True, Actual: False`로 실패함을 확인했다.
  - Green/검증: focused `BaselineSummaryGeneratorTests` 8개 통과, `Hps.Benchmarks.Tests` 51개 통과.

- [x] 2026-06-24 summary/history comparison signal Task 1을 구현했다.
  - 범위: `tests/Hps.Benchmarks/BaselineReport.cs`, `tests/Hps.Benchmarks/BaselineReportReader.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineSummaryGeneratorTests.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineSummaryMarkdownWriterTests.cs`, root 상태 문서.
  - 결과: `BaselineReport`가 raw report 의 `PayloadBytes`, `TargetRateHz`, `TargetDurationSeconds`를 보존하고,
    `BaselineReportReader`가 `payload-bytes`, `target-rate-hz`, `target-duration-seconds`를 읽는다.
  - 비고: direct `BaselineReport` helper 호출부에는 현재 benchmark 기본값 `4096`, `100.0`, `30`을 명시했다.
  - Red: payload/target property 부재 contract test 가 `Assert.NotNull()` 실패함을 확인했다.
    reader behavior test 는 `Expected: 4096, Actual: 0`으로 실패함을 확인했다.
  - Green/검증: focused `BaselineReportReaderWriterTests` 8개 통과, focused `BaselineSummary*` 6개 통과,
    `Hps.Benchmarks.Tests` 46개 통과.

- [x] 2026-06-24 summary/history comparison signal 구현 계획을 작성했다.
  - 범위: `docs/superpowers/specs/2026-06-23-summary-history-comparison-signal-design.md`,
    `tests/Hps.Benchmarks/BaselineReport*.cs`, `BaselineSummary*.cs`, `BaselineHistory*.cs`,
    관련 benchmark tests.
  - 결과: `docs/superpowers/plans/2026-06-24-summary-history-comparison-signal.md`에 5개 커밋 단위 구현 계획을 추가했다.
  - 작업 단위: Task 1 `BaselineReport` payload/target settings, Task 2 summary comparison model/generator,
    Task 3 summary JSON/Markdown output, Task 4 history reader/generator aggregate,
    Task 5 history JSON/Markdown output 과 CLI smoke.
  - 비고: 새 테스트에는 무엇을 검증하는지 한국어 주석을 남기고, comparison mismatch 는 hard gate/기존 `warning-count`/exit code 와
    분리한다는 요구를 계획에 명시했다.
  - 검증: D080 설계와 현재 source/test 구조를 대조해 touched files, Red/Green 경계, 커밋 경계를 확인했다.

- [x] 2026-06-23 summary/history comparison signal 설계를 완료했다.
  - 범위: `docs/superpowers/specs/2026-06-23-summary-history-comparison-signal-design.md`,
    D079 raw metadata, `BaselineReport`, `BaselineSummary*`, `BaselineHistory*`.
  - 결과: summary/history JSON에 `comparison-compatible`, `comparison-key`, `comparison-mismatch-count`,
    `comparison-mismatches`, `unknown-runner-count` 계열 additive field 를 두는 설계를 작성했다.
  - 결정: D080으로 comparison signal 은 hard gate, 기존 `warning-count`, CLI exit code 에 영향을 주지 않는
    non-failing compatibility artifact 로 둔다.
  - 비고: summary 안에서 `load`와 `open-loop` scenario 가 다를 수 있으므로, comparison key 는 단일 scenario 가 아니라
    `result-name`별 `cases` 배열로 표현한다.
  - 검증: current benchmark model/writer/reader 구조와 D079 설계를 대조했다.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 246개 통과.

- [x] 2026-06-23 benchmark runner identity Task 1~3 구현 검토를 완료했다.
  - 범위: D079 설계, 구현 계획, `BenchmarkRunIdentity`, `TcpLoopbackRunResult`, `TcpLoopbackReportWriter`,
    `BaselineReport`, `BaselineReportReader`, 관련 focused tests.
  - 결과: 새 Blocker/Major finding 은 없다.
  - 비고: writer metadata field drift 를 더 강하게 잡는 roundtrip test 는 `P3_NICE` deferred backlog 로 남겼다.
  - 리뷰: `docs/agent-state/reviews/2026-06-23-benchmark-runner-identity-implementation-review.md`.
  - 검증: 코드/테스트/문서 대조를 수행했다. `git diff --check`, solution build 경고 0/오류 0, solution tests 246개 통과.

- [x] 2026-06-23 benchmark runner identity Task 3 raw report reader/legacy compatibility 를 구현했다.
  - 범위: `tests/Hps.Benchmarks/BaselineReport.cs`, `tests/Hps.Benchmarks/BaselineReportReader.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`, root 상태 문서.
  - 결과: `BaselineReport`가 `BenchmarkRunIdentity`를 보존하고, `BaselineReportReader`가 신규 raw report metadata 를 읽는다.
  - 비고: metadata 가 없는 legacy raw report 는 crash 나 임의 추론 없이 `BenchmarkRunIdentity.Unknown`으로 보존한다.
  - Red: `BaselineReport.Identity` property 부재로 contract test 가 `Assert.NotNull()` 실패함을 확인했다.
    metadata 포함 raw report reader test 는 `Expected: tcp-loopback-saea-v1, Actual: unknown`으로 실패함을 확인했다.
  - Green/검증: focused `BaselineReportReaderWriterTests` 6개 통과, `Hps.Benchmarks.Tests` 44개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 246개 통과.

- [x] 2026-06-23 benchmark runner identity Task 2 raw report writer metadata 를 구현했다.
  - 범위: `tests/Hps.Benchmarks/TcpLoopbackRunResult.cs`, `tests/Hps.Benchmarks/TcpLoopbackReportWriter.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`, root 상태 문서.
  - 결과: `TcpLoopbackRunResult`가 `BenchmarkRunIdentity`를 보존하고, `TcpLoopbackReportWriter`가 raw report schema v1 top-level 에
    runner/environment metadata field 를 additive 로 기록한다.
  - 비고: 기존 runner 생성자는 identity optional parameter 로 호환성을 유지하며, 명시 identity 가 없으면 `CaptureDefault()`를 사용한다.
  - Red: writer metadata shape test 가 `benchmark-profile` 미기록으로 `Assert.True()` 실패함을 확인했다.
  - Green/검증: focused writer metadata test 1개 통과, `Hps.Benchmarks.Tests` 41개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 243개 통과.

- [x] 2026-06-23 benchmark runner identity Task 1 model 을 구현했다.
  - 범위: `tests/Hps.Benchmarks/BenchmarkRunIdentity.cs`, `tests/Hps.Benchmarks.Tests/BenchmarkRunIdentityTests.cs`, root 상태 문서.
  - 결과: raw report metadata 의 공통 identity model 과 `CaptureDefault()`를 추가했다.
  - 비고: default runner id/kind 는 privacy 우선으로 `local-unspecified`/`local`이며,
    명시 override 는 `HPS_BENCHMARK_RUNNER_ID`, `HPS_BENCHMARK_RUNNER_KIND`만 사용한다.
  - Red: 타입 부재 contract test 1개 `Assert.NotNull()` 실패, behavior tests 2개가 `unknown` 반환으로 실패함을 확인했다.
  - Green/검증: focused `BenchmarkRunIdentityTests` 3개 통과, `Hps.Benchmarks.Tests` 40개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 242개 통과.

- [x] 2026-06-23 benchmark runner identity 구현 계획을 작성했다.
  - 범위: D079 설계, benchmark raw report writer/reader/source model, 기존 benchmark test 패턴.
  - 결과: `docs/superpowers/plans/2026-06-23-benchmark-runner-identity.md`에 3개 커밋 단위 구현 계획을 추가했다.
  - 작업 단위: Task 1 `BenchmarkRunIdentity` model, Task 2 raw report writer metadata, Task 3 raw report reader/legacy compatibility.
  - 비고: summary/history comparison signal 은 raw metadata 원천 기록 뒤 별도 단위에서 다룬다.
  - 검증: 계획 self-review 로 D079 coverage, type consistency, commit boundary 를 확인했다.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 239개 통과.

- [x] 2026-06-23 Phase 4 backlog 를 재평가하고 benchmark runner identity 를 다음 구현 후보로 설계했다.
  - 범위: baseline history 이후 남은 Phase 4 항목, D069/D070/D071/D078, benchmark raw report/summary/history source, baseline index.
  - 결과: CI workflow/warning-as-failure/latency hard gate 보다 runner identity/environment metadata 를 먼저 기록해야 한다고 판단했다.
    설계는 `docs/superpowers/specs/2026-06-23-benchmark-runner-identity-design.md`에 기록했고 D079로 결정했다.
  - 비고: schema 는 raw report v1 additive field 로 유지하고, host name/user name/IP address 는 자동 수집하지 않는다.
  - 검증: 관련 상태 문서, 결정 문서, benchmark writer/reader/source model 을 대조했다.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 239개 통과.

- [x] 2026-06-23 baseline history report command 전체 구현 검토를 완료했다.
  - 범위: Task 1~4 parser/reader/generator/writer/Program wiring, tests, D078 설계 정합성.
  - 결과: 새 Blocker/Major finding 은 없고, `docs/agent-state/reviews/2026-06-23-baseline-history-command-implementation-review.md`에 검토 결과를 기록했다.
  - 비고: CLI optional Markdown path 오류 메시지 정밀화와 date root 직접 입력 Program smoke 는 비차단 후속으로 남겼다.
  - 검증: 실제 baseline root CLI smoke 로 session-count 3, hard-passed true, warning-count 0과 UTF-8 Markdown 출력을 확인했다.

- [x] 2026-06-23 baseline history report command Task 4 Program wiring/smoke 를 구현했다.
  - 범위: `tests/Hps.Benchmarks/Program.cs`, `tests/Hps.Benchmarks.Tests/BaselineHistoryProgramTests.cs`, root 상태 문서.
  - 결과: `Program.Main`이 `--summarize-baseline-history <baseline-root> --history <output-json> [--history-md <output-md>]`를
    실행해 history JSON/Markdown artifact 를 생성한다.
  - 비고: warning-only history 는 success exit code 를 유지하고, failed session 이 있으면 failed-run exit code 를 반환한다.
  - Red: focused Program tests 3개가 구현 전 usage error exit code 2 반환으로 실패함을 확인했다.
  - Green/검증: focused Program tests 3개 통과, 실제 baseline root CLI smoke 는 session-count 3, hard-passed true, warning-count 0을 출력했다.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 239개 통과.

- [x] 2026-06-23 baseline history report command Task 3 history aggregate/writer 를 구현했다.
  - 범위: `tests/Hps.Benchmarks/BaselineHistory.cs`, `BaselineHistoryGenerator.cs`, `BaselineHistoryWriter.cs`,
    `BaselineHistoryMarkdownWriter.cs`, `tests/Hps.Benchmarks.Tests/BaselineHistoryGeneratorWriterTests.cs`, root 상태 문서.
  - 결과: session 목록을 history aggregate 로 변환하고, stable JSON schema 와 Markdown 보조 artifact 를 생성한다.
  - 비고: `hard-passed`는 session flag AND, 실패 카운터는 `failed-session-count`, p99 누락은 JSON `null`/Markdown `-`로 표현한다.
  - Red: reflection contract test 실패 1개, behavior tests 5개가 aggregate/writer stub 에서 실패함을 확인했다.
  - Green/검증: focused generator/writer tests 5개 통과, `git diff --check`, solution build 경고 0/오류 0, solution tests 236개 통과.

- [x] 2026-06-23 baseline history report command Task 2 history domain/reader 를 구현했다.
  - 범위: `tests/Hps.Benchmarks/BaselineHistorySession.cs`, `BaselineHistoryReader.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineHistoryReaderTests.cs`, root 상태 문서.
  - 결과: date root 와 parent baseline root 를 bounded discovery 로 읽고, legacy root `summary.json`과
    `session-NN/summary.json`을 `BaselineHistorySession` 목록으로 변환한다.
  - 비고: load/open-loop p99 가 없으면 `null`로 보존하고, HWM 은 없으면 0으로 둔다. summary 가 하나도 없으면 실패한다.
  - Red: reflection contract test 실패 1개, behavior tests 4개가 stub `NotSupportedException`으로 실패함을 확인했다.
  - Green/검증: focused reader tests 4개 통과, `git diff --check`, solution build 경고 0/오류 0, solution tests 231개 통과.

- [x] 2026-06-23 baseline history report command Task 1 parser contract 를 구현했다.
  - 범위: `tests/Hps.Benchmarks/BenchmarkCommand.cs`, `BenchmarkCommandLine.cs`, `BenchmarkCommandParser.cs`, `Program.cs`,
    `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`, root 상태 문서.
  - 결과: `--summarize-baseline-history <baseline-root> --history <output-json> [--history-md <output-md>]` parser contract 를 추가했다.
    `--report` 혼용은 usage error 로 막고, 실행 wiring 은 계획대로 Task 4에 남겼다.
  - Red: focused parser tests 에서 history command 테스트 5개가 실패함을 확인했다.
  - Green/검증: focused parser tests 15개 통과, `git diff --check`, solution build 경고 0/오류 0, solution tests 227개 통과.

- [x] 2026-06-23 baseline history report command 구현 계획 리뷰 보정을 완료했다.
  - 범위: `.claude/review/2026-06-23-baseline-history-report-command-review.md`,
    baseline history command 설계/구현 계획, D078 결정 문서, root 상태 문서.
  - 결과: history `hard-passed` 기준을 session `hard-passed` AND 로 명시했고, root 실패 카운터를
    `failed-session-count`로 고정했으며, 누락 p99 는 JSON `null`/Markdown `-`로 표현하도록 계획을 보정했다.
  - 다음: Task 1(parser contract) 구현부터 진행한다.

- [x] 2026-06-23 baseline history report command 구현 계획을 작성했다.
  - 범위: D078 설계, baseline history 설계 리뷰, `tests/Hps.Benchmarks` parser/source, summary reader/writer/test 패턴.
  - 결과: `docs/superpowers/plans/2026-06-23-baseline-history-report-command.md`에 4개 커밋 단위 구현 계획을 추가했다.
  - 작업 단위: Task 1 parser contract, Task 2 history domain/reader, Task 3 aggregate/writer, Task 4 Program wiring/smoke.
  - 비고: Task 2/3은 새 타입 도입 시 컴파일 실패 Red 를 피하기 위해 reflection contract Red → stub → behavior Red 순서를 명시했다.
  - 검증: 계획 self-review 로 spec coverage, placeholder scan, type consistency, commit boundary 를 확인했다.

- [x] 2026-06-23 baseline history report command 설계 리뷰를 완료했다.
  - 범위: `docs/superpowers/specs/2026-06-23-baseline-history-report-command-design.md`,
    `tests/Hps.Benchmarks/`, `tests/Hps.Benchmarks.Tests/`, `docs/benchmarks/baselines/index.md`, 결정/상태 문서.
  - 결과: enum 이름 모호성은 `BenchmarkCommand.SummarizeBaselineHistory`로 고정했고, parent baseline root/date root 입력 discovery 규칙을 분리했다.
  - 결정: D078로 history command 를 provider-independent aggregate artifact 로 두고 warning 은 soft signal 로 유지한다고 기록했다.
  - 리뷰: `docs/agent-state/reviews/2026-06-23-baseline-history-report-command-design-review.md`.
  - 검증: benchmark CLI/parser/source, summary writer/generator, baseline artifact 구조를 대조했다.

- [x] 2026-06-23 Phase 4 backlog 를 재평가하고 baseline history report command 를 설계했다.
  - 범위: `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`, baseline 관련 specs/plans/review, benchmark CLI/source 구조.
  - 결과: CI workflow/warning-as-failure 는 아직 보류하고, 여러 session `summary.json`을 읽어 `history.json`과 선택적 `history.md`를 쓰는
    provider-independent command 를 다음 구현 후보로 좁혔다.
  - 설계: `docs/superpowers/specs/2026-06-23-baseline-history-report-command-design.md`.
  - 검증: `PLAN.md`, `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`, baseline specs/plans/review,
    `tests/Hps.Benchmarks` CLI/parser/summary source 를 대조했다.

- [x] 2026-06-23 UDP lease sweep registry race guard 리뷰를 완료했다.
  - 범위: `a817c6e`, `src/Hps.Broker/BrokerUdpDatagramHandler.cs`,
    `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`, D077 관련 문서, root 상태 문서.
  - 결과: handler gate 직렬화는 sweep/re-register stale cleanup race 를 닫고, `PUBLISH` fan-out 을 lock 밖에 둔 범위도 D077과 정합했다.
  - 비고: race regression test 의 250ms scheduling window 는 비차단 Minor 관찰로 남겼다. fixed path green 판단은 해당 반환값에 의존하지 않는다.
  - 검증: `git show`/`rg`/line review 로 코드·테스트·문서 정합성을 대조했다.

- [x] 2026-06-23 UDP lease sweep registry cleanup stale snapshot race 를 막았다.
  - 범위: `src/Hps.Broker/BrokerUdpDatagramHandler.cs`, `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: UDP receive command/endpoint-close/sweep state mutation 을 handler gate 로 직렬화해, sweep expired snapshot 이후
    같은 stable target 이 재등록되는 경우 stale registry cleanup 이 새 online 상태를 disconnected 로 덮지 못하게 했다.
  - 비고: `PUBLISH` fan-out 은 lock 밖에서 유지해 transport send path 를 handler gate 에 묶지 않는다(D077).
  - 검증: focused race test Red assertion failure 1개 확인(`Assert.True()` failure), focused race test 통과,
    focused UDP handler tests 17개 통과, Broker tests 73개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 222개 통과.

- [x] 2026-06-23 UDP stable identity F1/F2 수정분 리뷰를 완료했다.
  - 범위: `b85220f`, `8749c64`, `src/Hps.Broker/BrokerUdpDatagramHandler.cs`,
    `src/Hps.Broker/UdpRemoteLeaseTracker.cs`, `src/Hps.Server/BrokerServer.cs`, root 상태 문서.
  - 결과: F2 invalid identity datagram isolation 은 적절하지만, F1 lease sweep registry cleanup 에 stale snapshot race 가 남아 있음을 확인했다.
  - 다음: 위 `P0_NOW` 항목으로 다음 구현 단위를 분리했다.
  - 검증: `rg` 기반 코드 경계 대조와 리뷰 문서 작성. `git diff --check`, solution build 경고 0/오류 0,
    solution tests 221개 통과.

- [x] 2026-06-23 UDP invalid stable identity datagram isolation 을 구현했다.
  - 범위: `src/Hps.Broker/BrokerUdpDatagramHandler.cs`, `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`, root 상태 문서.
  - 결과: UDP `REGISTER`/`UNREGISTER` identity token 이 decoder 를 통과한 뒤 registry validation 에서 거부될 값이어도
    handler 밖으로 예외가 전파되지 않고 해당 datagram 만 drop 된다.
  - 비고: Protocol decoder 전체 whitespace grammar 는 이번 범위에서 바꾸지 않았다. UDP handler boundary 에서 stable identity token 을
    비예외 방식으로 먼저 검사해 shared endpoint close 를 막는다.
  - 검증: focused Red assertion failure 2개 확인(`Assert.Null()` failure), focused invalid identity tests 2개 통과,
    focused UDP handler tests 16개 통과. `git diff --check`, solution build 경고 0/오류 0, solution tests 221개 통과.

- [x] 2026-06-23 UDP stable identity lease sweep registry cleanup 을 구현했다.
  - 범위: `src/Hps.Broker/BrokerUdpDatagramHandler.cs`, `src/Hps.Broker/UdpRemoteLeaseTracker.cs`,
    `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`, root 상태 문서.
  - 결과: UDP lease sweep 으로 만료된 stable remote target 이 routing table 뿐 아니라
    `SubscriberRegistry`에서도 disconnected 상태가 되어 retention sweep 대상이 된다.
  - 비고: `UdpRemoteLeaseTracker.SweepExpired(...)`의 기존 반환값은 routing table 제거 수로 유지하고,
    registry cleanup 용 expired target snapshot 은 선택적 side-channel 로 분리했다.
  - 검증: focused Red assertion failure 1개 확인(`Expected: 1, Actual: 0`), focused UDP handler tests 14개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 219개 통과.

- [x] 2026-06-23 Stable subscriber identity 구현 교차검증을 완료했다.
  - 범위: D075/D076 설계, Protocol/Broker/Server 구현, stable identity 관련 tests, root 상태 문서.
  - 결과: 구현 방향은 타당하지만 UDP 경계 must-fix 2건을 발견했다.
    상세는 `docs/agent-state/reviews/2026-06-23-stable-subscriber-identity-cross-check.md`에 기록했다.
  - 검증: `rg`와 줄 번호 기반 소스/테스트 대조를 수행했다.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 218개 통과.

- [x] 2026-06-22 Stable subscriber identity UDP loopback coverage 를 추가했다.
  - 범위: `tests/Hps.Server.Tests/BrokerServerTests.cs`, root 상태 문서.
  - 결과: 실제 `BrokerServer` + `SaeaTransport` UDP datagram loopback 에서 stable identity same-id remote rebind 가
    publish fan-out target 을 새 remote 로 옮김을 검증한다.
    새 remote 는 `REGISTER`만 보내고 `SUBSCRIBE`를 반복하지 않아 retained topic set 복구까지 확인한다.
  - 비고: UDP는 TCP처럼 old remote 를 close 할 수 없으므로, 이 테스트는 routing table 에서 old remote 만 제거되고
    새 remote 로 metadata 가 재바인딩되는 정책을 실제 datagram 경로로 고정한다.
  - 검증: focused stable UDP loopback test 1개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 218개 통과.

- [x] 2026-06-22 Stable subscriber identity TCP loopback coverage 를 추가했다.
  - 범위: `tests/Hps.Server.Tests/BrokerServerTests.cs`, root 상태 문서.
  - 결과: 실제 `BrokerServer` + `SaeaTransport` TCP loopback 에서 stable identity reconnect/rebind 가 동작함을 검증한다.
    새 socket 은 `REGISTER`만 보내고 `SUBSCRIBE`를 반복하지 않아 retained topic set 복구까지 확인한다.
  - 비고: old TCP target close 는 Windows loopback 에서 FIN 또는 reset 으로 관측될 수 있어 두 경우를 모두 close 완료로 본다.
  - 검증: focused stable TCP loopback test 1개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 217개 통과.

- [x] 2026-06-22 Stable subscriber identity UDP late REGISTER lease cleanup 을 구현했다.
  - 범위: `src/Hps.Broker/UdpRemoteLeaseTracker.cs`, `src/Hps.Broker/BrokerUdpDatagramHandler.cs`,
    `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`, stable identity 설계 문서, root 상태 문서.
  - 결과: UDP remote 가 `SUBSCRIBE` 후 `REGISTER`하면 routing table 뿐 아니라 optional lease tracker 의 pre-register
    runtime topic metadata 도 제거된다.
  - 비고: `REGISTER` 성공 후 같은 remote 의 lease metadata 는 registry rebound topic set 으로 교체하고,
    stable topic 이 없으면 lease 를 남기지 않는다.
  - 검증: focused UDP handler Red assertion failure 1개 확인, focused tests 13개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 216개 통과.

- [x] 2026-06-22 Stable subscriber identity late REGISTER cleanup 을 구현했다.
  - 범위: `src/Hps.Broker/SubscriberRegistry.cs`, `tests/Hps.Broker.Tests/SubscriberRegistryTests.cs`,
    stable identity 설계 문서, 결정/상태 문서.
  - 결과: `SUBSCRIBE` 후 `REGISTER` 순서에서 identity metadata 에 없는 runtime 구독을 `REGISTER` 시점에 제거해,
    close cleanup 이후 stale target 이 routing table 에 남지 않게 했다.
  - 결정: D076으로 late `REGISTER`는 기존 runtime 구독을 stable identity metadata 로 자동 이관하지 않는다고 기록했다.
  - 검증: focused registry Red assertion failure 1개 확인, focused tests 10개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 215개 통과.

- [x] 2026-06-22 Stable subscriber identity BrokerServer opt-in wiring 을 구현했다.
  - 범위: `src/Hps.Server/BrokerServerOptions.cs`, `src/Hps.Server/BrokerServer.cs`,
    `tests/Hps.Server.Tests/BrokerServerOptionsTests.cs`, `tests/Hps.Server.Tests/BrokerServerTests.cs`, root 상태 문서.
  - 결과: stable identity public options/factory/with method, shared `SubscriberRegistry` TCP/UDP handler 주입,
    retention sweep timer 생성/중복 방지/StopAsync dispose 를 연결했다.
  - 검증: stable identity Server/Options Red assertion failure 7개 확인, focused tests 7개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 214개 통과.

- [x] 2026-06-22 Stable subscriber identity UDP handler wiring 을 구현했다.
  - 범위: `src/Hps.Broker/BrokerUdpDatagramHandler.cs`, `src/Hps.Broker/UdpRemoteLeaseTracker.cs`,
    `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`, root 상태 문서.
  - 결과: optional registry internal constructor, UDP `REGISTER`/`UNREGISTER`, registered remote subscribe/unsubscribe,
    same-id remote rebind, duplicate target datagram-drop, endpoint close retention 을 연결했다.
  - 검증: internal constructor 부재 Red assertion failure 4개 확인, focused UDP handler tests 12개 통과.

- [x] 2026-06-22 Stable subscriber identity TCP handler wiring 을 구현했다.
  - 범위: `src/Hps.Broker/BrokerTcpFrameHandler.cs`, `tests/Hps.Broker.Tests/BrokerTcpFrameHandlerTests.cs`, root 상태 문서.
  - 결과: optional registry/time provider internal constructor, TCP `REGISTER`/`UNREGISTER`, registered target subscribe/unsubscribe,
    same-id reconnect rebind, duplicate target reject/close, connection close cleanup 을 연결했다.
  - 검증: internal constructor 부재 Red assertion failure 4개 확인, focused TCP handler tests 11개 통과.

- [x] 2026-06-22 Stable subscriber identity pure registry 를 구현했다.
  - 범위: `src/Hps.Broker/SubscriberIdentity.cs`, `src/Hps.Broker/SubscriberRegistrationResult.cs`,
    `src/Hps.Broker/SubscriberRegistry.cs`, `tests/Hps.Broker.Tests/SubscriberIdentityTests.cs`,
    `tests/Hps.Broker.Tests/SubscriberRegistryTests.cs`, root 상태 문서.
  - 결과: identity token validation, identity별 topic metadata, same-id rebind, duplicate target conflict,
    disconnect retention, explicit unregister, disconnected sweep, UDP endpoint cleanup 을 pure model 로 추가했다.
  - 검증: reflection contract Red assertion failure 2개, behavior Red assertion failure 10개 확인,
    focused broker identity/registry tests 15개 통과.

- [x] 2026-06-22 Stable subscriber identity protocol decode 를 구현했다.
  - 범위: `src/Hps.Protocol/TcpCommandKind.cs`, `src/Hps.Protocol/TcpCommandDecoder.cs`,
    `tests/Hps.Protocol.Tests/TcpCommandDecoderTests.cs`, root 상태 문서.
  - 결과: `REGISTER <subscriber-id>`와 `UNREGISTER <subscriber-id>`를 token-only command 로 decode 한다.
  - 검증: Red assertion failure 9개 확인, focused protocol tests 24개 통과.

- [x] 2026-06-22 Stable subscriber identity 구현 계획을 작성했다.
  - 범위: `docs/superpowers/plans/2026-06-22-stable-subscriber-identity.md`, root 상태 문서.
  - 결과: D075 설계를 protocol decode, pure registry, TCP handler, UDP handler, Server opt-in wiring 의 5개 커밋 단위로 나눴다.
  - 검증: 계획 self-review 로 spec coverage, placeholder, type consistency 를 확인했다.
    `git diff --check`, solution build/test 로 문서 변경이 빌드 상태를 깨지 않음을 확인한다.

- [x] 2026-06-22 Stable subscriber identity / reconnect rebinding 정책을 설계했다.
  - 범위: `docs/superpowers/specs/2026-06-22-stable-subscriber-identity-reconnect-policy-design.md`,
    `DECISIONS.md`, root 상태 문서.
  - 결과: 기본 runtime target subscription 은 유지하고, 후속 stable identity 는 opt-in `REGISTER <subscriber-id>` 기반 Broker registry 로 설계했다.
  - 결정: 같은 id 재등록은 새 runtime target 이 이기며, disconnected 동안 payload 는 저장하지 않는다. `EndpointId`는 계속 diagnostics id 로 둔다.
  - 검증: 기존 endpoint identity policy, D058/D059/D060, 실제 Broker routing/handler/decoder 구조와 대조했다.
    `git diff --check`, solution build/test 로 문서 변경이 빌드 상태를 깨지 않음을 확인한다.

- [x] 2026-06-22 BrokerServer UDP lease sweep host timer/public settings 를 구현했다.
  - 범위: `src/Hps.Server/BrokerServer.cs`, `src/Hps.Broker/Properties/AssemblyInfo.cs`,
    `tests/Hps.Server.Tests/BrokerServerTests.cs`, root 상태 문서.
  - 결과: `BrokerServerOptions` enabled 설정을 `BrokerUdpDatagramHandler`에 연결하고,
    `StartUdpAsync` 성공 후 sweep timer 를 생성하며 `StopAsync`/start 실패 cleanup 에서 dispose 한다.
  - 비고: 기본 생성자는 options 생성자로 위임해 disabled 기본 동작과 enabled host timer 경로가 같은 초기화 흐름을 사용한다.
  - 검증: Red assertion failure 2개 확인, focused tests 2개 통과, 생성자 reflection 제거 후 focused tests 2개 통과,
    solution build 경고 0/오류 0, solution tests 175개 통과.

- [x] 2026-06-22 BrokerServerOptions public 설정 타입을 구현했다.
  - 범위: `src/Hps.Server/BrokerServerOptions.cs`, `tests/Hps.Server.Tests/BrokerServerOptionsTests.cs`, root 상태 문서.
  - 결과: 기본 disabled options, 양수 timeout/interval 검증, explicit time provider 저장을 추가했다.
  - 검증: Red assertion failure 3개 확인, focused tests 3개 통과, reflection 제거 후 focused tests 3개 통과,
    solution build 경고 0/오류 0, solution tests 173개 통과.

- [x] 2026-06-22 BrokerServer UDP lease host timer 설계를 작성했다.
  - 범위: `docs/superpowers/specs/2026-06-22-broker-server-udp-lease-host-timer-design.md`, `DECISIONS.md`, root 상태 문서.
  - 결과: `BrokerServerOptions` public 설정, 기본 disabled, explicit timeout/interval, `TimeProvider.CreateTimer`,
    `Hps.Broker` friend assembly 경계를 D074로 확정했다.
  - 검증: 설계 self-review, `git diff --check`, solution build/test.

- [x] 2026-06-22 UDP lease tracker handler wiring 을 구현했다.
  - 범위: `src/Hps.Broker/BrokerUdpDatagramHandler.cs`, `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`, root 상태 문서.
  - 결과: UDP SUBSCRIBE/UNSUBSCRIBE/PUBLISH/endpoint-close activity 가 `UdpRemoteLeaseTracker`로 연결되고, handler 내부 sweep entry point 가 생겼다.
  - 비고: public constructor 는 disabled options 를 사용해 기존 기본 동작을 보존하고, internal constructor 로 후속 host/test wiring 이 options/time provider 를 주입한다.
  - 검증: Red assertion failure 2개 확인, focused handler tests 8개 통과, reflection 제거 후 focused handler tests 8개 통과, solution build 경고 0/오류 0, solution tests 170개 통과.

- [x] 2026-06-22 UDP remote lease pure sweep 을 구현했다.
  - 범위: `src/Hps.Broker/UdpRemoteLeaseTracker.cs`, `tests/Hps.Broker.Tests/UdpRemoteLeaseTrackerTests.cs`, root 상태 문서.
  - 결과: `SweepExpired(DateTimeOffset)`가 idle timeout 을 초과한 UDP remote target 을 모든 topic 에서 제거한다.
  - 비고: plan 예시의 survivor remote setup 은 같은 시점 구독이면 함께 만료되므로 survivor를 늦게 구독하도록 테스트를 보정했다.
  - 검증: Red assertion failure 3개 확인, focused tests 8개 통과, reflection 제거 후 focused tests 8개 통과, solution build 경고 0/오류 0, solution tests 168개 통과.

- [x] 2026-06-22 UDP remote lease tracker activity 를 구현했다.
  - 범위: `src/Hps.Broker/UdpRemoteLeaseTracker.cs`, `tests/Hps.Broker.Tests/UdpRemoteLeaseTrackerTests.cs`, root 상태 문서.
  - 결과: disabled options 에서는 기존 subscription 동작만 수행하고, enabled options 에서는 UDP remote target 별 lease 를 생성/갱신/제거한다.
  - 비고: 계획서의 compile-failure Red는 AGENTS의 assertion-failure Red 규칙에 맞춰 reflection 기반 타입 부재 assertion 실패로 보정했다.
  - 검증: Red assertion failure 5개 확인, focused tests 5개 통과, reflection 제거 후 focused tests 5개 통과, solution build 경고 0/오류 0, solution tests 165개 통과.

- [x] 2026-06-22 UDP lease options 를 구현했다.
  - 범위: `src/Hps.Broker/UdpLeaseOptions.cs`, `src/Hps.Broker/Properties/AssemblyInfo.cs`, `tests/Hps.Broker.Tests/UdpLeaseOptionsTests.cs`, 구현 계획/상태 문서.
  - 결과: 기본 비활성 options, 양수 timeout/interval 검증, 테스트 assembly internal 접근 경계를 추가했다.
  - 비고: 계획서의 `Enabled(...)` factory 는 `Enabled` property 와 C# 멤버 이름이 충돌해 `CreateEnabled(...)`로 정정했다.
  - 검증: Red assertion failure 확인, focused tests 3개 통과, solution build 경고 0/오류 0, solution tests 160개 통과.

- [x] 2026-06-22 UDP optional lease sweep 구현 계획을 작성했다.
  - 범위: `docs/superpowers/plans/2026-06-22-udp-optional-lease-sweep.md`, root 상태 문서.
  - 결과: D073 설계를 4개 커밋 단위로 나누고 각 단위의 Red-Green 검증 경로, touched files, produced interfaces 를 명시했다.
  - 검증: 계획 self-review 완료, `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 157개 통과.

- [x] 2026-06-22 UDP optional lease tracker / sweep owner 를 설계했다.
  - 범위: `docs/superpowers/specs/2026-06-22-udp-optional-lease-sweep-design.md`, root 상태 문서.
  - 결과: lease/sweep owner 를 Broker 소유·Server 트리거로, 설정을 내부 options(기본 비활성)로, 시간 소스를 `TimeProvider` 로 확정하고 sweep 의 `UnsubscribeAll(IUdpEndpoint, EndPoint)` 사용 방식을 D073으로 못 박았다.
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 157개 통과.

- [x] 2026-06-22 UDP remote-wide unsubscribe primitive 를 구현했다.
  - 범위: `src/Hps.Broker/SubscriptionTable.cs`, `tests/Hps.Broker.Tests/BrokerRoutingTests.cs`, root 상태 문서.
  - 결과: `(IUdpEndpoint, EndPoint)` 조합을 모든 topic 에서 제거하면서 같은 endpoint 의 다른 remote, 다른 endpoint 의 같은 remote, TCP subscriber 를 보존한다.
  - 검증: focused Red/Green/Refactor 완료, solution build 경고 0/오류 0, solution tests 157개 통과.

- [x] 2026-06-19 UDP stale remote idle expiry 를 설계했다.
  - 범위: `docs/superpowers/specs/2026-06-19-udp-stale-remote-idle-expiry-design.md`, root 상태 문서.
  - 결과: cleanup owner 를 Broker/Server 로 두고 기본 idle expiry 는 비활성화하며, 다음 구현을 remote-wide unsubscribe primitive 로 좁혔다(D072).
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 156개 통과.

- [x] 2026-06-18 baseline history index 를 추가했다.
  - 범위: `docs/benchmarks/baselines/index.md`, `docs/superpowers/specs/2026-06-18-baseline-report-history-warning-policy-design.md`, root 상태 문서.
  - 결과: 2026-06-18 root/session-02/session-03 summary artifact, hard/warning 상태, p99/HWM 대표값을 전역 entry point 에 연결하고 D071을 확정했다.
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 156개 통과.

- [x] 2026-06-18 baseline report history/warning 정책 설계를 작성했다.
  - 범위: `docs/superpowers/specs/2026-06-18-baseline-report-history-warning-policy-design.md`, root 상태 문서.
  - 결과: baseline session directory 를 history 단위로 보고, raw JSON/summary JSON/summary Markdown 역할을 분리하며, warning-as-failure 와 latency hard gate 는 보류하는 정책을 제안했다.
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 156개 통과.

- [x] 2026-06-18 baseline summary Markdown artifact 를 생성했다.
  - 범위: `docs/benchmarks/baselines/2026-06-18/**/summary.md`, `local-latency-baseline.md`, root 상태 문서.
  - 검증: CLI 3회 exit-code 0, source report count 6, hard-passed true, warning count 0. build 0/0, solution tests 156개 통과, `git diff --check` 통과.

- [x] baseline summary Markdown CLI 선택 출력을 연결했다.
  - 범위: `tests/Hps.Benchmarks/BenchmarkCommandLine.cs`, `BenchmarkCommandParser.cs`, `Program.cs`, parser tests, root 상태 문서.
  - 검증: parser/CLI Red-Green, focused benchmark tests 20개 통과, solution tests 156개 통과.

- [x] baseline summary Markdown writer 를 구현했다.
  - 범위: `BaselineSummaryMarkdownWriter`, writer tests, root 상태 문서.
  - 검증: writer Red-Green 후 focused tests 통과.

- [x] 2026-06-18 baseline summary JSON artifact 를 생성했다.
  - 범위: baseline root/session-02/session-03 `summary.json`, `local-latency-baseline.md`, root 상태 문서.
  - 검증: 세 summary 모두 source report count 6, hard-passed true, warning 0.

- [x] 반복 baseline summary artifact 와 soft warning 산출을 구현했다.
  - 범위: `tests/Hps.Benchmarks/`, `tests/Hps.Benchmarks.Tests/`, D070 spec/plan/state docs.
  - 검증: root/session-02/session-03 CLI smoke 통과, solution tests 통과.

- [x] 3개 반복 baseline session 기반 latency/CI 정책을 D070으로 정리했다.
  - 결과: p50/p99 hard threshold 는 보류하고 summary/soft warning 을 먼저 만든다.

- [x] State document compaction 을 수행했다.
  - 범위: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`, `docs/agent-state/` archive.
  - 결과: root 상태 파일은 현재 진입점만 남기고 상세 이력은 archive 로 이동했다.
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 전체 156개 통과/실패 0.
