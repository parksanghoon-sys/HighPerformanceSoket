# RIO UDP receive window hardening 설계

## 상태

Proposed.

이 문서는 D112 scratch benchmark 와 D113 receive registration 보정 이후 남은 RIO UDP open-loop delivery loss 를 다룬다.
구현 전 검토 대상이며, 구현 계획은 이 설계가 수락된 뒤 별도 문서로 분리한다.

## 배경

RIO UDP backend 는 native Ex operation, endpoint owner, receive/send loop, diagnostics parity 를 갖췄다.
D113으로 두 가지 실제 결함도 보정했다.

- `RIOReceiveEx` completion 뒤 handler dispatch 전에 receive buffer registration 을 해제한다.
- UDP receive block 을 4096B 에서 SAEA 기준선과 같은 8192B 로 올린다.

그 결과 RIO UDP smoke 와 closed-loop load 는 broker fan-out payload 를 전달할 수 있다.
하지만 scratch artifact `artifacts/benchmarks/rio-udp/2026-06-26/session-01/`에서 다음 상태가 확인됐다.

- SAEA UDP summary: hard-passed true, warning 0.
- RIO UDP closed-loop load: sent/received 3000/3000, payload-errors 0, p99 약 16.7ms.
- RIO UDP open-loop: sent 3000 / received 2263 / payload-errors 0, hard-passed false, warning 3.

D111의 no-prefetch 정책은 pool ownership 경계를 단순하게 만들었지만, handler dispatch 와 다음 receive post 사이에
RIO provider 가 datagram 을 받을 준비가 안 된 window 를 만든다. open-loop 100Hz publish 에서는 이 window 가 실제 loss 로 드러났다.

## 목표

- RIO UDP receive window 를 줄여 open-loop delivery loss 를 줄인다.
- handler dispatch 동시성은 늘리지 않고, broker state mutation 의 현재 단일 handler 호출 모델을 유지한다.
- receive buffer ownership, native registration lifetime, close drain 을 명확히 유지한다.
- SAEA/RIO public `ITransport` 계약과 diagnostics schema 를 바꾸지 않는다.
- 구현 뒤 D112 scratch command 로 RIO UDP load/open-loop artifact 를 다시 수집할 수 있게 한다.

## 비목표

- UDP 신뢰성, 재전송, 순서보장, 혼잡제어를 만들지 않는다.
- RIO UDP default backend 승격을 결정하지 않는다.
- IPv6 UDP 지원을 이 작업에 포함하지 않는다.
- CI latency hard gate 또는 warning-as-failure 를 승격하지 않는다.
- handler 를 여러 task 에서 병렬 호출하지 않는다.

## 접근안

### 접근 A — no-prefetch 유지

현재 구조를 유지하고 RIO open-loop loss 를 v1 제약으로 문서화한다.

장점:

- 구현 변경이 없다.
- pool 대여 수와 close drain 이 단순하다.
- D111의 원래 해석을 그대로 유지한다.

단점:

- D112 open-loop artifact 가 계속 fail 이다.
- Interface Server 목표인 4096B x 100Hz UDP publish 를 RIO backend evidence 로 뒷받침하기 어렵다.
- default promotion 재평가가 계속 막힌다.

판단: scratch evidence 이후에는 유지할 이유가 약하다.

### 접근 B — one-deep receive pre-post

현재 handler 를 호출하기 전에 다음 `RIOReceiveEx`를 먼저 post 한다.
항상 handler dispatch 와 동시에 최대 1개의 다음 receive 가 kernel/provider 쪽에 대기한다.
handler 호출은 여전히 receive loop 단일 흐름에서 순차적으로 수행한다.

장점:

- handler 병렬성 없이 receive-not-posted window 를 대부분 제거한다.
- pending datagram ownership 모델을 크게 바꾸지 않는다.
- close drain 과 leak 검증을 한 단계 작은 단위로 설계할 수 있다.
- D111의 “blocked-window retention 보장 없음”을 “one-deep pre-post 로 최소 흡수”로 자연스럽게 갱신할 수 있다.

단점:

- dispatch 중 endpoint close 가 발생하면 이미 post 된 receive operation 을 정리해야 한다.
- receive operation owner 타입이 필요하다.
- pool 대여 수가 handler dispatch 중 최대 1개 늘어난다.

판단: 이번 단계의 권장안이다. RIO UDP v1의 구조를 크게 흔들지 않으면서 benchmark loss 원인을 직접 줄인다.

### 접근 C — bounded outstanding receive queue

receive depth 를 2~N 으로 열고, completion queue 에서 완료된 datagram 을 순차 dispatch 한다.

장점:

- burst absorption 이 가장 좋다.
- 향후 high-rate UDP workload 로 확장하기 쉽다.

