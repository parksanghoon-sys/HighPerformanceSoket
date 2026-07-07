# D206 이후 io_uring TCP payload fixed-write 통합 설계

## 상태

Accepted.

## 배경

D206에서 D205 TCP send pump shutdown tracking fix 와 D203 fixed-send lease native evidence 가 원격
`iouring-linux-contract.yml` gate 를 통과했다.

- workflow run: `28842952688`
- head SHA: `6e9e14d679740235cfe79f10faae02fc3e356b09`
- TRX counters: total/executed/passed 70, failed 0
- `TcpLoopback_WhenIoUringAvailable_SendsQueuedPayloadToPeer`: Passed
- `Lease_WhenLinuxCapabilityAvailable_WritesRegisteredPayloadSliceToSocketPair`: Passed
- capability: `io_uring capability status: Available`
- socket fixed-write evidence: `fixed socket write completion result: 2`

이 evidence 는 Linux stream socket fd 에 `IORING_OP_WRITE_FIXED`로 registered buffer slice 를 쓸 수 있고,
lease 가 completion 이후 registration owner 와 payload ref 를 정리할 수 있음을 보여준다.

다만 production TCP send pump 는 아직 다음 구조다.

- TCP length prefix 는 `IoUringTcpConnectionResource.LengthPrefixBlock` scratch buffer 로 `TrySubmitSend` 전송.
- payload 는 `RefCountedBuffer`의 underlying array slice 를 `TrySubmitSend`로 전송.
- `TransportConnection.InFlightSend`가 pending queue 에서 dequeue 된 transport-owned ref 를 completion/unwind 때 1회 release.
- D205 이후 `IoUringTransport.StopAsync`는 close 로 unregister 된 connection 의 send pump task 까지 기다린다.

따라서 fixed-write production 통합을 검토할 수 있는 최소 조건은 갖춰졌다. 그러나 `IoUringFixedSendLease`는 dispose 시
payload ref 를 release 하므로, production pump 에서 기존 `InFlightSend` ref 와 같은 ref 를 공유하면 double release 가 된다.
production 통합은 lease 전용 payload ref 를 별도로 획득하는 경계를 먼저 명시해야 한다.

## 목표

다음 구현 단위는 **io_uring TCP payload fixed-write pump integration**으로 잡는다.

단, 이 통합은 성능 최적화 완료나 zero-copy 달성이 아니라 production data path 에 fixed-write payload submission 을
연결하는 correctness/evidence 단계다.

구체 목표:

- TCP payload 전송만 `TrySubmitWriteFixed` 기반 helper 로 바꾼다.
- TCP length prefix 는 기존 `TrySubmitSend` scratch path 로 유지한다.
- `IoUringFixedSendLease` 사용 시 send pump 전용 payload ref 를 명시적으로 획득하고, 실패 시 반드시 반환한다.
- `InFlightSend` transport-owned ref 와 lease-owned ref 를 분리해 double release 와 leak 을 막는다.
- Linux contract workflow 에서 TCP send loopback, fixed-send lease evidence, payload fixed-write pump path 가 함께 통과함을 확인한다.

## 후보 비교

### 후보 A: 바로 `SendInFlightAsync` payload 를 `IoUringFixedSendLease.Create(...)`로 감싸기

장점:

- 코드 변경이 가장 작다.
- 기존 native evidence helper 를 바로 사용한다.

단점:

- 현재 lease 는 dispose 시 payload ref 를 release 한다.
- production `InFlightSend`도 같은 `TransportSendBuffer` ref 를 release 하므로, 별도 `AddRef()` 없이 쓰면 double release 가 된다.
- `AddRef()`를 호출하더라도 registration 실패 시 추가 ref 를 반환하는 실패 경계가 필요하다.

판단: 그대로 적용하지 않는다.

### 후보 B: production-safe lease acquisition helper 를 먼저 만들고 payload path 에만 연결

장점:

- 기존 `InFlightSend` ref 와 lease ref 의 역할을 분리한다.
- registration 실패, submit 실패, completion 실패, close unwind 모두 같은 `using lease` 경계로 수렴시킬 수 있다.
- length prefix path 를 유지하므로 framing 변경과 payload fixed-write 변경을 분리한다.
- D206 evidence 를 실제 production TCP payload path 로 연결하면서도 UDP/zero-copy/default promotion 범위를 열지 않는다.

단점:

- per-send `RegisterBuffers`/`UnregisterBuffers` 비용이 생겨 성능 개선으로 주장할 수 없다.
- 후속으로 registration cache 또는 benchmark evidence 가 필요하다.

판단: 다음 구현 단위로 채택한다.

### 후보 C: connection-local registration cache 를 먼저 구현

장점:

- per-send registration 비용을 줄일 수 있다.
- 장기 성능 목표에 더 가깝다.

단점:

- cache lifetime, eviction, outstanding send lease, fan-out shared payload reuse, close drain 이 함께 얽힌다.
- D206의 다음 단위로는 변경 범위가 크고 실패 원인을 분리하기 어렵다.

판단: 후속 최적화로 둔다.

### 후보 D: benchmark artifact 를 먼저 추가 수집

장점:

- fixed-write 전환 전후의 기준선을 더 많이 확보한다.

단점:

- 아직 production fixed-write path 가 없으므로 benchmark 가 직접 판단할 대상이 없다.
- 현재 판단의 blocking gap 은 관측성이 아니라 ownership boundary 다.

