# CURRENT_PLAN.md — 현재 실행 지점

## Archive

이 파일은 현재 진입점만 유지한다. 2026-06-18 이전 상세 원문은 아래 archive 를 본다.

- 전체 pre-compaction snapshot: `docs/agent-state/snapshots/2026-06-18-pre-compaction/`
- 완료 이력 원문: `docs/agent-state/backlog/completed-history-2026-06-18.md`
- 변경 이력 원문: `docs/agent-state/changelog/2026-06.md`
- 결정 상세 원문: `docs/agent-state/decisions/2026-06.md`

## 최종 목표

고성능 소켓 기반 Interface Server 를 구현한다. 외부 source 에서 들어온 정보를 topic/data type 기준으로 받아,
구독된 TCP/UDP endpoint 로 추가 payload 복사 없이 발행하는 것이 핵심 목표다.

현재 성능 기준은 단일 stream 기준 **4096 bytes 메시지 100 Hz**다. 아직 고정 latency SLO는 없으며,
지속 부하에서 송신 큐 적체와 p99 지연 증가가 누적되지 않는 상태를 관측값으로 추적한다.

## 현재 Phase

Phase 4 — 벤치마크 하니스, SAEA 기준선 수치 기록, Interface Server endpoint/send-side 관측성 설계.

## 현재 상태 요약

- Phase 1~3의 핵심 TCP broker 경로는 완료됐다. TCP/UDP transport, protocol, broker, server 통합 경로가 존재한다.
- TCP subscriber outbound 는 D065에 따라 `4-byte big-endian length prefix + payload` frame 으로 전송한다.
- TCP/UDP pending send queue 는 capacity 16 bounded drop-oldest 정책을 유지한다(D064, D067).
- drop 관측은 transport/endpoint snapshot 기반 pull diagnostics 로 유지한다(D062, D066).
- Phase 4 benchmark 는 delivery/drop/leak hard gate 와 latency/HWM 관측값을 분리한다(D063).
- latency hard gate 전에는 반복 baseline artifact 와 summary/soft warning 을 우선한다(D069, D070).
- `--baseline-suite`로 closed-loop/open-loop raw JSON artifact 를 반복 수집할 수 있다.
- `--summarize-baseline <input-dir> --summary <output-json> [--summary-md <output-md>]`로 summary JSON과 사람이 읽는 Markdown 보조 artifact 를 생성할 수 있다.
- 2026-06-18 baseline root, `session-02`, `session-03`에는 `summary.json`과 `summary.md`가 모두 생성되어 있다.
- baseline summary 이후 report history 와 warning 승격 정책은
  `docs/superpowers/specs/2026-06-18-baseline-report-history-warning-policy-design.md`로 정리했다(D071).
- 반복 baseline session 을 빠르게 찾기 위한 전역 index 는 `docs/benchmarks/baselines/index.md`에 둔다(D071).
- UDP stale remote cleanup 은 Broker/Server 소유의 선택적 lease cleanup 으로 설계했고, 기본 idle expiry 는 비활성화한다(D072).
- `SubscriptionTable.UnsubscribeAll(IUdpEndpoint, EndPoint)`로 특정 UDP remote target 만 모든 topic 에서 제거할 수 있다(D072).
- UDP idle lease tracker/sweep 은 Broker 소유·Server timer 트리거, 내부 options(기본 비활성), `TimeProvider` 시간 소스로
  설계했다(D073). 설계는 `docs/superpowers/specs/2026-06-22-udp-optional-lease-sweep-design.md`에 있다.
- UDP optional lease sweep 구현 계획은 `docs/superpowers/plans/2026-06-22-udp-optional-lease-sweep.md`에 있다.
- 내부 `UdpLeaseOptions` 타입과 `Hps.Broker.Tests` internal 접근 경계가 생겼다. 활성 옵션 factory 는 C# 멤버 이름 충돌을 피하기 위해
  `CreateEnabled(TimeSpan, TimeSpan)` 이름을 사용한다.
- 내부 `UdpRemoteLeaseTracker`가 생겼고, UDP remote subscribe/unsubscribe/publish activity 와 endpoint close cleanup 을
  lease table 과 `SubscriptionTable`에 같은 lock 경계로 반영한다.
