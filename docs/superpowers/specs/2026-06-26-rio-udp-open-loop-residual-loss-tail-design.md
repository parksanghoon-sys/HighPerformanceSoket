# RIO UDP open-loop residual loss/tail 재평가 설계

## 상태

Accepted, 2026-06-26.

이 문서는 D114 one-deep pre-post 이후에도 남은 RIO UDP open-loop delivery loss 와 p99 tail 의 다음 처리 방향을 정한다.
구현은 포함하지 않고, source/benchmark evidence 로 가장 작은 다음 구현 후보를 좁힌다.

## 문제

D114 이후 RIO UDP receive window 는 no-prefetch 에서 close-safe one-deep pre-post 로 바뀌었다.
이 변경은 handler dispatch 중 다음 `RIOReceiveEx`를 하나 미리 post 하므로 D111의 receive-not-posted window 를 줄인다.

하지만 2026-06-26 scratch benchmark `artifacts/benchmarks/rio-udp/2026-06-26/session-02/rio` 결과는 아직 목표를 만족하지 않는다.

- closed-loop load: sent 3000 / received 3000 / dropped 0 / payload-errors 0 / pool-rented 0 / actual-rate 99.7 Hz / p99 16719.2 us / passed true.
- open-loop: sent 3000 / received 2409 / dropped 0 / payload-errors 0 / pool-rented 0 / actual-rate 85.7 Hz / p99 16709.1 us / passed false.
- summary: hard-passed false, warning 3 (`load-p99-latency-high`, `open-loop-p99-latency-high`, `actual-rate-low`).

비교 기준으로 같은 harness 의 SAEA UDP `session-01/saea`는 다음과 같다.

- closed-loop load: sent/received 3000/3000, p99 814.2 us, passed true.
- open-loop: sent/received 3000/3000, p99 852.2 us, passed true.

따라서 benchmark harness 만으로 설명하기 어렵고, RIO UDP backend 의 completion wake 또는 native operation 비용을 먼저 봐야 한다.

## 관찰된 증상

### p99 tail

RIO UDP load/open-loop p99 는 모두 약 16.7 ms 이다.
first-half/second-half p99 growth ratio 는 약 1.01로 누적 악화라기보다 반복적으로 같은 tail 이 끼어드는 모양이다.

`RioTransport.WaitForUdpCompletionAsync(...)`는 다음 순서로 completion 을 기다린다.

1. `RIODequeueCompletion`을 반복 확인한다.
2. completion 이 없으면 `Task.Yield()`를 `UdpCompletionYieldBudget`만큼 반복한다.
3. 그래도 없으면 `Task.Delay(1)`로 fallback 한다.

Windows 기본 timer granularity 에서 `Task.Delay(1)`은 1ms가 아니라 약 15.6ms 이상으로 관측될 수 있다.
RIO UDP p99 16.7ms는 이 fallback 경로와 숫자가 맞다.

### open-loop actual-rate low

`TcpLoopbackRunResult.ActualRateHz`는 `Sent * 1000 / ElapsedMilliseconds`로 계산된다.
RIO UDP open-loop 는 sent 3000이지만 elapsed 가 약 35003ms 이므로 85.7 Hz 가 된다.
이는 30초 publish schedule 뒤 subscriber receive loop 가 누락된 datagram 을 기다리다가 5초 receive timeout 으로 종료된 결과다.

따라서 actual-rate-low warning 은 publisher 가 85.7Hz로 보냈다는 뜻이 아니라,
open-loop hard failure 로 runner 가 timeout 대기까지 포함했다는 뜻이다.

### drop/HWM

RIO UDP open-loop 의 `udp-pending-send-queue-high-watermark`는 2이고 dropped 는 0이다.
transport send queue capacity 16 drop-oldest 에 걸린 증상은 아니다.
payload-errors 와 pool-rented 도 0이므로 payload corruption 또는 refcount leak 로 보지 않는다.

