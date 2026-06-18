# TODOS.md

## Current TODOs

- 현재 Codex가 자동으로 이어서 실행할 항목은 없다.
  - `BrokerPublisher`로 Broker publish fan-out 소유권 경계를 완료했다.
  - `BrokerPublisher`가 payload range 를 추가 복사 없이 fan-out 할 수 있게 됐다.
  - `SubscriptionTable.UnsubscribeAll(IConnection)`으로 Broker 라우팅 테이블의 connection-wide cleanup API를 완료했다.
  - `BrokerTcpFrameHandler`가 TCP command decode 결과를 Broker subscribe/publish/close cleanup 으로 연결한다.
  - `BrokerServer` 최소 TCP host wiring 으로 Transport receive handler 등록, listen, accept loop, stop 정리를 완료했다.
  - `BrokerServer + SaeaTransport` 실제 TCP command loopback 통합 테스트로 subscriber/publisher socket 경로를 검증했다.
  - TCP `TransportConnection` pending send queue 에 capacity 16 drop-oldest backpressure 와 evict-release 를 적용했다.
  - UDP `SaeaUdpEndpoint` pending send queue 에도 capacity 16 drop-oldest backpressure 와 evict-release 를 적용했다.
  - TCP/UDP drop-oldest 경로의 내부 `DroppedPendingSendCount` counter 를 추가했다.
  - `ITransportDiagnostics`와 `TransportDiagnosticsSnapshot`으로 Transport 수명 누적 drop snapshot 을 public 으로 읽을 수 있게 했다.
  - `BrokerServer + SaeaTransport` 실제 TCP command 경로에서 subscriber 2명 fan-out 통합 테스트를 추가했다.
  - malformed TCP command 로 Broker 가 직접 connection 을 닫는 경로에서도 connection-wide subscription cleanup 을 보장했다.
  - UDP datagram handler 예외가 receive loop task fault 로 숨지 않고 endpoint close notification 으로 수렴하도록 보강했다.
  - TCP receive handler 예외도 connection close notification 으로 수렴하도록 보강해 UDP와 수명 정책을 맞췄다.
  - `.claude/review/` 검토 의견의 현재 조치 현황을 문서로 남겼다.
  - D013 리뷰 게이트에 따라 다음 구현은 사용자 검토 후 별도 단위로 진행한다.
  - TCP wire protocol 기반 publisher/subscriber sample client 를 추가했다.
  - 수동 fan-out 확인을 위한 broker server console sample 을 추가했다.
  - D010 TCP frame assembler 랜덤 적대적 fuzz 를 영구 회귀 테스트로 추가했다.
  - Phase 4 `Hps.Benchmarks` 프로젝트와 4096B×100Hz 기준 목표 출력을 추가했다.
  - Phase 4 TCP loopback smoke runner 로 sent/received/drop/leak/latency summary 계측 경계를 검증했다.
  - Phase 4 TCP loopback load runner 로 4096B×100Hz×30초 stdout gate 를 추가했다.
  - `overall-state-2026-06-15.md`의 closed-loop 한계 지적을 상태 문서와 후속 backlog 에 반영했다.
  - Phase 4 open-loop TCP load/backpressure benchmark 를 추가했다.
  - EndpointId 를 실제 TCP/UDP endpoint lifecycle 에 연결하고 active endpoint snapshot collection API 를 추가했다.
  - D058로 `EndpointId`가 stable routing key 가 아니라 runtime diagnostics id 임을 결정했다.
  - D059로 v1 subscription 은 runtime endpoint 수명에 묶고 reconnect rebinding 은 제공하지 않기로 결정했다.
  - D060으로 UDP broker v1 wire/control 을 datagram self-command 와 runtime remote target 정책으로 확정했다.
  - `BrokerSubscriber`에 UDP runtime target 값을 추가하고 `BrokerPublisher`의 TCP/UDP mixed fan-out 분기를 구현했다.
  - `BrokerUdpDatagramHandler`로 UDP datagram self-command 를 Broker routing/fan-out 에 연결했다.
  - `BrokerServer`가 UDP datagram handler 등록과 UDP endpoint bind/stop 수명을 관리하도록 연결했다.
  - `BrokerServer + SaeaTransport` 실제 UDP broker socket loopback 통합 테스트를 추가했다.
  - 마지막 drop 발생 범위 관측성 판단을 D062로 닫았다.
  - Phase 4 benchmark latency SLO gate 판단을 D063으로 닫았다.
  - 백프레셔 기본 정책 정합성 판단을 D064로 닫았다.
  - TCP outbound message boundary 정책을 D065로 닫았다. TCP subscriber outbound 는 length-prefixed frame 으로 보낸다.
  - D065 구현으로 TCP outbound length-prefixed fan-out, 샘플 subscriber 수신, benchmark receive path 를 갱신했다.
  - stalled TCP subscriber stress 통합 테스트로 drop-oldest evict 와 TCP HWM 16 포화 관측을 고정했다.
  - D066으로 v1 drop 관측은 pull snapshot 으로 충분하다고 판단하고 drop log/sampling 은 보류했다.
  - D067로 configurable backpressure/QoS policy surface 는 v1에 추가하지 않기로 결정했다.
  - 2026-06-18 로컬 TCP loopback latency baseline 을 수집했다.
  - D068로 `BrokerServer` diagnostics pass-through API 는 v1에 추가하지 않기로 결정했다.
  - 아직 추적되지 않던 `.claude/review` snapshot 원문을 보존하고,
    `review-status-2026-06-18.md`로 과거 review snapshot 의 현재 상태를 정리했다.
  - D069로 latency hard gate 전에는 반복 baseline artifact 를 먼저 축적하기로 결정했다.
  - 반복 baseline collection 구현 계획을 `docs/superpowers/plans/2026-06-18-repeat-baseline-collection.md`에 작성했다.
  - 반복 baseline collection 계획의 Task 1로 benchmark CLI parser/test seam 을 구현했다.
  - 반복 baseline collection 계획의 Task 2로 `--baseline-suite` parser 확장을 구현했다.
  - 반복 baseline collection 계획의 Task 3으로 fake runner 기반 `BaselineSuiteRunner`를 구현했다.
  - 반복 baseline collection 계획의 Task 4로 `Program` wiring 과 실제 CLI 검증을 완료했다.
  - 다음 후보: 사용자 리뷰 뒤 finding 이 있으면 먼저 반영한다.
  - 반복 baseline command 구현 완료 뒤, `TODOS.md`의 중복 P1 backlog 를 완료된 command 와 후속 정책 항목으로 분리 정리했다.
  - 남아 있던 사용자 설계 문서 변경을 검토하고, 현재 결정과 충돌하지 않도록 상태를 정리했다.
  - 반복 baseline artifact `session-02`를 수집해 closed-loop/open-loop 각 3회 raw JSON 을 추가했다.
  - 반복 baseline artifact `session-03`를 수집해 D069의 최소 3개 baseline session 조건을 채웠다.

## Deferred Backlog

- [ ] `P1_SOON` 반복 baseline artifact 축적 이후 latency/CI 정책을 재판단한다.
  - 무엇이 남았는지: `--baseline-suite <output-dir> [--runs <count>]` command 는 구현 완료됐다.
    아직 남은 것은 raw JSON artifact 를 여러 session 축적한 뒤, summary JSON, Markdown report,
    CI provider workflow, soft warning, p50/p99 hard threshold 를 도입할지 판단하는 정책 작업이다.
  - 왜 defer 되었는지: D069에 따라 hard latency gate 는 단일 로컬 실행값으로 고정하지 않는다.
    같은 장비 또는 같은 CI runner 에서 최소 3개 baseline session 을 먼저 확보해야 false negative 위험을 줄일 수 있다.
  - objective: 반복 수집된 raw JSON artifact 를 근거로 latency regression 판단 방식을 정한다.
    필요하면 summary/Markdown/CI workflow 를 별도 구현 단위로 승격하되, 현재 delivery/drop/leak hard gate 는 유지한다.
  - relevant context: DECISIONS D063/D069,
    `docs/superpowers/specs/2026-06-18-ci-repeat-baseline-policy-design.md`,
    `docs/superpowers/plans/2026-06-18-repeat-baseline-collection.md`,
    `tests/Hps.Benchmarks/Program.cs`, `tests/Hps.Benchmarks/BaselineSuiteRunner.cs`,
    `tests/Hps.Benchmarks/TcpLoopbackRunResult.cs`, `tests/Hps.Benchmarks/TcpLoopbackReportWriter.cs`,
    `docs/benchmarks/baselines/2026-06-18/local-latency-baseline.md`.
  - 관련 파일/범위: `tests/Hps.Benchmarks/`, `docs/benchmarks/baselines/`, 향후 CI script 또는 docs report 위치.
  - 현재 상태: `--baseline-suite`는 closed-loop load 와 open-loop load 를 반복 실행하고
    `load-01.json`, `open-loop-01.json` 형식의 per-run raw JSON 을 생성한다.
    Task 4 CLI smoke 에서 exit code 0, 두 report 의 `schema-version == 1`, `passed == true`를 확인했다.
  - 현재 상태: 2026-06-18 로컬 baseline 에서 `--load` 3회는 p99 879.7~924.1us/TCP HWM 1,
    `--load-open-loop` 3회는 p99 915.9~1005.5us/TCP HWM 2였으며 모든 run 은 drop/leak/payload error 0으로 pass 했다.
  - 현재 상태: `docs/benchmarks/baselines/2026-06-18/session-02/`에 `--baseline-suite --runs 3` 결과를 추가했다.
    closed-loop 3회는 p99 481.6~512.1us/TCP HWM 1,
    open-loop 3회는 p99 564.9~643.3us/TCP HWM 2~3이었고 모든 run 은 drop/leak/payload error 0으로 pass 했다.
  - 현재 상태: `docs/benchmarks/baselines/2026-06-18/session-03/`에 `--baseline-suite --runs 3` 결과를 추가했다.
    closed-loop 3회는 p99 471.0~489.9us/TCP HWM 1,
    open-loop 3회는 p99 502.6~587.8us/TCP HWM 2~3이었고 모든 run 은 drop/leak/payload error 0으로 pass 했다.
  - known blockers/open questions: 같은 장비 기준 최소 3개 session 은 확보됐다. 다만 summary JSON/Markdown report 를 만들지,
    CI workflow 로 올릴지, hard threshold 대신 soft warning 부터 둘지는 별도 판단이 필요하다.
    최초 baseline p99가 session-02/session-03보다 높아 session 간 편차 해석도 함께 필요하다.
  - next step: 사용자 리뷰 뒤 finding 이 없으면 세 session 의 분포를 요약하고,
    soft warning/hard failure 경계를 어떤 기준으로 둘지 설계한다. 구현은 그 설계 이후 별도 단일 작업으로 분리한다.

- [ ] `P3_NICE` 실제 host/metrics surface 가 생기면 server-level diagnostics model 을 설계한다.
  - 무엇이 남았는지: D068로 `BrokerServer` 단순 pass-through diagnostics API 는 v1에 추가하지 않기로 했다.
    다만 실제 운영 host, metrics exporter, HTTP endpoint, 또는 `BrokerServer`만 보유한 consumer 가 생기면
    server-level diagnostics snapshot 이 필요할 수 있다.
  - 왜 defer 되었는지: 현재 서버는 단일 injected `ITransport` 를 감싼 얇은 host 이며, 다중 transport 합산, endpoint registry,
    hosting configuration surface 가 아직 없다. 현재 diagnostics 소비자는 테스트/benchmark 중심이고 transport 인스턴스를 직접 보유한다.
  - objective: 실제 host/운영 API가 구체화된 뒤 nullable pass-through 가 아니라 server-level diagnostics model 이 필요한지 결정한다.
  - relevant context: DECISIONS D041/D042/D056/D062/D066,
    `docs/superpowers/specs/2026-06-18-server-diagnostics-surface-design.md`,
    `src/Hps.Transport/Abstractions/ITransportDiagnostics.cs`,
    `src/Hps.Transport/Abstractions/TransportDiagnosticsSnapshot.cs`, `src/Hps.Server/BrokerServer.cs`.
  - 관련 파일/범위: `src/Hps.Server/`, `src/Hps.Transport/`, host/sample 코드, 관련 tests.
  - 현재 상태: Transport 수명 누적 TCP/UDP drop snapshot 은 public 으로 읽을 수 있고 reset API는 없다.
    active endpoint snapshot 도 optional capability 로 읽을 수 있다. `BrokerServer` public API 는 lifecycle orchestration 에 집중한다.
  - known blockers/open questions: server-level snapshot 에 transport aggregate 만 넣을지 endpoint snapshots, subscription count,
    closed endpoint attribution, drop timestamp 까지 포함할지 정해야 한다. 다중 transport 를 도입할 경우 `EndpointId` namespace 도 정해야 한다.
  - next step: 실제 운영 host 표면이 생기거나 metrics/exporter 요구가 나오면 server-level diagnostics surface 를 별도 설계로 승격한다.

## Completed

- [x] 반복 baseline artifact `session-03`를 수집했다.
  - 범위: `docs/benchmarks/baselines/2026-06-18/session-03/*.json`,
    `docs/benchmarks/baselines/2026-06-18/local-latency-baseline.md`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 결과: `--baseline-suite docs\benchmarks\baselines\2026-06-18\session-03 --runs 3`가 closed-loop 3회와
    open-loop 3회를 모두 pass 로 끝냈다. 모든 run 은 sent/received 3000, dropped 0, payload-errors 0,
    pool-rented 0이었다. closed-loop p99 범위는 471.0~489.9us, open-loop p99 범위는 502.6~587.8us였다.
  - 검증: `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
    baseline suite 실행은 exit code 0과 `baseline-suite-result: pass`를 확인했다.
    `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 144개 통과/실패 0.
    `git diff --check`는 통과했고 CRLF 변환 경고만 있으며 whitespace 오류는 없었다.

- [x] 반복 baseline artifact `session-02`를 수집했다.
  - 범위: `docs/benchmarks/baselines/2026-06-18/session-02/*.json`,
    `docs/benchmarks/baselines/2026-06-18/local-latency-baseline.md`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 결과: `--baseline-suite docs\benchmarks\baselines\2026-06-18\session-02 --runs 3`가 closed-loop 3회와
    open-loop 3회를 모두 pass 로 끝냈다. 모든 run 은 sent/received 3000, dropped 0, payload-errors 0,
    pool-rented 0이었다. closed-loop p99 범위는 481.6~512.1us, open-loop p99 범위는 564.9~643.3us였다.
  - 검증: `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
    `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 144개 통과/실패 0.
    baseline suite 실행은 exit code 0과 `baseline-suite-result: pass`를 확인했다.
    `git diff --check`는 통과했고 CRLF 변환 경고만 있으며 whitespace 오류는 없었다.

- [x] 남아 있던 사용자 설계 문서 변경의 현재 상태를 정리했다.
  - 범위: `AGENTS.md`, `PLAN.md`,
    `docs/superpowers/specs/2026-06-16-interface-server-endpoint-model-design.md`,
    `docs/superpowers/specs/2026-06-18-drop-stress-and-observability-design.md`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 결과: Interface Server 명명 변경과 high-watermark 의미 보정은 현재 설계와 일치하므로 보존했다.
    drop-stress spec 은 D066/D067/D068로 반영 완료된 historical proposal 로 상태를 정리했다.
  - 검증: 문서 전용 변경이므로 build/test 는 실행하지 않았다. `git diff --check`는 통과했고,
    CRLF 변환 경고만 있으며 whitespace 오류는 없었다.