- `UdpRemoteLeaseTracker.SweepExpired(DateTimeOffset)`가 생겼고, 만료된 remote target 을
  `SubscriptionTable.UnsubscribeAll(IUdpEndpoint, EndPoint)`로 모든 topic 에서 제거한다.
- `BrokerUdpDatagramHandler`가 SUBSCRIBE/UNSUBSCRIBE/PUBLISH/endpoint-close activity 를 tracker 로 위임하고,
  `SweepExpiredUdpLeases(DateTimeOffset)` 내부 entry point 를 제공한다.
- BrokerServer UDP lease host timer/public settings 설계는
  `docs/superpowers/specs/2026-06-22-broker-server-udp-lease-host-timer-design.md`에 있다(D074).
  기본은 disabled 이고, 활성화 시 idle timeout/sweep interval 은 명시 입력으로만 받는다.
- `BrokerServerOptions` public 설정 타입이 생겼다. `Default`는 UDP lease sweep disabled 이고,
  `CreateWithUdpLeaseSweep(...)`는 explicit timeout/interval 과 `TimeProvider`를 저장한다.
- `BrokerServer`가 UDP lease sweep enabled options 를 받으면 `StartUdpAsync` 성공 후 `TimeProvider.CreateTimer`로 sweep timer 를 만들고,
  `StopAsync`와 start 실패 cleanup 에서 timer 를 dispose 한다. timer callback 은 `BrokerUdpDatagramHandler.SweepExpiredUdpLeases(...)`를 호출한다.
- Stable subscriber identity / reconnect rebinding 정책은 D075로 정리했다.
  기본 v1 runtime target subscription 은 유지하고, 후속 stable identity 는 `REGISTER <subscriber-id>` 기반 opt-in Broker registry 로 설계한다.
  같은 id 재등록은 새 runtime target 이 이기며, disconnected 동안 payload 는 저장하지 않는다.
- Stable subscriber identity 구현 계획은
  `docs/superpowers/plans/2026-06-22-stable-subscriber-identity.md`에 있다.
  계획은 protocol decode, pure registry, TCP handler, UDP handler, Server opt-in wiring 의 5개 커밋 단위로 나뉜다.
- Protocol 계층은 `REGISTER <subscriber-id>`와 `UNREGISTER <subscriber-id>`를 token-only command 로 decode 한다.
  stable identity token 은 다음 Broker 단계에서 `TcpCommand.Topic` span view 를 해석해 사용한다.
- Broker 계층에는 내부 `SubscriberIdentity`, `SubscriberRegistrationResult`, `SubscriberRegistry` pure model 이 있다.
  registry 는 identity별 topic metadata 와 현재 online `BrokerSubscriber` target 을 연결하며 payload 는 저장하지 않는다.
- TCP `BrokerTcpFrameHandler`는 선택적 `SubscriberRegistry`가 주입되면 `REGISTER`/`UNREGISTER`와 registered
  target 의 subscribe/unsubscribe/close cleanup 을 registry 로 연결한다. 기본 public constructor 는 기존 runtime target 동작을 유지한다.
- UDP `BrokerUdpDatagramHandler`는 선택적 `SubscriberRegistry`가 주입되면 UDP `REGISTER`/`UNREGISTER`,
  registered remote subscribe/unsubscribe, same-id remote rebind, endpoint close cleanup 을 registry 와 lease tracker 로 연결한다.
- `BrokerServerOptions`는 stable subscriber identity opt-in 설정과 retention timeout 을 노출한다.
  `BrokerServer`는 enabled options 일 때 TCP/UDP handler 에 shared `SubscriberRegistry`를 주입하고,
  host 소유 retention timer 로 disconnected identity metadata 를 sweep 한다.
- Late `REGISTER`는 같은 runtime target 의 기존 runtime 구독을 stable identity metadata 로 자동 이관하지 않고 제거한다(D076).
  stable identity client 는 `REGISTER` 후 필요한 topic 을 다시 `SUBSCRIBE`해야 한다.

## 최근 완료 단위

