# CURRENT_PLAN.md - 현재 실행 지점

## 목표

- 고성능 TCP/UDP Interface Server에서 4096바이트 메시지를 100 Hz로 처리한다.
- 정확성, 누수 없음, bounded backpressure를 먼저 보장하고 지연과 처리량은 재현 가능한 측정으로 판단한다.
- 상위 Protocol/Broker는 OS별 transport 구현을 모르며, 기본 transport 의미는 명시적 근거 없이 바꾸지 않는다.

## 현재 상태

- Phase 1~5의 메모리, SAEA, Protocol/Broker/Server, benchmark, RIO 경로가 구현되어 있다.
- Phase 6 io_uring은 native queue, TCP/UDP pump, fixed buffer registration과 registered payload opt-in 경로까지 구현되어 있다.
- D231 원격 Linux gate에서 production TCP payload의 registered pool hit와 native `WRITE_FIXED` 사용을 확인했다.
- D236 원격 Linux gate에서 sample broker의 explicit `--transport iouring` project build와 backend native contract를 확인했다.
- 위 증거는 end-to-end zero-copy, `auto`/default 승격, latency hard gate를 뜻하지 않는다.
- `TransportFactory.CreateDefault()`는 SAEA 기본값을 유지하고 sample `auto`는 RIO preferred/SAEA fallback 의미를 유지한다.

## 최근 정리 결과

- 2026-07-10 상태 문서 압축 전 원문을 `docs/agent-state/snapshots/2026-07-10-pre-compaction/`에 보존했다.
- `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`는 현재 진입점만 남기도록 압축했다.
- Sample Broker selector의 사용되지 않는 4/5-argument overload와 전용 fallback helper를 제거했다.
- selector 정책 테스트는 실제 7-argument production entry를 직접 사용하며 public `Select`는 하나만 남았다.
- D237 legacy overload test 제안은 overload 제거로 종료됐다.
- D238로 cross-module subscription reflection을 단일 `BrokerServer.WaitForSubscriberCountAsync` seam으로 교체하는 방향을 확정했다.

## 다음 단일 작업 단위

### D238 written design 사용자 검토

- 검토 문서: `docs/superpowers/specs/2026-07-10-subscription-readiness-seam-design.md`
- 핵심 계약: `BrokerServer.WaitForSubscriberCountAsync(topic, minimumCount, timeout, cancellationToken)` 하나만 추가한다.
- wire ACK, behavior probe, event/snapshot, 새 project와 friend assembly는 추가하지 않는다.
- implementation은 사용자 문서 검토 뒤 하나의 TDD 단위로 진행한다.

## 최신 검증 기준선

- D235 local gate: solution build 경고 0/오류 0, solution tests 510/510, Sample Broker tests 25/25.
- D236 remote gate: io_uring TRX total/executed/passed 88, 실패/오류/timeout 0.
- native evidence: capability `Available`, registered payload registration과 TCP send loopback 통과,
  `registered payload fixed send path: hit` 확인.
- selector 단순화: 구조 Red가 public `Select` 3개를 검출했고, Green 후 selector tests 13/13,
  Sample Broker tests 25/25, solution build 경고 0/오류 0, solution tests 510/510이다.
- D238 설계 검증: 네 reflection 호출, TCP/UDP handler mutation 시점, D068 경계,
  Benchmark의 `Hps.Broker` reference 용도를 실제 코드와 대조했다. production code/test는 변경하지 않았다.

## 다음 후보

1. benchmark 실행과 artifact/history 분석 책임을 별도 도구 경계로 분리할지 설계한다.
2. RIO full IPv6와 server-level diagnostics는 실제 제품 요구가 열릴 때만 재평가한다.

## 이번 범위 밖

- native backend 내부 class의 일괄 병합
- default transport 승격
- end-to-end zero-copy 주장
- latency warning의 hard gate 전환
- benchmark report 기능 추가
- readiness 계약 선택 전 protocol/server 동시 구현

## Archive

- 압축 전 전체 상태: `docs/agent-state/snapshots/2026-07-10-pre-compaction/`
- 상세 변경 이력: `docs/agent-state/changelog/2026-07.md`
- 상세 결정 이력: `docs/agent-state/decisions/2026-07.md`
