# D216 이후 io_uring fixed send registration lifetime 설계

## 상태

Draft for review.

## 배경

D210에서 TCP payload 전송을 `IORING_OP_WRITE_FIXED`로 직접 연결했지만,
원격 `iouring-linux-contract.yml` run `28907016232`에서 test step 이 20분 timeout/cancelled 로 끝났다.
D211에서 production payload path 는 기존 `SendArrayAsync`/`TrySubmitSend` baseline 으로 rollback 했다.
D215/D216에서는 같은 종류의 hang 이 다시 발생할 때 원격 artifact 에 `vstest-diag.log`와 blame-hang evidence 를 남기도록
workflow 관측성을 먼저 보강했고, run `28916879277`에서 Linux contract gate 가 통과했다.

현재 코드 상태는 다음과 같다.

- `IoUringTransport.SendInFlightAsync`는 TCP length prefix 와 payload 를 모두 `SendArrayAsync`로 보낸다.
- `IoUringFixedSendLease.CreateForSendPump(...)`는 send pump 전용 extra `RefCountedBuffer` ref 를 획득한다.
- 하지만 `CreateForSendPump(...)` 내부 registration factory 는 여전히 매 send 마다
  `IoUringRegisteredBufferSet.Register(queue, new byte[][] { segment.Array })`를 호출하는 형태다.
- `IoUringRegisteredBufferSet.Dispose()`는 `IoUringNative.UnregisterBuffers(...)`를 호출해 queue 전체 fixed buffer table 을 해제한다.

따라서 D216 이후 바로 production fixed-write path 를 다시 연결하면, active send pump 중 register/unregister 가 반복되고
queue-level fixed buffer table lifetime 과 in-flight CQE lifetime 이 섞이는 D210 실패 패턴을 반복할 가능성이 높다.

## 목표

다음 구현 단위는 **io_uring fixed send registration lifetime owner** 로 둔다.

목표는 TCP send pump 를 바로 `WRITE_FIXED`로 다시 바꾸는 것이 아니라,
fixed buffer registration 이 다음 조건을 만족하도록 수명 경계를 먼저 설계하고 구현할 수 있게 만드는 것이다.

- registration/unregistration 은 per-send hot path 에서 발생하지 않는다.
- registration owner 는 queue 또는 TCP connection resource lifetime 에 묶인다.
- in-flight send completion 이 남아 있는 동안 fixed buffer table 을 unregister 하지 않는다.
- `RefCountedBuffer` 소유권은 기존 send queue/in-flight ref-count 규칙과 충돌하지 않는다.
- 기존 `TrySubmitSend` baseline 은 fallback 으로 유지된다.

## 후보 비교

### 후보 A: production TCP payload fixed-write 를 즉시 재연결

장점:

- D206, D212, D216까지 socket fixed-write evidence 는 이미 green 이다.
- 실제 production TCP payload path 에 가장 빨리 fixed-write 를 적용할 수 있다.

단점:

- 현재 `CreateForSendPump(...)`는 per-send `RegisterBuffers`/`UnregisterBuffers`를 반복한다.
- `RegisterBuffers`는 queue 단위 fixed buffer table 을 교체하는 계약이므로, active send pump 중 반복 호출하면
  다른 in-flight SQE/CQE와 충돌할 수 있다.
- D210에서 이미 유사한 직접 연결이 원격 hang 을 만들었다.

판단: 지금 선택하지 않는다.

### 후보 B: benchmark/default promotion 으로 이동

장점:

- 기능 코드를 더 건드리지 않고 현재 baseline 의 성능/안정성 evidence 를 늘릴 수 있다.
- default promotion 논의에 필요한 장기 artifact 를 계속 축적할 수 있다.

단점:

- 현재 production TCP payload path 는 `WRITE_FIXED`를 사용하지 않으므로, benchmark 를 늘려도 fixed send lifetime gap 을 닫지 못한다.
- default promotion 은 fixed registration lifetime, zero-copy, fallback, IPv6/UDP 범위가 얽힌 더 큰 결정이다.

판단: fixed send lifetime boundary 이후로 둔다.

### 후보 C: queue/connection lifetime fixed send registration owner

장점:

