# RIO Registered Buffer Reuse Design

## 배경

D105로 RIO completion wait 의 16ms대 p99 tail 은 `RIONotify` + shared IOCP pump 로 해소됐다.
남은 RIO hot path 에는 receive/send operation 마다 `RIORegisterBuffer`와 `RIODeregisterBuffer`를 호출하는 비용이 있다.

Microsoft RIO 문서상 registered buffer 는 virtual memory page 를 physical memory 에 lock 하며,
여러 작은 non-contiguous buffer 를 각각 등록하면 page 단위 footprint 와 registration overhead 가 커질 수 있다.
또한 deregister 는 해당 buffer id 를 더 이상 쓰지 않겠다는 선언이고,
outstanding send/receive request 가 남아 있을 때 deregister 하면 결과가 undefined 로 간주된다.
`RIOSend`/`RIOReceive`는 등록된 buffer id 와 offset/length 를 사용하며,
request completion 전까지 buffer id 와 buffer memory 가 유효해야 한다.

현재 `RioTransport`는 다음처럼 operation 단위 등록을 수행한다.

- receive: `ReceivePool.Rent()` → `RegisterPinnedArray(...)` → `RIOReceive` → completion → `RIODeregisterBuffer` → `ReceivePool.Return(...)`
- length prefix send: send loop local pinned 4-byte array 를 매 send 마다 등록/해제
- payload send: `RefCountedBuffer.Memory`의 backing array 를 매 send 마다 등록/해제

## 목표

- RIO receive path 에서 per-operation buffer registration 을 제거한다.
- RIO length-prefix send path 에서 per-operation buffer registration 을 제거한다.
- send payload path 의 registration cache 는 ownership risk 를 먼저 설계하고, 안전한 최소 단위로 분리한다.
- outstanding request 가 있는 buffer id 를 deregister 하지 않는 규칙을 코드 구조로 강제한다.
- `RefCountedBuffer`의 pool return/use-after-free 방지 계약을 깨지 않는다.

## 비목표

- 이번 설계에서 RIO default backend 승격을 결정하지 않는다.
- multi-result dequeue batching, `RIO_MSG_DEFER`, UDP RIO는 포함하지 않는다.
- `PinnedBlockMemoryPool` public API를 바로 확장하지 않는다.
- send payload registration cache 구현을 receive/length-prefix와 같은 task에 섞지 않는다.

## 판단

### Receive block

현재 RIO receive 는 `MaxOutstandingReceive = 1`이고 receive pump 하나가 post → completion → handler dispatch 를 직렬화한다.
receive handler 는 synchronous `OnReceived(...)` 호출 안에서만 `TransportReceiveBuffer` span 을 본다.
따라서 connection resource lifetime 에 receive block 하나를 고정 대여하고 한 번만 `RIORegisterBuffer`로 등록해도 안전하다.

설계:

- `RioConnectionResource`가 `byte[] ReceiveBlock`과 `IntPtr ReceiveBufferId`를 소유한다.
- constructor 에서 pinned receive block 을 대여하고 한 번 등록한다.
- receive loop 는 매번 같은 `ReceiveBlock`/`ReceiveBufferId`로 `RIOReceive`를 post 한다.
- completion 과 `DispatchReceived(...)`가 끝난 뒤에만 다음 receive 를 post 한다.
- resource dispose 에서 CQ close 이후 `RIODeregisterBuffer(ReceiveBufferId)`와 pool return 을 수행한다.

### Length prefix send buffer

length prefix 는 send loop 가 소유하는 4-byte pinned array 다.
send loop 는 단일 pump 이고 prefix send completion 을 기다린 뒤 payload send 로 넘어간다.
따라서 connection resource lifetime 에 registered prefix buffer 를 하나 두고 재사용해도 안전하다.

설계:

- `RioConnectionResource`가 `byte[] LengthPrefixBlock`과 `IntPtr LengthPrefixBufferId`를 소유한다.
- `SendInFlightAsync(...)`는 prefix 값을 block 에 쓰고 `LengthPrefixBufferId`로 send 한다.
- prefix request completion 전에는 같은 block 을 다시 쓰지 않는다.
- resource dispose 에서 buffer id 를 deregister 한다.

### Payload send buffer

payload 는 `RefCountedBuffer`의 backing array 다.
이 array 는 마지막 `Release()` 후 pool 로 돌아갈 수 있고, 나중에 다른 payload 로 재사용될 수 있다.
등록 자체는 backing array lifetime 동안 유지 가능하지만, 현재 public 계약에는 pool이 언제 array 를 완전히 폐기하는지,
transport 가 어떤 pool의 array 를 얼마나 오래 등록해도 되는지 드러나지 않는다.

