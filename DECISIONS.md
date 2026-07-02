# DECISIONS.md

## Archive

상세 결정 원문은 `docs/agent-state/decisions/2026-06.md`에 보존했다.
이 파일은 현재 구현 판단에 필요한 decision index 로 유지한다.

## Active Decision Index

- D168 — D167 두 date root reference 원격 artifact gate 는 TCP reference 4/UDP reference 7 기준 signal 0으로 통과했다.
- D167 — D165 raw report 는 TCP 4-session/UDP 7-session protocol history 로 채택 완료했다.
- D166 — D165 passing artifact 는 protocol별 두 번째 date root reference 로 수동 채택한다.
- D165 — D164 reference 확장 원격 artifact gate 는 TCP reference 3/UDP reference 6 기준 signal 0으로 통과했다.
- D164 — D163 reference expansion 은 TCP 3-session/UDP 6-session protocol history 로 채택 완료했다.
- D163 — D160/D162 raw report 는 protocol별 provisional reference 확장 표본으로 수동 채택한다.
- D162 — D161 Markdown label 원격 artifact gate 는 label 반영과 TCP/UDP envelope signal 0을 확인했다.
- D161 — benchmark Markdown HWM label 은 TCP 전용 표현 대신 protocol-neutral `send queue HWM max`로 표시한다.
- D160 — D159 updated reference 원격 artifact gate 는 TCP/UDP envelope signal 0으로 통과했다.
- D159 — D158 이후 다음 단위는 fixed registration/zero-copy/default promotion 이 아니라 updated reference 원격 artifact gate 다.
- D158 — UDP open-loop p50 반복 signal 은 얇은 provisional reference 문제로 보고 D155~D157 UDP candidates 를 수동 채택해 reference 를 안정화한다.
- D157 — D156 추가 artifact 2개 수집 결과 UDP open-loop p50-median signal 이 3/3 반복되어 UDP latency triage 설계로 전환한다.
- D156 — D155 UDP envelope signal 은 즉시 최적화로 연결하지 않고 추가 reference-present artifact 로 반복성을 확인한다.
- D155 — D154 reference-present 원격 run 은 envelope artifact 생성을 검증했고 UDP signal 2개는 report-only triage 대상으로 남긴다.
- D154 — run 28492234252 artifact 는 ci-linux-iouring-x64-01 TCP/UDP protocol별 provisional repository reference baseline 으로 채택됐다.
- D153 — D152 artifact 는 io_uring protocol별 첫 provisional repository reference baseline 으로 수동 채택할 수 있다.
- D152 — io_uring envelope artifact 원격 run 은 reference 없음 skip 경로를 충족했고 다음 단위는 protocol별 reference baseline 수동 채택 정책 설계다.
- D151 — D150 p99 warning 은 io_uring protocol별 envelope comparison artifact 로 먼저 해석한다.
- D150 — io_uring `--runs 3` benchmark artifact 는 D149 evidence gate 를 충족했고 p99 warning 은 후속 분석으로 분리한다.
- D149 — io_uring benchmark artifact workflow 는 D148 이후 TCP/UDP baseline suite 를 `--runs 3`으로 승격한다.
- D148 — io_uring benchmark artifact workflow 는 protocol-first history root 구조로 D147 evidence gate 를 충족했다.
- D147 — Linux io_uring benchmark artifact 는 별도 workflow_dispatch workflow 로 수집한다.
- D146 — benchmark CLI 는 io_uring opt-in backend 를 지원하되 default promotion 은 계속 보류한다.
- D145 — D144 이후 다음 단위는 io_uring benchmark backend selector 로 성능 artifact 경로를 먼저 연다.
- D144 — io_uring bounded UDP receive window Linux contract artifact 는 native path 검증 gate 를 충족했다.
- D143 — io_uring UDP receive pump 는 bounded receive slot window 로 확장한다.
- D142 — io_uring Linux contract artifact 는 UDP receive/send pump native 검증 gate 를 충족했다.
- D141 — io_uring backend 도 선택적 endpoint diagnostics snapshot surface 를 SAEA/RIO와 맞춘다.
- D140 — io_uring UDP v1은 IPv4 one-deep recvmsg/sendmsg pump 로 제한한다.
- D139 — io_uring Linux contract artifact 는 TCP receive/send pump native 검증 gate 를 충족했다.
- D138 — io_uring 후속 구현은 UDP/zero-copy 최적화 전에 Linux contract evidence gate 를 먼저 둔다.
- D137 — io_uring TCP-first pump 구현은 shared queue/completion loop 와 공통 TransportConnection send queue 로 수락한다.
- D136 — io_uring TCP-first pump 설계는 transport shared queue/completion loop 와 reusable operation context 를 채택한다.
- D135 — io_uring native wrapper boundary 는 구현 완료하고 TCP/UDP pump 는 후속 설계로 분리한다.
- D134 — io_uring native wrapper 는 native adapter, queue owner, fixed buffer registration owner 로 분리한다.
- D133 — Phase 6 io_uring 첫 구현은 skeleton/probe/unsupported boundary 로 제한하고 native syscall wrapper 는 후속 task 로 분리한다.
- D132 — D131 이후 다음 후보는 Phase 6 Linux io_uring boundary 설계와 첫 구현 계획으로 둔다.
- D131 — CI push run 28350456434는 D127/D130 원격 artifact 검증 표본이자 두 번째 CI repository baseline 으로 채택한다.
- D130 — CI benchmark hard gate 실패도 summary/history/envelope artifact 작성 뒤 최종 실패로 복원한다.
- D129 — RIO native send probe 는 peer receive 를 먼저 열어 실제 transport drain 조건에서 completion 을 검증한다.
- D128 — D127 이후 다음 실행 후보는 push-triggered CI artifact 에 envelope 산출물이 포함되는지 검증하는 것이다.
- D127 — CI benchmark workflow 는 reference history 가 있을 때 report-only envelope comparison artifact 를 업로드한다.
- D126 — net9.0 프로젝트 검증은 repository `global.json`으로 .NET SDK 9.0 계열을 선택한다.
- D125 — runner/profile scoped 판단은 기존 warning-count 가 아니라 별도 baseline envelope comparison artifact 로 분리한다.
- D124 — local 3-date-root evidence 는 runner-local reference envelope 로 채택하고 warning/gate 승격은 runner-scoped threshold 설계 뒤로 분리한다.
- D123 — `local-win-x64-01` explicit runner 는 세 date root evidence 를 충족했지만 gate 승격은 별도 정책 재평가로 분리한다.
- D122 — RIO backend 는 TCP/UDP 모두 현재 IPv4-only opt-in 이며 host auto selection 은 address-family-aware fallback 을 수행한다.
- D121 — RIO UDP v1은 IPv4-only opt-in backend 로 유지하고 IPv6는 default promotion gate 로 남긴다.
- D120 — RIO preferred/default selection 은 base factory 가 아니라 host composition 책임으로 두고 sample broker server 에 optional transport selector 를 추가한다.
- D119 — RIO UDP gate 이후에도 base `TransportFactory.CreateDefault()`는 SAEA로 유지하고 preferred RIO는 composition layer policy 로 둔다.
- D118 — RIO UDP bounded receive window 는 open-loop delivery hard gate 를 닫은 기준선으로 수락한다.
- D117 — RIO UDP open-loop delivery loss 는 receive payload registration reuse 가 아니라 bounded receive slot window 로 먼저 다룬다.
- D116 — RIO UDP IOCP/RIONotify wait 는 p99 wake tail 을 해소했지만 open-loop delivery loss 는 receive-side 후속으로 남긴다.
- D115 — RIO UDP residual p99 tail/loss 는 receive depth 확대 전에 UDP CQ IOCP/RIONotify wait parity 로 먼저 검증한다.
- D114 — RIO UDP receive window 는 close-safe one-deep pre-post 로 전환하고, receive native resource 는 receive loop drain 뒤 닫는다.
- D113 — RIO UDP receive completion 은 handler dispatch 전 native receive registration 을 해제하고 8192B block 으로 SAEA UDP envelope 와 맞춘다.
- D112 — UDP benchmark artifact 는 기존 benchmark command 에 protocol selector 를 추가해 수집한다.
- D110 — RIO UDP parity 이후에도 default backend 승격은 보류하고 contract matrix 를 먼저 보강한다.
- D109 — RIO UDP backend 는 TCP resource 를 재사용하지 않고 UDP endpoint owner 로 설계한다.
- D108 — RIO는 아직 default backend 로 승격하지 않고, TCP/UDP parity readiness gate 를 먼저 닫는다.
- D107 — RIO payload registration cache 는 connection resource bounded cache 로 먼저 구현한다.
- D106 — RIO registered buffer reuse 는 receive/length-prefix 를 먼저 처리하고 payload cache 는 분리한다.
- D105 — RIO IOCP notification wait 는 p99 tail 을 해소했으며 다음 최적화는 buffer registration 재사용으로 분리한다.
- D104 — RIO notification wait 는 CQ별 event 가 아니라 shared IOCP pump 로 설계한다.
- D103 — RIO p99 tail 제거는 polling budget 확대가 아니라 IOCP/RIONotify completion wait 설계로 진행한다.
- D102 — RIO completion wake latency 는 bounded yield polling 으로 먼저 줄이고 IOCP/RIONotify 는 후속으로 둔다.
- D101 — SAEA/RIO benchmark 비교는 benchmark 전용 backend selector 로 수행하고 default factory 는 유지한다.
- D100 — RIO TCP drop-oldest ownership 은 shared `TransportConnection` 계약 테스트를 기준으로 검증하고 live saturation 테스트는 보류한다.
- D099 — RIO TCP accept 는 registered I/O accept socket 을 미리 제공해 request queue 생성 가능성을 보장한다.
- D098 — RIO TCP pump 전에 실제 `WSAIoctl` 기반 function table loader 를 먼저 완료한다.
- D097 — Phase 5 Windows RIO backend 는 TCP-first 로 설계하되 capability probe/native wrapper 를 첫 task 로 분리한다.
- D096 — 첫 CI repository baseline 채택 이후에도 latency/warning gate 는 승격하지 않고 Phase 5 RIO 설계로 넘어간다.
- D095 — CI artifact 는 checklist 통과 후 raw report 를 repository baseline 구조로 수동 채택한다.
- D094 — CI artifact workflow 는 `workflow_dispatch`와 제한된 `push` to `master` path trigger 로 운영한다.
- D093 — CI artifact-only manual run 2회만으로는 gate/trigger 를 승격하지 않고 다음 단위는 trigger policy 설계로 둔다.
- D092 — CI benchmark workflow actions 는 Node 24 런타임을 명시한 최신 major/minor 로 갱신해 runner deprecation annotation 을 제거한다.
- D091 — CI benchmark workflow artifact upload name carries GitHub run identity, while the uploaded directory keeps history-compatible date/session layout.
- D090 — CI benchmark 는 latency hard gate 가 아니라 artifact-only 단계로 시작하고 local baseline 과 분리한다.
- D089 — explicit runner 2-date-root/6-session evidence 로는 latency hard gate 를 승격하지 않고 다음 단위는 CI artifact-only benchmark 정책 설계로 둔다.
- D088 — `local-win-x64-01/2026-06-25/session-03`로 두 번째 explicit runner 3-session date root 를 완성했고 gate 승격은 다음 재평가 단위로 분리한다.
- D087 — `local-win-x64-01/2026-06-25/session-02`는 두 번째 explicit runner date root 의 2-session 표본이며 gate 승격은 계속 보류한다.
- D086 — `local-win-x64-01/2026-06-25/session-01`은 두 번째 explicit runner date root 시작 표본이며 gate 승격은 계속 보류한다.
- D085 — explicit runner 3-session 이후 다음 Phase 4 단위는 같은 runner 의 다음 date root session-01 수집으로 둔다.
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
- 첫 CI repository baseline 은 `ci-windows-x64-01/2026-06-25/session-01` 1개 session 이며,
  hard-passed true, warning-count 0, comparison-compatible true 다(D096).
  이는 CI artifact chain 과 D095 수동 채택 절차가 동작함을 보여주지만, date root 1개/session 1개뿐이라
  latency hard gate 또는 warning-as-failure 승격 근거로 쓰지 않는다.
  CI runner evidence 는 자연스러운 push-triggered run 이 더 쌓일 때 수동 채택 여부를 다시 판단하고,
  현재 실행 가능한 다음 큰 흐름은 Phase 5 Windows RIO backend 설계로 둔다.