- 이번 단위 — Stable subscriber identity late REGISTER stale subscription cleanup
  - self-review 중 `SUBSCRIBE` 후 `REGISTER` 순서에서 metadata 에 없는 runtime 구독이 close cleanup 이후 stale target 으로 남을 수 있음을 확인했다.
  - `SubscriberRegistry.Register(...)`가 새 target 을 stable identity 에 매핑하기 전에 같은 runtime target 의 기존 routing 구독을 제거하도록 보정했다.
  - D076으로 late REGISTER 정책을 기록하고 stable identity 설계 문서에 command ordering 기준을 추가했다.
  - 검증: `SubscriberRegistryTests` Red assertion failure 1개 확인, focused registry tests 10개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 215개 통과.
- 이번 단위 — Stable subscriber identity BrokerServer opt-in wiring
  - Task 5로 `BrokerServerOptions`에 stable identity enabled/retention 설정과 factory/with method 를 추가했다.
  - `BrokerServer`가 enabled options 일 때 shared `SubscriberRegistry`를 만들고 TCP/UDP handler 에 같은 registry 를 주입한다.
  - TCP 또는 UDP start 성공 후 retention timer 를 한 번만 만들고, StopAsync 에서 UDP lease timer 와 함께 dispose 한다.
  - 검증: stable identity Server/Options Red assertion failure 7개 확인, focused stable tests 7개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 214개 통과.
- 이번 단위 — Stable subscriber identity UDP handler wiring
  - Task 4로 `BrokerUdpDatagramHandler`에 optional `SubscriberRegistry` internal constructor 를 추가했다.
  - UDP `REGISTER`/`UNREGISTER`, registered remote subscribe/unsubscribe, same-id remote rebind,
    duplicate target datagram-drop, endpoint close retention 을 registry/lease tracker 경로로 연결했다.
  - `UdpRemoteLeaseTracker.RemoveRemote(...)`와 `MarkSubscribedTopics(...)`를 추가해 rebind 시 old lease 제거와 new lease topic 복구를 처리한다.
  - 검증: internal constructor 부재 Red assertion failure 4개 확인, focused UDP handler tests 12개 통과.
- 이번 단위 — Stable subscriber identity TCP handler wiring
  - Task 3으로 `BrokerTcpFrameHandler`에 optional `SubscriberRegistry`와 `TimeProvider` internal constructor 를 추가했다.
  - TCP `REGISTER`/`UNREGISTER`, registered target subscribe/unsubscribe, same-id reconnect rebind,
    same-target different-id reject/close, connection close cleanup 을 registry 경로로 연결했다.
  - 검증: internal constructor 부재 Red assertion failure 4개 확인, focused TCP handler tests 11개 통과.
- 이번 단위 — Stable subscriber identity pure registry
  - Task 2로 `SubscriberIdentity`, `SubscriberRegistrationResult`, `SubscriberRegistry`를 추가했다.
  - registry 는 registered target 의 subscribe/unsubscribe metadata, same-id rebind, duplicate target conflict,
    disconnect retention, explicit unregister, disconnected sweep, UDP endpoint cleanup 을 다룬다.
  - 검증: reflection contract Red assertion failure 2개, behavior Red assertion failure 10개 확인,
    focused broker identity/registry tests 15개 통과.
- 이번 단위 — Stable subscriber identity protocol decode
  - Task 1로 `TcpCommandKind.Register/Unregister`와 decoder 분기를 추가했다.
  - `REGISTER`/`UNREGISTER`는 기존 `SUBSCRIBE`/`UNSUBSCRIBE`와 같은 단일 token 문법을 사용한다.
  - 검증: Red assertion failure 9개 확인, focused protocol tests 24개 통과.
- 이번 단위 — Stable subscriber identity 구현 계획
  - D075 설계를 구현 가능한 5개 Task 로 분해했다.
  - 각 Task 는 Red-Green-Refactor, touched files, produced interfaces, 검증 명령, 커밋 경계를 포함한다.
  - 검증: 계획 self-review 로 spec coverage, placeholder, type consistency 를 확인했고,
    `git diff --check`, solution build/test 로 문서 변경이 빌드 상태를 깨지 않음을 확인한다.
