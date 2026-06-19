# UDP stale remote idle expiry 설계

- 날짜: 2026-06-19
- 상태: Accepted
- 관련 결정: D060, D072
- 범위: UDP runtime subscriber target 이 endpoint 수명보다 오래 남는 문제를 어떻게 정리할지 결정한다.

## 목적

UDP broker v1은 remote 를 stable identity 로 등록하지 않고, datagram 을 보낸 순간의
`(IUdpEndpoint localEndpoint, EndPoint remoteEndPoint)` 조합을 runtime subscriber target 으로 사용한다.
이 모델은 단순하고 fan-out 경로에 잘 맞지만, remote process 가 `UNSUBSCRIBE` 없이 종료되면 해당 remote subscription 이
topic table 에 계속 남을 수 있다.

이번 설계는 UDP stale remote 정리의 owner, key, sweep 정책, 다음 최소 구현 단위를 정한다.

## 현재 확인된 사실

- `BrokerUdpDatagramHandler`는 `SUBSCRIBE`, `UNSUBSCRIBE`, `PUBLISH` datagram 을 같은 UDP socket 에서 처리한다.
- malformed UDP command 는 shared endpoint 를 닫지 않고 datagram 만 버린다.
- endpoint close notification 은 `SubscriptionTable.UnsubscribeAll(IUdpEndpoint)`로 같은 local endpoint 에 묶인 모든
  remote subscription 을 제거한다.
- 개별 remote 를 모든 topic 에서 제거하는 API 는 아직 없다.
- `SubscriptionTable`은 D008에 따라 비어 있는 topic entry 를 즉시 제거하지 않는다.
- UDP에는 TCP connection close 처럼 remote 별 lifecycle event 가 없다.

## 검토한 선택지

### 선택지 A: Transport 계층에서 remote idle expiry 를 처리

부적합하다. Transport 는 datagram 을 주고받는 socket 계층이며 어떤 datagram 이 subscription activity 인지 알지 못한다.
Transport 가 remote idle 을 지우려면 Broker routing table 을 알아야 하므로 계층 방향이 깨진다.

### 선택지 B: Broker handler 가 모든 datagram 수신 때 즉시 timer 를 관리

가능하지만 첫 구현 단위로는 범위가 크다. last-seen map, clock abstraction, sweep timer, 설정값, close race 를 한 번에
넣어야 한다. 특히 timeout 기본값을 잘못 고르면 정상적으로 조용한 subscriber 를 끊을 수 있다.

### 선택지 C: Broker/Server 소유의 선택적 lease cleanup 으로 설계하고, 기본값은 비활성

권장 방향이다. Transport 경계를 유지하고, v1의 explicit `UNSUBSCRIBE`와 endpoint close cleanup 의미를 보존한다.
실제 expiry 는 운영자가 idle timeout 을 명시한 경우에만 켠다. 다음 구현은 timer 보다 먼저 remote-wide unsubscribe
primitive 를 작게 추가한다.

## 결정

UDP stale remote cleanup 은 Transport 가 아니라 Broker/Server 책임이다.

- cleanup key 는 `(IUdpEndpoint localEndpoint, EndPoint remoteEndPoint)`다.
- `EndpointId` 또는 stable subscriber identity 를 사용하지 않는다.
- 기본 BrokerServer 동작에서는 idle expiry 를 켜지 않는다.
- idle expiry 는 명시적 설정이 생긴 뒤에만 활성화한다.
- expiry 가 활성화되면 sweep 은 해당 remote target 을 모든 topic 에서 제거한다.
- topic entry eager cleanup 은 계속 하지 않는다(D008 유지).

## Runtime lease 모델

추후 idle expiry 를 구현할 때 lease table 은 Broker 계층 또는 Server host 계층이 소유한다.

권장 상태:

- local UDP endpoint reference
- remote endpoint value
- last-seen timestamp
- subscribed topic count 또는 remote-wide subscription 존재 여부

activity 갱신 규칙:

- valid `SUBSCRIBE <topic>`: remote lease 를 만들거나 갱신한다.
- valid `PUBLISH <topic> <payload>`: 이미 lease 가 있는 remote 라면 last-seen 을 갱신한다.
- valid `UNSUBSCRIBE <topic>`: 해당 remote 가 다른 topic 에 남아 있으면 last-seen 을 갱신하고, 더 이상 남은 topic 이 없으면
  lease 를 제거한다.
- malformed datagram: lease 를 만들거나 갱신하지 않는다.
- endpoint close: 해당 endpoint 의 모든 lease 와 subscription 을 제거한다.

이 규칙은 malformed traffic 이 stale remote 를 계속 살려 두는 문제를 막고, publisher-only remote 가 subscription lease 를
불필요하게 만들지 않도록 하기 위한 것이다.

## Sweep 정책

expiry 가 활성화된 경우 sweep 은 다음 순서를 따른다.

1. 현재 시각과 last-seen 을 비교해 idle timeout 을 초과한 remote target 을 찾는다.
2. remote target 별로 `SubscriptionTable.UnsubscribeAll(IUdpEndpoint endpoint, EndPoint remoteEndPoint)`를 호출한다.
3. 제거된 subscription 수를 lease table 에 반영한다.
4. sweep 중 새 datagram 이 도착해 같은 remote 가 다시 subscribe 하면, 다음 subscribe 가 새 lease 를 만든다.

sweep 은 payload buffer 소유권과 무관해야 한다. subscription table 에서 routing target 만 제거하며, pending UDP send queue 의
이미 enqueue 된 datagram 을 취소하지 않는다. 이미 enqueue 된 send 는 기존 send queue 소유권 규칙에 따라 완료되거나 drop 된다.

## 다음 최소 구현 단위

다음 구현은 timer 를 만들지 않고 `SubscriptionTable`의 remote-wide cleanup primitive 만 추가한다.

목표:

- `UnsubscribeAll(IUdpEndpoint endpoint, EndPoint remoteEndPoint)`를 추가한다.
- 같은 local endpoint 의 특정 remote subscription 만 모든 topic 에서 제거한다.
- 같은 endpoint 의 다른 remote 와 TCP subscriber 는 보존한다.
- D008에 따라 빈 topic entry 는 제거하지 않는다.

이 primitive 가 생기면 그 다음 단위에서 optional lease tracker 와 sweep owner 를 작게 설계/구현할 수 있다.

## 범위 밖

- 기본 idle timeout 값 확정
- sweep timer 구현
- BrokerServer public configuration API 추가
- stable subscriber identity 또는 reconnect rebinding
- reliable UDP, retry, 순서 보장
- pending UDP send queue 취소
- topic entry eager cleanup

## 검증 계획

이번 단위는 설계 문서와 상태 문서만 변경한다.

- 실제 `BrokerUdpDatagramHandler`, `SubscriptionTable`, `BrokerSubscriber` 구조와 설계가 충돌하지 않는지 확인한다.
- 문서에 미완성 표기, 내부 모순, scope 누수가 없는지 확인한다.
- `git diff --check`로 whitespace 오류를 확인한다.
- 코드 변경은 없지만 repository 상태 확인을 위해 solution build/test 를 실행한다.