- Phase 5 Windows RIO backend 는 D097 기준으로 TCP-first 로 설계한다.
  단, 첫 구현 task 는 바로 TCP pump 가 아니라 RIO project skeleton, Windows capability probe,
  native function table wrapper 로 분리한다. 기본 `TransportFactory.CreateDefault()`는 SAEA를 유지하고,
  RIO는 명시 opt-in/test path 로 먼저 검증한다. UDP RIO, batching, automatic default backend selection 은 후속이다.
- RIO TCP accept 경로는 D099 기준으로 OS가 일반 accepted socket 을 만들게 두지 않는다.
  `RIOCreateRequestQueue`는 `WSA_FLAG_REGISTERED_IO` socket 에만 붙으므로, listener 는 accept 대상 socket 을
  `RioNative.CreateTcpSocket()`으로 먼저 만들고 `AcceptAsync(acceptSocket, cancellationToken)`에 넘긴다.
  이 방식으로 accepted connection 도 client connection 과 같은 RIO RQ/CQ pump 에 연결한다.
- RIO TCP drop-oldest ownership 은 D100 기준으로 live loopback saturation 테스트를 추가하지 않는다.
  RIO는 `TransportBase.CreateConnection()`이 만든 `TransportConnection` pending queue 를 그대로 사용하며,
  drop-oldest, in-flight release, close drain, transport-wide diagnostics callback 은
  `tests/Hps.Transport.Tests/Runtime/TransportSendQueueTests.cs`의 공통 runtime 계약 테스트가 검증한다.
  실제 RIO socket pump 는 OS send drain 속도에 의존하므로 queue saturation 을 안정적으로 재현하기 어렵고,
  brittle live test 는 기본 factory 승격 근거로 쓰지 않는다.
