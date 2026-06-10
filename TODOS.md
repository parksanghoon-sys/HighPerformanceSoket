# TODOS.md

## Current TODOs

- 현재 Codex가 자동으로 이어서 실행할 항목은 없다.
  - D013 리뷰 게이트에 따라 `ITransport.TrySend` 송신 큐 close/drain release 구현을 사용자 검토한 뒤 다음 단위로 진행한다.

## Deferred Backlog

- [ ] `P1_SOON` 송신 펌프의 in-flight 완료 Release 경로를 구현한다.
  - 무엇이 남았는지: `TransportConnection.TryDequeueSend`로 송신 펌프가 가져간 항목을 실제 send 완료 경로에서
    정확히 한 번 Release 하는 작은 컴포넌트/메서드와 테스트가 필요하다.
  - 왜 defer 되었는지: 이번 사이클은 pending 큐 수락, close reject, close pending drain, in-flight 항목을 close 가 건드리지 않는
    기본 소유권 경계까지만 구현했고, 펌프 완료는 D013에 따라 다음 리뷰 단위로 분리한다.
  - objective: D011의 in-flight release 계약을 소켓 I/O 없이 먼저 검증해, 이후 SAEA/RIO/io_uring completion callback이 같은 규칙을 재사용하게 한다.
  - relevant context: `PLAN.md` Phase 2, DECISIONS D007·D011, `.claude/review/phase2-transport-bipbuffer.md`,
    `.claude/review/phase3-framing-and-close.md`. 현재 `TransportBase.TrySend`는 open 연결이면 pending queue 에 소유권을 넘기고,
    `TransportConnection.Close()`는 pending 만 drain 한다. 이미 dequeue 된 항목은 pump 가 Release 해야 한다.
  - 관련 파일/범위: `src/Hps.Transport/`, `tests/Hps.Transport.Tests/`.
  - 현재 상태: `TransportBase`, `TransportConnection`, pending queue, close drain, close reject 테스트는 존재한다.
    실제 송신 펌프 completion, SAEA 구현은 아직 없다.
  - known blockers/open questions: drop-oldest 정책은 D012로 확정됐지만, 다음 단위에서는 기본 pump completion release부터 처리하고
    drop-oldest evict release 는 이후 별도 단위로 분리하는 편이 리뷰하기 쉽다.
  - next step: 사용자 리뷰 후 계속 진행 지시가 있으면 pump 가 dequeue 한 항목을 completion 경로에서 Release 해
    `RentedCount==0`으로 돌아가는 Red 테스트부터 작성한다.

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

