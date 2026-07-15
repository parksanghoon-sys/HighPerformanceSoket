# PLAN.md — 단계별 구현 계획 (Codex 실행용)

규칙·불변식은 [`AGENTS.md`](./AGENTS.md) 를 따른다. 이 문서는 **무엇을, 어떤 순서로** 만드는지 정의한다.
각 Phase 는 독립적으로 검증 가능하며, **순서대로** 진행한다. Phase 완료 = 해당 "완료 기준" 충족 +
Claude 검토(`.claude/review/`)의 must-fix 해소.

> Phase 1~4 로 동작하는 크로스플랫폼 Interface Server(topic 기반 pub/sub broker 메커니즘, D053)를
> 먼저 완성하고, Phase 5~6 에서 동일 `ITransport` 뒤에 RIO/io_uring 커널 백엔드를 붙인다.

---

## 현재 상태 (스냅샷)
- [x] **Phase 0~5**: 메모리, SAEA, Protocol/Broker/Server, benchmark와 Windows RIO 경로 구현·검증 완료.
- [x] **Phase 6**: Linux io_uring TCP/UDP pump, fixed buffer와 registered payload opt-in 경로 구현 완료.
- [~] **현재 hardening**: D241 transport resource 등록-pump 시작 원자성 보강 완료, 사용자 review stop.
  세부 실행 지점과 최신 검증값은 [`CURRENT_PLAN.md`](./CURRENT_PLAN.md)를 source of truth로 사용한다.

---

## Phase 1 — 메모리 계층  (`src/Hps.Buffers/`)
모든 상위 계층의 토대. 정확성이 최우선.

**작업**
1. `BipBuffer` (초안 존재): 검토 의견 반영. SPSC 가정/1바이트 갭/랩어라운드 불변식 유지.
2. `PinnedBlockMemoryPool`: 고정 블록 풀.
   - `Rent()`/`Return(byte[])`, `BlockSize`, 누수 감지용 `RentedCount`.
   - 블록은 `GC.AllocateUninitializedArray<byte>(size, pinned: true)` (POH, 영구 고정) 로 할당.
   - 스레드 안전(`ConcurrentQueue<byte[]>`). 블록 주소는 RIO/io_uring 등록용으로 후속 Phase 에서 노출.
3. `RefCountedBuffer`: 팬아웃용 참조계수 버퍼.
   - 생성 시 refcount=1. `AddRef()`/`Release()`. 0 도달 시 **정확히 1회** 풀에 반환.
   - 0 미만으로 내려가면 예외. `Memory`/`Span`/`Length` 노출. 풀에 팩토리(`RentCounted()`) 추가.

**테스트** (`tests/Hps.Buffers.Tests/`, 먼저 작성)
- BipBuffer: 빈/가득참, 꼬리→앞쪽 랩, watermark 경계에서 read 랩, 부분 commit/consume,
  용량=capacity-1 확인, **무작위 fuzz**(랜덤 write/read 시퀀스 vs 단순 참조 큐 동등성),
  SPSC 스레드 1쌍 스트레스(생산/소비 바이트 합 일치, 데이터 무결성).
- Pool: 대여/반환 후 `RentedCount==0`(누수 없음), 재사용 시 같은 블록 회수, 멀티스레드 대여.
- RefCounted: AddRef/Release 균형 시 정확히 1회 반환, 과다 Release 예외.

**완료 기준**: `dotnet test` green, 누수 0.

---

## Phase 2 — Transport 추상화 + 크로스플랫폼 백엔드  (`src/Hps.Transport/`)
**작업**
1. `ITransport` / `IConnection` 인터페이스: 리스닝/연결/수락/종료 수명주기,
   `ValueTask` 기반 send/recv 가 풀 버퍼 핸들(`RefCountedBuffer`/lease)을 다룬다(D007).
   **종료 계약(D011)**: `Close()/Dispose()` 는 송신 큐 pending·in-flight·조립 중 `RefCountedBuffer` 를
   모두 `Release` 하고 이후 enqueue 원자적 reject. 종료 후 `RentedCount==0`.
2. `SaeaTransport`: `SocketAsyncEventArgs` + 풀 버퍼, `ValueTask` 완성. TCP + UDP.
3. 백엔드 선택기: OS/capability 프로브 → 없으면 SAEA 폴백.
4. 수용기 + N I/O 워커(연결 샤딩), 선택적 코어 핀(옵션).

**테스트** (`tests/Hps.Transport.Tests/`): 로컬 루프백 TCP/UDP 에코 왕복, 동시 연결 N개 안정성,
**종료 시 버퍼 누수 0**(pending/in-flight/조립중 release 포함, `RentedCount==0`).

**완료 기준**: 에코 통합 테스트 green (Windows/Linux 양쪽에서 SAEA 로 동작).

---

## Phase 3 — 프레이밍 + 브로커(pub/sub) + 샘플
대상: `src/Hps.Protocol/`, `src/Hps.Broker/`, `src/Hps.Server/`, `samples/`

