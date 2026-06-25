# RIO TCP Pump Hardening Design

## 상태

Accepted — 2026-06-25

## 배경

Task 6에서 `RioTransport`는 opt-in TCP listen/connect/accept/receive/send loopback 을 만족했다.
이후 self-review(`docs/agent-state/reviews/2026-06-25-rio-task6-self-review.md`)에서 두 가지 hardening 필요성이 남았다.

1. RIO send completion 이 요청 길이보다 작은 byte count 로 끝날 때 remaining payload 를 계속 보내야 한다.
2. close 시점에 이미 post 된 RIO receive/send request 의 completion, buffer deregistration, CQ close 순서를 더 명시해야 한다.

## 목표

RIO TCP pump 를 SAEA 기준선의 send/close 수명 계약에 더 가깝게 보강한다.
이번 hardening 은 여전히 opt-in RIO backend 내부 변경이며, 기본 `TransportFactory.CreateDefault()`는 SAEA를 유지한다.

## 설계

### 1. Send completion byte count loop

`SendRegisteredArrayAsync(...)`는 한 번의 `RIOSend` completion 을 전체 send 성공으로 간주하지 않는다.
요청한 `length`가 모두 전송될 때까지 다음 규칙으로 반복한다.

- `remaining == 0`이면 성공.
- `completion.Status != 0`이면 socket error 로 connection close 경로에 수렴.
- `completion.BytesTransferred == 0`이면 connection reset 으로 취급.
- `completion.BytesTransferred > remaining`이면 provider 계약 위반으로 close 경로에 수렴.
- 그 외에는 `offset += completed`, `remaining -= completed` 후 같은 registered buffer id 로 다음 `RIOSend`를 post 한다.

length prefix header 와 payload 는 같은 loop helper 를 사용한다.
payload buffer ref 는 기존 `InFlightSend`가 소유하므로, completion loop 가 끝나거나 예외로 unwind 될 때 기존 `using(inFlight)` 규칙이 ref 를 반환한다.

### 2. Close/drain owner

이번 hardening 의 최소 변경은 native handle crash 방지와 buffer deregistration 순서 명확화다.
`RioConnectionResource`는 CQ close 와 dequeue 를 계속 같은 gate 로 직렬화한다.
추가로 send/receive operation helper 는 buffer id 를 등록한 뒤 completion 관측 전에는 deregister 하지 않는다는 규칙을 코드 주석과 helper 경계로 명시한다.

완전한 drain owner(예: active operation ref count, pump exit 후 CQ close)는 다음 조건 중 하나가 확인되면 별도 구현으로 승격한다.

- repeated close/churn stress 에서 flake 또는 native crash 가 재현된다.
- RIO provider 가 socket dispose 후 completion 을 안정적으로 반환하지 않는 환경이 확인된다.
- RIO backend 를 default factory 후보로 올리기 전 운영 수준 close semantics 검증 단계에 진입한다.

즉, 이번 단위에서는 확인된 crash class(CQ close/dequeue 동시 접근)는 이미 고정된 상태를 유지하고,
send partial correctness 를 우선 코드로 닫는다. 더 큰 close-drain 재구조화는 증거 없이 확대하지 않는다.

### 3. Test coverage

테스트는 RIO available Windows 에서만 live assertion 을 수행한다.

- send partial loop 자체는 provider 가 partial completion 을 쉽게 강제하지 않으므로 direct Red가 어렵다.
  대신 larger payload loopback 과 length-prefixed loopback 을 추가해 기존 raw 2-byte smoke 보다 send path coverage 를 넓힌다.
- repeated RIO focused test 는 verification command 로 유지한다.
- native close/churn stress 는 flake 가 확인되면 별도 Red로 승격한다.

## 범위 밖

- IOCP/event notification 기반 CQ wait.
- full close-drain owner 재구조화.
- RIO UDP.
- IPv6 registered socket factory.
- default backend selection.

## 검증

- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`
- focused RIO tests 반복 실행
- `dotnet build HighPerformanceSocket.slnx --no-restore`
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`
- `git diff --check`

## Self-review

- Placeholder 없음.
- 설계는 Task 6 self-review F1/F2/F3와 연결된다.
- 이번 구현 범위는 send partial correctness 와 contract coverage 보강으로 제한한다.
- full close-drain owner 는 증거 기반 후속으로 둔다.
