# Endpoint Identity Policy

- 날짜: 2026-06-16
- 상태: Accepted
- 근거 리뷰: `.claude/review/2026-06-16-endpoint-model-cross-verification.md`
- 관련 결정: D053, D054, D056, D057, D058, D059

## 결론

`EndpointId`는 Transport 가 실행 중에 살아 있는 TCP connection 또는 UDP endpoint 를 관측하기 위해 발급하는
transient diagnostics id 이다. `EndpointId`는 reconnect 이후 같은 외부 endpoint 를 보장하는 stable routing id 가 아니다.

Broker routing 이 reconnect 를 같은 endpoint 로 재바인딩해야 한다면, 그 identity 는 socket handle 이나 `EndpointId`에서
추론하지 않고 별도 control-plane 또는 설정에서 명시적으로 받아야 한다.

## v1 subscription 정책

v1은 reconnect 후 subscription 유지까지 제공하지 않는다. Subscription 은 현재 살아 있는 runtime send target 의 수명에
묶인다.

- TCP subscription 은 현재 TCP `IConnection` 수명에 묶인다.
- TCP connection 이 닫히면 기존처럼 모든 topic 에서 해당 connection subscription 을 제거한다.
- TCP client 가 reconnect 하면 새 connection 으로 다시 `SUBSCRIBE` 해야 한다.
- UDP subscription 을 v1에 포함하더라도 stable subscriber identity 없이 bind 된 UDP endpoint 와 remote endpoint 조합을
  runtime send target 으로 다룬다.
- UDP remote 의 stale/expiry, explicit unsubscribe, remote endpoint 등록 방식은 UDP broker v1 wire/control 설계에서 별도로 닫는다.

이 결정은 stable identity 를 폐기하는 것이 아니라, stable identity 를 요구하는 기능을 v1 runtime routing 뒤의 별도 단계로
분리한다는 뜻이다.

## 왜 `EndpointId`를 routing key 로 쓰지 않는가

현재 `EndpointId`는 `TransportBase.CreateEndpointId()`에서 transport 수명 안의 증가값으로 발급된다.
이 값은 다음 성질을 가진다.

- 동일 실행 중 endpoint snapshot 을 구분하기 위한 값이다.
- TCP reconnect 또는 UDP remote 재등장을 같은 logical subscriber 로 묶지 않는다.
- process restart 뒤에도 유지되지 않는다.
- peer IP/port, protocol-level name, topic, data type id 같은 외부 의미를 담지 않는다.

따라서 `SubscriptionTable`이 `EndpointId`를 key 로 사용하면 이름은 endpoint 중심처럼 보이지만 실제 reconnect semantics 는
해결되지 않는다. reconnect 때 새 `EndpointId`가 발급되므로 이전 subscription 을 자동으로 이어받지 못하고, 반대로 이를
이어받는다고 주장하려면 어떤 외부 identity 가 같은지를 판단할 근거가 없다.

## Broker routing 의 현재 의미

현재 `BrokerSubscriber`는 TCP `IConnection` send target 을 감싸는 runtime target 값이다.

- 같은 TCP connection reference 는 같은 subscriber 로 본다.
- connection 이 닫히면 `UnsubscribeAll(connection)`으로 routing table 에서 제거한다.
- reconnect 는 새 connection 이므로 새 subscriber 이다.
- UDP target 은 아직 구현하지 않았다.

이 모델은 v1 TCP broker 동작에는 충분하지만, stable external endpoint 재바인딩을 제공하지 않는다.

## stable routing identity 가 필요한 경우

다음 요구사항 중 하나를 v1 또는 후속 버전에 넣으려면 `EndpointId`가 아니라 별도 identity 모델이 필요하다.

- reconnect 뒤 기존 subscription 유지
- 동일 장비 또는 동일 data consumer 를 TCP/UDP transport 변경과 무관하게 같은 endpoint 로 취급
- operator 화면에서 사람이 이해하는 endpoint name 으로 drop/backlog 를 추적
- DDS 유사 discovery 또는 configured endpoint registry

가능한 identity source 는 별도 설계에서 결정한다.

- protocol handshake: 예를 들어 `REGISTER <endpoint-name>` 또는 `SUBSCRIBE <topic> AS <endpoint-name>`
- configuration: server 가 미리 알고 있는 endpoint name 과 allowed remote 를 매핑
- external adapter binding: source adapter 또는 host 가 endpoint identity 를 Broker 에 직접 제공

## 다음 구현 게이트

다음 코드 단위에서 stable endpoint routing 을 구현하려면 먼저 아래 질문을 닫아야 한다.

1. v1 이 reconnect subscription 유지까지 요구하는가, 아니면 runtime connection/UDP endpoint subscription 만 보장하는가.
2. stable identity 를 wire protocol 에서 받을지, server configuration 에서 받을지, host API 에서 주입할지.
3. TCP 와 UDP 가 같은 identity namespace 를 공유해야 하는가.
4. 동일 stable identity 로 새 connection 이 들어왔을 때 기존 connection 의 subscription 을 이전할지, 중복 endpoint 로 거부할지.

이 질문이 닫히기 전에는 `EndpointId`를 `SubscriptionTable`의 stable key 로 승격하지 않는다.

## 비범위

- 인증, 권한, TLS
- DDS discovery, durable history, reliable UDP
- reconnect 중 미전송 메시지 보존
- endpoint 별 QoS 정책
