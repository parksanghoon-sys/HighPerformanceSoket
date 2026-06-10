# 검토: Phase 1 — BipBuffer (초안)

- **대상**: `src/Hps.Buffers/BipBuffer.cs` (테스트 작성 전 초안)
- **요약 판정**: **조건부 승인** — 아래 must-fix 2건(M1, M2)을 고친 뒤 테스트 작성 진행.
- **검증 방법**: 임시 하니스(`scratch/Verify`, 검토 후 삭제)로 실제 `BipBuffer` 를 직접 구동.
  M1 결정적 재현, 무작위 fuzz(참조 큐 동등성, 6 caps × 4 seeds × 200k iter), SPSC 2스레드
  200만 바이트 스트레스를 실행해 아래 두 결함을 **실측 확인**했다.

## 구현 필수 항목 (이 4가지를 반드시 충족)
1. **M1 회귀 테스트**: `write == capacity` 이후 버퍼가 비면 다시 쓰기 가능해야 한다(데드락 없음).
2. **M1 수정**: `Commit()` 에서 `_write == _capacity` 상태를 **저장하지 말고 즉시 wrap** 한다.
3. **M2 회귀 테스트**: SPSC 스트레스에서 `produced == consumed`, 바이트 시퀀스 무결성, `Count >= 0`.
4. **M2 수정**: `GetReadSpan()` 의 **반환 길이를 `_count` 기준으로 제한(clamp)** 하고, XML doc 에
   "소비자는 데이터를 처리한 뒤에만 `Consume` 호출" 계약을 명시한다.

> ⚠️ **용어 주의(중요)**: "clamp" 는 **`GetReadSpan()` 이 돌려주는 길이**를 `Volatile.Read(ref _count)`
> 이하로 제한한다는 뜻이다. **`_count` 필드 값 자체를 0 이상으로 보정하는 것이 절대 아니다** —
> 그건 버그(소비/생산 카운트 불일치)를 숨기는 잘못된 수정이다. `_count` 가 음수가 되면 그것은
> 신호이지 보정 대상이 아니며, 올바른 수정(반환 길이 제한)을 하면 `_count` 는 애초에 음수가 되지 않는다.

전반적으로 SPSC 소유권 분리(_write/_watermark=생산자, _read=소비자), 1바이트 갭으로 full/empty
구분, watermark 기반 랩 처리 방향은 적절하다. 다만 (1) 커서가 물리적 끝(capacity)에 닿는 경계의
**데드락(M1)** 과, (2) 크로스스레드에서 읽기 영역이 커밋량을 초과하는 **데이터 손상(M2)** 이 있다.

### 실측 결과 요약
| 항목 | 초안 BipBuffer | M1+M2 수정본 |
|---|---|---|
| M1 결정적(cap=8) | **FAIL — 데드락 재현** | OK |
| fuzz (단일스레드, 4.8M iter) | **FAIL — M1 liveness** | OK |
| SPSC 2스레드 200만 바이트 | (해당 없음) | **클램프 전 FAIL → 클램프 후 OK** |

> SPSC 클램프 전 실측: `produced=845,631` 인데 `consumed=2,000,000`, `count=-1,154,369`(음수).
> 소비자가 미커밋 영역 약 115만 바이트를 과독함. → M2.

## must-fix

### M1. write 가 capacity 에 도달한 뒤 read 가 0 으로 랩하면 영구 정지(deadlock)
**재현 시나리오**
1. `read > 0` 인 상태에서 꼬리 영역을 가득 채워 `Commit` → `_write == _capacity` 저장됨
   (`GetWriteSpan` 의 `read>0` 꼬리 분기는 `tail = _capacity - _write` 전체를 돌려주므로 가능).
2. 이후 소비자가 데이터를 모두 소비 → `Consume` 의 "랩되지 않음" 분기에서 `newRead == _capacity`
   가 되어 `_read = 0`. 이때 `_count == 0`.
3. 최종 상태: `_write == _capacity`, `_read == 0`, `_count == 0` (논리적으로는 빈 버퍼).
4. 그런데 `GetWriteSpan` 의 `read == 0` 분기는 `t = _capacity - _write - 1 = -1 → 0` 을 돌려준다.
   → **빈 버퍼인데 쓰기 공간 0**. 생산자/소비자 모두 진행 불가.

**원인**: `_write` 가 `_capacity` 값으로 "저장"될 수 있는데, 그 상태는 `read==0` 분기와 결합하면
가용 공간 계산이 음수가 된다.

**권장 수정**: `Commit` 에서 결과 `write == _capacity` 가 되면 **즉시 앞쪽으로 랩**한다.
이 순간 `read > 0` 이 보장된다(소비자는 아직 `_capacity` 까지 소비할 수 없으므로). 따라서 모호함 없이
랩 상태로 전이할 수 있다:

