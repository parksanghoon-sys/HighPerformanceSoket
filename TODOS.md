# TODOS.md

## Current TODOs

- 현재 Codex가 자동으로 이어서 실행할 항목은 없다.
  - `BrokerPublisher`로 Broker publish fan-out 소유권 경계를 완료했다.
  - `BrokerPublisher`가 payload range 를 추가 복사 없이 fan-out 할 수 있게 됐다.
  - `SubscriptionTable.UnsubscribeAll(IConnection)`으로 Broker 라우팅 테이블의 connection-wide cleanup API를 완료했다.
  - `BrokerTcpFrameHandler`가 TCP command decode 결과를 Broker subscribe/publish/close cleanup 으로 연결한다.
  - `BrokerServer` 최소 TCP host wiring 으로 Transport receive handler 등록, listen, accept loop, stop 정리를 완료했다.
  - `BrokerServer + SaeaTransport` 실제 TCP command loopback 통합 테스트로 subscriber/publisher socket 경로를 검증했다.
  - TCP `TransportConnection` pending send queue 에 capacity 16 drop-oldest backpressure 와 evict-release 를 적용했다.
  - UDP `SaeaUdpEndpoint` pending send queue 에도 capacity 16 drop-oldest backpressure 와 evict-release 를 적용했다.
  - TCP/UDP drop-oldest 경로의 내부 `DroppedPendingSendCount` counter 를 추가했다.
  - `ITransportDiagnostics`와 `TransportDiagnosticsSnapshot`으로 Transport 수명 누적 drop snapshot 을 public 으로 읽을 수 있게 했다.
  - `BrokerServer + SaeaTransport` 실제 TCP command 경로에서 subscriber 2명 fan-out 통합 테스트를 추가했다.
  - malformed TCP command 로 Broker 가 직접 connection 을 닫는 경로에서도 connection-wide subscription cleanup 을 보장했다.
  - UDP datagram handler 예외가 receive loop task fault 로 숨지 않고 endpoint close notification 으로 수렴하도록 보강했다.
  - TCP receive handler 예외도 connection close notification 으로 수렴하도록 보강해 UDP와 수명 정책을 맞췄다.
  - `.claude/review/` 검토 의견의 현재 조치 현황을 문서로 남겼다.
  - D013 리뷰 게이트에 따라 다음 구현은 사용자 검토 후 별도 단위로 진행한다.
  - TCP wire protocol 기반 publisher/subscriber sample client 를 추가했다.
  - 수동 fan-out 확인을 위한 broker server console sample 을 추가했다.
  - D010 TCP frame assembler 랜덤 적대적 fuzz 를 영구 회귀 테스트로 추가했다.
  - Phase 4 `Hps.Benchmarks` 프로젝트와 4096B×100Hz 기준 목표 출력을 추가했다.
  - Phase 4 TCP loopback smoke runner 로 sent/received/drop/leak/latency summary 계측 경계를 검증했다.
  - Phase 4 TCP loopback load runner 로 4096B×100Hz×30초 stdout gate 를 추가했다.
  - `overall-state-2026-06-15.md`의 closed-loop 한계 지적을 상태 문서와 후속 backlog 에 반영했다.
  - Phase 4 open-loop TCP load/backpressure benchmark 를 추가했다.
  - 다음 후보: 사용자 검토 후 Deferred Backlog 를 다시 평가한다.

## Deferred Backlog

- [ ] `P1_SOON` EndpointId 와 endpoint snapshot 최소 계약을 설계/구현한다.
  - 무엇이 남았는지: 현재 broker subscription 은 `IConnection` 중심이며, Interface Server 가 요구하는 안정적인 endpoint identity,
    transport kind, endpoint state, endpoint-level diagnostics 모델이 없다.
  - 왜 defer 되었는지: subscription value 를 바꾸면 Broker/Server/Protocol 테스트 범위가 넓어진다. send queue high-watermark
    구현으로 transport kind 별 backlog 관측은 확보됐으므로, 이제 다음 P1 후보로 올릴 수 있다.
  - objective: TCP connection 과 UDP remote endpoint 를 같은 logical endpoint 모델로 관찰하고, 이후 TCP/UDP fan-out 정책을 같은 개념으로 다룰 수 있게 한다.
  - relevant context: `docs/superpowers/specs/2026-06-16-interface-server-endpoint-model-design.md`, `SubscriptionTable`,
    `BrokerPublisher`, `BrokerTcpFrameHandler`, `IConnection`, `IUdpEndpoint`.
  - 관련 파일/범위: `src/Hps.Broker/`, `src/Hps.Server/`, `src/Hps.Transport/Abstractions/`, 관련 테스트 프로젝트.
  - 현재 상태: TCP broker 는 동작하지만 endpoint identity 가 없어 reconnect, UDP endpoint, endpoint별 상태 관측을 자연스럽게 표현하지 못한다.
    선행 HWM snapshot 은 endpoint 식별 없이 TCP/UDP transport kind 별 max 만 제공한다.
  - known blockers/open questions: endpoint identity 를 broker 내부 transient id 로 시작할지, 외부 subscriber 가 제공하는 stable id 를 요구할지 결정해야 한다.
  - next step: endpoint snapshot 이 실제로 담아야 할 최소 필드를 Red 테스트로 고정한다.

- [ ] `P2_LATER` Phase 4 benchmark latency SLO gate 여부를 결정한다.
  - 무엇이 남았는지: `--smoke`, `--load`, `--load-open-loop` 결과를 JSON report 로 저장하는 경로는 D052와 이번 완료 항목으로 닫혔다.
    아직 p50/p99 또는 p99 증가율을 명시적인 실패 조건으로 승격할지 결정하지 않았다.
  - 왜 defer 되었는지: latency threshold 는 개발 PC, CI, 백그라운드 부하, OS scheduling 상태에 민감하다. 또한 Interface Server 목표에서는
    먼저 endpoint/send-side backlog 를 설명할 수 있어야 latency SLO 실패 원인을 분해할 수 있다.
  - objective: `tcp-loopback-saea-baseline` closed-loop/open-loop 결과에서 p50/p99, first-half/second-half p99,
    p99-latency-growth-ratio 를 어떤 기준으로 합격/실패 판정할지 정한다.
  - relevant context: DECISIONS D050/D051/D052, `.claude/review/overall-state-2026-06-15.md` P1,
    `tests/Hps.Benchmarks/TcpLoopbackRunResult.cs`, `tests/Hps.Benchmarks/TcpLoopbackReportWriter.cs`,
    `tests/Hps.Benchmarks/TcpLoopbackScenarioRunner.cs`.
  - 관련 파일/범위: `tests/Hps.Benchmarks/`, 필요 시 CI script 또는 benchmark output 문서.
  - 현재 상태: runner pass/fail 은 sent==planned==received, dropped==0, payload-errors==0, pool-rented==0 만 본다.
    latency 값은 stdout 과 JSON report 에 기록되지만 실패 조건은 아니다.
  - known blockers/open questions: 개발/CI 환경별 변동을 감안한 threshold 를 고정할지, baseline 대비 상대 변화율만 볼지 결정해야 한다.
  - next step: 최근 `--load`/`--load-open-loop --report` 결과와 send queue high-watermark 값을 함께 보고
    절대 threshold, p99 증가율 threshold, 또는 관측-only 유지 중 하나를 선택한다.

- [ ] `P2_LATER` 백프레셔 기본 정책을 PLAN/AGENTS 설계 의도와 재정렬한다.
  - 무엇이 남았는지: PLAN Phase 3은 기본 정책을 "느린 소비자 끊기", 옵션을 "drop-oldest"로 설명하지만,
    현재 구현은 TCP/UDP pending send queue 모두 capacity 16 drop-oldest 만 제공한다.
  - 왜 defer 되었는지: 현재 코드 경로는 리뷰에서 버그 없음으로 검증됐고, 4096B×100Hz 정상 소비자 기준에서는 trigger 가능성이 낮다.
    정책 변경은 메시지 손실/연결 종료 semantics 를 바꾸므로 benchmark scaffold 와 한 커밋에 섞지 않는다.
  - objective: 설계 기본값을 코드에 맞춰 drop-oldest 로 갱신할지, 아니면 코드에 disconnect 정책과 설정 surface 를 추가할지 결정한다.
  - relevant context: `.claude/review/overall-state-2026-06-15.md` P2, PLAN Phase 3 backpressure,
    DECISIONS D039/D040/D042, `TransportConnection`, `SaeaUdpEndpoint`.
  - 관련 파일/범위: `PLAN.md`, `AGENTS.md`, `DECISIONS.md`, `src/Hps.Transport/Runtime/TransportConnection.cs`,
    `src/Hps.Transport/Saea/SaeaUdpEndpoint.cs`, transport tests.
  - 현재 상태: drop counter 와 diagnostics snapshot 은 존재하지만 policy 선택 API 와 disconnect 기본 정책은 없다.
  - known blockers/open questions: v1 기본 정책을 안정성 중심 disconnect 로 둘지, 최신성 중심 drop-oldest 로 둘지 사용자 결정이 필요하다.
  - next step: Phase 4 open-loop 결과를 검토한 뒤 drop-oldest 만으로 충분한지, disconnect 정책과 설정 surface 가 필요한지 설계 결정을 요청한다.

- [ ] `P2_LATER` UDP pub/sub 를 v1 범위에 포함할지 결정한다.
  - 무엇이 남았는지: UDP transport bind/send/recv/echo 기준선은 있으나, UDP command decode, Broker fan-out 연결,
    UDP end-to-end pub/sub 테스트는 없다.
  - 왜 defer 되었는지: Phase 1~3 리뷰는 TCP broker 완성을 인정했고, UDP broker 는 별도 범위 결정이 필요한 항목으로 남았다.
    TCP benchmark 진입과 UDP feature 결선을 같은 단위에 섞으면 검증 축이 달라진다.
  - objective: v1을 TCP broker 로 고정할지, UDP `1 datagram = 1 메시지` pub/sub 도 Phase 3/4 범위에 포함할지 결정한다.
  - relevant context: `.claude/review/overall-state-2026-06-15.md` P3, AGENTS 프레이밍 규칙, D024/D046,
    `ITransportDatagramHandler`, `SaeaTransport` UDP tests.
  - 관련 파일/범위: `src/Hps.Protocol/`, `src/Hps.Broker/`, `src/Hps.Server/`, `tests/Hps.Transport.Tests/`,
    신규 UDP broker tests.
  - 현재 상태: UDP datagram ownership 과 endpoint send queue 는 검증됐지만 Broker command path 는 TCP 전용이다.
  - known blockers/open questions: UDP command wire format 을 TCP text command 와 동일하게 둘지, datagram payload 자체를 publish message 로 볼지 결정해야 한다.
  - next step: 사용자에게 v1 UDP 포함 여부를 확인한 뒤 포함이면 별도 설계/테스트 단위로 승격한다.

