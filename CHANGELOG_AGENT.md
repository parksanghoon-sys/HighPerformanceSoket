# CHANGELOG_AGENT.md

## 2026-06-10 (Codex — RefCountedBuffer 동시 Release 스트레스 테스트)

### 작업 단위
- D013에 따라 `RefCountedBuffer` 동시 Release/팬아웃 스트레스 테스트만 별도 리뷰 단위로 보강했다.
- production code 수정 없이 기존 참조계수 구현이 동시 반환 계약을 만족하는지 테스트로 확인했다.

### 테스트
- 구독자 수 0, 1, 2, 4, 8, 32명 fan-out에서 publish 가드 ref와 구독자별 ref가 동시에 `Release()`되는 상황을 반복 검증했다.
- 64개 buffer가 동시에 in-flight 상태일 때 각 buffer의 여러 ref가 경쟁적으로 `Release()`되어도 종료 후 `RentedCount==0`으로 돌아오는지 검증했다.
- 새 테스트와 helper에는 무엇을 보호하는지 설명하는 한국어 주석을 남겼다.

### 상태 갱신
- `CURRENT_PLAN.md`에 테스트 18개 통과와 빌드 경고 0개 상태를 반영했다.
- `TODOS.md`에서 이번 스트레스 테스트 보강을 Completed로 이동하고, 다음 리뷰 단위를 Phase 2 `ITransport`/버퍼 소유권 계약 구체화로 남겼다.

### 검증
- `dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~RefCountedBufferTests"` → 통과 7, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → 통과 18, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

## 2026-06-10 (Codex — private helper 주석 보강)

### 작업 단위
- 사용자 검토 의견에 따라 `BipBuffer`와 `RefCountedBuffer`의 private helper 주석을 보강했다.
- 기능 변경 없이 helper가 감싼 volatile snapshot/publish 의미와 소유권/수명 경계를 설명하는 주석만 추가했다.

### 수정
- `BipBuffer` helper에 committed count, consumer cursor snapshot, producer cursor snapshot, watermark snapshot,
  producer/consumer cursor publish 의도를 설명했다.
- `RefCountedBuffer` helper에 payload length publish, ref count snapshot, live block snapshot, returned flag 의미를 설명했다.

### 상태 갱신
- `CURRENT_PLAN.md`에 private helper 주석 보강 상태와 검증 결과를 반영했다.
- `TODOS.md`에 이번 주석 보강을 Completed로 기록했고, 다음 리뷰 단위는 `RefCountedBuffer` 동시 Release/fan-out 스트레스 테스트로 유지했다.

### 검증
- `dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~BipBufferTests|FullyQualifiedName~RefCountedBufferTests"` → 통과 11, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → 통과 16, 실패 0, 건너뜀 0.

## 2026-06-10 (Codex — BipBuffer Volatile helper 리팩터링)

### 작업 단위
- 사용자 검토 의견에 따라 `BipBuffer` 내부 public 메서드 본문에 직접 보이던 `Volatile.Read/Write` 호출을 helper로 감쌌다.
- 기능 변경 없이 SPSC cursor/count 상태 관측 의미를 더 읽기 쉽게 만드는 리팩터링 단위로만 진행했다.

### 수정
- `ReadCommittedCountSnapshot`, `IsCommittedCountZero`, `ReadConsumerCursorSnapshot`, `ReadProducerCursorSnapshot`,
  `ReadWatermarkSnapshot`, `PublishProducerCursor`, `PublishConsumerCursor` helper를 추가했다.
- `Interlocked.Add(ref _count, ...)`는 생산자/소비자 간 commit/consume count 변경의 핵심이라 그대로 명시적으로 남겼다.
- `Volatile.Read/Write` 호출은 helper 영역으로 모았다.

### 상태 갱신
- `CURRENT_PLAN.md`에 BipBuffer helper 리팩터링 상태와 검증 결과를 반영했다.
- `TODOS.md`에 이번 리팩터링을 Completed로 기록했고, 다음 리뷰 단위는 `RefCountedBuffer` 동시 Release/fan-out 스트레스 테스트로 유지했다.

### 검증
- 리팩터링 전 `dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~BipBufferTests"` → 통과 6, 실패 0, 건너뜀 0.
- 리팩터링 후 `dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~BipBufferTests"` → 통과 6, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → 통과 16, 실패 0, 건너뜀 0.

## 2026-06-10 (Codex — RefCountedBuffer Volatile helper 리팩터링)

