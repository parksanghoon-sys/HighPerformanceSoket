# RIO default promotion readiness 설계

## 상태

Accepted.

## 배경

Phase 5에서 `RioTransport`는 Windows opt-in TCP backend 로 성장했다.
현재 구현은 RIO capability probe, native function table load, TCP listen/connect/accept, receive/send pump,
IOCP notification wait, receive/length-prefix/payload buffer registration reuse 를 갖고 있다.

하지만 `TransportFactory.CreateDefault()`는 여전히 `SaeaTransport`를 반환한다.
D097, D101도 RIO를 명시 opt-in/test path 로 먼저 검증하고, default factory 는 유지한다고 기록했다.

이번 설계의 목적은 RIO를 지금 기본 backend 로 바꾸는 것이 아니라,
기본 backend 후보로 승격하기 전에 닫아야 하는 조건을 명확히 하는 것이다.

## 현재 확인된 사실

- `TransportFactory.CreateDefault()`는 `new SaeaTransport()`를 반환한다.
- `CreateDefault_DuringRioOptInPhase_ReturnsSaeaTransport` 테스트가 이 정책을 고정한다.
- `RioCapabilityProbe.GetStatus()`는 Windows/비Windows 및 function table load 가능 여부를 예외 없이 상태값으로 반환한다.
- benchmark runner 는 `--backend rio` 명시 선택으로만 `RioTransport`를 생성한다.
- explicit RIO benchmark 에서 RIO unavailable 은 SAEA fallback 이 아니라 실행 실패로 처리한다.
- `RioTransport`는 TCP path 만 구현했다. `BindUdpAsync(...)`는 `TransportBase` 기본 구현을 따라 `NotSupportedException`이다.
- Interface Server 목표는 TCP/UDP endpoint 로 외부 데이터를 받아 topic subscriber 에 발행하는 것이다.

## 결정

`TransportFactory.CreateDefault()`는 계속 SAEA를 반환한다.
현재 RIO는 default backend 로 승격하지 않는다.

이유는 성능 이전에 기능 parity 문제가 먼저 있기 때문이다.
`ITransport` 기본 구현은 server/broker 상위 계층에서 TCP와 UDP를 모두 요구할 수 있는 단일 backend 로 취급된다.
RIO는 현재 TCP-only 이므로 default 로 바꾸면 UDP endpoint path 가 Windows에서 기능 퇴행한다.

따라서 RIO default promotion 은 아래 readiness gate 가 모두 닫힌 뒤 별도 결정으로만 진행한다.

## Readiness gate

### Gate 1. 기능 parity

기본 backend 후보는 public `ITransport` 계약을 실제로 만족해야 한다.

- TCP: listen/connect/accept/receive/send/close notify/backpressure/drop diagnostics.
- UDP: bind/receive/send-to/handler exception close notify/backpressure/drop diagnostics.
- Start/Stop/Dispose 반복 수명과 pending/in-flight buffer release.
- `ITransportDiagnostics`와 endpoint snapshot semantics.

현재 상태:

- TCP는 opt-in path 에서 상당 부분 검증됐다.
- UDP RIO는 없다.
- 따라서 Gate 1 은 미충족이다.

### Gate 2. fallback 정책

default factory 는 capability probe 실패로 호출자를 실패시키면 안 된다.

- Windows + RIO available + full parity 가 확인된 경우에만 RIO 선택을 고려한다.
- 그 외에는 SAEA fallback 이어야 한다.
- 명시 opt-in RIO path 와 benchmark `--backend rio`는 지금처럼 fallback 하지 않고 실패해야 한다.

이렇게 해야 benchmark artifact 오염과 production fallback semantics 를 분리할 수 있다.

### Gate 3. contract parity test matrix

기본 승격 전에는 backend 별로 같은 의미의 계약 테스트가 필요하다.

- 공통 `ITransport` contract suite 를 SAEA/RIO에 모두 적용한다.
- TCP loopback: small/large/length-prefixed payload, handler exception, repeated close/churn, pending queue drain.
- UDP loopback: datagram receive/send, handler exception, no prefetch, drop-oldest, high-watermark.
- BrokerServer: TCP/UDP subscribe/publish, stable identity reconnect/rebind, pool leak 0.
- diagnostics: transport snapshot, endpoint snapshot, drop/high-watermark counters.

현재 RIO tests 는 TCP 중심이며 UDP matrix 가 비어 있다.

### Gate 4. performance evidence

RIO가 default 로 들어가려면 TCP path 에서 최소한 SAEA 대비 명확한 regression 이 없어야 한다.

