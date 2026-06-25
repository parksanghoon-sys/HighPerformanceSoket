# RIO Payload Registration Cache Design

## 배경

D106 Task A로 RIO receive block 과 TCP length-prefix block 은 connection resource lifetime 에서 한 번만
`RIORegisterBuffer`로 등록해 재사용한다. 남은 per-operation registration 비용은 payload send path 에 있다.

현재 RIO payload send 는 다음 순서로 동작한다.

1. `TransportConnection.TryBeginInFlightSend(...)`가 `TransportSendBuffer`를 in-flight owner 로 꺼낸다.
2. `TransportSendBuffer.Buffer.Memory`에서 backing `byte[]`와 offset 을 얻는다.
3. 매 send 마다 `RIORegisterBuffer(byte[])`를 호출한다.
4. `RIOSend` completion 을 모두 기다린 뒤 `RIODeregisterBuffer`를 호출한다.
5. in-flight owner dispose 가 `RefCountedBuffer.Release()`를 호출하고, 마지막 ref 이면 block 이 pool 로 돌아간다.

이 구조는 안전하지만, fan-out payload 가 같은 backing array 를 여러 subscriber connection 에 반복 전송할 때
registration 비용을 매번 낸다.

## 목표

- payload send path 의 repeated `RIORegisterBuffer`/`RIODeregisterBuffer` 비용을 줄인다.
- `RefCountedBuffer.Release()`와 pool return 시점을 깨지 않는다.
- outstanding `RIOSend` request 가 남은 buffer id 를 deregister 하지 않는다.
- cache 가 unbounded physical memory registration 으로 커지지 않게 한다.
- RIO default factory 정책, SAEA 동작, broker fan-out ownership 은 변경하지 않는다.

## 범위 밖

- receive block/prefix block 재구현. Task A에서 이미 완료됐다.
- RIO UDP, batching, multi-result dequeue, `RIO_MSG_DEFER`.
- `PinnedBlockMemoryPool` public API 확장.
- global process-wide cache. 먼저 RIO connection resource 안의 bounded cache 로 제한한다.

## 설계 선택지

### 선택지 A: connection resource bounded cache

각 `RioConnectionResource`가 payload backing `byte[]` object identity 를 key 로 삼는 cache 를 가진다.
send pump 가 payload 를 보낼 때 cache lease 를 얻고, completion 뒤 lease 를 release 한다.
cache dispose 는 idle entry 를 즉시 deregister 하고, outstanding entry 는 마지막 lease release 시 deregister 한다.

장점:

- connection close/dispose 수명과 cache 수명이 같은 owner 안에 있다.
- send pump 가 현재 단일 in-flight 이므로 correctness 를 작게 검증할 수 있다.
- transport-wide 동시성 자료구조 없이 시작할 수 있다.

단점:

- 같은 fan-out payload array 가 여러 RIO connection 에서 각각 한 번씩 등록된다.
- connection 별 cache capacity 가 필요하다.

### 선택지 B: RioTransport-wide bounded cache

`RioTransport`가 모든 connection 에서 공유하는 payload registration cache 를 가진다.
같은 payload array 는 여러 subscriber connection 에서 buffer id 하나를 공유할 수 있다.

장점:

- fan-out workload 에서 registration 중복 제거 효과가 가장 크다.
- array object identity 기준 cache hit 확률이 높다.

단점:

- transport stop, connection close, send completion 사이의 outstanding lease 수명이 더 복잡하다.
- 여러 send pump 가 동시에 접근하므로 lock/contention 과 dispose ordering 검증이 커진다.
- RIO opt-in backend 의 다음 hardening 단위로는 blast radius 가 크다.

### 선택지 C: payload cache 보류

payload path 는 per-operation registration 을 유지하고, completion wait 와 receive/prefix reuse 만 기준선으로 둔다.

장점:

- 가장 안전하다.
- 현재 session-05 benchmark 는 4096B x 100Hz 목표를 이미 pass 한다.

단점:

- fan-out subscriber 수가 늘면 payload registration 비용이 subscriber 수에 비례해 남는다.
- RIO backend 의 핵심 이점인 registered buffer reuse 가 payload hot path 에 적용되지 않는다.

## 결정

다음 구현은 **선택지 A: connection resource bounded cache** 로 진행한다.

이유는 현재 RIO backend 가 아직 opt-in 단계이고, default factory 승격 전에는 correctness 와 close 수명 안정성이
fan-out 최적화 폭보다 중요하기 때문이다. connection resource 내부 cache 는 close/dispose owner 가 명확하고,
Task A에서 이미 정리한 `RioConnectionResource` 수명에 자연스럽게 붙는다.

