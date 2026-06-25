# RIO completion wake latency 개선 설계

## 배경

SAEA/RIO comparison artifact(`artifacts/benchmarks/rio-comparison/2026-06-25/session-01/`)에서
delivery/drop/leak hard gate 는 모두 통과했지만 RIO latency 가 SAEA보다 크게 높았다.

- SAEA load p99: 890.8 us
- SAEA open-loop p99: 872.7 us
- RIO load p99: 16654.0 us
- RIO open-loop p99: 16826.6 us
- RIO closed-loop actual-rate: 64.5 Hz

현재 `RioTransport.WaitForCompletionAsync(...)`는 completion 이 없으면 매번 `Task.Delay(1)`로 대기한다.
Windows timer granularity 에 따라 1 ms delay 가 실제로 약 15.6 ms 근처로 깨어날 수 있고,
이번 RIO p50/p99가 약 15~16 ms로 모이는 현상과 맞다.

## 목표

- RIO opt-in TCP backend 의 completion wake latency 를 먼저 낮춘다.
- request/CQ 수명 규칙과 close/churn 안정성을 깨지 않는다.
- default factory 는 계속 SAEA로 유지한다.
- latency hard gate 는 계속 승격하지 않고 benchmark artifact 로만 관측한다.

## 비목표

- IOCP/RIONotify 기반 completion notification 전체 구현.
- request queue batching, `RIO_MSG_DEFER`, `RIONotify` notification tuning.
- RIO UDP.
- default backend selection.

## 선택지

### A. `Task.Delay(1)`을 `Task.Yield()` polling 으로 대체

completion 이 없으면 timer sleep 대신 scheduler yield 만 수행한다.

장점:

- 변경 범위가 작다.
- Windows timer granularity 로 인한 15 ms급 wake 지연을 바로 제거할 수 있다.
- 기존 CQ dequeue gate, close/churn 테스트 범위를 유지한다.

단점:

- completion 이 오랫동안 없으면 background task 가 계속 깨어 CPU를 더 쓸 수 있다.
- idle connection 이 많아지면 C10K 목표와 충돌할 수 있다.

### B. bounded spin/yield 후 `Task.Delay(1)` fallback

completion 대기 초반에는 빠르게 재시도하고, 일정 횟수 이상 비어 있으면 기존 timer delay 로 후퇴한다.

장점:

- hot path latency 를 줄이면서 idle CPU burn 을 제한한다.
- close/churn 수명 구조를 크게 바꾸지 않는다.
- RIO Task 6/6 hardening 의 작은 opt-in backend 단위와 잘 맞는다.

단점:

- fallback 이후에는 여전히 timer granularity 영향이 남는다.
- 높은 connection 수에서 적절한 spin/yield budget 은 후속 tuning 이 필요하다.

### C. IOCP/RIONotify 기반 completion wait

RIO completion queue notification 을 IOCP와 연결하고, completion 이 생기면 wait handle 로 pump 를 깨운다.

장점:

- idle CPU와 wake latency를 둘 다 제대로 다룰 수 있는 방향이다.
- 장기적으로 RIO backend 다운 구조다.

단점:

- `RioCompletionQueue`, `RioConnectionResource`, pump ownership, close-drain 순서를 다시 설계해야 한다.
- 현재 확인된 병목을 고치는 최소 단위보다 크다.
- close/churn native 수명 회귀 위험이 높다.

## 결정

이번 구현은 **B. bounded spin/yield 후 `Task.Delay(1)` fallback** 으로 진행한다.

구체적으로 `WaitForCompletionAsync(...)`는 completion 이 없을 때 다음 순서로 대기한다.

1. 매 loop 시작에서 close/resource disposed 를 확인한다.
2. `RIODequeueCompletion`을 시도한다.
3. completion 이 있으면 즉시 반환한다.
4. 비어 있으면 작은 counter 를 증가시킨다.
5. counter 가 `FastCompletionPollYieldCount` 이하이면 `await Task.Yield()`로 timer sleep 없이 재시도한다.
6. counter 를 초과하면 `Task.Delay(CompletionPollDelayMilliseconds)`로 후퇴해 idle CPU를 제한한다.

초기 값:

- `FastCompletionPollYieldCount = 256`
- `CompletionPollDelayMilliseconds = 1` 유지

이 값은 RIO opt-in backend 의 첫 latency hardening 기본값이다.
추후 connection 수/CPU 사용량 evidence 가 생기면 조정한다.

## 테스트 계획

- 기존 RIO TCP focused tests 전체.
- 기존 repeated close/churn stress 10회 반복.
- SAEA/RIO smoke CLI 재실행.
- RIO load/open-loop benchmark artifact 재수집.

기대:

- RIO delivery/drop/leak hard gate 는 계속 pass.
- RIO p99는 16 ms대에서 의미 있게 낮아져야 한다.
- 만약 p99가 낮아지지 않으면 completion wake 외 원인(send/receive pump 직렬화, frame path, per-operation buffer registration)을 다음 병목 후보로 승격한다.

## 후속

- IOCP/RIONotify completion wait 는 P1_SOON 후속으로 유지한다.
- per-operation `RIORegisterBuffer` 비용도 benchmark 개선 후 재평가한다.
- RIO repository baseline 채택 구조는 별도 설계가 필요하다.
