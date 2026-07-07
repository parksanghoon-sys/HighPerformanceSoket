# D199 io_uring post-D198 next scope design

## 상태

Accepted.

## 배경

D198에서 D197 socket fixed-write evidence 가 원격 `iouring-linux-contract.yml` gate 를 통과했다.

- workflow run: `28837405462`
- head SHA: `84af508110a1c104c8b484cf138e05c83f8893d8`
- test counters: total/executed/passed 63, failed 0
- 통과 test: `WriteFixed_WhenLinuxCapabilityAvailable_WritesRegisteredBufferSliceToSocketPair`
- capability: `Available`
- completion result: `2`
- 검증 payload: registered buffer `{10,20,30,40}`의 offset 1 length 2를 stream socket fd 로 보내고 `{20,30}`을 읽었다.

이 결과는 Linux stream socket fd 에 `IORING_OP_WRITE_FIXED`를 제출할 수 있다는 kernel contract 를 닫는다.
하지만 production TCP send pump 를 fixed-write 로 바꾸기에는 아직 다음 경계가 남아 있다.

- `TransportConnection.InFlightSend`는 `RefCountedBuffer` ref 를 completion 또는 unwind 시점에 정확히 1회 반환한다.
- `IoUringRegisteredBufferSet`은 queue 단위 fixed buffer registration owner 이지만, 아직 in-flight send lifetime 과 연결되지 않았다.
- 현재 `IoUringTransport.SendInFlightAsync`는 TCP length prefix 를 connection resource 의 4-byte scratch block 으로 먼저 보내고, payload 는 `TrySubmitSend`로 보낸다.
- TCP outbound frame 은 D065에 따라 `4-byte big-endian length prefix + payload`이고, prefix 와 payload 의 lifetime/registration 정책을 한 번에 바꾸면 실패 원인을 분리하기 어렵다.
- UDP send path 는 `sendmsg`/`IoUringUdpMessageBuffer` 기반이므로 D198의 stream socket fixed-write evidence 와 직접 연결하지 않는다.

따라서 D198 이후 바로 TCP/UDP pump 를 바꾸면 native contract, registration lifetime, close drain, length prefix framing, fallback policy 를 동시에 건드린다.
다음 단위는 이 중 가장 위험한 ownership gap 을 먼저 좁혀야 한다.

## 목표

다음 구현 단위는 production pump 통합이 아니라 **TCP fixed-send lease owner 계약 설계와 구현 계획**으로 둔다.

이 설계의 목적은 `RefCountedBuffer` payload slice 를 kernel fixed buffer registration lifetime 과 어떻게 묶을지 결정하는 것이다.
구체적으로는 다음 질문을 닫는다.

- in-flight send 가 살아 있는 동안 registered buffer 가 unregister 되지 않음을 어떻게 보장할 것인가?
- send completion, socket error, connection close, cancellation/unwind 중 어느 경로에서도 payload ref 와 registration lease 가 정확히 한 번 정리되는가?
- TCP length prefix 는 payload fixed-write lease 와 분리된 connection scratch 로 유지할 것인가?
- 첫 production 연결 전에 어떤 contract test 로 lifetime 모델을 증명할 것인가?

## 후보 비교

### 후보 A: TCP send pump 를 바로 `TrySubmitWriteFixed`로 변경

장점:

- 실제 성능 경로에 가장 직접적으로 접근한다.
- benchmark artifact 로 차이를 빠르게 관측할 수 있다.

단점:

- length prefix, payload registration, in-flight release, close drain, socket error unwind 를 동시에 바꾼다.
- 실패 시 fixed-write native 문제인지, registration lease 문제인지, 기존 send queue ownership 문제인지 분리하기 어렵다.
- 현재 `IoUringRegisteredBufferSet`은 단순 queue-wide owner 이며 per-send lease 나 close-safe outstanding tracking 을 제공하지 않는다.

판단: 아직 이르다. D199 범위에서 제외한다.

### 후보 B: UDP fixed-buffer send 또는 zero-copy send 로 이동

장점:

- 커널 단 복사 감소라는 장기 목표에 가까워 보인다.

단점:

- UDP는 현재 `sendmsg` 기반이며 D198의 stream socket `WRITE_FIXED` evidence 와 직접 같은 경로가 아니다.
- zero-copy send 는 일반 completion 과 별도 notification completion, payload reuse 금지 시점, fan-out shared payload release 정책을 새로 설계해야 한다.

