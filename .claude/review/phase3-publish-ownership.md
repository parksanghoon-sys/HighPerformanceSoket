# 검토: Phase 3 — Publish payload 소유권 핸드오프 (설계 결정)

- **대상**: recv BipBuffer에서 파싱한 PUBLISH payload를 `RefCountedBuffer` 팬아웃으로 넘기는 소유권/수명 설계.
- **요약 판정**: **결정 확정** (사용자 승인). DECISIONS **D009** 로 기록.

## 문제 (소유권/수명 불일치)
- recv 경로는 I/O 워커가 recv BipBuffer를 채우고, 같은 스레드 파서가 그 위에서 무복사로 프레임을 읽는다.
- 파싱한 PUBLISH payload는 recv 링 안의 `ReadOnlySpan<byte>` 뷰다. 파서가 `Consume` 하면 그 영역은
  후속 recv 로 덮어쓰여진다.
- 그러나 팬아웃은 **M개 구독자가 비동기로(느린 구독자 포함)** 소비하므로 payload 바이트가 recv 영역보다
  **오래 살아야** 한다. `Span` 은 ref struct 라 큐에 저장조차 불가.
- → **recv 링에서 팬아웃으로 직접 무복사 전달은 원천적으로 불가능.** (브로커는 같은 바이트를 M개 다른
  소켓에 다른 시각에 보내므로 진짜 0복사 자체가 성립하지 않는다. 공유+refcount 가 곧 최소복사다.)

## 결정 — `RefCountedBuffer` 를 recv→팬아웃 경계의 유일한 소유권 단위로 통일
- **TCP**: recv BipBuffer는 **스트림 재조립/프레이밍 전용**. PUBLISH 프레임 파싱 시 payload를 풀에서 빌린
  `RefCountedBuffer` 로 **1회 복사**(`recvSpan.CopyTo(buf.Span)`, 무할당) 후 recv 영역 **즉시 Consume**.
  팬아웃은 그 버퍼를 공유(구독자당 0회). recv 파이프라인이 느린 구독자에 막히지 않는다.
- **UDP**: datagram 을 풀에서 빌린 `RefCountedBuffer` 로 **직접 recv**(BipBuffer 미사용). 팬아웃이 직접
  공유 → **publish 경로 진짜 zero-copy**. (D007 "버퍼는 풀 핸들로"와 일치)
- recv BipBuffer 는 **절대 살아있는 참조를 팬아웃에 넘기지 않는다**(복사본 또는 미사용).

## 소유권/수명 규칙 (refcount 균형)
1. 수신측이 `RefCountedBuffer` 생성(ref=1, `SetLength(payloadLen)`). 이 ref=1 은 "publish 작업"의 **가드**.
2. 브로커가 구독자 set 스냅샷을 돌며 각 구독자에 대해 **AddRef → 송신 MPSC 큐에 `(buf, 0, len)` enqueue**.
   - 큐 가득(느린 구독자) → enqueue 실패 시 **그 자리에서 즉시 Release** + 백프레셔 정책(끊기/drop).
   - publish 의 가드 ref=1 이 루프 내내 유지되어 중간에 0으로 떨어지지 않음 → 부활 없음(D006 충족).
3. 루프 종료 후 publish 작업이 **자신의 ref 를 마지막에 Release**(구독자 0명이어도 안전 반환).
4. 각 구독자 송신 펌프가 send 완료 후 `Release`. 마지막 Release(=0) → 풀 반환.

> 이 패턴은 phase1-refcounted-pool 검토의 안티패턴("보내고 나서 AddRef")을 피한다. AddRef 는 enqueue
> 직전에 하되, publish 가드 ref 가 0 도달을 막는다.

## 복사 회계 (정정)
- TCP publish = recv 링 → 공유 메시지 버퍼 **1회** + 구독자당 **0회**.
- UDP publish = **0회**.
- → `AGENTS.md §2-1` 불변식 문구를 **"구독자당/불필요한 관리힙 복사 금지(TCP publish 의 recv 링→메시지
  버퍼 1회 복사는 허용)"** 로 정정한다.

## 구현 영향
- `RefCountedBuffer` 에 `Span`/`Memory`/`Length`/`SetLength` 추가(복사 대상·송신 뷰). phase1-refcounted
  검토 should-fix 와 일치.
- UDP Transport 는 `RefCountedBuffer` 를 recv 버퍼로 직접 사용 가능해야 함(D007 풀 핸들 계약).
- 복사는 `Span<byte>.CopyTo`(무할당). 큰 메시지의 TCP 2단계 recv(헤더→본문 직접 수신) 진짜 zero-copy 는
  Phase 7 최적화로 보류.

## 반드시 추가할 테스트 (Phase 3)
- **TCP 무복사-독립성**: PUBLISH 직후 recv 링을 의도적으로 덮어써도(후속 recv 시뮬) 팬아웃된 payload 가
  손상 없음 → 복사가 실제로 일어나 recv 재사용과 독립임을 단언.
- **백프레셔 누수**: 일부 구독자 큐 가득 → 해당 구독자 ref 즉시 Release, `RentedCount` 0 복귀(누수 0).
- **구독자 0명 publish**: 즉시 풀 반환(누수 0).
- UDP: datagram → RefCountedBuffer 직접 수신 후 팬아웃 무결성.
