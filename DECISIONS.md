# DECISIONS.md

## Archive

상세 결정 원문은 `docs/agent-state/decisions/2026-06.md`에 보존했다.
이 파일은 현재 구현 판단에 필요한 decision index 로 유지한다.

## Active Decision Index

- D084 — explicit runner baseline 은 `docs/benchmarks/baselines/runners/<runner-id>/YYYY-MM-DD/session-NN/`에 저장한다.
- D083 — explicit runner baseline 은 기존 date root 에 섞지 말고 저장 구조 정책을 먼저 설계한다.
- D082 — 2026-06-24 compatible baseline 3개는 reference latency envelope 이며 hard latency/CI gate 는 계속 보류한다.
- D081 — 문서 전용 작업은 관련 문서 전체를 한 coherent documentation cycle 로 정렬한다.
- D080 — summary/history comparison signal 은 non-failing compatibility artifact 로 둔다.
- D079 — benchmark runner identity/environment metadata 는 raw report schema v1 additive 관측 필드로 먼저 기록한다.
- D078 — baseline history report command 는 provider-independent aggregate artifact 로 두고 warning 은 계속 soft signal 로 유지한다.
- D077 — UDP receive command 와 lease sweep 의 broker state mutation 은 `BrokerUdpDatagramHandler` gate 로 직렬화한다.
- D076 — Late `REGISTER`는 같은 runtime target 의 기존 runtime 구독을 stable identity metadata 로 이관하지 않고 제거한다.
- D075 — Stable subscriber identity 는 후속 opt-in Broker registry 로 설계하며, 기본 v1 runtime target subscription 은 유지한다.
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
- 문서 전용 작업은 코드/테스트 구현과 분리하되, 같은 정책·설계·상태를 설명하는 관련 문서는 한 번에
  갱신한다(D081). 문서 작업을 너무 잘게 나눠 root state, decision, spec, review 문서 사이에 모순을
  남기지 않는다.
- Interface Server v1은 최신 상태 fan-out, bounded drop-oldest, diagnostics snapshot, benchmark artifact 를 우선한다.
- 2026-06-24 compatible baseline 3개는 reference latency envelope 로 채택하되, hard latency gate 와 CI
  warning-as-failure 는 계속 보류한다(D082). 현재 `p99 max`는 load 1020.4 us, open-loop 1006.5 us 까지
  관측됐으므로 1 ms hard SLO 는 현 baseline 과 맞지 않는다. 이 baseline 은 `runner-id=local-unspecified`라
  gate 승격 표본 count 에 산입하지 않고 reference 로만 사용한다.
- 명시적 runner id baseline 은 현재 `2026-06-24` date root 에 `session-04`처럼 바로 섞지 않는다(D083).
  기존 date root 는 `local-unspecified` session 으로 이미 history-compatible 하므로, 다음 단위에서 runner/date
  directory grouping 과 history reader 입력 범위를 먼저 설계한다.
- 명시적 runner id baseline 은 `docs/benchmarks/baselines/runners/<runner-id>/YYYY-MM-DD/session-NN/`
  구조에 저장한다(D084). runner root 는 현재 `BaselineHistoryReader` parent-root 규칙으로 바로 읽을 수 있고,
  기존 top-level `YYYY-MM-DD` roots 는 legacy/local-unspecified baseline 으로 보존한다. `runner-id`는
  privacy-safe stable token 으로 두며 host name, user name, IP address, 내부 자산 번호를 쓰지 않는다.
- CI workflow, RIO/io_uring backend 는 아직 별도 설계 대상이다.
- baseline run 비교 가능성은 runner identity/environment metadata 가 raw report 에 남은 뒤에 판단한다.
  초기 metadata 는 schema-version 1 additive field 로 두고, host name/user name/IP address 는 자동 수집하지 않는다.
  runner 를 명확히 구분해야 하면 `HPS_BENCHMARK_RUNNER_ID`를 명시한다.
- summary/history comparison signal 은 hard gate 나 기존 `warning-count`와 분리한다.
  `comparison-compatible`, `comparison-key`, `comparison-mismatches` 계열 field 는 같은 runner/profile/case 인지 판단하는
  non-failing compatibility artifact 이며, legacy/unknown metadata 는 compatible 로 추정하지 않는다.
  `BenchmarkProfile`, `RunnerId`, `RunnerKind`, `TransportBackend`, OS/framework/architecture field 중 하나라도 `unknown`이면
  partial metadata 라도 비교 가능성이 증명되지 않은 것으로 보고 `unknown-runner`로 처리한다.
  summary comparison key 는 summary 안의 `load`/`open-loop` scenario 차이를 허용하기 위해 `result-name`별 `cases` 배열로 표현한다.
- baseline history report 는 여러 session `summary.json`을 읽는 파생 aggregate artifact 로 만들고, warning 은 exit code 에 영향을 주지 않는
  soft signal 로 유지한다. history `hard-passed`는 모든 session summary 의 `hard-passed`가 true 일 때만 true 이며,
  상위 실패 카운터는 `failed-session-count`로 기록한다. 누락된 p99 값은 `0`이 아니라 JSON `null`과 Markdown `-`로 드러낸다.
  구현 command enum 은 `SummarizeBaselineHistory`로 고정한다.
- stable subscriber 가 `REGISTER` 전에 만든 runtime 구독은 identity metadata 로 자동 이관하지 않는다.
  `REGISTER` 시점에 제거해 metadata 에 없는 stale target 이 close 이후 routing table 에 남지 않게 한다.
- UDP lease sweep 이 활성화된 remote 도 같은 기준을 따른다. `REGISTER` 성공 후 같은 remote 의 lease metadata 는
  stable identity topic set 으로 교체하고, topic 이 없으면 기존 runtime lease 를 제거한다.
- stable subscriber identity 는 `docs/superpowers/specs/2026-06-22-stable-subscriber-identity-reconnect-policy-design.md`의
  opt-in registry/rebind 정책을 기준으로 후속 구현 계획을 작성한다.
- UDP stable identity 와 optional lease sweep 을 함께 쓰는 경로에서는 handler gate 를 receive command/sweep/endpoint-close state mutation 의
  최상위 직렬화 지점으로 둔다. 단, UDP PUBLISH 의 실제 fan-out 은 lease activity 갱신 후 lock 밖에서 수행한다.