판단: 별도 설계가 필요한 후속이다. D199 범위에서 제외한다.

### 후보 C: TCP fixed-send lease owner 설계와 contract 구현 계획

장점:

- D198 이후 production pump 전환 전에 남은 가장 큰 gap 인 lifetime/ownership 을 먼저 닫는다.
- `TransportConnection.InFlightSend`의 ref-count ownership 과 `IoUringRegisteredBufferSet`의 native registration ownership 을 명시적으로 연결한다.
- 첫 구현을 pure/internal contract test 로 제한할 수 있어 fallback/default/benchmark 경로를 흔들지 않는다.
- length prefix 는 기존 connection scratch 전송으로 남겨 payload fixed-write lease 와 분리할 수 있다.

단점:

- 아직 성능 개선은 없다.
- lease owner shape 를 잘못 크게 만들면 production pump 요구보다 앞선 abstraction 이 될 수 있다.

판단: 다음 단위로 채택한다.

### 후보 D: benchmark/diagnostics 보강

장점:

- 이후 pump 변경의 회귀 관측성이 좋아진다.

단점:

- 현재 blocked gap 은 관측성보다 fixed buffer lifetime 계약이다.
- benchmark 를 먼저 늘려도 production fixed-write 연결의 안전성은 증명되지 않는다.

판단: 후속 보강으로 둔다.

## 결정

D199 다음 단위는 **TCP fixed-send lease owner 구현 계획**이다.

구현 계획에서 다룰 최소 생산 코드 후보는 다음처럼 제한한다.

- `IoUringFixedSendLease` 또는 동등한 internal owner 를 추가한다.
- lease 는 `RefCountedBuffer` payload slice, registered byte array, buffer index, offset, length 를 하나의 in-flight scope 로 묶는다.
- lease dispose 는 registration unregister/pin 해제와 payload ref release 순서를 명확히 가진다.
- lease 는 completion 전에는 unregister 되지 않는다.
- dispose 는 idempotent 해야 한다.
- TCP length prefix 는 이번 단계에서 fixed-write lease 에 포함하지 않고 기존 4-byte connection scratch send 로 유지한다.

첫 구현 계획은 production `SendInFlightAsync`를 바꾸지 않는다.
대신 다음 contract 를 Red-Green 으로 먼저 고정한다.

- lease type 존재와 internal surface contract.
- lease dispose 가 payload ref 를 정확히 1회 반환한다.
- lease lifetime 동안 registered buffer count 또는 registration owner 가 살아 있음을 관측한다.
- dispose 두 번, send 실패 unwind, close-like early dispose 에서 leak/double release 가 없는지 검증한다.
- Linux capability available 환경에서는 가능하면 lease 가 소유한 registered buffer slice 로 `TrySubmitWriteFixed`를 실행하고 completion 뒤 dispose 하는 native contract 를 추가한다.

## 제외 범위

- production `IoUringTransport.SendInFlightAsync`를 fixed-write 로 변경
- UDP send pump 변경
- zero-copy send, `send_zc`, notification CQE 처리
- transport-wide payload registration cache
- `RefCountedBuffer` public API 확장
- `TransportFactory.CreateDefault()` promotion
- latency hard gate 또는 warning-as-failure

## 검증 계획

이번 D199 문서 단위:

- 실제 `IoUringTransport`, `TransportConnection`, `IoUringRegisteredBufferSet` 경계와 대조한다.
- `git diff --check`로 문서 whitespace 를 확인한다.
- 코드 변경이 없으므로 build/test 는 생략 가능하다.

다음 D200 구현 계획 단위:

- `docs/superpowers/plans/2026-07-07-iouring-fixed-send-lease-owner.md`를 작성한다.
- 각 task 는 Red assertion failure 를 먼저 만들고 Green 구현으로 닫는다.
- production pump 연결은 별도 후속 설계로 남긴다.

## 상태 문서 반영

- `DECISIONS.md`: D199 active decision 으로 추가한다.
- `TODOS.md`: D198 재평가를 완료로 이동하고 D200 구현 계획 작성을 Current TODO 로 둔다.
- `CURRENT_PLAN.md`: D199 결정과 다음 실행 지점을 기록한다.
- `CHANGELOG_AGENT.md`: D199 설계 결과와 검증 한계를 기록한다.