- [ ] `P2_LATER` drop log/sampling 과 Server convenience diagnostics API 필요성을 검토한다.
  - 무엇이 남았는지: `ITransportDiagnostics.GetDiagnosticsSnapshot()`으로 Transport-level public 누적 metric 은 제공하지만,
    drop 발생 시 log 를 남기거나 `BrokerServer`가 Transport diagnostics 를 직접 감싸는 convenience API 는 없다.
  - 왜 defer 되었는지: 이번 단위는 hot path 비용이 낮은 누적 snapshot 까지만 닫았다. drop 마다 log 를 남기면 고빈도 과부하에서
    비용과 노이즈가 커질 수 있고, Server convenience API 는 실제 운영 host surface 가 더 구체화된 뒤 결정해도 된다.
  - objective: 운영자가 snapshot pull 방식만으로 충분한지, 아니면 sampling/threshold log 또는 Server-level aggregate accessor 가 필요한지 결정한다.
  - relevant context: DECISIONS D041/D042, `src/Hps.Transport/Abstractions/ITransportDiagnostics.cs`,
    `src/Hps.Transport/Abstractions/TransportDiagnosticsSnapshot.cs`, `src/Hps.Server/BrokerServer.cs`.
  - 관련 파일/범위: `src/Hps.Transport/`, `src/Hps.Server/`, 테스트 프로젝트 전반.
  - 현재 상태: Transport 수명 누적 TCP/UDP drop snapshot 은 public 으로 읽을 수 있고 reset API는 없다.
  - known blockers/open questions: log 는 drop 마다 남길지 sampling/threshold 기반으로 둘지, Server 는 nullable snapshot 을 노출할지
    diagnostics capability 를 필수로 요구할지 결정해야 한다.
  - next step: Phase 3 host/samples surface 가 더 구체화된 뒤 pull snapshot 만으로 운영성이 충분한지 먼저 검토한다.

## Completed

- [x] TCP/UDP send queue high-watermark diagnostics 를 public snapshot 과 benchmark report 에 연결했다.
  - 범위: `src/Hps.Transport/Abstractions/TransportDiagnosticsSnapshot.cs`, `src/Hps.Transport/Runtime/TransportBase.cs`,
    `src/Hps.Transport/Runtime/TransportConnection.cs`, `src/Hps.Transport/Saea/SaeaUdpEndpoint.cs`,
    `tests/Hps.Transport.Tests/`, `tests/Hps.Benchmarks/`.
  - 결과: Transport lifetime 기준 TCP/UDP kind 별 pending send queue high-watermark 를 기록하고 stdout/JSON report 에
    `tcp-pending-send-queue-high-watermark`, `udp-pending-send-queue-high-watermark` 로 남긴다.
  - 비고: high-watermark 는 endpoint identity 가 아니라 TCP/UDP transport kind 별 max pending depth 이며,
    capacity 16에서 포화되므로 drop count 와 함께 해석한다.
  - 근거: `22591b5`에서 high-watermark tracking 을 추가했고, `db8984f`에서 benchmark stdout/JSON report 연결을 추가했다.
  - 검증: 실제 구현 존재는 `rg`로 확인했고, 이번 문서 동기화 단위는 `git diff --check`로 검증한다.

- [x] Interface Server endpoint model 설계를 문서화했다.
  - 범위: `docs/superpowers/specs/2026-06-16-interface-server-endpoint-model-design.md`, `CURRENT_PLAN.md`,
    `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - 결과: 최종 목표를 외부 ingress 를 topic/data type 으로 받아 TCP/UDP endpoint 로 발행하는 Interface Server 로 재정렬했다.
  - 결정: latency SLO gate 보다 send-side endpoint 관측성을 먼저 보강하고, 다음 구현 후보를 TCP/UDP send queue high-watermark diagnostics 로 잡았다.
  - 검증: 문서 전용 변경이므로 build/test 는 실행하지 않고 state 문서 연결 확인과 `git diff --check`로 검증한다.

- [x] Phase 4 benchmark JSON report persistence 를 추가했다.
  - 범위: `tests/Hps.Benchmarks`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `--smoke --report <path>`는 기존 구현에서 `Program.Main`의 `args.Length == 1` 분기를 타지 못하고
    BenchmarkDotNet fallback 으로 흘러가 `smoke`/`report` unknown option 이 출력됐으며 report 파일도 생성되지 않았다.
  - 구현: `Program`의 benchmark runner CLI parser 를 다중 인자 옵션 구조로 확장하고, `--smoke`, `--load`, `--load-open-loop`에
    선택적 `--report <path>`를 추가했다.
  - 구현: `TcpLoopbackReportWriter`를 추가해 세 runner 가 같은 `TcpLoopbackRunResult` 기반 JSON schema 를 항상 기록하게 했다.
    기존 파일은 덮어쓰고 상위 디렉터리는 자동 생성한다.
  - 후속: latency SLO threshold, Markdown report, report history, queue depth diagnostics 는 별도 backlog 로 유지한다.

- [x] Phase 4 open-loop TCP load/backpressure benchmark 를 추가했다.
  - 범위: `tests/Hps.Benchmarks`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `BenchmarkTargets`.
  - Red: `--load-open-loop` 출력 검증은 기존 구현에서 BenchmarkDotNet unknown option 으로 처리되어 `open-loop-result:`가 출력되지 않아 실패했다.
  - 구현: `Program --load-open-loop`와 `TcpLoopbackScenarioRunner.RunOpenLoopAsync()`를 추가했다.
  - 구현: open-loop runner 는 subscriber receive task 를 먼저 시작하고, publisher loop 는 subscriber 수신 완료를 기다리지 않고
    100Hz schedule 에 맞춰 4096B payload 3000개를 전송한다.
  - 구현: payload 내부에 timestamp 와 sequence 를 넣어 수신 순서/무결성을 `payload-errors`로 관측하고,
    first-half/second-half p99 와 p99 growth ratio 로 지연 증가 추세를 출력한다.
  - 검증: focused open-loop 는 `open-loop-result: pass`, planned/sent/received 3000, dropped 0, payload-errors 0,
    pool-rented 0, actual-rate-hz 99.9, p50 221.6us, p99 867.6us, first-half p99 873.3us,
    second-half p99 850.3us, p99 growth ratio 0.97로 통과했다. closed-loop `--load`, `--smoke`, `--target` 회귀도 통과했다.
    solution build 는 경고 0/오류 0으로 통과했고, 솔루션 전체 테스트는 통과 106, 실패 0, 건너뜀 0으로 통과했다.
    `git diff --check`는 whitespace 오류 없이 통과했다.

- [x] `overall-state-2026-06-15.md`의 closed-loop benchmark 한계 검토를 상태 문서에 반영했다.
  - 범위: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - 검토 반영: `--load`는 4096B×100Hz×30초 closed-loop baseline 으로 유지하되, subscriber 수신 뒤 다음 publish 로 넘어가므로
    queue depth 증가나 drop-oldest/backpressure 경로를 검증하지 않는다고 명시했다.
  - 후속: open-loop TCP load/backpressure benchmark 를 P1 Deferred Backlog 로 추가했다. 이 후속은 publish loop 와 receive loop 를
    분리해 queue backlog, dropped count, latency 증가 추세를 관측하는 별도 작업 단위다.
  - 검증: 문서 전용 변경이므로 build/test 는 실행하지 않고 `rg` 상태 문서 연결 확인과 `git diff --check`로 검증한다.

- [x] Phase 4 TCP loopback load runner 를 추가했다.
  - 범위: `tests/Hps.Benchmarks`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - Red: `--load` 출력 검증은 기존 구현에서 BenchmarkDotNet unknown option 으로 처리되어 `load-result:`가 출력되지 않아 실패했다.
  - 구현: `Program --load`와 `TcpLoopbackScenarioRunner.RunLoadAsync()`를 추가하고, 기존 smoke runner 를 같은 scenario runner 로 통합했다.
  - 구현: load 는 실제 `BrokerServer + SaeaTransport` loopback 과 TCP subscriber/publisher socket 을 사용해
    4096B payload 3000개를 100Hz pacing 으로 약 30초 전송한다.
  - 구현: pass/fail 은 sent==planned==received, dropped 0, pool-rented 0으로 판정하고, actual-rate/p50/p99 latency 는
    stdout summary 로 출력한다. latency threshold 와 파일 report 는 후속 항목으로 남겼다.
  - 검증: focused load 는 `load-result: pass`, planned/sent/received 3000, dropped 0, pool-rented 0,
    actual-rate-hz 99.9, p50 205.9us, p99 799.0us 로 통과했다. benchmark project build 와 solution build 는
    경고 0/오류 0으로 통과했고, 솔루션 전체 테스트는 통과 106, 실패 0, 건너뜀 0으로 통과했다.

- [x] Phase 4 TCP loopback smoke runner 를 추가했다.
  - 범위: `tests/Hps.Benchmarks`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - Red: `--smoke` 출력 검증은 기존 구현에서 BenchmarkDotNet unknown option 으로 처리되어 `smoke-result:`가 출력되지 않아 실패했다.
    최초 시도는 sandbox 네트워크 restore 차단으로 실패해 권한 요청 후 Red 를 재확인했다.
  - 구현: `Program --smoke`, `TcpLoopbackSmokeRunner`, `TcpLoopbackSmokeResult`를 추가했다.
  - 구현: smoke 는 실제 `BrokerServer + SaeaTransport` loopback 과 TCP subscriber/publisher socket 을 사용해
    4096B payload 8개를 보내고 수신 원문, sent/received, drop count, pool rented count, p50/p99 latency sample 을 검증한다.
  - 검증: focused smoke 는 `smoke-result: pass`, sent 8, received 8, dropped 0, pool-rented 0으로 통과했다.
    benchmark project build 경고 0/오류 0, solution build 경고 0/오류 0, 솔루션 전체 테스트 통과 106, 실패 0,
    건너뜀 0. `git diff --check` 통과. 병렬 build/test 시 obj lock 충돌이 있어 직렬 재실행으로 확인했다.

- [x] Phase 4 benchmark scaffold 와 4096B×100Hz 기준 목표 출력을 추가했다.
  - 범위: `tests/Hps.Benchmarks`, `HighPerformanceSocket.slnx`, `.gitignore`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `dotnet build tests\Hps.Benchmarks\Hps.Benchmarks.csproj`가 프로젝트 파일 부재로 실패했다.
  - 구현: BenchmarkDotNet 기반 console project 를 추가하고, `BenchmarkTargets`에 SAEA TCP loopback baseline 목표값을 고정했다.
  - 구현: `--target` 명령은 4096B, 100Hz, 30초, planned 3000 messages, dropped 0/누수 0/p50/p99 report gate 를 출력한다.
  - 구현: 첫 microbench 로 `PinnedBlockMemoryPoolBenchmarks`의 `RentCounted + Release`를 추가했다.
  - 구현: BenchmarkDotNet 기본 artifact 폴더는 `.gitignore`에 추가해 임시 측정 산출물이 의도 없이 커밋되지 않게 했다.
  - 검증: benchmark project build 경고 0/오류 0, `--target` 실행 성공, solution build 경고 0/오류 0.
    솔루션 전체 테스트 통과 106, 실패 0, 건너뜀 0. `git diff --check` 통과.

- [x] D010 TCP frame assembler 랜덤 적대적 fuzz 를 영구 회귀 테스트로 추가했다.
  - 범위: `tests/Hps.Protocol.Tests/TcpFrameAssemblerTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 테스트: 고정 seed 4개로 frame 길이와 receive chunk 길이를 바꾸며 header 1바이트 분할, 0바이트 payload,
    max payload, 한 chunk 안의 다중 frame 을 함께 검증한다.
  - 결과: 기존 `TcpFrameAssembler` 구현이 랜덤 적대적 분할 케이스를 즉시 통과해 production code 수정은 없었다.
  - 검증: focused fuzz 테스트 통과 4, Protocol 전체 통과 28, solution build 경고 0/오류 0,
    솔루션 전체 테스트 통과 106, 실패 0, 건너뜀 0. `git diff --check` 통과.

