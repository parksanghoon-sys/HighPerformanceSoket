# 검토: Phase 2 — ITransport ↔ BipBuffer 연동 설계 (선행 설계 검토)

- **대상**: PLAN Phase 2 의 `ITransport`/`IConnection` 와 BipBuffer/Pool 연동 **설계** (코드 전).
  TODOS 의 미해결 질문("버퍼 소유권: raw `Memory<byte>` vs pool lease/handle", "backpressure 책임")을 해소한다.
- **요약 판정**: **설계 보완 필요** — 아래 D1(다중 생산자), D2(소유권 계약) 를 확정한 뒤 Phase 2 착수.

## 핵심 발견

### D1. (must) 송신 BipBuffer 는 팬아웃 시 **다중 생산자**가 되어 SPSC 가정을 위반한다
BipBuffer 는 **SPSC 전용**(생산자 1 + 소비자 1)이다. 경로별 스레드 모델을 보면:

| 버퍼 | 생산자 | 소비자 | 동시성 |
|---|---|---|---|
| 연결별 **수신(recv)** BipBuffer | I/O 워커(recv) | **같은** I/O 워커(파서) | 단일 스레드 → 안전 |
| 연결별 **송신(send)** BipBuffer | **브로커/발행 스레드(들)** | I/O 워커(send) | 교차 스레드 |

- **수신 경로**: recv 와 프레임 파싱을 **같은 I/O 워커 스레드에서 인라인**으로 처리하면 동시성이 없어
  BipBuffer SPSC 가 자명하게 성립한다(권장). 파싱을 다른 스레드로 오프로드하면 교차 스레드 SPSC 가 되어
  phase1 의 **M2 clamp 가 필수**가 된다.
- **송신 경로(문제)**: pub/sub 팬아웃에서 **여러 발행 스레드가 같은 구독자의 송신 버퍼에 동시 enqueue**
  할 수 있다 → **다중 생산자**. BipBuffer 로 직접 받으면 깨진다.

**권장 해소 — 연결별 송신 직렬화**: 송신 BipBuffer 는 SPSC 로 유지하고, 그 앞에 **MPSC 명령 큐**를 둔다.
- 발행자들은 `(RefCountedBuffer, offset, length)` 를 연결의 `ConcurrentQueue`(MPSC)에 넣기만 한다(복사 0회, AddRef).
- 연결당 **단일 "송신 펌프"**(해당 I/O 워커)가 큐를 드레인하며 송신 BipBuffer 에 단독으로 채운다 → 생산자 1명.
- 대안(단순): 연결별 송신 락으로 생산자를 직렬화. 경합이 낮으면 충분하나, 락 없는 MPSC 큐를 권장.

> 결론: **BipBuffer 를 교차 스레드 다중 생산자에 직접 노출하지 말 것.** 송신은 "MPSC 큐 → 단일 펌프 → SPSC BipBuffer".

### D2. (must) 버퍼 소유권 계약 — raw `Memory<byte>` 가 아니라 **풀 lease/handle** 로
ITransport 는 raw `Memory<byte>` 대신 **풀 소유 버퍼 핸들**(예: `RefCountedBuffer` 또는 lease 구조체)로
주고받아야 한다. 이유:
1. **RIO/io_uring 등록**: 커널 백엔드는 사전 등록된 버퍼의 **식별자/주소**가 필요하다. raw `Memory<byte>`
   는 출처(등록 여부)를 알 수 없다. 핸들은 등록 슬롯/주소를 실어 나를 수 있다.
2. **반환 책임 명확화**: 송신 완료 시 누가 `Release`/`Return` 하는지가 핸들에 묶인다. raw Memory 는
   소유권이 모호해 이중 반환/누수를 부른다.
3. **팬아웃 refcount**: 송신 큐에 들어가는 것은 `RefCountedBuffer` 참조이며, 송신 완료 콜백이 `Release`.

**계약(초안)**:
- 수신: Transport 가 풀에서 수신 버퍼를 대여해 recv BipBuffer 를 채우고, 파서에게 `ReadOnlySpan`(무복사 뷰)만 넘긴다. 파서는 소유하지 않는다.
- 송신: 상위 계층이 `RefCountedBuffer`(AddRef 된)를 넘기고, Transport 가 송신 완료 후 `Release` 한다.

### D3. (should) Backpressure 책임은 **송신 큐(연결)** 에 둔다
구독자의 송신 MPSC 큐/BipBuffer 가 가득 차면(느린 소비자), 정책을 **연결 단위**로 적용한다:
기본 "느린 소비자 끊기", 옵션 "drop-oldest". 발행 경로는 enqueue 실패를 받아 해당 구독자만 처리하고
다른 구독자 팬아웃을 막지 않는다. (AGENTS.md §2-5, PLAN Phase 3 와 일치)

## Phase 2 착수 전 확정할 것 (체크)
- [ ] D1: 송신 경로를 "MPSC 큐 → 단일 펌프 → SPSC 송신 BipBuffer" 로 확정.
- [ ] D2: `ITransport` API 를 풀 핸들(`RefCountedBuffer`/lease) 기반으로 정의(raw `Memory<byte>` 금지).
- [ ] 수신 경로: recv+파싱을 같은 I/O 워커에서 인라인(단일 스레드)으로 둘지 확정. 오프로드 시 M2 clamp 의존.

## 검증 권고 (구현 시)
- recv 경로 단일 스레드 파싱 정확성(부분 프레임/경계) 테스트.
- send 경로 다중 발행 스레드 → 단일 펌프 직렬화에서 바이트 무결성·refcount 누수 0(이미 phase1-refcounted 에서
  refcount 자체는 검증됨; 여기서는 "MPSC→펌프" 통합을 검증).
