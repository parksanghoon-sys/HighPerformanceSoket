# RIO UDP 반복 안정성 hardening 설계

- 날짜: 2026-07-11
- 상태: Accepted - 2026-07-13 implementation plan 작성
- 관련 결정: D113, D114, D116, D117, D118, D240
- 목표: Windows RIO UDP에서 4096B 메시지 100 Hz open-loop 3회 모두 delivery hard gate를 통과한다.

## 1. 문제와 확인된 범위

현재 RIO UDP는 endpoint마다 receive slot 2개를 생성하고 `RIOReceiveEx`를 미리 post한다.
completion은 request context로 slot에 매핑하고, 완료된 slot은 handler dispatch 전에 다시 post한다.

현재 checkout의 30초 baseline을 3회 반복한 결과는 다음과 같다.

- RIO TCP load/open-loop 6개 report: 모두 3000/3000, hard pass.
- RIO UDP load 3개 report: 모두 3000/3000, hard pass.
- RIO UDP open-loop 3개 report: 2996/2997/2999, hard fail.
- 같은 환경의 SAEA UDP open-loop: 3000/3000, hard pass.
- RIO UDP send queue HWM: 최대 2, transport drop 0, payload error 0, pool rented 0.
- 기존 RIO UDP focused tests: 18/18 통과.

따라서 이 설계는 benchmark 공통 하네스나 send queue를 바꾸지 않는다.
첫 원인 후보를 RIO receive-side burst absorption 여유로 제한하고, 기존 depth 2 window를 작은 고정 depth 4로 검증한다.
이는 UDP 신뢰성이나 순서 보장을 추가하는 설계가 아니다.

## 2. 대안 비교

### A. 고정 depth 4 bounded receive window - 채택

- `RioUdpEndpoint.ReceiveWindowSize`를 내부 상수 4로 둔다.
- 기존 slot owner, request-context mapping, completion loop를 그대로 재사용한다.
- public 설정과 새 abstraction을 추가하지 않는다.
- depth 2 대비 endpoint당 pinned payload slot은 16 KiB에서 32 KiB로 증가한다.
- handler가 current datagram을 소유한 순간까지 포함한 peak receive payload 대여는 5개, 약 40 KiB다.

현재 손실은 3000개 중 1~4개이고 과거 depth 1에서 depth 2로 확장했을 때 대량 손실이 해소됐다.
따라서 구조 변경 없이 burst absorption 여유만 한 단계 늘리는 가장 작은 검증 가능한 변경이다.

### B. receive payload registration 재사용 - 제외

completion payload는 `RefCountedBuffer`로 handler와 fan-out에 넘어간다.
receive registration을 slot lifetime 동안 유지하면 같은 backing array의 send registration과 겹칠 수 있어 D113 소유권 경계를 다시 설계해야 한다.
복사로 분리하면 UDP publish 0-copy 원칙을 깨므로 이번 원인 검증에 비해 범위가 크다.

### C. configurable 또는 adaptive depth - 제외

public option, 동적 slot 증감, 운영 tuning 기준과 추가 diagnostics가 필요하다.
현재 단일 4096B x 100 Hz 목표를 검증하기에는 연결과 상태가 과도하게 늘어난다.

## 3. 구조와 데이터 흐름

production 데이터 흐름은 바꾸지 않는다.

1. endpoint 생성 시 receive slot 4개를 만들고 각 slot의 remote address block을 한 번 등록한다.
2. 각 slot은 pinned receive payload를 대여·등록하고 고유 request context로 `RIOReceiveEx`를 post한다.
3. completion을 dequeue하면 request context로 완료 slot을 찾는다.
4. slot은 payload 길이와 remote endpoint를 확정하고 payload registration을 해제한다.
5. endpoint가 open이면 같은 slot에 새 payload를 대여해 handler dispatch 전에 다시 post한다.
6. 완료 payload는 기존 단일 receive loop에서 handler로 전달한다.

동시에 post된 receive는 최대 4개다. handler dispatch는 계속 직렬이며 별도 worker, channel, queue를 추가하지 않는다.
completion queue 크기 64와 send-side `MaxOutstandingSend = 1`도 변경하지 않는다.

## 4. 소유권과 종료 계약

depth 증가가 native registration이나 pooled buffer 누수로 이어지지 않아야 한다.

- 각 `RioUdpReceiveSlot`은 자신이 post한 payload ref, payload buffer id, remote address block/id를 단독 소유한다.
- completion 후 handler로 넘긴 current datagram은 receive loop가 dispatch 전까지 소유한다.
- endpoint close 후에는 replacement receive를 post하지 않는다.
- receive loop `finally`는 모든 slot을 dispose한 뒤 receive drain 완료를 알린다.
- slot dispose는 outstanding payload registration을 해제하고 보유 ref를 정확히 한 번 release한다.
- completion됐지만 dispatch되지 못한 current datagram도 receive loop exit에서 정확히 한 번 release한다.
- handler 예외는 기존처럼 endpoint close notification 1회로 수렴한다.