### 작업 단위
- 사용자 검토 의견에 따라 `RefCountedBuffer` 내부의 `Volatile.Read/Write` 호출을 읽기 쉬운 helper로 감쌌다.
- 기능 변경 없이 코드 읽기성을 개선하는 리팩터링 단위로만 진행했다.

### 수정
- `ReadPublishedLength`, `PublishLength`, `ReadRefCountSnapshot`, `ReadBlockSnapshot`, `IsReturned` helper를 추가했다.
- public API와 참조계수 알고리즘은 변경하지 않았다.
- `Interlocked.CompareExchange`/`Exchange`는 참조계수와 정확히-1회 반환 알고리즘의 핵심이므로 `AddRef`/`Release`/반환 경로에 명시적으로 남겼다.

### 상태 갱신
- `CURRENT_PLAN.md`에 helper 리팩터링 상태와 검증 결과를 반영했다.
- `TODOS.md`에 이번 리팩터링을 Completed로 기록했고, 다음 리뷰 단위는 `RefCountedBuffer` 동시 Release/fan-out 스트레스 테스트로 유지했다.

### 검증
- 리팩터링 전 `dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~RefCountedBufferTests"` → 통과 5, 실패 0, 건너뜀 0.
- 리팩터링 후 `dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~RefCountedBufferTests"` → 통과 5, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → 통과 16, 실패 0, 건너뜀 0.

## 2026-06-10 (Codex — RefCountedBuffer 최소 참조계수/반환 계약)

### 작업 단위
- D013에 따라 `RefCountedBuffer`의 최소 참조계수/반환 계약만 별도 리뷰 단위로 진행했다.
- 고동시성 fan-out/release 해머 테스트는 다음 보강 단위로 분리했다.

### Red
- `RefCountedBufferTests`를 먼저 추가했다.
- 컴파일 실패가 아니라 단언 실패가 되도록 임시 reflection helper를 사용했다.
- `PinnedBlockMemoryPool.RentCounted 메서드가 존재해야 한다.` 실패 5개로 Red를 확인했다.

### 구현
- `src/Hps.Buffers/RefCountedBuffer.cs`를 추가했다.
- `PinnedBlockMemoryPool.RentCounted()`를 추가해 기존 `Rent()`/`Return(byte[])` 경로를 재사용하도록 했다.
- `RefCountedBuffer`는 생성 ref=1로 시작하고, `AddRef()`/`Release()`를 Interlocked 기반으로 처리한다.
- 마지막 `Release()`가 0에 도달하면 내부 블록을 풀에 정확히 1회 반환한다.
- 이미 반환된 버퍼의 과다 `Release()`와 반환 후 `AddRef()` 부활을 계약 위반으로 거부한다.
- `Memory`/`Span`은 전체 블록을 노출하고, `Length`/`SetLength(int)`는 유효 payload 길이를 별도로 관리한다.

### 테스트
- counted buffer 대여 시 `RentedCount` 증가, `Memory`/`Span` 전체 블록 노출, `Length` 갱신, 마지막 `Release()` 반환.
- 균형 잡힌 `AddRef()`/`Release()`에서 마지막 Release 전에는 반환되지 않고 마지막 Release 에서만 반환.
- 과다 `Release()` 예외 및 풀 카운트 보존.
- 반환 후 `AddRef()` 부활 거부.
- `SetLength` 음수/용량 초과 거부 및 기존 길이 보존.
- Green 후 테스트를 직접 public API 호출 방식으로 리팩터링해 reflection helper를 제거했다.

### 상태 갱신
- `CURRENT_PLAN.md`에 RefCountedBuffer 최소 계약 구현과 테스트 16개 통과 상태를 반영했다.
- `TODOS.md`에서 최소 계약 구현을 Completed로 옮기고, 동시 Release/fan-out 스트레스 테스트를 다음 `P1_SOON` 항목으로 분리했다.

### 검증
- `dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~RefCountedBufferTests"` → 통과 5, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → 통과 16, 실패 0, 건너뜀 0.

## 2026-06-10 (Codex — PinnedBlockMemoryPool 테스트 직접 API 리팩터링)

### 작업 단위
- `PinnedBlockMemoryPoolTests`에서 production 타입을 reflection으로 호출하던 `PoolApi` nested class를 제거했다.
- `PinnedBlockMemoryPool`은 이미 public API가 존재하므로, 현재 테스트는 실제 호출 경로를 직접 검증하는 방식이 더 적합하다.
- production code 수정은 없었다.