- [x] 반복 baseline command 구현 후 상태 문서 backlog 를 정리했다.
  - 범위: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 결과: `TODOS.md`의 기존 P1 항목이 command 미구현 상태를 계속 설명하던 문제를 정리했다.
    완료된 `--baseline-suite` command 는 Completed 이력에 남기고,
    남은 일은 "baseline artifact 축적 이후 summary/CI/latency threshold 정책 판단"으로 재기술했다.
  - 검증: 문서 전용 변경이므로 build/test 는 실행하지 않았다. `git diff --check`는 통과했고,
    CRLF 변환 경고만 있으며 whitespace 오류는 없었다.

- [x] 반복 baseline collection Task 4로 `Program` wiring 과 실제 CLI 검증을 완료했다.
  - 범위: `tests/Hps.Benchmarks/Program.cs`, root state docs.
  - Red: `--baseline-suite artifacts\baseline-red --runs 1` 실행 시 exit code 2,
    `load-01.json` 미생성, `open-loop-01.json` 미생성을 확인했다.
    parser 는 command 를 인식하지만 `Program` switch 에 execution wiring 이 없던 상태다.
  - Green: `Program`이 `BenchmarkCommand.BaselineSuite`를 `BaselineSuiteRunner`로 연결하고,
    `TcpLoopbackScenarioRunner.RunLoadAsync`, `RunOpenLoopAsync`, `TcpLoopbackReportWriter.Write`를 조립하게 했다.
  - 검증: solution build 경고 0/오류 0, solution tests 통과 144/실패 0.
    CLI smoke 로 `--baseline-suite <temp-output> --runs 1`을 실행해 exit code 0,
    `load-01.json`과 `open-loop-01.json` 생성, 두 JSON 모두 `schema-version == 1`,
    `passed == true`를 확인했다.

- [x] 반복 baseline collection Task 3으로 fake runner 기반 `BaselineSuiteRunner`를 구현했다.
  - 범위: `tests/Hps.Benchmarks/BaselineRunKind.cs`,
    `tests/Hps.Benchmarks/BaselineSuiteRunner.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineSuiteRunnerTests.cs`, root state docs.
  - Red 1: `BaselineSuiteRunner` 타입 부재를 bootstrap 테스트의 `Assert.NotNull()` 실패로 확인했다.
  - Green 1: 타입 seam 만 최소 구현했다.
  - Red 2: 실제 runner 동작 테스트 3개가 `NotImplementedException`으로 실패해
    반복 실행, report path 생성, 실패 집계가 아직 없음을 확인했다.
  - Green: runner 가 load/open-loop 순서로 run 을 실행하고,
    `load-01.json`/`open-loop-01.json` 형식의 per-run path 를 만들며,
    run count 가 100처럼 두 자리보다 크면 index 폭을 run count 자리수로 맞추고,
    하나라도 `Passed == false`이면 suite 실패를 반환하게 했다.
  - 검증: focused runner tests 통과 3, solution build 경고 0/오류 0,
    solution tests 통과 144/실패 0.

- [x] 반복 baseline collection Task 2로 `--baseline-suite` parser 확장을 구현했다.
  - 범위: `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`,
    `tests/Hps.Benchmarks/BenchmarkCommandParser.cs`, `tests/Hps.Benchmarks/Program.cs`, root state docs.
  - Red: baseline suite parser 테스트 3개가 실패해 parser 가 아직 `--baseline-suite`를 인식하지 않음을 확인했다.
  - Green: parser 가 `--baseline-suite <output-dir> [--runs <count>]`를 인식하고,
    기본 run count 3, 명시 run count, `--report` 혼용 usage error 를 반환하게 했다.
    Program usage 에도 새 command 형식을 표시했다.
  - 검증: focused benchmark parser tests 통과 5, solution build 경고 0/오류 0,
    solution tests 통과 141/실패 0.

- [x] 반복 baseline collection Task 1로 benchmark CLI parser/test seam 을 구현했다.
  - 범위: `tests/Hps.Benchmarks.Tests`, `tests/Hps.Benchmarks/BenchmarkCommand.cs`,
    `tests/Hps.Benchmarks/BenchmarkCommandLine.cs`, `tests/Hps.Benchmarks/BenchmarkCommandParser.cs`,
    `tests/Hps.Benchmarks/Properties/AssemblyInfo.cs`, `tests/Hps.Benchmarks/Program.cs`,
    `HighPerformanceSocket.slnx`, root state docs.
  - Red: `BenchmarkCommandParser_TypeExists` bootstrap 테스트가 `Assert.NotNull()` 실패로 parser seam 부재를 확인했다.
  - Green: 기존 `Program` 내부 parsing 을 internal parser 타입으로 분리하고,
    `--load --report`와 `--report` 단독 usage error 계약을 직접 parser 테스트로 고정했다.
  - 검증: focused benchmark tests 통과 2, solution build 경고 0/오류 0,
    solution tests 통과 138/실패 0.

- [x] 반복 baseline collection 구현 계획을 작성했다.
  - 범위: `docs/superpowers/plans/2026-06-18-repeat-baseline-collection.md`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 결과: `--baseline-suite <output-dir> [--runs <count>]` 구현을 parser/test seam, parser 확장,
    suite runner, Program wiring 의 4개 reviewable task 로 쪼갰다.
  - 결정: 다음 구현은 production code 를 바로 넓히지 않고, 먼저 `tests/Hps.Benchmarks.Tests`와 parser extraction 으로
    빠른 TDD 경계를 만든다. latency hard gate, summary JSON, Markdown report, CI provider workflow 는 제외한다.
  - 검증: plan self-review 와 `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.

- [x] CI/반복 baseline 확대 정책을 D069로 닫았다.
  - 범위: `docs/superpowers/specs/2026-06-18-ci-repeat-baseline-policy-design.md`,
    `DECISIONS.md`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 결정: p50/p99, p99 growth ratio, actual-rate, TCP/UDP high-watermark 기반 hard failure threshold 는 아직 추가하지 않는다.
    기존 hard pass/fail 은 planned/sent/received 일치, dropped 0, payload-errors 0, pool-rented 0으로 유지한다.
  - 이유: 2026-06-18 로컬 baseline 은 모두 pass 했지만, 단일 개발 PC의 같은 날 실행값만으로 latency threshold 를 고정하면
    OS scheduling, 백그라운드 부하, JIT/워밍업 상태 때문에 false negative 위험이 크다.
  - 후속: 같은 장비 또는 같은 CI runner 에서 `--load` 3회와 `--load-open-loop` 3회를 포함하는 baseline session 을
    최소 3개 축적한 뒤, soft warning 과 hard failure 경계를 다시 판단한다.
  - 검증: 문서 일관성 확인과 `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.