- 이번 단위 — Stable subscriber identity / reconnect rebinding 정책 설계
  - D058/D059/D060 이후 남아 있던 stable subscriber identity 후속 방향을 D075로 정리했다.
  - 기본 runtime target 모델을 유지하고, 후속 opt-in `REGISTER` identity registry, duplicate/rebind, disconnect retention, 테스트 순서를 설계했다.
  - 검증: 실제 `BrokerSubscriber`, `SubscriptionTable`, TCP/UDP handler, 기존 identity policy 문서와 대조했고,
    `git diff --check`, solution build/test 로 문서 변경이 빌드 상태를 깨지 않음을 확인한다.
- 이번 단위 — BrokerServer UDP lease host timer wiring
  - D074 구현 두 번째 단위로 `BrokerServerOptions`를 `BrokerServer`와 `BrokerUdpDatagramHandler`에 연결했다.
  - UDP start 성공 시 sweep timer 를 만들고, Stop/start 실패 시 timer 수명을 정리한다.
  - 검증: Red assertion failure 2개 확인, focused tests 2개 통과, 생성자 reflection 제거 후 focused tests 2개 통과,
    solution build 경고 0/오류 0, solution tests 175개 통과.
- 이번 단위 — BrokerServerOptions public 설정 타입
  - D074 구현 첫 단위로 `BrokerServerOptions`를 추가했다.
  - 기본 disabled, 0 이하 timeout/interval 거부, explicit 값과 `TimeProvider` 보존을 테스트했다.
  - 검증: Red assertion failure 3개 확인, focused tests 3개 통과, reflection 제거 후 focused tests 3개 통과,
    solution build 경고 0/오류 0, solution tests 173개 통과.
- 이번 단위 — BrokerServer UDP lease host timer 설계
  - D074로 `BrokerServerOptions` public 설정 표면, 기본 disabled 정책, 명시 timeout/interval 입력, `TimeProvider` timer 수명,
    Broker friend assembly 경계를 확정했다.
  - 검증: 설계 self-review, `git diff --check`, solution build/test.
- 이번 단위 — UDP lease tracker handler wiring
  - D073 구현 Task 4로 `BrokerUdpDatagramHandler`를 `UdpRemoteLeaseTracker`에 연결했다.
  - public constructor 는 기존처럼 disabled lease options 를 사용해 기본 동작을 보존하고, internal constructor 로 테스트/후속 host wiring 이 options/time provider 를 주입한다.
  - 검증: Red assertion failure 2개 확인, focused handler tests 8개 통과, reflection 제거 후 focused handler tests 8개 통과,
    solution build 경고 0/오류 0, solution tests 170개 통과.
- 이번 단위 — UDP remote lease pure sweep
  - D073 구현 Task 3으로 `UdpRemoteLeaseTracker.SweepExpired(DateTimeOffset)`를 추가했다.
  - plan 예시의 survivor remote setup 은 같은 시점 구독이면 함께 만료되므로, survivor를 늦게 구독하도록 테스트를 보정했다.
  - 검증: Red assertion failure 3개 확인, focused tests 8개 통과, reflection 제거 후 focused tests 8개 통과,
    solution build 경고 0/오류 0, solution tests 168개 통과.
- 이번 단위 — UDP remote lease tracker activity
  - D073 구현 Task 2로 내부 `UdpRemoteLeaseTracker`와 focused 테스트를 추가했다.
  - 계획서의 compile-failure Red는 AGENTS의 assertion-failure Red 규칙에 맞춰 reflection 기반 타입 부재 assertion 실패로 보정했다.
  - tracker는 기본 비활성 options 에서 기존 subscription 동작을 보존하고, 활성 options 에서 `(IUdpEndpoint, EndPoint)`당 lease 1개를 추적한다.
  - 검증: Red assertion failure 5개 확인, focused tests 5개 통과, reflection 제거 후 focused tests 5개 통과,
    solution build 경고 0/오류 0, solution tests 165개 통과.
