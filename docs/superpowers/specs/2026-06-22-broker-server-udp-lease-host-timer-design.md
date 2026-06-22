# BrokerServer UDP lease host timer 설계

- 날짜: 2026-06-22
- 상태: Accepted
- 관련 결정: D060, D061, D067, D068, D072, D073, D074
- 범위: 이미 구현된 UDP lease tracker/sweep 을 `BrokerServer` host 수명에 연결하는 public 설정 표면과 timer wiring 을 정한다.

## 목적

D073의 core 구현은 Broker 계층 내부에 `UdpLeaseOptions`, `UdpRemoteLeaseTracker`,
`BrokerUdpDatagramHandler.SweepExpiredUdpLeases(DateTimeOffset)`를 추가했다.
남은 문제는 Server host 가 언제, 어떤 설정으로 sweep 을 주기 실행할지다.

이번 설계의 목표는 기본 BrokerServer 동작을 바꾸지 않으면서, 운영자가 명시적으로 UDP remote idle expiry 를 켰을 때만
host timer 가 sweep 을 트리거하게 만드는 것이다.

## 확인된 사실

- `BrokerServer`는 `Hps.Server`에 있고 `Hps.Broker`를 참조한다.
- `UdpLeaseOptions`와 `BrokerUdpDatagramHandler`의 options constructor 는 `Hps.Broker` 내부 API다.
- 기존 public `BrokerServer(ITransport, PinnedBlockMemoryPool, int)` 생성자는 disabled lease 동작을 유지해야 한다.
- D073은 "기본 idle expiry 비활성"과 "host timer 는 Server 소유"를 이미 결정했다.
- 기본 idle timeout/sweep interval 값은 아직 운영 요구로 확정되지 않았다.

## 선택지

### 선택지 A - Broker public options 노출

`UdpLeaseOptions`를 public 으로 바꾸고 Server 가 그대로 사용한다.
구현은 단순하지만 Broker 내부 tracker 설정 타입이 public API가 되어 이후 변경 비용이 커진다.

### 선택지 B - Server public options + Broker friend assembly

`Hps.Server`에 public `BrokerServerOptions`를 추가하고, `Hps.Broker`는 `InternalsVisibleTo("Hps.Server")`로
Server host 에게만 내부 lease wiring 을 허용한다. Broker public API는 늘리지 않는다.

단점은 friend access 범위가 넓어지는 것이다. 다만 같은 제품 내부 계층이며, Server 가 Broker orchestration host 라는
D061 책임과 맞는다.

### 선택지 C - Server public options + Broker public factory

Broker public API에 lease-enabled handler factory 를 추가한다.
friend assembly 보다 public 표면은 명시적이지만, 외부 소비자가 handler lease 설정을 직접 만지는 경로가 생긴다.

## 결정

선택지 B를 채택한다.

- `Hps.Server`에 public sealed `BrokerServerOptions`를 추가한다.
- `BrokerServerOptions.Default`는 UDP lease sweep disabled 다.
- 활성화는 `BrokerServerOptions.CreateWithUdpLeaseSweep(TimeSpan idleTimeout, TimeSpan sweepInterval, TimeProvider? timeProvider)`로만 한다.
- idle timeout 과 sweep interval 은 활성화 호출자가 반드시 명시한다. 이번 단위에서 임의 기본 시간값은 정하지 않는다.
- `timeProvider`가 null 이면 `TimeProvider.System`을 사용한다.
- `Hps.Broker`는 `InternalsVisibleTo("Hps.Server")`를 추가해 Server host 가 내부 `UdpLeaseOptions`와 handler constructor 를 사용할 수 있게 한다.
- 기존 `BrokerServer` public 생성자는 `BrokerServerOptions.Default`로 위임해 기존 동작을 유지한다.

## Timer 수명

- timer 는 UDP endpoint bind 가 성공한 뒤, options enabled 일 때만 만든다.
- timer dueTime 과 period 는 모두 `SweepInterval`이다. 즉 시작 직후 즉시 sweep 하지 않고 첫 interval 이후부터 실행한다.
- timer callback 은 `timeProvider.GetUtcNow()`를 읽고 `_brokerDatagramHandler.SweepExpiredUdpLeases(now)`를 호출한다.
- Stop/Dispose 는 timer 를 먼저 분리하고 dispose 한 뒤 UDP endpoint 와 transport 를 닫는다.
- StartUdpAsync 실패 경로에서 timer 가 만들어졌다면 dispose 해야 한다.
- timer callback 은 payload buffer 소유권을 건드리지 않는다. routing table cleanup 만 수행한다.

## 테스트 계획

- `BrokerServerOptionsTests`
  - default 가 disabled 이고 interval 값이 zero 인지 검증한다.
  - enabled factory 가 0 이하 timeout/interval 을 거부하는지 검증한다.
  - enabled factory 가 명시 값과 time provider 를 저장하는지 검증한다.
- `BrokerServerTests`
  - enabled options 로 `StartUdpAsync`를 호출하면 bind 성공 뒤 timer 가 생성되고 dueTime/period 가 sweep interval 인지 검증한다.
  - subscribe 후 timeout 을 넘기고 manual timer 를 fire 하면 remote subscription 이 제거되는지 검증한다.
  - `StopAsync`가 생성된 timer 를 dispose 하는지 검증한다.
  - default options 에서는 timer 가 생성되지 않는지 검증한다.

## 범위 밖

- 환경 변수, 설정 파일, CLI sample 의 실제 설정 연결
- 운영 metrics/logging
- timeout 기본값 정책
- stable subscriber identity 와 reconnect rebinding
- reliable UDP, retry, 순서 보장
