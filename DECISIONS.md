# DECISIONS.md

## D013 — 구현은 작은 기능 단위로 끊고 사용자 리뷰 뒤 다음 단위로 진행한다

- 날짜: 2026-06-10
- 상태: Accepted
- 결정: 한 사이클은 작고 리뷰 가능한 단일 기능 단위만 구현·검증·문서화한다. 독립 기능, 보강 테스트,
  리팩터링, 문서 정리는 한 번에 묶지 않고 별도 사이클로 나눈다. 기능 단위 완료 후에는 관련 파일만 stage 하여
  단일 커밋으로 남기고, 다음 단위로 자동 진행하지 않고 사용자 검토와 명시적 진행 지시를 기다린다.
- 근거: 한 번에 많은 수정이 들어가면 리뷰 비용이 커지고 결함 원인 추적이 어려워진다.
- 영향: `CURRENT_PLAN.md`는 다음 단일 리뷰 단위만 표현해야 한다. 기존 계획에 여러 독립 작업이 섞여 있으면
  현재 단위를 제외한 항목은 `TODOS.md`의 `Deferred Backlog`로 내려 명확히 기록한다. 커밋 전에는
  `git status`로 의도한 파일만 stage 되었는지 확인하고, unrelated 변경은 커밋에 포함하지 않는다.

## D001 — Phase 순서는 `PLAN.md`를 따른다

- 날짜: 2026-06-10
- 상태: Accepted
- 결정: Phase 1~4에서 크로스플랫폼 기준선 브로커를 먼저 완성하고, Phase 5~6에서 RIO/io_uring 백엔드를 붙인다.
- 근거: 메모리 계층, Transport 계약, Protocol/Broker 동작을 먼저 안정화해야 OS별 P/Invoke 백엔드의 회귀를 통합 테스트로 잡을 수 있다.
- 영향: RIO/io_uring 구현은 Phase 1~4 완료 전에는 착수하지 않는다.

## D002 — 사용자 성능 목표를 초기 기준선으로 기록한다

- 날짜: 2026-06-10
- 상태: Accepted
- 결정: 우선 목표는 4096 bytes 메시지를 100 Hz로 지연 누적 없이 처리하는 것이다.
- 근거: 사용자가 명시한 목표이며, 설계와 벤치마크의 중심 기준이 된다.
- 영향: Phase 1~3에서는 복사 최소화, pinned pool, fan-out zero-copy 불변식을 이 목표를 위한 구조적 제약으로 유지한다. Phase 4에서는 이 목표를 p50/p99 latency, throughput, queue backlog로 측정 가능하게 만든다.

## D003 — `dotnet test` green만으로 Phase 완료를 인정하지 않는다

- 날짜: 2026-06-10
- 상태: Accepted
- 결정: 테스트가 discover되지 않는 상태의 `dotnet test` 성공은 완료 기준으로 인정하지 않는다.
- 근거: 현재 `tests/Hps.Buffers.Tests`에 실제 테스트 `.cs` 파일이 없고, 테스트 0개 상태가 성공 종료 코드처럼 보일 수 있다.
- 영향: 각 Phase 완료 시 필수 테스트가 실제 discover되고 실행됐는지 확인한다.

## D004 — 다음 구현 작업은 BipBuffer must-fix TDD로 제한한다

- 날짜: 2026-06-10
- 상태: Accepted (D005로 갱신 — must-fix가 2건으로 늘어남)
- 결정: 다음 코드 변경은 `BipBuffer` M1 deadlock 재현 테스트와 최소 수정으로 제한한다.
- 근거: `.claude/review/phase1-bipbuffer.md`에 must-fix가 있으며, `BipBuffer`는 이후 pool, transport, protocol의 기반이다.
- 영향: `PinnedBlockMemoryPool`, `RefCountedBuffer`, `Hps.Transport`는 BipBuffer 테스트가 green이 될 때까지 착수하지 않는다.