안전한 방향은 payload registration cache 를 별도 task 로 분리하는 것이다.
cache 는 최소한 다음을 만족해야 한다.

- key 는 backing `byte[]` object identity 다.
- registration 은 `RioNative` instance 에 묶인다.
- request outstanding count 또는 send pump 단일 in-flight 경계를 통해 deregister 시점을 보장한다.
- transport/resource dispose 시 cache entry 를 deregister 한다.
- `RefCountedBuffer.Release()`가 array 를 pool 로 돌려도 registration 은 array object 에 묶여 유지될 수 있지만,
  해당 array 가 다른 pool 또는 다른 native provider 로 넘어가는 구조는 금지해야 한다.

## 설계 선택

이번 구현 후보는 두 단계로 나눈다.

1. **Task A — receive + length-prefix resource lifetime registration**
   - 가장 안전하고 즉시 효과가 있는 범위다.
   - `RefCountedBuffer` ownership 과 fan-out 경로를 건드리지 않는다.
   - RIO receive/send existing tests 로 regression 을 잡을 수 있다.

2. **Task B — payload registration cache design/implementation**
   - 별도 설계/테스트 후 진행한다.
   - cache key, native provider lifetime, deregister timing, pool ownership을 명확히 해야 한다.

## 테스트 전략

Task A:

- Red: RIO receive path 가 connection lifetime registered receive block 을 재사용한다는 관측 test 를 추가한다.
  구현 detail 을 직접 노출하지 않기 위해 `RioConnectionResource` 내부 diagnostic counter 를 추가할지 검토한다.
  public surface 를 넓히지 않으려면 existing RIO loopback + code review + benchmark observation 으로 보완한다.
- Green: focused RIO tests 전체.
- Green: close/wake 핵심 tests 10회 반복.
- Green: solution build/test.
- Observation: RIO load/open-loop scratch benchmark 를 session-05로 수집해 session-04와 비교한다.

Task B:

- Red: 같은 backing array 를 두 번 send 할 때 registration count 가 1로 유지되는 internal cache test.
- Red: cache dispose 가 outstanding request 완료 전 deregister 하지 않는 owner test.
- Green: RIO loopback, Broker fan-out, pool rented count 0.

## 구현 영향

Task A touched files:

- `src/Hps.Transport.Rio/RioTransport.cs`
  - `RioConnectionResource`에 registered receive/prefix buffer owner 추가.
  - `ReceiveLoopAsync`에서 per-iteration `RegisterPinnedArray`/`DeregisterBuffer` 제거.
  - `SendRegisteredArrayAsync`를 prefix registered buffer 전용 helper 와 payload per-operation helper 로 분리.
- `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`
  - existing loopback/length-prefix/close tests 재사용.
  - 필요 시 internal diagnostic counter test 추가.

Task B touched files:

- `src/Hps.Transport.Rio/RioRegisteredBufferPool.cs`
  - 현재 native id 없이 outstanding request 수명만 추적하는 placeholder 를 실제 registration cache 로 바꾼다.
- `src/Hps.Transport.Rio/RioTransport.cs`
  - payload send path 가 cache 에서 buffer id 를 얻고 completion 뒤 outstanding count 를 줄인다.
- `tests/Hps.Transport.Rio.Tests/`
  - cache lifetime, duplicate registration 방지, dispose ordering test 추가.

## Open risks

- Receive block resource lifetime 등록은 MaxOutstandingReceive=1 불변식에 의존한다.
  outstanding receive 를 늘리는 future batching 단계에서는 registered receive slot pool 로 확장해야 한다.
- Length prefix registered buffer 는 MaxOutstandingSend=1 및 단일 send pump 에 의존한다.
  send batching 단계에서는 prefix buffer slot 이 필요하다.
- Payload registration cache 는 pool/array lifetime 을 장기 보유하므로 physical memory footprint 를 늘릴 수 있다.
  cache eviction 을 넣으면 outstanding request 와 deregister 경합이 생기므로 첫 구현은 connection/transport lifetime cache 로 제한한다.

## 완료 기준

- receive path 에 per-operation `RIORegisterBuffer`/`RIODeregisterBuffer`가 남지 않는다.
- length-prefix send path 에 per-operation registration 이 남지 않는다.
- payload send path 는 명시적으로 per-operation registration 을 유지하거나, 별도 cache task 로 안전하게 이동한다.
- focused RIO tests, repeated close/wake tests, solution build/test 가 통과한다.
- session-05 benchmark artifact 에서 session-04 대비 p50/p99/actual-rate 변화가 기록된다.