- RIO UDP native/endpoint/receive/send/diagnostics parity 이후에도 D110 기준으로 `TransportFactory.CreateDefault()`는
  계속 `SaeaTransport`를 반환한다. RIO는 opt-in/test/benchmark backend 로 유지하고,
  다음 작업은 default promotion 이 아니라 RIO/SAEA backend contract matrix 보강이다.
  default 승격은 shared contract matrix, RIO UDP benchmark artifact, fallback/default selection policy,
  IPv6 지원 여부 판단 이후 별도 결정으로만 재평가한다.
- RIO UDP receive window 는 D114 기준으로 close-safe one-deep pre-post 를 사용한다.
  handler dispatch 전에 다음 `RIOReceiveEx`를 하나 먼저 post 하되, handler 병렬 호출과 configurable receive depth 는
  아직 도입하지 않는다. `Close()`는 shutdown request 만 수행하고, receive CQ/address/native receive resource 는
  receive loop drain 이후 닫는다.
  2026-06-26 session-02 scratch benchmark 에서 closed-loop delivery 는 통과했지만 open-loop 는
  sent 3000 / received 2409 / actual-rate 85.7 Hz / p99 약 16.7 ms 로 hard gate 실패 상태이므로,
  성능 목표 달성은 별도 residual loss/tail 분석 이후에만 주장한다.
  burst absorption 이 필요해지면 bounded receive prefetch 를 별도 설계로 승격한다.
