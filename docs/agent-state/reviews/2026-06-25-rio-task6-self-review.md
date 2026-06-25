# RIO Task 6 구현 self-review

## Scope

- 대상: `RioTransport` TCP listen/connect/accept/receive/send pump, `RioConnectionListener`, RIO loopback test, D099.
- 목적: Task 6 구현이 SAEA 기준선의 transport 계약과 충돌하지 않는지 확인하고, factory default 승격 전에 필요한 hardening 후보를 분리한다.
- 범위 밖: UDP RIO, batching, IOCP notification, automatic default backend selection, latency gate.

## Findings

### F1. RIO send completion partial byte 처리 미흡

- Severity: Major
- Dimension: correctness / reliability
- Evidence: `RioTransport.SendRegisteredArrayAsync(...)`는 `completion.BytesTransferred != 0`만 확인하고 요청 길이보다 작은 completion 을 성공으로 취급한다. SAEA 기준선은 `SendInFlightAsync(...)`에서 `remaining`이 0이 될 때까지 반복 전송한다.
- Impact: RIO provider 가 TCP stream send 를 partial completion 으로 반환하면 payload tail 이 전송되지 않았는데 in-flight ref 가 release 된다. Broker fan-out 에서는 subscriber 가 잘린 frame 을 받을 수 있다.
- Recommendation: RIO send path 도 `offset += completedBytes`, `remaining -= completedBytes` 루프로 바꾸고, 0 byte 또는 error status 는 connection close 로 수렴시킨다. `PrependLengthPrefix` header 와 payload 모두 같은 helper 를 사용해야 한다.

### F2. close 시 outstanding RIO request completion drain 모델이 아직 명시적이지 않음

- Severity: Major
- Dimension: reliability / operability
- Evidence: 전체 테스트 1차 실행에서 CQ close 와 background dequeue 경합이 `RIODequeueCompletion` access violation 으로 드러나 resource gate 로 보정했다. 현재는 close/dequeue 동시 접근은 막지만, close 시점에 이미 post 된 receive/send request 의 completion 을 어디까지 drain 해야 하는지는 코드 구조상 명시적 owner 로 분리되어 있지 않다.
- Impact: 현재 테스트에서는 안정화됐지만, 높은 churn 이나 close/send/receive 경합에서 buffer deregistration, CQ close, socket dispose 순서가 다시 native 수명 문제로 드러날 수 있다.
- Recommendation: connection resource 에 outstanding receive/send state 를 명시하고, post 된 request 는 completion 관측 후 buffer deregister/queue close 로 수렴시키는 hardening 단위를 별도로 둔다. close 는 socket shutdown 신호를 먼저 만들고, CQ close 는 pump exit 이후로 지연하는 구조가 더 안전하다.

### F3. RIO transport contract coverage 가 아직 최소 loopback 에 머문다

- Severity: Minor
- Dimension: testing
- Evidence: `RioTransportTcpTests`는 raw payload `TrySend` loopback 만 검증한다. `TransportSendBuffer.WithLengthPrefix()`, close churn, larger payload, send queue drop-oldest contract 는 RIO 전용 테스트로 아직 고정하지 않았다.
- Impact: 실제 BrokerServer opt-in 연결 전 regression 감지가 약하다.
- Recommendation: 다음 hardening 뒤 RIO 전용 contract tests 를 추가한다. 최소 후보는 length-prefixed send, repeated close loopback, pending queue ownership/drop-oldest 관측이다.

## Material failure modes

- Trigger: RIO send completion 이 요청 길이보다 작은 byte count 로 완료된다.
  Impact: payload tail 누락, TCP frame corruption.
  Detection: length-prefixed 또는 큰 payload loopback에서 payload mismatch.
  Mitigation: completion byte count 기반 remaining loop.

- Trigger: connection close 가 outstanding RIO receive/send request 와 경합한다.
  Impact: native access violation, buffer deregistration 순서 위반, background pump fault.
  Detection: repeated close/churn stress, full solution test 중 testhost crash.
  Mitigation: outstanding request owner 와 deferred CQ close.

## Deferred items

- IOCP/event notification 기반 CQ wait 로 polling delay 제거.
- IPv6/local endpoint family 대응. 현재 `RioNative.CreateTcpSocket()`은 IPv4 registered socket 만 만든다.
- RIO UDP endpoint, batching, default backend selection.

## Completion summary

- Task 6 구현은 opt-in RIO TCP loopback 최소 계약을 만족한다.
- 즉시 확인된 crash class(CQ close/dequeue 경합)는 Task 6 중 보정됐다.
- 다음 구현 후보는 RIO send partial completion loop 와 outstanding request close-drain 모델이다.