- [x] `ITransport.TrySend` 송신 큐의 enqueue/close release 계약을 구현했다.
  - 범위: `src/Hps.Transport/`, `tests/Hps.Transport.Tests/TransportSendQueueTests.cs`.
  - Red: `TransportBase` 타입 부재를 reflection 기반 테스트의 단언 실패로 확인했다.
  - 구현: `TransportBase.TrySend(IConnection, TransportSendBuffer)`가 내부 `TransportConnection`에 pending 송신을 위임하도록 했다.
  - 구현: `TransportConnection.Close()`는 close 표시와 pending drain 을 같은 lock 안에서 처리하고, pending 항목의
    `RefCountedBuffer`를 Release 한다. close 이후 `TrySend`는 false 를 반환해 호출자가 Release 하게 한다.
  - 구현: 송신 펌프가 `TryDequeueSend`로 가져간 in-flight 항목은 close 가 Release 하지 않도록 분리했다.
  - 구현: `TransportBase.TrySend`가 pending 큐에 넣기 전에 `TransportSendBuffer`의 live buffer 접근을 확인해
    `default(TransportSendBuffer)` 같은 생성자 미통과 요청이 close drain 까지 지연되지 않게 했다.
  - 테스트: open 연결에서 TrySend 성공 후 publish 가드 ref 를 해제해도 close 전까지 pool 이 반환되지 않고,
    close drain 에서 반환되는지 검증했다.
  - 테스트: closed 연결의 TrySend false 경로에서 Transport 가 소유권을 가져가지 않아 호출자가 Release 해야 함을 검증했다.
  - 테스트: default 송신 요청은 pending 큐에 들어가기 전에 즉시 거부되어 close drain 시점의 늦은 실패를 만들지 않는지 검증했다.
  - 테스트: Close idempotency 와 in-flight 항목을 close 가 Release 하지 않는 경계를 검증했다.
  - 검증: focused `TransportSendQueueTests` → 통과 5, 실패 0, 건너뜀 0. Transport 전체 → 통과 9. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 9. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] Phase 2 `ITransport`와 버퍼 소유권 계약을 구체화했다.
  - 범위: `src/Hps.Transport/`, `tests/Hps.Transport.Tests/`, `HighPerformanceSocket.slnx`.
  - Red: `Hps.Transport.TransportSendBuffer` 타입 부재를 reflection 기반 테스트의 단언 실패로 확인했다.
  - 구현: `TransportSendBuffer`를 `RefCountedBuffer + offset + length` 기반 값 타입으로 추가했고,
    payload `Length` 범위 밖 송신 요청을 거부하도록 했다.
  - 구현: 사용자 리뷰를 반영해 송신 시도와 소유권 판정을 `IConnection`이 아니라 `ITransport.TrySend(IConnection, TransportSendBuffer)`에 둔다.
    `IConnection`은 `Close()`/`Dispose()` 수명 계약만 노출한다.
  - 구현: `ITransport`는 lifecycle 계약만 우선 추가했고, 실제 listen/connect/accept와 SAEA 구현은 다음 단위로 남겼다.
  - 테스트: `TransportSendBuffer`의 버퍼/범위 노출, payload 범위 검증, `ITransport.TrySend` 존재, `IConnection`에
    `TransportSendBuffer` parameter 가 없는지, public 계약에 raw `Memory<byte>`/`ReadOnlyMemory<byte>` parameter 가 없는지 검증했다.
    이미 풀에 반환된 버퍼는 길이 0 요청이라도 거부되는지 검증했다.
  - 검증: focused `TransportContractTests` → 통과 4, 실패 0, 건너뜀 0. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 4. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] `RefCountedBuffer` 동시 Release/팬아웃 스트레스 테스트를 보강했다.
  - 범위: `tests/Hps.Buffers.Tests/RefCountedBufferTests.cs`.
  - 테스트: 구독자 수 0, 1, 2, 4, 8, 32명 fan-out에서 publish 가드 ref와 구독자 ref를 동시에 `Release()`하고,
    각 반복에서 풀 반환이 정확히 1회 이루어져 `RentedCount==0`으로 돌아오는지 검증했다.
  - 테스트: 64개 buffer가 동시에 in-flight 상태일 때 각 buffer의 publish 가드 ref와 구독자 ref들이 경쟁적으로 `Release()`되어도
    전체 풀 누수 없이 `RentedCount==0`으로 끝나는지 검증했다.
  - production code 수정은 없었다. 기존 `RefCountedBuffer` 구현이 동시 반환 계약을 만족해 추가 구현 없이 통과했다.
  - 검증: focused `RefCountedBufferTests` → 통과 7, 실패 0, 건너뜀 0. 전체 `dotnet test HighPerformanceSocket.slnx` → 통과 18, 실패 0, 건너뜀 0.
    `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] `BipBuffer`와 `RefCountedBuffer` private helper 주석을 보강했다.
  - 범위: `src/Hps.Buffers/BipBuffer.cs`, `src/Hps.Buffers/RefCountedBuffer.cs`.
  - 기능 변경 없이 helper별 snapshot/publish 의미, SPSC cursor 소유권, payload length publish, 반환 상태/부활 방지 의도를 주석으로 남겼다.
  - 검증: focused `BipBufferTests|RefCountedBufferTests` → 통과 11, 실패 0, 건너뜀 0. 전체 `dotnet test HighPerformanceSocket.slnx` → 통과 16, 실패 0, 건너뜀 0.

- [x] `BipBuffer`의 `Volatile.Read/Write` 호출을 cursor/count 의미 기반 helper로 정리했다.
  - 범위: `src/Hps.Buffers/BipBuffer.cs`.
  - 기능 변경 없이 `ReadCommittedCountSnapshot`, `IsCommittedCountZero`, `ReadConsumerCursorSnapshot`,
    `ReadProducerCursorSnapshot`, `ReadWatermarkSnapshot`, `PublishProducerCursor`, `PublishConsumerCursor` helper를 추가했다.
  - 목적: public 메서드 본문에서 저수준 memory primitive보다 SPSC 소유권 경계와 publish/snapshot 의미가 먼저 보이도록 한다.
  - 검증: 리팩터링 전 focused 테스트 → 통과 6. 리팩터링 후 focused 테스트 → 통과 6. 전체 `dotnet test HighPerformanceSocket.slnx` → 통과 16, 실패 0, 건너뜀 0.

- [x] `RefCountedBuffer`의 `Volatile.Read/Write` 호출을 의도 기반 helper로 정리했다.
  - 범위: `src/Hps.Buffers/RefCountedBuffer.cs`.
  - 기능 변경 없이 `ReadPublishedLength`, `PublishLength`, `ReadRefCountSnapshot`, `ReadBlockSnapshot`, `IsReturned` helper를 추가했다.
  - 목적: 호출부가 저수준 memory primitive보다 길이 publish, ref count snapshot, 반환 상태 관측이라는 의도를 드러내도록 한다.
  - 검증: 리팩터링 전 focused 테스트 → 통과 5. 리팩터링 후 focused 테스트 → 통과 5. 전체 `dotnet test HighPerformanceSocket.slnx` → 통과 16, 실패 0, 건너뜀 0.

- [x] `RefCountedBuffer` 최소 참조계수/반환 계약을 구현했다.
  - 범위: `src/Hps.Buffers/RefCountedBuffer.cs`, `src/Hps.Buffers/PinnedBlockMemoryPool.cs`, `tests/Hps.Buffers.Tests/RefCountedBufferTests.cs`.
  - Red: reflection 기반 테스트로 `PinnedBlockMemoryPool.RentCounted` 부재를 단언 실패로 확인했다.
  - 구현: `RentCounted()`, `RefCountedBuffer.AddRef()`, `Release()`, `Memory`, `Span`, `Length`, `SetLength(int)`를 추가했다.
  - 계약: 생성 ref=1, 마지막 `Release()`에서 정확히 1회 풀 반환, 과다 `Release()` 예외, 반환 후 `AddRef()` 부활 금지,
    `Length` 경계 검증, 반환 후 블록 접근 거부.
  - Green 후 테스트를 직접 public API 호출 방식으로 리팩터링해 reflection helper를 남기지 않았다.
  - 검증: focused 테스트 → 통과 5, 실패 0, 건너뜀 0. 전체 `dotnet test HighPerformanceSocket.slnx` → 통과 16, 실패 0, 건너뜀 0.

- [x] `PinnedBlockMemoryPoolTests`에서 reflection 기반 `PoolApi` 래퍼를 제거했다.
  - 범위: `tests/Hps.Buffers.Tests/PinnedBlockMemoryPoolTests.cs`.
  - 기존 테스트가 production 타입 존재 여부를 확인하기 위해 reflection 래퍼를 유지하고 있었지만,
    `PinnedBlockMemoryPool`이 이미 구현된 뒤에는 테스트가 실제 public API를 직접 검증하는 편이 더 단순하고 명확하다.
  - `System.Reflection`, `ExceptionDispatchInfo`, `PoolApi` nested class를 제거하고 `new PinnedBlockMemoryPool(...)` 호출로 바꿨다.
  - production code 수정은 없었다.
  - 검증: focused 테스트 → 통과 5, 실패 0, 건너뜀 0. 전체 `dotnet test HighPerformanceSocket.slnx` → 통과 11, 실패 0, 건너뜀 0.

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

- [x] `BipBuffer` seeded fuzz 테스트를 별도 리뷰 단위로 추가했다.
  - 범위: `src/Hps.Buffers/BipBuffer.cs`, `tests/Hps.Buffers.Tests/BipBufferTests.cs`.
  - 테스트: capacity 2, 3, 4, 8, 17, 64와 seed 4개 조합에서 20,000회 랜덤 write/read를 실행하고
    단순 참조 큐와 바이트 순서 및 `Count`를 비교한다.
  - Red: `capacity=3, seed=4660` 및 `capacity=4, seed=4660`에서 empty non-zero cursor 상태가 front wrap과 만나
    `GetReadSpan()`이 빈 span을 반환하는 문제가 재현됐다.
  - 수정: 버퍼가 비어 있고 `read/write`가 0이 아닌 위치에서 만난 경우에는 `minimumSize`보다 작더라도 tail을 먼저 반환한다.
    또한 tail/front 비교는 실제 front 여유(`read - 1`) 기준으로 한다.
  - 검증: `dotnet test HighPerformanceSocket.slnx` → 통과 6, 실패 0, 건너뜀 0.

- [x] `PinnedBlockMemoryPool` 최소 API와 단일스레드 테스트를 별도 리뷰 단위로 구현했다.
  - 범위: `src/Hps.Buffers/PinnedBlockMemoryPool.cs`, `tests/Hps.Buffers.Tests/PinnedBlockMemoryPoolTests.cs`.
  - Red: reflection 기반 테스트로 타입 부재를 단언 실패로 확인했다.
  - 구현: `Rent()`/`Return(byte[])`, `BlockSize`, `RentedCount`, POH pinned 배열 생성, 반환 블록 크기 검증,
    대여 카운트 음수 방지 가드를 추가했다.
  - 테스트: block size와 count 추적, 반납 블록 재사용, 잘못된 크기 반환 거부, 0 이하 block size 거부.
  - 검증: `dotnet test HighPerformanceSocket.slnx` → 통과 10, 실패 0, 건너뜀 0.

- [x] `PinnedBlockMemoryPool` 멀티스레드 대여/반환 스트레스 테스트를 별도 리뷰 단위로 추가했다.
  - 범위: `tests/Hps.Buffers.Tests/PinnedBlockMemoryPoolTests.cs`.
  - 테스트: 8개 worker가 동시에 시작해 각 10,000회 `Rent()`/`Return(byte[])`을 반복하고,
    worker 예외 없음과 종료 후 `RentedCount==0`을 검증한다.
  - production code 수정은 없었다.
  - 검증: `dotnet test HighPerformanceSocket.slnx` → 통과 11, 실패 0, 건너뜀 0.

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
