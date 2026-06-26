# 2026-06-26 RIO UDP IPv6 Support Gate Design

## 목표

RIO UDP는 D118 이후 IPv4 loopback 기준으로 4096B x 100Hz load/open-loop scratch gate 를 통과했다.
하지만 D110에서 남긴 default promotion gate 중 `IPv6 지원 여부 판단`은 아직 닫히지 않았다.

이 설계의 목표는 지금 RIO UDP IPv6를 바로 구현할지, default promotion 전 gate 로 둘지,
또는 명시적 unsupported policy 로 둘지를 결정하는 것이다.

## 현재 확인된 사실

- SAEA UDP는 bind endpoint 의 `AddressFamily`로 socket 을 만들고,
  receive placeholder 도 IPv6 bind 인 경우 `IPAddress.IPv6Any`를 사용한다.
- RIO UDP는 `RioNative.CreateUdpSocket()`에서 `AF_INET` socket 을 만든다.
- RIO UDP send/receive address helper 는 `SOCKADDR_INET` buffer 를 쓰지만 현재 IPv4만 encode/decode 한다.
  - receive decode 는 `AddressFamily.InterNetwork`가 아니면 `NotSupportedException`을 던진다.
  - send encode 는 IPv4 `IPEndPoint`가 아니면 `NotSupportedException`을 던진다.
- RIO UDP tests 와 scratch benchmark 는 모두 IPv4 loopback 이다.
- `TransportFactory.CreateDefault()`는 D119에 따라 계속 deterministic SAEA default 이다.

## 문제

현재 RIO UDP IPv6는 두 가지 면에서 명확하지 않다.

1. `BindUdpAsync(new IPEndPoint(IPAddress.IPv6Loopback, 0))`는 RIO IPv4 socket 에 IPv6 endpoint 를 bind 하게 되어,
   정책 의도가 아니라 socket 오류 형태로 실패할 수 있다.
2. `TrySendTo(..., IPv6 remote, ...)`는 지금 구조상 enqueue 이후 background send loop 에서
   `EncodeSockaddrInet(...)`가 실패할 수 있다.

두 번째 경로는 특히 피해야 한다. `TrySendTo`가 `true`를 반환하면 transport queue 가 buffer ref 를 소유하는데,
background pump 가 unsupported address family 로 fault 되면 endpoint 는 열린 것처럼 보이고 send pump 만 죽는 상태가 될 수 있다.

## 선택지

### A. 지금 RIO UDP IPv6를 구현한다

채택하지 않는다.

필요 작업은 단순 guard 가 아니라 다음을 모두 포함한다.

- `WSASocketW` IPv6 registered UDP socket 생성 경로
- `SOCKADDR_IN6` encode/decode
- scope id 처리
- IPv6 loopback receive/send/fan-out tests
- IPv4/IPv6 mixed policy 또는 dual-mode socket 정책
- RIO UDP benchmark artifact 분리

현재 v1 evidence 목표는 IPv4 loopback 4096B x 100Hz이고, default backend 승격도 아직 보류다.
full IPv6 구현은 지금 작업 단위의 비용 대비 이득이 크지 않다.

### B. RIO UDP v1을 IPv4-only로 명시하고 unsupported IPv6를 public boundary 에서 막는다

채택한다.

RIO UDP는 opt-in backend 이며, D118 성능 evidence 는 IPv4 기준으로 수락된 상태다.
따라서 지금은 IPv6를 지원한다고 암묵적으로 보이게 두지 않고,
unsupported address family 를 public boundary 에서 명확히 거부하는 것이 가장 작은 올바른 변경이다.

구체적으로 다음 구현 단위에서 처리한다.

- `BindUdpAsync(...)`는 UDP local endpoint 가 IPv4 `IPEndPoint`가 아니면
  `NotSupportedException`으로 즉시 실패한다.
- `TrySendTo(...)`는 remote endpoint 가 IPv4 `IPEndPoint`가 아니면 enqueue 하지 않고 `false`를 반환한다.
  - `false`는 기존 send ownership 계약과 맞다. caller 는 방금 추가한 ref 를 되돌릴 수 있다.
  - unsupported remote 를 background send pump 까지 넘기지 않는다.
- 오류 메시지는 RIO UDP v1이 IPv4-only 임을 직접 말한다.

### C. default promotion 이 오기 전까지 아무것도 하지 않는다

채택하지 않는다.

default promotion 은 아직 보류지만, opt-in RIO backend 는 이미 사용 가능한 상태다.
unsupported IPv6가 public boundary 밖 background loop 에서 실패할 수 있는 상태는 operability 관점에서 정리해야 한다.

## 결정

D121로 다음을 기록한다.

- RIO UDP v1은 IPv4-only opt-in backend 로 유지한다.
- IPv6 UDP support 는 default backend promotion 전 gate 로 남긴다.
- default promotion 을 다시 검토하려면 다음 중 하나가 필요하다.
  - RIO UDP IPv6 bind/send/receive 를 구현하고 SAEA와 같은 contract matrix 로 검증한다.
  - 또는 default selector/composite policy 가 IPv6 endpoint 에 대해 SAEA fallback 을 명시적으로 제공한다.
- 지금 다음 구현은 full IPv6가 아니라 explicit unsupported boundary guard 다.

## 구현 계획 후보

다음 구현 계획은 한 작업 단위로 충분하다.

1. RIO UDP IPv6 bind guard test 를 추가한다.
   - RIO datagram unavailable 환경에서는 skip 한다.
   - available 환경에서는 `BindUdpAsync(IPAddress.IPv6Loopback)`가 `NotSupportedException`으로 실패해야 한다.
2. RIO UDP IPv6 remote send guard test 를 추가한다.
   - IPv4 local endpoint 를 bind 한 뒤 IPv6 remote 로 `TrySendTo`를 호출하면 `false`를 반환해야 한다.
   - pending queue/HWM/drop count 가 증가하지 않아야 한다.
   - caller-owned buffer ref 는 테스트에서 반환해 pool leak 0을 확인한다.
3. `RioTransport`에 UDP endpoint address-family guard helper 를 추가한다.
4. `BindUdpAsync`와 `TrySendTo`가 helper 를 사용하게 한다.

## 테스트 전략

- Red:
  - 현재 `BindUdpAsync(IPAddress.IPv6Loopback)`는 explicit `NotSupportedException`이 아니라 socket bind 오류 또는 다른 예외로 실패한다.
  - 현재 `TrySendTo`는 IPv6 remote 를 enqueue 하고 `true`를 반환할 수 있다.
- Green:
  - focused `RioTransportUdpTests`에서 IPv6 guard tests 통과.
  - focused `Hps.Transport.Rio.Tests` 통과.
  - 필요 시 solution build/test.

## Scope Exclusions

- `SOCKADDR_IN6` encode/decode 구현.
- IPv6 RIO benchmark artifact 수집.
- `TransportFactory.CreateDefault()` 변경.
- SAEA transport 동작 변경.
- host `--transport auto` 정책 변경.
- io_uring UDP IPv6 정책.

## Self-Review

- Placeholder scan: 남은 `TBD`/`TODO` placeholder 없음.
- Internal consistency: RIO opt-in IPv4-only 정책, D119 SAEA default 유지, D121 default promotion gate 가 서로 충돌하지 않는다.
- Scope check: full IPv6 구현이 아니라 unsupported boundary guard 로 다음 구현 단위가 충분히 작다.
- Ambiguity check: bind 는 `NotSupportedException`, send 는 ownership 계약 때문에 `false` 반환으로 고정했다.