- D210 실패 원인 후보인 per-send registration churn 을 제거한다.
- production path 재연결 전에 fixed buffer table lifetime 과 send completion lifetime 을 분리할 수 있다.
- 기존 `IoUringTcpConnectionResource`가 send context, receive block, length prefix scratch 를 소유하므로
  connection-scoped owner 를 붙이기 자연스럽다.

단점:

- 단순 payload helper 보다 구현 범위가 넓다.
- pool block 이 여러 개라면 connection owner 가 어떤 block 을 어떤 buffer index 로 등록할지 정책이 필요하다.
- 등록 가능한 fixed buffer 수와 fallback 기준을 정해야 한다.

판단: 다음 단위로 채택한다.

## 결정

D217 다음 단위는 **TCP connection-scoped fixed send registration owner 설계/구현 계획**이다.

핵심 방향:

1. 새 owner 는 `IoUringTcpConnectionResource` 또는 그 하위 객체가 소유한다.
2. owner 는 connection start 또는 첫 fixed-send 사용 전에 bounded fixed buffer table 을 등록한다.
3. send hot path 는 등록된 buffer index 를 조회하고 `TrySubmitWriteFixed(...)`를 제출할 뿐,
   `RegisterBuffers`/`UnregisterBuffers`를 호출하지 않는다.
4. owner dispose 는 send pump task completion 과 in-flight send drain 이후에만 수행한다.
5. 등록되지 않은 payload block 이거나 index lookup 이 실패하면 기존 `SendArrayAsync` fallback 을 사용한다.
6. 이 단계는 TCP payload fixed-write 재연결의 선행 수명 모델이며, 아직 zero-copy send 가 아니다.

## 등록 모델

첫 구현은 **connection-scoped bounded registration window** 로 제한한다.

- owner 이름 후보: `IoUringFixedSendBufferRegistry`
- 위치 후보: `src/Hps.Transport.IoUring/`
- 소유자: `IoUringTcpConnectionResource`
- 등록 대상: fixed send 에 사용할 pinned byte[] block
- 조회 key: underlying `byte[]` reference identity
- 조회 결과: buffer index, payload offset, payload length
- capacity: 작은 상수로 시작한다. 구현 계획에서 현재 pool/block 사용 흐름을 더 확인한 뒤 정한다.

registration table 이 가득 찬 경우의 v1 정책:

- 새 block 을 register/unregister churn 으로 밀어내지 않는다.
- lookup/register 실패 시 기존 `SendArrayAsync` fallback 으로 보낸다.
- drop/backpressure 정책은 send queue 계층이 이미 담당하므로 registration miss 를 메시지 drop 으로 해석하지 않는다.

## 소유권 모델

send queue/in-flight payload 소유권은 기존 규칙을 유지한다.

- queue 는 enqueue 된 `TransportSendBuffer` ref 를 소유한다.
- send pump 는 `TryBeginInFlightSend`로 in-flight ref 를 소유한다.
- `InFlightSend.Dispose()` 또는 `Complete()` 경계에서 기존 ref 를 반환한다.
- fixed registration owner 는 payload ref 를 소유하지 않는다.
- fixed registration owner 는 pinned array 주소가 pool return 후 재사용될 수 있음을 고려해야 하므로,
  등록된 block 이 pool 로 반환되지 않도록 별도 block ownership 을 갖거나, pool block lifetime 과 직접 연결된 등록 방식이 필요하다.

첫 구현 계획에서는 이 지점을 반드시 Red test 로 고정한다.
등록 owner 가 단순히 payload block reference 만 기억하고 ref 를 잡지 않으면, block 이 pool 로 돌아간 뒤에도
kernel fixed table 에 남을 수 있다. 따라서 owner 는 등록된 block 에 대해 명시적 lifetime guard 를 가져야 한다.

가능한 guard 방식:

- connection owner 가 별도 send-fixed 전용 pinned blocks 를 pool 에서 대여해 등록하고, payload 를 그 block 으로 1회 복사한다.
- 또는 payload block 자체에 registration ref 를 붙일 수 있는 pool/RefCountedBuffer 확장을 설계한다.

현재 Interface Server 목표는 구독자당 복사 0회이며, TCP publish 의 공유 payload buffer 1회 복사는 이미 허용되어 있다.
send-fixed 전용 block 으로 추가 복사하면 목표와 충돌할 수 있으므로 v1에서는 payload block registration ref 쪽이 더 적합하다.
다만 이 확장은 `RefCountedBuffer` public API 를 넓히기 전에 internal owner 로 최소화해야 한다.