## working pattern

RIO TCP path 는 이미 IOCP notification 기반 wait 를 사용한다.

- `RioConnectionResource`는 `RioCompletionPort.CreateSignal()`로 receive/send signal 을 만든다.
- `CreateCompletionQueue(..., signal.NotificationCompletionPointer)`로 CQ를 생성한다.
- `WaitForCompletionAsync(...)`는 dequeue 실패 시 `RIO_NOTIFY`를 arm 하고 signal 을 기다린다.

이 패턴은 RIO TCP benchmark tail 을 줄이기 위해 D103~D105에서 채택된 기준선이다.
RIO UDP endpoint 는 같은 native RQ/CQ 계열을 쓰지만, 현재 CQ를 notification pointer 없이 생성하고 bounded yield/delay polling 으로 기다린다.

## 후보

### 후보 A — receive depth 확대

`MaxOutstandingReceive`를 2 이상으로 늘리고 여러 receive operation 을 미리 post 한다.

장점:

- 순간 burst 흡수에는 가장 직접적이다.
- receive-not-posted window 를 더 줄일 수 있다.

단점:

- request context 기반 completion-to-operation mapping 이 필요하다.
- endpoint lifetime shared remote address block 을 operation-local address block 으로 바꿔야 한다.
- pool high-watermark, close drain, handler exception cleanup, ordering policy 가 모두 다시 커진다.
- D114가 막 닫은 close/resource ownership 범위를 다시 넓힌다.

판단: 아직 첫 후보로 보지 않는다.
현재 p99 tail 이 completion wake 경로와 더 강하게 맞고, receive depth 확대는 구조 변경 비용이 더 크다.

### 후보 B — UDP completion wait 를 IOCP/RIONotify 로 전환

RIO UDP endpoint 도 TCP RIO와 같은 completion signal 을 갖고, UDP receive/send CQ를 notification pointer 로 생성한다.
`WaitForUdpCompletionAsync(...)`는 TCP `WaitForCompletionAsync(...)`처럼 dequeue 실패 후 `RIONotify`를 arm 하고 signal 을 기다린다.

장점:

- p99 16.7ms tail 의 직접 후보인 `Task.Delay(1)` fallback 을 hot path 에서 제거한다.
- one-deep receive policy 와 `MaxOutstandingReceive = 1`을 유지하므로 D114 ownership 모델을 크게 흔들지 않는다.
- TCP RIO의 검증된 wait pattern 을 재사용한다.
- open-loop loss 가 completion wake tail 때문인지 확인하는 가장 작은 실험이 된다.

단점:

- UDP endpoint close-drain 경로에서 close 이후 completion 을 관측해야 하므로, signal dispose 순서와 CQ close 순서를 명확히 해야 한다.
- receive/send signal 을 endpoint resource 로 추가해야 한다.

판단: 다음 구현 후보로 채택한다.

### 후보 C — per-datagram receive registration 재사용

현재 `RioUdpReceiveOperation.Post()`는 datagram backing array 를 매 receive 마다 `RIORegisterBuffer`하고, completion 뒤 deregister 한다.
이를 endpoint-local receive registration cache 또는 reusable receive block 으로 줄인다.

장점:

- native register/deregister 비용을 줄일 수 있다.
- p50 개선 가능성이 있다.

단점:

- D113은 handler fan-out send path 와 receive registration 중첩을 피하기 위해 completion 뒤 registration 해제를 요구했다.
- fan-out payload 가 handler 이후에도 살아야 하므로 receive block 재사용은 소유권 모델을 다시 키운다.
- 현재 p50은 172~544us이고 핵심 실패 숫자는 16.7ms p99 tail 이므로 첫 후보로는 약하다.

판단: IOCP wait 이후에도 p50 또는 CPU 비용이 문제로 남을 때 별도 설계로 다룬다.

## 결정

다음 구현 후보는 후보 B, RIO UDP completion wait 의 IOCP/RIONotify parity 다.

