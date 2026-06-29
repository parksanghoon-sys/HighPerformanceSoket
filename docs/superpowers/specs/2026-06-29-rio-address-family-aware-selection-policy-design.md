# 2026-06-29 RIO Address-Family-Aware Selection Policy Design

## 목표

D121은 RIO UDP v1을 IPv4-only opt-in backend 로 남기고 IPv6를 default promotion gate 로 보류했다.
이후 코드를 다시 확인해 보니 RIO native socket 생성 경로는 UDP뿐 아니라 TCP도 `AF_INET`으로 고정되어 있다.
따라서 host composition 이 `--transport auto`에서 endpoint address family 를 보지 않고 RIO를 고르면,
IPv6 listen 주소에서는 "RIO를 선택했다"는 출력과 실제 socket 실패가 뒤섞인다.

이 설계의 목표는 full IPv6 구현 전에 RIO의 현재 지원 범위를 정확히 드러내고,
sample broker host 가 IPv6 endpoint 에서 명시적으로 SAEA를 선택하거나 실패하도록 만드는 것이다.

## 확인된 사실

- `RioNative.CreateTcpSocket()`과 `RioNative.CreateUdpSocket()`은 `AddressFamily.InterNetwork` 기반 registered socket 을 만든다.
- `RioTransport.BindUdpAsync(...)`와 `RioTransport.TrySendTo(...)`는 이미 D121 이후 IPv6 UDP local/remote endpoint 를 public boundary 에서 거부한다.
- `RioTransport.ListenTcpAsync(...)`와 `RioTransport.ConnectTcpAsync(...)`는 아직 TCP endpoint address family 를 조기에 확인하지 않는다.
  IPv6 endpoint 를 받으면 IPv4 registered socket 을 만든 뒤 bind/connect 단계에서 socket 계층 오류가 발생할 수 있다.
- `samples/Hps.Sample.BrokerServer`의 `SampleTransportSelector.Select(...)`는 RIO capability 상태만 보고 선택하며,
  사용자가 입력한 listen address family 를 모른다.
- D119에 따라 base `TransportFactory.CreateDefault()`는 계속 deterministic SAEA default 다.
- D120에 따라 RIO preferred/default 선택은 base factory 가 아니라 host composition 책임이다.

## 문제

`--transport auto`는 운영자가 "가능하면 RIO, 아니면 SAEA"를 기대하는 mode 다.
하지만 현재 RIO 지원 범위는 IPv4-only 이므로, IPv6 listen 주소와 함께 auto 를 쓰면 RIO capability 가 Available 인 환경에서
RIO가 선택되고 이후 bind 단계에서 실패할 수 있다.

명시적 `--transport rio`도 마찬가지다. 이 mode 는 fallback 하지 않는 opt-in 이므로,
IPv6 endpoint 에 대해서는 socket layer 예외가 아니라 "현재 RIO backend 는 IPv4-only"라는 오류를 먼저 보여주는 편이 맞다.

## 선택지

### A. 지금 RIO TCP/UDP full IPv6를 구현한다

채택하지 않는다.

필요한 범위는 단순 guard 가 아니라 다음을 포함한다.

- `WSASocketW` IPv6 registered TCP/UDP socket 생성 경로
- TCP listen/connect/accept 의 address-family propagation
- UDP `SOCKADDR_IN6` encode/decode, scope id 처리
- IPv4/IPv6 dual-mode socket 정책
- TCP/UDP IPv6 loopback contract matrix
- 별도 IPv6 benchmark artifact

D118/D121의 현재 evidence 는 IPv4 기준이며, default promotion 도 아직 보류되어 있다.
지금 full IPv6를 구현하면 RIO default promotion 판단보다 큰 범위의 native backend 확장이 먼저 들어간다.

### B. RIO backend 는 IPv4-only 로 명시하고 host auto 는 address family 를 기준으로 fallback 한다

채택한다.

구체 정책은 다음과 같다.

- RIO transport public boundary 는 TCP/UDP 모두 IPv4 `IPEndPoint`만 지원한다고 명시적으로 검사한다.
- `--transport rio`는 RIO capability 가 Available 이어도 listen address 가 IPv4가 아니면 runtime failure 로 종료한다.
- `--transport auto`는 listen address 가 IPv4가 아니면 RIO probe 결과와 무관하게 SAEA를 선택하고 fallback notice 를 출력한다.
- `--transport saea`는 address family 제한을 두지 않는다.
- base `TransportFactory.CreateDefault()`는 계속 SAEA를 반환한다.
- benchmark `--backend rio` 같은 explicit 측정 경로는 fallback 없이 RIO 계약을 그대로 검증한다.

이 정책은 현재 구현 범위와 일치한다. full IPv6 구현 없이도 sample host 는 IPv6 listen 에서 동작 가능한 SAEA 경로를 유지하고,
explicit RIO opt-in 은 지원하지 않는 endpoint 를 조기에 드러낸다.

### C. 하나의 host 에서 endpoint 별 composite transport 를 둔다

지금은 채택하지 않는다.

TCP는 SAEA, UDP는 RIO처럼 protocol/address family 별로 backend 를 섞는 composite host 는 장기적으로 가능하다.
하지만 현재 `BrokerServer`는 하나의 injected `ITransport`를 공유하고, diagnostics/endpoint identity 도 그 전제에 맞춰져 있다.
default promotion 이전 단계에서 composite transport 를 추가하면 backend 선택 문제보다 더 큰 orchestration surface 가 열린다.

## 결정

D122로 다음을 기록한다.

- RIO backend 의 현재 public support matrix 는 TCP/UDP 모두 IPv4 `IPEndPoint` 전용이다.
- full IPv6 구현은 default promotion gate 로 남기고 지금은 구현하지 않는다.
- host-level preferred selection 은 address-family-aware 해야 한다.
  - IPv4 listen endpoint + RIO Available + `auto`: RIO 선택
  - IPv6 listen endpoint + `auto`: SAEA fallback notice
  - IPv6 listen endpoint + explicit `rio`: runtime failure
  - explicit `saea`: 기존 동작 유지
- RIO transport 자체도 TCP IPv6 listen/connect 를 socket bind/connect 전에 명시적으로 거부한다.

## 구현 계획 후보

1. `RioTransportTcpTests`에 TCP IPv6 listen/connect explicit unsupported tests 를 추가한다.
2. `RioTransport.ListenTcpAsync(...)`와 `ConnectTcpAsync(...)`에 TCP endpoint guard 를 추가한다.
3. `SampleTransportSelector.Select(...)`에 listen address family 입력을 추가한다.
4. selector tests 에 IPv6 auto fallback 과 explicit rio failure 를 추가한다.
5. `Program.Main`에서 parsed host `IPAddress.AddressFamily`를 selector 로 전달한다.
6. D122/state docs 를 갱신하고 build/test/diff-check 를 수행한다.

## Scope Exclusions

- RIO full IPv6 TCP/UDP 구현
- `SOCKADDR_IN6` encode/decode
- dual-mode socket 정책
- base `TransportFactory.CreateDefault()` 변경
- `BrokerServer` composite transport
- benchmark explicit RIO fallback 정책 변경

## Self-Review

- Placeholder scan: `TBD`/`TODO` placeholder 없음.
- D119/D120/D121와의 일관성: base factory 는 유지하고, host composition 에서만 preferred policy 를 구체화한다.
- 최소 변경성: IPv6 구현 대신 현재 IPv4-only 범위를 public boundary 와 sample host 선택 정책에 반영한다.
- 운영 관측성: auto fallback 은 notice 로 드러나고, explicit RIO 실패는 runtime failure exit code 로 드러난다.