- [x] 과거 Claude review snapshot 을 현재 HEAD 기준으로 정리했다.
  - 범위: 아직 추적되지 않던 `.claude/review/*.md` snapshot 원문,
    `.claude/review/review-status-2026-06-18.md`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 기준: HEAD `980721c`, `dotnet build HighPerformanceSocket.slnx --no-restore` 경고 0/오류 0,
    `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 전체 136개 통과/실패 0.
  - 결과: 과거 review snapshot 의 H1/H2/H3, G1, endpoint model F1/F3, high-watermark 후속 등은
    해소 또는 명시적 설계 결정으로 재분류했다. 현재 다음 구현을 막는 must-fix/blocker 는 없다.
  - 남은 비차단 후속: CI/장기 baseline 기반 hard SLO 재검토, 실제 host/metrics surface 발생 시 server-level diagnostics,
    topic entry 안전 sweep, 다중 transport `EndpointId` namespace, `EndpointState.Closing/Faulted` 산출 여부,
    샘플 기반 수동 fan-out 확인.

- [x] `BrokerServer` diagnostics pass-through API 필요성을 D068로 닫았다.
  - 범위: `docs/superpowers/specs/2026-06-18-server-diagnostics-surface-design.md`,
    `DECISIONS.md`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 결정: v1에서는 `BrokerServer`에 `GetDiagnostics`, `TryGetTransportDiagnostics`, `GetEndpointSnapshots` 같은
    convenience API 를 추가하지 않는다.
  - 이유: 현재 `BrokerServer`는 단일 injected transport 를 조립하는 얇은 host 이며,
    diagnostics 소비자는 테스트/benchmark 중심으로 transport 인스턴스를 직접 보유한다.
    지금 pass-through API 를 추가하면 nullable capability, endpoint snapshot 포함 여부, 다중 transport 합산,
    `EndpointId` namespace 같은 결정을 앞당긴다.
  - 후속: 실제 host/metrics/exporter 가 생기거나 `BrokerServer`만 보유한 소비자가 diagnostics 를 읽어야 하면
    server-level diagnostics model 을 별도 설계로 승격한다.

- [x] 2026-06-18 로컬 TCP loopback latency baseline 을 수집했다.
  - 범위: `docs/benchmarks/baselines/2026-06-18/*.json`,
    `docs/benchmarks/baselines/2026-06-18/local-latency-baseline.md`, root state docs.
  - 실행: `dotnet build HighPerformanceSocket.slnx --no-restore` 후, `--load --report` 3회와
    `--load-open-loop --report` 3회를 `--no-build`로 실행했다.
  - 결과: closed-loop 3회는 모두 sent/received 3000, dropped 0, payload-errors 0, pool-rented 0,
    TCP HWM 1, p99 879.7~924.1us 로 pass 했다. open-loop 3회는 모두 sent/received 3000, dropped 0,
    payload-errors 0, pool-rented 0, TCP HWM 2, p99 915.9~1005.5us 로 pass 했다.
  - 결정: 새 hard SLO는 정하지 않았다. 이 결과는 현재 개발 PC의 참고 baseline 이며,
    날짜를 달리한 반복 실행이나 CI 전용 baseline 이 쌓인 뒤 threshold 승격을 다시 판단한다.

- [x] configurable backpressure/QoS policy surface 필요성을 D067로 닫았다.
  - 범위: `docs/superpowers/specs/2026-06-18-backpressure-qos-policy-surface-design.md`,
    `DECISIONS.md`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 결정: v1 TCP/UDP send queue 는 capacity 16 bounded drop-oldest 를 public 설정 없이 유지한다.
    `BackpressurePolicy` enum, pending capacity option, topic/endpoint 별 QoS, disconnect/reject 기본 정책은 추가하지 않는다.
  - 이유: D066으로 drop-oldest 는 실제 stalled subscriber 경로에서 fire 하며 기존 diagnostics 로 관측 가능함이 확인됐다.
    disconnect/reject/reliable/durable 정책은 구독 정리, 재구독, publisher 실패 응답, ack/retry/history 저장 등
    protocol/control-plane 결정을 함께 요구하므로 v1 transport queue 옵션으로 넣기에는 범위가 크다.
  - 후속: 손실 불가 topic, endpoint 별 최신성/신뢰성 혼합 운영, pending capacity 16의 반복 benchmark 부적합 근거,
    stable endpoint identity/reconnect rebinding 도입이 확인되면 별도 설계 단위로 재등록한다.
  - 검증: 문서 일관성 확인과 `git diff --check`로 검증한다.

- [x] stalled TCP subscriber stress 로 drop-oldest evict 경로를 검증했다.
  - 범위: `tests/Hps.Server.Tests/BrokerServerTests.cs`, `DECISIONS.md`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 테스트: subscriber 가 `SUBSCRIBE` 후 socket 을 읽지 않도록 정체시키고, publisher 가 큰 payload 를 반복 발행해
    실제 SAEA TCP send loop 의 OS send buffer 포화와 Transport pending send queue 포화를 유도한다.
  - 단언: `TcpDroppedPendingSendCount > 0`, `TcpPendingSendQueueHighWatermark == 16`,
    UDP drop/HWM 0, 종료 후 `PinnedBlockMemoryPool.RentedCount == 0`.
  - 결과: 기존 production code 에서 신규 stress 테스트가 바로 통과했다. 즉 D012 drop-oldest 구현은 이미 동작했고,
    이번 단위는 그동안 benchmark 로 fire 하지 못한 경로를 end-to-end 회귀 테스트로 고정한 것이다.
  - 결정: D066으로 v1 drop 관측은 pull snapshot 으로 충분하다고 판단하고 drop log/sampling 은 보류했다.
    Server convenience diagnostics API 는 실제 host surface 가 더 구체화될 때 재검토한다.
  - 검증: 신규 focused test 통과, Server tests 통과 12, solution build 경고 0/오류 0,
    solution tests 통과 136/실패 0, `git diff --check` 통과(CRLF 변환 경고만 존재).

- [x] TCP outbound length-prefixed fan-out 을 구현했다.
  - 범위: `src/Hps.Transport/Abstractions/TransportSendBuffer.cs`,
    `src/Hps.Broker/BrokerSubscriber.cs`, `src/Hps.Transport/Saea/SaeaTransport.cs`,
    `tests/Hps.Server.Tests/BrokerServerTests.cs`, `samples/Shared/SampleTcpFrames.cs`,
    `samples/Hps.Sample.Subscriber/Program.cs`, `tests/Hps.Benchmarks/TcpLoopbackScenarioRunner.cs`,
    root state docs.
  - Red: 서로 다른 길이의 연속 TCP fan-out 메시지를 subscriber 가 length-prefixed frame 으로 읽는 테스트를 추가했고,
    raw outbound 구현에서 첫 payload 일부 `[170, 187, 204]`만 수신되어 실패함을 확인했다.
  - 구현: TCP subscriber target 은 `TransportSendBuffer.WithLengthPrefix()`로 logical send item 을 만들고,
    UDP target 은 기존 raw datagram send 를 유지한다. SAEA send loop 는 연결당 pinned 4바이트 header buffer 를 재사용해
    length prefix 를 먼저 보낸 뒤 기존 shared payload slice 를 전송한다.
  - 소유권: header 는 payload buffer 로 합치지 않고 metadata 로만 유지한다. drop-oldest, close drain, in-flight unwind 는
    기존 payload `RefCountedBuffer` transport ref 1개를 그대로 Release 한다.
  - 후속: RIO/io_uring backend 에서는 같은 logical send item 을 scatter/gather send 로 더 최적화할 수 있다.
  - 검증: Red focused 실패 확인, Green focused 통과, Server tests 통과 11, solution build 경고 0/오류 0.
    solution test 통과 135/실패 0, benchmark smoke 통과(sent 8, received 8, dropped 0, pool-rented 0).
    상태 문서 연결 확인 통과, `git diff --check` 통과(CRLF 변환 경고만 존재).

- [x] TCP outbound message boundary 정책을 D065로 닫았다.
  - 범위: `docs/superpowers/specs/2026-06-18-tcp-outbound-framing-policy-design.md`,
    `DECISIONS.md`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 결정: broker->TCP subscriber outbound 는 inbound 와 같은 `4-byte big-endian length prefix + payload` frame 으로 보낸다.
    UDP outbound 는 기존대로 `1 datagram = 1 message`를 유지한다.
  - 이유: TCP stream 은 message boundary 를 보존하지 않으므로 raw payload outbound 는 가변 길이 연속 메시지에서 깨진다.
    다만 header 를 붙이기 위해 header+payload 를 구독자별 새 버퍼로 합치는 방식은 fan-out payload 무복사 불변식을 깨므로,
    다음 구현은 header metadata 와 shared payload slice 를 하나의 logical framed/composite send item 으로 다뤄야 한다.
  - 후속: 실제 TCP outbound framed send 구현은 별도 `P1_SOON` TDD 항목으로 남겼다.
  - 검증: 상태 문서 연결 확인과 `git diff --check`로 검증한다. 문서 전용 결정 단위라 production code/test 는 변경하지 않는다.

- [x] 백프레셔 기본 정책 정합성 판단을 D064로 닫았다.
  - 범위: `PLAN.md`, `DECISIONS.md`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 결정: v1 TCP/UDP transport send queue 기본 정책은 bounded drop-oldest 로 유지한다.
    TCP `TransportConnection`과 UDP `SaeaUdpEndpoint`는 capacity 16 queue 가 가득 찬 상태에서 새 send 를 수락하면
    가장 오래된 pending 항목을 evict 하고 evict 된 `RefCountedBuffer` transport 소유 ref 를 정확히 1회 Release 한다.
  - 이유: 현재 구현과 D039/D040/D041/D042 관측성 경계가 이미 이 정책으로 검증돼 있다.
    disconnect/reject 기본 정책은 reconnect, 구독 복구, endpoint 별 QoS, host 설정 표면을 함께 요구하므로 v1 기본값으로 넣지 않는다.
  - 후속: configurable backpressure/QoS policy surface 는 별도 `P2_LATER` 항목으로 남겼다.
  - 검증: 상태 문서 연결 확인 통과, `git diff --check` 통과(CRLF 변환 경고만 존재).

- [x] Phase 4 benchmark latency SLO gate 판단을 D063으로 닫았다.
  - 범위: `DECISIONS.md`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 결정: p50/p99 latency, p99 growth ratio, actual-rate, TCP/UDP high-watermark 를 hard pass/fail 조건으로 승격하지 않는다.
    현재 pass/fail 은 planned/sent/received 일치, dropped 0, payload-errors 0, pool-rented 0으로 유지한다.
  - 이유: latency 값은 개발 PC, OS scheduling, 백그라운드 부하, JIT/워밍업 상태에 민감해 단일 로컬 실행값으로
    절대 threshold 를 고정하면 false negative 위험이 크다.
  - 검증: `--load --report`는 sent/received 3000, dropped 0, pool-rented 0, TCP HWM 1, p99 720.9us 로 pass 했다.
    `--load-open-loop --report`는 sent/received 3000, dropped 0, pool-rented 0, TCP HWM 3, p99 527.7us 로 pass 했다.
    상태 문서 연결 확인 통과, `git diff --check` 통과(CRLF 변환 경고만 존재).

- [x] 마지막 drop 발생 범위 관측성 판단을 D062로 닫았다.
  - 범위: `DECISIONS.md`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 결정: v1 diagnostics 에 `last-drop` 전용 timestamp/id 필드를 추가하지 않는다.
    transport kind 범위는 `TransportDiagnosticsSnapshot`의 TCP/UDP 누적 drop count 로 보고,
    active endpoint 범위는 `ITransportEndpointDiagnostics.GetEndpointSnapshots()`의
    `EndpointSnapshot.DroppedPendingSendCount`로 본다.
  - 이유: 단일 마지막 drop 값은 여러 endpoint 의 동시 drop 에서 이전 사건을 덮어쓰고,
    timestamp/ordering 의미를 새로 정해야 하며 hot path metadata 갱신 비용을 추가한다.
    현재 운영 질문은 마지막 1건의 시각보다 어느 kind/endpoint 에서 drop 이 누적되는지에 가깝다.
  - 후속: closed endpoint attribution, drop timestamp, log/sampling, Server convenience diagnostics API 는
    필요성이 확인될 때 별도 후속으로 다룬다. 당시 다음 후보였던 Phase 4 benchmark latency SLO gate 판단은 D063으로 닫혔다.
  - 검증: 문서 연결 확인 통과, `git diff --check` 통과(CRLF 변환 경고만 존재).

- [x] `BrokerServer + SaeaTransport` UDP broker socket loopback 통합 테스트를 추가했다.
  - 범위: `tests/Hps.Server.Tests/BrokerServerTests.cs`, root state docs.
  - 테스트: 실제 UDP subscriber socket 이 `SUBSCRIBE alpha` datagram 을 server UDP endpoint 로 보내고,
    실제 UDP publisher socket 이 `PUBLISH alpha <payload>` datagram 을 보내면 subscriber socket 이 raw payload 만 받는 end-to-end 경계를 검증한다.
  - 경계: UDP protocol 에 public subscribe ack 가 아직 없으므로 publish 전 `SubscriptionTable.CountSubscribers` white-box 대기로 등록 race 를 제거했다.
  - 결과: 테스트 추가 직후 기존 `BrokerServer`/`SaeaTransport`/`BrokerUdpDatagramHandler` 구현에서 바로 통과했다.
    생산 코드 결선 누락은 드러나지 않았으므로 production code 변경은 없다.
  - 검증: focused UDP loopback 테스트 통과 1, Server tests 통과 10, solution build 경고 0/오류 0,
    solution tests 통과 134/실패 0, `git diff --check` 통과(CRLF 변환 경고만 존재).

- [x] BrokerServer UDP bind wiring 을 구현했다.
  - 범위: `src/Hps.Server/BrokerServer.cs`, `tests/Hps.Server.Tests/BrokerServerTests.cs`, root state docs.
  - Red: UDP host API 계약 테스트가 `UdpLocalEndPoint` 부재로 실패했다.
  - Red: UDP behavior focused 테스트 3개가 `StartUdpAsync` stub 에서 handler 등록/bind/stop 수명을 수행하지 않아 실패했다.
  - 구현: `BrokerServer.StartUdpAsync`를 추가해 `BrokerUdpDatagramHandler`를 Transport 에 등록하고 `BindUdpAsync` 결과 endpoint 를 보관한다.
  - 수명: TCP listener 와 UDP endpoint 는 독립 시작 가능하지만 하나의 `ITransport.StartAsync`/`StopAsync` 수명 안에서 공유된다.
    `StopAsync`는 TCP listener 와 UDP endpoint 를 함께 닫고 dispose 한다.
  - 후속: 실제 `SaeaTransport` UDP socket loopback 에서 `SUBSCRIBE`/`PUBLISH` datagram fan-out 을 검증해야 한다.
  - 검증: API Red 실패 1, API Green 통과 1, behavior Red 실패 3, focused Green 통과 3,
    Server tests 통과 9, solution build 경고 0/오류 0, solution test 통과 133/실패 0.

- [x] UDP broker datagram handler 를 구현했다.
  - 범위: `src/Hps.Broker/BrokerUdpDatagramHandler.cs`, `src/Hps.Broker/SubscriptionTable.cs`,
    `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`, `tests/Hps.Broker.Tests/TestDoubles/FakeUdpEndpoint.cs`,
    root state docs.
  - Red: handler 계약 테스트가 `BrokerUdpDatagramHandler` 부재로 실패했다.
  - Red: UDP subscribe/unsubscribe/publish/malformed drop/endpoint close cleanup 행위 테스트 5개가 빈 handler 에서 실패했다.
  - 구현: datagram payload 를 기존 command decoder 로 해석해 `BrokerSubscriber.ForUdp(endpoint, remoteEndPoint)` target 을
    subscribe/unsubscribe 하고, publish 는 datagram buffer 의 payload range 를 `BrokerPublisher`로 넘긴다.
    malformed UDP command 는 endpoint 를 닫지 않고 datagram 만 release/drop 한다.
  - cleanup: `SubscriptionTable.UnsubscribeAll(IUdpEndpoint)`를 추가해 endpoint close notification 때 같은 local UDP endpoint 의
    모든 remote 구독을 제거한다. D008 정책대로 빈 topic entry 는 제거하지 않는다.
  - 후속: `BrokerServer`가 UDP datagram handler 를 등록하고 bind endpoint 수명을 관리하는 host wiring 이 남았다.
  - 검증: focused Red 실패 1, focused Green 통과 1, behavior Red 실패 5/통과 1, focused Green 통과 6,
    Broker tests 통과 30, solution build 경고 0/오류 0, solution test 통과 129/실패 0.

- [x] protocol command grammar 에 `UNSUBSCRIBE <topic>`를 추가했다.
  - 범위: `src/Hps.Protocol/TcpCommandKind.cs`, `src/Hps.Protocol/TcpCommand.cs`,
    `src/Hps.Protocol/TcpCommandDecoder.cs`, `src/Hps.Broker/BrokerTcpFrameHandler.cs`,
    `tests/Hps.Protocol.Tests/TcpCommandDecoderTests.cs`, `tests/Hps.Broker.Tests/BrokerTcpFrameHandlerTests.cs`,
    root state docs.
  - Red: enum 계약 테스트가 `Unsubscribe` 미존재로 실패했다.
  - Red: `UNSUBSCRIBE alpha` decode 와 TCP handler unsubscribe 동작 테스트가 기존 decoder/handler 에서 실패했다.
  - 구현: `UNSUBSCRIBE`를 topic-only command 로 decode 하고, malformed unsubscribe topic 은 기존 protocol error 경로로 보고한다.
    TCP handler 는 `SubscriptionTable.Unsubscribe(topic, connection)`만 수행하며 connection 을 닫지 않는다.
  - 후속: UDP broker datagram handler 에서 같은 command grammar 를 datagram self-command 로 연결해야 한다.
  - 검증: Protocol tests 통과 33, Broker tests 통과 24, solution build 경고 0/오류 0, solution test 통과 123/실패 0.

- [x] `BrokerSubscriber`에 UDP runtime target 값을 추가하고 TCP/UDP mixed fan-out 분기를 구현했다.
  - 범위: `src/Hps.Broker/BrokerSubscriber.cs`, `tests/Hps.Broker.Tests/BrokerRoutingTests.cs`,
    `tests/Hps.Broker.Tests/BrokerPublisherTests.cs`, `tests/Hps.Broker.Tests/TestDoubles/FakeTransport.cs`,
    `tests/Hps.Broker.Tests/TestDoubles/FakeUdpEndpoint.cs`, root state docs.
  - Red: `ForUdp(IUdpEndpoint, EndPoint)` factory 부재를 assertion 실패 1건으로 확인했다.
  - Red: UDP runtime target duplicate 제거 부재와 publisher UDP send 분기 부재를 실패 2건으로 확인했다.
  - 구현: `BrokerSubscriber.ForUdp`를 추가하고, UDP equality/hash 를 local endpoint reference + remote `EndPoint` 값으로 고정했다.
    `BrokerSubscriber.TrySend`는 UDP target 에 대해 `ITransport.TrySendTo`를 호출한다.
  - 후속: UDP datagram handler 전에 `UNSUBSCRIBE <topic>` command grammar 를 추가해야 한다.
  - 검증: focused Red 실패 1, focused Red 실패 2, focused Green 통과 3, Broker tests 통과 23,
    solution build 경고 0/오류 0, solution test 통과 117/실패 0.

- [x] UDP broker v1 runtime target wire/control 정책을 확정했다.
  - 범위: `docs/superpowers/specs/2026-06-16-udp-broker-runtime-target-wire-control-design.md`, `DECISIONS.md`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 결정: UDP v1은 별도 TCP control plane 등록이 아니라 datagram self-command 를 사용한다.
    runtime target 은 `(IUdpEndpoint localEndpoint, EndPoint remoteEndPoint)`이며, stable id, `EndpointId`, `REGISTER`,
    reconnect subscription transfer 는 쓰지 않는다.
  - command set: `SUBSCRIBE <topic>`, `UNSUBSCRIBE <topic>`, `PUBLISH <topic> <payload>`.
  - cleanup: explicit `UNSUBSCRIBE`와 UDP endpoint close cleanup 만 v1에 포함하고, idle expiry 는 후속으로 둔다.
  - 후속: 첫 구현 단위는 `BrokerSubscriber` UDP runtime target 값과 TCP/UDP mixed fan-out 분기로 쪼갰다.
  - 검증: 문서 연결은 `rg`로 확인하고, whitespace 는 `git diff --check`로 검증한다.

- [x] UDP pub/sub v1 범위 판단을 runtime target wire/control 설계 단위로 승격했다.
  - 기존 `P2_LATER` 항목은 UDP broker 를 v1에 포함할지 묻는 범위 판단이었다.
  - D059 이후 v1은 stable identity 없이 runtime target subscription 으로 가는 방향이 정해졌으므로,
    실제 남은 판단은 UDP command/control 과 stale runtime target 정책이다.
  - 후속: `P1_SOON` UDP broker v1 runtime target wire/control 설계 항목에서 이어간다.

- [x] v1 subscription 을 runtime endpoint 수명 기반으로 확정하고 reconnect rebinding 을 v1 범위 밖으로 뺐다.
  - 범위: `docs/superpowers/specs/2026-06-16-endpoint-identity-policy.md`, `DECISIONS.md`, `CURRENT_PLAN.md`,
    `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 결정: TCP subscription 은 현재 `IConnection` 수명에 묶고, reconnect client 는 다시 `SUBSCRIBE` 해야 한다.
    UDP broker 를 v1에 포함하더라도 stable subscriber identity 없이 bind 된 UDP endpoint 와 remote `EndPoint` 조합을 runtime target 으로 다룬다.
  - 근거: stable identity 는 handshake/config/host API, duplicate id, reconnect transfer, UDP stale 정리까지 함께 결정해야 하므로
    TCP/UDP runtime fan-out 경계보다 뒤로 미룬다.
  - 후속: UDP broker v1 runtime target wire/control 설계를 새 `P1_SOON` 항목으로 올렸다.
  - 검증: 문서 연결은 `rg`로 확인하고, whitespace 는 `git diff --check`로 검증한다.

- [x] Endpoint identity 정책을 문서화해 `EndpointId`의 의미를 runtime diagnostics id 로 고정했다.
  - 범위: `docs/superpowers/specs/2026-06-16-endpoint-identity-policy.md`, `DECISIONS.md`, `CURRENT_PLAN.md`,
    `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 근거: `.claude/review/2026-06-16-endpoint-model-cross-verification.md`의 F1은 `EndpointId`가 아직 routing key 가 아니며
    reconnect stable identity 를 보장하지 않는다고 지적했다.
  - 결정: `EndpointId`는 Transport 수명 안에서 살아 있는 endpoint 를 관측하는 transient diagnostics id 로 유지한다.
    Broker stable routing/reconnect binding 은 explicit control-plane/configuration/host identity 가 생기기 전까지 제공하지 않는다.
  - 후속: stable subscriber identity source, duplicate/reconnect 처리, TCP/UDP command wire format 은 새 `P1_SOON` 항목으로 분리했다.
    이후 D059에서 v1은 runtime endpoint 수명 기반으로 가고 reconnect rebinding 은 제공하지 않기로 결정했다.
  - 검증: 문서 연결은 `rg`로 확인하고, whitespace 는 `git diff --check`로 검증한다.

- [x] Broker subscription value 를 TCP endpoint-target 값으로 1차 전환했다.
  - 범위: `src/Hps.Broker/BrokerSubscriber.cs`, `src/Hps.Broker/SubscriptionTable.cs`,
    `src/Hps.Broker/BrokerPublisher.cs`, `tests/Hps.Broker.Tests/BrokerRoutingTests.cs`, root state docs.
  - Red: `BrokerSubscriber` snapshot 계약 테스트 2건을 먼저 추가했고 기존 구현에서 type 부재 `Assert.NotNull` 실패로 확인했다.
  - 구현: `SubscriptionTable` 내부 구독자 key 를 `BrokerSubscriber` 로 바꾸고, 기존 TCP `IConnection` overload 는 compatibility API 로 유지했다.
    `BrokerPublisher`는 `BrokerSubscriber[]` snapshot 으로 fan-out 한다.
  - 후속: UDP broker v1 wire/control 과 UDP send target 값은 남아 있다. Stable endpoint id/reconnect binding 은 이후 D059에서
    v1 범위 밖으로 결정했다.
  - 검증: focused Red 실패 2, focused Green 통과 2, Broker tests 통과 20, solution build 경고 0/오류 0, solution test 통과 114.

- [x] EndpointId 를 실제 TCP/UDP endpoint lifecycle 에 연결하고 snapshot collection API 를 추가했다.
  - 범위: `src/Hps.Transport/Abstractions/ITransportEndpointDiagnostics.cs`,
    `src/Hps.Transport/Runtime/TransportBase.cs`, `src/Hps.Transport/Runtime/TransportConnection.cs`,
    `src/Hps.Transport/Saea/SaeaTransport.cs`, `src/Hps.Transport/Saea/SaeaUdpEndpoint.cs`,
    `tests/Hps.Transport.Tests/Contracts/TransportContractTests.cs`, `tests/Hps.Transport.Tests/Runtime/TransportSendQueueTests.cs`,
    `tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`, root state docs.
  - Red: endpoint diagnostics capability, TCP connection snapshot, SAEA TCP/UDP snapshot collection 테스트 3건을 먼저 추가했고
    기존 구현에서 `Assert.NotNull` 기반 assertion 실패 3건으로 Red 를 확인했다.
  - 구현: `ITransportEndpointDiagnostics.GetEndpointSnapshots()` 선택적 capability 를 추가하고,
    `TransportBase`가 transient `EndpointId`를 발급하도록 했다. TCP `TransportConnection`과 UDP `SaeaUdpEndpoint`는
    현재 pending depth, endpoint 수명 high-watermark, drop count, open/closed 상태를 `EndpointSnapshot`으로 만든다.
  - 구현: `SaeaTransport`는 tracking 중인 TCP connection 과 UDP endpoint 를 lock 안에서 값 배열로 복사한 뒤,
    lock 밖에서 snapshot 을 생성한다. 닫힌 endpoint 는 기존 unregister 경로로 tracking 목록에서 제거된다.
  - 후속: Broker subscription value endpoint-target 전환은 D057에서 완료했고, stable endpoint id/reconnect binding 은 D059에서
    v1 범위 밖으로 결정했다.
  - 검증: focused Red 실패 3, focused Green 통과 3. Transport 전체 통과 43, solution build 경고 0/오류 0,
    solution test 통과 112/실패 0/건너뜀 0. `git diff --check` whitespace 오류 없음.

- [x] EndpointId 와 endpoint snapshot 최소 public 계약을 추가했다.
  - 범위: `src/Hps.Transport/Abstractions/EndpointId.cs`, `EndpointTransportKind.cs`, `EndpointState.cs`, `EndpointSnapshot.cs`,
    `tests/Hps.Transport.Tests/Contracts/TransportContractTests.cs`, root state docs.
  - Red: `EndpointSnapshot_Contract_ExposesStableIdentityAndSendDiagnostics`가 기존 구현에서 `EndpointId` 타입 부재로 실패했다.
  - 구현: connection 객체 참조를 직접 노출하지 않는 `EndpointId` 값 타입, TCP/UDP transport kind, endpoint state,
    pending send count/high-watermark/drop count 를 담는 immutable snapshot 계약을 추가했다.
  - 후속: endpoint id 발급, TCP/UDP lifecycle 등록, snapshot collection API, Broker subscription value 전환은 별도 P1 항목으로 남겼다.
  - 검증: focused Red 실패 1, Green 통과 1. Transport 전체 통과 40. 솔루션 build 경고 0/오류 0,
    솔루션 테스트 통과 109/실패 0/건너뜀 0, `git diff --check` whitespace 오류 없음.

- [x] TCP/UDP send queue high-watermark diagnostics 를 public snapshot 과 benchmark report 에 연결했다.
  - 범위: `src/Hps.Transport/Abstractions/TransportDiagnosticsSnapshot.cs`, `src/Hps.Transport/Runtime/TransportBase.cs`,
    `src/Hps.Transport/Runtime/TransportConnection.cs`, `src/Hps.Transport/Saea/SaeaUdpEndpoint.cs`,
    `tests/Hps.Transport.Tests/`, `tests/Hps.Benchmarks/`.
  - 결과: Transport lifetime 기준 TCP/UDP kind 별 pending send queue high-watermark 를 기록하고 stdout/JSON report 에
    `tcp-pending-send-queue-high-watermark`, `udp-pending-send-queue-high-watermark` 로 남긴다.
  - 비고: high-watermark 는 endpoint identity 가 아니라 TCP/UDP transport kind 별 max pending depth 이며,
    capacity 16에서 포화되므로 drop count 와 함께 해석한다.
  - 근거: `22591b5`에서 high-watermark tracking 을 추가했고, `db8984f`에서 benchmark stdout/JSON report 연결을 추가했다.
  - 검증: 실제 구현 존재는 `rg`로 확인했고, 이번 문서 동기화 단위는 `git diff --check`로 검증한다.

- [x] Interface Server endpoint model 설계를 문서화했다.
  - 범위: `docs/superpowers/specs/2026-06-16-interface-server-endpoint-model-design.md`, `CURRENT_PLAN.md`,
    `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - 결과: 최종 목표를 외부 ingress 를 topic/data type 으로 받아 TCP/UDP endpoint 로 발행하는 Interface Server 로 재정렬했다.
  - 결정: latency SLO gate 보다 send-side endpoint 관측성을 먼저 보강하고, 다음 구현 후보를 TCP/UDP send queue high-watermark diagnostics 로 잡았다.
  - 검증: 문서 전용 변경이므로 build/test 는 실행하지 않고 state 문서 연결 확인과 `git diff --check`로 검증한다.

- [x] Phase 4 benchmark JSON report persistence 를 추가했다.
  - 범위: `tests/Hps.Benchmarks`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `--smoke --report <path>`는 기존 구현에서 `Program.Main`의 `args.Length == 1` 분기를 타지 못하고
    BenchmarkDotNet fallback 으로 흘러가 `smoke`/`report` unknown option 이 출력됐으며 report 파일도 생성되지 않았다.
  - 구현: `Program`의 benchmark runner CLI parser 를 다중 인자 옵션 구조로 확장하고, `--smoke`, `--load`, `--load-open-loop`에
    선택적 `--report <path>`를 추가했다.
  - 구현: `TcpLoopbackReportWriter`를 추가해 세 runner 가 같은 `TcpLoopbackRunResult` 기반 JSON schema 를 항상 기록하게 했다.
    기존 파일은 덮어쓰고 상위 디렉터리는 자동 생성한다.
  - 후속: latency SLO threshold, Markdown report, report history, queue depth diagnostics 는 별도 backlog 로 유지한다.

- [x] Phase 4 open-loop TCP load/backpressure benchmark 를 추가했다.
  - 범위: `tests/Hps.Benchmarks`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `BenchmarkTargets`.
  - Red: `--load-open-loop` 출력 검증은 기존 구현에서 BenchmarkDotNet unknown option 으로 처리되어 `open-loop-result:`가 출력되지 않아 실패했다.
  - 구현: `Program --load-open-loop`와 `TcpLoopbackScenarioRunner.RunOpenLoopAsync()`를 추가했다.
  - 구현: open-loop runner 는 subscriber receive task 를 먼저 시작하고, publisher loop 는 subscriber 수신 완료를 기다리지 않고
    100Hz schedule 에 맞춰 4096B payload 3000개를 전송한다.
  - 구현: payload 내부에 timestamp 와 sequence 를 넣어 수신 순서/무결성을 `payload-errors`로 관측하고,
    first-half/second-half p99 와 p99 growth ratio 로 지연 증가 추세를 출력한다.
  - 검증: focused open-loop 는 `open-loop-result: pass`, planned/sent/received 3000, dropped 0, payload-errors 0,
    pool-rented 0, actual-rate-hz 99.9, p50 221.6us, p99 867.6us, first-half p99 873.3us,
    second-half p99 850.3us, p99 growth ratio 0.97로 통과했다. closed-loop `--load`, `--smoke`, `--target` 회귀도 통과했다.
    solution build 는 경고 0/오류 0으로 통과했고, 솔루션 전체 테스트는 통과 106, 실패 0, 건너뜀 0으로 통과했다.
    `git diff --check`는 whitespace 오류 없이 통과했다.

- [x] `overall-state-2026-06-15.md`의 closed-loop benchmark 한계 검토를 상태 문서에 반영했다.
  - 범위: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - 검토 반영: `--load`는 4096B×100Hz×30초 closed-loop baseline 으로 유지하되, subscriber 수신 뒤 다음 publish 로 넘어가므로
    queue depth 증가나 drop-oldest/backpressure 경로를 검증하지 않는다고 명시했다.
  - 후속: open-loop TCP load/backpressure benchmark 를 P1 Deferred Backlog 로 추가했다. 이 후속은 publish loop 와 receive loop 를
    분리해 queue backlog, dropped count, latency 증가 추세를 관측하는 별도 작업 단위다.
  - 검증: 문서 전용 변경이므로 build/test 는 실행하지 않고 `rg` 상태 문서 연결 확인과 `git diff --check`로 검증한다.

- [x] Phase 4 TCP loopback load runner 를 추가했다.
  - 범위: `tests/Hps.Benchmarks`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - Red: `--load` 출력 검증은 기존 구현에서 BenchmarkDotNet unknown option 으로 처리되어 `load-result:`가 출력되지 않아 실패했다.
  - 구현: `Program --load`와 `TcpLoopbackScenarioRunner.RunLoadAsync()`를 추가하고, 기존 smoke runner 를 같은 scenario runner 로 통합했다.
  - 구현: load 는 실제 `BrokerServer + SaeaTransport` loopback 과 TCP subscriber/publisher socket 을 사용해
    4096B payload 3000개를 100Hz pacing 으로 약 30초 전송한다.
  - 구현: pass/fail 은 sent==planned==received, dropped 0, pool-rented 0으로 판정하고, actual-rate/p50/p99 latency 는
    stdout summary 로 출력한다. latency threshold 와 파일 report 는 후속 항목으로 남겼다.
  - 검증: focused load 는 `load-result: pass`, planned/sent/received 3000, dropped 0, pool-rented 0,
    actual-rate-hz 99.9, p50 205.9us, p99 799.0us 로 통과했다. benchmark project build 와 solution build 는
    경고 0/오류 0으로 통과했고, 솔루션 전체 테스트는 통과 106, 실패 0, 건너뜀 0으로 통과했다.

- [x] Phase 4 TCP loopback smoke runner 를 추가했다.
  - 범위: `tests/Hps.Benchmarks`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - Red: `--smoke` 출력 검증은 기존 구현에서 BenchmarkDotNet unknown option 으로 처리되어 `smoke-result:`가 출력되지 않아 실패했다.
    최초 시도는 sandbox 네트워크 restore 차단으로 실패해 권한 요청 후 Red 를 재확인했다.
  - 구현: `Program --smoke`, `TcpLoopbackSmokeRunner`, `TcpLoopbackSmokeResult`를 추가했다.
  - 구현: smoke 는 실제 `BrokerServer + SaeaTransport` loopback 과 TCP subscriber/publisher socket 을 사용해
    4096B payload 8개를 보내고 수신 원문, sent/received, drop count, pool rented count, p50/p99 latency sample 을 검증한다.
  - 검증: focused smoke 는 `smoke-result: pass`, sent 8, received 8, dropped 0, pool-rented 0으로 통과했다.
    benchmark project build 경고 0/오류 0, solution build 경고 0/오류 0, 솔루션 전체 테스트 통과 106, 실패 0,
    건너뜀 0. `git diff --check` 통과. 병렬 build/test 시 obj lock 충돌이 있어 직렬 재실행으로 확인했다.

- [x] Phase 4 benchmark scaffold 와 4096B×100Hz 기준 목표 출력을 추가했다.
  - 범위: `tests/Hps.Benchmarks`, `HighPerformanceSocket.slnx`, `.gitignore`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `dotnet build tests\Hps.Benchmarks\Hps.Benchmarks.csproj`가 프로젝트 파일 부재로 실패했다.
  - 구현: BenchmarkDotNet 기반 console project 를 추가하고, `BenchmarkTargets`에 SAEA TCP loopback baseline 목표값을 고정했다.
  - 구현: `--target` 명령은 4096B, 100Hz, 30초, planned 3000 messages, dropped 0/누수 0/p50/p99 report gate 를 출력한다.
  - 구현: 첫 microbench 로 `PinnedBlockMemoryPoolBenchmarks`의 `RentCounted + Release`를 추가했다.
  - 구현: BenchmarkDotNet 기본 artifact 폴더는 `.gitignore`에 추가해 임시 측정 산출물이 의도 없이 커밋되지 않게 했다.
  - 검증: benchmark project build 경고 0/오류 0, `--target` 실행 성공, solution build 경고 0/오류 0.
    솔루션 전체 테스트 통과 106, 실패 0, 건너뜀 0. `git diff --check` 통과.

- [x] D010 TCP frame assembler 랜덤 적대적 fuzz 를 영구 회귀 테스트로 추가했다.
  - 범위: `tests/Hps.Protocol.Tests/TcpFrameAssemblerTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 테스트: 고정 seed 4개로 frame 길이와 receive chunk 길이를 바꾸며 header 1바이트 분할, 0바이트 payload,
    max payload, 한 chunk 안의 다중 frame 을 함께 검증한다.
  - 결과: 기존 `TcpFrameAssembler` 구현이 랜덤 적대적 분할 케이스를 즉시 통과해 production code 수정은 없었다.
  - 검증: focused fuzz 테스트 통과 4, Protocol 전체 통과 28, solution build 경고 0/오류 0,
    솔루션 전체 테스트 통과 106, 실패 0, 건너뜀 0. `git diff --check` 통과.

- [x] 수동 fan-out 확인을 위한 broker server console sample 을 추가했다.
  - 범위: `samples/Hps.Sample.BrokerServer`, `HighPerformanceSocket.slnx`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `dotnet build samples\Hps.Sample.BrokerServer\Hps.Sample.BrokerServer.csproj`가 프로젝트 파일 부재로 실패했다.
  - 구현: sample 은 `<host> <port> <max-frame-bytes>` 인자를 받아 `BrokerServer + TransportFactory.CreateDefault()`를 시작하고,
    Ctrl+C 입력 시 `BrokerServer.StopAsync`를 거쳐 정리한다.
  - 결정: D049에 따라 이 sample 은 운영용 daemon 이 아니라 기존 library host 를 조립하는 실행 harness 로 둔다.
  - 검증: sample build 는 Red 프로젝트 파일 부재 실패 뒤 Green 경고 0/오류 0. invalid args smoke 는 사용법 출력과 exit code 2 확인.
    solution build 는 병렬 test 와의 obj lock 충돌 뒤 직렬 재실행 경고 0/오류 0. 솔루션 전체 테스트 통과 102, 실패 0, 건너뜀 0.
    `git diff --check` 통과.

- [x] TCP receive handler 예외가 connection close notification 으로 수렴하도록 보강했다.
  - 범위: `src/Hps.Transport/Saea/SaeaTransport.cs`, `src/Hps.Transport/Abstractions/ITransportReceiveHandler.cs`,
    `tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `ReceivePump_WhenHandlerThrows_ClosesConnectionAndNotifiesHandler`가 `OnConnectionClosed` 미호출로 5초 timeout 실패했다.
  - 구현: `DispatchReceived` 예외를 catch 해 `NotifyConnectionClosed(connection)` 후 receive loop 를 종료하도록 했다.
  - 결정: D048에 따라 TCP handler 예외는 background task fault 가 아니라 connection 수명 종료로 관측된다.
  - 검증: focused 테스트는 Red 실패 1/통과 0 이후 Green 통과 1. Transport 전체 통과 37, solution build 경고 0/오류 0,
    솔루션 전체 테스트 통과 102, 실패 0, 건너뜀 0. `git diff --check` 통과.

- [x] TCP wire protocol 기반 publisher/subscriber sample client 를 추가했다.
  - 범위: `samples/Hps.Sample.Publisher`, `samples/Hps.Sample.Subscriber`, `samples/Shared/SampleTcpFrames.cs`,
    `HighPerformanceSocket.slnx`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `dotnet build samples\Hps.Sample.Publisher\Hps.Sample.Publisher.csproj`와
    `dotnet build samples\Hps.Sample.Subscriber\Hps.Sample.Subscriber.csproj`가 프로젝트 파일 부재로 실패했다.
  - 구현: publisher 는 `PUBLISH <topic> <payload>` frame 을 한 번 전송하고, subscriber 는 `SUBSCRIBE <topic>` frame 전송 뒤
    broker 가 fan-out 하는 raw payload chunk 를 stdout 으로 출력한다.
  - 결정: D047에 따라 샘플 client 는 `Hps.Server` 내부 타입을 참조하지 않고 broker TCP wire protocol 만 사용한다.
  - 검증: publisher/subscriber sample build 와 solution build 는 경고 0, 오류 0으로 통과했다.
    솔루션 전체 테스트 통과 101, 실패 0, 건너뜀 0. `git diff --check` 통과.

- [x] UDP receive backpressure Q1 중 SAEA Transport 내부 prefetch 경계를 회귀 테스트로 고정했다.
  - 범위: `tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - 테스트: `UdpReceive_WhenHandlerIsBlocked_DoesNotPrefetchAdditionalDatagrams`가 첫 datagram handler 를 막은 상태에서
    두 번째 datagram 을 보내도 receive loop 가 추가 `RefCountedBuffer`를 대여하지 않는지 검증한다.
  - 결정: D046에 따라 현재 SAEA UDP receive 기준선에는 별도 receive queue/drop 정책을 추가하지 않는다.
    동기 handler 가 반환될 때까지 다음 `RentCounted()`로 넘어가지 않으므로 Transport 내부 pool 대여 수가 무제한 누적되지 않는다.
  - 남은 범위: handler/Broker 가 datagram ref 를 별도 작업으로 넘기고 즉시 반환하는 경우의 상위 fan-out backpressure 정책은
    UDP Broker publish 경계가 생길 때 다시 결정한다.
  - 검증: focused 테스트는 최초 기대값을 receive loop idle buffer 모델에 맞게 조정한 뒤 통과했다.
    Transport 전체 통과 36, 솔루션 전체 통과 101, 빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] SAEA 기준선의 direct pinned block send/receive 예외를 문서 불변식과 맞췄다.
  - 범위: `AGENTS.md`, `DECISIONS.md`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 구현: `AGENTS.md`의 `BipBuffer` send/recv 큐 원칙은 유지하되, 현재 `SaeaTransport` raw Socket 기준선이
    D023/D024/D045에 따른 계약/수명 검증용 예외임을 명시했다.
  - 구현: `DECISIONS.md` D045로 SAEA 기준선 예외와 향후 RIO/io_uring 또는 명시적 송수신 큐 최적화의
    `BipBuffer` 적용 요구를 분리했다.
  - 검증: `rg` 문서 검색으로 D045/SAEA 예외 연결을 확인했다. `dotnet build HighPerformanceSocket.slnx`는
    경고 0, 오류 0으로 통과했고, `git diff --check`는 whitespace 오류 없이 통과했다.
    이번 단위는 문서 전용 변경이므로 full test 는 실행하지 않았다.

- [x] UDP datagram handler 예외 정책을 endpoint close notification 으로 고정했다.
  - 범위: `src/Hps.Transport/Abstractions/ITransportDatagramHandler.cs`,
    `src/Hps.Transport/Saea/SaeaTransport.cs`, `tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: handler 가 datagram 을 Release 한 뒤 예외를 던지면 close notification 이 오지 않아 timeout 으로 실패하는 것을 확인했다.
  - 구현: UDP receive loop 의 일반 예외 경로를 task fault 대신 `NotifyUdpEndpointClosed`로 수렴시켰다.
  - 구현: datagram 소유권은 handler 호출 시점에 이전된 상태를 유지하므로, handler 예외 후에도 datagram 반환 책임은 handler 에 남는다고 XML doc 에 명시했다.
  - 검증: focused policy 테스트 Green 통과 1, Transport 전체 통과 35, 솔루션 전체 통과 100,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] malformed TCP command 직접 close 경로의 subscription cleanup 누락을 수정했다.
  - 범위: `src/Hps.Broker/BrokerTcpFrameHandler.cs`, `tests/Hps.Broker.Tests/BrokerTcpFrameHandlerTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: 구독된 connection 이 malformed command 를 보낸 뒤 transport close notify 가 없으면 `alpha` topic 에 connection 이 남아
    `Assert.False()`가 실패하는 것을 확인했다.
  - 구현: `BrokerTcpFrameHandler`가 malformed command 또는 내부 오류 때문에 직접 `connection.Close()`를 호출할 때
    `SubscriptionTable.UnsubscribeAll(connection)`을 먼저 수행한다.
  - 검증: focused cleanup 테스트 Green 통과 1, Broker 전체 통과 18, 솔루션 전체 통과 100,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] drop-oldest public diagnostics snapshot 을 구현했다.
  - 범위: `src/Hps.Transport/Abstractions/ITransportDiagnostics.cs`,
    `src/Hps.Transport/Abstractions/TransportDiagnosticsSnapshot.cs`, `src/Hps.Transport/Runtime/TransportBase.cs`,
    `src/Hps.Transport/Runtime/TransportConnection.cs`, `src/Hps.Transport/Saea/SaeaTransport.cs`,
    `src/Hps.Transport/Saea/SaeaUdpEndpoint.cs`, transport tests, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `ITransportDiagnostics`/`TransportDiagnosticsSnapshot` 타입 부재와 diagnostics snapshot 부재가 `Assert.NotNull` 실패 3건으로 확인됐다.
  - 구현: `ITransport` 기본 계약은 넓히지 않고 선택적 `ITransportDiagnostics.GetDiagnosticsSnapshot()` capability 로 노출했다.
  - 구현: `TransportBase`가 TCP/UDP drop-oldest 누적 counter 를 유지하고, connection/endpoint 내부 counter 와 별도로
    close 이후에도 Transport-level snapshot 에 drop 수가 남도록 했다.
  - 검증: focused `TransportDiagnostics` 통과 3, Transport 전체 통과 35, 솔루션 전체 통과 99,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] 다중 subscriber TCP command fan-out 통합 테스트를 추가했다.
  - 범위: `tests/Hps.Server.Tests/BrokerServerTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 테스트: `BrokerServer + SaeaTransport` loopback listener 를 시작하고, raw TCP subscriber socket 2개가 같은 topic 에
    length-prefix `SUBSCRIBE alpha` frame 을 보낸 뒤 publisher socket 1개가 `PUBLISH alpha <payload>`를 보내면 두 subscriber 가
    동일 payload 원문을 받는지 검증했다.
  - 테스트: 공유 `RefCountedBuffer` fan-out 과 send completion 이후 server payload pool 이 `RentedCount==0`으로 돌아오는지 검증했다.
  - 결과: 기존 Server/Transport/Protocol/Broker 구현이 테스트를 즉시 통과해 production code 수정은 없었다.
  - 검증: focused `TcpCommandLoopback_WhenTwoSubscribersShareTopic` 통과 1, Server 전체 통과 5,
    솔루션 전체 통과 96, 빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] drop-oldest 내부 관측성 counter 를 구현했다.
  - 범위: `src/Hps.Transport/Runtime/TransportConnection.cs`, `src/Hps.Transport/Saea/SaeaUdpEndpoint.cs`,
    `tests/Hps.Transport.Tests/Runtime/TransportSendQueueTests.cs`, `tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: TCP/UDP 각각 `DroppedPendingSendCount` property 부재가 `Assert.NotNull` 실패로 확인됐다.
  - 구현: drop-oldest evict 발생 시 `Interlocked.Increment`로 내부 counter 를 증가시키고,
    `Volatile.Read` 기반 internal property 로 읽을 수 있게 했다.
  - 구현: public Transport/Broker/Server metric API 와 log 출력은 추가하지 않았다.
  - 검증: focused `DroppedPendingSendCount` 통과 2, Transport 전체 통과 32, 솔루션 전체 통과 95,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] UDP `SaeaUdpEndpoint` pending send queue backpressure 를 구현했다.
  - 범위: `src/Hps.Transport/Saea/SaeaUdpEndpoint.cs`,
    `tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: capacity 17번째 datagram send 후 pending count 가 17로 남아 실패하는 것을 확인했다.
  - Red: overflow 뒤 publisher guard ref 를 놓고 close 하면 evict 가 없어 `RentedCount==17`로 남는 실패를 확인했다.
  - 구현: endpoint pending queue 기본 capacity 를 16으로 두고, 가득 찬 상태에서 새 datagram 을 수락하면
    가장 오래된 pending datagram 을 evict 한 뒤 Transport 소유 ref 를 Release 한다.
  - 구현: evict 대상 선택과 queue 제거는 `_sendGate` lock 으로 직렬화하고, Release 는 lock 밖에서 수행한다.
  - 검증: focused `UdpSendTo_WhenPendingQueue` 통과 2, Transport 전체 통과 30, 솔루션 전체 통과 93,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] TCP `TransportConnection` pending send queue backpressure 를 구현했다.
  - 범위: `src/Hps.Transport/Runtime/TransportConnection.cs`,
    `tests/Hps.Transport.Tests/Runtime/TransportSendQueueTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: capacity 17번째 send 후 pending count 가 17로 남아 실패하는 것을 확인했다.
  - Red: overflow 뒤 publisher guard ref 를 놓고 close 하면 evict 가 없어 `RentedCount==17`로 남는 실패를 확인했다.
  - 구현: pending queue 기본 capacity 를 16으로 두고, 가득 찬 상태에서 새 send 를 수락하면 가장 오래된 pending 항목을 evict 한 뒤
    Transport 소유 ref 를 Release 한다.
  - 구현: evict 대상 선택과 queue 제거는 connection lock 으로 직렬화하고, Release 는 lock 밖에서 수행한다.
  - 검증: focused `TransportSendQueueTests` 통과 9, Transport 전체 통과 28, 솔루션 전체 통과 91,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] `BrokerServer` 실제 TCP command loopback 통합 테스트를 추가했다.
  - 범위: `tests/Hps.Server.Tests/BrokerServerTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 테스트: `BrokerServer + SaeaTransport` loopback listener 를 시작하고, subscriber raw TCP socket 이 length-prefix
    `SUBSCRIBE alpha`를 보낸 뒤 publisher raw TCP socket 이 `PUBLISH alpha <payload>`를 보내면 subscriber 가 payload 원문을 받는지 검증했다.
  - 테스트: publish frame/send ref 가 모두 반환되어 server payload pool 의 `RentedCount==0`으로 돌아오는지 검증했다.
  - 결과: 기존 Server/Transport/Protocol/Broker 구현이 테스트를 즉시 통과해 production code 수정은 없었다.
  - 검증: focused `TcpCommandLoopback` 통과 1, Server 전체 통과 4, 솔루션 전체 통과 89,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] `Hps.Server` 최소 TCP host wiring 을 구현했다.
  - 범위: `src/Hps.Server/Hps.Server.csproj`, `src/Hps.Server/BrokerServer.cs`,
    `tests/Hps.Server.Tests/Hps.Server.Tests.csproj`, `tests/Hps.Server.Tests/BrokerServerTests.cs`,
    `HighPerformanceSocket.slnx`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `BrokerServer` 타입 부재를 reflection 기반 단언 실패로 확인했다.
  - Red: stub 상태에서 receive handler 등록, Transport start/listen, accept loop 시작, Stop listener/Transport 정리 단언이 실패하는 것을 확인했다.
  - 구현: `BrokerServer`가 `SubscriptionTable`, `BrokerPublisher`, `BrokerTcpFrameHandler`, `TcpFrameReceiveHandler`를 조립하고
    주입된 `ITransport`에 receive handler 를 등록한다.
  - 구현: `StartTcpAsync`는 transport start/listen 후 accept loop 를 시작하고, `StopAsync`/`Dispose`는 accept loop 를 깨운 뒤
    listener 와 Transport 를 정리한다.
  - 검증: focused `BrokerServerTests` 통과 3, 솔루션 전체 통과 88, 빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] Broker TCP frame command handler 를 구현했다.
  - 범위: `src/Hps.Protocol/TcpCommand.cs`, `src/Hps.Protocol/TcpCommandDecoder.cs`,
    `src/Hps.Broker/BrokerTcpFrameHandler.cs`, `src/Hps.Broker/Hps.Broker.csproj`,
    `tests/Hps.Protocol.Tests/TcpCommandDecoderTests.cs`, `tests/Hps.Broker.Tests/BrokerTcpFrameHandlerTests.cs`,
    `tests/Hps.Broker.Tests/Hps.Broker.Tests.csproj`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `TcpCommand.PayloadOffset` 부재를 reflection 기반 단언 실패로 확인했다.
  - Red: `PayloadOffset` 기본값 0 상태에서 `PUBLISH alpha <payload>`의 실제 payload 시작 offset 14 단언 실패를 확인했다.
  - Red: `BrokerTcpFrameHandler` 타입/생성자/`ITcpFrameHandler` 구현 부재를 확인했다.
  - Red: no-op handler 에서 subscribe 등록, publish payload range fan-out, close cleanup, malformed frame close/release 가 실패하는 것을 확인했다.
  - 구현: `BrokerTcpFrameHandler.OnFrame`은 command 를 decode 해 subscribe/publish 로 연결하고, 수락한 frame guard ref 를 항상 Release 한다.
  - 구현: `OnConnectionClosed`는 `SubscriptionTable.UnsubscribeAll`을 호출하며, malformed command 는 frame 을 회수하고 connection 을 닫는다.
  - 검증: focused `TcpCommandDecoderTests` 통과 10, focused `BrokerTcpFrameHandlerTests` 통과 5,
    Protocol 전체 통과 24, Broker 전체 통과 17, 솔루션 전체 통과 85, 빌드 경고 0/오류 0,
    `git diff --check` 통과.

- [x] Broker subscription connection-wide cleanup API 를 구현했다.
  - 범위: `src/Hps.Broker/SubscriptionTable.cs`, `tests/Hps.Broker.Tests/BrokerRoutingTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `SubscriptionTable.UnsubscribeAll(IConnection)` 메서드 부재를 reflection 기반 단언 실패로 확인했다.
  - Red: no-op stub 에서 같은 connection 이 여러 topic 에 남아 제거 수 0으로 실패하는 것을 확인했다.
  - 구현: `UnsubscribeAll`은 모든 topic set 을 열거하며 대상 connection 만 제거하고, D008에 따라 topic entry 자체는 제거하지 않는다.
  - 검증: focused `BrokerRoutingTests` 통과 6, Broker 전체 통과 12, 솔루션 전체 통과 79,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] Broker publish payload range 를 구현했다.
  - 범위: `src/Hps.Broker/BrokerPublisher.cs`, `tests/Hps.Broker.Tests/BrokerPublisherTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `Publish(string, RefCountedBuffer, int, int)` overload 부재를 reflection 기반 단언 실패로 확인했다.
  - Red: no-op overload 에서 payload range fan-out 과 잘못된 range 즉시 거부가 실패하는 것을 확인했다.
  - 구현: 기존 full publish 는 ranged publish 로 위임하고, ranged publish 는 구독자 snapshot 전에 offset/length 를 검증한다.
  - 구현: 구독자별 send 는 기존 AddRef/TrySend/false-release 계약을 유지하면서 `TransportSendBuffer`에 offset/length 를 그대로 전달한다.
  - 검증: focused `BrokerPublisherTests` 통과 6, Broker 전체 통과 10, 솔루션 전체 통과 77,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] Claude 검토 의견 조치 현황을 문서화했다.
  - 범위: `.claude/review/review-status-2026-06-11.md`, `.claude/review/README.md`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 구현: 기존 Claude 검토 원문은 삭제하지 않고 보존했다.
  - 구현: 현재 작업 트리 기준으로 must-fix 해소 여부, 오래된 종합 리뷰의 superseded 상태,
    남은 비차단 항목을 별도 review status 문서에 정리했다.
  - 검증: 솔루션 전체 테스트 통과 75, 빌드 경고 0/오류 0, `git diff --check` whitespace 오류 없음.

- [x] Broker publish fan-out 을 구현했다.
  - 범위: `src/Hps.Broker/BrokerPublisher.cs`, `src/Hps.Broker/Hps.Broker.csproj`,
    `tests/Hps.Broker.Tests/BrokerPublisherTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `BrokerPublisher` 타입 부재와 생성자/`Publish` 계약 부재를 reflection 기반 단언 실패로 확인했다.
  - Red: no-op stub 에서 구독자 2명 fan-out 과 Transport 거부 구독자 경계가 기대 수락 수를 반환하지 못해 실패했다.
  - 구현: `SubscriptionTable.CopySubscribers` snapshot 을 `ArrayPool<IConnection>`으로 받아 구독자별 `AddRef` 후
    `ITransport.TrySend`로 넘긴다.
  - 구현: `TrySend` false 또는 send buffer 생성/전송 예외 경로에서는 Broker 가 방금 추가한 구독자 ref 를 즉시 `Release`한다.
  - 구현: publish guard ref 는 caller 소유로 유지해, command handler/Server wiring 이 Publish 반환 뒤 원본 ref 를 해제해야 한다.
  - 검증: focused `BrokerPublisherTests` 통과 4, Broker 전체 통과 8, 솔루션 전체 통과 75,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] Broker subscription routing table 을 구현했다.
  - 범위: `src/Hps.Broker/`, `tests/Hps.Broker.Tests/`, `HighPerformanceSocket.slnx`, `CURRENT_PLAN.md`,
    `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `SubscriptionTable` 타입 부재와 routing API 부재를 reflection 기반 단언 실패로 확인했다.
  - Red: no-op stub 에서 subscribe, unsubscribe, snapshot copy, D008 R1 동시 subscribe-vs-unsubscribe 테스트가 실패하는 것을 확인했다.
  - 구현: `topic -> connection set`을 `ConcurrentDictionary`로 관리하고, connection 은 reference equality 로 비교한다.
  - 구현: D008에 따라 구독자 set 이 비어도 topic entry 를 즉시 제거하지 않는 NoCleanup 정책을 적용했다.
  - 테스트: Green 후 reflection 테스트를 제거하고 직접 public API 테스트 4개만 남겼다.
  - 검증: focused `BrokerRoutingTests` 통과 4, Broker 전체 통과 4, 솔루션 전체 통과 71,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] TCP frame receive handler 수명/예외 경계를 보강했다.
  - 범위: `src/Hps.Protocol/`, `tests/Hps.Protocol.Tests/`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: PayloadTooLarge 후 Transport close 알림이 다시 오면 상위 close handler 가 2회 호출되는 실패를 확인했다.
  - Red: `ITcpFrameHandler.OnFrame` 예외 후 완성 frame 이 Release 되지 않아 `RentedCount==1`로 남는 실패를 확인했다.
  - 구현: close 통지는 connection 별 1회만 수행하며, weak marker 로 이미 통지한 connection 을 추적해 단명 connection 누수를 피한다.
  - 구현: `OnFrame` 예외 시 frame 을 회수하고 assembler 를 제거한 뒤 connection 을 닫는다.
  - 검증: focused `TcpFrameReceiveHandlerTests` Red 실패 2/통과 5, Green 통과 7, Protocol 전체 통과 23,
    솔루션 전체 통과 67, 빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] TCP command decoder 를 구현했다.
  - 범위: `src/Hps.Protocol/`, `tests/Hps.Protocol.Tests/`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `TcpCommandDecoder` 타입 부재와 `TcpCommand`/`TcpCommandKind`/`TcpCommandDecodeError`/`TryDecode`
    계약 부재를 reflection 기반 단언 실패로 확인했다.
  - Red: 동작 테스트 8개는 스텁 decoder 에서 subscribe/publish 성공, publish payload 보존,
    malformed frame 별 error 반환을 만족하지 못해 실패했다.
  - 구현: `SUBSCRIBE <topic>`과 `PUBLISH <topic> <payload>`를 해석하고, malformed input 은 예외 대신
    `TcpCommandDecodeError`로 반환한다.
  - 구현: `TcpCommand`는 `readonly ref struct` span view 이므로 topic/payload 를 복사하지 않고 frame 수명 안에서만 사용된다.
  - 검증: focused `TcpCommandDecoderTests` 통과 9, Protocol 전체 통과 21, 솔루션 전체 통과 65,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] TCP receive frame 어댑터를 구현했다.
  - 범위: `src/Hps.Protocol/`, `tests/Hps.Protocol.Tests/`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `TcpFrameReceiveHandler` 타입 부재와 `ITcpFrameHandler`/constructor/Transport handler 계약 부재를
    reflection 기반 단언 실패로 확인했다.
  - Red: 동작 테스트 3개는 빈 adapter 구현에서 frame 전달, partial payload 대여, payload-too-large close 를 수행하지 않아 실패했다.
  - 구현: `TcpFrameReceiveHandler`가 `ITransportReceiveHandler`를 구현하고 connection 별 `TcpFrameAssembler`를 소유한다.
  - 구현: raw TCP chunk 를 consumed loop 로 처리해 한 chunk 의 다중 frame 도 모두 `ITcpFrameHandler.OnFrame`으로 전달한다.
  - 구현: `OnConnectionClosed`는 partial assembler payload 를 Dispose 하고, `PayloadTooLarge`는 connection 을 닫은 뒤 close callback 을 전달한다.
  - 검증: focused `TcpFrameReceiveHandlerTests` 통과 5, Protocol 전체 통과 12, 솔루션 전체 통과 56,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] TCP 프레임 조립기 edge/fuzz coverage 를 보강했다.
  - 범위: `tests/Hps.Protocol.Tests/TcpFrameAssemblerTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 테스트: 0 length frame 이 caller 소유의 빈 `RefCountedBuffer`로 완성되고 Release 후 풀 누수가 없는지 검증했다.
  - 테스트: 한 TCP chunk 에 여러 frame 이 붙은 경우 첫 호출의 `consumed`가 첫 frame 끝까지만 가리키고,
    remaining slice 재호출로 다음 frame 이 완성되는 caller loop 계약을 검증했다.
  - 테스트: `payloadLength == maxPayloadLength` 성공 경계와 24개 frame 결정적 fragmentation fuzz 를 검증했다.
  - 결과: 기존 `TcpFrameAssembler` 구현이 새 edge/fuzz 테스트를 즉시 통과해 production code 수정은 없었다.
  - 검증: focused `TcpFrameAssemblerTests` 통과 7, Protocol 전체 통과 7, 솔루션 전체 통과 51, 빌드 경고 0/오류 0,
    `git diff --check` 통과.

- [x] TCP 프레임 조립기 기본 계약을 구현했다.
  - 범위: `src/Hps.Protocol/`, `tests/Hps.Protocol.Tests/`, `HighPerformanceSocket.slnx`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `TcpFrameAssembler` 타입 부재와 `TryReadFrame` API 부재를 reflection 기반 단언 실패로 확인했다.
  - Red: 동작 테스트 3개는 스텁 구현에서 frame 을 만들지 못하거나 maxPayload/Dispose 경계를 지키지 못해 실패했다.
  - 구현: `TcpFrameAssembler`가 TCP 4바이트 big-endian length header 를 누적하고 payload 를 `RefCountedBuffer`로 복사한다.
  - 구현: 완성된 frame 은 caller 가 Release 해야 하며, 조립 중 Dispose 되면 partial payload ref 를 반환한다.
  - 테스트: fragmented header/payload 조립, maxPayload 초과 시 buffer 미대여, partial payload dispose 반환을 검증했다.
  - 검증: Protocol focused Red/Green 완료. 리팩터 후 Protocol 전체 통과 3, 솔루션 전체 통과 47, 빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] TCP 동시 연결 echo 통합 테스트를 추가했다.
  - 범위: `tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 목적: Phase 2 테스트 기준의 동시 연결 안정성을 receive pump 와 send pump 의 실제 loopback echo 왕복으로 검증한다.
  - 테스트: 8개 raw TCP client 를 같은 listener 에 연결하고, 각 accepted `IConnection`이 서로 다른 payload 를 동시에 echo 받는지 확인했다.
  - 테스트: echo buffer pool 이 `RentedCount==0`으로 돌아오고, 모든 inbound connection close 뒤 transport tracking count 가 0인지 확인했다.
  - 결과: 기존 production code 가 기준을 이미 만족해 production code 수정은 없었다.
  - 검증: focused TCP 동시 echo 테스트 통과 1, Transport 전체 통과 26, 솔루션 전체 통과 44, 빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] UDP echo loopback 통합 테스트를 추가했다.
  - 범위: `tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 목적: Phase 2 완료 기준의 UDP loopback echo 왕복을 receive loop 와 endpoint send pump 결합 경로로 검증한다.
  - 테스트: datagram handler 가 받은 owned `RefCountedBuffer`에 Transport 송신 ref 를 추가하고,
    같은 `IUdpEndpoint`의 `TrySendTo`로 remote endpoint 에 되돌려 보내 raw client socket 이 동일 payload 를 받는지 확인했다.
  - 결과: 기존 production code 가 기준을 이미 만족해 production code 수정은 없었다.
  - 검증: focused UDP echo 테스트 통과 1, Transport 전체 통과 25, 솔루션 전체 통과 43, 빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] TCP echo loopback 통합 테스트를 추가했다.
  - 범위: `tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 목적: Phase 2 완료 기준의 TCP loopback echo 왕복을 recv pump 와 send pump 결합 경로로 검증한다.
  - 테스트: receive handler 가 borrowed `TransportReceiveBuffer`를 테스트 전용 `RefCountedBuffer`로 즉시 복사하고,
    같은 `IConnection`에 `TrySend`해 raw client socket 이 동일 payload 를 다시 받는지 확인했다.
  - 결과: 기존 production code 가 기준을 이미 만족해 production code 수정은 없었다.
  - 검증: focused echo 테스트 통과 1.

- [x] UDP endpoint send 를 endpoint별 pending queue 와 단일 pump 로 직렬화했다.
  - 범위: `src/Hps.Transport/Saea/SaeaTransport.cs`, `src/Hps.Transport/Saea/SaeaUdpEndpoint.cs`,
    `tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `UdpSendTo_WhenEndpointClosesBeforePumpSends_DrainsQueuedDatagramRef`가 `SaeaUdpEndpoint.PendingSendCount`
    부재 단언 실패로 실패하는 것을 확인했다.
  - 구현: `TrySendTo`는 datagram 마다 `Task.Run`을 만들지 않고 `SaeaUdpEndpoint` pending queue 에 송신 요청을 넣는다.
  - 구현: bind 된 endpoint 마다 단일 UDP send pump 를 시작해 queued datagram 을 순차적으로 `SendToAsync`로 전송하고,
    기존 completion/unwind 경로에서 Transport 소유 ref 를 Release 한다.
  - 구현: endpoint close 는 아직 pump 가 가져가지 않은 queued datagram 의 ref 를 drain 하므로 close 전 송신 대기 항목이 누수되지 않는다.
  - 테스트: pump 없는 internal endpoint 로 queued 상태를 고정하고 close drain 후 `RentedCount==0`을 검증했다.
  - 검증: focused Red 실패 1, Green 통과 1. UDP focused 통과 2. Transport 전체 통과 23.

- [x] `Hps.Transport`와 `Hps.Transport.Tests` 폴더 구조를 책임별로 분리했다.
  - 범위: `src/Hps.Transport/`, `tests/Hps.Transport.Tests/`, `AGENTS.md`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - 구조: `src/Hps.Transport/Abstractions`에는 public 계약과 buffer view 를 배치했다.
  - 구조: `src/Hps.Transport/Runtime`에는 `TransportBase`, `TransportConnection`, `TransportFactory`를 배치했다.
  - 구조: `src/Hps.Transport/Saea`에는 `SaeaTransport`, listener, UDP endpoint 구현을 배치했다.
  - 구조: `tests/Hps.Transport.Tests`도 `Contracts`, `Runtime`, `Saea`로 나눠 production 책임 축과 맞췄다.
  - 구현: namespace 는 그대로 `Hps.Transport`/`Hps.Transport.Tests`를 유지해 public API 와 using churn 을 만들지 않았다.
  - 검증: Transport 전체 → 통과 22. 전체 `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 22.
    `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] Phase 2 backend selector 최소 계약을 구현했다.
  - 범위: `src/Hps.Transport/TransportFactory.cs`, `tests/Hps.Transport.Tests/TransportContractTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `TransportFactory` 타입 부재를 reflection 기반 contract 테스트의 `Assert.NotNull` 실패로 확인했다.
  - 구현: `TransportFactory.CreateDefault()` 정적 factory 를 추가하고 현재는 모든 환경에서 `SaeaTransport`를 `ITransport`로 반환하게 했다.
  - 테스트: Green 후 테스트를 직접 public API 호출로 리팩터링해 `ITransport` 반환값이 현재 SAEA fallback 인지 검증했다.
  - 검증: focused factory 테스트 → Red 실패 1, Green 통과 1. Transport 전체 → 통과 22.

- [x] UDP datagram handler 예외 시 receive loop 이중 Release 가능성을 제거했다.
  - 범위: `src/Hps.Transport/SaeaTransport.cs`, `tests/Hps.Transport.Tests/SaeaTransportTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: handler 가 `RefCountedBuffer`를 Release 한 뒤 예외를 던지면 receive loop 가 같은 datagram 을 다시 Release 하여
    handler 예외가 `InvalidOperationException`으로 덮이는 실패를 확인했다.
  - 구현: UDP receive loop 에서 handler 호출 전에 `ownedDatagram`으로 소유권을 옮기고 local `datagram`을 null 로 끊어,
    handler 예외 경로에서 catch 가 이미 이전된 ref 를 다시 만지지 않게 했다.
  - 테스트: private receive loop 를 white-box 로 실행해 background loop 예외를 직접 관측하고, handler 예외가 double-release 예외로 덮이지 않는지 검증했다.
  - 검증: focused S1 회귀 테스트 → Red 실패 1, Green 통과 1. Transport 전체 → 통과 21.

- [x] UDP datagram public 계약과 `SaeaTransport` UDP loopback 기준선을 구현했다.
  - 범위: `src/Hps.Transport/ITransport.cs`, `src/Hps.Transport/ITransportDatagramHandler.cs`,
    `src/Hps.Transport/IUdpEndpoint.cs`, `src/Hps.Transport/SaeaTransport.cs`, `src/Hps.Transport/SaeaUdpEndpoint.cs`,
    `src/Hps.Transport/TransportBase.cs`, `tests/Hps.Transport.Tests/TransportContractTests.cs`,
    `tests/Hps.Transport.Tests/SaeaTransportTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: UDP endpoint/datagram handler 계약 부재는 reflection 기반 contract 테스트의 `IUdpEndpoint` 타입 부재 실패로 확인했다.
  - Red: UDP receive 기준선은 `BindUdpAsync`가 `NotImplementedException`을 던지는 실패로 확인했다.
  - Red: UDP send 기준선은 `TrySendTo`가 `NotImplementedException`을 던지는 실패로 확인했다.
  - 구현: UDP는 TCP accept 모델과 분리해 `IUdpEndpoint` 수명 핸들을 사용하고, `BindUdpAsync`/`TrySendTo`/`SetDatagramHandler`로 bind/send/receive 경계를 노출한다.
  - 구현: `SaeaTransport`는 UDP socket 을 bind 하고 receive loop 에서 pinned counted buffer 를 직접 대여해 datagram handler 에 `RefCountedBuffer` 소유권을 넘긴다.
  - 구현: `TrySendTo` 성공 시 Transport 가 `TransportSendBuffer`의 ref 를 소유하고, UDP socket send completion/unwind 경로에서 정확히 한 번 Release 한다.
  - 테스트: UDP receive 가 1 datagram = 1 message 로 handler 에 도착하고 handler 가 받은 `RefCountedBuffer`를 Release 하는지 검증했다.
  - 테스트: UDP send 가 `TransportSendBuffer.Offset/Length` 범위만 전송하고 publish guard ref 해제 뒤 send completion 에서 `RentedCount==0`으로 돌아오는지 검증했다.
  - 검증: focused UDP 계약/수신/송신 테스트 각각 Red 실패 1회와 Green 통과 1회. Transport 전체 → 통과 20. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 20. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] `SaeaTransport` TCP send pump 가 pending `TransportSendBuffer`를 실제 socket 으로 보내고 ref 를 반환하는 최소 loopback 기준선을 구현했다.
  - 범위: `src/Hps.Transport/SaeaTransport.cs`, `src/Hps.Transport/TransportConnection.cs`,
    `tests/Hps.Transport.Tests/SaeaTransportTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: accepted connection 에 `TrySend`한 payload 가 raw client socket 으로 도착하지 않아 receive timeout 단언 실패로 확인했다.
  - 구현: `TransportConnection`에 pending send signal 을 추가해 빈 큐에서 첫 항목이 들어오거나 close 될 때 단일 send loop 를 깨운다.
  - 구현: `SaeaTransport`가 connection 생성 시 send loop 를 시작하고, `TryBeginInFlightSend`로 얻은 handle 을 socket send completion/unwind 경로에서 완료 또는 Dispose 한다.
  - 테스트: `TransportSendBuffer.Offset/Length` 범위만 raw socket client 로 전송되는지, publish guard ref 해제 후 send completion 이 Transport 소유 ref 를 반환해 `RentedCount==0`이 되는지 검증했다.
  - 검증: focused send pump 테스트 → Red 실패 1, Green 통과 1. Transport 전체 → 통과 17. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 17. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] `SaeaTransport`에서 닫힌 connection 이 transport 추적 목록에 남는 누수를 수정했다.
  - 범위: `src/Hps.Transport/SaeaTransport.cs`, `src/Hps.Transport/TransportConnection.cs`,
    `tests/Hps.Transport.Tests/SaeaTransportTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: accepted `IConnection.Close()` 이후 transport 내부 `_connections` count 가 1로 남아 단언 실패하는 테스트로 확인했다.
  - 구현: `TransportConnection`에 close callback 을 추가하고 `SaeaTransport`가 `UnregisterConnection`을 연결해 개별 connection close 시 추적 목록에서 제거한다.
  - 구현: pending drain 과 closed 표시는 connection lock 안에서 유지하되, unregister callback 과 backend socket dispose 는 lock 밖에서 수행한다.
  - 테스트: raw socket client 로 accepted connection 하나를 만들고 close 한 뒤 transport tracking count 가 0으로 돌아오는지 검증했다.
  - 검증: focused unregister 테스트 → Red 실패 1, Green 통과 1. Transport 전체 → 통과 16. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 16. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] `SaeaTransport` TCP recv pump 가 receive handler 로 byte stream 조각을 전달하는 최소 loopback 기준선을 구현했다.
  - 범위: `src/Hps.Transport/SaeaTransport.cs`, `tests/Hps.Transport.Tests/SaeaTransportTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: raw socket client 가 보낸 bytes 가 receive handler 로 도착하지 않아 timeout 단언 실패로 확인했다.
  - 구현: accepted/outbound socket 연결마다 receive loop 를 시작하고, `PinnedBlockMemoryPool`에서 대여한 receive block 으로 socket bytes 를 읽는다.
  - 구현: receive loop 는 raw TCP byte chunk 를 `TransportReceiveBuffer` borrowed view 로 만들어 현재 handler snapshot 의 `OnReceived`에 동기 전달한다.
  - 구현: remote close 또는 socket error 는 `OnConnectionClosed`를 호출하고 `IConnection.Close()` 경로로 정리한다.
  - 테스트: raw socket client 가 loopback listener 로 보낸 `{10,20,30,40}` payload 가 accepted `IConnection`과 함께 handler 로 전달되는지 검증했다.
  - 검증: focused recv pump 테스트 → 통과 1, 실패 0, 건너뜀 0. Transport 전체 → 통과 15. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 15. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] TCP payload I/O 전에 Transport 수신 전달 계약과 receive buffer 소유권을 확정했다.
  - 범위: `src/Hps.Transport/ITransport.cs`, `src/Hps.Transport/ITransportReceiveHandler.cs`,
    `src/Hps.Transport/TransportReceiveBuffer.cs`, `src/Hps.Transport/TransportBase.cs`,
    `tests/Hps.Transport.Tests/TransportContractTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `ITransportReceiveHandler`/`TransportReceiveBuffer` 타입 부재를 reflection 기반 테스트의 `Assert.NotNull` 실패로 확인했다.
  - 구현: `ITransport.SetReceiveHandler(ITransportReceiveHandler)`를 추가했다.
  - 구현: `ITransportReceiveHandler.OnReceived(IConnection, TransportReceiveBuffer)`와 `OnConnectionClosed(IConnection)` 계약을 추가했다.
  - 구현: `TransportReceiveBuffer`를 `readonly ref struct`로 추가해 `ReadOnlySpan<byte>` borrowed view 와 `Length`만 노출한다.
  - 구현: `TransportBase`가 receive handler 등록과 snapshot helper 를 공통 처리한다.
  - 테스트: receive handler/borrowed buffer 계약이 raw `Memory<byte>`/`ReadOnlyMemory<byte>` parameter/property 를 노출하지 않고,
    `TransportReceiveBuffer`가 byref-like 타입으로 `Span`/`Length`를 제공하는지 검증했다.
  - 검증: focused receive 계약 테스트 → 통과 1, 실패 0, 건너뜀 0. Transport 전체 → 통과 14. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 14. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] `SaeaTransport`의 TCP listen/connect/accept 최소 loopback 기준선을 구현했다.
  - 범위: `src/Hps.Transport/SaeaTransport.cs`, `src/Hps.Transport/SaeaConnectionListener.cs`,
    `src/Hps.Transport/TransportConnection.cs`, `tests/Hps.Transport.Tests/SaeaTransportTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `SaeaTransport` 타입 부재를 reflection 기반 테스트의 `Assert.NotNull` 실패로 확인했다.
  - 구현: `SaeaTransport`가 `StartAsync`, `ListenTcpAsync`, `ConnectTcpAsync`, `StopAsync`, `Dispose`를 구현한다.
  - 구현: `SaeaConnectionListener`가 listen socket 을 감싸고 `AcceptAsync`에서 accepted socket 을 `TransportConnection`으로 등록한다.
  - 구현: `TransportConnection.Close()`가 pending drain 뒤 backend socket 같은 transport resource 를 dispose 할 수 있게 했다.
  - 테스트: localhost loopback 에서 포트 0 listener 를 열고, `LocalEndPoint`로 connect 한 뒤 accept 된 inbound 연결과 outbound 연결을 얻는지 검증했다.
  - 검증: focused loopback 테스트 → 통과 1, 실패 0, 건너뜀 0. Transport 전체 → 통과 13. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 13. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] Phase 2 SAEA 기준선 착수 전에 TCP public listen/connect/accept 연결 모델을 확정했다.
  - 범위: `src/Hps.Transport/ITransport.cs`, `src/Hps.Transport/IConnectionListener.cs`, `src/Hps.Transport/TransportBase.cs`,
    `tests/Hps.Transport.Tests/TransportContractTests.cs`, `tests/Hps.Transport.Tests/TransportSendQueueTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `IConnectionListener` 타입 부재를 reflection 기반 테스트의 `Assert.NotNull` 실패로 확인했다.
  - 구현: `ITransport.ListenTcpAsync(EndPoint, CancellationToken)`와 `ConnectTcpAsync(EndPoint, CancellationToken)`를 추가했다.
  - 구현: `IConnectionListener`를 추가해 listener 의 `LocalEndPoint`, `AcceptAsync`, `Close`/`Dispose` 계약을 명시했다.
  - 구현: `TransportBase`가 TCP listen/connect 추상 멤버를 강제하도록 했다.
  - 테스트: TCP listener/connector/accept 계약이 `IConnection`과 `IConnectionListener`를 통해 노출되고,
    public 계약이 raw `Memory<byte>` parameter 를 다시 노출하지 않는지 검증했다.
  - 검증: focused 연결 계약 테스트 → 통과 1, 실패 0, 건너뜀 0. Transport 전체 → 통과 12. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 12. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] 송신 펌프 abandon-leak 방어를 위해 in-flight handle 경로를 구현했다.
  - 범위: `src/Hps.Transport/TransportConnection.cs`, `tests/Hps.Transport.Tests/TransportSendQueueTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `TryBeginInFlightSend` 메서드 부재를 reflection 기반 테스트의 `Assert.NotNull` 실패로 확인했다.
  - 구현: `TryDequeueSend(out TransportSendBuffer)` raw dequeue API를 제거하고,
    `TryBeginInFlightSend(out InFlightSend?)`가 dispose 가능한 in-flight handle 을 반환하게 했다.
  - 구현: `InFlightSend.Complete()`와 `Dispose()`는 같은 release 경로를 타며, `Interlocked.Exchange`로 여러 번 호출돼도
    실제 `RefCountedBuffer.Release()`는 한 번만 수행한다.
  - 테스트: pump 가 dequeue 이후 close/unwind 로 completion 없이 빠져나가는 abandon 시나리오에서 `Dispose()`가
    Transport 소유 ref 를 반환해 `RentedCount==0`으로 돌아오는지 검증했다.
  - 테스트: 정상 completion 후 `Dispose()`가 다시 호출되어도 이중 반환이 발생하지 않는지 검증했다.
  - 검증: focused `TransportSendQueueTests` → 통과 7, 실패 0, 건너뜀 0. Transport 전체 → 통과 11. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 11. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] 송신 펌프의 in-flight 완료 Release 경로를 구현했다.
  - 범위: `src/Hps.Transport/TransportConnection.cs`, `tests/Hps.Transport.Tests/TransportSendQueueTests.cs`.
  - Red: `CompleteInFlightSend` 메서드 부재를 reflection 기반 테스트의 `Assert.NotNull` 실패로 확인했다.
  - 구현: `TransportConnection.CompleteInFlightSend(TransportSendBuffer)`를 추가해, 송신 펌프가 완료/취소/unwind 시
    이미 dequeue 한 in-flight 항목의 Transport 소유 ref 를 반환하게 했다.
  - 구현: 이 경로는 pending 큐 상태를 변경하지 않으므로 `_gate` lock 을 잡지 않는다. close 는 pending 만 drain 하고,
    in-flight ref 는 이 completion 경로가 책임진다는 D016/D017 경계를 유지한다.
  - 테스트: close 이후에도 이미 dequeue 된 in-flight 항목은 close 가 반환하지 않고, completion 경로에서 반환되는지 검증했다.
  - 테스트: close 없이 정상 completion 만으로도 Transport 소유 ref 가 반환되어 `RentedCount==0`으로 돌아오는지 검증했다.
  - 검증: focused `TransportSendQueueTests` → 통과 6, 실패 0, 건너뜀 0. Transport 전체 → 통과 10. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 10. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] `ITransport.TrySend` 송신 큐의 enqueue/close release 계약을 구현했다.
  - 범위: `src/Hps.Transport/`, `tests/Hps.Transport.Tests/TransportSendQueueTests.cs`.
  - Red: `TransportBase` 타입 부재를 reflection 기반 테스트의 단언 실패로 확인했다.
  - 구현: `TransportBase.TrySend(IConnection, TransportSendBuffer)`가 내부 `TransportConnection`에 pending 송신을 위임하도록 했다.
  - 구현: `TransportConnection.Close()`는 close 표시와 pending drain 을 같은 lock 안에서 처리하고, pending 항목의
    `RefCountedBuffer`를 Release 한다. close 이후 `TrySend`는 false 를 반환해 호출자가 Release 하게 한다.
  - 구현: 송신 펌프가 `TryDequeueSend`로 가져간 in-flight 항목은 close 가 Release 하지 않도록 분리했다.
  - 구현: `TransportBase.TrySend`가 pending 큐에 넣기 전에 `TransportSendBuffer`의 live buffer 접근을 확인해
    `default(TransportSendBuffer)` 같은 생성자 미통과 요청이 close drain 까지 지연되지 않게 했다.
  - 테스트: open 연결에서 TrySend 성공 후 publish 가드 ref 를 해제해도 close 전까지 pool 이 반환되지 않고,
    close drain 에서 반환되는지 검증했다.
  - 테스트: closed 연결의 TrySend false 경로에서 Transport 가 소유권을 가져가지 않아 호출자가 Release 해야 함을 검증했다.
  - 테스트: default 송신 요청은 pending 큐에 들어가기 전에 즉시 거부되어 close drain 시점의 늦은 실패를 만들지 않는지 검증했다.
  - 테스트: Close idempotency 와 in-flight 항목을 close 가 Release 하지 않는 경계를 검증했다.
  - 검증: focused `TransportSendQueueTests` → 통과 5, 실패 0, 건너뜀 0. Transport 전체 → 통과 9. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 9. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] Phase 2 `ITransport`와 버퍼 소유권 계약을 구체화했다.
  - 범위: `src/Hps.Transport/`, `tests/Hps.Transport.Tests/`, `HighPerformanceSocket.slnx`.
  - Red: `Hps.Transport.TransportSendBuffer` 타입 부재를 reflection 기반 테스트의 단언 실패로 확인했다.
  - 구현: `TransportSendBuffer`를 `RefCountedBuffer + offset + length` 기반 값 타입으로 추가했고,
    payload `Length` 범위 밖 송신 요청을 거부하도록 했다.
  - 구현: 사용자 리뷰를 반영해 송신 시도와 소유권 판정을 `IConnection`이 아니라 `ITransport.TrySend(IConnection, TransportSendBuffer)`에 둔다.
    `IConnection`은 `Close()`/`Dispose()` 수명 계약만 노출한다.
  - 구현: `ITransport`는 lifecycle 계약만 우선 추가했고, 실제 listen/connect/accept와 SAEA 구현은 다음 단위로 남겼다.
  - 테스트: `TransportSendBuffer`의 버퍼/범위 노출, payload 범위 검증, `ITransport.TrySend` 존재, `IConnection`에
    `TransportSendBuffer` parameter 가 없는지, public 계약에 raw `Memory<byte>`/`ReadOnlyMemory<byte>` parameter 가 없는지 검증했다.
    이미 풀에 반환된 버퍼는 길이 0 요청이라도 거부되는지 검증했다.
  - 검증: focused `TransportContractTests` → 통과 4, 실패 0, 건너뜀 0. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 4. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] `RefCountedBuffer` 동시 Release/팬아웃 스트레스 테스트를 보강했다.
  - 범위: `tests/Hps.Buffers.Tests/RefCountedBufferTests.cs`.
  - 테스트: 구독자 수 0, 1, 2, 4, 8, 32명 fan-out에서 publish 가드 ref와 구독자 ref를 동시에 `Release()`하고,
    각 반복에서 풀 반환이 정확히 1회 이루어져 `RentedCount==0`으로 돌아오는지 검증했다.
  - 테스트: 64개 buffer가 동시에 in-flight 상태일 때 각 buffer의 publish 가드 ref와 구독자 ref들이 경쟁적으로 `Release()`되어도
    전체 풀 누수 없이 `RentedCount==0`으로 끝나는지 검증했다.
  - production code 수정은 없었다. 기존 `RefCountedBuffer` 구현이 동시 반환 계약을 만족해 추가 구현 없이 통과했다.
  - 검증: focused `RefCountedBufferTests` → 통과 7, 실패 0, 건너뜀 0. 전체 `dotnet test HighPerformanceSocket.slnx` → 통과 18, 실패 0, 건너뜀 0.
    `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] `BipBuffer`와 `RefCountedBuffer` private helper 주석을 보강했다.
  - 범위: `src/Hps.Buffers/BipBuffer.cs`, `src/Hps.Buffers/RefCountedBuffer.cs`.
  - 기능 변경 없이 helper별 snapshot/publish 의미, SPSC cursor 소유권, payload length publish, 반환 상태/부활 방지 의도를 주석으로 남겼다.
  - 검증: focused `BipBufferTests|RefCountedBufferTests` → 통과 11, 실패 0, 건너뜀 0. 전체 `dotnet test HighPerformanceSocket.slnx` → 통과 16, 실패 0, 건너뜀 0.

- [x] `BipBuffer`의 `Volatile.Read/Write` 호출을 cursor/count 의미 기반 helper로 정리했다.
  - 범위: `src/Hps.Buffers/BipBuffer.cs`.
  - 기능 변경 없이 `ReadCommittedCountSnapshot`, `IsCommittedCountZero`, `ReadConsumerCursorSnapshot`,
    `ReadProducerCursorSnapshot`, `ReadWatermarkSnapshot`, `PublishProducerCursor`, `PublishConsumerCursor` helper를 추가했다.
  - 목적: public 메서드 본문에서 저수준 memory primitive보다 SPSC 소유권 경계와 publish/snapshot 의미가 먼저 보이도록 한다.
  - 검증: 리팩터링 전 focused 테스트 → 통과 6. 리팩터링 후 focused 테스트 → 통과 6. 전체 `dotnet test HighPerformanceSocket.slnx` → 통과 16, 실패 0, 건너뜀 0.

- [x] `RefCountedBuffer`의 `Volatile.Read/Write` 호출을 의도 기반 helper로 정리했다.
  - 범위: `src/Hps.Buffers/RefCountedBuffer.cs`.
  - 기능 변경 없이 `ReadPublishedLength`, `PublishLength`, `ReadRefCountSnapshot`, `ReadBlockSnapshot`, `IsReturned` helper를 추가했다.
  - 목적: 호출부가 저수준 memory primitive보다 길이 publish, ref count snapshot, 반환 상태 관측이라는 의도를 드러내도록 한다.
  - 검증: 리팩터링 전 focused 테스트 → 통과 5. 리팩터링 후 focused 테스트 → 통과 5. 전체 `dotnet test HighPerformanceSocket.slnx` → 통과 16, 실패 0, 건너뜀 0.

- [x] `RefCountedBuffer` 최소 참조계수/반환 계약을 구현했다.
  - 범위: `src/Hps.Buffers/RefCountedBuffer.cs`, `src/Hps.Buffers/PinnedBlockMemoryPool.cs`, `tests/Hps.Buffers.Tests/RefCountedBufferTests.cs`.
  - Red: reflection 기반 테스트로 `PinnedBlockMemoryPool.RentCounted` 부재를 단언 실패로 확인했다.
  - 구현: `RentCounted()`, `RefCountedBuffer.AddRef()`, `Release()`, `Memory`, `Span`, `Length`, `SetLength(int)`를 추가했다.
  - 계약: 생성 ref=1, 마지막 `Release()`에서 정확히 1회 풀 반환, 과다 `Release()` 예외, 반환 후 `AddRef()` 부활 금지,
    `Length` 경계 검증, 반환 후 블록 접근 거부.
  - Green 후 테스트를 직접 public API 호출 방식으로 리팩터링해 reflection helper를 남기지 않았다.
  - 검증: focused 테스트 → 통과 5, 실패 0, 건너뜀 0. 전체 `dotnet test HighPerformanceSocket.slnx` → 통과 16, 실패 0, 건너뜀 0.

- [x] `PinnedBlockMemoryPoolTests`에서 reflection 기반 `PoolApi` 래퍼를 제거했다.
  - 범위: `tests/Hps.Buffers.Tests/PinnedBlockMemoryPoolTests.cs`.
  - 기존 테스트가 production 타입 존재 여부를 확인하기 위해 reflection 래퍼를 유지하고 있었지만,
    `PinnedBlockMemoryPool`이 이미 구현된 뒤에는 테스트가 실제 public API를 직접 검증하는 편이 더 단순하고 명확하다.
  - `System.Reflection`, `ExceptionDispatchInfo`, `PoolApi` nested class를 제거하고 `new PinnedBlockMemoryPool(...)` 호출로 바꿨다.
  - production code 수정은 없었다.
  - 검증: focused 테스트 → 통과 5, 실패 0, 건너뜀 0. 전체 `dotnet test HighPerformanceSocket.slnx` → 통과 11, 실패 0, 건너뜀 0.

- [x] `BipBuffer` must-fix **2건(M1, M2)** 을 3색 TDD로 해소했다.
  - 범위: `src/Hps.Buffers/BipBuffer.cs`, `tests/Hps.Buffers.Tests/BipBufferTests.cs`.
  - M1: capacity 끝까지 commit 후 read가 0으로 wrap하면 빈 버퍼가 다시 쓰기 가능해야 함을 Red로 확인했고,
    `Commit`에서 `_write == _capacity`를 저장하지 않고 즉시 0으로 wrap하도록 수정했다.
  - M2: SPSC 스트레스에서 `GetReadSpan()`이 커밋량보다 긴 span을 노출해 `Consume` 계약을 깨는 것을 Red로 확인했고,
    반환 길이를 `_count` 기준으로 제한(clamp)했다. `_count` 값 자체는 보정하지 않는다.
  - XML doc에 소비자는 데이터를 처리한 뒤에만 `Consume`해야 한다는 계약을 명시했다.
  - 검증: `dotnet test HighPerformanceSocket.slnx` → 통과 2, 실패 0, 건너뜀 0.

- [x] `BipBuffer` deterministic edge 테스트를 별도 리뷰 단위로 추가했다.
  - 범위: `tests/Hps.Buffers.Tests/BipBufferTests.cs`.
  - 추가한 테스트: `Capacity - 1` 실사용 용량과 full 상태, partial commit/consume, tail이 minimum size를
    만족하지 못할 때 front wrap 및 watermark 순서 보존.
  - production code 수정은 없었다.
  - 검증: `dotnet test HighPerformanceSocket.slnx` → 통과 5, 실패 0, 건너뜀 0.

- [x] `BipBuffer` seeded fuzz 테스트를 별도 리뷰 단위로 추가했다.
  - 범위: `src/Hps.Buffers/BipBuffer.cs`, `tests/Hps.Buffers.Tests/BipBufferTests.cs`.
  - 테스트: capacity 2, 3, 4, 8, 17, 64와 seed 4개 조합에서 20,000회 랜덤 write/read를 실행하고
    단순 참조 큐와 바이트 순서 및 `Count`를 비교한다.
  - Red: `capacity=3, seed=4660` 및 `capacity=4, seed=4660`에서 empty non-zero cursor 상태가 front wrap과 만나
    `GetReadSpan()`이 빈 span을 반환하는 문제가 재현됐다.
  - 수정: 버퍼가 비어 있고 `read/write`가 0이 아닌 위치에서 만난 경우에는 `minimumSize`보다 작더라도 tail을 먼저 반환한다.
    또한 tail/front 비교는 실제 front 여유(`read - 1`) 기준으로 한다.
  - 검증: `dotnet test HighPerformanceSocket.slnx` → 통과 6, 실패 0, 건너뜀 0.

- [x] `PinnedBlockMemoryPool` 최소 API와 단일스레드 테스트를 별도 리뷰 단위로 구현했다.
  - 범위: `src/Hps.Buffers/PinnedBlockMemoryPool.cs`, `tests/Hps.Buffers.Tests/PinnedBlockMemoryPoolTests.cs`.
  - Red: reflection 기반 테스트로 타입 부재를 단언 실패로 확인했다.
  - 구현: `Rent()`/`Return(byte[])`, `BlockSize`, `RentedCount`, POH pinned 배열 생성, 반환 블록 크기 검증,
    대여 카운트 음수 방지 가드를 추가했다.
  - 테스트: block size와 count 추적, 반납 블록 재사용, 잘못된 크기 반환 거부, 0 이하 block size 거부.
  - 검증: `dotnet test HighPerformanceSocket.slnx` → 통과 10, 실패 0, 건너뜀 0.

- [x] `PinnedBlockMemoryPool` 멀티스레드 대여/반환 스트레스 테스트를 별도 리뷰 단위로 추가했다.
  - 범위: `tests/Hps.Buffers.Tests/PinnedBlockMemoryPoolTests.cs`.
  - 테스트: 8개 worker가 동시에 시작해 각 10,000회 `Rent()`/`Return(byte[])`을 반복하고,
    worker 예외 없음과 종료 후 `RentedCount==0`을 검증한다.
  - production code 수정은 없었다.
  - 검증: `dotnet test HighPerformanceSocket.slnx` → 통과 11, 실패 0, 건너뜀 0.

- [x] Phase 0 스캐폴딩이 존재한다.
  - 근거: `HighPerformanceSocket.slnx`, `Directory.Build.props`, `src/Hps.Buffers`, `tests/Hps.Buffers.Tests` 확인.

- [x] Phase 1 BipBuffer 초안 검토서가 존재한다.
  - 근거: `.claude/review/phase1-bipbuffer.md`.
  - 결과: must-fix **2건(M1 deadlock, M2 크로스스레드 over-read)** 이 다음 구현 작업의 선행 조건으로 기록됨.

- [x] 핵심 자료구조/설계를 실측 검증했다(임시 하니스 사용 후 삭제).
  - BipBuffer: M1·M2 재현 및 수정 검증(`phase1-bipbuffer.md`).
  - RefCountedBuffer/Pool: 팬아웃 정확히-1회 반환·누수 0 검증, 설계 승인(`phase1-refcounted-pool.md`).
  - ITransport↔BipBuffer 연동: 송신 다중생산자(D1)·소유권(D2) 설계 결정(`phase2-transport-bipbuffer.md`).
  - 브로커 라우팅: 빈 토픽 eager-cleanup 경합(R1, ~51% 유실) 재현·회피안 검증(`phase3-broker-routing.md`).
  - Publish payload 소유권(D009): recv→팬아웃 핸드오프 결정(`phase3-publish-ownership.md`).
  - TCP 프레임 조립(D010): 파서 상태머신 실측(recv 링 64B < payload 300B, 청크 1~7B, 10만 프레임 무결성·누수 0)
    + 연결 종료 release 계약(D011) 명문화 + drop-oldest evict release(D012) 실측(720만 enqueue, cap=16,
    누수·이중반환 0)(`phase3-framing-and-close.md`).
  - 결정 반영: DECISIONS D005~D012.

- [x] 상태 관리 문서 초기 세트를 작성했다.
  - 파일: `AGENT_RULES.md`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - 목적: `PLAN.md` 기반의 장기 실행 상태와 사용자 성능 목표를 이어받을 수 있게 관리한다.