- [x] 수동 fan-out 확인을 위한 broker server console sample 을 추가했다.
  - 범위: `samples/Hps.Sample.BrokerServer`, `HighPerformanceSocket.slnx`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `dotnet build samples\Hps.Sample.BrokerServer\Hps.Sample.BrokerServer.csproj`가 프로젝트 파일 부재로 실패했다.
  - 구현: sample 은 `<host> <port> <max-frame-bytes>` 인자를 받아 `BrokerServer + TransportFactory.CreateDefault()`를 시작하고,
    Ctrl+C 입력 시 `BrokerServer.StopAsync`를 거쳐 정리한다.
  - 결정: D049에 따라 이 sample 은 운영용 daemon 이 아니라 기존 library host 를 조립하는 실행 harness 로 둔다.
  - 검증: sample build 는 Red 프로젝트 파일 부재 실패 뒤 Green 경고 0/오류 0. invalid args smoke 는 사용법 출력과 exit code 2 확인.
    solution build 는 병렬 test 와의 obj lock 충돌 뒤 직렬 재실행 경고 0/오류 0. 솔루션 전체 테스트 통과 102, 실패 0, 건너뜀 0.
    `git diff --check` 통과.

- [x] TCP receive handler 예외가 connection close notification 으로 수렴하도록 보강했다.
  - 범위: `src/Hps.Transport/Saea/SaeaTransport.cs`, `src/Hps.Transport/Abstractions/ITransportReceiveHandler.cs`,
    `tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `ReceivePump_WhenHandlerThrows_ClosesConnectionAndNotifiesHandler`가 `OnConnectionClosed` 미호출로 5초 timeout 실패했다.
  - 구현: `DispatchReceived` 예외를 catch 해 `NotifyConnectionClosed(connection)` 후 receive loop 를 종료하도록 했다.
  - 결정: D048에 따라 TCP handler 예외는 background task fault 가 아니라 connection 수명 종료로 관측된다.
  - 검증: focused 테스트는 Red 실패 1/통과 0 이후 Green 통과 1. Transport 전체 통과 37, solution build 경고 0/오류 0,
    솔루션 전체 테스트 통과 102, 실패 0, 건너뜀 0. `git diff --check` 통과.

- [x] TCP wire protocol 기반 publisher/subscriber sample client 를 추가했다.
  - 범위: `samples/Hps.Sample.Publisher`, `samples/Hps.Sample.Subscriber`, `samples/Shared/SampleTcpFrames.cs`,
    `HighPerformanceSocket.slnx`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `dotnet build samples\Hps.Sample.Publisher\Hps.Sample.Publisher.csproj`와
    `dotnet build samples\Hps.Sample.Subscriber\Hps.Sample.Subscriber.csproj`가 프로젝트 파일 부재로 실패했다.
  - 구현: publisher 는 `PUBLISH <topic> <payload>` frame 을 한 번 전송하고, subscriber 는 `SUBSCRIBE <topic>` frame 전송 뒤
    broker 가 fan-out 하는 raw payload chunk 를 stdout 으로 출력한다.
  - 결정: D047에 따라 샘플 client 는 `Hps.Server` 내부 타입을 참조하지 않고 broker TCP wire protocol 만 사용한다.
  - 검증: publisher/subscriber sample build 와 solution build 는 경고 0, 오류 0으로 통과했다.
    솔루션 전체 테스트 통과 101, 실패 0, 건너뜀 0. `git diff --check` 통과.

- [x] UDP receive backpressure Q1 중 SAEA Transport 내부 prefetch 경계를 회귀 테스트로 고정했다.
  - 범위: `tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - 테스트: `UdpReceive_WhenHandlerIsBlocked_DoesNotPrefetchAdditionalDatagrams`가 첫 datagram handler 를 막은 상태에서
    두 번째 datagram 을 보내도 receive loop 가 추가 `RefCountedBuffer`를 대여하지 않는지 검증한다.
  - 결정: D046에 따라 현재 SAEA UDP receive 기준선에는 별도 receive queue/drop 정책을 추가하지 않는다.
    동기 handler 가 반환될 때까지 다음 `RentCounted()`로 넘어가지 않으므로 Transport 내부 pool 대여 수가 무제한 누적되지 않는다.
  - 남은 범위: handler/Broker 가 datagram ref 를 별도 작업으로 넘기고 즉시 반환하는 경우의 상위 fan-out backpressure 정책은
    UDP Broker publish 경계가 생길 때 다시 결정한다.
  - 검증: focused 테스트는 최초 기대값을 receive loop idle buffer 모델에 맞게 조정한 뒤 통과했다.
    Transport 전체 통과 36, 솔루션 전체 통과 101, 빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] SAEA 기준선의 direct pinned block send/receive 예외를 문서 불변식과 맞췄다.
  - 범위: `AGENTS.md`, `DECISIONS.md`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 구현: `AGENTS.md`의 `BipBuffer` send/recv 큐 원칙은 유지하되, 현재 `SaeaTransport` raw Socket 기준선이
    D023/D024/D045에 따른 계약/수명 검증용 예외임을 명시했다.
  - 구현: `DECISIONS.md` D045로 SAEA 기준선 예외와 향후 RIO/io_uring 또는 명시적 송수신 큐 최적화의
    `BipBuffer` 적용 요구를 분리했다.
  - 검증: `rg` 문서 검색으로 D045/SAEA 예외 연결을 확인했다. `dotnet build HighPerformanceSocket.slnx`는
    경고 0, 오류 0으로 통과했고, `git diff --check`는 whitespace 오류 없이 통과했다.
    이번 단위는 문서 전용 변경이므로 full test 는 실행하지 않았다.