### 수정
- `PoolApi.Create(...)` 호출을 `new PinnedBlockMemoryPool(...)`로 바꿨다.
- reflection 전용 `using System.Reflection`, `using System.Runtime.ExceptionServices`를 제거했다.
- `PoolApi` nested class를 삭제해 테스트가 타입/메서드 존재 여부가 아니라 실제 API 계약을 바로 검증하게 했다.

### 상태 갱신
- `CURRENT_PLAN.md`에 Pool 테스트가 직접 public API를 사용하도록 정리됐음을 반영했다.
- `TODOS.md`에 이번 리팩터링을 Completed로 기록했고, 다음 리뷰 단위는 `RefCountedBuffer` 최소 참조계수/반환 계약으로 유지했다.

### 검증
- `dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~PinnedBlockMemoryPoolTests"` → 통과 5, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → 통과 11, 실패 0, 건너뜀 0.

## 2026-06-10 (Codex — PinnedBlockMemoryPool 멀티스레드 스트레스 테스트)

### 작업 단위
- D013에 따라 `PinnedBlockMemoryPool` 멀티스레드 대여/반환 스트레스 테스트만 별도 리뷰 단위로 진행했다.
- production code 수정 없이 테스트 보강만 수행했다.

### 테스트
- 8개 worker를 동시에 시작해 각 10,000회 `Rent()`/`Return(byte[])`을 반복한다.
- 각 worker는 대여한 블록 길이가 `BlockSize`와 같은지 확인하고, 예외가 발생하면 테스트 스레드로 전달한다.
- 모든 worker 종료 후 `RentedCount==0`을 검증해 누수와 카운트 경합을 확인한다.

### 상태 갱신
- `CURRENT_PLAN.md`를 사용자 리뷰 대기 상태로 갱신했다.
- `TODOS.md`에서 Pool 멀티스레드 스트레스 테스트를 Completed로 옮기고,
  다음 리뷰 단위는 `RefCountedBuffer` 최소 참조계수/반환 계약으로 유지했다.

### 검증
- `dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~RentAndReturn_WhenCalledFromMultipleThreads_FinishesWithNoLeaks"` → 통과 1, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → 통과 11, 실패 0, 건너뜀 0.

## 2026-06-10 (Codex — PinnedBlockMemoryPool 최소 API)

### 작업 단위
- D013에 따라 `PinnedBlockMemoryPool` 최소 API와 단일스레드 계약 테스트만 별도 리뷰 단위로 진행했다.
- `RefCountedBuffer`와 Pool 멀티스레드 스트레스 테스트는 이번 단위에서 제외했다.

### Red
- `PinnedBlockMemoryPoolTests`를 먼저 추가했다.
- 타입이 아직 없어서 `Hps.Buffers.PinnedBlockMemoryPool, Hps.Buffers 타입이 존재해야 한다.` 단언 실패로 Red를 확인했다.

### 구현
- `src/Hps.Buffers/PinnedBlockMemoryPool.cs`를 추가했다.
- API: `PinnedBlockMemoryPool(int blockSize)`, `BlockSize`, `RentedCount`, `Rent()`, `Return(byte[])`.
- 새 블록은 `GC.AllocateUninitializedArray<byte>(BlockSize, pinned: true)`로 생성한다.
- 반납 블록 크기가 `BlockSize`와 다르면 `ArgumentException`으로 거부한다.
- `RentedCount`가 음수가 되지 않도록 Return 시 대여 카운트 가드를 둔다.

### 테스트
- block size와 `RentedCount` 추적.
- 반납 블록 재사용.
- 잘못된 크기 배열 반환 거부 및 count 보존.
- 0 이하 block size 거부.

### 상태 갱신
- `CURRENT_PLAN.md`를 사용자 리뷰 대기 상태로 갱신했다.
- `TODOS.md`에서 Pool 최소 API를 Completed로 옮기고, 멀티스레드 대여/반환 스트레스 테스트를 다음 `P1_SOON` 항목으로 분리했다.

### 검증
- `dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~PinnedBlockMemoryPoolTests"` → 통과 4, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → 통과 10, 실패 0, 건너뜀 0.

## 2026-06-10 (Codex — 테스트 의도 주석 규칙 반영)

