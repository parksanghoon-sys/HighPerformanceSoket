# TODOS.md

## Archive

이 파일은 현재 실행 가능한 항목과 소수의 deferred backlog 만 유지한다. 긴 완료 이력은 archive 를 본다.

- 완료 이력 원문: `docs/agent-state/backlog/completed-history-2026-06-18.md`
- 전체 pre-compaction snapshot: `docs/agent-state/snapshots/2026-06-18-pre-compaction/`

## Current TODOs

- 현재 Codex가 자동으로 이어서 실행할 항목은 없다.
  - 최신 완료 단위: 2026-06-18 baseline report history/warning 정책 설계 초안 작성.
  - 다음 작업은 사용자 리뷰 뒤 finding 이 있으면 먼저 반영한다.
  - 설계가 승인되면 아래 Deferred Backlog 중 하나만 Current TODO 로 승격한다.

## Deferred Backlog

- [ ] `P1_SOON` baseline report history index 를 작은 문서 단위로 추가한다.
  - 무엇이 남았는지: 2026-06-18 baseline root/session-02/session-03 summary 는 있지만, 여러 session 의 summary 경로와 hard/warning 상태를 한곳에서 보는 history index 는 없다.
  - 왜 defer 되었는지: report history/warning 정책 설계 초안이 사용자 리뷰 전이다. 승인 전에는 index 형식을 고정하지 않는다.
  - objective: CI workflow 없이도 사람이 반복 baseline 상태를 빠르게 찾고 비교할 수 있는 provider-independent history entry point 를 만든다.
  - relevant context: `docs/superpowers/specs/2026-06-18-baseline-report-history-warning-policy-design.md`, D069, D070, `docs/benchmarks/baselines/2026-06-18/`.
  - 관련 파일/범위: `docs/benchmarks/baselines/index.md` 또는 `docs/benchmarks/baselines/2026-06-18/index.md`, root 상태 문서.
  - 현재 상태: raw JSON, `summary.json`, `summary.md`는 존재한다. `summary.md`는 session 단위 사람이 읽는 artifact 이고, cross-session index 는 없다.
  - known blockers/open questions: spec 리뷰에서 directory/index 위치가 바뀔 수 있다.
  - next step: 설계 승인 후 index 위치를 하나로 고정하고, 현 3개 session 의 hard/warning 상태와 summary 링크를 기록한다.

- [ ] `P2_LATER` stable subscriber identity 와 reconnect rebinding 을 설계한다.
  - 무엇이 남았는지: v1 subscription 은 runtime endpoint 수명에 묶여 있고 reconnect 후 자동 rebinding 은 없다.
  - 왜 defer 되었는지: D058/D059에서 stable identity 는 handshake/configuration/control-plane 결정을 동반하므로 v1 밖으로 뺐다.
  - objective: 실제 요구가 생기면 TCP/UDP 공통 subscriber identity, duplicate 처리, reconnect semantics 를 정한다.
  - relevant context: D058, D059, D060, `docs/superpowers/specs/2026-06-16-endpoint-identity-policy.md`.
  - 관련 파일/범위: `src/Hps.Broker/`, `src/Hps.Protocol/`, `src/Hps.Server/`, samples, 관련 tests.
  - next step: 요구가 확인되면 먼저 wire/control-plane 설계를 작성한다.

- [ ] `P2_LATER` UDP stale remote idle expiry 를 설계한다.
  - 무엇이 남았는지: UDP runtime subscriber target 은 `(IUdpEndpoint, EndPoint)` 조합이며 idle remote 자동 제거가 없다.
  - 왜 defer 되었는지: v1은 datagram self-command 와 explicit `UNSUBSCRIBE`를 우선했다(D060).
  - objective: UDP remote churn 환경에서 stale subscription 이 영구 보존되지 않도록 expiry 또는 host 정책을 정한다.
  - relevant context: D060, `BrokerUdpDatagramHandler`, `SubscriptionTable`, `BrokerSubscriber`.
  - 관련 파일/범위: `src/Hps.Broker/`, `src/Hps.Server/`, UDP transport tests.
  - next step: idle 기준과 cleanup owner 를 설계한다.

- [ ] `P3_NICE` 실제 host/metrics surface 가 생기면 server-level diagnostics model 을 설계한다.
  - 무엇이 남았는지: D068로 `BrokerServer` 단순 pass-through diagnostics API 는 v1에 추가하지 않기로 했다.
  - 왜 defer 되었는지: 현재 서버는 단일 injected `ITransport` 를 감싼 얇은 host 이며, diagnostics 소비자는 테스트/benchmark 중심이다.
  - objective: 실제 host/운영 API가 구체화된 뒤 server-level diagnostics model 이 필요한지 결정한다.
  - relevant context: D041, D042, D056, D062, D066, D068, `docs/superpowers/specs/2026-06-18-server-diagnostics-surface-design.md`.
  - 관련 파일/범위: `src/Hps.Server/`, `src/Hps.Transport/`, host/sample 코드, 관련 tests.
  - next step: metrics/exporter 또는 server-only consumer 요구가 나오면 별도 설계로 승격한다.

## Completed

최근 완료 항목만 유지한다. 전체 완료 이력은 `docs/agent-state/backlog/completed-history-2026-06-18.md`를 본다.

- [x] 2026-06-18 baseline report history/warning 정책 설계 초안을 작성했다.
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
