# DECISIONS.md

## D060 — UDP broker v1은 datagram self-command 와 runtime remote target 으로 설계한다

- 날짜: 2026-06-16
- 상태: Accepted
- 결정: UDP broker v1은 별도 TCP control plane 으로 UDP remote 를 등록하지 않고, bound UDP socket 으로 들어오는
  datagram payload 자체를 broker command 로 해석한다. runtime subscriber identity 는 `(IUdpEndpoint localEndpoint, EndPoint remoteEndPoint)`
  조합이며, stable id, `EndpointId`, `REGISTER`, `SUBSCRIBE ... AS ...`, reconnect subscription transfer 는 사용하지 않는다.
  v1 command set 은 `SUBSCRIBE <topic>`, `UNSUBSCRIBE <topic>`, `PUBLISH <topic> <payload>`로 시작한다.
  malformed UDP command 는 해당 datagram 만 폐기하고 shared UDP endpoint 를 닫지 않는다.
- 근거: TCP control plane 으로 UDP remote 를 등록하면 TCP connection 과 UDP remote address 를 묶는 cross-transport registry,
  주소 검증, NAT/port 변경, stale remote 정책을 함께 설계해야 한다. 이는 D059에서 v1 밖으로 뺀 stable subscriber identity 문제를
  다시 끌어온다. UDP datagram self-command 는 `ITransportDatagramHandler`가 이미 제공하는 local endpoint 와 remote `EndPoint`
  정보를 그대로 runtime send target 으로 사용할 수 있어 가장 작은 구현 경계다. UDP에는 per-remote close notification 이 없으므로
  explicit cleanup 을 위해 `UNSUBSCRIBE`를 command set 에 포함한다.
- 영향: 다음 구현은 datagram parser 부터 크게 넓히지 않고, 먼저 `BrokerSubscriber`가 UDP runtime target 을 표현하고
  `BrokerPublisher`가 TCP/UDP target 으로 분기해 fan-out 할 수 있는지 작은 TDD 단위로 검증한다.
  idle expiry, stale remote sweep, stable subscriber identity 는 후속 backlog 로 유지한다.
  세부 설계는 `docs/superpowers/specs/2026-06-16-udp-broker-runtime-target-wire-control-design.md`를 따른다.

## D059 — v1 subscription 은 runtime endpoint 수명에 묶고 reconnect rebinding 은 제공하지 않는다

- 날짜: 2026-06-16
- 상태: Accepted
- 결정: v1에서는 reconnect 후 기존 subscription 유지나 stable subscriber rebinding 을 제공하지 않는다.
  TCP subscription 은 현재 TCP `IConnection` 수명에 묶이며, connection 이 닫히면 기존처럼 `UnsubscribeAll(connection)`으로 제거한다.
  reconnect 한 client 는 새 connection 에서 다시 `SUBSCRIBE` 해야 한다. UDP broker 를 v1에 포함하더라도 stable subscriber identity 없이
  bind 된 UDP endpoint 와 remote endpoint 조합을 runtime send target 으로 다룬다.
- 근거: stable subscriber identity 는 handshake/configuration/host API, duplicate id 처리, reconnect 시 기존 endpoint 처리,
  UDP stale target 정리까지 함께 결정해야 한다. 이를 지금 routing key 로 끌어들이면 UDP broker 결선과 TCP command 경계를 동시에 넓힌다.
  현재 목표는 TCP/UDP runtime send target 으로 payload fan-out 경계를 먼저 완성하고, stable identity 는 실제 요구가 선명해진 뒤 설계하는 것이다.
- 영향: `EndpointId`는 D058처럼 diagnostics id 로 유지한다. `REGISTER`, `SUBSCRIBE ... AS ...`, reconnect subscription transfer 는
  v1 범위 밖이다. 다음 단위는 stable identity 없이 UDP runtime target 을 어떻게 등록/해지하고 fan-out 할지 결정한다.

## D058 — `EndpointId`는 transient diagnostics id 이며 stable routing id 가 아니다

- 날짜: 2026-06-16
- 상태: Accepted
- 결정: `EndpointId`는 Transport 가 실행 중에 살아 있는 TCP connection 또는 UDP endpoint 를 구분하기 위해 발급하는
  transient diagnostics id 로 유지한다. Broker reconnect 재바인딩이나 stable subscription key 로 사용하지 않는다.
  stable routing identity 가 필요하면 protocol handshake, server configuration, host API 같은 별도 control-plane 에서
  명시적으로 받은 broker-level identity 를 도입해야 한다.
- 근거: 현재 `EndpointId`는 `TransportBase.CreateEndpointId()`에서 transport 수명 안의 증가값으로 발급된다.
  이 값은 socket handle 과 분리된 관측값이라는 장점이 있지만, reconnect 이후 같은 외부 endpoint 인지 판단할 외부 의미를 담지 않는다.
  이를 곧바로 `SubscriptionTable` key 로 쓰면 이름만 endpoint 중심이고 reconnect semantics 는 해결되지 않는다.
- 영향: `BrokerSubscriber`의 현재 TCP reference identity 는 runtime send target 의미로 유지한다. 다음 UDP broker 또는 stable endpoint routing
  단위에서는 `EndpointId`를 stable key 로 승격하지 않고, 먼저 explicit subscriber identity source 를 결정해야 한다.
  세부 정책은 `docs/superpowers/specs/2026-06-16-endpoint-identity-policy.md`를 따른다.

## D057 — Broker routing value 는 `BrokerSubscriber` endpoint target 으로 저장한다

- 날짜: 2026-06-16
- 상태: Accepted
- 결정: `SubscriptionTable` 내부 구독자 값은 raw `IConnection` 이 아니라 `BrokerSubscriber` 로 저장한다.
  `BrokerSubscriber` 는 현재 TCP `IConnection` 을 send target 으로 감싸고 `EndpointTransportKind` 를 노출한다.
  기존 TCP command handler 와 테스트 경계를 깨지 않기 위해 `Subscribe(topic, IConnection)`,
  `Unsubscribe(topic, IConnection)`, `IsSubscribed(topic, IConnection)`, `CopySubscribers(topic, IConnection[])`
  overload 는 compatibility API 로 유지한다. 신규 publish fan-out 경로는 `BrokerSubscriber[]` snapshot 을 사용한다.
- 근거: Interface Server 목표에서는 TCP connection 과 UDP endpoint 를 같은 "발행 대상" 개념으로 다뤄야 한다.
  Broker routing table 이 계속 raw TCP connection 배열만 노출하면 UDP broker 를 붙일 때 `SubscriptionTable` 과
  `BrokerPublisher` 를 다시 동시에 갈아엎어야 한다. 먼저 TCP 동작을 보존한 endpoint-target 값 경계를 만들면
  후속 UDP wire/control 결정을 별도 단위로 분리할 수 있다.
- 영향: v1 TCP fan-out 동작과 소유권 규칙은 그대로 유지된다. stable external endpoint id, reconnect binding,
  UDP command wire format, UDP send target 값은 이 결정에 포함하지 않고 후속 UDP broker 단위에서 정한다.

## D056 — Endpoint snapshot collection 은 선택적 Transport diagnostics capability 로 노출한다

- 날짜: 2026-06-16
- 상태: Accepted
- 결정: 실제 TCP/UDP endpoint lifecycle 에 발급된 transient `EndpointId`와 send queue 상태는
  `ITransportEndpointDiagnostics.GetEndpointSnapshots()` 선택적 capability 로 읽는다. 기본 `ITransport` 계약은 넓히지 않는다.
  SAEA 기준선은 active TCP connection 과 UDP endpoint 를 `EndpointSnapshot[]` 값 배열로 반환하며, 각 snapshot 은 id, transport kind,
  open/closed state, 현재 pending send count, endpoint 수명 high-watermark, dropped pending send count 만 담는다.
- 근거: Endpoint 관측은 운영/벤치마크/후속 Broker 전환에 필요하지만 hot path 송수신 API의 필수 계약이 아니다.
  선택적 capability 로 두면 backend 가 같은 값을 제공할 수 있을 때만 좁혀서 사용하고, Transport/Broker 의 기본 송수신 경계를 흔들지 않는다.
  snapshot 은 connection/socket handle 을 담지 않으므로 닫힌 endpoint 참조를 상위 계층이 붙잡지 않는다.
- 영향: `TransportBase`는 backend 수명 안에서 증가하는 transient endpoint id 를 발급한다. 이 id 는 실행 중 관측용이며,
  stable external endpoint id 나 reconnect binding 을 보장하지 않는다. Broker subscription value 의 endpoint 중심 전환과 UDP broker 결선은
  이 capability 를 기반으로 별도 단위에서 결정한다.

## D055 — Benchmark report high-watermark 필드는 schema-version 1의 additive field 로 유지한다

- 날짜: 2026-06-16
- 상태: Accepted
- 결정: `tcp-pending-send-queue-high-watermark`와 `udp-pending-send-queue-high-watermark`는 D052의 benchmark report schema 에
  additive field 로 추가하고, `schema-version`은 1로 유지한다. 이 두 필드는 기존 key 의 의미를 바꾸지 않고, 기존 runner 공통 결과에
  send-side 관측값을 덧붙이는 확장이다. 세 runner 는 계속 같은 key 집합을 항상 출력한다.
- 근거: 현재 report consumer 는 repo 안에 버전 분기 로직을 요구하지 않으며, high-watermark 필드는 누락 시 기존 결과 해석을 깨는
  breaking change 가 아니다. version 을 올리면 실제 호환성 단절이 없는데도 비교 도구가 불필요하게 schema 를 나눌 수 있다.
  반대로 D052의 key 목록을 최신화하지 않으면 report writer 와 결정 문서가 어긋나므로 field 목록은 즉시 보강한다.
- 영향: 이후 field 삭제, 타입 변경, 기존 key 의미 변경, pass/fail gate 의미 변경처럼 consumer 호환성을 깨는 변경이 생길 때만
  `schema-version`을 올린다. last-drop scope 나 endpoint 별 queue depth 처럼 새 진단값을 추가하는 경우도 additive 이면 version 1을
  유지할 수 있지만, 해당 단위에서 다시 판단한다.

## D054 — Endpoint identity 최소 계약은 Transport abstraction 의 값 snapshot 으로 시작한다

- 날짜: 2026-06-16
- 상태: Accepted
- 결정: Endpoint 중심 Interface Server 로 가기 위한 첫 코드 단위는 runtime registry 가 아니라 `Hps.Transport` public abstraction 의
  값 계약으로 제한한다. `EndpointId`는 connection 객체나 UDP endpoint 객체와 분리된 logical id 값이며,
  `EndpointSnapshot`은 id, transport kind, state, pending send count, pending send queue high-watermark,
  dropped pending send count 만 담는다. Snapshot 은 socket, `IConnection`, `IUdpEndpoint`, `RefCountedBuffer`,
  `Memory<byte>` 같은 수명 있는 handle 을 포함하지 않는다.
- 근거: Broker subscription value 를 곧바로 바꾸면 Broker/Server/Protocol 테스트 범위가 넓어진다. 먼저 값 계약을 고정하면
  후속 runtime registry, snapshot collection, Broker endpoint 전환이 같은 필드 집합을 기준으로 진행된다.
  또한 snapshot 이 transport handle 을 보관하면 닫힌 connection 이 불필요하게 살아남거나 상위 계층이 Transport 수명 경계를 우회할 수 있다.
- 영향: 다음 P1 단위는 TCP connection/UDP endpoint lifecycle 에 transient `EndpointId`를 발급하고 snapshot collection API 를
  제공하는 runtime wiring 이다. Stable external endpoint id, reconnect binding, UDP broker subscription 정책은 이 값 계약이 아니라
  후속 Endpoint registry/Broker 단위에서 결정한다.

