# phase2-udp-datagram — SAEA UDP datagram 기준선 검토

검토자: Claude / 날짜: 2026-06-11

## 1. 대상

커밋 `bd5d46b feat: add saea udp datagram baseline` (및 직전 `ca086d4` tcp send pump 기준선).

파일:
- `src/Hps.Transport/SaeaUdpEndpoint.cs` (신규)
- `src/Hps.Transport/IUdpEndpoint.cs` (신규)
- `src/Hps.Transport/ITransportDatagramHandler.cs` (신규)
- `src/Hps.Transport/SaeaTransport.cs` (BindUdpAsync / UdpReceiveLoopAsync / TrySendTo /
  SendUdpDatagramAsync / Dispatch·Notify / Unregister·StopCore UDP 분기 추가)
- `src/Hps.Transport/ITransport.cs`, `TransportBase.cs` (UDP 계약 추가)
- `tests/Hps.Transport.Tests/SaeaTransportTests.cs` (UDP recv/send 테스트 2건)

검증: `dotnet build` 경고/오류 0, `dotnet test` 38개 통과(Buffers 18 + Transport 20), 실패·건너뜀 0.

## 2. 요약 판정

**승인.**

핵심 설계 불변식 **D009(UDP 는 datagram 을 `RefCountedBuffer` 로 직접 recv, BipBuffer 미사용,
publish zero-copy)가 정확히 구현**됐다. 소유권 경계·수명 대칭·누수 검증 모두 충족. 치명적 버그 없음.
아래 should-fix 2건은 "baseline" 한정 개선점이며 다음 단위로 넘겨도 된다.

### 확인된 정확성

- **D009 zero-copy recv**: `UdpReceiveLoopAsync` 가 `_receivePool.RentCounted()` 로 받은 버퍼의
  블록 세그먼트에 `ReceiveFromAsync` 로 **직접 수신**한다(중간 복사·BipBuffer 없음). 수신 후
  `SetLength(ReceivedBytes)` → handler 에 소유권 이전. ✅
- **소유권 핸드오프**: dispatch 성공 후 `datagram = null` 로 루프의 release 책임을 끊고, handler 가
  ref 를 소유(`ITransportDatagramHandler` 계약)한다. handler 가 null 이면 `DispatchDatagramReceived`
  가 즉시 `Release`(누수 차단, line 450). 수신 예외 경로(ObjectDisposed/Socket/generic) 모두
  `datagram?.Release()` 로 정확히 1회 반환. ✅
- **send 소유권 경계**: `TrySendTo` 는 TCP `TrySend` 와 동일하게 live buffer 확인 후, closed 면
  false(호출자 release), open 이면 true(Transport 소유). `SendUdpDatagramAsync` 의 `finally` 가
  정확히 1회 `Release`. offset/length 전송 범위도 테스트로 검증(junk 바이트 제외, 수신측 정확). ✅
- **수명 대칭(M1 패턴 일관 적용)**: `SaeaUdpEndpoint.Close()` 가 Interlocked 가드 + socket dispose +
  `UnregisterUdpEndpoint`. `StopCore` 가 `_udpEndpoints` 스냅샷→clear→lock 밖 close. 닫힌
  endpoint 가 목록에 남지 않는다. TCP 연결 누수 수정과 동일한 대칭이 UDP 에도 들어갔다. ✅
- **누수 테스트**: `UdpSendTo_...` 가 `WaitForRentedCountAsync(pool, 0)` 으로 send 완료 후 풀 반환을
  검증한다. TOCTOU(IsClosed 통과 후 socket dispose)는 `SendToAsync` 의 ObjectDisposed 를 삼키고
  finally 가 release 하므로 benign. ✅

## 3. should-fix (baseline 한정, 비차단)

### S1 — recv 루프의 소유권 이전을 dispatch **전**으로 옮길 것

- 현재 순서: `DispatchDatagramReceived(...)` 호출 → 그 다음 `datagram = null`
  (`UdpReceiveLoopAsync`). handler 의 `OnDatagramReceived` 가 **부분 소유권을 가져간 뒤 예외를
  던지면**, 루프의 `catch { datagram?.Release(); throw; }` 가 같은 버퍼를 다시 건드린다. handler 가
  이미 guard ref 까지 Release 한 상태라면 이중 반환(가드가 예외로 검출)으로 이어진다.
- 정확성 자체는 `RefCountedBuffer` 이중 반환 가드가 막아 손상으로 가지 않지만, 소유권 경계가 흐리다.
- 권고: 로컬로 옮긴 뒤 dispatch 전에 `datagram = null` 로 필드를 끊고 호출한다(소유권 이전 = 호출
  시점). 그러면 handler 가 던져도 루프는 그 버퍼를 다시 만지지 않는다.

### S2 — UDP send 가 datagram 당 `Task.Run` 1개

- `TrySendTo` 가 datagram 마다 `Task.Run(SendUdpDatagramAsync)` 를 띄운다. TCP 는 연결당 단일 send
  pump 가 있지만 UDP send 에는 펌프/배압이 없다. 고빈도 송신 시 thread-pool flooding·순서/배압
  부재가 된다(UDP 라 순서 보장은 원래 없음, 하지만 thread 폭주는 perf 문제).
- 현재 목표(4096B×100Hz)에는 충분하나, Phase 4 벤치 전에 endpoint 당 send 직렬화(작은 펌프/채널)로
  바꾸는 것을 권한다. 주석에도 baseline 명시돼 있어 인지된 상태로 본다.

## 4. 확인 필요(질문)

- Q1: UDP recv 루프는 datagram 마다 풀에서 `RentCounted` 한다. handler/fan-out 이 느리면 풀이
  계속 커진다(UDP 는 네트워크 배압이 없음). 풀 상한·drop 정책을 Phase 3 백프레셔(D012)와 함께
  UDP 에도 적용할지, UDP 는 별도 정책으로 둘지.
- Q2: `GetSocketSendSegment`(SaeaTransport.cs:368)가 `GetRefCountedBlockSegment` 로 단순 위임만
  하는 helper 다. 현재 호출처가 있는지(없으면 dead) 확인 후 정리 여부.

## 5. 비차단 유지 항목 (이월, 정보용)

- 실제 SAEA `SocketAsyncEventArgs` completion pump 미적용 — recv/send 모두 raw `Socket` async loop
  기준선. 주석에 명시. RIO/io_uring 단위에서 교체 예정.
- BipBuffer 동시성 스트레스가 capacity=256 단일 케이스 (이월).