- RIO UDP residual p99 tail/loss 는 D115 기준으로 receive depth 확대 전에 UDP CQ IOCP/RIONotify wait parity 를 먼저 검증한다.
  현재 UDP wait 는 bounded `Task.Yield()` 이후 `Task.Delay(1)` fallback 을 쓰고, 16.7 ms p99 tail 은 Windows timer quantum 과 맞는다.
  TCP RIO는 이미 CQ notification pointer + `RIONotify` + IOCP signal wait 를 쓰므로,
  UDP도 같은 wait pattern 을 적용해 hot path 의 delay fallback 을 제거하는 것이 다음 최소 구현 후보다.
- UDP benchmark artifact 는 D112 기준으로 기존 `--smoke`, `--load`, `--load-open-loop`, `--baseline-suite`
  실행 명령에 `--protocol <tcp|udp>` selector 를 추가해 수집한다. 기본값은 `tcp`라서 기존 TCP benchmark command 는
  그대로 유지된다. UDP raw report 는 기존 schema 를 재사용하고 `benchmark-profile`/`scenario` 값을
  `udp-loopback-...` 계열로 채워 TCP/RIO/SAEA 결과와 구분한다. 첫 RIO UDP evidence 는 repository baseline 이 아니라
  `artifacts/benchmarks/rio-udp/...` scratch 영역에 수집한다.