## D053 — Interface Server 목표는 endpoint-aware publish model 과 send-side 관측성을 우선한다

- 날짜: 2026-06-16
- 상태: Accepted
- 결정: 프로젝트의 상위 목표를 단순 TCP pub/sub broker 가 아니라 외부 source data 를 받아 구독된 TCP/UDP endpoint 로 발행하는
  Interface Server 로 재정렬한다. Phase 4의 다음 구현 우선순위는 latency SLO gate 가 아니라 endpoint/send-side 관측성이다.
  TCP/UDP pending send queue high-watermark 는 public diagnostics 와 benchmark report 에 연결됐다(`22591b5`, `db8984f`).
  다음 코드 단위는 EndpointId/endpoint snapshot 최소 계약을 우선 검토한다.
- high-watermark 는 endpoint registry 도입 전까지 "endpoint identity"를 알려주는 값이 아니라,
  transport kind 별 lifetime max pending depth 를 뜻한다. 즉 TCP는 어떤 단일 connection 이 도달한 최대 pending depth,
  UDP는 어떤 단일 endpoint queue 가 도달한 최대 pending depth 를 Transport 수명 동안 보존한다. bounded drop-oldest capacity 때문에
  이 값은 capacity 에서 포화되며, "천장 도달" 여부는 알려주지만 초과 적체량은 drop count 와 함께 해석해야 한다.
- 근거: 현재 benchmark 는 subscriber 수신 latency 와 drop count 를 기록하지만, 느린 endpoint 때문에 send queue 가 어디까지 밀렸는지
  직접 설명하지 못한다. Interface Server/DDS 유사 목표에서는 endpoint 별 transport kind, send backlog, drop 여부를 먼저 관측해야
  latency SLO 실패 원인을 분해할 수 있다. endpoint identity 와 UDP broker 결선은 필요하지만 영향 범위가 넓으므로 high-watermark 관측성 뒤에 진행한다.
- 영향: `docs/superpowers/specs/2026-06-16-interface-server-endpoint-model-design.md`를 후속 설계 기준으로 둔다.
  `TODOS.md`의 latency SLO gate 는 P2로 낮추고, endpoint snapshot 최소 계약을 다음 P1 후속으로 유지한다.
  DDS wire protocol, discovery, reliable UDP, durable history 는 v1 범위 밖으로 유지한다.

## D052 — Phase 4 benchmark report 는 공통 JSON schema 로 저장한다

- 날짜: 2026-06-16
- 상태: Accepted
- 결정: `tests/Hps.Benchmarks`의 `--smoke`, `--load`, `--load-open-loop` 명령은 선택적 `--report <path>` 옵션을 받는다.
  report 는 `System.Text.Json.Utf8JsonWriter`로 명시 필드를 쓰는 JSON 파일이며, 신규 NuGet 의존성은 추가하지 않는다.
  기존 파일은 덮어쓰고, 상위 디렉터리가 없으면 생성한다. `--report` 단독 사용, `--target --report`, path 누락은 usage error 로 처리한다.
  세 runner 는 모두 같은 key 집합을 항상 출력한다. schema 는 `schema-version: 1`, `result-name`, `passed`, `scenario`,
  `payload-bytes`, `target-rate-hz`, `target-duration-seconds`, `planned-message-count`, `sent`, `received`, `dropped`,
  `tcp-pending-send-queue-high-watermark`, `udp-pending-send-queue-high-watermark`, `payload-errors`, `pool-rented`,
  `actual-rate-hz`, `p50-latency-us`, `p99-latency-us`, `first-half-p99-latency-us`, `second-half-p99-latency-us`,
  `p99-latency-growth-ratio`, `elapsed-ms`를 포함한다.
- 근거: 세 runner 는 이미 같은 `TcpLoopbackRunResult`를 반환하므로 Transport/Broker public 계약을 넓히지 않고도 같은 결과 schema 를 만들 수 있다.
  stdout 은 사람이 보는 즉시 요약으로 유지하고, JSON report 는 리뷰와 추세 비교를 위한 파일 산출물로 둔다.
  latency 값은 아직 환경별 SLO 가 확정되지 않았으므로 report 에 관측값으로만 남기고 pass/fail gate 로 승격하지 않는다.
- 영향: Phase 4 결과는 수동 실행이나 CI에서 같은 JSON schema 로 보존할 수 있다. latency SLO threshold, Markdown report,
  report history 관리, queue depth diagnostics 는 별도 작업 단위에서 결정한다.

## D051 — Phase 4 closed-loop load 와 open-loop backpressure benchmark 를 분리한다

- 날짜: 2026-06-15
- 상태: Accepted
- 결정: `tests/Hps.Benchmarks`의 현재 `--load`는 SAEA TCP loopback closed-loop 기준선으로 유지한다.
  이 runner 는 한 publish payload 를 subscriber socket 에서 수신한 뒤 다음 publish 로 넘어가므로, 4096B×100Hz×30초 조건에서
  처리량, p50/p99 지연, drop 없음, pool leak 없음은 검증하지만 송신 큐 적체나 drop-oldest/backpressure 경로를 stress 하지 않는다.
  큐 적체와 backpressure 검증은 publisher 가 subscriber 수신과 독립적으로 100Hz 발사를 지속하는 open-loop benchmark 로 별도 추가한다.
- 근거: `overall-state-2026-06-15.md` 추가 검토에서 closed-loop 구조상 publisher 가 subscriber 보다 앞설 수 없어
  `dropped==0`이 backpressure 안정성 증거가 아니라는 점이 확인됐다. 현재 runner 는 기본 성능 기준선으로 가치가 있지만,
  `CURRENT_PLAN.md`가 해석한 "지속 부하에서 큐 적체가 누적되지 않는 상태"까지 증명하지는 않는다.
- 영향: `--load` 결과는 SAEA loopback 단일 subscriber 기준 baseline 으로 해석한다. 이후 open-loop runner 는 별도 작업 단위에서
  queue depth, dropped count, send backlog 증가 여부, latency 증가 추세를 측정해야 한다. 백프레셔 기본 정책을 disconnect 로 둘지
  drop-oldest 로 둘지 결정하는 P2 항목은 open-loop 결과를 보고 다시 판단한다.

## D050 — Phase 4 첫 벤치마크 기준은 SAEA TCP loopback 4096B×100Hz 로 고정한다

- 날짜: 2026-06-15
- 상태: Accepted
- 결정: Phase 4의 첫 재현 기준은 `tcp-loopback-saea-baseline`으로 둔다. 기본 시나리오는 SAEA transport,
  loopback TCP broker, topic `alpha`, payload 4096 bytes, publish rate 100 Hz, subscriber 1명, duration 30초,
  planned message count 3000개다. `tests/Hps.Benchmarks`는 이 목표값을 `BenchmarkTargets`로 코드에 고정하고,
  BenchmarkDotNet 기반 microbench 와 이후 TCP 부하 생성 하니스를 같은 프로젝트 안에서 확장한다.
- 근거: 리뷰의 P1은 기능 완성 여부가 아니라 "4096B×100Hz를 지연 누적 없이 처리"한다는 목표를 재현 가능한 수치로
  검증하라는 요구다. 목표값을 문서에만 두면 이후 microbench, load runner, 리포트가 서로 다른 조건을 사용할 수 있다.
  코드 상수와 `--target` 출력으로 먼저 고정하면 다음 단위에서 실제 TCP load runner 를 붙일 때 기준 drift 를 막을 수 있다.
- 영향: 이번 결정은 성능 달성을 주장하지 않는다. 첫 커밋은 benchmark project, 목표 출력, pinned pool microbench 골격까지만 제공한다.
  실제 pass/fail gate 는 다음 단위에서 sent==received, dropped==0, pool-rented==0, p50/p99 report 기록을 구현해 닫는다.

## D049 — broker server 샘플은 기존 host 를 조립하는 실행 harness 로 둔다

- 날짜: 2026-06-12
- 상태: Accepted
- 결정: `Hps.Sample.BrokerServer`는 `BrokerServer`, `TransportFactory.CreateDefault()`, `PinnedBlockMemoryPool`을 조립하는
  console executable 로 둔다. 실행 인자는 `<host> <port> <max-frame-bytes>`이며, sample publisher/subscriber 가 붙을
  TCP broker process 를 띄운 뒤 Ctrl+C 로 `BrokerServer.StopAsync` 경로를 통과해 종료한다.
- 근거: 수동 fan-out 확인에는 publisher/subscriber client 와 별도로 broker process 가 필요하지만, 이를 위해 `Hps.Server` public API 를
  넓히거나 별도 hosting abstraction 을 추가할 필요는 없다. 기존 library host 와 Transport factory 를 그대로 사용하면 이후 backend 선택이
  `TransportFactory`로 이동해도 sample 실행 흐름은 유지된다.
- 영향: 이 sample 은 운영용 daemon 이 아니며 설정 파일, logging, diagnostics endpoint, protocol error response 를 제공하지 않는다.
  `max-frame-bytes`는 Broker TCP frame payload 상한이므로 `PUBLISH <topic> <payload>` 명령 전체 길이를 수용할 만큼 크게 지정해야 한다.

## D048 — TCP receive handler 예외는 connection close notification 으로 수렴한다

- 날짜: 2026-06-12
- 상태: Accepted
- 결정: `ITransportReceiveHandler.OnReceived`가 예외를 던지면 `SaeaTransport` TCP receive loop 는 background task 를
  fault 상태로 방치하지 않고 `OnConnectionClosed`를 통지한 뒤 해당 connection 을 닫고 receive loop 를 종료한다.
- 근거: UDP handler 예외는 D044에서 endpoint close notification 으로 수렴하도록 보강됐지만, TCP `DispatchReceived` 호출은
  socket receive 예외 처리 밖에 있어 handler 예외가 close notification 없이 Task fault 로 새어 나갈 수 있었다.
  이 경우 Protocol/Broker 계층의 close cleanup, 특히 subscription cleanup 이 실행되지 않아 dead connection reference 가 남을 수 있다.
- 영향: handler 내부 예외를 무시하고 같은 connection 에서 계속 수신하지 않는다. 현재 public surface 에 background receive loop fault 를
  관측할 API 가 없으므로, 실패를 connection 수명 종료로 바꾸는 쪽이 UDP 정책과 일관된다. `OnConnectionClosed` 자체가 예외를 던지는
  계약 위반 경로는 이번 단위에서 별도 복구하지 않는다.

## D047 — publisher/subscriber 샘플은 TCP wire protocol 클라이언트로 둔다

- 날짜: 2026-06-12
- 상태: Accepted
- 결정: `Hps.Sample.Publisher`와 `Hps.Sample.Subscriber`는 `Hps.Server` 내부 타입을 참조하지 않는 독립 TCP client 로 둔다.
  두 샘플은 broker TCP wire format 인 `4바이트 big-endian 길이 + command payload`만 생성해 서버에 전송한다.
  publisher 는 `PUBLISH <topic> <payload>` frame 을 한 번 보내고 종료하며, subscriber 는 `SUBSCRIBE <topic>` frame 을 보낸 뒤
  현재 Broker fan-out 정책에 맞춰 서버가 보내는 raw payload chunk 를 stdout 으로 출력한다.
- 근거: 샘플 client 가 `BrokerServer`, `SaeaTransport`, 내부 pool 같은 서버 구현 타입을 직접 참조하면, 이후 RIO/io_uring backend 나
  서버 실행 방식이 바뀔 때 샘플까지 함께 흔들린다. wire protocol client 로 두면 실제 사용자가 보는 TCP 경계를 그대로 검증하면서도
  서버 구현 교체와 독립적으로 유지할 수 있다.