- 같은 runner/date 에서 SAEA와 RIO raw report 를 분리 수집한다.
- actual-rate, sent/received/drop/pool-rented hard gate 는 통과해야 한다.
- p50/p99 latency 는 아직 hard SLO가 아니므로 report-only로 비교하되, default 승격 판단에는 evidence 로 사용한다.
- RIO가 특정 path 에서만 유리하다면 default 가 아니라 명시 backend selector 또는 host-level policy 가 더 적절하다.

현재 session-06 RIO는 100Hz target 을 맞추고 pool leak/drop 없이 통과했지만,
이것만으로 TCP/UDP 통합 default 를 결정할 수는 없다.

### Gate 5. 운영/문서 경계

default 승격은 public behavior 변화이므로 문서와 운영 경계가 필요하다.

- `AGENTS.md`, `DECISIONS.md`, benchmark docs 에 default 선택 정책을 반영한다.
- runtime report identity 에 backend 가 항상 남아야 한다.
- RIO unavailable/fallback event 를 관측할 수 있는 최소 diagnostics 또는 startup log 정책을 정한다.
- Windows-only RIO와 cross-platform SAEA/io_uring future path 의 선택 순서를 명시한다.

## 대안 검토

### 대안 A. 지금 SAEA default 유지

선택한다.

장점:

- TCP/UDP 기능 parity 를 보존한다.
- 기존 benchmark/broker/server tests 의 의미가 변하지 않는다.
- RIO를 계속 opt-in 으로 hardening 할 수 있다.

단점:

- Windows에서 RIO TCP 개선이 자동으로 production path 에 들어가지 않는다.

### 대안 B. Windows + RIO available 이면 `CreateDefault()`가 RIO 반환

선택하지 않는다.

문제:

- UDP path 가 `NotSupportedException`으로 퇴행한다.
- 상위 계층은 concrete backend 를 모르기 때문에 TCP-only backend 선택을 안전하게 보정할 수 없다.
- RIO unavailable fallback 과 RIO TCP-only fallback 이 섞여 운영 원인 분리가 어려워진다.

### 대안 C. composite default backend

후속 설계 후보로 둔다.

예: TCP는 RIO, UDP는 SAEA로 위임하는 composite `ITransport`.

장점:

- Windows에서 TCP RIO 최적화를 default path 에 일부 반영할 수 있다.
- UDP parity 부재를 SAEA로 보완할 수 있다.

위험:

- 하나의 `ITransportDiagnostics` snapshot 에 서로 다른 backend endpoint 를 섞어야 한다.
- Start/Stop/Dispose, shared receive handler, datagram handler, endpoint identity, stable identity diagnostics 경계가 복잡해진다.
- RIO/SAEA connection list 와 UDP endpoint list 를 합치는 새로운 owner 가 필요하다.

따라서 composite 는 지금 default 승격 shortcut 으로 만들지 않고, 별도 설계가 필요하다.

## 구현 지침

이번 설계 직후 코드 behavior 는 바꾸지 않는다.

다음 구현 후보는 둘 중 하나다.

1. RIO UDP backend 설계/구현으로 Gate 1 을 닫기 시작한다.
2. composite backend feasibility 설계를 작성해 TCP RIO + UDP SAEA default 가 구조적으로 타당한지 먼저 판단한다.

현재 목표가 Interface Server 의 TCP/UDP endpoint 발행이므로, 기본 추천은 RIO UDP backend 설계다.
composite 는 운영 단순성보다 조기 TCP 최적화 노출을 우선할 때만 선택한다.

## 검증 계획

이번 설계 단위:

- `TransportFactory.CreateDefault()` 현재 behavior 확인.
- `RioCapabilityProbe`/benchmark backend selector 확인.
- RIO TCP tests 와 SAEA UDP/Broker tests coverage 대조.
- `git diff --check`.

후속 구현 단위:

- RIO UDP 설계: `RIOSendEx`/`RIOReceiveEx`, remote endpoint buffer ownership, endpoint close notify, datagram backpressure.
- 또는 composite 설계: transport handler multiplexing, diagnostics merge, lifecycle ordering, fallback visibility.

## 미결정/후속

- RIO UDP를 먼저 구현할지, composite backend feasibility 를 먼저 검토할지.
- default factory 가 향후 `CreateDefault()` 하나로 충분한지, 아니면 `CreatePreferred(...)` 같은 명시 policy API가 필요한지.
- RIO default 승격 시점에 performance threshold 를 hard gate 로 둘지, report-only evidence 로 둘지.