**작업**
1. 프레이밍/코덱: TCP는 **copy 기반 per-connection 조립 상태머신(D010)** — 헤더 4B 누적(분할 처리) →
   payload를 풀 `RefCountedBuffer`로 누적 복사. recv 링이 프레임을 통째로 담을 필요 없음(payload > recv 링 허용),
   `maxPayload` 상한. UDP datagram 1:1. 와이어 명령: `SUBSCRIBE <topic>`, `PUBLISH <topic> <payload>`.
   - **PUBLISH payload 소유권(D009)**: TCP는 payload를 `RefCountedBuffer`로 **1회 복사**(조립 시 청크에 걸쳐 실현)
     후 recv Consume. UDP는 datagram을 `RefCountedBuffer`로 **직접 recv**. recv 링은 살아있는 참조를 팬아웃에 넘기지 않음.
2. 브로커: `topic → 구독자 set`(concurrent, **빈 토픽 eager-cleanup 금지 = D008**) 라우팅. 발행 시
   `RefCountedBuffer` 1개 → **연결별 송신 MPSC 큐 → 단일 펌프 → SPSC 송신 BipBuffer**(D007)에 (참조+off+len)
   enqueue (**구독자당 복사 0회** 팬아웃). 수명: publish 가드 ref → 구독자별 AddRef+enqueue(실패 시 즉시 Release)
   → publish 마지막 Release → 송신 펌프 완료 후 Release → 0 도달 풀 반환.
3. 백프레셔: v1 transport 송신 큐 기본 정책은 현재 구현과 D053/D064에 맞춰 **bounded drop-oldest** 로 둔다.
   **drop-oldest(D012)**: evict한 `RefCountedBuffer`를 정확히 1회 Release. evict/dequeue/close를 단일 락으로
   직렬화(이중 release/누수 차단). 메시지 손실은 diagnostics 로 관측 가능해야 한다.
   느린 소비자 disconnect/reject, topic/endpoint 별 QoS, reliable/durable delivery 는 후속 설계 단위에서 결정한다.
4. `Hps.Server` 호스트 + 샘플 publisher/subscriber 콘솔.

**테스트** (`tests/Hps.Broker.Tests/`): 1 publisher → M subscribers 팬아웃 정확성/순서, 토픽 격리,
느린 소비자 처리, RefCount 누수 0; **R1 라우팅 경합**("Y 구독"‖"X 해지 후 Y 잔존"); **TCP 무복사-독립성**
(PUBLISH 후 recv 링 덮어써도 팬아웃 payload 무손상); 구독자 0명 publish 즉시 반환;
**D010 프레임 fuzz**(적대적 청크·recv 링 < payload·0 길이·maxPayload 경계); **D011 종료 누수 0**
(pending 남긴 채 Close + 조립중 드롭 → `RentedCount==0`); **D012 drop-oldest** 지속 과부하에서 큐 길이
≤ 용량·종료 후 `RentedCount==0`·이중 반환 0.

**완료 기준**: 통합 테스트 green + 샘플로 수동 팬아웃 확인.

---

## Phase 4 — 벤치마크 하니스  (`tests/Hps.Benchmarks/`)
BenchmarkDotNet 마이크로벤치(BipBuffer/Pool) + 부하 생성 클라이언트로 처리량(msg/s)·
지연(p50/p99)·연결 스케일링 측정. **SAEA 기준선 수치 기록**.
**완료 기준**: 재현 가능한 리포트 산출.

---

## Phase 5 — Windows RIO 백엔드  (`src/Hps.Transport.Rio/`)
P/Invoke: `WSAIoctl(SIO_GET_MULTIPLE_EXTENSION_FUNCTION_POINTER)` 로 RIO 함수테이블 →
`RIOCreateCompletionQueue`, `RIOCreateRequestQueue`, `RIORegisterBuffer`(고정 풀 등록),
`RIOReceive`/`RIOSend`, `RIONotify` + IOCP 완성 처리.
`ITransport` 구현으로 끼워 넣고 **Phase 2/3 통합 테스트를 그대로 재사용**.
**완료 기준**: RIO 백엔드로 통합 테스트 green, Phase 4 벤치로 SAEA 대비 개선 확인.

---

## Phase 6 — Linux io_uring 백엔드  (`src/Hps.Transport.IoUring/`)
P/Invoke: `io_uring_setup`, SQ/CQ mmap, `io_uring_register`(fixed buffers),
`IORING_OP_SEND`/`RECV`(또는 `SENDMSG`), 가능 시 `MSG_ZEROCOPY`/send ZC, `io_uring_enter` 제출/수확.
동일 `ITransport` 구현, 통합 테스트 재사용.
**완료 기준**: Linux 에서 통합 테스트 green + 벤치 비교.

---

## Phase 7 — 튜닝 & 비교
코어 핀, 워커 수, 버퍼 크기, Nagle off/busy-poll 파라미터 스윕. SAEA vs RIO vs io_uring 비교 리포트.
**완료 기준**: 균형 목표(처리량/지연/동시성) 대비 측정값 정리.

---

## 알려진 위험
- RIO/io_uring P/Invoke 는 디버깅 난도 높음(핸들 수명, 완성 큐 경합). Phase 1~4 검증 위에서 백엔드만
  교체하므로 회귀를 빨리 잡는다.
- 고정 풀 메모리 = 동시연결 × (send+recv 버퍼). C10K 급은 메모리 상한 계산 필요(Phase 7).
- `BipBuffer` SPSC 가정 위반 시 깨짐 — 멀티 컨슈머 필요해지면 설계 재검토(범위 밖).
