# 검토: Phase 3 — TCP 프레임 조립(D010) + 연결 종료 release 계약(D011)

구현 전 남은 두 설계 결정. 외부 검토(Major×2)에서 제기됨. D010은 프로토타입 실측으로 검증, D011은 계약 명문화.

---

## D010 — TCP 프레임이 contiguous read span / recv 링을 걸칠 때의 파서 정책

### 문제
`BipBuffer.GetReadSpan()`은 단일 연속 span을 돌려준다. TCP는 임의 바이트 단위로 도착하므로:
- 4바이트 길이 헤더가 watermark 경계에 걸쳐 분할될 수 있다.
- payload가 현재 read span에 일부만 있거나, recv 링 용량보다 클 수 있다.
- → "한 프레임이 항상 contiguous span에 통째로 들어온다"는 보장은 **불가능**(recv가 분할을 통제 못함).

### 결정 — copy 기반 per-connection 조립 상태머신
recv BipBuffer는 **미파싱 바이트 스트림**만 담는다. 연결별 파서가 상태머신으로 조립한다:
1. **Header 상태**: 4바이트 길이를 작은 4B 버퍼에 **누적**(여러 read span/wrap에 걸쳐도 바이트 단위로 모음).
   4B 완성 시 big-endian 파싱 → 길이 검증(0 ≤ len ≤ **maxPayload**) → 풀에서 `RefCountedBuffer(len)` 대여.
2. **Body 상태**: payload를 `RefCountedBuffer`로 **누적 복사**(`readSpan.CopyTo(buf.Span.Slice(got))` 후 Consume),
   `got == len`까지 read span을 거쳐 반복. 완성 시 메시지 dispatch → (D009 수명 규칙으로 release).
- 이로써 D009의 "TCP 1회 복사"가 **청크에 걸쳐 1회**로 실현된다(각 payload 바이트는 정확히 1번 복사).
- **recv BipBuffer는 프레임을 통째로 담을 필요가 없다.** payload가 recv 링보다 커도 됨.
- `maxPayload` 상한을 두어 잘못된/악의적 길이 프리픽스로 인한 과대 할당·DoS를 방지(초과 시 연결 끊기).

### 실측 검증 (통과)
프로토타입(검증된 FixedBip + 파서 상태머신 + RefCountedBuffer)으로:
- recv 링 **64B < maxPayload 300B**, recv 청크 **1~7B**(헤더/본문 강제 분할 + 잦은 wrap), payload 0~300(0 포함).
- 5개 시드 × 20,000 프레임 = **10만 프레임 전부 무결성 일치, 누수 0**(`RentedCount==0`).
- 결론: 위 조립 정책이 모든 경계/wrap/대형 payload에서 정확.

### 구현/테스트 메모
- 헤더도 분할될 수 있으므로 "4B 다 모일 때까지 누적"을 빠뜨리지 말 것(흔한 버그).
- 테스트: 적대적 청크(1B 포함)·recv 링 < payload·0 길이·maxPayload 경계·연속 다중 프레임 fuzz.

---

## D011 — 연결 종료/Dispose 시 queued + in-flight RefCountedBuffer release 계약

### 문제
D009는 enqueue 실패 즉시 Release, send 완료 후 Release를 정의한다. 그러나 **enqueue 성공 후** 연결 종료/
송신 펌프 중단/Dispose 시, 송신 MPSC 큐에 남은 항목과 in-flight send descriptor의 release가 미정의였다.
느린 소비자 끊기 정책에서 pending 항목이 남으면 **`RefCountedBuffer` 누수**로 직결된다.

### 결정 — `IConnection.Close()/Dispose()` 계약
종료 시 다음을 보장한다:
1. 송신 경로를 **원자적으로 "closed" 표시** → 이후 `TryEnqueue`는 false 반환(발행자는 D009대로 자기 AddRef를 즉시 Release).
2. 송신 MPSC 큐를 **drain하며 각 pending 항목을 `Release`**.
3. 송신 펌프가 dequeue해 보내던 **in-flight 버퍼를 펌프 unwind 시 `Release`**.
4. **drain과 펌프 dequeue는 상호배타**여야 한다(같은 동기화 하에서 closed 설정+drain → close 이후 펌프는
   아무것도 dequeue하지 않음). 이중 release 금지(`RefCountedBuffer`의 이중 반환 가드로 회귀 검출).
5. **recv 측**: 조립 중이던 파서의 `_cur`(부분 수신 `RefCountedBuffer`)도 종료 시 **`Release`**(누수 방지).
6. 경합: 발행자가 `AddRef`+`TryEnqueue` 중 close가 끼어들면 `TryEnqueue`가 원자적으로 reject → 발행자 Release.

### 테스트 (Phase 2/3)
- 연결을 열어 다수 publish/enqueue 후, **pending 항목이 남은 상태로 Close** + 느린 소비자 강제 끊기 시나리오에서
  종료 후 **`pool.RentedCount == 0`**(누수 0), 이중 반환 0.
- 조립 중(`_cur` 보유) 연결 드롭 시에도 `RentedCount == 0`.

---

---

## D012 — drop-oldest backpressure evict release (실측 검증)

### 문제
백프레셔 "drop-oldest"는 큐가 가득 차면 **이미 enqueue된 가장 오래된 항목을 능동 제거**한다. 이는
enqueue 실패(D009)·종료(D011)와 **별개의 release 지점**이다. 누락 시 누수. 또한 evict(producer)와
dequeue(pump)가 같은 head를 경합하면 같은 항목을 두 번 빼서 **이중 release** 위험.

### 결정
- evict한 `RefCountedBuffer`를 **정확히 1회 `Release`**.
- enqueue-with-evict / dequeue(pump) / drain(close)를 **단일 락(또는 단일 소유자)으로 직렬화** →
  각 항목은 큐에서 정확히 1번 제거 → 정확히 1번 release.

### 실측 검증 (통과)
6 producers × 300,000 × 4 seed(= **720만 enqueue**), cap=16(대량 eviction) + 동시 pump + close-drain →
**누수 0, 이중 반환 0**.

### 테스트
지속 과부하 drop-oldest에서 큐 길이 ≤ 용량 유지, 종료 후 `RentedCount==0`, 이중 반환 0.

---

## 종합
세 결정을 문서에 못박았으니(D010 실측·D011 계약·D012 실측) Phase 1 구현(BipBuffer M1·M2 TDD)으로 진행 가능.