### 작업 단위
- 사용자 지시에 따라 테스트에도 무엇을 검증하는지 주석으로 남기는 규칙을 `AGENT_RULES.md`에 추가했다.
- 장기 결정으로 DECISIONS D014를 추가했다.
- `BipBufferTests.cs`의 각 테스트에 보호하는 불변식, 회귀 사례, 경계 조건, 동시성 가정을 설명하는 주석을 추가했다.

### 검증
- `dotnet test HighPerformanceSocket.slnx` → 통과 6, 실패 0, 건너뜀 0.

## 2026-06-10 (Codex — BipBuffer seeded fuzz 테스트)

### 작업 단위
- D013에 따라 `BipBuffer` seeded fuzz 테스트만 별도 리뷰 단위로 진행했다.
- 테스트는 capacity `2, 3, 4, 8, 17, 64`와 seed 4개 조합에서 20,000회 랜덤 write/read를 실행하고,
  단순 참조 큐와 바이트 순서 및 `Count`를 비교한다.
- 실패 시 최근 operation 로그를 메시지에 포함해 재현 조건을 바로 볼 수 있게 했다.

### Red 및 원인
- Red 확인: `capacity=3, seed=4660, iteration=17`에서 `GetReadSpan()`이 빈 span을 반환했지만
  참조 큐와 `buffer.Count`에는 1바이트가 남아 있었다.
- 추가 확인: 첫 수정 후 `capacity=4, seed=4660, iteration=6`에서도 같은 계열이 재현됐다.
- 원인: 버퍼가 비어 있고 `read == write > 0`인 상태에서 producer가 front로 wrap하면
  `watermark == read`인 0길이 상단 구간을 만들 수 있다. 이 경우 consumer는 아직 `read`를 0으로
  되돌릴 기회가 없어 front 데이터를 관측하지 못한다.

### 수정
- `GetWriteSpan()`에서 버퍼가 비어 있고 cursor가 non-zero 위치에서 만난 경우에는 `minimumSize`보다 작더라도
  tail을 먼저 반환하도록 했다.
- tail/front 비교는 실제 front 여유인 `read - 1` 기준으로 바꿨다.

### 상태 갱신
- `CURRENT_PLAN.md`를 사용자 리뷰 대기 상태로 갱신했다.
- `TODOS.md`에서 fuzz 테스트를 Completed로 옮기고, 다음 리뷰 단위는 `PinnedBlockMemoryPool`로 유지했다.

### 검증
- `dotnet test HighPerformanceSocket.slnx` → 통과 6, 실패 0, 건너뜀 0.

## 2026-06-10 (Codex — BipBuffer deterministic edge 테스트)

### 작업 단위
- D013에 따라 `BipBuffer` deterministic edge 테스트만 별도 리뷰 단위로 진행했다.
- 추가한 테스트:
  - `Capacity - 1` 실사용 용량과 full 상태 검증.
  - partial commit 후 커밋된 prefix만 읽히는지 검증.
  - tail이 `minimumSize`를 만족하지 못할 때 front wrap으로 전환되고 watermark 순서가 보존되는지 검증.
- production code 수정은 없었다.

### 상태 갱신
- `CURRENT_PLAN.md`를 사용자 리뷰 대기 상태로 갱신했다.
- `TODOS.md`에서 deterministic edge 테스트를 Completed로 옮기고, fuzz 테스트는 별도 `P1_SOON` 항목으로 남겼다.

### 검증
- `dotnet test HighPerformanceSocket.slnx` → 통과 5, 실패 0, 건너뜀 0.

## 2026-06-10 (Codex — BipBuffer M1/M2 최소 구현 + 리뷰 게이트 반영)

### 작업 단위 크기 규칙 추가
- 사용자 지시에 따라 구현을 작고 리뷰 가능한 기능 단위로 나누고, 한 단위 완료 후 사용자 리뷰 전에는
  다음 기능으로 자동 진행하지 않는 규칙을 `AGENT_RULES.md`에 추가했다.
- 장기 결정으로 DECISIONS D013을 추가했다.
- 후속 사용자 지시에 따라 각 기능 단위 완료 후 관련 파일만 stage 하여 단일 커밋으로 남기고,
  unrelated 변경은 커밋에 포함하지 않는 규칙을 D013과 `AGENT_RULES.md`에 보강했다.

### BipBuffer must-fix 2건 구현
- M1: capacity 끝까지 commit 후 read가 0으로 돌아온 빈 버퍼가 다시 쓰기 가능해야 함을 Red 테스트로 확인했다.
  `Commit()`에서 `_write == _capacity` 상태를 저장하지 않고 즉시 0으로 wrap하도록 수정했다.
