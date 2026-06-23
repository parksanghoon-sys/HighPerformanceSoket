# TODOS.md

## Archive

이 파일은 현재 실행 가능한 항목과 소수의 deferred backlog 만 유지한다. 긴 완료 이력은 archive 를 본다.

- 완료 이력 원문: `docs/agent-state/backlog/completed-history-2026-06-18.md`
- 전체 pre-compaction snapshot: `docs/agent-state/snapshots/2026-06-18-pre-compaction/`

## Current TODOs

- [ ] `P1_SOON` Phase 4 backlog 를 재평가하고 다음 구현 단위를 설계한다.
  - 무엇이 남았는지: stable subscriber identity / UDP lease sweep must-fix 체인이 닫혔으므로, Phase 4 원래 흐름으로 돌아가 다음 작은 구현 단위를 골라야 한다.
  - 왜 지금 해야 하는지: 현재 `Deferred Backlog`에는 server-level diagnostics P3만 남아 있고, baseline/observability 관련 spec/plan 문서에는 아직 승격 가능한 후속이 있을 수 있다.
  - objective: `CURRENT_PLAN.md`, `DECISIONS.md`, 최근 review/spec/plan 문서를 대조해 지금 바로 실행 가능한 가장 작은 Phase 4 작업을 하나로 좁힌다.
  - 관련 파일: `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`, `docs/superpowers/specs/`, `docs/superpowers/plans/`, `docs/agent-state/reviews/`.
  - next step: 후보 작업을 하나 선택하고, touched files/검증 경로/커밋 경계까지 설계한다.

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
