# TODOS.md

## Current TODOs

- 현재 Codex가 자동으로 이어서 실행할 항목은 없다.
  - D013 리뷰 게이트에 따라 `BipBuffer` M1/M2 변경을 사용자 검토한 뒤 다음 단위로 진행한다.

## Deferred Backlog

- [ ] `P1_SOON` `BipBuffer` fuzz 테스트를 별도 리뷰 단위로 추가한다.
  - 무엇이 남았는지: M1/M2 핵심 회귀 테스트와 deterministic edge 테스트는 들어갔지만,
    무작위 write/read 시퀀스를 참조 큐와 비교하는 fuzz 테스트는 아직 필요하다.
  - 왜 defer 되었는지: D013에 따라 deterministic edge 테스트와 fuzz 테스트를 같은 리뷰 단위에 묶지 않기로 했다.
  - objective: `BipBuffer`의 랜덤 write/read 시퀀스를 단순 참조 큐와 비교해 wrap, partial commit/consume,
    watermark 전환이 길게 섞여도 바이트 순서와 Count가 유지되는지 검증한다.
  - relevant context: `PLAN.md` Phase 1 테스트 요구사항, `.claude/review/phase1-bipbuffer.md`의 "반드시 추가할 테스트",
    DECISIONS D005 및 D013.
  - 관련 파일/범위: `tests/Hps.Buffers.Tests/BipBufferTests.cs`, 필요 시 `src/Hps.Buffers/BipBuffer.cs`.
  - 현재 상태 또는 이미 시도한 접근: M1 결정적 회귀 테스트, M2 SPSC 스트레스 테스트, deterministic edge 테스트는 통과한다.
    `dotnet test HighPerformanceSocket.slnx` 결과: 통과 5, 실패 0.
  - known blockers/open questions: 사용자 리뷰 완료 전에는 다음 구현 사이클을 시작하지 않는다.
  - 가장 자연스러운 next step: 사용자가 계속 진행을 승인하면 seeded fuzz 테스트를 추가하고 필요한 최소 수정만 한다.

- [ ] `P1_SOON` `PinnedBlockMemoryPool`의 소유권 모델을 확정하고 구현한다.
  - 무엇이 남았는지: 고정 블록 풀의 public API와 반환 검증 정책을 결정하고 구현해야 한다.
  - 왜 defer 되었는지: BipBuffer must-fix와 테스트가 Phase 1의 선행 조건이다.
  - objective: 모든 I/O 버퍼를 pinned pool에서 대여하고, 누수와 이중 반환을 테스트로 검출한다.
  - relevant context: `PLAN.md` Phase 1은 `Rent()`/`Return(byte[])`, `BlockSize`, `RentedCount`, POH pinned array를 요구한다.
    **설계 검토 완료(승인)**: `.claude/review/phase1-refcounted-pool.md`, DECISIONS D006. should-fix: `Return`에서 블록 크기 검증, 풀 보관 상한 옵션, 블록 주소 노출 API(RIO/io_uring 등록용).
  - 관련 파일/범위: `src/Hps.Buffers/`, `tests/Hps.Buffers.Tests/`.
  - 현재 상태: 구현 파일 없음.
  - known blockers/open questions: (해소) raw `byte[]` 반환 + `RentedCount` 누수 감지로 충분함이 실측 확인됨.
  - next step: BipBuffer green 이후 pool API 테스트(Red)를 먼저 작성한다.

- [ ] `P1_SOON` `RefCountedBuffer`의 release 책임과 pool 반환 규칙을 구현한다.
  - 무엇이 남았는지: AddRef/Release, 0 도달 시 정확히 1회 반환, 과다 Release 예외, **`Span`/`Memory`/`Length`/`SetLength` 노출**(D009 복사 대상·송신 뷰)이 필요하다.
  - 왜 defer 되었는지: pool 구현이 선행되어야 실제 반환 경로를 검증할 수 있다.
  - objective: Phase 3 팬아웃에서 구독자당 복사 없이 참조계수 기반으로 메시지를 공유한다.
  - relevant context: `AGENTS.md`는 구독자별 payload 복사를 금지하고, `PLAN.md`는 `RefCountedBuffer` 1개를 팬아웃에 사용하도록 한다.
    **설계 검토 완료(승인)**: `.claude/review/phase1-refcounted-pool.md`(+`phase3-publish-ownership.md`), DECISIONS D006·D009.
    **필수 계약**: 팬아웃에서 publish 가드 ref 보유 → 구독자별 AddRef+enqueue(실패 시 즉시 Release) → publish 마지막 Release.
    이중 반환/부활 가드 유지. recv→팬아웃 경계 소유권 단위는 RefCountedBuffer 하나(TCP 1회 복사 / UDP 직접 recv).
  - 관련 파일/범위: `src/Hps.Buffers/`, `tests/Hps.Buffers.Tests/`, 이후 `src/Hps.Broker/`.
  - 현재 상태: 구현 파일 없음.
  - known blockers/open questions: (해소) `Release()` 이후 `Memory`/`Span` 접근 금지 — 송신측이 완료까지 ref를 보유(D007).
  - next step: Pool green 이후 RefCountedBuffer Red 테스트(팬아웃 정확히-1회, 부활/이중반환 가드)를 작성한다.

