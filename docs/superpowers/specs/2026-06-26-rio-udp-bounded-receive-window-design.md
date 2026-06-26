# 2026-06-26 RIO UDP Bounded Receive Window Design

## 목표

D116 이후 RIO UDP의 p99 wake tail 은 해결됐지만 open-loop delivery hard gate 는 아직 실패한다.
이번 설계의 목표는 추가 wait/polling 조정이 아니라, RIO UDP receive-side 에 남은 no-posted-receive window 를 줄이는 다음 구현 단위를 정하는 것이다.

## 현재 근거

- D116 이전 RIO `session-02/open-loop`: sent/received 3000/2409, p99 16709.1 us.
- D116 이후 RIO `session-03/open-loop`: sent/received 3000/2373, p99 647.6 us.
- 같은 session 의 RIO `load`: sent/received 3000/3000, p99 481 us.
- SAEA `session-01/open-loop`: sent/received 3000/3000, p99 852.2 us.
- RIO UDP send queue HWM 은 2, dropped 0 이므로 현재 증상은 send-side backpressure 보다 server receive-side absorption 부족 쪽이 더 그럴듯하다.

## 현재 RIO UDP receive 흐름

```mermaid
sequenceDiagram
    participant Loop as "UdpReceiveLoopAsync"
    participant Active as "active receive operation"
    participant Next as "next receive operation"
    participant Handler as "Broker UDP handler"

    Loop->>Active: "RIOReceiveEx post"
    Active-->>Loop: "completion"
    Loop->>Active: "Complete: decode remote, deregister data buffer"
    Loop->>Next: "rent/register data buffer and RIOReceiveEx post"
    Loop->>Handler: "dispatch completed datagram"
    Handler-->>Loop: "return"
    Loop->>Next: "promote next to active"
```

one-deep pre-post 는 handler 실행 중 receive 하나를 열어 두지만, completion 처리 직후 다음 receive 를 post 하기 전까지는 짧은 gap 이 남는다.
이 gap 에는 active data registration 해제, 새 `RefCountedBuffer` 대여, 새 data registration, `RIOReceiveEx` 호출이 들어간다.
D116으로 wait tail 은 제거됐으므로, 다음에는 이 gap 과 outstanding receive depth 를 줄이는 쪽이 맞다.

## 후보 비교

### 후보 A: trace-only diagnostics

RIO UDP ingress completion count, broker publish count, fan-out enqueue count 를 benchmark report 에 추가한다.

장점:

- loss 위치를 더 정밀하게 분리할 수 있다.
- production behavior 를 바꾸지 않는다.

단점:

- 현재 evidence 만으로도 send queue/drop 이 아니라 receive-side 가능성이 높다.
- public 또는 benchmark schema 관측 필드를 추가해야 하며, 바로 delivery 개선을 만들지 않는다.

판단:

- 단독 다음 구현으로는 보류한다.
- bounded receive window 후에도 delivery failure 가 남으면 ingress/fan-out counter 를 추가한다.

### 후보 B: receive payload registration reuse

UDP receive data buffer 를 endpoint 또는 slot lifetime 동안 등록해 매 datagram 의 `RIORegisterBuffer`/`RIODeregisterBuffer` 비용을 줄인다.

장점:

- completion 후 다음 receive post gap 을 줄일 수 있다.
- p50/CPU cost 개선 가능성이 있다.

단점:

- D113과 직접 충돌한다. 완료된 UDP receive payload 는 `RefCountedBuffer`로 handler/fan-out에 넘어가며, 같은 backing array 가 send payload registration/cache 경로에서 다시 쓰일 수 있다.
- receive buffer 를 장기 등록한 채 handler 로 넘기면 receive registration 과 send registration 이 겹칠 수 있다.
- 이를 피하려면 payload 를 다른 buffer 로 복사해야 하는데, UDP publish 0-copy 원칙을 깬다.

판단:

- receive payload registration reuse 는 단독 다음 구현으로 채택하지 않는다.
- remote address buffer 는 handler 로 넘어가지 않으므로 slot lifetime 재사용이 가능하지만, payload data buffer 는 completion 직후 deregister 하고 handler 로 넘기는 D113 규칙을 유지한다.

### 후보 C: bounded receive slot window

`MaxOutstandingReceive`를 2로 늘리고, receive loop 가 두 개의 operation-local receive slot 을 미리 post 한다.
각 completion 은 `RIOResult.RequestContext`로 slot 에 매핑한다.
completion 된 slot 은 data buffer registration 을 해제하고 datagram 을 handler 로 넘긴 뒤, endpoint 가 open 이면 같은 slot 에 새 data buffer 를 대여/등록해 handler dispatch 전에 다시 post 한다.

장점:

- completion 처리 중에도 다른 receive 가 이미 outstanding 상태로 남아 no-posted-receive window 를 줄인다.
- handler dispatch 가 막히는 동안 수용 가능한 datagram 수가 one-deep 보다 늘어난다.
- D113을 유지한다. payload data buffer 는 completion 직후 deregister 하고 handler 로 넘긴다.
- `RIOResult.RequestContext`가 이미 native result shape 에 있으므로 completion-to-operation mapping 을 추가할 수 있다.