판단: production payload fixed-write correctness gate 이후에 수행한다.

## 결정

다음 구현 단위는 **TCP payload fixed-write integration with production-safe lease ref acquisition**이다.

구현 방향:

1. `IoUringFixedSendLease`에 send pump 전용 factory 를 추가한다.
   - 예: `CreateForSendPump(IoUringQueue queue, TransportSendBuffer sendBuffer)`
   - 내부에서 `sendBuffer.Buffer.AddRef()`로 lease-owned ref 를 획득한다.
   - registration/lease 생성 실패 시 추가 ref 를 즉시 release 한다.
   - 반환된 lease dispose 는 기존 계약대로 registration owner 와 lease-owned payload ref 를 정리한다.
2. `IoUringTransport.SendInFlightAsync`의 payload 전송 구간만 fixed-write helper 로 바꾼다.
   - length prefix 는 기존 `SendArrayAsync`와 `TrySubmitSend`를 유지한다.
   - payload length 0이면 lease 를 만들지 않는다.
3. 새 helper 는 `IoUringFixedSendLease`를 `using`으로 잡고 `TrySubmitWriteFixed` completion 을 기다린다.
   - completion result 가 0 이하이거나 remaining 범위를 넘으면 기존 send path 와 같은 socket error 로 수렴한다.
   - partial completion 은 기존 `SendArrayAsync`처럼 offset/remaining 루프로 처리하되, 첫 단위에서는 하나의 lease 안에서 같은 registered array/range 를 반복 submit 한다.
4. `InFlightSend`는 기존 transport-owned ref release 책임을 유지한다.
   - lease 는 lease-owned 추가 ref 만 release 한다.
   - 따라서 정상 completion 후 `lease.Dispose()`와 `inFlight.Complete()`가 각각 다른 ref 를 반환한다.
5. 원격 Linux contract gate 를 필수로 둔다.
   - local Windows 에서는 shape/ownership tests 와 capability guard 를 통과한다.
   - Linux available 환경에서는 실제 TCP send loopback 이 fixed-write payload path 를 지나야 한다.

## 테스트 전략

### Red/Green 단위 1: send pump lease ref acquisition

목표:

- send pump 전용 factory 가 payload ref 를 직접 `AddRef()` 하고 dispose 에서 반환하는지 검증한다.
- registration 생성 실패 시 추가 ref 가 누수되지 않는지 검증한다.

검증 예:

- fake registration factory 또는 failing registration owner 를 통해 `CreateForSendPump` 실패 경로를 만든다.
- 성공 경로에서는 caller/publisher ref 와 transport ref 가 남아 있는 동안 lease dispose 가 lease ref 만 반환하는지 pool count 로 확인한다.

### Red/Green 단위 2: `IoUringTransport` payload path shape

목표:

- `SendInFlightAsync` payload 구간이 `SendFixedPayloadAsync` 또는 동등한 fixed-write helper 를 호출하는 구조를 고정한다.
- length prefix path 는 `SendArrayAsync`에 남아 있음을 확인한다.

검증 예:

- reflection shape test 로 `SendFixedPayloadAsync` 존재와 `TrySubmitWriteFixed` 사용 surface 를 고정한다.
- 기존 Linux-gated TCP send loopback test 는 remote gate 에서 실제 behavior 를 검증한다.

### Red/Green 단위 3: Linux remote contract gate

목표:

- `iouring-linux-contract.yml`에서 `Hps.Transport.IoUring.Tests` 전체 failed 0을 확인한다.
- `TcpLoopback_WhenIoUringAvailable_SendsQueuedPayloadToPeer`가 Passed 인지 확인한다.
- fixed-send lease native evidence 와 socket fixed-write evidence 도 계속 Passed 인지 확인한다.

## 범위 제외

- TCP length prefix fixed-write 전환
- UDP send pump fixed-buffer 전환
- zero-copy send 또는 `send_zc`
- connection-local 또는 transport-wide registration cache
- default backend promotion
- latency hard gate 또는 warning-as-failure
- benchmark artifact 자동 채택

## 리스크와 완화

- 리스크: per-send register/unregister 로 성능이 나빠질 수 있다.
  - 완화: 이번 단위는 correctness/evidence 단계로만 기록하고, benchmark/registration cache 는 후속으로 둔다.
- 리스크: lease ref 와 in-flight ref 가 섞이면 double release 또는 leak 이 생긴다.
  - 완화: send pump 전용 factory 에서 AddRef/rollback 을 닫고, pool count 테스트로 검증한다.
- 리스크: close 중 completion loop 를 너무 일찍 dispose 하면 send pump finally 가 실행되지 못한다.
  - 완화: D205 task tracking 구조를 유지하고, remote TCP loopback pool leak gate 를 계속 사용한다.
- 리스크: fixed-write helper partial completion 처리에서 offset 계산이 틀릴 수 있다.
  - 완화: helper 단위 테스트와 Linux socketpair evidence 를 offset 1/length 2 같은 slice payload 로 유지한다.

## 상태 문서 반영

- `DECISIONS.md`: D207로 다음 단위를 TCP payload fixed-write integration 으로 기록한다.
- `TODOS.md`: D206 재평가를 완료하고 D207 구현 계획 작성을 current TODO 로 둔다.
- `CURRENT_PLAN.md`: 다음 실행 지점을 D207 구현 계획 작성으로 갱신한다.
- `CHANGELOG_AGENT.md`: D207 설계 결과와 검증 범위를 기록한다.
