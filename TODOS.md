# TODOS.md

## Archive

이 파일은 현재 실행 가능한 항목과 소수의 deferred backlog 만 유지한다. 긴 완료 이력은 archive 를 본다.

- 완료 이력 원문: `docs/agent-state/backlog/completed-history-2026-06-18.md`
- 전체 pre-compaction snapshot: `docs/agent-state/snapshots/2026-06-18-pre-compaction/`

## Current TODOs

- 현재 Codex가 자동으로 이어서 실행할 항목은 없다.
  - 최신 완료 단위: 2026-06-22 UDP remote lease pure sweep 구현.
  - 다음 작업은 사용자 리뷰 뒤 finding 이 있으면 먼저 반영한다.
  - finding 이 없으면 아래 Deferred Backlog 중 하나만 Current TODO 로 승격한다.

## Deferred Backlog

- [ ] `P2_LATER` UDP optional lease tracker 와 sweep 을 구현한다.
  - 무엇이 남았는지: remote-wide unsubscribe primitive, owner/key/설정/clock 설계(D073), 내부 options, lease table/activity 모델, 순수 sweep 메서드는 있지만 handler wiring, host timer 트리거는 없다.
  - 왜 defer 되었는지: D072에서 기본 idle expiry 는 비활성으로 두고, 설계와 구현을 작은 단위로 분리하기로 했다.
  - objective: 운영자가 명시적으로 idle expiry 를 켰을 때 특정 UDP remote subscription 을 주기적으로 정리하는 선택적 cleanup 경로를 만든다.
  - relevant context: D060, D072, D073, `docs/superpowers/specs/2026-06-22-udp-optional-lease-sweep-design.md`, `docs/superpowers/plans/2026-06-22-udp-optional-lease-sweep.md`, `SubscriptionTable.UnsubscribeAll(IUdpEndpoint, EndPoint)`.
  - 관련 파일/범위: `src/Hps.Broker/`, `src/Hps.Server/`, 관련 tests.
  - 현재 상태: lease/sweep owner(Broker 소유·Server 트리거), key, 내부 options(기본 비활성), `TimeProvider` 시간 추상화가 D073으로 확정됐다. 내부 `UdpLeaseOptions`, `UdpRemoteLeaseTracker` activity 모델, `SweepExpired(DateTimeOffset)` 순수 sweep 은 구현됐다.
  - resolved: owner 계층, 설정 표면(내부 options, 기본 비활성), clock abstraction(`TimeProvider`)은 D073에서 결정됐다. C# 이름 충돌 때문에 활성 factory 는 `CreateEnabled(...)`로 확정했다. tracker는 `(IUdpEndpoint, EndPoint)`당 lease 1개를 유지하고 endpoint close 시 해당 endpoint lease와 subscription을 제거한다. sweep 은 timeout 을 초과한 remote target 을 모든 topic 에서 제거한다.
  - open questions: 기본 idle timeout/sweep interval 값, 운영자용 public 설정 표면은 여전히 별도 단위로 남는다.
  - next step: 구현 계획 Task 4, `BrokerUdpDatagramHandler`가 subscribe/unsubscribe/publish/endpoint-close activity 를 tracker 로 위임하고 `SweepExpiredUdpLeases(DateTimeOffset)` entry point 를 노출하게 한다. host timer 는 그 다음 단위다.

- [ ] `P2_LATER` stable subscriber identity 와 reconnect rebinding 을 설계한다.
  - 무엇이 남았는지: v1 subscription 은 runtime endpoint 수명에 묶여 있고 reconnect 후 자동 rebinding 은 없다.
  - 왜 defer 되었는지: D058/D059에서 stable identity 는 handshake/configuration/control-plane 결정을 동반하므로 v1 밖으로 뺐다.
  - objective: 실제 요구가 생기면 TCP/UDP 공통 subscriber identity, duplicate 처리, reconnect semantics 를 정한다.
  - relevant context: D058, D059, D060, `docs/superpowers/specs/2026-06-16-endpoint-identity-policy.md`.
  - 관련 파일/범위: `src/Hps.Broker/`, `src/Hps.Protocol/`, `src/Hps.Server/`, samples, 관련 tests.
  - next step: 요구가 확인되면 먼저 wire/control-plane 설계를 작성한다.

- [ ] `P3_NICE` 실제 host/metrics surface 가 생기면 server-level diagnostics model 을 설계한다.
  - 무엇이 남았는지: D068로 `BrokerServer` 단순 pass-through diagnostics API 는 v1에 추가하지 않기로 했다.
  - 왜 defer 되었는지: 현재 서버는 단일 injected `ITransport` 를 감싼 얇은 host 이며, diagnostics 소비자는 테스트/benchmark 중심이다.
  - objective: 실제 host/운영 API가 구체화된 뒤 server-level diagnostics model 이 필요한지 결정한다.
  - relevant context: D041, D042, D056, D062, D066, D068, `docs/superpowers/specs/2026-06-18-server-diagnostics-surface-design.md`.
  - 관련 파일/범위: `src/Hps.Server/`, `src/Hps.Transport/`, host/sample 코드, 관련 tests.
  - next step: metrics/exporter 또는 server-only consumer 요구가 나오면 별도 설계로 승격한다.

## Completed

최근 완료 항목만 유지한다. 전체 완료 이력은 `docs/agent-state/backlog/completed-history-2026-06-18.md`를 본다.

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