- M2: SPSC 스트레스에서 `GetReadSpan()`이 커밋량보다 긴 span을 노출해 `Consume` 계약을 깨는 Red 테스트를 확인했다.
  반환 길이를 `_count` 기준으로 제한했고, `_count` 값 자체는 보정하지 않았다.
- 소비자는 데이터를 처리한 뒤에만 `Consume()`해야 한다는 SPSC 계약을 XML doc에 명시했다.

### 범위 조정
- 이번 사이클은 M1/M2만 리뷰 단위로 닫는다.
- `PLAN.md`가 요구하는 추가 edge/fuzz 테스트는 `TODOS.md`의 `Deferred Backlog`로 분리했다.
  사용자 리뷰 후 계속 진행 지시가 있으면 별도 Red 테스트 사이클로 처리한다.

### 검증
- `dotnet test HighPerformanceSocket.slnx` → 통과 2, 실패 0, 건너뜀 0.

## 2026-06-10 (마무리 — drop-oldest release + CURRENT_PLAN 최신화, Claude)

### D012 (drop-oldest evict release) 확정 — 실측 검증
- 외부 검토의 남은 minor 항목. drop-oldest는 이미 enqueue된 가장 오래된 항목을 능동 제거하므로 별도
  release 지점. evict한 RefCountedBuffer를 정확히 1회 Release, evict/dequeue/close를 단일 락으로 직렬화.
- 프로토타입 실측: 720만 enqueue(cap=16, 대량 eviction) + 동시 pump + close-drain → 누수 0·이중 반환 0.
- 반영: DECISIONS D012, `AGENTS.md §2-5`, `PLAN.md` Phase 3, `phase3-framing-and-close.md`, TODOS.

### CURRENT_PLAN.md 최신화
- 검토 6건·결정 D005~D012 종결을 반영. 다음 단일 작업은 여전히 Phase 1 BipBuffer M1·M2 3색 TDD.
- 테스트 discover 재확인: 0개(D003 기준 green 아님). 첫 Red 테스트로 해소 예정.

### 검증
- D012 프로토타입 실측 통과. 프로덕션 코드 미변경(Codex 구현 대기). 구현 전 설계 결정은 모두 종결.

## 2026-06-10 (설계 결정 — TCP 프레임 조립 + 종료 release, Claude)

### 외부 검토 Major×2 반영 → D010, D011 확정
- **D010 (TCP 프레임 조립)**: recv BipBuffer는 미파싱 스트림만 담고, 파서 상태머신이 헤더 4B 누적(분할 처리)
  → payload를 RefCountedBuffer로 누적 복사. recv 링이 프레임을 통째로 담을 필요 없음(payload > recv 링 허용),
  maxPayload 상한. **프로토타입 실측**: recv 링 64B < payload 300B, 청크 1~7B, 10만 프레임 무결성·누수 0.
- **D011 (연결 종료 release 계약)**: `Close()/Dispose()`는 송신 큐 pending·in-flight·조립중 RefCountedBuffer를
  모두 Release + 이후 enqueue 원자적 reject. 종료 후 `RentedCount==0`. (느린 소비자 끊기 시 누수 방지)
- 반영: `.claude/review/phase3-framing-and-close.md` 신규, DECISIONS D010·D011, `AGENTS.md §2-7`(프레임 조립)·
  신규 `§2-8`(종료 계약), `PLAN.md` Phase 2(종료 계약)·Phase 3(프레임 조립 + D010/D011 테스트), TODOS.
- 검증: D010 프로토타입 실측 통과. 프로덕션 코드 미변경(Codex 구현 대기).

## 2026-06-10 (설계 결정 — Publish payload 소유권, Claude)

### recv→팬아웃 payload 소유권 핸드오프 확정 (D009)
- 미해결 핵심이던 "파싱한 PUBLISH payload를 어떤 소유권으로 RefCountedBuffer 팬아웃에 넘길지"를 결정.
  - TCP: recv 링은 프레이밍 전용, payload는 RefCountedBuffer로 **1회 복사** 후 recv 즉시 Consume.
  - UDP: datagram을 RefCountedBuffer로 **직접 recv**(zero-copy).
  - 수명: publish 가드 ref → 구독자별 AddRef+enqueue(실패 시 즉시 Release) → publish 마지막 Release.