## D005 — BipBuffer must-fix는 M1, M2 두 건이다 (실측 검증됨)

- 날짜: 2026-06-10
- 상태: Accepted (D004 갱신)
- 결정: `BipBuffer`는 두 건을 모두 고친다.
  - **M1**(단일스레드 deadlock): `Commit()`에서 `_write == _capacity` 상태를 저장하지 말고 즉시 wrap.
  - **M2**(크로스스레드 over-read): `GetReadSpan()`의 **반환 Span 길이**를 `Volatile.Read(ref _count)`
    이하로 제한(clamp)하고, "소비자는 데이터를 처리한 뒤에만 `Consume` 호출" 계약을 XML doc에 명시.
- 근거: 임시 하니스 실측에서 M1(데드락)과 M2(SPSC 200만 바이트에서 소비자가 미커밋 ~115만 바이트 과독,
  `_count` 음수)가 모두 재현됨. 두 수정 적용 시 단일스레드·크로스스레드 검증 통과. `.claude/review/phase1-bipbuffer.md` 참조.
- 영향(중요): "clamp"는 **반환 길이 제한**이지 `_count` 값 자체를 0 이상으로 보정하는 것이 아니다(그건 버그 은폐).
  완료 기준에 M1 회귀 테스트와 M2 SPSC 회귀 테스트(`produced==consumed`, 바이트 무결성, `Count >= 0`)를 포함한다.

## D006 — RefCountedBuffer/Pool 설계는 승인, AddRef 순서 계약을 강제한다

- 날짜: 2026-06-10
- 상태: Accepted
- 결정: PLAN의 참조계수 버퍼/고정 풀 설계를 채택한다. 0 도달 시 `Interlocked` + `Exchange` 가드로
  정확히 1회 반환. **팬아웃에서 모든 구독자 몫 AddRef를, 어떤 Release가 0에 도달하기 전에 완료**한다
  (권장: 생성 ref=1 → 구독자 M명 AddRef → 배포 → 마지막에 발행자 자신 Release).
- 근거: 실측에서 5만 반복 팬아웃·2만 동시 버퍼 모두 정확히-1회 반환·누수 0. "보내고 나서 AddRef" 식
  lazy 증가는 부활(use-after-free)을 유발. `.claude/review/phase1-refcounted-pool.md` 참조.
- 영향: 구현은 부활 가드(0→1 감지 예외)와 이중 반환 가드를 유지하고, 위 순서 계약을 테스트로 강제한다.

## D007 — 송신 경로는 "MPSC 큐 → 단일 펌프 → SPSC 송신 BipBuffer", 버퍼는 풀 핸들로 주고받는다

- 날짜: 2026-06-10
- 상태: Accepted
- 결정:
  - 팬아웃 시 다중 발행 스레드가 같은 구독자 송신 버퍼에 직접 쓰지 않는다. 발행자는 `(RefCountedBuffer,
    off, len)`을 연결별 **MPSC 큐**에 넣고, 연결당 **단일 송신 펌프**가 SPSC 송신 BipBuffer를 채운다.
  - `ITransport`/`IConnection`은 raw `Memory<byte>`가 아니라 **풀 소유 핸들**(`RefCountedBuffer`/lease)로
    버퍼를 주고받는다(RIO/io_uring 등록 식별·반환 책임·refcount 때문).
  - 수신 경로는 recv+프레이밍을 같은 I/O 워커에서 인라인 처리(단일 스레드)하여 SPSC를 자명하게 만든다.
- 근거: BipBuffer는 SPSC 전용. 다중 생산자 노출 시 깨진다. `.claude/review/phase2-transport-bipbuffer.md` 참조.
- 영향: Phase 2 인터페이스 설계 시 이 계약을 선반영한다. TODOS의 "raw Memory vs lease" 미결 질문을 lease로 확정.

