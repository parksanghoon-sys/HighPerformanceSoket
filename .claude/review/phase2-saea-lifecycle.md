# phase2-saea-lifecycle — SAEA TCP 연결 수명 검토

검토자: Claude / 날짜: 2026-06-11

## 1. 대상

커밋 범위 `d480453..HEAD` (3건):
- `26f0cad fix: guard abandoned in-flight send release`
- `116d7d0 feat: add tcp transport connection contract`
- `ff5d1d9 feat: add saea tcp connection baseline`

파일:
- `src/Hps.Transport/SaeaTransport.cs`
- `src/Hps.Transport/SaeaConnectionListener.cs`
- `src/Hps.Transport/IConnectionListener.cs`
- `src/Hps.Transport/TransportConnection.cs` (InFlightSend 핸들 추가)
- `tests/Hps.Transport.Tests/SaeaTransportTests.cs`,
  `TransportSendQueueTests.cs`, `TransportContractTests.cs`

검증: `dotnet build` 경고/오류 0, `dotnet test` 31개 통과(Buffers 18 + Transport 13), 실패·건너뜀 0.

## 2. 요약 판정

**조건부 승인 (must-fix 1건 해소 후 다음 단위 진행).**

치명적(크래시·데이터 손상·이중 반환·use-after-free)급 버그는 없다. 동시성/소유권 경계
(BipBuffer SPSC, RefCountedBuffer refcount, InFlightSend 멱등 Release)는 정밀하고 테스트로
방어된다. 직전 검토에서 지적한 펌프 abandon-leak 위험은 `InFlightSend : IDisposable` +
`Interlocked.Exchange` 가드로 정확히 해소됐고 회귀 테스트
(`InFlightSend_WhenPumpAbandonsAfterClose_DisposePathReleasesTransportOwnedRef`)도 추가됐다.

다만 연결 수명 와이어링에 실제 누수 1건이 있어 must-fix 로 둔다.

## 3. must-fix

### M1 — 닫힌 연결이 `SaeaTransport._connections` 에서 제거되지 않는다 (메모리 누수)

- 증상: `_connections` 리스트는 `RegisterConnection` 에서 `Add` 만 되고
  (`SaeaTransport.cs:244`), 비우는 경로는 전체 종료 `StopCore` 의 `Clear()`
  (`SaeaTransport.cs:262`) 뿐이다. **개별 `UnregisterConnection` 이 없다.**
  `TransportConnection` 은 transport 역참조가 없어 `Close()` 시 스스로 등록 해제하지 못한다.
- 결과: accept/connect 된 연결이 `Close()` 된 뒤에도 `_connections` 에 영구히 남는다.
  `TransportConnection` 객체와 (이미 Dispose 된) `Socket` 참조가 transport 수명 내내 GC 되지 않는다.
- 왜 must-fix: 목표가 C10K 급 서버다. 단명 연결이 churn 하는 서버 시나리오에서 **메모리가
  무한 증가**한다. 현재 테스트는 연결 1~2개만 만들고 즉시 StopAsync 로 끝나 잡히지 않지만,
  accept 루프가 도는 다음 Phase 에서 바로 문제가 된다. 이미 등록된 자원의 수명 관리 빈틈이지
  "후속 단위"로 미룰 설계 범위가 아니다.
- 근거(대칭 결손): `SaeaConnectionListener.Close()` 는 `_transport.UnregisterListener(this)`
  를 호출(`SaeaConnectionListener.cs:60`)해 자기 등록을 해제한다. 연결도 같은 대칭 처리가
  있어야 하는데 빠졌다.
- 권고 방향(구현은 사용자 검토 후):
  `TransportConnection` 생성 시 unregister 콜백(또는 `SaeaTransport` 역참조)을 주입하고,
  `Close()` 의 멱등 경로에서 정확히 1회 `_connections` 에서 제거한다. `StopCore` 가 이미
  스냅샷을 떠 lock 밖에서 닫으므로, 등록 해제도 같은 lock 규율(연결 자기 제거 시 transport
  `_gate` 사용)과 재진입 회피를 지켜야 한다. R1 류 churn 회귀(반복 accept→close 후
  `_connections.Count` 가 0 으로 수렴) 테스트를 함께 추가한다.

## 4. should-fix

### S1 — `TransportConnection.Close()` 가 `_gate` lock 안에서 소켓 Dispose

- `Close()` 가 `_gate` 를 잡은 채 `_transportResource?.Dispose()` 를 호출한다
  (`TransportConnection.cs:160`). 교착 위험은 없다(소켓 Dispose 가 이 lock 으로 재진입하지 않음).
  그러나 `LingerState` 설정에 따라 Dispose 가 잠깐 블록되면, 같은 연결의 `TrySend` /
  `PendingSendCount` 관측자가 그동안 대기한다.
- 권고: pending drain(release)은 lock 안에서 끝내고, 소켓 Dispose 는 lock 밖에서 수행한다.
  정확성 문제는 아니므로 should-fix.

## 5. 확인 필요(질문)

- Q1: `_connections` 목록의 용도가 "종료 시 일괄 정리"만인지, 아니면 이후 브로드캐스트/샤딩
  대상으로도 쓸 계획인지. 후자라면 M1 의 자료구조 선택(List → 제거 비용 O(n))도 함께
  재검토가 필요하다(연결 핸들에 노드/슬롯 참조를 들려 O(1) 제거).
- Q2: S1 의 소켓 종료 정책(graceful vs abortive, LingerState)을 Phase 7 튜닝 전에 잠정
  고정할지, 지금은 기본값으로 둘지.

## 6. 비차단 유지 항목 (직전 검토에서 이월, 정보용)

- 실제 send/recv 펌프 루프 미구현 (이번 단위는 listen/connect/accept 수명까지). 의도된 범위.
- BipBuffer 동시성 스트레스가 capacity=256 단일 케이스. 작은 capacity 동시 fuzz 추가 시 더 안전.
- `RefCountedBuffer.Memory`/`Span` 이 `Length` 너머 전체 블록 노출 — D009(UDP 직접 recv) 의도.