정책:

- `RioUdpEndpoint`는 receive/send `RioCompletionSignal`을 소유한다.
- UDP receive/send CQ는 `Native.CreateCompletionQueue(size, signal.NotificationCompletionPointer)`로 생성한다.
- `WaitForUdpCompletionAsync(...)`는 dequeue-first 원칙을 유지한다.
- endpoint open 상태에서는 dequeue 실패 시 `Native.Notify(completionQueue)`를 arm 하고 signal wait 로 들어간다.
- `Task.Delay(1)` fallback 은 open 상태 hot path 에서 제거한다.
- close-drain 경로에서는 D114 원칙을 유지한다.
  receive loop 는 endpoint close 이후에도 먼저 CQ를 dequeue 해 이미 완료된 receive 를 회수하고,
  completion 이 없을 때만 bounded close fallback 으로 operation cleanup 을 마무리한다.
- receive depth 는 계속 1로 유지한다.
- send outstanding 도 계속 1로 유지한다.
- bounded receive queue, operation-local remote address block, receive registration reuse 는 이번 다음 구현 범위에서 제외한다.

## 구현 계획으로 넘길 작업 단위

### Task 1 — UDP endpoint notification resource shape

목표:

- `RioUdpEndpoint`에 receive/send signal owner 를 추가한다.
- UDP CQ 생성이 notification pointer 를 사용하도록 바꾼다.
- close/drain 에서 signal dispose 순서가 CQ close 와 충돌하지 않게 한다.

Red 후보:

- reflection 또는 focused RIO test 로 `RioUdpEndpoint`가 receive/send signal resource 를 갖는지 검증한다.
- 가능하면 native integration test 로 RIO UDP receive/send smoke 가 기존과 동일하게 통과하는지 확인한다.

### Task 2 — `WaitForUdpCompletionAsync` notification wait

목표:

- TCP `WaitForCompletionAsync(...)`의 dequeue -> arm notification -> signal wait 패턴을 UDP용으로 분리/재사용한다.
- open 상태의 `Task.Delay(1)` fallback 을 제거한다.
- close 상태의 bounded drain fallback 은 유지한다.

Red 후보:

- 현재 코드의 UDP wait path 에 notification signal 이 전달되지 않는 shape 를 reflection assertion failure 로 잡는다.
- implementation 이후 focused RIO UDP tests 와 benchmark smoke 를 돌린다.

### Task 3 — scratch benchmark 재측정과 D116 여부 판단

목표:

- `artifacts/benchmarks/rio-udp/2026-06-26/session-03/rio`에 RIO UDP scratch baseline-suite 를 수집한다.
- p99 16.7ms tail 이 사라졌는지, open-loop received 가 3000에 가까워졌는지 확인한다.

판단:

- p99 tail 과 open-loop hard gate 가 같이 개선되면 D116으로 UDP IOCP wait 를 accepted 한다.
- p99는 개선되지만 open-loop delivery loss 가 남으면 receive depth 또는 registration reuse 를 다음 별도 설계로 승격한다.
- p99가 그대로면 IOCP wait 가 실제 hot path 에 적용되지 않았거나 다른 `Task.Delay`/timer wait 가 남은 것이므로 추가 trace 를 먼저 설계한다.

## 검증 기준

- focused `RioTransportUdpTests`.
- focused `Hps.Transport.Rio.Tests`.
- solution build/test.
- RIO UDP smoke.
- RIO UDP `--baseline-suite ... --protocol udp --backend rio --runs 1` scratch artifact.
- summary hard gate 와 warning 은 evidence 로 기록하되, repository baseline 으로 자동 채택하지 않는다.

## 범위 밖

- RIO default backend 승격.
- IPv6 UDP RIO 지원.
- UDP reliability, ordering, congestion control.
- receive depth 2 이상.
- receive payload registration reuse.
- latency hard gate 또는 warning-as-failure 승격.