## D008 — 브로커 라우팅은 빈 토픽 eager-cleanup을 금지한다 (실측 검증됨)

- 날짜: 2026-06-10
- 상태: Accepted
- 결정: `topic → 구독자 set` 라우팅에서 "빈 set이면 토픽 엔트리 즉시 제거" 최적화를 쓰지 않는다.
  기본은 **NoCleanup**(빈 토픽 미정리 + 필요 시 주기적 안전 sweep). 즉시 정리가 꼭 필요하면 **set 인스턴스
  락**으로 추가/빈-제거를 직렬화한다.
- 근거: 실측에서 순진한 eager-cleanup은 "동시 구독 vs 빈-정리" 경합으로 구독을 약 51% 유실. 영리한
  lock-free verify-retry도 여전히 약 50% 유실(틀림). NoCleanup·set-lock은 0 유실.
  `.claude/review/phase3-broker-routing.md` 참조.
- 영향: Phase 3 라우팅 구현은 R1 타깃 경합 회귀 테스트("Y 구독"‖"X 해지 후 Y 잔존")를 반드시 포함한다.
  흔한 churn 테스트만으로는 이 버그를 놓친다.

## D009 — Publish payload는 RefCountedBuffer를 소유권 단위로, TCP는 1회 복사·UDP는 직접 recv

- 날짜: 2026-06-10
- 상태: Accepted (사용자 승인)
- 결정:
  - recv→팬아웃 경계의 소유권 단위는 **`RefCountedBuffer` 하나**로 통일한다.
  - **TCP**: recv BipBuffer는 프레이밍 전용. PUBLISH payload를 풀 `RefCountedBuffer`로 **1회 복사**
    (`recvSpan.CopyTo`, 무할당) 후 recv 영역 즉시 Consume. 팬아웃은 그 버퍼 공유(구독자당 0회).
  - **UDP**: datagram을 `RefCountedBuffer`로 **직접 recv**(BipBuffer 미사용) → publish 진짜 zero-copy.
  - 수명: publish가 ref=1 가드 보유 → 구독자별 AddRef+enqueue(실패 시 즉시 Release) → publish가 마지막에
    자기 ref Release → 송신 펌프가 완료 후 Release → 0 도달 시 풀 반환.
- 근거: payload는 M개 구독자가 비동기 소비하므로 recv 링보다 오래 살아야 하나, `Span`은 ref struct라
  큐 저장 불가. recv 링 직접 무복사 전달은 원천 불가(브로커 팬아웃은 진짜 0복사 자체가 불성립).
  `.claude/review/phase3-publish-ownership.md` 참조.
- 영향: `RefCountedBuffer`에 `Span`/`Memory`/`Length`/`SetLength` 필요. UDP Transport는 RefCountedBuffer를
  recv 버퍼로 직접 사용. **`AGENTS.md §2-1` "중간 byte[] 복사 금지" 문구를 "구독자당/불필요한 관리힙 복사
  금지(TCP publish의 recv 링→메시지 버퍼 1회 복사 허용)"로 정정.** Phase 3에 TCP 무복사-독립성·백프레셔
  누수·구독자 0명 테스트 추가.

## D010 — TCP 프레임은 copy 기반 per-connection 조립 상태머신으로 처리한다 (실측 검증됨)

- 날짜: 2026-06-10
- 상태: Accepted
- 결정: recv BipBuffer는 미파싱 바이트 스트림만 담는다. 연결별 파서가 상태머신으로 조립한다.
  - Header 상태: 4바이트 길이를 누적(여러 read span/wrap에 걸쳐도 바이트 단위). 완성 시 big-endian 파싱,
    길이 검증(0 ≤ len ≤ **maxPayload**), `RefCountedBuffer(len)` 대여.
  - Body 상태: payload를 `RefCountedBuffer`로 누적 복사 후 Consume, `got == len`까지 반복. 완성 시 dispatch.
  - → recv 링이 프레임을 통째로 담을 필요 없음(payload가 recv 링보다 커도 됨). D009의 "TCP 1회 복사"가
    청크에 걸쳐 1회로 실현(각 바이트 정확히 1번 복사). `maxPayload` 상한으로 과대 할당/DoS 방지(초과 시 끊기).