- 영향: 수동 fan-out 확인에는 별도 broker 실행 프로세스가 필요하다. 현재 `Hps.Server`는 library host 이므로,
  broker server console sample 또는 실행 harness 는 다음 별도 단위에서 추가한다. subscriber 출력은 아직 message framing 이 아니라
  TCP receive chunk 단위이며, 서버 outbound framing/ack 정책이 생기면 샘플도 그 계약에 맞춰 갱신한다.

## D046 — SAEA UDP receive 는 동기 handler 호출로 Transport 내부 prefetch 를 만들지 않는다

- 날짜: 2026-06-12
- 상태: Accepted
- 결정: 현재 `SaeaTransport` UDP receive 기준선에는 별도 receive pending queue, drop-oldest/drop-newest queue,
  또는 endpoint close 기반 receive backpressure 를 추가하지 않는다. `ITransportDatagramHandler.OnDatagramReceived`는
  동기 콜백 계약이고, SAEA receive loop 는 handler 호출이 반환될 때까지 다음 `RentCounted()`와 다음 `ReceiveFromAsync`로
  넘어가지 않는다. 따라서 느린 동기 handler 때문에 Transport 내부에서 `RefCountedBuffer`가 무제한 prefetch 되는 구조는 아니다.
- 근거: `.claude/review/phase2-udp-datagram.md` Q1은 UDP receive 가 datagram 마다 풀에서 버퍼를 대여한다는 점을 지적했다.
  실제 현재 구현은 handler 를 fire-and-forget 으로 분리하지 않고 동기적으로 호출하므로, handler 가 막혀 있는 동안
  추가 datagram 은 OS UDP socket buffer 에 머물거나 커널 정책에 따라 drop 될 수 있지만 Transport pool 대여 수는 늘지 않는다.
  이를 `UdpReceive_WhenHandlerIsBlocked_DoesNotPrefetchAdditionalDatagrams` 회귀 테스트로 고정한다.
- 영향: 상위 handler 가 datagram ref 를 별도 작업으로 넘기고 즉시 반환하는 경우의 보관량은 Transport receive loop 가 아니라
  해당 handler/Broker fan-out 정책의 책임이다. 이후 UDP Broker publish fan-out 을 붙이거나 async datagram handler 계약을 도입하면,
  그 경계에서 bounded queue, drop 정책, diagnostics 를 별도 결정으로 다시 다룬다.

## D045 — SAEA 기준선은 raw pinned block/direct send 예외를 문서화한다

- 날짜: 2026-06-12
- 상태: Accepted
- 결정: 현재 `SaeaTransport` 기준선은 TCP receive 에서 pinned receive block 으로 raw byte chunk 를 받고,
  TCP send 에서 `TransportSendBuffer.Offset/Length` 범위를 raw Socket 으로 직접 전송하는 방식을 허용한다.
  UDP receive 는 D024에 따라 `RefCountedBuffer`로 datagram 을 직접 받고, UDP send 는 endpoint pending queue 와
  raw Socket send pump 를 사용한다. 이 방식은 SAEA 기준선의 계약/수명/통합 검증용 예외이며,
  AGENTS.md 의 `BipBuffer` send/recv 큐 원칙을 폐기하거나 대체하지 않는다.
- 근거: SAEA 기준선은 RIO/io_uring 최적화 이전에 public Transport 계약, 버퍼 소유권, close drain,
  fan-out, UDP endpoint 수명 정책을 실제 socket 으로 검증하는 역할이다. 이 단계에서 모든 송수신 큐를
  `BipBuffer`로 강제하면 검증 범위가 불필요하게 커지고, 이미 D023/D024로 수락한 raw Socket 기준선과
  상위 규칙 문서가 충돌한다.
- 영향: SAEA backend 에서는 현재 direct pinned block receive 와 `TransportSendBuffer` direct send 가 허용된다.
  이후 RIO/io_uring backend, 명시적 송수신 큐 최적화, 또는 SAEA 내부 큐 재작업 단위에서는 D007의
  `MPSC 큐 → 단일 펌프 → SPSC 송신 BipBuffer` 및 recv `BipBuffer` 원칙을 다시 검토하고 적용해야 한다.

## D044 — UDP datagram handler 예외는 endpoint close notification 으로 수렴한다

- 날짜: 2026-06-12
- 상태: Accepted
- 결정: `ITransportDatagramHandler.OnDatagramReceived` 호출 뒤 handler 예외가 발생하면 SAEA UDP receive loop 는 task 를 fault 상태로
  방치하지 않고 `OnDatagramEndpointClosed`를 통지한 뒤 endpoint 를 닫고 loop 를 종료한다. datagram 소유권은 handler 호출 시점에
  이미 이전된 것으로 유지하므로, handler 가 예외를 던져도 해당 `RefCountedBuffer` 참조 반환 책임은 handler 에 있다.
- 근거: 현재 public surface 에는 background receive loop fault 를 관측할 API 가 없다. 예외를 그대로 throw 하면 endpoint 는 열린 것처럼
  보이지만 실제 수신 loop 만 중단될 수 있다. 반대로 handler 예외를 완전히 무시하고 계속 수신하면 상위 handler 버그가 반복되어도
  운영자가 endpoint 수명 변화를 알 수 없다. endpoint close notification 은 현재 계약을 넓히지 않으면서 실패 상태를 관측 가능한 수명 전이로 만든다.
- 영향: handler 가 datagram 을 받은 뒤 반환하지 않고 예외를 던지는 경우의 누수는 handler 계약 위반으로 남는다. Transport 가 예외 catch 에서
  같은 datagram 을 다시 Release 하면, handler 가 이미 Release 한 뒤 예외를 던진 합법적인 unwind 경로에서 이중 Release 가 발생할 수 있기 때문이다.
  UDP receive prefetch 경계는 D046으로 별도 확인했으며, handler fault diagnostics counter/log 는 별도 단위에서 다룬다.

## D043 — Broker 가 직접 connection 을 닫는 protocol-error 경로는 구독 cleanup 을 먼저 수행한다

- 날짜: 2026-06-12
- 상태: Accepted
- 결정: `BrokerTcpFrameHandler`가 malformed command 또는 handler 내부 오류 때문에 `connection.Close()`를 직접 호출하는 경우,
  close 호출 전에 `SubscriptionTable.UnsubscribeAll(connection)`을 먼저 수행한다. Transport/Protocol 계층에서 이후 close notification 이
  다시 들어오면 `OnConnectionClosed`가 같은 cleanup 을 반복할 수 있지만, `UnsubscribeAll`은 idempotent 하므로 부작용 없이 통과한다.
- 근거: SAEA TCP receive loop 는 socket dispose 로 `ObjectDisposedException`이 발생하는 종료 경로에서 별도 close notify 없이 반환할 수 있다.
  따라서 Broker 가 protocol error 를 발견하고 직접 connection 을 닫았는데 close notify 에만 cleanup 을 의존하면,
  이미 구독된 connection 이 topic set 에 dead reference 로 남는다. 이는 D036의 connection-wide cleanup 목적과 C10K churn 환경의
  routing table 안정성에 맞지 않는다.
- 영향: protocol-error response frame 은 여전히 구현하지 않는다. malformed command 의 최소 정책은 frame guard ref 회수,
  subscription cleanup, connection close 순서다. UDP datagram handler 예외 정책은 다른 수명 경계이므로 별도 결정으로 남긴다.

## D042 — drop-oldest public 관측성은 선택적 Transport diagnostics snapshot 으로 노출한다

- 날짜: 2026-06-12
- 상태: Accepted
- 결정: `ITransport` 기본 송수신 계약은 넓히지 않고, 별도 public capability 인 `ITransportDiagnostics`와
  불변 값 타입 `TransportDiagnosticsSnapshot`을 추가한다. `TransportBase`가 이 capability 를 구현해 TCP/UDP
  drop-oldest 누적 counter 를 제공한다. TCP는 `TransportConnection` drop callback 으로, UDP는 `SaeaUdpEndpoint`가
  소유한 `SaeaTransport`에 직접 기록하는 방식으로 Transport 수명 누적 counter 를 증가시킨다.
- 근거: `ITransport`에 진단 메서드를 직접 추가하면 SAEA/RIO/io_uring 모든 backend 의 필수 API가 되어 수명/송수신 계약이
  불필요하게 커진다. 반면 drop-oldest 는 메시지 손실을 조용히 만들 수 있으므로 운영자가 읽을 수 있는 public metric surface 는 필요하다.
  선택적 capability 는 기존 public 송수신 경계를 유지하면서, 지원하는 Transport 에서만 낮은 비용의 snapshot 을 제공한다.
- 영향: snapshot 은 reset 되지 않는 누적값이며, connection 또는 UDP endpoint 가 닫힌 뒤에도 이미 발생한 drop 수를 보존한다.
  `DroppedPendingSendCount`는 TCP+UDP 합계이고, `TcpDroppedPendingSendCount`와 `UdpDroppedPendingSendCount`는 원인 분리를 위해
  별도로 유지한다. 동기 log 출력, sampling, Server-level diagnostics convenience API 는 별도 단위에서 필요성이 확인될 때 다룬다.

## D041 — drop-oldest 관측성은 우선 내부 누적 counter 로 제공한다

- 날짜: 2026-06-12
- 상태: Accepted
- 결정: TCP `TransportConnection`과 UDP `SaeaUdpEndpoint`에 `internal long DroppedPendingSendCount`를 둔다.
  drop-oldest eviction 이 발생할 때마다 `Interlocked.Increment`로 누적하고, 테스트와 내부 진단은 `Volatile.Read` 기반
  property 로 값을 읽는다. public Transport/Broker/Server metric API 와 동기 log 출력은 이번 단위에 포함하지 않는다.
- 근거: D039/D040의 drop-oldest 정책은 느린 소비자에서 메모리 상한을 보장하지만, 메시지 손실이 조용히 발생한다.
  hot path 에 직접 log 를 넣으면 비용과 노이즈가 커질 수 있으므로, 먼저 낮은 비용의 누적 counter 로 drop 발생 여부를
  관측 가능하게 만든다.
- 영향: counter 는 endpoint/connection 객체 수명 동안 누적되며 reset API 는 제공하지 않는다.
  이후 운영 metric 이 필요하면 이 내부 counter 를 Transport/Broker/Server 레벨 aggregate 로 끌어올리는 별도 단위에서 결정한다.

## D040 — UDP endpoint pending send queue 도 기본 drop-oldest capacity 로 제한한다

- 날짜: 2026-06-12
- 상태: Accepted
- 결정: `SaeaUdpEndpoint`의 pending send queue 에도 기본 capacity 16을 적용한다. open endpoint 에서
  `TrySendTo`가 호출됐을 때 queue 가 이미 가득 차 있으면 가장 오래된 `UdpSendRequest`를 dequeue 하고,
  해당 `TransportSendBuffer.Buffer`의 Transport 소유 ref 를 정확히 1회 `Release` 한 뒤 새 datagram 을 enqueue 한다.
  `TrySendTo` 자체는 새 datagram 을 수락했으므로 `true`를 반환한다.
- 근거: D028에서 UDP send 는 endpoint 단위 pending queue 와 단일 pump 로 직렬화하기로 했다. 이 queue 에 상한이 없으면
  느린 remote 또는 막힌 socket 상황에서 TCP 쪽 D039와 같은 메모리 증가 문제가 발생한다. D012의 drop-oldest 정책을
  TCP와 UDP send queue 모두에 적용하면 send 경계의 backpressure 의미와 release 책임이 일관된다.
- 영향: evict 대상 선택과 queue 제거는 `SaeaUdpEndpoint`의 `_sendGate` lock 안에서 직렬화한다.
  실제 `Release`는 lock 밖에서 수행해 producer/pump/close 가 queue mutation 에 대해서만 직렬화되도록 유지한다.
  close 는 남아 있는 pending datagram 만 drain 하고, 이미 evict 된 datagram 은 다시 만지지 않는다.
  drop 관측성(counter/log/metrics)은 hot path 비용과 public 진단 표면을 별도 판단해야 하므로 후속 단위로 둔다.