- [ ] `P1_SOON` Phase 2 착수 전에 `ITransport`와 버퍼 소유권 계약을 구체화한다.
  - 무엇이 남았는지: receive buffer, send buffer, send 완료 후 release 책임, backpressure 책임을 인터페이스 수준에서 명확히 해야 한다.
  - 왜 defer 되었는지: Phase 1 메모리 계층이 아직 완료되지 않았다.
  - objective: Transport/Protocol/Broker 사이에 중복 버퍼 경로와 소유권 모호성을 만들지 않는다.
  - relevant context: `PLAN.md` Phase 2. **설계 검토 완료**: `.claude/review/phase2-transport-bipbuffer.md`
    (+`phase3-framing-and-close.md`), DECISIONS D007·D011. 확정 사항:
    (D1) 송신은 "MPSC 큐 → 단일 펌프 → SPSC 송신 BipBuffer"(다중 생산자 직접 노출 금지).
    (D2) 버퍼는 풀 핸들(`RefCountedBuffer`/lease)로 주고받음. (수신) recv+파싱을 같은 I/O 워커 인라인.
    (D3) backpressure는 연결 단위(기본 느린 소비자 끊기 / 옵션 drop-oldest).
    (D011) `Close()/Dispose()`는 송신 큐 pending·in-flight·조립중 RefCountedBuffer를 모두 Release +
    이후 enqueue 원자적 reject. 종료 후 `RentedCount==0` 테스트 필수.
  - 관련 파일/범위: `src/Hps.Transport/`, `src/Hps.Protocol/`, `src/Hps.Broker/`.
  - 현재 상태: 프로젝트 없음.
  - known blockers/open questions: (해소) raw `Memory<byte>` 대신 lease/handle로 확정.
  - next step: Phase 1 완료 후 인터페이스 테스트와 간단한 echo 흐름부터 정의한다.

- [ ] `P2_LATER` Phase 3 브로커 라우팅의 빈 토픽 정리 경합(R1)을 회피해 구현한다.
  - 무엇이 남았는지: `topic → 구독자 set` 라우팅을 빈 토픽 eager-cleanup 없이 구현한다.
  - 왜 defer 되었는지: Phase 1~2가 선행이며, 라우팅은 Phase 3 범위다.
  - objective: 동시 구독/해지 하에서 구독 유실 없이 pub/sub 팬아웃을 라우팅한다.
  - relevant context: `.claude/review/phase3-broker-routing.md`, DECISIONS D008. 기본 NoCleanup(+주기적 안전 sweep),
    즉시 정리 필요 시 set 인스턴스 락. 영리한 lock-free eager-cleanup은 실측에서 틀림(약 50% 유실).
  - 관련 파일/범위: `src/Hps.Broker/`, `tests/Hps.Broker.Tests/`.
  - 현재 상태: 프로젝트 없음.
  - known blockers/open questions: 토픽 키 누적이 실제로 문제되는 규모인지(필요 시에만 sweep 도입).
  - next step: Phase 3 착수 시 R1 타깃 경합 회귀 테스트("Y 구독"‖"X 해지 후 Y 잔존")부터 작성한다.

- [ ] `P2_LATER` 4096 bytes × 100 Hz 목표를 Phase 4 벤치마크 기준으로 정량화한다.
  - 무엇이 남았는지: p50/p99 latency, 허용 큐 적체, 측정 시간, 동시 연결 수, TCP/UDP별 기준을 정해야 한다.
  - 왜 defer 되었는지: 벤치마크 하니스는 Phase 4 범위이며 Phase 1~3 기능 흐름이 선행되어야 한다.
  - objective: “딜레이 없이”를 재현 가능한 성능 게이트로 바꾼다.
  - relevant context: 사용 목표는 4096 bytes 메시지 100 Hz를 지연 누적 없이 처리하는 것이다.
  - 관련 파일/범위: `tests/Hps.Benchmarks/`, `src/Hps.Server/`, samples.
  - 현재 상태: 벤치마크 프로젝트 없음.
  - known blockers/open questions: 단일 publisher 기준인지, 구독자 수와 팬아웃 배율을 어떻게 둘지 추가 결정 필요.
  - next step: Phase 3 통합 테스트 green 이후 SAEA 기준선 벤치 시나리오를 작성한다.

## Completed

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
