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
  `SubscriptionTable.UnsubscribeAll(IUdpEndpoint, EndPoint)`로 모든 topic 에서 제거한다. 아직 handler wiring 은 없다.

## 최근 완료 단위

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

사용자 리뷰 대기.

이번 구현 단위 리뷰가 다음 게이트다.
리뷰 finding 이 있으면 먼저 반영한다. finding 이 없으면 `TODOS.md`의 Deferred Backlog 중 UDP optional lease sweep 구현을 Task 4부터 진행한다.

## 이번 단위의 검증 경로

이번 단위는 UDP optional lease sweep 구현 계획 Task 3, UDP remote lease pure sweep 추가다.

- Red: `UdpRemoteLeaseTrackerTests`에 sweep tests 를 reflection 기반으로 먼저 추가해 `SweepExpired` 메서드 부재에 따른 `Assert.NotNull` 실패 3개를 확인했다.
- Green: `SweepExpired(DateTimeOffset)`를 추가해 idle timeout 초과 remote target 을 모든 topic 에서 제거한다.
- Refactor: reflection 기반 Red helper 를 direct internal API 호출로 정리하고 focused tests 8개 통과를 확인했다.
- 계획 보정: plan 예시의 survivor remote 는 expired remote 와 같은 시점에 구독하면 함께 만료되므로, survivor를 늦게 구독하도록 테스트 setup 을 보정했다.
- 최종 검증: `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- 최종 검증: `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- 최종 검증: `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 168개 통과/실패 0.

## 이번 작업에서 건드리지 않는 범위

- benchmark schema 변경
- CI workflow 또는 warning-as-failure 정책 구현
- latency hard gate 확정
- RIO/io_uring backend 구현
- stable subscriber identity 구현
- BrokerUdpDatagramHandler lease wiring
- BrokerServer host timer/public settings 구현