```csharp
public void Commit(int bytes)
{
    if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
    if (bytes == 0) return;

    int write = _write + bytes;
    if (write > _capacity)
        throw new InvalidOperationException("Commit 이 직전 GetWriteSpan 영역을 초과했다.");

    Interlocked.Add(ref _count, bytes);

    if (write == _capacity)
    {
        // 꼬리를 끝까지 채웠다. capacity 값을 그대로 두면 read==0 과 만나 교착이 되므로
        // 즉시 앞쪽으로 랩한다(이 시점 read>0 보장). 상단 데이터 영역의 끝을 watermark 로 기록.
        _watermark = _capacity;
        write = 0;
    }
    Volatile.Write(ref _write, write);
}
```

> 참고: 이 수정 후 `_write` 는 항상 `[0, _capacity)` 범위가 되어, `GetReadSpan` 의 랩 분기
> (`write < read`)와 `Consume` 의 watermark 비교가 일관되게 동작한다. `count` 를 write 발행 전에
> 증가시키는 것도 의도적(소비자가 새 write 를 보기 전에 count 가 먼저 보이도록)이니 유지할 것.

### M2. 크로스스레드에서 `GetReadSpan` 이 커밋량을 초과하는 영역을 반환 → 데이터 손상
**증상(실측)**: SPSC 스트레스에서 소비자가 생산자보다 115만 바이트 더 소비, `_count` 가 음수로 추락.
**원인**: `GetReadSpan` 이 읽기 영역 길이를 커서 기하(`write - read` 또는 `watermark - read`)로만
계산하고 **권위값 `_count` 로 한정하지 않는다**. 단일스레드에서는 기하 == count 라 문제없지만,
크로스스레드에서는 소비자가 `_write`/`_watermark` 를 `_count` 보다 앞서(또는 찢긴 상태로) 관측할 수
있어, 아직 커밋되지 않은 영역까지 읽어 버린다.

**권장 수정**: **반환 Span 의 길이**를 `Volatile.Read(ref _count)` 이하로 제한한다(`_count` 는 데이터
기록·커밋 완료 *후* 증가하므로, 길이를 그 값으로 제한하면 항상 커밋된 접두부만 노출된다).
`_count` 필드를 건드리는 게 아니라, 노출 길이만 줄이는 것임에 유의:
```csharp
public ReadOnlySpan<byte> GetReadSpan()
{
    int avail = Volatile.Read(ref _count);          // 권위값(커밋 완료 바이트 수)
    if (avail == 0) return ReadOnlySpan<byte>.Empty;
    int read = _read;
    int write = Volatile.Read(ref _write);
    int geom = (write > read) ? (write - read) : (Volatile.Read(ref _watermark) - read);
    int len = geom < avail ? geom : avail;          // 반환 '길이'를 avail 이하로 제한(=clamp)
    return _buffer.AsSpan(read, len < 0 ? 0 : len); // _count 값은 그대로 둔다
}
```
> 이 클램프를 적용하자 동일 SPSC 스트레스가 200만 바이트 무결성 통과로 바뀌었다(실측).
> 쓰기측(`GetWriteSpan`)은 `_read` 를 Volatile 로 읽고, 소비자가 데이터 처리 *후* `_read` 를
> 전진시키므로 추가 클램프 없이 안전했다. 다만 방어적으로 가용 전역 여유(`capacity-1-count`)로
> 한 번 더 클램프하는 것을 권장한다.

**SPSC 계약 명문화(필수)**: 이 자료구조는 "생산자 1 + 소비자 1" 전용이며, 소비자는 **`GetReadSpan`
으로 받은 데이터를 모두 처리한 뒤에만 `Consume` 을 호출**해야 한다는 계약을 XML doc 에 명시할 것.

## should-fix
- **S1. `GetWriteSpan(minimumSize)` 가 min 미만/0 을 돌려줄 수 있음**은 설계상 정상이나, 호출자가
  반드시 반환 길이를 확인하도록 XML doc 에 더 강조하고, Phase 2 통합 시 "공간 부족 시 재시도/대기"
  헬퍼를 Transport 계층에 두는 것을 권장.
- **S2. 갭 1바이트 손실**은 의도된 것이나, `Capacity`/실사용 용량의 관계를 노출하는
  `UsableCapacity => _capacity - 1` 프로퍼티를 추가하면 호출자 혼동이 준다.

## 반드시 추가할 테스트 (M1 회귀 포함)
- **M1 회귀**: capacity 작게(예: 8) 두고 (꼬리 가득 채움 → 전량 소비 → 다시 가득 채움) 사이클을
  여러 번 반복해도 매 사이클 쓰기/읽기가 진전되는지 확인.
- 무작위 fuzz: 랜덤 길이 write/commit 와 read/consume 를 수천 회 섞어, 단순 참조 큐(List/Queue)와
  바이트 순서·내용 동등성 비교. 매 스텝 `Count`·연속성 불변식 검증.
- **M2 회귀**: SPSC 스레드 1쌍 스트레스(수백만 바이트). 생산 총량 == 소비 총량, 바이트 시퀀스
  무결성, 그리고 **`Count` 가 절대 음수가 되지 않음**을 단언. 클램프 누락 시 반드시 실패하도록.