## D039 — TCP pending send queue 는 기본 drop-oldest capacity 로 제한한다

- 날짜: 2026-06-12
- 상태: Accepted
- 결정: `TransportConnection`의 pending send queue 에 기본 capacity 16을 적용한다. open connection 에서
  `TrySend`가 호출됐을 때 queue 가 이미 가득 차 있으면 가장 오래된 pending 항목을 dequeue 하고, 해당
  `TransportSendBuffer.Buffer`의 Transport 소유 ref 를 정확히 1회 `Release` 한 뒤 새 항목을 enqueue 한다.
  `TrySend` 자체는 새 항목을 수락했으므로 `true`를 반환한다.
- 근거: `.claude/review/overall-state-2026-06-11.md` H1과 D012가 지적한 대로 느린 소비자에서 pending queue 가
  무한 증가하면 C10K 목표와 맞지 않는다. drop-oldest 는 최신 publish 를 유지하면서 메모리 상한을 제공하고,
  evict 된 항목은 더 이상 socket 으로 보내지지 않으므로 Transport 가 소유 ref 를 반환해야 한다.
- 영향: evict 대상 선택과 queue 제거는 `TransportConnection`의 기존 `_gate` lock 안에서 직렬화한다.
  실제 `Release`는 queue mutation 뒤 lock 밖에서 수행해 producer/pump/close 의 구조적 직렬화는 유지하면서 lock 보유 시간을 줄인다.
  close 는 남아 있는 pending 만 drain 하고, 이미 evict 된 항목은 다시 만지지 않는다. UDP endpoint pending send queue 는 별도 단위에서
  같은 정책을 적용할지 검증한다.

## D038 — BrokerServer 는 Transport/Protocol/Broker wiring 과 accept loop 수명만 책임진다

- 날짜: 2026-06-12
- 상태: Accepted
- 결정: `Hps.Server.BrokerServer`를 Phase 3 TCP host 의 첫 진입점으로 둔다. 생성자는 테스트 가능성을 위해
  `ITransport`, `PinnedBlockMemoryPool`, `maxPayloadLength`를 주입받고, 내부에서 `SubscriptionTable`,
  `BrokerPublisher`, `BrokerTcpFrameHandler`, `TcpFrameReceiveHandler`를 조립한다. `StartTcpAsync`는
  `ITransport.SetReceiveHandler`로 TCP frame handler 를 등록한 뒤 `StartAsync`, `ListenTcpAsync`를 호출하고,
  listener 의 `AcceptAsync`를 반복하는 accept loop 를 시작한다. `StopAsync`/`Dispose`는 accept loop 를 깨우고
  listener 를 닫은 뒤 Transport 를 중지한다.
- 근거: Protocol/Broker 는 이미 계층별로 구현되어 있으므로 Server 계층이 별도 command parsing 또는 fan-out 경로를 만들면
  기존 소유권/프레이밍 결정(D009, D030, D037)을 우회하게 된다. Server 는 기존 구성요소를 연결하고 listener 수명만 관리하는
  얇은 조립 계층으로 두는 편이 책임 경계가 가장 명확하다.
- 영향: accepted connection 의 send/receive pump 와 connection tracking 은 Transport 구현이 계속 소유한다. Server accept loop 는
  반환된 `IConnection`을 저장하지 않고 다음 accept 를 걸어 새 연결 수락을 계속 가능하게 한다. 실제 socket end-to-end
  `SUBSCRIBE`/`PUBLISH` fan-out 검증과 `TransportFactory.CreateDefault()` 기반 convenience API 는 다음 단위에서 별도로 다룬다.

## D037 — Broker TCP frame handler 는 command decode 결과를 routing/fan-out 으로 연결한다

- 날짜: 2026-06-11
- 상태: Accepted
- 결정: `BrokerTcpFrameHandler`를 `ITcpFrameHandler` 구현체로 추가한다. `SUBSCRIBE` command 는 topic 을
  routing key string 으로 복사해 `SubscriptionTable.Subscribe`에 연결하고, `PUBLISH` command 는
  `TcpCommand.PayloadOffset`과 `Payload.Length`를 사용해 `BrokerPublisher.Publish(topic, frame, offset, length)`로 넘긴다.
  `OnConnectionClosed`는 `SubscriptionTable.UnsubscribeAll`을 호출한다.
- 근거: TCP frame payload 는 `PUBLISH <topic> <payload>` 전체가 하나의 `RefCountedBuffer`에 들어 있으므로,
  handler 가 payload 를 새 버퍼로 복사하면 D009의 TCP publish 1회 복사 원칙을 깨게 된다. payload span만으로는
  원본 buffer offset 을 알 수 없으므로 `TcpCommand`가 `PayloadOffset`을 제공하고, Broker는 command 문법을 다시 계산하지 않는다.
- 영향: handler 는 `OnFrame`에서 frame 소유권을 수락한 뒤 항상 내부에서 `Release`한다. malformed command 는 현재
  protocol error 응답이 없으므로 frame 을 회수하고 connection 을 닫는다. Server host 가 `TcpFrameReceiveHandler`와
  이 handler 를 실제 Transport 에 등록하는 작업은 다음 별도 단위로 남긴다.

## D036 — Broker subscription table 은 connection-wide cleanup API 를 제공한다

- 날짜: 2026-06-11
- 상태: Accepted
- 결정: `SubscriptionTable`에 `UnsubscribeAll(IConnection connection)`을 추가한다. 이 API 는 지정 connection 을
  모든 topic 의 구독자 set 에서 제거하고, 실제 제거된 구독 수를 반환한다. D008 NoCleanup 정책은 유지하므로
  빈 topic entry 자체는 제거하지 않는다.
- 근거: `.claude/review/overall-state-2026-06-11.md` H2가 지적한 대로 단명 연결이 churn 하는 서버에서는
  연결이 닫힌 뒤에도 topic set 에 dead `IConnection` 참조가 남으면 메모리와 `CountSubscribers`가 계속 증가한다.
  Transport/Protocol close 통지는 topic 이름을 알지 못하므로 Broker 쪽에 connection 단위 정리 경계가 필요하다.
- 영향: 이번 결정은 라우팅 테이블 API와 동작 테스트까지만 포함한다. 다음 TCP command handler 결선 단위는
  `ITcpFrameHandler.OnConnectionClosed`에서 이 API 를 호출해야 한다. drop-oldest/backpressure 정책은 D012의 별도 구현 단위로 남긴다.

## D035 — Broker publish fan-out 은 같은 RefCountedBuffer 안의 payload range 를 전송할 수 있어야 한다

- 날짜: 2026-06-11
- 상태: Accepted
- 결정: `BrokerPublisher`는 기존 전체 payload publish 외에 `Publish(string, RefCountedBuffer, int offset, int length)`를 제공한다.
  이 overload 는 같은 `RefCountedBuffer` 안의 지정된 offset/length 범위만 `TransportSendBuffer`로 전달한다. 범위는 구독자 snapshot
  전에 검증하며, 구독자가 0명인 topic 이라도 잘못된 offset/length 는 즉시 `ArgumentOutOfRangeException`으로 거부한다.
- 근거: TCP command frame 은 현재 `PUBLISH <topic> <payload>` 전체가 하나의 `RefCountedBuffer`로 조립된다. command handler 가
  실제 payload 만 fan-out 하려면 같은 buffer 의 payload slice 를 전송해야 하며, 여기서 새 `RefCountedBuffer`로 복사하면
  D009의 TCP publish 1회 복사 원칙을 깨고 추가 관리힙/I/O buffer 소유권 경계를 만든다.
- 영향: 다음 command handler 단위는 `TcpCommandDecoder`가 계산한 payload slice 를 이 ranged publish overload 로 넘겨야 한다.
  `BrokerPublisher`는 여전히 publish guard ref 를 해제하지 않으므로 handler 는 Publish 반환 뒤 원본 frame ref 를 Release 해야 한다.

## D034 — Broker publish fan-out 은 구독자별 AddRef 후 Transport.TrySend 계약을 따른다

- 날짜: 2026-06-11
- 상태: Accepted
- 결정: `BrokerPublisher.Publish(string, RefCountedBuffer)`는 `SubscriptionTable`의 현재 구독자 snapshot 을 읽고,
  구독자마다 같은 `RefCountedBuffer`에 `AddRef()`를 수행한 뒤 `ITransport.TrySend(IConnection, TransportSendBuffer)`로 넘긴다.
  `TrySend`가 `true`를 반환하면 Transport 가 해당 구독자 ref 1개를 소유한다. `false`를 반환하거나 send buffer 생성/전송 중
  예외가 발생하면 Broker 가 방금 추가한 구독자 ref 를 즉시 `Release()`한다. Publish 호출자가 보유한 publish guard ref 는
  이 메서드가 해제하지 않으며, 호출자가 Publish 반환 뒤 직접 `Release()`해야 한다.
- 근거: D009의 publish guard ref 모델과 `ITransport.TrySend` 소유권 계약을 그대로 결합하면 Broker fan-out 이 payload 를
  구독자 수만큼 복사하지 않고도 느린/닫힌 구독자에 대한 ref 누수를 피할 수 있다. `SubscriptionTable.CopySubscribers`는
  전체 구독자 수를 반환하므로, 최초 배열이 작으면 더 큰 `ArrayPool<IConnection>` buffer 로 snapshot 을 재시도한다.
- 영향: 이번 결정은 구독자별 송신 시도와 ref 반환 경계만 다룬다. drop-oldest/backpressure 정책, command handler,
  Server wiring, protocol error 응답은 별도 단위에서 결정한다. BrokerPublisher 는 publish guard 를 유지하므로 후속
  `TcpFrameReceiveHandler`/command handler wiring 은 Publish 호출 후 원본 frame ref 를 해제하는 책임을 명시해야 한다.

## D033 — Broker subscription routing 은 NoCleanup topic entry 로 시작한다

- 날짜: 2026-06-11
- 상태: Accepted
- 결정: Phase 3 Broker 의 첫 라우팅 테이블은 `SubscriptionTable`로 두며, topic 별 `IConnection` 구독자 set 을
  `ConcurrentDictionary`로 관리한다. `Subscribe`는 topic entry 를 `GetOrAdd`로 만들고, `Unsubscribe`는 connection 만 제거한다.
  구독자 set 이 비어도 topic entry 를 즉시 제거하지 않는다(NoCleanup). Publish fan-out 이 사용할 snapshot 경계는
  `CopySubscribers(string, IConnection[])`로 제공해 caller 가 준비한 배열에 현재 구독자를 복사하고 전체 구독자 수를 반환한다.
- 근거: `.claude/review/phase3-broker-routing.md`의 R1 실측에서 빈 topic eager-cleanup 과 동시 subscribe 가 겹치면
  새 구독자가 제거된 set 에 들어가 라우팅 테이블에서 유실됐다. NoCleanup 은 hot path 에 추가 lock 을 넣지 않으면서
  해당 경합을 제거한다. connection 은 handle identity 가 중요하므로 reference equality 로 비교한다.
- 영향: topic key 누적이 실제 문제가 되기 전까지 즉시 cleanup 을 추가하지 않는다. 필요해지면 "비어 있고 일정 시간 미사용"인
  entry 만 별도 안전 sweep 으로 제거하는 단위를 설계한다. 다음 Broker 단위는 이 routing table 위에서 publish fan-out,
  송신 enqueue 실패 release, 느린 소비자 정책을 붙인다.

## D032 — TCP frame handler dispatch 실패는 frame 회수 후 connection close 로 정리한다

