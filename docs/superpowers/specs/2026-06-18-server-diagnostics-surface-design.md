# Server diagnostics surface 필요성 재검토

- 날짜: 2026-06-18
- 상태: Accepted
- 관련 결정: D038, D042, D056, D061, D062, D066, D067
- 범위: `BrokerServer`에 diagnostics convenience API 를 지금 추가할지 판단한다. production code 변경은 포함하지 않는다.

## 목적

D042/D056으로 Transport diagnostics 는 이미 두 계층으로 분리됐다.

- `ITransportDiagnostics.GetDiagnosticsSnapshot()`:
  TCP/UDP transport kind 별 누적 drop count 와 pending send queue high-watermark.
- `ITransportEndpointDiagnostics.GetEndpointSnapshots()`:
  active endpoint 별 pending count, endpoint high-watermark, dropped pending send count.

D066에서는 stalled TCP subscriber stress 가 이 pull snapshot 으로 설명 가능함을 확인했고,
`BrokerServer` convenience diagnostics API 는 실제 host 운영 표면이 더 구체화될 때 다시 판단한다고 남겼다.

이번 문서는 그 판단을 현재 HEAD 기준으로 다시 닫는다. 결론은 **v1에서는 `BrokerServer` diagnostics pass-through 를 추가하지 않는다**이다.

## 현재 확인된 사실

- `BrokerServer`는 D038/D061에 따라 Transport/Protocol/Broker 를 조립하는 얇은 host 이다.
- 현재 `BrokerServer`는 단일 injected `ITransport`를 소유하며, 다중 transport 합산이나 monitoring endpoint 를 제공하지 않는다.
- 현재 diagnostics 사용 지점은 테스트와 benchmark 이며, 모두 `SaeaTransport` 또는 injected transport 를 직접 보유하고 있다.
- 샘플 `Hps.Sample.BrokerServer`는 운영 daemon 이 아니라 수동 fan-out 확인용 console host 이며, diagnostics endpoint 를 제공하지 않는다.
- `ITransport` 기본 계약은 의도적으로 diagnostics 를 포함하지 않는다. diagnostics 는 backend 가 제공할 때만 좁혀서 읽는 선택적 capability 이다.

## 검토한 접근

### A. v1에서는 Server API 를 추가하지 않는다

`BrokerServer`는 기존처럼 lifecycle orchestration 만 맡고, diagnostics 는 호출자가 주입한 transport 를
`ITransportDiagnostics` 또는 `ITransportEndpointDiagnostics`로 좁혀서 읽는다.

장점:

- D038의 얇은 host 책임과 맞다.
- `BrokerServer` public API 가 nullable pass-through 또는 aggregation semantics 를 성급히 약속하지 않는다.
- 다중 transport, endpoint id 충돌, closed endpoint attribution 같은 후속 문제를 지금 API에 고정하지 않는다.
- 테스트/benchmark 는 이미 transport 를 직접 보유하므로 기능 손실이 없다.

단점:

- `BrokerServer`만 들고 있는 future host code 는 diagnostics 를 읽으려면 transport 참조를 함께 보관해야 한다.

### B. nullable pass-through accessor 추가

예: `TransportDiagnosticsSnapshot? TryGetTransportDiagnostics()` 또는 `bool TryGetTransportDiagnostics(out TransportDiagnosticsSnapshot snapshot)`.

장점:

- Server consumer 가 transport 를 직접 캐스팅하지 않아도 된다.
- 단일 transport host 에서는 구현이 단순하다.

단점:

- Server API 에 "transport diagnostics 를 server surface 로 재노출한다"는 방향을 고정한다.
- endpoint snapshot 도 함께 노출할지, transport aggregate 만 노출할지 바로 다음 질문이 생긴다.
- 향후 다중 transport 나 multi-backend host 가 생기면 합산, 충돌, 부분 capability 미지원 semantics 를 다시 정해야 한다.
- 현재 샘플/운영 host 에는 이 API 를 사용할 소비자가 없다.

### C. Server-level diagnostics model 추가

예: `BrokerServerDiagnosticsSnapshot`을 새로 만들고 transport aggregate, endpoint snapshot, subscription count, server state 를 합산한다.

장점:

- 장기적으로 운영 endpoint 나 monitoring API 에 가장 적합하다.

단점:

- 현재 필요보다 훨씬 크다.
- Broker subscription state, closed endpoint attribution, 다중 transport id namespace 를 함께 설계해야 한다.

## 결정

v1에서는 **A. Server API 를 추가하지 않는다**를 채택한다.

- `BrokerServer`에 diagnostics pass-through accessor 를 추가하지 않는다.
- `BrokerServerDiagnosticsSnapshot` 같은 server-level 값 타입도 추가하지 않는다.
- 현재 diagnostics 는 기존처럼 optional Transport capability 로 읽는다.
- 테스트/benchmark 에서 transport 를 직접 캐스팅하는 것은 현재 범위에서 허용한다.

## 이유

현재 요구는 "drop 이 발생했는지, TCP/UDP 중 어느 kind 인지, active endpoint 중 누가 포화됐는지"를 관측하는 것이다.
이 요구는 이미 Transport diagnostics capability 두 개로 충족된다.

반대로 `BrokerServer` API 를 지금 넓히면, 단일 injected transport 를 감싼 pass-through 인지,
여러 transport 를 합산한 server-level view 인지, endpoint snapshot 까지 포함하는지, capability 미지원 backend 를
어떻게 표현할지 같은 결정을 앞당긴다. 이 결정들은 실제 운영 host/monitoring surface 가 생긴 뒤에 정하는 편이 낫다.

## 후속 승격 조건

다음 중 하나가 확인되면 별도 설계로 승격한다.

- 운영 host 가 `BrokerServer`만 주입받고 transport 참조를 갖지 않는 구조가 된다.
- 샘플 또는 실제 host 에서 diagnostics 출력/HTTP endpoint/metrics exporter 가 필요해진다.
- 여러 transport/backend 를 하나의 server 가 합산해야 한다.
- closed endpoint 의 drop attribution, drop timestamp, subscription count 같은 server-level 상태가 함께 필요해진다.

## 검증

- 현재 source 검색으로 diagnostics 소비자가 테스트/benchmark 중심임을 확인한다.
- 문서/결정 단위이므로 production build/test 는 새로 요구하지 않는다.
- `git diff --check`로 whitespace 오류를 확인한다.