- [x] UDP datagram handler 예외 정책을 endpoint close notification 으로 고정했다.
  - 범위: `src/Hps.Transport/Abstractions/ITransportDatagramHandler.cs`,
    `src/Hps.Transport/Saea/SaeaTransport.cs`, `tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: handler 가 datagram 을 Release 한 뒤 예외를 던지면 close notification 이 오지 않아 timeout 으로 실패하는 것을 확인했다.
  - 구현: UDP receive loop 의 일반 예외 경로를 task fault 대신 `NotifyUdpEndpointClosed`로 수렴시켰다.
  - 구현: datagram 소유권은 handler 호출 시점에 이전된 상태를 유지하므로, handler 예외 후에도 datagram 반환 책임은 handler 에 남는다고 XML doc 에 명시했다.
  - 검증: focused policy 테스트 Green 통과 1, Transport 전체 통과 35, 솔루션 전체 통과 100,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] malformed TCP command 직접 close 경로의 subscription cleanup 누락을 수정했다.
  - 범위: `src/Hps.Broker/BrokerTcpFrameHandler.cs`, `tests/Hps.Broker.Tests/BrokerTcpFrameHandlerTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: 구독된 connection 이 malformed command 를 보낸 뒤 transport close notify 가 없으면 `alpha` topic 에 connection 이 남아
    `Assert.False()`가 실패하는 것을 확인했다.
  - 구현: `BrokerTcpFrameHandler`가 malformed command 또는 내부 오류 때문에 직접 `connection.Close()`를 호출할 때
    `SubscriptionTable.UnsubscribeAll(connection)`을 먼저 수행한다.
  - 검증: focused cleanup 테스트 Green 통과 1, Broker 전체 통과 18, 솔루션 전체 통과 100,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] drop-oldest public diagnostics snapshot 을 구현했다.
  - 범위: `src/Hps.Transport/Abstractions/ITransportDiagnostics.cs`,
    `src/Hps.Transport/Abstractions/TransportDiagnosticsSnapshot.cs`, `src/Hps.Transport/Runtime/TransportBase.cs`,
    `src/Hps.Transport/Runtime/TransportConnection.cs`, `src/Hps.Transport/Saea/SaeaTransport.cs`,
    `src/Hps.Transport/Saea/SaeaUdpEndpoint.cs`, transport tests, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `ITransportDiagnostics`/`TransportDiagnosticsSnapshot` 타입 부재와 diagnostics snapshot 부재가 `Assert.NotNull` 실패 3건으로 확인됐다.
  - 구현: `ITransport` 기본 계약은 넓히지 않고 선택적 `ITransportDiagnostics.GetDiagnosticsSnapshot()` capability 로 노출했다.
  - 구현: `TransportBase`가 TCP/UDP drop-oldest 누적 counter 를 유지하고, connection/endpoint 내부 counter 와 별도로
    close 이후에도 Transport-level snapshot 에 drop 수가 남도록 했다.
  - 검증: focused `TransportDiagnostics` 통과 3, Transport 전체 통과 35, 솔루션 전체 통과 99,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] 다중 subscriber TCP command fan-out 통합 테스트를 추가했다.
  - 범위: `tests/Hps.Server.Tests/BrokerServerTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 테스트: `BrokerServer + SaeaTransport` loopback listener 를 시작하고, raw TCP subscriber socket 2개가 같은 topic 에
    length-prefix `SUBSCRIBE alpha` frame 을 보낸 뒤 publisher socket 1개가 `PUBLISH alpha <payload>`를 보내면 두 subscriber 가
    동일 payload 원문을 받는지 검증했다.
  - 테스트: 공유 `RefCountedBuffer` fan-out 과 send completion 이후 server payload pool 이 `RentedCount==0`으로 돌아오는지 검증했다.
  - 결과: 기존 Server/Transport/Protocol/Broker 구현이 테스트를 즉시 통과해 production code 수정은 없었다.
  - 검증: focused `TcpCommandLoopback_WhenTwoSubscribersShareTopic` 통과 1, Server 전체 통과 5,
    솔루션 전체 통과 96, 빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] drop-oldest 내부 관측성 counter 를 구현했다.
  - 범위: `src/Hps.Transport/Runtime/TransportConnection.cs`, `src/Hps.Transport/Saea/SaeaUdpEndpoint.cs`,
    `tests/Hps.Transport.Tests/Runtime/TransportSendQueueTests.cs`, `tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: TCP/UDP 각각 `DroppedPendingSendCount` property 부재가 `Assert.NotNull` 실패로 확인됐다.
  - 구현: drop-oldest evict 발생 시 `Interlocked.Increment`로 내부 counter 를 증가시키고,
    `Volatile.Read` 기반 internal property 로 읽을 수 있게 했다.
  - 구현: public Transport/Broker/Server metric API 와 log 출력은 추가하지 않았다.
  - 검증: focused `DroppedPendingSendCount` 통과 2, Transport 전체 통과 32, 솔루션 전체 통과 95,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] UDP `SaeaUdpEndpoint` pending send queue backpressure 를 구현했다.
  - 범위: `src/Hps.Transport/Saea/SaeaUdpEndpoint.cs`,
    `tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: capacity 17번째 datagram send 후 pending count 가 17로 남아 실패하는 것을 확인했다.
  - Red: overflow 뒤 publisher guard ref 를 놓고 close 하면 evict 가 없어 `RentedCount==17`로 남는 실패를 확인했다.
  - 구현: endpoint pending queue 기본 capacity 를 16으로 두고, 가득 찬 상태에서 새 datagram 을 수락하면
    가장 오래된 pending datagram 을 evict 한 뒤 Transport 소유 ref 를 Release 한다.
  - 구현: evict 대상 선택과 queue 제거는 `_sendGate` lock 으로 직렬화하고, Release 는 lock 밖에서 수행한다.
  - 검증: focused `UdpSendTo_WhenPendingQueue` 통과 2, Transport 전체 통과 30, 솔루션 전체 통과 93,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] TCP `TransportConnection` pending send queue backpressure 를 구현했다.
  - 범위: `src/Hps.Transport/Runtime/TransportConnection.cs`,
    `tests/Hps.Transport.Tests/Runtime/TransportSendQueueTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: capacity 17번째 send 후 pending count 가 17로 남아 실패하는 것을 확인했다.
  - Red: overflow 뒤 publisher guard ref 를 놓고 close 하면 evict 가 없어 `RentedCount==17`로 남는 실패를 확인했다.
  - 구현: pending queue 기본 capacity 를 16으로 두고, 가득 찬 상태에서 새 send 를 수락하면 가장 오래된 pending 항목을 evict 한 뒤
    Transport 소유 ref 를 Release 한다.
  - 구현: evict 대상 선택과 queue 제거는 connection lock 으로 직렬화하고, Release 는 lock 밖에서 수행한다.
  - 검증: focused `TransportSendQueueTests` 통과 9, Transport 전체 통과 28, 솔루션 전체 통과 91,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] `BrokerServer` 실제 TCP command loopback 통합 테스트를 추가했다.
  - 범위: `tests/Hps.Server.Tests/BrokerServerTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 테스트: `BrokerServer + SaeaTransport` loopback listener 를 시작하고, subscriber raw TCP socket 이 length-prefix
    `SUBSCRIBE alpha`를 보낸 뒤 publisher raw TCP socket 이 `PUBLISH alpha <payload>`를 보내면 subscriber 가 payload 원문을 받는지 검증했다.
  - 테스트: publish frame/send ref 가 모두 반환되어 server payload pool 의 `RentedCount==0`으로 돌아오는지 검증했다.
  - 결과: 기존 Server/Transport/Protocol/Broker 구현이 테스트를 즉시 통과해 production code 수정은 없었다.
  - 검증: focused `TcpCommandLoopback` 통과 1, Server 전체 통과 4, 솔루션 전체 통과 89,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] `Hps.Server` 최소 TCP host wiring 을 구현했다.
  - 범위: `src/Hps.Server/Hps.Server.csproj`, `src/Hps.Server/BrokerServer.cs`,
    `tests/Hps.Server.Tests/Hps.Server.Tests.csproj`, `tests/Hps.Server.Tests/BrokerServerTests.cs`,
    `HighPerformanceSocket.slnx`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `BrokerServer` 타입 부재를 reflection 기반 단언 실패로 확인했다.
  - Red: stub 상태에서 receive handler 등록, Transport start/listen, accept loop 시작, Stop listener/Transport 정리 단언이 실패하는 것을 확인했다.
  - 구현: `BrokerServer`가 `SubscriptionTable`, `BrokerPublisher`, `BrokerTcpFrameHandler`, `TcpFrameReceiveHandler`를 조립하고
    주입된 `ITransport`에 receive handler 를 등록한다.
  - 구현: `StartTcpAsync`는 transport start/listen 후 accept loop 를 시작하고, `StopAsync`/`Dispose`는 accept loop 를 깨운 뒤
    listener 와 Transport 를 정리한다.
  - 검증: focused `BrokerServerTests` 통과 3, 솔루션 전체 통과 88, 빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] Broker TCP frame command handler 를 구현했다.
  - 범위: `src/Hps.Protocol/TcpCommand.cs`, `src/Hps.Protocol/TcpCommandDecoder.cs`,
    `src/Hps.Broker/BrokerTcpFrameHandler.cs`, `src/Hps.Broker/Hps.Broker.csproj`,
    `tests/Hps.Protocol.Tests/TcpCommandDecoderTests.cs`, `tests/Hps.Broker.Tests/BrokerTcpFrameHandlerTests.cs`,
    `tests/Hps.Broker.Tests/Hps.Broker.Tests.csproj`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `TcpCommand.PayloadOffset` 부재를 reflection 기반 단언 실패로 확인했다.
  - Red: `PayloadOffset` 기본값 0 상태에서 `PUBLISH alpha <payload>`의 실제 payload 시작 offset 14 단언 실패를 확인했다.
  - Red: `BrokerTcpFrameHandler` 타입/생성자/`ITcpFrameHandler` 구현 부재를 확인했다.
  - Red: no-op handler 에서 subscribe 등록, publish payload range fan-out, close cleanup, malformed frame close/release 가 실패하는 것을 확인했다.
  - 구현: `BrokerTcpFrameHandler.OnFrame`은 command 를 decode 해 subscribe/publish 로 연결하고, 수락한 frame guard ref 를 항상 Release 한다.
  - 구현: `OnConnectionClosed`는 `SubscriptionTable.UnsubscribeAll`을 호출하며, malformed command 는 frame 을 회수하고 connection 을 닫는다.
  - 검증: focused `TcpCommandDecoderTests` 통과 10, focused `BrokerTcpFrameHandlerTests` 통과 5,
    Protocol 전체 통과 24, Broker 전체 통과 17, 솔루션 전체 통과 85, 빌드 경고 0/오류 0,
    `git diff --check` 통과.

- [x] Broker subscription connection-wide cleanup API 를 구현했다.
  - 범위: `src/Hps.Broker/SubscriptionTable.cs`, `tests/Hps.Broker.Tests/BrokerRoutingTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `SubscriptionTable.UnsubscribeAll(IConnection)` 메서드 부재를 reflection 기반 단언 실패로 확인했다.
  - Red: no-op stub 에서 같은 connection 이 여러 topic 에 남아 제거 수 0으로 실패하는 것을 확인했다.
  - 구현: `UnsubscribeAll`은 모든 topic set 을 열거하며 대상 connection 만 제거하고, D008에 따라 topic entry 자체는 제거하지 않는다.
  - 검증: focused `BrokerRoutingTests` 통과 6, Broker 전체 통과 12, 솔루션 전체 통과 79,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] Broker publish payload range 를 구현했다.
  - 범위: `src/Hps.Broker/BrokerPublisher.cs`, `tests/Hps.Broker.Tests/BrokerPublisherTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `Publish(string, RefCountedBuffer, int, int)` overload 부재를 reflection 기반 단언 실패로 확인했다.
  - Red: no-op overload 에서 payload range fan-out 과 잘못된 range 즉시 거부가 실패하는 것을 확인했다.
  - 구현: 기존 full publish 는 ranged publish 로 위임하고, ranged publish 는 구독자 snapshot 전에 offset/length 를 검증한다.
  - 구현: 구독자별 send 는 기존 AddRef/TrySend/false-release 계약을 유지하면서 `TransportSendBuffer`에 offset/length 를 그대로 전달한다.
  - 검증: focused `BrokerPublisherTests` 통과 6, Broker 전체 통과 10, 솔루션 전체 통과 77,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] Claude 검토 의견 조치 현황을 문서화했다.
  - 범위: `.claude/review/review-status-2026-06-11.md`, `.claude/review/README.md`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 구현: 기존 Claude 검토 원문은 삭제하지 않고 보존했다.
  - 구현: 현재 작업 트리 기준으로 must-fix 해소 여부, 오래된 종합 리뷰의 superseded 상태,
    남은 비차단 항목을 별도 review status 문서에 정리했다.
  - 검증: 솔루션 전체 테스트 통과 75, 빌드 경고 0/오류 0, `git diff --check` whitespace 오류 없음.

- [x] Broker publish fan-out 을 구현했다.
  - 범위: `src/Hps.Broker/BrokerPublisher.cs`, `src/Hps.Broker/Hps.Broker.csproj`,
    `tests/Hps.Broker.Tests/BrokerPublisherTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `BrokerPublisher` 타입 부재와 생성자/`Publish` 계약 부재를 reflection 기반 단언 실패로 확인했다.
  - Red: no-op stub 에서 구독자 2명 fan-out 과 Transport 거부 구독자 경계가 기대 수락 수를 반환하지 못해 실패했다.
  - 구현: `SubscriptionTable.CopySubscribers` snapshot 을 `ArrayPool<IConnection>`으로 받아 구독자별 `AddRef` 후
    `ITransport.TrySend`로 넘긴다.
  - 구현: `TrySend` false 또는 send buffer 생성/전송 예외 경로에서는 Broker 가 방금 추가한 구독자 ref 를 즉시 `Release`한다.
  - 구현: publish guard ref 는 caller 소유로 유지해, command handler/Server wiring 이 Publish 반환 뒤 원본 ref 를 해제해야 한다.
  - 검증: focused `BrokerPublisherTests` 통과 4, Broker 전체 통과 8, 솔루션 전체 통과 75,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] Broker subscription routing table 을 구현했다.
  - 범위: `src/Hps.Broker/`, `tests/Hps.Broker.Tests/`, `HighPerformanceSocket.slnx`, `CURRENT_PLAN.md`,
    `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `SubscriptionTable` 타입 부재와 routing API 부재를 reflection 기반 단언 실패로 확인했다.
  - Red: no-op stub 에서 subscribe, unsubscribe, snapshot copy, D008 R1 동시 subscribe-vs-unsubscribe 테스트가 실패하는 것을 확인했다.
  - 구현: `topic -> connection set`을 `ConcurrentDictionary`로 관리하고, connection 은 reference equality 로 비교한다.
  - 구현: D008에 따라 구독자 set 이 비어도 topic entry 를 즉시 제거하지 않는 NoCleanup 정책을 적용했다.
  - 테스트: Green 후 reflection 테스트를 제거하고 직접 public API 테스트 4개만 남겼다.
  - 검증: focused `BrokerRoutingTests` 통과 4, Broker 전체 통과 4, 솔루션 전체 통과 71,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] TCP frame receive handler 수명/예외 경계를 보강했다.
  - 범위: `src/Hps.Protocol/`, `tests/Hps.Protocol.Tests/`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: PayloadTooLarge 후 Transport close 알림이 다시 오면 상위 close handler 가 2회 호출되는 실패를 확인했다.
  - Red: `ITcpFrameHandler.OnFrame` 예외 후 완성 frame 이 Release 되지 않아 `RentedCount==1`로 남는 실패를 확인했다.
  - 구현: close 통지는 connection 별 1회만 수행하며, weak marker 로 이미 통지한 connection 을 추적해 단명 connection 누수를 피한다.
  - 구현: `OnFrame` 예외 시 frame 을 회수하고 assembler 를 제거한 뒤 connection 을 닫는다.
  - 검증: focused `TcpFrameReceiveHandlerTests` Red 실패 2/통과 5, Green 통과 7, Protocol 전체 통과 23,
    솔루션 전체 통과 67, 빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] TCP command decoder 를 구현했다.
  - 범위: `src/Hps.Protocol/`, `tests/Hps.Protocol.Tests/`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `TcpCommandDecoder` 타입 부재와 `TcpCommand`/`TcpCommandKind`/`TcpCommandDecodeError`/`TryDecode`
    계약 부재를 reflection 기반 단언 실패로 확인했다.
  - Red: 동작 테스트 8개는 스텁 decoder 에서 subscribe/publish 성공, publish payload 보존,
    malformed frame 별 error 반환을 만족하지 못해 실패했다.
  - 구현: `SUBSCRIBE <topic>`과 `PUBLISH <topic> <payload>`를 해석하고, malformed input 은 예외 대신
    `TcpCommandDecodeError`로 반환한다.
  - 구현: `TcpCommand`는 `readonly ref struct` span view 이므로 topic/payload 를 복사하지 않고 frame 수명 안에서만 사용된다.
  - 검증: focused `TcpCommandDecoderTests` 통과 9, Protocol 전체 통과 21, 솔루션 전체 통과 65,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] TCP receive frame 어댑터를 구현했다.
  - 범위: `src/Hps.Protocol/`, `tests/Hps.Protocol.Tests/`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `TcpFrameReceiveHandler` 타입 부재와 `ITcpFrameHandler`/constructor/Transport handler 계약 부재를
    reflection 기반 단언 실패로 확인했다.
  - Red: 동작 테스트 3개는 빈 adapter 구현에서 frame 전달, partial payload 대여, payload-too-large close 를 수행하지 않아 실패했다.
  - 구현: `TcpFrameReceiveHandler`가 `ITransportReceiveHandler`를 구현하고 connection 별 `TcpFrameAssembler`를 소유한다.
  - 구현: raw TCP chunk 를 consumed loop 로 처리해 한 chunk 의 다중 frame 도 모두 `ITcpFrameHandler.OnFrame`으로 전달한다.
  - 구현: `OnConnectionClosed`는 partial assembler payload 를 Dispose 하고, `PayloadTooLarge`는 connection 을 닫은 뒤 close callback 을 전달한다.
  - 검증: focused `TcpFrameReceiveHandlerTests` 통과 5, Protocol 전체 통과 12, 솔루션 전체 통과 56,
    빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] TCP 프레임 조립기 edge/fuzz coverage 를 보강했다.
  - 범위: `tests/Hps.Protocol.Tests/TcpFrameAssemblerTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 테스트: 0 length frame 이 caller 소유의 빈 `RefCountedBuffer`로 완성되고 Release 후 풀 누수가 없는지 검증했다.
  - 테스트: 한 TCP chunk 에 여러 frame 이 붙은 경우 첫 호출의 `consumed`가 첫 frame 끝까지만 가리키고,
    remaining slice 재호출로 다음 frame 이 완성되는 caller loop 계약을 검증했다.
  - 테스트: `payloadLength == maxPayloadLength` 성공 경계와 24개 frame 결정적 fragmentation fuzz 를 검증했다.
  - 결과: 기존 `TcpFrameAssembler` 구현이 새 edge/fuzz 테스트를 즉시 통과해 production code 수정은 없었다.
  - 검증: focused `TcpFrameAssemblerTests` 통과 7, Protocol 전체 통과 7, 솔루션 전체 통과 51, 빌드 경고 0/오류 0,
    `git diff --check` 통과.

