# phase2-udp-datagram-r2 — UDP send 직렬화 후속 검토

검토자: Claude / 날짜: 2026-06-11
선행: [`phase2-udp-datagram.md`](./phase2-udp-datagram.md) (S1·S2 should-fix)

## 1. 대상

- `1319de1 feat: serialize udp endpoint sends` (본 검토 대상)
- 참고로 직전 `844d353 fix: transfer udp datagram ownership before dispatch` 가 r1 의 **S1** 을 해소.

파일: `src/Hps.Transport/Saea/SaeaUdpEndpoint.cs`(+93), `Saea/SaeaTransport.cs`,
`tests/.../Saea/SaeaTransportTests.cs`(+45).

검증: `dotnet build` 0/0, `dotnet test` 41개 통과(Buffers 18 + Transport 23), 실패·건너뜀 0.

## 2. 요약 판정

**승인. r1 의 S2(datagram 당 `Task.Run`) 정확히 해소됨.**

`TrySendTo` 가 datagram 마다 독립 Task 를 띄우던 것을, **endpoint 당 pending queue + 단일 send
pump + `SemaphoreSlim` 신호**로 직렬화했다. TCP 송신 펌프와 동일한 소유권 반환 규율을 따른다.
close↔pump 경합의 이중 release/누수를 모든 분기에서 추적했고 결함 없음.

## 3. 정확성 추적 (이중 release / 누수)

- **소유권 1회 반환 불변식**: pending 항목은 `DrainPendingSends`(close) 또는 pump
  `TryBeginSend`(dequeue) 중 **하나만** 꺼낸다. 둘 다 `_sendGate` 락으로 직렬화되므로 동일 항목이
  양쪽에서 빠질 수 없다 → drain 직접 Release / pump 는 `SendUdpDatagramAsync` finally Release, 각각 1회. ✅
- **close 전 queue 잔류**: `Close()` 가 `_closed=1`(Interlocked) → `DrainPendingSends` 순서라,
  drain 이 잔류 항목을 모두 Release. 이후 pump 가 깨어도 `TryBeginSend` 가 closed 로 false. 누수 0. ✅
- **TryAcceptSend ↔ Close 경합**: TryAcceptSend 가 `_sendGate` 안에서 enqueue 하고, Close 의
  DrainPendingSends 도 같은 락을 잡는다. enqueue 가 먼저면 drain 이 그 항목을 잡아 Release,
  drain 이 먼저면 TryAcceptSend 가 IsClosed=true 로 false 반환(호출자 Release). 어느 순서든 정확히 1회. ✅
- **in-flight 중 close**: pump 가 dequeue 후 송신 중 socket dispose → `SendToAsync` 가
  ObjectDisposed/Socket 예외 → catch 후 finally Release. drain 은 빈 큐를 봄. 1회. ✅
- **신호 coalescing**: `shouldWakePump = (count==0)` 으로 빈→비움 전이에서만 `Release`. pump 는
  깨면 inner-while 로 큐를 **전량** drain. `SemaphoreSlim` 이 release 를 기억하므로 lost-wakeup 없음.
  여분 신호는 "빈 큐 깨움→재대기"로 무해. ✅
- **테스트**: `UdpSendTo_WhenEndpointClosesBeforePumpSends_DrainsQueuedDatagramRef` 가 pump 없이
  생성한 endpoint 에 enqueue 후 Close → `PendingSendCount==0`, `RentedCount==0` 으로 drain 경로를
  레이스 없이 결정적으로 검증. 좋은 격리 설계.

## 4. 사소 관찰 (비차단)

- `_sendSignal`(SemaphoreSlim) 은 Dispose 되지 않는다. `WaitAsync` 만 쓰므로 내부 WaitHandle 이
  지연 할당되지 않아 누수는 무시 가능하고, pending `WaitAsync` 와 dispose 경합을 피하려면 오히려
  **미 dispose 가 안전**하다. 의도된 선택으로 본다.
- pump(`UdpSendLoopAsync`)가 예기치 못한 예외로 죽어도, `StopAsync`→`Close`→`DrainPendingSends` 가
  잔류를 회수하므로 정상 종료 경로에서는 누수가 없다.

## 5. 남은 r1 항목

- S1 — 해소(`844d353`).
- S2 — 해소(본 커밋).
- r1 §4 Q1(UDP recv 풀 무한 증가/배압)·Q2(`GetSocketSendSegment` dead 여부)는 별개로 미해결.
  Phase 3 백프레셔/정리 단위에서 다룬다.