- 근거: "프레임이 항상 contiguous span에 통째로 온다"는 보장은 불가능(TCP가 임의 분할). 프로토타입 실측:
  recv 링 64B < payload 300B, 청크 1~7B, 10만 프레임 무결성·누수 0. `.claude/review/phase3-framing-and-close.md`.
- 영향: 흔한 버그(헤더 분할 미처리) 주의. Phase 3 테스트에 적대적 청크·recv 링 < payload·0 길이·maxPayload
  경계·연속 다중 프레임 fuzz 포함.

## D011 — 연결 종료/Dispose는 queued + in-flight + 조립중 RefCountedBuffer를 모두 release한다

- 날짜: 2026-06-10
- 상태: Accepted
- 결정: `IConnection.Close()/Dispose()` 계약으로 다음을 보장한다.
  1. 송신 경로를 원자적으로 "closed" 표시 → 이후 `TryEnqueue`는 false(발행자가 D009대로 자기 AddRef 즉시 Release).
  2. 송신 MPSC 큐를 drain하며 각 pending 항목 `Release`.
  3. 송신 펌프의 in-flight 버퍼를 펌프 unwind 시 `Release`.
  4. drain과 펌프 dequeue는 상호배타(close 이후 펌프는 dequeue 0) → 이중 release 금지(가드로 검출).
  5. recv 측: 조립 중이던 파서의 부분 수신 `RefCountedBuffer`(`_cur`)도 종료 시 `Release`.
  6. 경합: 발행자 `AddRef`+`TryEnqueue` 중 close가 끼어들면 `TryEnqueue`가 원자적으로 reject → 발행자 Release.
- 근거: enqueue 성공 후 종료 시 pending/in-flight release가 미정의면 느린 소비자 끊기 정책에서 누수 직결.
  외부 검토 Major. `.claude/review/phase3-framing-and-close.md`.
- 영향: Phase 2/3에 "pending 항목 남긴 채 Close + 느린 소비자 끊기" 후 `pool.RentedCount == 0`·이중 반환 0
  테스트, 조립 중 연결 드롭 시 `RentedCount == 0` 테스트 추가.

## D012 — drop-oldest backpressure는 evict한 RefCountedBuffer를 정확히 1회 Release한다 (실측 검증됨)

- 날짜: 2026-06-10
- 상태: Accepted
- 결정: 백프레셔 정책 "drop-oldest"에서 송신 큐가 가득 차 가장 오래된 항목을 evict할 때, **그 evict된
  `RefCountedBuffer`를 정확히 1회 `Release`** 한다(보내지 않으므로). evict(producer)·dequeue(pump)·drain(close)는
  **단일 락/단일 소유자로 직렬화**하여 같은 항목이 두 번 제거되지 않게 한다 → 이중 release/누수 차단.
- 근거: enqueue 실패(D009)·종료(D011)와 달리 drop-oldest는 *이미 enqueue된* 항목을 능동 제거하므로 별도
  release 지점이다. 누락 시 누수, evict와 pump dequeue가 같은 head를 경합하면 이중 release. 프로토타입 실측:
  6 producers × 300k × 4 seed(=720만 enqueue), cap=16(대량 eviction) + 동시 pump + close-drain에서
  누수 0·이중 반환 0. `.claude/review/phase3-framing-and-close.md`.
- 영향: Phase 3 백프레셔 구현은 단일 락 직렬화 + evict-release를 따른다. 테스트: 지속 과부하 drop-oldest에서
  큐 길이 ≤ 용량 유지, 종료 후 `RentedCount==0`, 이중 반환 0.
