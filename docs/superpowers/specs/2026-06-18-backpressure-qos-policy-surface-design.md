# backpressure/QoS policy surface 필요성 판단

- 날짜: 2026-06-18
- 상태: Accepted
- 관련 결정: D012, D039, D040, D041, D042, D056, D062, D064, D066
- 범위: v1 transport send queue 정책 표면을 확장할지 판단하는 설계 결정. production code 변경은 포함하지 않는다.

## 목적

D064로 v1 TCP/UDP transport send queue 기본 정책은 bounded drop-oldest 로 확정됐다. D066으로 실제 stalled TCP subscriber 경로에서
drop-oldest 와 HWM 16 포화가 관측됨도 확인됐다.

이번 문서는 그 다음 질문을 닫는다. 지금 `BackpressurePolicy`, endpoint/topic 별 QoS, disconnect/reject 정책, pending capacity 설정을
public API 로 추가해야 하는가?

결론은 **v1에서는 추가하지 않는다**이다. 현재 구현은 외부 최신 상태를 endpoint 로 배포하는 Interface Server 기준선이며,
drop-oldest 손실은 diagnostics 로 관측된다. 다른 정책은 메시지 의미와 control-plane semantics 를 함께 요구하므로 별도 후속으로 남긴다.

## 현재 확인된 사실

- TCP `TransportConnection`과 UDP `SaeaUdpEndpoint`는 capacity 16 pending queue 를 가진다.
- queue 가 가득 찬 상태에서 새 send 를 수락하면 가장 오래된 pending item 을 evict 하고, evict 된 `RefCountedBuffer` transport ref 를 정확히 1회 Release 한다.
- `ITransportDiagnostics.GetDiagnosticsSnapshot()`은 TCP/UDP 누적 drop count 와 pending send queue high-watermark 를 제공한다.
- active endpoint 별 snapshot 은 `ITransportEndpointDiagnostics.GetEndpointSnapshots()`로 읽을 수 있다.
- D066 stalled subscriber stress 는 실제 TCP socket 경로에서 drop count > 0, HWM 16, 종료 후 pool leak 0을 검증했다.
- UDP broker v1은 runtime endpoint 수명과 datagram self-command 기반이며, reliable UDP, durable history, reconnect rebinding 은 v1 밖이다.

## 검토한 접근

### A. v1은 fixed bounded drop-oldest 유지

현재 정책을 public 설정 없이 유지한다. diagnostics 로 손실과 queue 포화를 관측하고, 정책 확장은 실제 요구가 구체화될 때 설계한다.

장점:
- 현재 구현, 테스트, D064/D066과 정확히 일치한다.
- hot path API와 host 설정 표면을 넓히지 않는다.
- reliable/durable semantics 를 암묵적으로 약속하지 않는다.
- TCP/UDP send queue release 규율과 테스트 matrix 를 더 늘리지 않는다.

단점:
- command 또는 누락 불가 event 처럼 drop 이 허용되지 않는 데이터에는 v1 기본 정책이 맞지 않을 수 있다.
- queue capacity 16은 아직 workload별 설정값이 아니라 구현 기준값이다.

### B. transport-wide 설정만 추가

예: `BackpressurePolicy.DropOldest`, `BackpressurePolicy.Disconnect`, `pendingSendCapacity`를 transport 또는 server option 으로 둔다.

장점:
- API 폭은 endpoint/topic 별 QoS보다 작다.
- 같은 process 전체 정책을 바꾸는 요구에는 대응할 수 있다.

단점:
- disconnect/reject 는 subscription cleanup, reconnect, publisher 관측, protocol error/ack semantics 를 함께 요구한다.
- capacity 변경은 memory bound, latency, drop count 해석, benchmark 기준을 다시 정의해야 한다.
- UDP endpoint 는 connection close 의미가 TCP와 다르므로 같은 enum 으로 묶으면 이름만 같고 동작이 달라질 수 있다.

### C. endpoint/topic 별 QoS 정책 추가

topic 또는 endpoint마다 최신성 우선, 신뢰성 우선, queue capacity, drop/reject/disconnect 를 선택한다.

장점:
- DDS 유사 시스템의 장기 방향과 가장 잘 맞는다.
- 상태성 데이터와 command/event 데이터를 서로 다른 정책으로 다룰 수 있다.

단점:
- stable endpoint identity, subscription model, host configuration, policy inheritance, reconnect semantics 가 먼저 필요하다.
- reliable/durable delivery 는 storage/history/ack/retry 와 연결된다.
- 현재 v1 runtime endpoint 모델 위에 얹으면 정책 key 가 connection 수명에 묶여 재연결 시 의미가 불안정하다.

## 결정

v1에서는 **A. fixed bounded drop-oldest 유지**를 채택한다.

- TCP/UDP transport send queue 기본 정책은 계속 capacity 16 bounded drop-oldest 이다.
- public `BackpressurePolicy` enum, pending capacity option, topic/endpoint 별 QoS surface 는 추가하지 않는다.
- disconnect/reject, reliable/durable delivery, per-topic/per-endpoint QoS 는 별도 후속 설계로 남긴다.
- drop 발생과 queue 포화는 기존 diagnostics snapshot 으로 관측한다.

## 이유

현재 Interface Server v1의 주된 데이터 성격은 외부 상태를 구독 endpoint 로 배포하는 최신성 우선 흐름이다.
이 기준에서는 느린 subscriber 때문에 전체 broker memory 가 무한 증가하는 것보다 오래된 pending message 를 버리고 최신 publish 를 유지하는 편이 안전하다.

반대로 disconnect/reject/reliable 정책은 단순한 queue 처리 방식이 아니다. 연결을 끊으면 구독 정리와 재구독 요구가 생기고,
reject 는 publisher 에 실패를 어떤 방식으로 돌려줄지 정해야 하며, reliable/durable 은 history 저장과 ack/retry를 요구한다.
이들은 현재 v1 범위의 transport queue 설정을 넘어서는 protocol/control-plane 결정이다.

## 후속 승격 조건

다음 중 하나가 실제 요구로 확인되면 별도 설계 단위로 승격한다.

- 특정 topic 이 command/event log 이며 단일 message 손실도 허용할 수 없다.
- endpoint 별로 최신성 우선과 신뢰성 우선 정책을 동시에 운영해야 한다.
- pending capacity 16이 특정 workload 에서 과도하게 작거나 크다는 반복 benchmark 근거가 생긴다.
- reconnect 후 subscription 유지 또는 stable endpoint identity 가 도입된다.
- UDP reliable delivery 또는 durable history 가 v1 범위를 넘어 후속 scope 로 승인된다.

## 영향

- production code 변경 없음.
- `ITransport`, `IConnection`, `IUdpEndpoint`, `BrokerServer` public API 변경 없음.
- `TransportDiagnosticsSnapshot`과 `EndpointSnapshot`은 현재 필드를 유지한다.
- `TODOS.md`의 configurable backpressure/QoS 항목은 완료로 이동하고, 더 구체적인 요구가 생길 때 새 항목으로 재등록한다.

## 검증

문서 결정 단위이므로 build/test 대신 다음을 검증한다.

- D064/D066과 충돌하지 않는다.
- v1 밖 항목과 후속 승격 조건이 분리돼 있다.
- `PLAN.md`, `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`의 정책 설명이 같은 방향을 가리킨다.
- `git diff --check`로 whitespace 오류가 없음을 확인한다.