단점:

- shared remote address block 을 operation-local 또는 slot-local address block 으로 바꿔야 한다.
- close drain, handler exception cleanup, pool rented count 기대값이 모두 depth 기준으로 바뀐다.
- UDP 신뢰성/순서보장으로 오해되지 않게 v1 범위를 명확히 해야 한다.

판단:

- 다음 구현 후보로 채택한다.
- 첫 depth 는 2로 고정한다. configurable receive depth 는 아직 public/API 범위로 올리지 않는다.

## 결정

다음 구현은 RIO UDP bounded receive slot window 로 진행한다.

세부 결정:

- `MaxOutstandingReceive`는 1에서 2로 올린다.
- public 설정은 추가하지 않는다.
- receive loop 는 startup 에 receive slot 2개를 post 한다.
- `RIOReceiveEx` request context 에 slot id 를 넣고, `RioResult.RequestContext`로 completion slot 을 찾는다.
- 각 slot 은 remote address block 과 remote address buffer id 를 slot lifetime 동안 소유한다.
- payload data buffer 는 datagram 마다 `RefCountedBuffer`를 대여하고, completion 직후 data registration 을 해제한 뒤 handler 로 넘긴다.
- handler dispatch 는 계속 단일 receive loop 에서 직렬 호출한다. handler 병렬 호출은 하지 않는다.
- endpoint close 또는 handler exception 시 posted slot 들의 data registration 과 rented datagram 을 모두 정리한다.
- UDP delivery reliability, ordering guarantee, congestion control 은 계속 범위 밖이다. 이 변경은 posted receive window 를 넓히는 성능/흡수 개선일 뿐이다.

## 구현 계획 초안

### Task 1: receive depth behavior test

Red:

- `UdpReceive_WhenHandlerIsBlocked_PreservesTwoQueuedDatagramsWithBoundedWindow`
- 첫 datagram handler 를 block 한 상태에서 두 개의 datagram 을 추가로 보낸다.
- unblock 후 세 datagram 이 모두 handler 에 도착해야 한다.
- 현재 one-deep 구현은 첫 datagram + 추가 한 개만 안정적으로 보존하므로 `Expected: 3` 계열 실패가 나야 한다.

Green:

- `RioUdpReceiveOperation`을 slot 기반으로 바꾼다.
- slot id 를 request context 로 post 한다.
- completion request context 로 slot 을 찾아 complete 한다.
- startup 에 slot 2개를 post 하고, completion 된 slot 은 dispatch 전 repost 한다.

검증:

- focused new Red/Green test.
- focused `RioTransportUdpTests`.
- focused `Hps.Transport.Rio.Tests`.

### Task 2: close/drain ownership hardening

Red:

- handler exception 중 outstanding receive slot 2개가 모두 cleanup 되어 `ReceivePool.RentedCount == 0`이 되는지 검증한다.
- endpoint close 중 posted receive slot 들이 남아도 CQ/resource close 후 pool leak 이 0인지 검증한다.

Green:

- receive slot collection dispose path 를 명시한다.
- posted slot cleanup 과 completed datagram handoff cleanup 을 분리한다.
- close 후 replacement post 를 금지한다.

검증:

- focused close/handler exception tests.
- solution build/test.

### Task 3: benchmark and D118 decision

실제 RIO UDP scratch benchmark 를 `session-04/rio`에 수집한다.

판단 규칙:

- open-loop sent/received 3000/3000 이 되고 p99가 1ms 내외면 D118 accepted 로 bounded receive window 를 수락한다.
- received 가 개선되지만 3000 미만이면 D118 partial 로 두고 trace diagnostics 를 다음 후보로 올린다.
- received 가 개선되지 않으면 bounded receive window 를 성능 fix 로 수락하지 않고 broker/fan-out/subscriber-side trace 로 넘어간다.

## 예상 영향 파일

- `src/Hps.Transport.Rio/RioTransport.cs`
  - `UdpReceiveLoopAsync(...)` slot window 로 재구성.
  - `RioUdpReceiveOperation`에 request context/slot id 반영.
- `src/Hps.Transport.Rio/RioUdpEndpoint.cs`
  - request queue receive depth 2.
  - receive remote address block 을 endpoint shared 에서 slot-local 로 이동하거나 slot helper 에서 대여.
- `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`
  - blocked handler burst test.
  - close/handler exception cleanup tests.
- `docs/superpowers/plans/`
  - 구현 전 Red/Green 상세 계획.
- root state docs and decisions.

## 완료 기준

- D113 payload ownership 규칙이 유지된다.
- D114 close-safe receive drain 규칙이 유지된다.
- D116에서 확인한 IOCP/RIONotify wait 구조가 유지된다.
- focused RIO UDP tests 와 full RIO tests 가 green 이다.
- solution build warning 0/error 0, solution test green 이다.
- scratch benchmark 로 D118 accepted/partial/rejected 를 기록한다.