- 날짜: 2026-06-11
- 상태: Accepted
- 결정: `ITcpFrameHandler.OnFrame`이 정상 반환한 뒤에만 frame 소유권이 handler 로 이전된 것으로 본다.
  `OnFrame`이 예외를 던지면 `TcpFrameReceiveHandler`가 해당 `RefCountedBuffer`를 `Release()`하고 connection 을 닫는다.
  또한 `PayloadTooLarge`, handler 실패, Transport close 알림이 겹쳐도 `ITcpFrameHandler.OnConnectionClosed`는 connection 별
  한 번만 호출한다. 이 1회 통지 표식은 `ConditionalWeakTable<IConnection, ...>`에 저장해 단명 connection 을 강하게 붙잡지 않는다.
- 근거: 완성 frame 을 handler 로 넘기는 경계에서 예외가 발생하면 frame 을 실제로 fan-out 경로가 수락했는지 알 수 없다.
  이때 frame 을 방치하면 recv loop 중단과 함께 `RefCountedBuffer`가 누수될 수 있다. 반대로 connection 을 계속 열어 두면
  같은 TCP stream 의 protocol 상태가 불명확해진다. connection close 통지는 Transport 구현이 직접/간접으로 다시 보낼 수 있으므로
  Protocol 어댑터가 자체적으로 멱등성을 보장해야 backend 구현 세부에 덜 의존한다.
- 영향: 이후 Broker frame handler 는 `OnFrame`에서 frame 을 수락한 뒤 예외를 던지지 않아야 한다.
  수락 이후 내부 fan-out 실패는 handler 내부에서 ref 를 정리하고 정책에 따라 connection 을 닫거나 error 응답을 보내야 한다.

## D031 — TCP command decode 는 frame payload 의 span view 로 해석한다

- 날짜: 2026-06-11
- 상태: Accepted
- 결정: TCP frame payload 의 첫 token 은 ASCII command 로 해석한다. 현재 command 는 `SUBSCRIBE <topic>`과
  `PUBLISH <topic> <payload>` 두 가지로 제한한다. topic 은 비어 있지 않은 단일 token 이며 공백을 포함하지 않는다.
  `PUBLISH`의 payload 는 두 번째 공백 뒤의 나머지 전체 byte 로 유지하며, 비어 있을 수 있고 payload 내부 공백도 보존한다.
  `TcpCommand`는 원본 frame 을 복사하지 않는 `readonly ref struct` span view 이므로 caller 는 frame buffer 를 Release 하기 전
  동기 범위에서만 사용해야 한다. malformed input 은 정상 흐름 제어이므로 예외 대신 `false`와 `TcpCommandDecodeError`로 반환한다.
- 근거: TCP 프레이밍은 이미 소유권 있는 `RefCountedBuffer` frame 을 만든다(D029/D030). command decode 단계에서 topic/payload 를
  다시 복사하면 pub/sub fan-out 전에 불필요한 관리힙 복사가 생긴다. `ref struct` view 로 제한하면 frame 수명 밖 저장을
  컴파일러가 막아 소유권 경계를 더 명확하게 만든다.
- 영향: 이후 Broker/Server wiring 은 `TcpFrameReceiveHandler`가 넘긴 frame 을 decode 한 뒤, command lifetime 안에서 topic 을
  라우팅 키로 해석하거나 필요한 경우 명시적으로 복사해야 한다. 실제 subscription table, publish fan-out, protocol error 응답은
  후속 Phase 3 단위로 남긴다.

## D030 — TCP raw receive 는 TcpFrameReceiveHandler 어댑터가 frame 콜백으로 변환한다

- 날짜: 2026-06-11
- 상태: Accepted
- 결정: `Hps.Protocol`은 `Hps.Transport`의 public abstraction(`ITransportReceiveHandler`, `IConnection`,
  `TransportReceiveBuffer`)만 참조해 TCP raw receive chunk 를 처리한다. `TcpFrameReceiveHandler`가 connection 별
  `TcpFrameAssembler`를 소유하고, 완성된 `RefCountedBuffer` frame 은 `ITcpFrameHandler.OnFrame`으로
  소유권을 이전한다. `PayloadTooLarge`는 D010 계약대로 해당 connection 을 즉시 `Close()`하고
  `ITcpFrameHandler.OnConnectionClosed`로 상위 계층에 알린다.
- 근거: `TcpFrameAssembler`는 독립 상태머신이므로 실제 Transport receive pump 에 연결하는 얇은 어댑터가 필요하다.
  이를 Transport 내부에 넣으면 Transport 가 Protocol 을 알게 되어 계층 경계가 뒤집힌다. 반대로 Protocol 이
  Transport public abstraction 만 참조하면 SAEA/RIO/io_uring backend 는 계속 숨겨진다.
- 영향: 이후 Server/Broker 는 `TcpFrameReceiveHandler`를 `ITransport.SetReceiveHandler`에 등록하고,
  command codec 은 `ITcpFrameHandler` 뒤에서 frame payload 를 해석한다. command codec, Broker routing,
  Server wiring 은 별도 Phase 3 단위로 남긴다.

## D029 — TCP 프레임 조립은 TcpFrameAssembler per-connection 상태 객체로 시작한다

- 날짜: 2026-06-11
- 상태: Accepted
- 결정: Phase 3 Protocol 계층의 첫 TCP 프레이밍 단위는 `TcpFrameAssembler`로 둔다.
  assembler 는 connection 마다 하나씩 생성되는 상태 객체이며, `TryReadFrame(ReadOnlySpan<byte>, out int consumed, out RefCountedBuffer? frame)`로
  Transport 가 전달한 raw byte chunk 를 소비한다. 4바이트 big-endian payload length header 를 누적한 뒤,
  payload 는 `PinnedBlockMemoryPool.RentCounted()`로 얻은 `RefCountedBuffer`에 복사한다.
  `FrameReady`가 반환되면 frame 소유권은 caller 에게 넘어가며 caller 가 `Release()` 해야 한다.
  payload 조립 중 connection 이 닫히면 `Dispose()`가 partial payload buffer 를 반환한다.
- 근거: Transport receive buffer 는 borrowed view 라 콜백 밖으로 저장할 수 없다(D020). 따라서 TCP publish payload 는
  Protocol 계층에서 소유권 있는 `RefCountedBuffer`로 한 번 복사해야 한다(D009/D010). 이 객체를 connection 단위로 두면
  header 분할, payload 분할, close 시 partial release 책임을 한 곳에 모을 수 있다.
- 영향: 현재 기준선은 fragmented header/payload, maxPayload 초과 거부, partial payload dispose 반환을 검증한다.
  여러 frame 이 한 chunk 에 붙는 경우, 0 length frame, 적대적 chunk fuzz, 실제 command codec 연결은 후속 Phase 3 단위에서 확장한다.

## D028 — UDP send 는 endpoint별 pending queue 와 단일 pump 로 직렬화한다

- 날짜: 2026-06-11
- 상태: Accepted
- 결정: `ITransport.TrySendTo`가 true 를 반환할 때 Transport 는 `TransportSendBuffer`의 ref 1개를 소유하되,
  datagram 마다 독립 `Task.Run`을 만들지 않는다. `SaeaUdpEndpoint`가 endpoint 단위 pending send queue 를 보유하고,
  bind 된 endpoint 당 단일 send pump 가 queue 를 순차적으로 drain 한다. endpoint close 는 아직 pump 가 가져가지 않은
  queued datagram 을 drain 하며 각 `RefCountedBuffer`를 정확히 한 번 `Release()`한다.
- 근거: UDP 는 연결이 없어 TCP `TransportConnection`을 그대로 쓸 수 없지만, 고빈도 publish 에서 datagram 마다 task 를 만들면
  thread-pool 이 사실상의 unbounded send queue 가 된다. endpoint별 queue 와 단일 pump 는 S2 검토 의견의 thread-pool flooding
  위험을 줄이고, TCP 송신 경로와 같은 pending/in-flight/close 소유권 경계를 제공한다.
- 영향: SAEA 기준선의 UDP send 는 endpoint queue 를 거친다. 이후 RIO/io_uring UDP backend 도 `TrySendTo` 성공 후
  completion/drop/close 중 정확히 한 경로에서 ref 를 반환해야 한다. UDP receive prefetch 경계는 D046에서 별도 결정했다.

## D027 — Hps.Transport 파일은 Abstractions/Runtime/Saea 책임 축으로 배치한다

- 날짜: 2026-06-11
- 상태: Accepted
- 결정: `src/Hps.Transport`의 flat 파일 배치를 `Abstractions/`, `Runtime/`, `Saea/` 하위 폴더로 분리한다.
  `Abstractions/`에는 public 계약, receive/send buffer view, handler, endpoint 타입을 둔다.
  `Runtime/`에는 backend 공통 기반인 `TransportBase`, connection 상태/큐인 `TransportConnection`, 기본 생성 진입점인 `TransportFactory`를 둔다.
  `Saea/`에는 크로스플랫폼 SAEA/raw Socket 기준선 구현과 그 내부 listener/UDP endpoint 를 둔다.
  테스트도 같은 책임 축으로 `Contracts/`, `Runtime/`, `Saea/`에 배치한다.
- 근거: Phase 2가 진행되면서 TCP/UDP public 계약, 공통 소유권 런타임, SAEA 구현이 같은 폴더에 섞여 탐색 비용이 커졌다.
  namespace 를 바꾸면 public API 와 using 변경이 불필요하게 커지므로, 이번 구조 정리는 파일 경로만 바꾸고 namespace 는 유지한다.
- 영향: 이후 새 Transport public 계약은 `Abstractions/`, backend 공통 소유권/생성 로직은 `Runtime/`, SAEA 기준선 세부 구현은 `Saea/`에 추가한다.
  RIO/io_uring은 별도 프로젝트에 둘 계획을 유지한다.

## D026 — 기본 Transport 생성 진입점은 TransportFactory.CreateDefault 로 둔다

- 날짜: 2026-06-11
- 상태: Accepted
- 결정: 상위 계층은 concrete backend 를 직접 선택하지 않고 `TransportFactory.CreateDefault()`를 통해
  `ITransport`를 얻는다. 현재 Phase 2 기준선에서는 RIO/io_uring capability probe 가 없으므로 모든 환경에서
  `SaeaTransport`를 반환한다. 반환 instance 의 수명은 호출자가 소유하며 `Dispose()` 해야 한다.
- 근거: AGENTS 아키텍처 불변식 6은 OS별 backend 를 `ITransport` 뒤에 숨기도록 요구한다. 지금 바로
  `ITransportSelector` 같은 별도 abstraction 을 추가하면 probe 대상과 옵션이 아직 없는 상태에서 타입만 늘어난다.
  정적 factory 하나는 상위 계층의 `new SaeaTransport()` 의존을 줄이면서, 이후 Windows RIO와 Linux io_uring probe 를
  같은 위치에 추가할 수 있는 가장 작은 계약이다.
- 영향: `src/Hps.Transport.Rio/`와 `src/Hps.Transport.IoUring/`이 실제 구현되기 전까지는 factory 가 SAEA fallback 을 유지한다.
  이후 backend probe 를 추가할 때도 public 호출자는 `ITransport`만 보게 하며, 테스트는 현재 fallback 계약과 미래 capability 분기 계약을 나눠 검증한다.

## D025 — UDP datagram handler 호출 시점에 receive loop 의 Release 책임을 끊는다

- 날짜: 2026-06-11
- 상태: Accepted
- 결정: `SaeaTransport` UDP receive loop 는 `RefCountedBuffer.SetLength` 이후 handler 를 호출하기 전에
  local `datagram` 참조를 null 로 끊고, 별도 `ownedDatagram` 값으로 `DispatchDatagramReceived`에 넘긴다.
  handler 가 등록되어 있으면 호출 시점부터 datagram Release 책임은 `ITransportDatagramHandler` 계약으로 넘어간다.
  handler 가 없으면 `DispatchDatagramReceived`가 즉시 Release 한다. socket receive 전 예외, socket dispose, socket error 처럼
  handler 로 이전되기 전의 실패만 receive loop catch 가 Release 한다.