- [x] TCP 프레임 조립기 기본 계약을 구현했다.
  - 범위: `src/Hps.Protocol/`, `tests/Hps.Protocol.Tests/`, `HighPerformanceSocket.slnx`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `TcpFrameAssembler` 타입 부재와 `TryReadFrame` API 부재를 reflection 기반 단언 실패로 확인했다.
  - Red: 동작 테스트 3개는 스텁 구현에서 frame 을 만들지 못하거나 maxPayload/Dispose 경계를 지키지 못해 실패했다.
  - 구현: `TcpFrameAssembler`가 TCP 4바이트 big-endian length header 를 누적하고 payload 를 `RefCountedBuffer`로 복사한다.
  - 구현: 완성된 frame 은 caller 가 Release 해야 하며, 조립 중 Dispose 되면 partial payload ref 를 반환한다.
  - 테스트: fragmented header/payload 조립, maxPayload 초과 시 buffer 미대여, partial payload dispose 반환을 검증했다.
  - 검증: Protocol focused Red/Green 완료. 리팩터 후 Protocol 전체 통과 3, 솔루션 전체 통과 47, 빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] TCP 동시 연결 echo 통합 테스트를 추가했다.
  - 범위: `tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 목적: Phase 2 테스트 기준의 동시 연결 안정성을 receive pump 와 send pump 의 실제 loopback echo 왕복으로 검증한다.
  - 테스트: 8개 raw TCP client 를 같은 listener 에 연결하고, 각 accepted `IConnection`이 서로 다른 payload 를 동시에 echo 받는지 확인했다.
  - 테스트: echo buffer pool 이 `RentedCount==0`으로 돌아오고, 모든 inbound connection close 뒤 transport tracking count 가 0인지 확인했다.
  - 결과: 기존 production code 가 기준을 이미 만족해 production code 수정은 없었다.
  - 검증: focused TCP 동시 echo 테스트 통과 1, Transport 전체 통과 26, 솔루션 전체 통과 44, 빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] UDP echo loopback 통합 테스트를 추가했다.
  - 범위: `tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 목적: Phase 2 완료 기준의 UDP loopback echo 왕복을 receive loop 와 endpoint send pump 결합 경로로 검증한다.
  - 테스트: datagram handler 가 받은 owned `RefCountedBuffer`에 Transport 송신 ref 를 추가하고,
    같은 `IUdpEndpoint`의 `TrySendTo`로 remote endpoint 에 되돌려 보내 raw client socket 이 동일 payload 를 받는지 확인했다.
  - 결과: 기존 production code 가 기준을 이미 만족해 production code 수정은 없었다.
  - 검증: focused UDP echo 테스트 통과 1, Transport 전체 통과 25, 솔루션 전체 통과 43, 빌드 경고 0/오류 0, `git diff --check` 통과.

- [x] TCP echo loopback 통합 테스트를 추가했다.
  - 범위: `tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 목적: Phase 2 완료 기준의 TCP loopback echo 왕복을 recv pump 와 send pump 결합 경로로 검증한다.
  - 테스트: receive handler 가 borrowed `TransportReceiveBuffer`를 테스트 전용 `RefCountedBuffer`로 즉시 복사하고,
    같은 `IConnection`에 `TrySend`해 raw client socket 이 동일 payload 를 다시 받는지 확인했다.
  - 결과: 기존 production code 가 기준을 이미 만족해 production code 수정은 없었다.
  - 검증: focused echo 테스트 통과 1.

- [x] UDP endpoint send 를 endpoint별 pending queue 와 단일 pump 로 직렬화했다.
  - 범위: `src/Hps.Transport/Saea/SaeaTransport.cs`, `src/Hps.Transport/Saea/SaeaUdpEndpoint.cs`,
    `tests/Hps.Transport.Tests/Saea/SaeaTransportTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `UdpSendTo_WhenEndpointClosesBeforePumpSends_DrainsQueuedDatagramRef`가 `SaeaUdpEndpoint.PendingSendCount`
    부재 단언 실패로 실패하는 것을 확인했다.
  - 구현: `TrySendTo`는 datagram 마다 `Task.Run`을 만들지 않고 `SaeaUdpEndpoint` pending queue 에 송신 요청을 넣는다.
  - 구현: bind 된 endpoint 마다 단일 UDP send pump 를 시작해 queued datagram 을 순차적으로 `SendToAsync`로 전송하고,
    기존 completion/unwind 경로에서 Transport 소유 ref 를 Release 한다.
  - 구현: endpoint close 는 아직 pump 가 가져가지 않은 queued datagram 의 ref 를 drain 하므로 close 전 송신 대기 항목이 누수되지 않는다.
  - 테스트: pump 없는 internal endpoint 로 queued 상태를 고정하고 close drain 후 `RentedCount==0`을 검증했다.
  - 검증: focused Red 실패 1, Green 통과 1. UDP focused 통과 2. Transport 전체 통과 23.