- SAEA/RIO benchmark 비교는 D101 기준으로 benchmark 내부 `--backend <saea|rio>` selector 로 수행한다.
  `TransportFactory.CreateDefault()`는 계속 SAEA를 반환하며, benchmark runner 만 명시적으로 `RioTransport`를 생성한다.
  raw report schema 는 유지하고 `benchmark-profile`, `transport-backend`, `scenario` 값을 backend 별로 다르게 채워
  summary/history comparison key 가 SAEA/RIO artifact 혼합을 감지하게 한다.
- RIO completion wake latency 는 D102 기준으로 `Task.Delay(1)` 고정 polling 을 바로 IOCP/RIONotify 로 대체하지 않고,
  먼저 bounded `Task.Yield()` polling 후 `Task.Delay(1)` fallback 으로 줄인다.
  SAEA/RIO comparison 에서 RIO p99가 약 16 ms에 몰린 것은 Windows timer granularity 와 맞고,
  이 변경은 current CQ close/dequeue gate 와 close/churn stress 범위를 크게 흔들지 않는 최소 hardening 이다.
- D102 구현 후 RIO load actual-rate 는 64.5 Hz에서 99.8 Hz로 회복했고 p50은 15735 us에서 198.8 us로 줄었다.
  하지만 p99는 16689.0 us로 여전히 16ms대 tail 이 남았으므로, D103 기준으로 polling budget 을 더 키우는 방식은
  기본 방향으로 삼지 않는다. 다음 단계는 `RIONotify`/IOCP 또는 dedicated completion wait 구조 설계다.
- D104 기준으로 RIO notification wait 는 CQ별 event handle 이 아니라 `RioTransport`당 shared IOCP pump 로 설계한다.
  CQ별 event 는 구현은 작지만 connection 당 receive/send handle 과 wait owner 가 늘어 C10K 방향과 충돌한다.
  shared IOCP pump 는 구조 변경이 더 크지만 `RIONotify` completion 을 중앙에서 받아 CQ별 signal 만 깨우므로,
  p99 tail 제거와 후속 batching/shared completion 확장 방향에 맞다.
- D105 기준으로 RIO IOCP notification wait 는 D102에서 남은 16ms대 p99 tail 을 해결한 것으로 본다.
  session-04 scratch benchmark 에서 RIO load p99 는 16689.0 us에서 739.5 us,
  open-loop p99 는 16736.2 us에서 948.8 us 로 내려갔다.
  남은 최적화 후보인 per-operation `RIORegisterBuffer`/`RIODeregisterBuffer` 비용은 이번 completion wait 단위와 분리한다.
- D106 기준으로 RIO registered buffer reuse 는 receive block 과 length-prefix block 의 resource lifetime registration 을 먼저 처리한다.
  payload `RefCountedBuffer` registration cache 는 pool/array/native provider lifetime 과 fan-out ownership 경계가 얽히므로
  별도 설계/테스트 단위로 분리한다.
- D107 기준으로 payload registration cache 는 transport-wide shared cache 가 아니라 connection resource bounded cache 로 먼저 구현한다.
  connection-local cache 는 fan-out 중복 제거 폭은 작지만 close/dispose owner 가 명확하고,
  outstanding send 중 deregister 하지 않는 lease/dispose 규칙을 작은 단위로 검증할 수 있다.
  transport-wide shared cache 는 fan-out evidence 가 더 쌓인 뒤 별도 설계로 승격한다.
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
- explicit runner baseline 은 현재 `local-win-x64-01/2026-06-24`,
  `local-win-x64-01/2026-06-25`, `local-win-x64-01/2026-06-29` 세 date root 를 가지며,
  각 date root 는 3-session reference 다(D123).
  runner root history 는 9-session, hard-passed true, warning-count 0, comparison-compatible true 다.
  현재 explicit runner envelope 는 load p99 max 935.6 us, open-loop p99 max 1077.4 us 이며,
  hard gate, warning, comparison mismatch 는 없다.
  D124 기준으로 이 envelope 는 runner-local 수동 리뷰 기준으로 채택하지만, warning-as-failure 와 CI latency hard gate 는
  계속 보류한다. 현재 warning threshold 는 runner/profile scoped 가 아닌 전역 상수이므로,
  다음 판단은 D125의 별도 envelope comparison artifact 로 분리한다.
- runner/profile scoped 판단은 D125 기준으로 기존 `warning-count`를 바꾸지 않고 별도 envelope comparison artifact 로 만든다.
  새 command 는 reference `history.json`과 candidate `summary.json` 또는 `history.json`을 읽어
  `envelope-compatible`, `envelope-signal-count`, kind별 reference/limit/candidate 값을 기록한다.
  이 신호는 초기에는 process failure, CI failure, warning-as-failure 로 승격하지 않는다.
