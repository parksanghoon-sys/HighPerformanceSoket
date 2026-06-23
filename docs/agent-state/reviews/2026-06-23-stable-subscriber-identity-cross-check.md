# Stable subscriber identity 구현 교차검증

- 날짜: 2026-06-23
- 검토자: Codex
- 기준 HEAD: `22972be` (`test: cover stable udp subscriber rebind loopback`)
- 범위: D075/D076 stable subscriber identity 설계, Protocol/Broker/Server 구현, 관련 단위/loopback 테스트
- 판정: 구현 방향은 타당하지만, UDP 경계에 must-fix 2건이 남아 있다.

## 1. Scope

이번 검토는 `REGISTER <subscriber-id>` 기반 opt-in stable subscriber identity 구현이 설계와 실제 코드에서 같은 의미를 갖는지 확인했다.

검토한 핵심 범위:

- Protocol: `TcpCommandKind`, `TcpCommandDecoder`, `TcpCommandDecoderTests`
- Broker: `SubscriberIdentity`, `SubscriberRegistry`, `BrokerTcpFrameHandler`, `BrokerUdpDatagramHandler`, `UdpRemoteLeaseTracker`
- Server: `BrokerServerOptions`, `BrokerServer`, `BrokerServerTests`
- 문서: `DECISIONS.md`, `CURRENT_PLAN.md`, stable identity 설계/계획 문서

범위 밖:

- 인증/권한, persistent replay, durable delivery, DDS discovery
- RIO/io_uring backend
- latency hard gate 또는 CI workflow

## 2. Findings

### F1. UDP lease sweep 이 stable registry 를 disconnected 상태로 만들지 않는다

- Severity: Major
- Dimension: correctness / reliability / maintainability

Evidence:

- 설계는 "TCP connection close 또는 UDP idle sweep 으로 current target 이 사라지면 current target 을 null 로 바꾸고 retention timeout 으로 entry 를 제거한다"고 명시한다.
  - `docs/superpowers/specs/2026-06-22-stable-subscriber-identity-reconnect-policy-design.md:159`
  - `docs/superpowers/specs/2026-06-22-stable-subscriber-identity-reconnect-policy-design.md:163`
- 실제 `BrokerUdpDatagramHandler.SweepExpiredUdpLeases(...)`는 `_udpLeases.SweepExpired(now)`만 호출한다.
  - `src/Hps.Broker/BrokerUdpDatagramHandler.cs:153`
- `UdpRemoteLeaseTracker.SweepExpired(...)`는 routing table 과 lease table 에서만 remote 를 제거한다.
  - `src/Hps.Broker/UdpRemoteLeaseTracker.cs:152`
  - `src/Hps.Broker/UdpRemoteLeaseTracker.cs:171`
- `SubscriberRegistry.SweepDisconnected(...)`는 `CurrentTarget == null` 인 entry 만 제거한다.
  - `src/Hps.Broker/SubscriberRegistry.cs:240`
- 현재 lease sweep 테스트는 registry 를 주입하지 않는 runtime remote 기준만 검증한다.
  - `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs:296`

Impact:

stable identity 를 켠 UDP remote 가 idle sweep 으로 routing table 에서 제거되어도 registry 의 current target 은 계속 online 으로 남는다. 그 결과 retention timer 가 해당 identity metadata 를 제거하지 못하고, 오래된 `(IUdpEndpoint, remoteEndPoint)` target mapping 이 registry 내부에 남는다. 같은 remote 가 이후 다른 id 로 `REGISTER`하면 이미 다른 identity 에 매핑된 target 으로 판단되어 datagram 이 drop 될 수 있고, endpoint close 또는 explicit unregister 전까지 metadata 가 기대보다 오래 유지된다.

Recommendation:

`UdpRemoteLeaseTracker.SweepExpired(...)`가 만료된 remote target 정보를 handler 에 돌려주도록 바꾸고, `BrokerUdpDatagramHandler.SweepExpiredUdpLeases(...)`가 stable registry 에도 `RemoveTarget(BrokerSubscriber.ForUdp(endpoint, remote), now)`를 호출해야 한다. 이때 routing table 제거는 중복 호출되어도 idempotent 해야 하므로, 반환값 의미를 "routing 제거 수"와 "registry disconnected 수" 중 무엇으로 둘지 테스트에서 명확히 고정해야 한다.

추천 회귀 테스트:

- `BrokerUdpDatagramHandlerTests`
- stable registry + UDP lease enabled handler 구성
- `REGISTER device-a` -> `SUBSCRIBE alpha`
- idle timeout 초과 후 `SweepExpiredUdpLeases(now)`
- `registry.SweepDisconnected(now + retention, retention)` 이 1을 반환해야 한다.
- 같은 id 재등록 후 retained topic 이 복구되지 않아야 한다.

### F2. UDP invalid stable identity 예외가 handler 밖으로 escape 되어 endpoint 를 닫을 수 있다

- Severity: Major
- Dimension: reliability / operability / protocol consistency

Evidence:

- `TcpCommandDecoder.TryDecodeTopicOnlyCommand(...)`는 ASCII space 만 구분자로 보고, tab 같은 다른 whitespace 는 token 내부 값으로 통과시킨다.
  - `src/Hps.Protocol/TcpCommandDecoder.cs:72`
  - `src/Hps.Protocol/TcpCommandDecoder.cs:84`