- [x] `Hps.Transport`와 `Hps.Transport.Tests` 폴더 구조를 책임별로 분리했다.
  - 범위: `src/Hps.Transport/`, `tests/Hps.Transport.Tests/`, `AGENTS.md`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - 구조: `src/Hps.Transport/Abstractions`에는 public 계약과 buffer view 를 배치했다.
  - 구조: `src/Hps.Transport/Runtime`에는 `TransportBase`, `TransportConnection`, `TransportFactory`를 배치했다.
  - 구조: `src/Hps.Transport/Saea`에는 `SaeaTransport`, listener, UDP endpoint 구현을 배치했다.
  - 구조: `tests/Hps.Transport.Tests`도 `Contracts`, `Runtime`, `Saea`로 나눠 production 책임 축과 맞췄다.
  - 구현: namespace 는 그대로 `Hps.Transport`/`Hps.Transport.Tests`를 유지해 public API 와 using churn 을 만들지 않았다.
  - 검증: Transport 전체 → 통과 22. 전체 `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 22.
    `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] Phase 2 backend selector 최소 계약을 구현했다.
  - 범위: `src/Hps.Transport/TransportFactory.cs`, `tests/Hps.Transport.Tests/TransportContractTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `TransportFactory` 타입 부재를 reflection 기반 contract 테스트의 `Assert.NotNull` 실패로 확인했다.
  - 구현: `TransportFactory.CreateDefault()` 정적 factory 를 추가하고 현재는 모든 환경에서 `SaeaTransport`를 `ITransport`로 반환하게 했다.
  - 테스트: Green 후 테스트를 직접 public API 호출로 리팩터링해 `ITransport` 반환값이 현재 SAEA fallback 인지 검증했다.
  - 검증: focused factory 테스트 → Red 실패 1, Green 통과 1. Transport 전체 → 통과 22.

- [x] UDP datagram handler 예외 시 receive loop 이중 Release 가능성을 제거했다.
  - 범위: `src/Hps.Transport/SaeaTransport.cs`, `tests/Hps.Transport.Tests/SaeaTransportTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: handler 가 `RefCountedBuffer`를 Release 한 뒤 예외를 던지면 receive loop 가 같은 datagram 을 다시 Release 하여
    handler 예외가 `InvalidOperationException`으로 덮이는 실패를 확인했다.
  - 구현: UDP receive loop 에서 handler 호출 전에 `ownedDatagram`으로 소유권을 옮기고 local `datagram`을 null 로 끊어,
    handler 예외 경로에서 catch 가 이미 이전된 ref 를 다시 만지지 않게 했다.
  - 테스트: private receive loop 를 white-box 로 실행해 background loop 예외를 직접 관측하고, handler 예외가 double-release 예외로 덮이지 않는지 검증했다.
  - 검증: focused S1 회귀 테스트 → Red 실패 1, Green 통과 1. Transport 전체 → 통과 21.