## 다음 구현 계획의 최소 단위

구현 계획은 다음 순서로 쪼갠다.

1. pure registry contract
   - byte[] identity 를 buffer index 로 등록/조회한다.
   - capacity 초과 시 register miss 를 반환하고 기존 등록을 유지한다.
   - unregister 는 owner dispose 에서만 발생한다.
2. registration lifetime guard contract
   - registered block 이 owner dispose 전 pool 로 반환되지 않도록 ref 또는 guard 를 소유한다.
   - dispose 는 guard 를 정확히 1회 반환한다.
3. `IoUringTcpConnectionResource` wiring
   - resource 가 fixed send registry 를 소유한다.
   - resource dispose 는 send pump task/in-flight drain 이후 registry 를 dispose 하는 기존 shutdown ordering 과 맞춘다.
4. payload send path opt-in shape
   - 실제 production default 를 바로 바꾸지 않고, test seam 또는 internal path 로 fixed lookup/WRITE_FIXED shape 를 고정한다.
   - remote Linux gate 전에는 fallback path 를 유지한다.
5. remote contract gate
   - Linux available 환경에서 TCP loopback, fixed-send lease evidence, socket fixed-write evidence, 새 registry lifetime evidence 를 확인한다.

## 테스트 전략

테스트는 Red-Green 으로 작성한다.

- pure registry tests:
  - 같은 block 은 같은 buffer index 를 반환한다.
  - capacity 초과 시 기존 index 를 evict 하지 않고 register miss 를 반환한다.
  - dispose 는 registration owner 를 정확히 1회 해제한다.
- lifetime tests:
  - registered payload block 의 ref guard 가 owner dispose 전까지 유지된다.
  - owner dispose 후 ref guard 가 반환된다.
  - registration 실패 시 guard 는 rollback 된다.
- transport shape tests:
  - `IoUringTcpConnectionResource`가 fixed send registry owner 를 노출하지 않고 내부 소유한다.
  - `IoUringTransport.StopAsync` ordering 이 send pump task/in-flight drain 이후 registry dispose 로 수렴한다.
- remote tests:
  - Linux capability available 환경에서 새 registry evidence test 가 Passed 여야 한다.
  - hang 이 발생하면 D215 blame-hang/diag artifact 로 원인을 확인한다.

각 test method 바로 위에는 무엇을 검증하는지 한국어 주석을 남긴다.

## 범위 제외

- production TCP payload path 를 기본 `WRITE_FIXED`로 재연결
- TCP length prefix fixed-write 전환
- UDP fixed-buffer send
- `IORING_OP_SEND_ZC` 또는 zero-copy notification CQE 처리
- default backend promotion
- latency hard gate 또는 warning-as-failure
- transport-wide global registration cache

## 리스크와 완화

- 리스크: registration owner 가 payload ref 를 오래 붙잡아 pool pressure 를 높일 수 있다.
  - 완화: capacity 를 bounded 로 두고, miss 시 fallback 을 사용한다.
- 리스크: fixed table unregister 가 늦은 CQE보다 먼저 발생할 수 있다.
  - 완화: dispose ordering 을 send pump task completion 과 in-flight drain 이후로 고정한다.
- 리스크: fixed registration cache 가 복잡해져 hot path lock contention 을 만들 수 있다.
  - 완화: 첫 구현은 connection-local owner 로 제한하고, lookup path 는 단순 reference identity map 으로 유지한다.
- 리스크: registration miss fallback 이 성능 기대를 흐릴 수 있다.
  - 완화: v1 목적은 correctness/lifetime boundary 이며, hit ratio/benchmark 는 후속 artifact 로 판단한다.

## 검증

이 설계 단위 자체는 문서 변경이므로 다음으로 검증한다.

- 실제 `IoUringTransport.SendInFlightAsync`, `IoUringFixedSendLease`, `IoUringRegisteredBufferSet`,
  `IoUringTcpConnectionResource` 코드와 충돌하지 않는지 대조한다.
- `rg`로 placeholder, 불명확 표현, excluded scope 충돌을 확인한다.
- `git diff --check`를 통과해야 한다.

## 다음 단계

이 문서가 승인되면 구현 계획을 작성한다.
구현 계획은 pure registry, lifetime guard, resource wiring, opt-in shape, remote gate 문서화의 task 로 나눈다.