- 근거: D024에서 UDP handler 는 owned `RefCountedBuffer`를 받는다고 결정했다. 그런데 dispatch 호출 뒤에야 local 참조를 null 로 끊으면,
  handler 가 버퍼를 Release 한 뒤 예외를 던지는 경우 receive loop catch 가 같은 ref 를 다시 Release 하려 한다.
  `RefCountedBuffer`의 이중 반환 가드가 손상은 막지만, 원래 handler 예외가 double-release 예외로 덮이고 소유권 경계가 흐려진다.
- 영향: 이후 SAEA/RIO/io_uring UDP receive 구현은 handler 호출을 소유권 이전 지점으로 취급해야 한다.
  handler 예외 정책은 별도 안정화 단위에서 다룰 수 있지만, 이미 넘긴 datagram 을 Transport catch 경로가 다시 Release 해서는 안 된다.

## D024 — UDP datagram 은 IUdpEndpoint 와 RefCountedBuffer 소유권 handler 로 분리한다

- 날짜: 2026-06-11
- 상태: Accepted
- 결정: UDP public 계약은 TCP listener/connection accept 모델과 섞지 않고 `IUdpEndpoint` 수명 핸들로 분리한다.
  `ITransport.BindUdpAsync(EndPoint, CancellationToken)`는 local UDP endpoint 를 만들고,
  `ITransport.TrySendTo(IUdpEndpoint, EndPoint, TransportSendBuffer)`는 원격 endpoint 로 datagram 을 보낸다.
  수신은 `ITransport.SetDatagramHandler(ITransportDatagramHandler)`로 등록한 handler 에
  `OnDatagramReceived(IUdpEndpoint, EndPoint, RefCountedBuffer)`를 호출하는 방식으로 전달한다.
  handler 는 전달받은 `RefCountedBuffer`의 소유권을 가지며, 처리가 끝나면 직접 `Release()`해야 한다.
  `TrySendTo`가 true 를 반환하면 Transport 가 `TransportSendBuffer.Buffer`의 ref 1개를 소유하고 send completion, socket error,
  close unwind 중 하나의 경로에서 정확히 한 번 `Release()`한다. false 를 반환하면 호출자가 Release 책임을 유지한다.
- 근거: UDP는 accept 된 connection 이 없고, `1 datagram = 1 message` 이므로 TCP byte stream receive callback 과 같은 borrowed span 계약을
  억지로 재사용하면 소유권 경계가 흐려진다. D009에서 UDP publish 는 datagram 을 `RefCountedBuffer`로 직접 recv 하는 zero-copy 경로를
  선택했으므로 Transport datagram handler 가 처음부터 owned counted buffer 를 받는 것이 이후 Protocol/Broker fan-out 경계와 맞다.
  OS별 backend 는 계속 `ITransport` 뒤에 숨겨야 하므로 public 계약에는 `Socket`이나 `SocketAsyncEventArgs` 같은 backend 타입을 노출하지 않는다.
- 영향: `SaeaTransport` 기준선은 UDP socket 을 bind 하고 receive loop 에서 pinned counted block 을 직접 대여해 handler 로 넘긴다.
  이후 RIO/io_uring backend 도 같은 `IUdpEndpoint`/`ITransportDatagramHandler` 계약을 구현해야 한다. UDP 신뢰성, 순서 보장, 혼잡 제어는
  여전히 범위 밖이며, backend selector 는 다음 Phase 2 단위에서 SAEA fallback 기준선으로 다룬다.

## D023 — SAEA TCP send 기준선은 connection별 단일 raw Socket loop 로 pending 을 drain 한다

- 날짜: 2026-06-11
- 상태: Accepted
- 결정: `SaeaTransport`의 첫 TCP send 구현은 connection 생성 시 단일 send loop 를 시작하고,
  `TransportConnection.WaitForSendSignalAsync()`로 pending 항목 또는 close 신호를 기다린다.
  pending 항목은 `TransportConnection.TryBeginInFlightSend(out InFlightSend?)`로만 가져오며,
  실제 socket send 가 끝나면 `InFlightSend.Complete()`를 호출한다. socket dispose, socket error, close unwind 로
  completion 까지 가지 못한 항목은 `using`/`Dispose()` 경로가 Transport 소유 ref 를 반환한다.
  첫 기준선은 프레이밍 없이 `TransportSendBuffer.Offset/Length` 범위만 전송한다.
- 근거: D015-D017에서 이미 송신 소유권은 `ITransport.TrySend` 성공 시 Transport 가 ref 1개를 소유하고,
  pending 과 in-flight 반환 책임을 분리하는 것으로 정했다. concrete SAEA 구현은 이 경계를 우회하지 않고
  실제 socket I/O만 붙여야 한다. 또한 `RefCountedBuffer.Memory`는 전체 블록을 노출하므로 send pump 는 반드시
  `TransportSendBuffer`의 범위만 전송해 Length 바깥 바이트가 새지 않게 해야 한다.
- 영향: 이후 명시적인 SocketAsyncEventArgs, RIO, io_uring completion 구현도 raw `TransportSendBuffer`를 직접 release 하지 말고
  `InFlightSend` handle 의 `Complete()`/`Dispose()` 경로를 재사용해야 한다. backpressure, drop-oldest, 프레이밍은 별도 단위로 남긴다.

## D022 — 닫힌 SAEA connection 은 transport 추적 목록에서 즉시 제거한다

- 날짜: 2026-06-11
- 상태: Accepted
- 결정: `SaeaTransport`가 만든 `TransportConnection`은 `Close()`가 처음 성공할 때 transport unregister callback 을 호출한다.
  이 callback 은 `_connections` 추적 목록에서 해당 connection 을 제거한다. `StopCore()`가 이미 snapshot 을 만들고 목록을 비운 뒤
  connection 을 닫는 경로에서는 remove 가 no-op 이므로 같은 idempotent close 계약을 유지한다.
  `TransportConnection.Close()`는 closed 표시와 pending send drain 만 connection lock 안에서 처리하고,
  unregister callback 과 backend socket dispose 는 lock 밖에서 수행한다.
- 근거: listener 는 `Close()`에서 transport 목록에서 제거되지만 connection 은 등록만 되고 제거되지 않아,
  단명 TCP 연결이 반복되는 서버에서 닫힌 `TransportConnection`과 dispose 된 `Socket` 참조가 transport 수명 내내 누적될 수 있었다.
  이 문제는 send/recv pump 의 후속 범위가 아니라 이미 등록된 자원의 수명 대칭이 깨진 결함이므로 다음 기능 전에 해소해야 한다.
  또한 socket dispose 는 backend 외부 호출이므로 connection lock 안에서 오래 머무르지 않게 분리한다.
- 영향: 이후 accept loop 와 connect churn 테스트는 개별 connection close 후 transport 추적 수가 감소한다는 전제를 가진다.
  송신 pending/in-flight release 계약(D016, D017)은 그대로 유지하며, unregister 는 buffer 소유권 release 를 대체하지 않는다.

## D021 — SAEA TCP recv 기준선은 pinned block 으로 raw chunk 만 전달한다

- 날짜: 2026-06-11
- 상태: Accepted
- 결정: `SaeaTransport`의 첫 TCP receive 구현은 connection 생성 시 receive loop 를 시작하고,
  `PinnedBlockMemoryPool`에서 대여한 고정 receive block 으로 socket bytes 를 읽은 뒤
  `ITransportReceiveHandler.OnReceived(IConnection, TransportReceiveBuffer)`에 raw TCP byte chunk 를 전달한다.
  `TransportReceiveBuffer`는 동기 dispatch helper 안에서만 만들고 async receive loop 안에 저장하지 않는다.
  remote close 또는 socket error 는 `OnConnectionClosed`를 호출하고 `IConnection.Close()` 경로로 연결을 정리한다.
- 근거: Phase 2의 목표는 Transport 가 실제 socket I/O 경계를 갖는지 검증하는 것이다. 여기서 TCP framing,
  publish payload `RefCountedBuffer` 조립, broker fan-out 을 함께 넣으면 Phase 3 책임이 섞인다.
  또한 receive block 은 콜백 이후 재사용되어야 하므로 D020의 borrowed view 계약을 그대로 지켜야 한다.
- 영향: 다음 단위는 송신 방향이다. `SaeaTransport` send pump 는 `TransportConnection.TryBeginInFlightSend`로 pending 항목을 가져와
  socket send 를 수행하고, completion/unwind 에서 in-flight handle 을 완료/Dispose 해 ref 를 반환해야 한다.
  명시적인 SocketAsyncEventArgs 최적화와 프레이밍은 후속 단위로 유지한다.

## D020 — Transport 수신 전달은 borrowed ref struct 콜백으로 제한한다

- 날짜: 2026-06-11
- 상태: Accepted
- 결정: Transport 수신 경계는 `ITransport.SetReceiveHandler(ITransportReceiveHandler)`로 등록한 단일 handler 에
  `TransportReceiveBuffer`를 동기 전달하는 방식으로 한다. `TransportReceiveBuffer`는 `readonly ref struct`이며
  `ReadOnlySpan<byte>`와 `Length`만 노출한다. handler 는 콜백이 반환되기 전까지 span 을 즉시 처리해야 하며,
  콜백 이후에도 필요한 데이터는 Protocol 계층이 자신의 소유권 버퍼로 복사한다. 연결 종료 알림은
  `ITransportReceiveHandler.OnConnectionClosed(IConnection)`로 전달해 조립 중인 버퍼 release 시점을 제공한다.
- 근거: receive ring 또는 pinned receive block 을 `Memory<byte>`로 public 계약에 넘기면 상위 계층이 콜백 밖으로
  저장할 수 있어 Transport 의 버퍼 재사용/반환 책임이 흐려진다. `ref struct` borrowed view 는 async/heap 저장을
  언어 차원에서 막아 D010의 TCP parser 가 즉시 소비하거나 D009의 payload buffer 로 복사하는 흐름을 강제한다.
- 영향: 다음 `SaeaTransport` recv pump 구현은 socket recv 결과를 이 handler 로 전달해야 한다. 이 단계에서는 raw TCP
  byte stream chunk 전달까지만 담당하고, 프레이밍/메시지 조립/팬아웃 소유권은 Phase 3의 Protocol/Broker 단위에서 처리한다.
  public `IConnection`에는 receive 메서드를 추가하지 않는다.

## D019 — SAEA TCP 기준선은 socket 수명을 TransportConnection.Close에 묶는다

- 날짜: 2026-06-11
- 상태: Accepted
- 결정: `SaeaTransport`의 첫 concrete 구현 단위는 TCP listen/connect/accept 수명까지만 다룬다.
  listen socket 은 `SaeaConnectionListener`가 소유하고, accepted/outbound socket 은 `TransportConnection`의 backend resource 로 묶어
  `IConnection.Close()`/`Dispose()`에서 함께 닫는다. `SaeaTransport.StopAsync()`와 `Dispose()`는 등록된 listener 와 connection 을
  idempotent close 경로로 정리한다.
- 근거: 연결 객체가 실제 socket 을 닫지 않으면 D011의 종료 계약을 이후 payload I/O에 붙일 때 책임 경계가 흐려진다.
  반대로 이번 단위에서 SocketAsyncEventArgs send/recv 펌프까지 함께 넣으면 리뷰 범위가 커지고, 아직 정해지지 않은
  receive delivery 계약까지 암묵적으로 고정될 위험이 있다.