- [x] UDP datagram public 계약과 `SaeaTransport` UDP loopback 기준선을 구현했다.
  - 범위: `src/Hps.Transport/ITransport.cs`, `src/Hps.Transport/ITransportDatagramHandler.cs`,
    `src/Hps.Transport/IUdpEndpoint.cs`, `src/Hps.Transport/SaeaTransport.cs`, `src/Hps.Transport/SaeaUdpEndpoint.cs`,
    `src/Hps.Transport/TransportBase.cs`, `tests/Hps.Transport.Tests/TransportContractTests.cs`,
    `tests/Hps.Transport.Tests/SaeaTransportTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: UDP endpoint/datagram handler 계약 부재는 reflection 기반 contract 테스트의 `IUdpEndpoint` 타입 부재 실패로 확인했다.
  - Red: UDP receive 기준선은 `BindUdpAsync`가 `NotImplementedException`을 던지는 실패로 확인했다.
  - Red: UDP send 기준선은 `TrySendTo`가 `NotImplementedException`을 던지는 실패로 확인했다.
  - 구현: UDP는 TCP accept 모델과 분리해 `IUdpEndpoint` 수명 핸들을 사용하고, `BindUdpAsync`/`TrySendTo`/`SetDatagramHandler`로 bind/send/receive 경계를 노출한다.
  - 구현: `SaeaTransport`는 UDP socket 을 bind 하고 receive loop 에서 pinned counted buffer 를 직접 대여해 datagram handler 에 `RefCountedBuffer` 소유권을 넘긴다.
  - 구현: `TrySendTo` 성공 시 Transport 가 `TransportSendBuffer`의 ref 를 소유하고, UDP socket send completion/unwind 경로에서 정확히 한 번 Release 한다.
  - 테스트: UDP receive 가 1 datagram = 1 message 로 handler 에 도착하고 handler 가 받은 `RefCountedBuffer`를 Release 하는지 검증했다.
  - 테스트: UDP send 가 `TransportSendBuffer.Offset/Length` 범위만 전송하고 publish guard ref 해제 뒤 send completion 에서 `RentedCount==0`으로 돌아오는지 검증했다.
  - 검증: focused UDP 계약/수신/송신 테스트 각각 Red 실패 1회와 Green 통과 1회. Transport 전체 → 통과 20. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 20. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] `SaeaTransport` TCP send pump 가 pending `TransportSendBuffer`를 실제 socket 으로 보내고 ref 를 반환하는 최소 loopback 기준선을 구현했다.
  - 범위: `src/Hps.Transport/SaeaTransport.cs`, `src/Hps.Transport/TransportConnection.cs`,
    `tests/Hps.Transport.Tests/SaeaTransportTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: accepted connection 에 `TrySend`한 payload 가 raw client socket 으로 도착하지 않아 receive timeout 단언 실패로 확인했다.
  - 구현: `TransportConnection`에 pending send signal 을 추가해 빈 큐에서 첫 항목이 들어오거나 close 될 때 단일 send loop 를 깨운다.
  - 구현: `SaeaTransport`가 connection 생성 시 send loop 를 시작하고, `TryBeginInFlightSend`로 얻은 handle 을 socket send completion/unwind 경로에서 완료 또는 Dispose 한다.
  - 테스트: `TransportSendBuffer.Offset/Length` 범위만 raw socket client 로 전송되는지, publish guard ref 해제 후 send completion 이 Transport 소유 ref 를 반환해 `RentedCount==0`이 되는지 검증했다.
  - 검증: focused send pump 테스트 → Red 실패 1, Green 통과 1. Transport 전체 → 통과 17. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 17. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] `SaeaTransport`에서 닫힌 connection 이 transport 추적 목록에 남는 누수를 수정했다.
  - 범위: `src/Hps.Transport/SaeaTransport.cs`, `src/Hps.Transport/TransportConnection.cs`,
    `tests/Hps.Transport.Tests/SaeaTransportTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`,
    `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: accepted `IConnection.Close()` 이후 transport 내부 `_connections` count 가 1로 남아 단언 실패하는 테스트로 확인했다.
  - 구현: `TransportConnection`에 close callback 을 추가하고 `SaeaTransport`가 `UnregisterConnection`을 연결해 개별 connection close 시 추적 목록에서 제거한다.
  - 구현: pending drain 과 closed 표시는 connection lock 안에서 유지하되, unregister callback 과 backend socket dispose 는 lock 밖에서 수행한다.
  - 테스트: raw socket client 로 accepted connection 하나를 만들고 close 한 뒤 transport tracking count 가 0으로 돌아오는지 검증했다.
  - 검증: focused unregister 테스트 → Red 실패 1, Green 통과 1. Transport 전체 → 통과 16. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 16. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] `SaeaTransport` TCP recv pump 가 receive handler 로 byte stream 조각을 전달하는 최소 loopback 기준선을 구현했다.
  - 범위: `src/Hps.Transport/SaeaTransport.cs`, `tests/Hps.Transport.Tests/SaeaTransportTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: raw socket client 가 보낸 bytes 가 receive handler 로 도착하지 않아 timeout 단언 실패로 확인했다.
  - 구현: accepted/outbound socket 연결마다 receive loop 를 시작하고, `PinnedBlockMemoryPool`에서 대여한 receive block 으로 socket bytes 를 읽는다.
  - 구현: receive loop 는 raw TCP byte chunk 를 `TransportReceiveBuffer` borrowed view 로 만들어 현재 handler snapshot 의 `OnReceived`에 동기 전달한다.
  - 구현: remote close 또는 socket error 는 `OnConnectionClosed`를 호출하고 `IConnection.Close()` 경로로 정리한다.
  - 테스트: raw socket client 가 loopback listener 로 보낸 `{10,20,30,40}` payload 가 accepted `IConnection`과 함께 handler 로 전달되는지 검증했다.
  - 검증: focused recv pump 테스트 → 통과 1, 실패 0, 건너뜀 0. Transport 전체 → 통과 15. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 15. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] TCP payload I/O 전에 Transport 수신 전달 계약과 receive buffer 소유권을 확정했다.
  - 범위: `src/Hps.Transport/ITransport.cs`, `src/Hps.Transport/ITransportReceiveHandler.cs`,
    `src/Hps.Transport/TransportReceiveBuffer.cs`, `src/Hps.Transport/TransportBase.cs`,
    `tests/Hps.Transport.Tests/TransportContractTests.cs`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `ITransportReceiveHandler`/`TransportReceiveBuffer` 타입 부재를 reflection 기반 테스트의 `Assert.NotNull` 실패로 확인했다.
  - 구현: `ITransport.SetReceiveHandler(ITransportReceiveHandler)`를 추가했다.
  - 구현: `ITransportReceiveHandler.OnReceived(IConnection, TransportReceiveBuffer)`와 `OnConnectionClosed(IConnection)` 계약을 추가했다.
  - 구현: `TransportReceiveBuffer`를 `readonly ref struct`로 추가해 `ReadOnlySpan<byte>` borrowed view 와 `Length`만 노출한다.
  - 구현: `TransportBase`가 receive handler 등록과 snapshot helper 를 공통 처리한다.
  - 테스트: receive handler/borrowed buffer 계약이 raw `Memory<byte>`/`ReadOnlyMemory<byte>` parameter/property 를 노출하지 않고,
    `TransportReceiveBuffer`가 byref-like 타입으로 `Span`/`Length`를 제공하는지 검증했다.
  - 검증: focused receive 계약 테스트 → 통과 1, 실패 0, 건너뜀 0. Transport 전체 → 통과 14. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 14. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] `SaeaTransport`의 TCP listen/connect/accept 최소 loopback 기준선을 구현했다.
  - 범위: `src/Hps.Transport/SaeaTransport.cs`, `src/Hps.Transport/SaeaConnectionListener.cs`,
    `src/Hps.Transport/TransportConnection.cs`, `tests/Hps.Transport.Tests/SaeaTransportTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `SaeaTransport` 타입 부재를 reflection 기반 테스트의 `Assert.NotNull` 실패로 확인했다.
  - 구현: `SaeaTransport`가 `StartAsync`, `ListenTcpAsync`, `ConnectTcpAsync`, `StopAsync`, `Dispose`를 구현한다.
  - 구현: `SaeaConnectionListener`가 listen socket 을 감싸고 `AcceptAsync`에서 accepted socket 을 `TransportConnection`으로 등록한다.
  - 구현: `TransportConnection.Close()`가 pending drain 뒤 backend socket 같은 transport resource 를 dispose 할 수 있게 했다.
  - 테스트: localhost loopback 에서 포트 0 listener 를 열고, `LocalEndPoint`로 connect 한 뒤 accept 된 inbound 연결과 outbound 연결을 얻는지 검증했다.
  - 검증: focused loopback 테스트 → 통과 1, 실패 0, 건너뜀 0. Transport 전체 → 통과 13. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 13. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] Phase 2 SAEA 기준선 착수 전에 TCP public listen/connect/accept 연결 모델을 확정했다.
  - 범위: `src/Hps.Transport/ITransport.cs`, `src/Hps.Transport/IConnectionListener.cs`, `src/Hps.Transport/TransportBase.cs`,
    `tests/Hps.Transport.Tests/TransportContractTests.cs`, `tests/Hps.Transport.Tests/TransportSendQueueTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `IConnectionListener` 타입 부재를 reflection 기반 테스트의 `Assert.NotNull` 실패로 확인했다.
  - 구현: `ITransport.ListenTcpAsync(EndPoint, CancellationToken)`와 `ConnectTcpAsync(EndPoint, CancellationToken)`를 추가했다.
  - 구현: `IConnectionListener`를 추가해 listener 의 `LocalEndPoint`, `AcceptAsync`, `Close`/`Dispose` 계약을 명시했다.
  - 구현: `TransportBase`가 TCP listen/connect 추상 멤버를 강제하도록 했다.
  - 테스트: TCP listener/connector/accept 계약이 `IConnection`과 `IConnectionListener`를 통해 노출되고,
    public 계약이 raw `Memory<byte>` parameter 를 다시 노출하지 않는지 검증했다.
  - 검증: focused 연결 계약 테스트 → 통과 1, 실패 0, 건너뜀 0. Transport 전체 → 통과 12. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 12. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] 송신 펌프 abandon-leak 방어를 위해 in-flight handle 경로를 구현했다.
  - 범위: `src/Hps.Transport/TransportConnection.cs`, `tests/Hps.Transport.Tests/TransportSendQueueTests.cs`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - Red: `TryBeginInFlightSend` 메서드 부재를 reflection 기반 테스트의 `Assert.NotNull` 실패로 확인했다.
  - 구현: `TryDequeueSend(out TransportSendBuffer)` raw dequeue API를 제거하고,
    `TryBeginInFlightSend(out InFlightSend?)`가 dispose 가능한 in-flight handle 을 반환하게 했다.
  - 구현: `InFlightSend.Complete()`와 `Dispose()`는 같은 release 경로를 타며, `Interlocked.Exchange`로 여러 번 호출돼도
    실제 `RefCountedBuffer.Release()`는 한 번만 수행한다.
  - 테스트: pump 가 dequeue 이후 close/unwind 로 completion 없이 빠져나가는 abandon 시나리오에서 `Dispose()`가
    Transport 소유 ref 를 반환해 `RentedCount==0`으로 돌아오는지 검증했다.
  - 테스트: 정상 completion 후 `Dispose()`가 다시 호출되어도 이중 반환이 발생하지 않는지 검증했다.
  - 검증: focused `TransportSendQueueTests` → 통과 7, 실패 0, 건너뜀 0. Transport 전체 → 통과 11. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 11. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] 송신 펌프의 in-flight 완료 Release 경로를 구현했다.
  - 범위: `src/Hps.Transport/TransportConnection.cs`, `tests/Hps.Transport.Tests/TransportSendQueueTests.cs`.
  - Red: `CompleteInFlightSend` 메서드 부재를 reflection 기반 테스트의 `Assert.NotNull` 실패로 확인했다.
  - 구현: `TransportConnection.CompleteInFlightSend(TransportSendBuffer)`를 추가해, 송신 펌프가 완료/취소/unwind 시
    이미 dequeue 한 in-flight 항목의 Transport 소유 ref 를 반환하게 했다.
  - 구현: 이 경로는 pending 큐 상태를 변경하지 않으므로 `_gate` lock 을 잡지 않는다. close 는 pending 만 drain 하고,
    in-flight ref 는 이 completion 경로가 책임진다는 D016/D017 경계를 유지한다.
  - 테스트: close 이후에도 이미 dequeue 된 in-flight 항목은 close 가 반환하지 않고, completion 경로에서 반환되는지 검증했다.
  - 테스트: close 없이 정상 completion 만으로도 Transport 소유 ref 가 반환되어 `RentedCount==0`으로 돌아오는지 검증했다.
  - 검증: focused `TransportSendQueueTests` → 통과 6, 실패 0, 건너뜀 0. Transport 전체 → 통과 10. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 10. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] `ITransport.TrySend` 송신 큐의 enqueue/close release 계약을 구현했다.
  - 범위: `src/Hps.Transport/`, `tests/Hps.Transport.Tests/TransportSendQueueTests.cs`.
  - Red: `TransportBase` 타입 부재를 reflection 기반 테스트의 단언 실패로 확인했다.
  - 구현: `TransportBase.TrySend(IConnection, TransportSendBuffer)`가 내부 `TransportConnection`에 pending 송신을 위임하도록 했다.
  - 구현: `TransportConnection.Close()`는 close 표시와 pending drain 을 같은 lock 안에서 처리하고, pending 항목의
    `RefCountedBuffer`를 Release 한다. close 이후 `TrySend`는 false 를 반환해 호출자가 Release 하게 한다.
  - 구현: 송신 펌프가 `TryDequeueSend`로 가져간 in-flight 항목은 close 가 Release 하지 않도록 분리했다.
  - 구현: `TransportBase.TrySend`가 pending 큐에 넣기 전에 `TransportSendBuffer`의 live buffer 접근을 확인해
    `default(TransportSendBuffer)` 같은 생성자 미통과 요청이 close drain 까지 지연되지 않게 했다.
  - 테스트: open 연결에서 TrySend 성공 후 publish 가드 ref 를 해제해도 close 전까지 pool 이 반환되지 않고,
    close drain 에서 반환되는지 검증했다.
  - 테스트: closed 연결의 TrySend false 경로에서 Transport 가 소유권을 가져가지 않아 호출자가 Release 해야 함을 검증했다.
  - 테스트: default 송신 요청은 pending 큐에 들어가기 전에 즉시 거부되어 close drain 시점의 늦은 실패를 만들지 않는지 검증했다.
  - 테스트: Close idempotency 와 in-flight 항목을 close 가 Release 하지 않는 경계를 검증했다.
  - 검증: focused `TransportSendQueueTests` → 통과 5, 실패 0, 건너뜀 0. Transport 전체 → 통과 9. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 9. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] Phase 2 `ITransport`와 버퍼 소유권 계약을 구체화했다.
  - 범위: `src/Hps.Transport/`, `tests/Hps.Transport.Tests/`, `HighPerformanceSocket.slnx`.
  - Red: `Hps.Transport.TransportSendBuffer` 타입 부재를 reflection 기반 테스트의 단언 실패로 확인했다.
  - 구현: `TransportSendBuffer`를 `RefCountedBuffer + offset + length` 기반 값 타입으로 추가했고,
    payload `Length` 범위 밖 송신 요청을 거부하도록 했다.
  - 구현: 사용자 리뷰를 반영해 송신 시도와 소유권 판정을 `IConnection`이 아니라 `ITransport.TrySend(IConnection, TransportSendBuffer)`에 둔다.
    `IConnection`은 `Close()`/`Dispose()` 수명 계약만 노출한다.
  - 구현: `ITransport`는 lifecycle 계약만 우선 추가했고, 실제 listen/connect/accept와 SAEA 구현은 다음 단위로 남겼다.
  - 테스트: `TransportSendBuffer`의 버퍼/범위 노출, payload 범위 검증, `ITransport.TrySend` 존재, `IConnection`에
    `TransportSendBuffer` parameter 가 없는지, public 계약에 raw `Memory<byte>`/`ReadOnlyMemory<byte>` parameter 가 없는지 검증했다.
    이미 풀에 반환된 버퍼는 길이 0 요청이라도 거부되는지 검증했다.
  - 검증: focused `TransportContractTests` → 통과 4, 실패 0, 건너뜀 0. 전체 `dotnet test HighPerformanceSocket.slnx`
    → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 4. `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] `RefCountedBuffer` 동시 Release/팬아웃 스트레스 테스트를 보강했다.
  - 범위: `tests/Hps.Buffers.Tests/RefCountedBufferTests.cs`.
  - 테스트: 구독자 수 0, 1, 2, 4, 8, 32명 fan-out에서 publish 가드 ref와 구독자 ref를 동시에 `Release()`하고,
    각 반복에서 풀 반환이 정확히 1회 이루어져 `RentedCount==0`으로 돌아오는지 검증했다.
  - 테스트: 64개 buffer가 동시에 in-flight 상태일 때 각 buffer의 publish 가드 ref와 구독자 ref들이 경쟁적으로 `Release()`되어도
    전체 풀 누수 없이 `RentedCount==0`으로 끝나는지 검증했다.
  - production code 수정은 없었다. 기존 `RefCountedBuffer` 구현이 동시 반환 계약을 만족해 추가 구현 없이 통과했다.
  - 검증: focused `RefCountedBufferTests` → 통과 7, 실패 0, 건너뜀 0. 전체 `dotnet test HighPerformanceSocket.slnx` → 통과 18, 실패 0, 건너뜀 0.
    `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