- 반영: `.claude/review/phase3-publish-ownership.md` 신규, DECISIONS D009, `AGENTS.md §2-1/§2-5` 복사 불변식
  문구 정정("구독자당/불필요한 복사 금지, TCP publish 1회 복사 허용"), `PLAN.md` Phase 3, TODOS RefCountedBuffer 항목.
- `RefCountedBuffer`에 `Span`/`Memory`/`Length`/`SetLength` 필요(복사 대상·송신 뷰)로 명시.

### 테스트 discover 상태 확인
- `tests/Hps.Buffers.Tests`에 테스트 `.cs`가 없어 `dotnet test`가 0개 discover. 회귀 아님(Phase 1 TDD 미착수).
  다음 P0(M1·M2 Red 테스트)가 들어가면 discover 시작. D003대로 0개 상태는 green 불인정.

### 검증
- 설계/문서 작업. 프로덕션 코드 미변경(Codex 구현 대기).

## 2026-06-10 (검토 사이클 — Claude)

### 설계 실측 검증 + 상태 파일 동기화
- 핵심 자료구조/설계를 임시 하니스로 실측 검증하고 결과를 `.claude/review/`에 기록(하니스는 검토 후 삭제).
  - `phase1-bipbuffer.md`: **M1**(단일스레드 deadlock)·**M2**(크로스스레드 over-read, SPSC 200만 바이트에서
    소비자가 미커밋 ~115만 바이트 과독·`Count` 음수) 재현. 두 수정 적용 시 단일·크로스스레드 통과.
    M2 문구를 "반환 길이 clamp(≠ `_count` 값 보정)"로 정확히 명시.
  - `phase1-refcounted-pool.md`: 팬아웃 5만 반복·동시 2만 버퍼에서 정확히-1회 반환·누수 0. 설계 승인.
  - `phase2-transport-bipbuffer.md`: 송신 다중생산자 위험(D1) → MPSC 큐→단일 펌프→SPSC. 버퍼 소유권은 풀 핸들(D2).
  - `phase3-broker-routing.md`: 빈 토픽 eager-cleanup이 동시 구독과 경합해 약 51% 유실(20만 회 실측).
    NoCleanup·set-lock은 0 유실. 영리한 lock-free verify-retry는 여전히 틀림(약 50% 유실).
- 위 결과로 상태 파일을 갱신: BipBuffer must-fix를 1건→2건으로, 신규 결정 DECISIONS D005~D008 추가,
  `CURRENT_PLAN.md`/`TODOS.md`의 미결 질문(버퍼 소유권 등)을 해소.

### 검증
- 검증은 임시 콘솔 하니스(`dotnet run`)로 수행. 프로덕션 코드/테스트는 아직 변경하지 않음(Codex 구현 대기).
- `BipBuffer.cs`는 여전히 초안(수정 전). 다음 P0는 M1·M2를 3색 TDD로 해소하는 것.

### 남은 불확실성
- M1·M2 수정과 회귀 테스트는 아직 코드에 반영되지 않음(P0_NOW).
- 라우팅 토픽 키 누적이 실제 문제되는 규모인지는 미정(필요 시에만 sweep).

## 2026-06-10

### 상태 관리 문서 초기화
- `PLAN.md`와 `AGENTS.md` 기준으로 작업 상태 관리 파일을 추가했다.
- 사용자 목표를 현재 작업 목표에 반영했다.
  - 4096 bytes 메시지.
  - 100 Hz.
  - 지연 누적 없이 처리.
- 현재 실행 지점을 Phase 1의 `BipBuffer` must-fix TDD 작업으로 정리했다.
- `.claude/review/phase1-bipbuffer.md`의 M1 deadlock 지적을 다음 작업의 P0 항목으로 연결했다.
- 현재 테스트 프로젝트에는 실제 테스트 `.cs` 파일이 없으므로 `dotnet test` 성공만으로 완료 판단하지 않도록 기록했다.

### 검증
- 문서 작성 작업이므로 빌드/테스트는 새로 실행하지 않았다.
- 이전 확인 기준으로 `dotnet test HighPerformanceSocket.slnx`는 테스트를 discover하지 못하는 상태였다.

### 남은 불확실성
- “딜레이 없이”의 정량 기준은 아직 확정되지 않았다.
- Phase 4에서 p50/p99 latency, 큐 적체, 동시 연결 수, 팬아웃 배율을 포함한 벤치마크 기준으로 구체화해야 한다.
