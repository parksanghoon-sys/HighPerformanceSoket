# 검토: Phase 1 — RefCountedBuffer / PinnedBlockMemoryPool (설계 검증)

- **대상**: PLAN 의 팬아웃용 참조계수 버퍼 + 고정 블록 풀 **설계** (아직 src 에 코드 없음).
  설계를 그대로 프로토타입으로 구현해 동시성 스트레스로 검증했다(`scratch/VerifyPool`, 검토 후 삭제).
- **요약 판정**: **승인** — 아래 구현 계약을 지키는 한 설계는 건전하다.

## 실측 결과 (모두 통과)
| 검증 | 결과 |
|---|---|
| 팬아웃 정확히-1회 반환 (구독자 0~32명, 동시 Release × 50,000 반복) | ✅ 각 1회 반환, 누수 0 |
| 고동시성 해머 (20,000 버퍼 동시 in-flight, 균형 AddRef/Release) | ✅ 누수 0, 이중 반환 0 |
| 과다 Release 가드 | ✅ 예외 발생 |

검증한 프로토타입의 핵심:
- `Pool.Rent/Return` 은 `Interlocked` 카운트 + `ConcurrentQueue`. `RentedCount` 로 누수 감지.
- `RefCountedBuffer`: 생성 시 `_ref=1`. `Release` 가 0 도달 시 `Interlocked.Exchange(_returned,1)` 로
  **정확히 1회만** 풀 반환. `AddRef` 가 0→1 증가를 감지하면 **부활(use-after-free)** 예외.

## 구현 시 반드시 지킬 계약 (must-keep)
1. **0 도달 반환은 Interlocked 로 정확히 1회.** `if (Interlocked.Decrement(ref _ref) == 0)` 안에서만
   반환하고, 이중 반환을 `Interlocked.Exchange` 가드로 막는다.
2. **부활 금지 — AddRef 순서 계약.** 발행 팬아웃에서 **모든 구독자 몫의 AddRef 를, 어떤 Release 가
   0 에 도달할 수 있기 *전에* 완료**해야 한다. 권장: 생성(ref=1) → 구독자 M 명만큼 AddRef →
   구독자에게 배포 → 마지막에 **발행자 자신의 ref 를 Release**. 구독자 송신 완료가 아무리 빨라도
   발행자 ref(=최소 1) 가 남아 있어 0 으로 떨어지지 않는다.
   - **안티패턴**: 구독자를 순회하며 "보내고 나서 AddRef" 식으로 lazy 증가시키면, 먼저 보낸 구독자의
     완료 Release 가 ref 를 0 으로 만들어 반환된 뒤 다음 AddRef 가 부활시킨다. 금지.
3. **AddRef 부활 가드**(0→1 감지 예외)는 디버그 자산으로 유지 권장. 위 계약을 어기면 즉시 드러난다.

## should-fix / 후속
- **반환 시 블록 크기 검증**: `Return(byte[])` 에서 `block.Length == BlockSize` 단언(다른 풀 블록 혼입 방지).
- **풀 상한**: 무한정 캐싱 방지 위해 최대 보관 개수 옵션(초과분은 버려 GC 회수). C10K 메모리 상한과 연계.
- **블록 주소 노출**: RIO `RIORegisterBuffer` / io_uring fixed buffer 등록을 위해, POH 고정 배열의
  안정된 주소(또는 `Memory<byte>`+`MemoryHandle`)를 노출하는 API 를 Phase 2/5/6 에서 추가.
- **`SetLength`/`Memory`/`Span`**: 팬아웃 payload 길이를 담는 `Length` 와 무복사 뷰 제공.

## 반드시 추가할 테스트
- 팬아웃 정확히-1회 반환(동시 Release, 구독자 수 가변) + 누수 0(`RentedCount==0`).
- 고동시성 해머(다수 버퍼 동시) — 이중 반환/부활 0.
- 과다 Release 예외, (선택) 부활 시나리오가 가드에 걸리는지.