- [x] `BipBuffer`와 `RefCountedBuffer` private helper 주석을 보강했다.
  - 범위: `src/Hps.Buffers/BipBuffer.cs`, `src/Hps.Buffers/RefCountedBuffer.cs`.
  - 기능 변경 없이 helper별 snapshot/publish 의미, SPSC cursor 소유권, payload length publish, 반환 상태/부활 방지 의도를 주석으로 남겼다.
  - 검증: focused `BipBufferTests|RefCountedBufferTests` → 통과 11, 실패 0, 건너뜀 0. 전체 `dotnet test HighPerformanceSocket.slnx` → 통과 16, 실패 0, 건너뜀 0.

- [x] `BipBuffer`의 `Volatile.Read/Write` 호출을 cursor/count 의미 기반 helper로 정리했다.
  - 범위: `src/Hps.Buffers/BipBuffer.cs`.
  - 기능 변경 없이 `ReadCommittedCountSnapshot`, `IsCommittedCountZero`, `ReadConsumerCursorSnapshot`,
    `ReadProducerCursorSnapshot`, `ReadWatermarkSnapshot`, `PublishProducerCursor`, `PublishConsumerCursor` helper를 추가했다.
  - 목적: public 메서드 본문에서 저수준 memory primitive보다 SPSC 소유권 경계와 publish/snapshot 의미가 먼저 보이도록 한다.
  - 검증: 리팩터링 전 focused 테스트 → 통과 6. 리팩터링 후 focused 테스트 → 통과 6. 전체 `dotnet test HighPerformanceSocket.slnx` → 통과 16, 실패 0, 건너뜀 0.

- [x] `RefCountedBuffer`의 `Volatile.Read/Write` 호출을 의도 기반 helper로 정리했다.
  - 범위: `src/Hps.Buffers/RefCountedBuffer.cs`.
  - 기능 변경 없이 `ReadPublishedLength`, `PublishLength`, `ReadRefCountSnapshot`, `ReadBlockSnapshot`, `IsReturned` helper를 추가했다.
  - 목적: 호출부가 저수준 memory primitive보다 길이 publish, ref count snapshot, 반환 상태 관측이라는 의도를 드러내도록 한다.
  - 검증: 리팩터링 전 focused 테스트 → 통과 5. 리팩터링 후 focused 테스트 → 통과 5. 전체 `dotnet test HighPerformanceSocket.slnx` → 통과 16, 실패 0, 건너뜀 0.

- [x] `RefCountedBuffer` 최소 참조계수/반환 계약을 구현했다.
  - 범위: `src/Hps.Buffers/RefCountedBuffer.cs`, `src/Hps.Buffers/PinnedBlockMemoryPool.cs`, `tests/Hps.Buffers.Tests/RefCountedBufferTests.cs`.
  - Red: reflection 기반 테스트로 `PinnedBlockMemoryPool.RentCounted` 부재를 단언 실패로 확인했다.
  - 구현: `RentCounted()`, `RefCountedBuffer.AddRef()`, `Release()`, `Memory`, `Span`, `Length`, `SetLength(int)`를 추가했다.
  - 계약: 생성 ref=1, 마지막 `Release()`에서 정확히 1회 풀 반환, 과다 `Release()` 예외, 반환 후 `AddRef()` 부활 금지,
    `Length` 경계 검증, 반환 후 블록 접근 거부.
  - Green 후 테스트를 직접 public API 호출 방식으로 리팩터링해 reflection helper를 남기지 않았다.
  - 검증: focused 테스트 → 통과 5, 실패 0, 건너뜀 0. 전체 `dotnet test HighPerformanceSocket.slnx` → 통과 16, 실패 0, 건너뜀 0.

- [x] `PinnedBlockMemoryPoolTests`에서 reflection 기반 `PoolApi` 래퍼를 제거했다.
  - 범위: `tests/Hps.Buffers.Tests/PinnedBlockMemoryPoolTests.cs`.
  - 기존 테스트가 production 타입 존재 여부를 확인하기 위해 reflection 래퍼를 유지하고 있었지만,
    `PinnedBlockMemoryPool`이 이미 구현된 뒤에는 테스트가 실제 public API를 직접 검증하는 편이 더 단순하고 명확하다.
  - `System.Reflection`, `ExceptionDispatchInfo`, `PoolApi` nested class를 제거하고 `new PinnedBlockMemoryPool(...)` 호출로 바꿨다.
  - production code 수정은 없었다.
  - 검증: focused 테스트 → 통과 5, 실패 0, 건너뜀 0. 전체 `dotnet test HighPerformanceSocket.slnx` → 통과 11, 실패 0, 건너뜀 0.

- [x] `BipBuffer` must-fix **2건(M1, M2)** 을 3색 TDD로 해소했다.
  - 범위: `src/Hps.Buffers/BipBuffer.cs`, `tests/Hps.Buffers.Tests/BipBufferTests.cs`.
  - M1: capacity 끝까지 commit 후 read가 0으로 wrap하면 빈 버퍼가 다시 쓰기 가능해야 함을 Red로 확인했고,
    `Commit`에서 `_write == _capacity`를 저장하지 않고 즉시 0으로 wrap하도록 수정했다.
  - M2: SPSC 스트레스에서 `GetReadSpan()`이 커밋량보다 긴 span을 노출해 `Consume` 계약을 깨는 것을 Red로 확인했고,
    반환 길이를 `_count` 기준으로 제한(clamp)했다. `_count` 값 자체는 보정하지 않는다.
  - XML doc에 소비자는 데이터를 처리한 뒤에만 `Consume`해야 한다는 계약을 명시했다.
  - 검증: `dotnet test HighPerformanceSocket.slnx` → 통과 2, 실패 0, 건너뜀 0.

- [x] `BipBuffer` deterministic edge 테스트를 별도 리뷰 단위로 추가했다.
  - 범위: `tests/Hps.Buffers.Tests/BipBufferTests.cs`.
  - 추가한 테스트: `Capacity - 1` 실사용 용량과 full 상태, partial commit/consume, tail이 minimum size를
    만족하지 못할 때 front wrap 및 watermark 순서 보존.
  - production code 수정은 없었다.
  - 검증: `dotnet test HighPerformanceSocket.slnx` → 통과 5, 실패 0, 건너뜀 0.

- [x] `BipBuffer` seeded fuzz 테스트를 별도 리뷰 단위로 추가했다.
  - 범위: `src/Hps.Buffers/BipBuffer.cs`, `tests/Hps.Buffers.Tests/BipBufferTests.cs`.
  - 테스트: capacity 2, 3, 4, 8, 17, 64와 seed 4개 조합에서 20,000회 랜덤 write/read를 실행하고
    단순 참조 큐와 바이트 순서 및 `Count`를 비교한다.
  - Red: `capacity=3, seed=4660` 및 `capacity=4, seed=4660`에서 empty non-zero cursor 상태가 front wrap과 만나
    `GetReadSpan()`이 빈 span을 반환하는 문제가 재현됐다.
  - 수정: 버퍼가 비어 있고 `read/write`가 0이 아닌 위치에서 만난 경우에는 `minimumSize`보다 작더라도 tail을 먼저 반환한다.
    또한 tail/front 비교는 실제 front 여유(`read - 1`) 기준으로 한다.
  - 검증: `dotnet test HighPerformanceSocket.slnx` → 통과 6, 실패 0, 건너뜀 0.

- [x] `PinnedBlockMemoryPool` 최소 API와 단일스레드 테스트를 별도 리뷰 단위로 구현했다.
  - 범위: `src/Hps.Buffers/PinnedBlockMemoryPool.cs`, `tests/Hps.Buffers.Tests/PinnedBlockMemoryPoolTests.cs`.
  - Red: reflection 기반 테스트로 타입 부재를 단언 실패로 확인했다.
  - 구현: `Rent()`/`Return(byte[])`, `BlockSize`, `RentedCount`, POH pinned 배열 생성, 반환 블록 크기 검증,
    대여 카운트 음수 방지 가드를 추가했다.
  - 테스트: block size와 count 추적, 반납 블록 재사용, 잘못된 크기 반환 거부, 0 이하 block size 거부.
  - 검증: `dotnet test HighPerformanceSocket.slnx` → 통과 10, 실패 0, 건너뜀 0.

- [x] `PinnedBlockMemoryPool` 멀티스레드 대여/반환 스트레스 테스트를 별도 리뷰 단위로 추가했다.
  - 범위: `tests/Hps.Buffers.Tests/PinnedBlockMemoryPoolTests.cs`.
  - 테스트: 8개 worker가 동시에 시작해 각 10,000회 `Rent()`/`Return(byte[])`을 반복하고,
    worker 예외 없음과 종료 후 `RentedCount==0`을 검증한다.
  - production code 수정은 없었다.
  - 검증: `dotnet test HighPerformanceSocket.slnx` → 통과 11, 실패 0, 건너뜀 0.

- [x] Phase 0 스캐폴딩이 존재한다.
  - 근거: `HighPerformanceSocket.slnx`, `Directory.Build.props`, `src/Hps.Buffers`, `tests/Hps.Buffers.Tests` 확인.

- [x] Phase 1 BipBuffer 초안 검토서가 존재한다.
  - 근거: `.claude/review/phase1-bipbuffer.md`.
  - 결과: must-fix **2건(M1 deadlock, M2 크로스스레드 over-read)** 이 다음 구현 작업의 선행 조건으로 기록됨.

- [x] 핵심 자료구조/설계를 실측 검증했다(임시 하니스 사용 후 삭제).
  - BipBuffer: M1·M2 재현 및 수정 검증(`phase1-bipbuffer.md`).
  - RefCountedBuffer/Pool: 팬아웃 정확히-1회 반환·누수 0 검증, 설계 승인(`phase1-refcounted-pool.md`).
  - ITransport↔BipBuffer 연동: 송신 다중생산자(D1)·소유권(D2) 설계 결정(`phase2-transport-bipbuffer.md`).
  - 브로커 라우팅: 빈 토픽 eager-cleanup 경합(R1, ~51% 유실) 재현·회피안 검증(`phase3-broker-routing.md`).
  - Publish payload 소유권(D009): recv→팬아웃 핸드오프 결정(`phase3-publish-ownership.md`).
  - TCP 프레임 조립(D010): 파서 상태머신 실측(recv 링 64B < payload 300B, 청크 1~7B, 10만 프레임 무결성·누수 0)
    + 연결 종료 release 계약(D011) 명문화 + drop-oldest evict release(D012) 실측(720만 enqueue, cap=16,
    누수·이중반환 0)(`phase3-framing-and-close.md`).
  - 결정 반영: DECISIONS D005~D012.

- [x] 상태 관리 문서 초기 세트를 작성했다.
  - 파일: `AGENT_RULES.md`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`.
  - 목적: `PLAN.md` 기반의 장기 실행 상태와 사용자 성능 목표를 이어받을 수 있게 관리한다.
