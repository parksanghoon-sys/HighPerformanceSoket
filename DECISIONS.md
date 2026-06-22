# DECISIONS.md

## Archive

상세 결정 원문은 `docs/agent-state/decisions/2026-06.md`에 보존했다.
이 파일은 현재 구현 판단에 필요한 decision index 로 유지한다.

## Active Decision Index

- D074 — BrokerServer UDP lease sweep host timer 는 `BrokerServerOptions` public 설정으로 명시 활성화하며, 기본은 disabled 로 유지한다.
- D073 — UDP idle lease tracker/sweep 은 Broker 가 소유하고 Server 가 timer 로 트리거하며, 설정은 내부 options(기본 비활성)·시간은 `TimeProvider` 로 둔다.
- D072 — UDP stale remote cleanup 은 Broker/Server 소유의 선택적 lease cleanup 으로 두고 기본 idle expiry 는 비활성화한다.
- D071 — baseline report history 는 전역 index 를 두고 warning 은 계속 soft signal 로 유지한다.
- D070 — 3개 baseline session 확보 후에도 latency hard gate 는 보류하고 summary/soft warning 을 먼저 만든다.
- D069 — latency hard gate 전에는 반복 baseline artifact 를 먼저 축적한다.
- D068 — v1에서는 BrokerServer diagnostics pass-through API 를 추가하지 않는다.
- D067 — v1에는 configurable backpressure/QoS policy surface 를 추가하지 않는다.
- D066 — drop-oldest stress 관측은 pull snapshot 으로 충분하며 log/sampling 은 보류한다.
- D065 — TCP subscriber outbound 는 length-prefixed message frame 으로 보낸다.
- D064 — v1 transport send backpressure 기본 정책은 bounded drop-oldest 로 둔다.
- D063 — Phase 4 benchmark latency 는 hard gate 가 아니라 관측/추세 신호로 유지한다.
- D062 — 마지막 drop 메타데이터는 v1에 추가하지 않고 누적 kind/endpoint drop snapshot 으로 좁힌다.
- D061 — `BrokerServer`는 TCP/UDP ingress 를 독립 시작하고 Transport 수명은 공유한다.
- D060 — UDP broker v1은 datagram self-command 와 runtime remote target 으로 설계한다.
- D059 — v1 subscription 은 runtime endpoint 수명에 묶고 reconnect rebinding 은 제공하지 않는다.
- D058 — `EndpointId`는 transient diagnostics id 이며 stable routing id 가 아니다.
- D057 — Broker routing value 는 `BrokerSubscriber` endpoint target 으로 저장한다.
- D056 — Endpoint snapshot collection 은 선택적 Transport diagnostics capability 로 노출한다.
- D055 — Benchmark report high-watermark 필드는 schema-version 1의 additive field 로 유지한다.
- D054 — Endpoint identity 최소 계약은 Transport abstraction 의 값 snapshot 으로 시작한다.
- D053 — Interface Server 목표는 endpoint-aware publish model 과 send-side 관측성을 우선한다.
- D052 — Phase 4 benchmark report 는 공통 JSON schema 로 저장한다.
- D051 — Phase 4 closed-loop load 와 open-loop backpressure benchmark 를 분리한다.
- D050 — Phase 4 첫 벤치마크 기준은 SAEA TCP loopback 4096B×100Hz 로 고정한다.

## Historical Decision Archive

D001~D049 상세 내용은 `docs/agent-state/decisions/2026-06.md`를 본다. 주요 축은 다음과 같다.

- D001~D014: Phase 순서, 초기 성능 목표, TDD/작업 단위/테스트 주석 규칙.
- D015~D028: Transport send ownership, in-flight release, TCP/UDP SAEA 기준선.
- D029~D038: TCP framing, command decode, Broker routing/fan-out, BrokerServer 책임.
- D039~D049: drop-oldest, diagnostics, handler 예외 정책, sample/host 결정.

## 현재 판단 기준

- 새 기능은 작은 reviewable 단위로 구현하고, 관련 파일만 커밋한다(D013).
- 코드 변경은 Red-Green-Refactor 를 따른다(D014 및 AGENTS.md).
- Interface Server v1은 최신 상태 fan-out, bounded drop-oldest, diagnostics snapshot, benchmark artifact 를 우선한다.
- latency hard gate, CI workflow, stable subscriber identity, RIO/io_uring backend 는 아직 별도 설계 대상이다.