transport-wide cache 는 후속 최적화 후보로 남긴다. connection cache benchmark 와 fan-out evidence 가 쌓인 뒤,
같은 array 를 connection 간 공유해야 할 필요가 명확해지면 별도 설계로 승격한다.

## cache 모델

새 internal 타입 이름은 `RioPayloadRegistrationCache`로 둔다.

핵심 상태:

- key: backing `byte[]` object identity.
- value:
  - `byte[] Block`
  - `IntPtr BufferId`
  - `int OutstandingLeaseCount`
  - `long LastUsedTick`
  - `bool DeregisterWhenIdle`
- capacity: 초기 기본값은 connection 당 64 entries.

cache acquire:

1. lock 안에서 key 를 찾는다.
2. hit 이면 `OutstandingLeaseCount++`, `LastUsedTick` 갱신 후 lease 를 반환한다.
3. miss 이면 idle entry 중 LRU 를 capacity 만큼 evict 한다.
4. evict 할 idle entry 가 없고 capacity 가 찼으면 cache 를 우회해 per-operation registration lease 를 반환한다.
5. 여유가 있으면 `RegisterPinnedArray(...)`로 등록하고 cache entry 를 만든 뒤 lease 를 반환한다.

lease release:

1. lock 안에서 `OutstandingLeaseCount--`.
2. cache dispose 또는 eviction 이 이미 `DeregisterWhenIdle`을 표시했고 count 가 0이면 lock 밖에서 deregister 한다.
3. double release 는 test failure 로 잡을 수 있게 예외 처리한다.

cache dispose:

1. 새 acquire 를 거부한다.
2. idle entry 는 lock 밖에서 deregister 한다.
3. outstanding entry 는 `DeregisterWhenIdle=true`로 표시한다.
4. 마지막 outstanding lease release 가 deregister 를 수행한다.

## payload ownership 정합성

`RefCountedBuffer`는 마지막 `Release()`에서 backing `byte[]`를 pool 로 반환한다. cache 는 `byte[]` object 를 key 로
유지하지만 `RefCountedBuffer` ref 를 보유하지 않는다. 이것은 의도된 구조다.

- RIO registration 은 array object 의 pinned memory 에 묶인다.
- pool 이 같은 array 를 다음 payload 에 재사용해도 같은 buffer id 는 여전히 같은 memory 를 가리킨다.
- 실제 send 중에는 `TransportConnection.InFlightSend`가 payload ref 를 보유하고, send completion 후에만 release 된다.
- 따라서 cache 는 payload lifetime 을 늘리지 않고, array memory registration 만 재사용한다.

단, array 가 다른 native provider 또는 다른 process boundary 로 이동하는 구조는 없다. cache 는 `RioConnectionResource`가
소유한 `RioNative` delegate 로 register/deregister 하며, 해당 resource dispose 와 함께 닫힌다.

## 테스트 전략

1. pure owner tests:
   - 같은 `byte[]`를 두 번 acquire 해도 registrar register count 는 1이다.
   - 다른 `byte[]`가 capacity 를 초과하면 idle LRU entry 를 deregister 한다.
   - outstanding lease 가 있는 entry 는 dispose 시 바로 deregister 하지 않고 마지막 release 때 deregister 한다.
   - capacity 가 가득 차고 모두 outstanding 이면 per-operation fallback lease 를 사용한다.

2. RIO loopback tests:
   - 같은 connection 에서 같은 backing payload block 을 두 번 보내면 payload registration count 가 1회만 증가한다.
   - length-prefix/receive diagnostic tests 는 계속 pass 해야 한다.
   - close/wake 반복 테스트로 dispose 경합을 확인한다.

3. 전체 검증:
   - focused RIO tests.
   - close/wake 핵심 테스트 10회 반복.
   - solution build/test.
   - session-06 RIO benchmark observation.

## 구현 경계

다음 구현 계획은 아래 task 로 나눈다.

1. `RioPayloadRegistrationCache` pure owner 와 tests.
2. `RioConnectionResource`에 payload cache 소유권 연결.
3. `RioTransport.SendRegisteredArrayAsync(...)`를 payload cache lease 기반으로 전환.
4. 반복 close/wake, full build/test, session-06 benchmark, 상태 문서 갱신.

## 완료 기준

- payload send path 가 cache hit 시 per-operation `RIORegisterBuffer`/`RIODeregisterBuffer`를 호출하지 않는다.
- cache miss/fallback/eviction/dispose 가 outstanding send 중 deregister 하지 않는다.
- `RefCountedBuffer` pool rented count 0 계약이 유지된다.
- Task A receive/prefix tests, RIO focused tests, solution build/test 가 통과한다.
- benchmark 관측값과 payload cache의 남은 한계를 상태 문서에 기록한다.
