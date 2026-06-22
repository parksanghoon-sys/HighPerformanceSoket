# UDP optional lease tracker / sweep owner 설계

- 날짜: 2026-06-22
- 상태: Accepted
- 관련 결정: D060, D061, D067, D068, D072, D073
- 범위: UDP stale remote idle expiry 의 lease tracker 소유 계층, 설정 표면, clock/timer 추상화, sweep 이
  `SubscriptionTable.UnsubscribeAll(IUdpEndpoint, EndPoint)` 를 사용하는 방식을 확정한다.

## 목적

D072는 UDP stale remote cleanup 을 Broker/Server 소유의 선택적 lease cleanup 으로 두기로 정했고,
직후 단위에서 `SubscriptionTable.UnsubscribeAll(IUdpEndpoint endpoint, EndPoint remoteEndPoint)`
remote-wide cleanup primitive 를 추가했다.

이제 그 primitive 위에 올라갈 lease tracker 와 sweep owner 의 경계를 정한다. 이번 설계도 코드를 만들지 않고
다음 구현 단위가 결정해야 할 owner, key, 설정 표면, 시간 추상화, sweep 사용 방식을 못 박는다.

## 현재 확인된 사실

- `SubscriptionTable.UnsubscribeAll(IUdpEndpoint, EndPoint)` 가 특정 local endpoint/remote 조합을 모든 topic 에서
  제거하며, 같은 endpoint 의 다른 remote 와 TCP subscriber 는 보존한다.
- UDP subscriber identity 는 D060에 따라 `(IUdpEndpoint localEndpoint, EndPoint remoteEndPoint)` runtime 조합이다.
  stable subscriber id 는 없다.
- `BrokerUdpDatagramHandler` 는 `SUBSCRIBE`, `UNSUBSCRIBE`, `PUBLISH` datagram 을 같은 socket 에서 처리하며
  malformed datagram 은 endpoint 를 닫지 않고 버린다.
- `BrokerServer` 는 D061에 따라 TCP/UDP ingress 를 독립 시작하고 Transport 수명을 공유하는 얇은 host 다.
- D067/D068에 따라 `BrokerServer` 에 configurable backpressure/QoS, diagnostics pass-through 같은 public
  configuration surface 는 v1에 추가하지 않기로 했다.
- D072에 따라 기본 BrokerServer 동작에서 idle expiry 는 켜지 않으며, topic entry eager cleanup 도 하지 않는다(D008 유지).
- 런타임은 net9.0 이므로 BCL `TimeProvider` 를 시간 소스로 쓸 수 있다.

## 검토한 선택지

### lease owner 위치

- **선택지 A — Broker 소유 + Server 트리거(채택)**: lease table, activity 갱신, sweep 로직을 Broker 계층이
  소유한다. lease 갱신은 `BrokerUdpDatagramHandler` 가 보는 datagram activity 에 의존하고, sweep 은
  `SubscriptionTable.UnsubscribeAll` 를 호출하므로 Broker 계층이 가장 정보가 많다. timer(언제 sweep 을 도느냐)만
  Server host 가 트리거한다. D061의 "Server 가 수명을 소유한다" 와 정보 응집을 동시에 만족한다.
- **선택지 B — Server host 전부 소유**: 단순하지만 Server 가 datagram activity 를 다시 관측해야 하므로
  Broker handler 가 이미 보는 정보를 중복으로 끌어와야 한다.
- **선택지 C — Broker 가 timer 까지 소유**: 응집도는 높지만 host/수명 책임이 Broker 로 내려가 D061과 어긋난다.

### 설정 표면

- **선택지 A — 내부 options 타입, 기본 비활성(채택)**: lease/sweep 옵션을 내부 타입으로 두고 생성자 주입으로만
  받는다. 운영자용 public 설정 API 는 만들지 않는다. D072(기본 비활성)와 D067/D068(public config surface 보류)을
  모두 보존한다.
- **선택지 B — BrokerServer public 설정 API**: 운영자가 바로 켤 수 있지만 D067/D068 보류 결정을 이번 단위에서
  뒤집게 된다.
- **선택지 C — 설정 표면 자체를 범위 밖**: 가장 좁지만 enabled/timeout 을 어디서 받을지 미정인 채로 두면 다음 구현이
  매번 같은 결정을 다시 해야 한다.

### clock / timer 추상화

- **선택지 A — TimeProvider + 순수 sweep 메서드(채택)**: last-seen 비교는 `TimeProvider` 로 주입하고, sweep 은
  현재 시각을 인자로 받는 순수 메서드로 둔다. 실제 timer 는 host 가 `TimeProvider.CreateTimer` 로 별도 소유한다.
  단위 테스트는 fake `TimeProvider` 와 직접 sweep 호출로 idle 판정을 검증할 수 있다.
- **선택지 B — 커스텀 IClock**: BCL `TimeProvider` 와 중복되는 추상화를 새로 만든다.
- **선택지 C — 실제 시계 직접 사용**: idle 판정 단위 테스트가 wall-clock 에 묶여 어려워진다.

## 결정 (D073)

UDP optional lease tracker 와 sweep owner 를 다음과 같이 확정한다.