정상 open 상태에서 handler가 첫 datagram을 보유하고 slot 4개가 다시 post된 순간의 예상 `ReceivePool.RentedCount`는 5다.
close 또는 handler 예외 drain 이후에는 0이어야 한다.

## 5. TDD 검증 설계

### Red 1: depth 4 burst 보존

기존 `UdpReceive_WhenHandlerIsBlocked_PreservesTwoQueuedDatagramsWithBoundedWindow`를
`UdpReceive_WhenHandlerIsBlocked_PreservesFourQueuedDatagramsWithBoundedWindow`로 강화한다.

- 첫 handler를 block한다.
- 추가 datagram 4개를 전송한다.
- block 중 `ReceivePool.RentedCount == 5`를 확인한다.
- unblock 후 총 5개 datagram이 handler에 도착해야 한다.
- 현재 depth 2 구현은 expected 5를 만족하지 못해 assertion failure 또는 receive count timeout으로 실패해야 한다.
- Green 뒤에는 이 계약에 포함되는 기존 `UdpReceive_WhenHandlerIsBlocked_PrePostsOneAdditionalReceive`를 제거해
  같은 pre-post 동작을 강도만 다르게 검증하는 중복 test를 남기지 않는다.

### Red 2: close 중 depth 4 owner 정리

기존 endpoint close 테스트의 outstanding count를 5로 강화한다.

- 첫 handler가 block된 상태에서 current 1개와 posted slot 4개가 대여됐음을 확인한다.
- endpoint를 close하고 handler를 해제한다.
- receive drain 뒤 `ReceivePool.RentedCount == 0`이어야 한다.
- 현재 depth 2 구현은 expected 5에서 먼저 실패해야 한다.

### Green

- `ReceiveWindowSize`를 2에서 4로 변경한다.
- generic slot 배열과 request queue가 이미 이 상수를 사용하므로 별도 production 경로를 추가하지 않는다.
- 새 실패가 드러나지 않는 한 receive loop, slot owner, CQ wait를 리팩터링하지 않는다.

### 회귀 검증

- 강화한 blocked-handler와 close tests.
- handler exception cleanup과 close notification 1회 test.
- `RioTransportUdpTests` 전체.
- `Hps.Transport.Rio.Tests` 전체.
- solution build와 solution tests.

## 6. 반복 benchmark 수락 gate

구현 test가 green이어도 benchmark gate 전에는 fix로 수락하지 않는다.

실행 조건:

- Release build.
- `--baseline-suite <temp> --runs 3 --protocol udp --backend rio`.
- raw report와 summary는 임시 경로에만 둔다.

필수 수락 조건:

- open-loop 3회 모두 sent/received 3000/3000.
- load 3회 모두 sent/received 3000/3000.
- 모든 run에서 transport drop 0.
- 모든 run에서 payload error 0.
- 모든 run 종료 후 pool rented 0.
- summary hard failure 0.

latency와 HWM은 기록하지만 기존 정책대로 report-only다. latency warning만으로 구현을 실패 처리하거나 hard gate로 승격하지 않는다.

한 번이라도 delivery hard gate가 실패하면 depth 4를 fix로 커밋하지 않는다.
depth 8이나 동적 설정으로 바로 확대하지 않고, 해당 변경을 되돌린 뒤 ingress completion, broker dispatch, fan-out send,
subscriber receive 중 누락 위치를 구분하는 diagnostics 설계로 돌아간다.

## 7. 예상 변경 범위

- `src/Hps.Transport.Rio/RioUdpEndpoint.cs`
  - 내부 `ReceiveWindowSize` 2에서 4로 변경.
- `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`
  - blocked-handler burst와 close owner test를 depth 4 기준으로 강화.
  - fixed-four 계약에 포함되는 약한 one-additional pre-post test 제거.
  - 관련 주석과 이름을 현재 계약에 맞게 갱신.
- root state docs와 2026-07 archive
  - Red/Green, 반복 gate, 수락 또는 실패 결과 기록.

`RioTransport.cs`, public transport abstraction, Broker, Protocol, benchmark schema와 workflow는 기본 변경 범위가 아니다.
검증 중 실제 결함이 확인되지 않는 한 이 파일들로 범위를 넓히지 않는다.

## 8. 범위 밖

- UDP ACK, retry, ordering, congestion control.
- public/configurable/adaptive receive depth.
- receive payload registration cache.
- RIO IPv6.
- default transport 승격.
- latency warning hard gate 전환.
- 새 diagnostics API나 server-level aggregation.

## 9. 구현 handoff 기준

- 첫 production 변경 전에 두 Red가 assertion failure로 재현돼야 한다.
- production 변경은 내부 상수 1개를 우선으로 한다.
- generic owner가 depth 4에서 깨진다는 증거가 있을 때만 해당 owner를 수정한다.
- close/drain과 pool leak 검증을 benchmark보다 먼저 통과시킨다.
- 반복 benchmark까지 통과한 뒤에만 구현 커밋과 D240 결과 갱신을 수행한다.
- push와 원격 io_uring gate는 이 Windows RIO 단위와 섞지 않는다.
