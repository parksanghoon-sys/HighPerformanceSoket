# RIO UDP parity / default promotion readiness 검토

## Scope

- 검토 범위: `RioTransport`, `RioUdpEndpoint`, `TransportFactory.CreateDefault()`, RIO/SAEA transport tests, D108/D109.
- 핵심 목적: D109 RIO UDP 구현 완료 이후 `TransportFactory.CreateDefault()`를 RIO 후보로 승격할 수 있는지 재평가한다.
- 범위 밖: 실제 default factory 변경, composite backend 구현, RIO UDP 성능 벤치마크 수집.

## 확인된 상태

- RIO UDP native Ex wrapper, endpoint owner, receive loop, send loop, endpoint diagnostics parity 는 구현됐다.
- RIO UDP loopback tests 는 bind, receive, echo send, endpoint snapshot 을 검증한다.
- `dotnet build HighPerformanceSocket.slnx --no-restore`는 경고 0/오류 0이다.
- `dotnet test HighPerformanceSocket.slnx --no-build`는 314개 통과 상태다.
- `TransportFactory.CreateDefault()`는 여전히 `SaeaTransport`를 반환한다.

## Findings

### Major / architecture, compatibility

RIO default promotion 은 아직 이르다.

Evidence:

- D108의 readiness gate 는 기능 parity, fallback 정책, contract parity matrix, performance evidence, 운영 문서 경계를 모두 요구한다.
- RIO UDP 구현은 현재 IPv4 `SOCKADDR_INET` encode/decode 만 지원한다.
- RIO UDP tests 는 live loopback 핵심 경로를 검증하지만, SAEA UDP에 있는 close drain, drop-oldest, high-watermark, no-prefetch, handler exception 세부 matrix 를 모두 같은 강도로 공유하지는 않는다.
- default factory 는 RIO unavailable 시 SAEA fallback 을 선택하는 probe/wiring 을 아직 갖지 않는다.
- RIO UDP 성능 artifact 는 아직 없다.

Impact:

- 지금 default 를 RIO로 바꾸면 IPv6 UDP, 일부 UDP failure mode, fallback observability 가 충분히 검증되지 않은 상태로 production 기본 경로가 바뀐다.
- Interface Server 기본 backend 는 TCP/UDP 모두를 안정적으로 제공해야 하므로, 단순히 RIO TCP/UDP loopback이 green이라는 이유만으로 승격하면 운영 리스크가 크다.

Recommendation:

- `TransportFactory.CreateDefault()`는 계속 SAEA를 반환한다.
- 다음 구현 단위는 default promotion 이 아니라 backend contract matrix 보강으로 둔다.
- 특히 RIO UDP close drain, drop-oldest/high-watermark, handler exception close notify, no-prefetch/pool ownership 을 SAEA와 같은 의미로 검증한다.
- 그 다음 RIO UDP benchmark artifact 를 수집하고 fallback policy 를 별도 설계한다.

## Material failure modes

- Trigger: Windows에서 default backend 를 RIO로 즉시 승격.
- Impact: IPv6 UDP bind/send, fallback visibility, 일부 close/drop edge case 가 검증 부족 상태로 기본 서버 경로에 들어간다.
- Detection: shared backend contract suite, RIO UDP stress/ownership tests, benchmark artifact 비교.
- Mitigation: default SAEA 유지, RIO opt-in 유지, contract matrix와 benchmark evidence 축적 후 재평가.

## Deferred items

- RIO UDP shared contract matrix 보강.
- RIO UDP benchmark artifact 수집.
- RIO unavailable fallback/default selection policy 설계.
- IPv6 UDP RIO `SOCKADDR_INET` encode/decode 지원 여부 결정.

## Unresolved decisions that may bite you later

- RIO default promotion 을 `CreateDefault()` 내부 자동 선택으로 할지, 명시 policy API로 분리할지.
- RIO UDP IPv6 지원을 default promotion gate 전에 필수로 볼지.
- RIO UDP completion wait 를 bounded polling 유지로 둘지, TCP처럼 IOCP/RIONotify 로 재승격할지.

## Completion summary

- Reviewed scope: RIO UDP D109 구현 완료 후 D108 default readiness.
- Major findings: default promotion 은 아직 보류해야 한다.
- Key risks: IPv4-only UDP, incomplete shared contract matrix, fallback policy 부재, UDP benchmark evidence 부족.
- Deferred items: contract matrix, benchmark, fallback policy, IPv6 decision.
- Unresolved important decisions: automatic default vs explicit policy, IPv6 gate, UDP completion wait strategy.