단점:

- request context 기반 completion-to-buffer mapping 이 필요하다.
- close drain, endpoint close notification, handler exception policy, pool high-watermark 관측을 다시 설계해야 한다.
- UDP reliability/smoothing 쪽 의미가 커져 v1 범위를 넓힌다.

판단: one-deep pre-post 이후에도 open-loop loss 가 크면 후속 설계로 승격한다.

## 결정

이번 구현 후보는 접근 B, one-deep receive pre-post 로 한다.

핵심 정책:

- handler dispatch 전에 다음 receive 를 post 한다.
- 동시에 handler 는 하나만 호출한다.
- outstanding receive 는 최대 1개만 유지한다.
- receive operation 은 `RefCountedBuffer`, data buffer id, remote address block/id 를 함께 소유하는 작은 internal owner 로 둔다.
- completion 이 끝난 operation 은 handler dispatch 전에 data buffer id 를 해제한다(D113 유지).
- post 된 다음 operation 이 endpoint close 로 취소되면 completion 을 관측한 뒤 buffer id 해제와 datagram release 를 수행한다.

## 예상 구조

### `RioUdpReceiveOperation`

신규 internal owner 후보.

책임:

- `RefCountedBuffer` 대여.
- receive data buffer registration id 보존.
- remote address scratch block 과 registration id 보존.
- `RIOReceiveEx` post.
- completion 이후 `SetLength`, remote endpoint decode, data registration 해제.
- dispose/cancel cleanup 에서 datagram release 와 native deregister 를 정확히 1회 수행.

remote address block 은 현재 endpoint lifetime block 하나를 공유하고 있다.
one-deep pre-post 에서는 dispatch 중 다음 receive 가 같은 remote address block 을 덮을 수 있으므로,
remote address block 은 operation-local 로 바꾼다.
send address block 은 send loop 전용이므로 그대로 endpoint lifetime resource 로 유지한다.

### receive loop 흐름

초기:

1. endpoint 가 첫 receive operation 을 만들고 post 한다.
2. loop 는 current operation completion 을 기다린다.

반복:

1. current completion 을 읽고 data registration 을 해제한다.
2. endpoint 가 아직 open 이면 next receive operation 을 만들고 post 한다.
3. current datagram 과 decoded remote endpoint 를 handler 에 dispatch 한다.
4. dispatch 가 끝나면 next 를 current 로 교체한다.

오류:

- current completion status 가 실패면 current 를 release 하고 endpoint close notify 로 수렴한다.
- next post 실패면 current dispatch 전 endpoint close 로 수렴한다. 이 경우 current datagram 도 release 한다.
- handler exception 은 기존 정책대로 endpoint close notify 로 수렴한다.

close:

- endpoint close 가 관측되면 이미 완료된 current datagram 은 release 한다.
- post 된 next receive 는 socket close 로 completion/cancel 을 유도하고, loop 가 completion 을 관측해 deregister/release 한다.
- completion 을 관측할 수 없는 native failure 경로가 있으면 endpoint close cleanup 이 leak 없이 끝나는지 별도 테스트로 고정한다.

## 테스트 전략

Red tests:

- handler 가 첫 datagram 처리 중일 때 두 번째 datagram 을 보내도, one-deep pre-post 가 두 번째 datagram 을 수신한다.
  기존 D111 테스트는 no-prefetch 를 기대했으므로 새 정책에 맞게 바뀐다.
- endpoint close 중 outstanding next receive operation 이 leak 없이 정리된다.
- handler exception 중 pre-post 된 next receive operation 이 leak 없이 정리되고 close notification 은 1회만 발생한다.

Green verification:

- focused `RioTransportUdpTests`.
- focused `Hps.Transport.Rio.Tests`.
- `Hps.Benchmarks.Tests`.
- RIO UDP smoke.
- RIO UDP `--baseline-suite ... --protocol udp --backend rio --runs 1` scratch 재수집.

기대 artifact:

- RIO UDP open-loop `payload-errors`는 0을 유지한다.
- `received`가 3000에 가까워지는지 확인한다.
- p99 16ms tail 이 계속 남으면 receive pre-post와 별개로 UDP completion wait/notification 설계를 다음 후보로 둔다.

## 문서/결정 갱신

구현이 수락되면 D111은 “no-prefetch”에서 “one-deep pre-post” 정책으로 대체하거나 D114로 supersede 한다.
D113은 유지한다. receive registration 해제 시점과 8192B block size 는 one-deep pre-post 이후에도 필요하다.

## 열린 질문

현재 설계는 first implementation 에서 receive depth 를 1로 고정한다.
depth 를 설정값으로 열지는 않는다. open-loop artifact 가 계속 fail 일 때만 bounded depth 설계를 별도 작업으로 승격한다.