- `SubscriberIdentity.Create(...)`는 `char.IsWhiteSpace(...)`를 거부한다.
  - `src/Hps.Broker/SubscriberIdentity.cs:30`
- `BrokerUdpDatagramHandler.OnDatagramReceived(...)`는 `REGISTER` 처리 중 발생한 `ArgumentException`을 잡지 않고, `finally`에서 datagram 만 release 한다.
  - `src/Hps.Broker/BrokerUdpDatagramHandler.cs:93`
  - `src/Hps.Broker/BrokerUdpDatagramHandler.cs:132`
  - `src/Hps.Broker/BrokerUdpDatagramHandler.cs:163`
- SAEA UDP receive loop 는 handler 예외를 endpoint close notification 으로 수렴시킨다.
  - `src/Hps.Transport/Saea/SaeaTransport.cs:340`
  - `src/Hps.Transport/Saea/SaeaTransport.cs:346`
- 기존 UDP duplicate identity 테스트의 의도는 "bad datagram 하나 때문에 shared UDP endpoint 전체를 닫지 않는다"이다.
  - `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs:176`

Impact:

공유 UDP socket 에서 한 remote 가 `REGISTER <tab>` 또는 `UNREGISTER <tab>` 같은 invalid identity command 를 보내면 Broker handler 예외가 transport 로 전파될 수 있다. SAEA 기준선에서는 이 예외가 endpoint close 로 이어지므로, malformed datagram 하나가 모든 UDP remote 의 수신 경로를 중단시킬 수 있다. 이는 UDP malformed command 는 datagram 만 drop 한다는 v1 정책과 어긋난다.

Recommendation:

UDP handler 는 stable identity validation 실패를 protocol-error datagram drop 으로 격리해야 한다. 최소 수정은 `RegisterUdpTarget(...)`와 `UNREGISTER` 처리에서 `ArgumentException`을 잡아 datagram drop 으로 반환하는 것이다. 더 구조적인 수정은 Protocol token validation 과 `SubscriberIdentity.Create(...)`의 whitespace 기준을 맞추는 것이지만, UDP endpoint close 방지는 handler 경계에서 반드시 보장해야 한다.

추천 회귀 테스트:

- `BrokerUdpDatagramHandlerTests`
- `REGISTER \t` 또는 `UNREGISTER \t` datagram 을 보낸다.
- `OnDatagramReceived(...)`가 throw 하지 않아야 한다.
- `FakeUdpEndpoint.CloseCallCount == 0`
- `PinnedBlockMemoryPool.RentedCount == 0`
- 기존 subscription 이 있으면 그대로 유지되어야 한다.

## 3. Material failure modes

### UDP stable identity idle sweep 후 registry stale-current

- Trigger: stable identity enabled + UDP lease sweep enabled + registered UDP remote idle timeout 초과
- Impact: routing table 에서는 fan-out target 이 제거되지만 registry current target 이 남아 retention cleanup 이 실행되지 않는다.
- Detection: registry 가 주입된 handler lease sweep 테스트에서 `SweepDisconnected(...)`가 0을 반환한다.
- Mitigation: lease sweep 만료 target 을 registry 에도 `RemoveTarget(...)`으로 전달한다.

### Invalid UDP identity command 로 endpoint close

- Trigger: shared UDP endpoint 에 `REGISTER`/`UNREGISTER` command 가 decoder 는 통과하지만 `SubscriberIdentity.Create(...)`에서 거부되는 token 을 포함
- Impact: handler 예외가 SAEA receive loop 로 전파되고, endpoint close notification 이후 UDP 수신 loop 가 종료된다.
- Detection: 실제 SAEA UDP loopback 에서 invalid identity datagram 이후 정상 datagram 을 보내면 수신되지 않는다.
- Mitigation: UDP handler 에서 identity validation failure 를 datagram drop 으로 수렴시킨다.

## 4. Deferred items

- stable subscriber identity 의 인증/권한 검증은 의도된 범위 밖이다. 현재 wire identity 는 trusted/internal network 전제에서만 안전하다.
- diagnostics friendly-name 또는 stable identity 노출은 아직 v1 surface 에 추가하지 않는다.
- disconnected payload replay, durable history, reliable delivery 는 여전히 범위 밖이다.

## 5. Unresolved decisions that may bite you later

- UDP lease sweep 의 반환값 의미를 명확히 해야 한다. 지금은 제거된 routing subscription 수를 반환한다. registry disconnect 까지 같이 처리하면 "subscription 제거 수"와 "expired remote 수"가 달라질 수 있다.
- Protocol decoder 와 Broker identity validator 의 token validation 기준이 다르다. 우선 handler 방어로 endpoint close 를 막되, 후속으로 token grammar 를 한 곳에 모을지 결정해야 한다.

## 6. Completion summary

- Reviewed scope: stable identity protocol decode, registry, TCP/UDP handler, Server opt-in wiring, loopback coverage, 상태 문서
- Major findings: UDP lease sweep 이 registry disconnected 상태를 만들지 않음, UDP invalid identity 예외가 endpoint close 로 이어질 수 있음
- Key risks: idle UDP stable subscriber metadata 누적, malformed datagram 1개로 shared UDP endpoint 수신 중단
- Deferred items: 인증/권한, diagnostics friendly-name, replay/durable delivery
- Next work unit: F1을 먼저 TDD로 수정하고, 그 다음 F2를 별도 작은 커밋 단위로 수정한다.