- D127/D130 원격 검증은 push run `28350456434`로 완료됐다.
  이 run 은 raw report 6개, summary/history JSON/Markdown, envelope JSON/Markdown을 모두 업로드했고 workflow 도 성공했다.
  업로드 artifact 의 envelope 는 이전 1-session CI reference 대비 p99 upper-bound signal 2개를 기록했지만,
  D125/D127 기준 report-only 이므로 CI failure 나 채택 차단 조건으로 보지 않는다.
  D131 기준으로 raw report 6개를 `ci-windows-x64-01/2026-06-29/session-01`에 수동 채택했고,
  runner root history 는 2-session, hard-passed true, warning-count 0, comparison-compatible true 다.
- D132 기준으로 다음 실행 후보는 CI gate 승격, RIO default promotion, full IPv6, server diagnostics API가 아니라
  Phase 6 Linux io_uring boundary 설계와 첫 구현 계획이다.
  현재 Windows 환경에서 Linux native integration 을 바로 검증할 수 없으므로, 첫 구현 후보는
  `Hps.Transport.IoUring` skeleton, capability probe, non-Linux unsupported boundary, default SAEA 유지 regression 으로 제한한다.
- D137 기준으로 io_uring TCP-first pump 구현은 shared `IoUringQueue`/`IoUringCompletionLoop`,
  reusable operation context, 공통 `TransportConnection` send queue 를 재사용하는 boundary 로 수락한다.
  Windows 환경에서는 shape test 와 Linux-gated early-return 까지만 검증됐으므로,
  실제 Linux available host receive/send loopback 은 `TODOS.md` Deferred Backlog 의 환경 의존 검증으로 남긴다.
- D138 기준으로 io_uring 후속 구현은 UDP pump 나 fixed-buffer/zero-copy 최적화가 아니라
  Linux contract evidence gate 를 먼저 둔다. 새 gate 는 workflow_dispatch Linux workflow 와 capability evidence test 로
  TCP pump native syscall 검증 공백을 줄이며, capability unavailable 은 failure 가 아니라 evidence 상태로 취급한다.
- D133 기준으로 Phase 6 io_uring 첫 구현은 `Hps.Transport.IoUring` source/test project, `IoUringCapabilityProbe`,
  `IoUringTransport` lifecycle shell, non-Linux unsupported boundary 까지만 수락한다.
  native `io_uring_setup`/`io_uring_enter`, SQ/CQ mmap, fixed buffer registration, TCP/UDP pump 는 후속 native wrapper
  shape 설계와 별도 TDD task 로 분리한다.
- D134 기준으로 io_uring native wrapper 는 `IoUringNative` syscall adapter, `IoUringQueue` fd/mmap owner,
  `IoUringRegisteredBufferSet` fixed buffer registration owner 로 나눈다.
  `IoUringTransport`는 raw syscall, mmap pointer, registration lifetime 을 직접 알지 않으며,
  첫 구현 계획은 real Linux setup probe 까지만 다루고 TCP/UDP pump 는 후속으로 둔다.
- CI benchmark 는 D090 기준으로 artifact-only 단계로 시작한다. 권장 CI runner id 는 `ci-windows-x64-01`,
  runner kind 는 `ci`이며, 매 실행 artifact 는 `artifacts/benchmarks/runners/<ci-runner-id>/...` 같은 CI artifact
  영역에 둔다. `docs/benchmarks/baselines/runners/<runner-id>/...`는 사람이 검토해 repository baseline 으로
  채택한 결과만 보존한다. CI exit code 는 build/test, report write/usage failure, delivery/drop/leak hard gate 실패만
  반영하고, latency/HWM/warning 은 report-only 로 둔다.
- CI workflow 구현에서는 GitHub `run_id`/`run_attempt`를 upload artifact 이름에 넣고, 업로드되는 디렉터리 내부는
  `artifacts/benchmarks/runners/<ci-runner-id>/<yyyy-mm-dd>/session-01/` 구조를 유지한다(D091).
  현재 `BaselineHistoryReader`가 date root 와 `session-NN` children 만 history source 로 읽기 때문에,
  GitHub run id 를 session directory 이름으로 쓰면 `history.json` 생성 경로와 충돌한다.
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
