# DECISIONS.md

## Active Architecture Decisions

- D007 - 송신 경로는 MPSC pending queue와 단일 pump를 사용한다.
- D009 - TCP publish payload는 ownership을 위해 1회 복사하고 fan-out은 subscriber당 복사하지 않는다.
- D010 - TCP frame은 connection별 copy-based assembler로 분할 header/payload를 처리한다.
- D011 - close/dispose는 pending, in-flight, assembling buffer를 정확히 해제하고 이후 enqueue를 거부한다.
- D012 - bounded drop-oldest의 evict/dequeue/close는 단일 락으로 직렬화하고 ref를 정확히 한 번 해제한다.
- D053 - 최상위 제품 경계는 단순 broker가 아니라 endpoint-aware Interface Server다.
- D238 - subscription readiness reflection은 wire ACK가 아니라 단일 in-process `BrokerServer` wait seam으로 교체한다.

## Active Transport Decisions

- D119 - `TransportFactory.CreateDefault()`는 deterministic SAEA 기본값을 유지한다.
- D120 - RIO/io_uring 선택은 base factory가 아니라 host composition 책임이다.
- D122 - RIO는 현재 IPv4 전용이며 non-IPv4 sample `auto`는 SAEA로 fallback한다.
- D211 - 검증되지 않은 production fixed-write 직접 연결은 원격 hang 때문에 rollback됐다.
- D217 - fixed send registration은 connection lifetime과 in-flight ref를 명시적으로 소유한다.
- D229 - registered payload source는 `BrokerServer`가 concrete backend를 알지 않도록 provider seam으로 노출한다.
- D231 - Linux에서 registered TCP payload의 native `WRITE_FIXED` hit를 확인했지만 zero-copy/default promotion 근거는 아니다.
- D233 - sample broker는 explicit `--transport iouring`만 제공하고 기존 `auto`/library default 의미를 유지한다.
- D236 - explicit sample remote gate는 Linux project build와 backend native contract만 수락한다.

## Active Workflow Decisions

- D013 - 한 cycle은 하나의 coherent work unit과 review stop으로 제한하고 unrelated change를 같은 commit에 섞지 않는다.
- D239 - Benchmark 실행과 reporting은 raw report JSON 논리 경계를 유지하고 실제 trigger 전에는 project를 분리하지 않는다.
- 원격 workflow의 green 결과는 artifact/TRX와 failure counter를 직접 확인한 뒤에만 evidence로 수락한다.
- 검증 결과나 작은 test-hardening마다 새 decision ID를 만들지 않는다. 구조, 계약, 목표를 바꾸는 선택만 decision으로 남긴다.

## Current Scope Boundary

- D233/D234의 4/5-argument selector source-compatibility 하위 결정은 저장소 내부 호출 부재를 확인한 뒤 superseded됐다.
- D237 legacy selector overload test 제안은 해당 overload 제거로 종료됐다.
- Sample Broker transport 선택은 public `Select` 하나를 사용하며 tests도 같은 production entry를 직접 호출한다.
- native backend의 resource owner 분리는 유지한다. 현재 과엔지니어링 정리 대상은 상태 문서, sample compatibility layer,
  private reflection readiness였으며 모두 정리했다. benchmark/report는 D239 논리 경계를 유지하고 물리 분리는 보류한다.

## Archive

- 2026-07 상세 결정: `docs/agent-state/decisions/2026-07.md`
- 2026-06 상세 결정: `docs/agent-state/decisions/2026-06.md`
- 압축 전 active/historical index 원문: `docs/agent-state/snapshots/2026-07-10-pre-compaction/DECISIONS.md`