- 이번 단위 — UDP lease options
  - D073 구현 Task 1로 내부 `UdpLeaseOptions`와 테스트 assembly internal 접근 경계를 추가했다.
  - 계획서의 `Enabled(...)` factory 이름은 `Enabled` property 와 C# 멤버 이름이 충돌하므로 `CreateEnabled(...)`로 보정했다.
  - 검증: Red assertion failure 확인, focused tests 3개 통과, solution build 경고 0/오류 0, solution tests 160개 통과.
- 이번 단위 — UDP optional lease sweep 구현 계획
  - D073 설계를 내부 options, lease tracker activity, 순수 sweep, handler wiring 의 4개 커밋 단위로 쪼갰다.
  - host timer, public settings, 기본 timeout 값 확정은 별도 후속 범위로 남겼다.
  - 검증: 계획 self-review 완료, `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 157개 통과.
- 이번 단위 — UDP optional lease tracker / sweep owner 설계
  - lease/sweep owner(Broker 소유·Server 트리거), key, 내부 options 설정 표면(기본 비활성), `TimeProvider` 시간 추상화,
    sweep 의 `UnsubscribeAll(IUdpEndpoint, EndPoint)` 사용 방식을 D073으로 확정했다.
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 157개 통과.
- 이번 단위 — UDP remote-wide unsubscribe primitive
  - D072 idle sweep 선행 API로 `(IUdpEndpoint, EndPoint)` 조합을 모든 topic 에서 제거하는 `SubscriptionTable` overload 를 추가했다.
  - 검증: focused Red/Green/Refactor 완료, solution build 경고 0/오류 0, solution tests 157개 통과.
- 이번 단위 — UDP stale remote idle expiry 설계
  - UDP remote cleanup owner, key, activity 갱신 규칙, sweep 범위, 다음 최소 구현 단위를 정리했다.
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 156개 통과.
- 이번 단위 — baseline history index 추가
  - 2026-06-18 baseline root/session-02/session-03 summary artifact 를 전역 index 에 연결했다.
  - report history 와 warning soft-signal 정책을 D071로 확정했다.
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 156개 통과.
- 이번 단위 — baseline report history/warning 정책 설계
  - baseline summary 이후 report history 단위, summary artifact 역할, warning-as-failure 보류 조건을 provider-independent 설계로 정리했다.
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 156개 통과.
- `6a3d747 docs: add baseline summary markdown artifacts`
  - 2026-06-18 baseline root/session-02/session-03 에 `summary.md` 보조 artifact 를 생성했다.
  - 검증: build 0/0, solution tests 156개 통과, `git diff --check` 통과.
- `17df6d0 feat: wire baseline summary markdown cli`
  - `--summarize-baseline`에 `--summary-md <output-md>` 선택 옵션을 연결했다.
  - JSON summary 는 canonical artifact 로 유지하고 Markdown 은 리뷰용 보조 출력으로 둔다.
- `8a48faa feat: write baseline summary markdown`
  - `BaselineSummaryMarkdownWriter`를 추가했다.

## 다음 단일 작업 단위

Stable subscriber identity 구현 계획 Task 1~5와 late REGISTER cleanup 보강이 완료됐다.

다음 단위는 구현 리뷰 대기다. `.claude/review/` 검토에서 must-fix 가 나오면 그 항목을 다음 작은 커밋 단위로 처리한다.
새 기능으로 넘어가기 전에는 stable identity 전체 흐름(protocol → registry → TCP/UDP handler → Server opt-in)을 리뷰 대상으로 둔다.

## 이번 단위의 검증 경로

이번 단위는 Stable subscriber identity late REGISTER stale subscription cleanup 이다.

- Red: `dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter FullyQualifiedName~SubscriberRegistryTests`
  에서 late REGISTER 이후 pre-register runtime 구독이 남는 assertion failure 1개를 확인했다.
- Green: 같은 focused registry tests 10개가 통과했다.
- 최종 검증: `git diff --check` 통과, `dotnet build HighPerformanceSocket.slnx --no-restore` 경고 0/오류 0,
  `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 전체 215개 통과.

## 이번 작업에서 건드리지 않는 범위

- benchmark schema 변경
- CI workflow 또는 warning-as-failure 정책 구현
- latency hard gate 확정
- RIO/io_uring backend 구현
- stable identity 인증/권한 검증, persistence, payload replay, diagnostics friendly-name 노출