- 영향: 다음 단위는 TCP payload I/O 구현 전에 receive delivery 계약과 pinned receive buffer 소유권을 먼저 확정해야 한다.
  `SaeaTransport`라는 이름은 크로스플랫폼 기준선을 의미하며, 이번 단위의 connect/accept 는 socket 수명 기준선이다.
  실제 SocketAsyncEventArgs 기반 send/recv 버퍼 운용은 후속 단위에서 이 연결 수명 위에 붙인다.

## D018 — TCP 연결 획득은 Listener 기반 Listen/Accept와 Connect로 분리한다

- 날짜: 2026-06-11
- 상태: Accepted
- 결정: `ITransport`는 TCP 연결 획득을 `ListenTcpAsync(EndPoint, CancellationToken)`와
  `ConnectTcpAsync(EndPoint, CancellationToken)`로 노출한다. `ListenTcpAsync`는 `IConnectionListener`를 반환하고,
  listener 는 `LocalEndPoint`, `AcceptAsync(CancellationToken)`, `Close`/`Dispose`만 책임진다.
  accept 또는 connect 로 얻은 연결은 모두 동일한 `IConnection` 수명/송신 계약을 따른다.
- 근거: listen socket 의 수명과 개별 connection 수명을 분리해야 close/drain 책임이 흐려지지 않는다.
  또한 `Socket`, `SocketAsyncEventArgs`, RIO, io_uring 같은 backend 세부 타입이 public 계약으로 새면
  상위 Protocol/Broker 계층이 OS별 구현을 알게 된다. UDP datagram 은 accept 개념이 없으므로 이 TCP 계약에
  억지로 포함하지 않고 별도 단위에서 추가한다.
- 영향: 다음 `SaeaTransport` 구현은 이 계약을 기준으로 TCP loopback listen/connect/accept 테스트를 먼저 통과시켜야 한다.
  listener 가 닫혀도 이미 accept 되어 반환된 연결의 종료는 각 `IConnection.Close()` 경로가 책임진다.
  포트 0 listen 테스트와 샘플은 요청 endpoint 가 아니라 `IConnectionListener.LocalEndPoint`를 사용해 connect 해야 한다.

## D017 — in-flight 송신 항목은 handle 의 Complete/Dispose 경로에서 release 한다

- 날짜: 2026-06-10
- 갱신: 2026-06-11 (`TryDequeueSend` raw 값 반환 대신 `InFlightSend` handle 로 abandon-leak 방어)
- 상태: Accepted
- 결정: 송신 펌프는 pending 큐에서 raw `TransportSendBuffer`를 직접 꺼내지 않고,
  `TransportConnection.TryBeginInFlightSend(out InFlightSend?)`로 dispose 가능한 in-flight handle 을 얻는다.
  정상 completion callback 은 `InFlightSend.Complete()`를 호출하고, close/취소/예외 unwind 경로는 `InFlightSend.Dispose()`를 호출한다.
  두 경로는 같은 release 경로를 타며 `Interlocked.Exchange`로 해당 `TransportSendBuffer.Buffer`를 정확히 한 번만 `Release`한다.
- 근거: D016에서 pending 과 in-flight 의 반환 책임을 분리했다. completion callback 마다 직접 `Release`를 흩뿌리면
  close/drain 과의 책임 경계가 다시 흐려지고, 이후 실패/취소/unwind 경로마다 반환 누락이 생기기 쉽다.
  또한 펌프가 dequeue 후 close 되어 completion 없이 빠져나가면 raw 값만으로는 abandon-leak 를 막기 어렵다.
  handle 로 감싸면 정상 완료와 unwind/finally 가 같은 idempotent release 규칙을 재사용할 수 있다.
- 영향: 이후 실제 송신 펌프나 SAEA/RIO/io_uring 구현은 `TryBeginInFlightSend`로 얻은 handle 을 try/finally 범위에서 보유해야 한다.
  socket completion 에서 `Complete()`를 호출하더라도 finally 의 `Dispose()`가 다시 실행될 수 있으므로 handle release 는 idempotent 해야 한다.
  `Close()`는 pending 만 drain 하고, 이미 begin 된 in-flight ref 는 handle 이 반환한다.

## D016 — Transport close 는 pending 만 drain 하고 in-flight 는 펌프 완료 경로가 release 한다

- 날짜: 2026-06-10
- 상태: Accepted
- 결정: `TransportBase.TrySend`가 수락한 송신 항목은 내부 `TransportConnection` pending queue 에 들어간다.
  단, pending 큐에 넣기 전 `TransportSendBuffer`가 live `RefCountedBuffer`를 가리키는지 먼저 확인한다.
  `TransportSendBuffer`는 struct 이므로 생성자를 거치지 않은 default 값이 들어올 수 있고, 이 값은 수락 경계에서 즉시 거부한다.
  `TransportConnection.Close()`는 closed 표시와 pending drain 을 같은 lock 안에서 처리하며, pending queue 에 남은
  `TransportSendBuffer.Buffer`만 `Release`한다. 송신 펌프가 이미 `TryBeginInFlightSend`로 가져간 in-flight 항목은
  close 가 release 하지 않고, 이후 펌프 handle 의 completion/unwind 경로가 정확히 한 번 release 한다.
- 근거: D011은 pending 과 in-flight 를 모두 누수 없이 release 하라고 요구하지만, 같은 항목을 close 와 펌프가
  동시에 release 하면 이중 반환이 된다. pending drain 과 pump dequeue 를 같은 lock 으로 직렬화하면 항목의 현재
  소유자가 pending queue 인지 pump 인지 분명해진다.
- 영향: 다음 Phase 2 단위는 in-flight completion release 경로를 구현해야 한다. SAEA/RIO/io_uring completion callback 은
  이 경로를 재사용해야 하며, drop-oldest evict release(D012)는 별도 backpressure 단위로 구현한다.

## D015 — Transport 송신 시도 계약은 ITransport.TrySend 기반으로 한다

- 날짜: 2026-06-10
- 상태: Accepted
- 결정: 송신 시도와 버퍼 소유권 판정은 `IConnection`이 아니라 `ITransport.TrySend(IConnection, TransportSendBuffer)`에 둔다.
  `IConnection`은 연결 핸들과 수명(`Close`/`Dispose`)에 집중한다. public 송신 계약은 raw `Memory<byte>`/
  `ReadOnlyMemory<byte>`를 받지 않고, `RefCountedBuffer + offset + length`를 담은 `TransportSendBuffer`를 받는다.
  `TrySend` 성공 시 Transport가 해당 버퍼 참조 1개를 소유하며, 송신 완료·drop·close drain 중 정확히 한 곳에서
  `Release`한다. 실패 시 Transport는 소유권을 갖지 않으므로 호출자가 즉시 `Release`한다.
- 근거: D007/D011에 따라 Transport는 커널 등록 버퍼 출처와 refcount 반환 책임을 알아야 한다. raw Memory를
  받으면 RIO/io_uring 등록 식별, 송신 완료 반환, close 시 pending drain 책임이 모호해진다. 또한 큐라는 내부 구현
  세부사항을 `IConnection`에 노출하면 연결 핸들의 책임이 넓어진다.
- 영향: Phase 2 이후 concrete transport/send queue 구현은 이 계약을 따라야 한다. 테스트는 `IConnection`에
  `TransportSendBuffer` parameter 가 다시 들어오지 않는지, `ITransport` public 메서드에 raw Memory parameter 가
  들어오지 않는지 확인한다. listen/connect/accept endpoint 모델은 SAEA 기준선 구현 단위에서 별도 테스트와 함께 확정한다.

## D014 — 테스트에는 검증 의도 주석을 남긴다

- 날짜: 2026-06-10
- 상태: Accepted
- 결정: 테스트 메서드에는 무엇을 검증하는지와 왜 필요한지를 한국어 주석으로 남긴다.
  주석은 테스트 이름을 반복하지 않고, 보호하려는 불변식, 회귀 사례, 경계 조건, 동시성/소유권 가정을 설명한다.
- 근거: 테스트가 많아질수록 이름만으로는 리뷰어와 다음 작업자가 해당 테스트의 의도와 유지 기준을 빠르게 파악하기 어렵다.
- 영향: 새 테스트를 추가할 때는 Red 단계부터 테스트 의도 주석을 함께 작성한다. 기존 테스트도 수정 범위에 들어오면
  해당 테스트가 보호하는 동작을 주석으로 보강한다.

## D013 — 구현은 작은 기능 단위로 끊고 사용자 리뷰 뒤 다음 단위로 진행한다

- 날짜: 2026-06-10
- 상태: Accepted
- 결정: 한 사이클은 작고 리뷰 가능한 단일 기능 단위만 구현·검증·문서화한다. 독립 기능, 보강 테스트,
  리팩터링, 문서 정리는 한 번에 묶지 않고 별도 사이클로 나눈다. 기능 단위 완료 후에는 관련 파일만 stage 하여
  단일 커밋으로 남기고, 다음 단위로 자동 진행하지 않고 사용자 검토와 명시적 진행 지시를 기다린다.
- 근거: 한 번에 많은 수정이 들어가면 리뷰 비용이 커지고 결함 원인 추적이 어려워진다.
- 영향: `CURRENT_PLAN.md`는 다음 단일 리뷰 단위만 표현해야 한다. 기존 계획에 여러 독립 작업이 섞여 있으면
  현재 단위를 제외한 항목은 `TODOS.md`의 `Deferred Backlog`로 내려 명확히 기록한다. 커밋 전에는
  `git status`로 의도한 파일만 stage 되었는지 확인하고, unrelated 변경은 커밋에 포함하지 않는다.

## D001 — Phase 순서는 `PLAN.md`를 따른다

- 날짜: 2026-06-10
- 상태: Accepted
- 결정: Phase 1~4에서 크로스플랫폼 기준선 브로커를 먼저 완성하고, Phase 5~6에서 RIO/io_uring 백엔드를 붙인다.
- 근거: 메모리 계층, Transport 계약, Protocol/Broker 동작을 먼저 안정화해야 OS별 P/Invoke 백엔드의 회귀를 통합 테스트로 잡을 수 있다.
- 영향: RIO/io_uring 구현은 Phase 1~4 완료 전에는 착수하지 않는다.

## D002 — 사용자 성능 목표를 초기 기준선으로 기록한다

- 날짜: 2026-06-10
- 상태: Accepted
- 결정: 우선 목표는 4096 bytes 메시지를 100 Hz로 지연 누적 없이 처리하는 것이다.
- 근거: 사용자가 명시한 목표이며, 설계와 벤치마크의 중심 기준이 된다.
- 영향: Phase 1~3에서는 복사 최소화, pinned pool, fan-out zero-copy 불변식을 이 목표를 위한 구조적 제약으로 유지한다. Phase 4에서는 이 목표를 p50/p99 latency, throughput, queue backlog로 측정 가능하게 만든다.

## D003 — `dotnet test` green만으로 Phase 완료를 인정하지 않는다

- 날짜: 2026-06-10
- 상태: Accepted
- 결정: 테스트가 discover되지 않는 상태의 `dotnet test` 성공은 완료 기준으로 인정하지 않는다.
- 근거: 현재 `tests/Hps.Buffers.Tests`에 실제 테스트 `.cs` 파일이 없고, 테스트 0개 상태가 성공 종료 코드처럼 보일 수 있다.
- 영향: 각 Phase 완료 시 필수 테스트가 실제 discover되고 실행됐는지 확인한다.

## D004 — 다음 구현 작업은 BipBuffer must-fix TDD로 제한한다

