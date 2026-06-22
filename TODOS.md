# TODOS.md

## Archive

이 파일은 현재 실행 가능한 항목과 소수의 deferred backlog 만 유지한다. 긴 완료 이력은 archive 를 본다.

- 완료 이력 원문: `docs/agent-state/backlog/completed-history-2026-06-18.md`
- 전체 pre-compaction snapshot: `docs/agent-state/snapshots/2026-06-18-pre-compaction/`

## Current TODOs

- Stable subscriber identity 구현 계획 Task 2 — `SubscriberIdentity` / `SubscriberRegistry` pure model 구현.
  - 계획 문서: `docs/superpowers/plans/2026-06-22-stable-subscriber-identity.md`.
  - 범위: Broker 내부 pure model, identity token validation, topic set retention, rebind, unregister, disconnected sweep.
  - handler/server wiring 은 Task 3~5에서 별도 커밋으로 진행한다.

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