- lease table, activity 갱신, sweep 로직은 **Broker 계층**이 소유한다. sweep 트리거(timer)는 **Server host** 가 소유한다.
- lease/sweep key 는 `(IUdpEndpoint localEndpoint, EndPoint remoteEndPoint)` 다. stable subscriber identity 를
  도입하지 않는다(D060 유지).
- idle expiry 설정은 **내부 options 타입**으로 두고 생성자 주입으로만 받는다. enabled 기본값은 **false** 다.
  운영자용 public 설정 API 는 이번 단위에서 추가하지 않는다(D067/D068 보존).
- 시간 소스는 **`TimeProvider`** 로 주입한다. sweep 은 현재 시각을 인자로 받는 순수 메서드이며, 실제 주기 timer 는
  host 가 `TimeProvider.CreateTimer` 로 소유한다.
- sweep 은 idle 초과 remote target 별로 `SubscriptionTable.UnsubscribeAll(IUdpEndpoint, EndPoint)` 를 호출한다.
- topic entry eager cleanup 은 계속 하지 않는다(D008 유지).

## Lease 모델

lease table 은 key `(IUdpEndpoint localEndpoint, EndPoint remoteEndPoint)` 에 대해 다음을 보관한다.

- local UDP endpoint reference
- remote endpoint value
- last-seen timestamp (`TimeProvider` 기준)

activity 갱신 규칙(2026-06-19 설계 유지):

- valid `SUBSCRIBE <topic>`: remote lease 를 만들거나 last-seen 을 갱신한다.
- valid `PUBLISH <topic> <payload>`: 이미 lease 가 있는 remote 면 last-seen 을 갱신한다. lease 가 없으면 만들지 않는다.
- valid `UNSUBSCRIBE <topic>`: 남은 topic 이 있으면 last-seen 을 갱신하고, 더 이상 남은 topic 이 없으면 lease 를 제거한다.
- malformed datagram: lease 를 만들거나 갱신하지 않는다.
- endpoint close: 해당 endpoint 의 모든 lease 와 subscription 을 제거한다.

## 설정 options 타입

내부 옵션은 최소 3개 값만 가진다.

- `Enabled` (기본 `false`)
- `IdleTimeout` (idle 초과 판정 기준)
- `SweepInterval` (host timer 주기)

`Enabled == false` 면 lease 갱신과 sweep 을 모두 건너뛰어, 기본 BrokerServer 동작이 v1과 동일하게 유지된다.
public/operator-facing 설정 표면(환경변수, BrokerServer 공개 옵션 등)은 실제 운영 요구가 생긴 뒤 별도 단위에서 설계한다.

## Sweep 정책

`Enabled == true` 일 때 host timer 가 주기적으로 sweep 을 호출하면 sweep 은 다음 순서를 따른다.

1. `TimeProvider` 의 현재 시각과 각 lease 의 last-seen 을 비교해 `IdleTimeout` 을 초과한 remote target 을 찾는다.
2. remote target 별로 `SubscriptionTable.UnsubscribeAll(IUdpEndpoint endpoint, EndPoint remoteEndPoint)` 를 호출한다.
3. 제거 결과를 lease table 에 반영하고 해당 lease 를 제거한다.
4. sweep 도중 같은 remote 가 다시 valid datagram 을 보내면 다음 활동이 새 lease 를 만든다.

sweep 은 payload buffer 소유권과 무관하다. routing target 만 제거하며 pending UDP send queue 에 이미 enqueue 된
datagram 을 취소하지 않는다(기존 send queue 소유권 규칙 유지).

## 다음 최소 구현 단위

다음 구현은 host timer 전체가 아니라 다음 순서로 좁힌다.

1. 내부 `UdpLeaseOptions` 타입(`Enabled`/`IdleTimeout`/`SweepInterval`)을 추가한다.
2. `TimeProvider` 를 주입받는 Broker 계층 lease tracker 를 추가하고 activity 갱신 규칙을 구현한다.
3. 현재 시각을 인자로 받는 순수 sweep 메서드를 추가해, idle 초과 remote 를
   `SubscriptionTable.UnsubscribeAll(IUdpEndpoint, EndPoint)` 로 제거한다.

이 세 단계 뒤에 Server host 가 `TimeProvider.CreateTimer` 로 sweep 을 주기 호출하는 트리거를 별도 단위로 붙인다.

## 범위 밖

- host timer 트리거 구현
- 기본 idle timeout / sweep interval 값 확정
- 운영자용 public 설정 표면(환경변수, BrokerServer 공개 옵션)
- stable subscriber identity 또는 reconnect rebinding
- reliable UDP, retry, 순서 보장
- pending UDP send queue 취소
- topic entry eager cleanup

## 검증 계획

이번 단위는 설계 문서와 상태 문서만 변경한다.

- 실제 `BrokerUdpDatagramHandler`, `SubscriptionTable`, `BrokerServer`, `BrokerSubscriber` 구조와 설계가
  충돌하지 않는지 확인한다.
- D061/D067/D068/D072 와의 정합성을 확인한다(소유 계층, public config 보류, 기본 비활성).
- 문서에 미완성 표기, 내부 모순, scope 누수가 없는지 확인한다.
- `git diff --check` 로 whitespace 오류를 확인한다.
- 코드 변경은 없지만 repository 상태 확인을 위해 solution build/test 를 실행한다.