- 날짜: 2026-06-10
- 상태: Accepted (D005로 갱신 — must-fix가 2건으로 늘어남)
- 결정: 다음 코드 변경은 `BipBuffer` M1 deadlock 재현 테스트와 최소 수정으로 제한한다.
- 근거: `.claude/review/phase1-bipbuffer.md`에 must-fix가 있으며, `BipBuffer`는 이후 pool, transport, protocol의 기반이다.
- 영향: `PinnedBlockMemoryPool`, `RefCountedBuffer`, `Hps.Transport`는 BipBuffer 테스트가 green이 될 때까지 착수하지 않는다.

## D005 — BipBuffer must-fix는 M1, M2 두 건이다 (실측 검증됨)

- 날짜: 2026-06-10
- 상태: Accepted (D004 갱신)
- 결정: `BipBuffer`는 두 건을 모두 고친다.
  - **M1**(단일스레드 deadlock): `Commit()`에서 `_write == _capacity` 상태를 저장하지 말고 즉시 wrap.
  - **M2**(크로스스레드 over-read): `GetReadSpan()`의 **반환 Span 길이**를 `Volatile.Read(ref _count)`
    이하로 제한(clamp)하고, "소비자는 데이터를 처리한 뒤에만 `Consume` 호출" 계약을 XML doc에 명시.
- 근거: 임시 하니스 실측에서 M1(데드락)과 M2(SPSC 200만 바이트에서 소비자가 미커밋 ~115만 바이트 과독,
  `_count` 음수)가 모두 재현됨. 두 수정 적용 시 단일스레드·크로스스레드 검증 통과. `.claude/review/phase1-bipbuffer.md` 참조.
- 영향(중요): "clamp"는 **반환 길이 제한**이지 `_count` 값 자체를 0 이상으로 보정하는 것이 아니다(그건 버그 은폐).
  완료 기준에 M1 회귀 테스트와 M2 SPSC 회귀 테스트(`produced==consumed`, 바이트 무결성, `Count >= 0`)를 포함한다.

## D006 — RefCountedBuffer/Pool 설계는 승인, AddRef 순서 계약을 강제한다

- 날짜: 2026-06-10
- 상태: Accepted
- 결정: PLAN의 참조계수 버퍼/고정 풀 설계를 채택한다. 0 도달 시 `Interlocked` + `Exchange` 가드로
  정확히 1회 반환. **팬아웃에서 모든 구독자 몫 AddRef를, 어떤 Release가 0에 도달하기 전에 완료**한다
  (권장: 생성 ref=1 → 구독자 M명 AddRef → 배포 → 마지막에 발행자 자신 Release).
- 근거: 실측에서 5만 반복 팬아웃·2만 동시 버퍼 모두 정확히-1회 반환·누수 0. "보내고 나서 AddRef" 식
  lazy 증가는 부활(use-after-free)을 유발. `.claude/review/phase1-refcounted-pool.md` 참조.
- 영향: 구현은 부활 가드(0→1 감지 예외)와 이중 반환 가드를 유지하고, 위 순서 계약을 테스트로 강제한다.

## D007 — 송신 경로는 "MPSC 큐 → 단일 펌프 → SPSC 송신 BipBuffer", 버퍼는 풀 핸들로 주고받는다

- 날짜: 2026-06-10
- 상태: Accepted
- 결정:
  - 팬아웃 시 다중 발행 스레드가 같은 구독자 송신 버퍼에 직접 쓰지 않는다. 발행자는 `(RefCountedBuffer,
    off, len)`을 연결별 **MPSC 큐**에 넣고, 연결당 **단일 송신 펌프**가 SPSC 송신 BipBuffer를 채운다.
  - `ITransport`/`IConnection`은 raw `Memory<byte>`가 아니라 **풀 소유 핸들**(`RefCountedBuffer`/lease)로
    버퍼를 주고받는다(RIO/io_uring 등록 식별·반환 책임·refcount 때문).
  - 수신 경로는 recv+프레이밍을 같은 I/O 워커에서 인라인 처리(단일 스레드)하여 SPSC를 자명하게 만든다.
- 근거: BipBuffer는 SPSC 전용. 다중 생산자 노출 시 깨진다. `.claude/review/phase2-transport-bipbuffer.md` 참조.
- 영향: Phase 2 인터페이스 설계 시 이 계약을 선반영한다. TODOS의 "raw Memory vs lease" 미결 질문을 lease로 확정.

## D008 — 브로커 라우팅은 빈 토픽 eager-cleanup을 금지한다 (실측 검증됨)

- 날짜: 2026-06-10
- 상태: Accepted
- 결정: `topic → 구독자 set` 라우팅에서 "빈 set이면 토픽 엔트리 즉시 제거" 최적화를 쓰지 않는다.
  기본은 **NoCleanup**(빈 토픽 미정리 + 필요 시 주기적 안전 sweep). 즉시 정리가 꼭 필요하면 **set 인스턴스
  락**으로 추가/빈-제거를 직렬화한다.
- 근거: 실측에서 순진한 eager-cleanup은 "동시 구독 vs 빈-정리" 경합으로 구독을 약 51% 유실. 영리한
  lock-free verify-retry도 여전히 약 50% 유실(틀림). NoCleanup·set-lock은 0 유실.
  `.claude/review/phase3-broker-routing.md` 참조.
- 영향: Phase 3 라우팅 구현은 R1 타깃 경합 회귀 테스트("Y 구독"‖"X 해지 후 Y 잔존")를 반드시 포함한다.
  흔한 churn 테스트만으로는 이 버그를 놓친다.

## D009 — Publish payload는 RefCountedBuffer를 소유권 단위로, TCP는 1회 복사·UDP는 직접 recv

- 날짜: 2026-06-10
- 상태: Accepted (사용자 승인)
- 결정:
  - recv→팬아웃 경계의 소유권 단위는 **`RefCountedBuffer` 하나**로 통일한다.
  - **TCP**: recv BipBuffer는 프레이밍 전용. PUBLISH payload를 풀 `RefCountedBuffer`로 **1회 복사**
    (`recvSpan.CopyTo`, 무할당) 후 recv 영역 즉시 Consume. 팬아웃은 그 버퍼 공유(구독자당 0회).
  - **UDP**: datagram을 `RefCountedBuffer`로 **직접 recv**(BipBuffer 미사용) → publish 진짜 zero-copy.
  - 수명: publish가 ref=1 가드 보유 → 구독자별 AddRef+enqueue(실패 시 즉시 Release) → publish가 마지막에
    자기 ref Release → 송신 펌프가 완료 후 Release → 0 도달 시 풀 반환.
- 근거: payload는 M개 구독자가 비동기 소비하므로 recv 링보다 오래 살아야 하나, `Span`은 ref struct라
  큐 저장 불가. recv 링 직접 무복사 전달은 원천 불가(브로커 팬아웃은 진짜 0복사 자체가 불성립).
  `.claude/review/phase3-publish-ownership.md` 참조.
- 영향: `RefCountedBuffer`에 `Span`/`Memory`/`Length`/`SetLength` 필요. UDP Transport는 RefCountedBuffer를
  recv 버퍼로 직접 사용. **`AGENTS.md §2-1` "중간 byte[] 복사 금지" 문구를 "구독자당/불필요한 관리힙 복사
  금지(TCP publish의 recv 링→메시지 버퍼 1회 복사 허용)"로 정정.** Phase 3에 TCP 무복사-독립성·백프레셔
  누수·구독자 0명 테스트 추가.

## D010 — TCP 프레임은 copy 기반 per-connection 조립 상태머신으로 처리한다 (실측 검증됨)

- 날짜: 2026-06-10
- 상태: Accepted
- 결정: recv BipBuffer는 미파싱 바이트 스트림만 담는다. 연결별 파서가 상태머신으로 조립한다.
  - Header 상태: 4바이트 길이를 누적(여러 read span/wrap에 걸쳐도 바이트 단위). 완성 시 big-endian 파싱,
    길이 검증(0 ≤ len ≤ **maxPayload**), `RefCountedBuffer(len)` 대여.
  - Body 상태: payload를 `RefCountedBuffer`로 누적 복사 후 Consume, `got == len`까지 반복. 완성 시 dispatch.
  - → recv 링이 프레임을 통째로 담을 필요 없음(payload가 recv 링보다 커도 됨). D009의 "TCP 1회 복사"가
    청크에 걸쳐 1회로 실현(각 바이트 정확히 1번 복사). `maxPayload` 상한으로 과대 할당/DoS 방지(초과 시 끊기).
- 근거: "프레임이 항상 contiguous span에 통째로 온다"는 보장은 불가능(TCP가 임의 분할). 프로토타입 실측:
  recv 링 64B < payload 300B, 청크 1~7B, 10만 프레임 무결성·누수 0. `.claude/review/phase3-framing-and-close.md`.
- 영향: 흔한 버그(헤더 분할 미처리) 주의. Phase 3 테스트에 적대적 청크·recv 링 < payload·0 길이·maxPayload
  경계·연속 다중 프레임 fuzz 포함.

## D011 — 연결 종료/Dispose는 queued + in-flight + 조립중 RefCountedBuffer를 모두 release한다

- 날짜: 2026-06-10
- 상태: Accepted
- 결정: `IConnection.Close()/Dispose()` 계약으로 다음을 보장한다.
  1. 송신 경로를 원자적으로 "closed" 표시 → 이후 `TryEnqueue`는 false(발행자가 D009대로 자기 AddRef 즉시 Release).
  2. 송신 MPSC 큐를 drain하며 각 pending 항목 `Release`.
  3. 송신 펌프의 in-flight 버퍼를 펌프 unwind 시 `Release`.
  4. drain과 펌프 dequeue는 상호배타(close 이후 펌프는 dequeue 0) → 이중 release 금지(가드로 검출).
  5. recv 측: 조립 중이던 파서의 부분 수신 `RefCountedBuffer`(`_cur`)도 종료 시 `Release`.
  6. 경합: 발행자 `AddRef`+`TryEnqueue` 중 close가 끼어들면 `TryEnqueue`가 원자적으로 reject → 발행자 Release.
- 근거: enqueue 성공 후 종료 시 pending/in-flight release가 미정의면 느린 소비자 끊기 정책에서 누수 직결.
  외부 검토 Major. `.claude/review/phase3-framing-and-close.md`.
- 영향: Phase 2/3에 "pending 항목 남긴 채 Close + 느린 소비자 끊기" 후 `pool.RentedCount == 0`·이중 반환 0
  테스트, 조립 중 연결 드롭 시 `RentedCount == 0` 테스트 추가.

## D012 — drop-oldest backpressure는 evict한 RefCountedBuffer를 정확히 1회 Release한다 (실측 검증됨)

- 날짜: 2026-06-10
- 상태: Accepted
- 결정: 백프레셔 정책 "drop-oldest"에서 송신 큐가 가득 차 가장 오래된 항목을 evict할 때, **그 evict된
  `RefCountedBuffer`를 정확히 1회 `Release`** 한다(보내지 않으므로). evict(producer)·dequeue(pump)·drain(close)는
  **단일 락/단일 소유자로 직렬화**하여 같은 항목이 두 번 제거되지 않게 한다 → 이중 release/누수 차단.
- 근거: enqueue 실패(D009)·종료(D011)와 달리 drop-oldest는 *이미 enqueue된* 항목을 능동 제거하므로 별도
  release 지점이다. 누락 시 누수, evict와 pump dequeue가 같은 head를 경합하면 이중 release. 프로토타입 실측:
  6 producers × 300k × 4 seed(=720만 enqueue), cap=16(대량 eviction) + 동시 pump + close-drain에서
  누수 0·이중 반환 0. `.claude/review/phase3-framing-and-close.md`.
- 영향: Phase 3 백프레셔 구현은 단일 락 직렬화 + evict-release를 따른다. 테스트: 지속 과부하 drop-oldest에서
  큐 길이 ≤ 용량 유지, 종료 후 `RentedCount==0`, 이중 반환 0.
