# CURRENT_PLAN.md — 현재 실행 지점

## Archive

이 파일은 현재 진입점만 유지한다. 2026-06-18 이전 상세 원문은 아래 archive 를 본다.

- 전체 pre-compaction snapshot: `docs/agent-state/snapshots/2026-06-18-pre-compaction/`
- 완료 이력 원문: `docs/agent-state/backlog/completed-history-2026-06-18.md`
- 변경 이력 원문: `docs/agent-state/changelog/2026-06.md`
- 결정 상세 원문: `docs/agent-state/decisions/2026-06.md`

## 최종 목표

고성능 소켓 기반 Interface Server 를 구현한다. 외부 source 에서 들어온 정보를 topic/data type 기준으로 받아,
구독된 TCP/UDP endpoint 로 추가 payload 복사 없이 발행하는 것이 핵심 목표다.

현재 성능 기준은 단일 stream 기준 **4096 bytes 메시지 100 Hz**다. 아직 고정 latency SLO는 없으며,
지속 부하에서 송신 큐 적체와 p99 지연 증가가 누적되지 않는 상태를 관측값으로 추적한다.

## 현재 Phase

Phase 6 — Linux io_uring backend boundary 및 native wrapper 설계.

## 현재 상태 요약

- Phase 1~3의 핵심 TCP broker 경로는 완료됐다. TCP/UDP transport, protocol, broker, server 통합 경로가 존재한다.
- TCP subscriber outbound 는 D065에 따라 `4-byte big-endian length prefix + payload` frame 으로 전송한다.
- TCP/UDP pending send queue 는 capacity 16 bounded drop-oldest 정책을 유지한다(D064, D067).
- drop 관측은 transport/endpoint snapshot 기반 pull diagnostics 로 유지한다(D062, D066).
- Phase 4 benchmark 는 delivery/drop/leak hard gate 와 latency/HWM 관측값을 분리한다(D063).
- latency hard gate 전에는 반복 baseline artifact 와 summary/soft warning 을 우선한다(D069, D070).
- 문서 전용 작업은 D081 기준으로 관련 설계/상태/결정 문서를 한 coherent documentation cycle 에서 같이 정렬한다.
  코드/테스트 구현 변경은 계속 작은 기능 단위로 분리한다.
- D110에 따라 RIO UDP parity 이후에도 `TransportFactory.CreateDefault()`는 계속 `SaeaTransport`를 반환한다.
  다음 구현 후보는 default backend 승격이 아니라 RIO/SAEA backend contract matrix 보강이다.
- `--baseline-suite`로 closed-loop/open-loop raw JSON artifact 를 반복 수집할 수 있다.
- `--summarize-baseline <input-dir> --summary <output-json> [--summary-md <output-md>]`로 summary JSON과 사람이 읽는 Markdown 보조 artifact 를 생성할 수 있다.
- 2026-06-18 baseline root, `session-02`, `session-03`에는 `summary.json`과 `summary.md`가 모두 생성되어 있다.
- 2026-06-18 baseline root, `session-02`, `session-03`의 `summary.json`/`summary.md`는 D079/D080 이후 현재 schema 로 재생성됐다.
- 2026-06-18 date root 에는 세 session 을 묶는 `history.json`과 `history.md`가 생성되어 있다.
  기존 raw report 는 D079 이전 artifact 이므로 comparison signal 은 `unknown-runner` mismatch 를 기록하지만,
  hard gate 와 warning 은 계속 pass/0 상태다.
- baseline summary 이후 report history 와 warning 승격 정책은
  `docs/superpowers/specs/2026-06-18-baseline-report-history-warning-policy-design.md`로 정리했다(D071).
- 반복 baseline session 을 빠르게 찾기 위한 전역 index 는 `docs/benchmarks/baselines/index.md`에 둔다(D071).
- Phase 4 backlog 재평가 결과, 다음 코드 구현 후보는 CI workflow 가 아니라 여러 session `summary.json`을 읽는
  provider-independent baseline history report command 로 좁혔다.
  설계 초안은 `docs/superpowers/specs/2026-06-23-baseline-history-report-command-design.md`에 있다.
- baseline history report command 설계 리뷰에서 enum 이름과 parent/date root discovery 모호성을 발견해 보정했다.
  설계는 D078로 수락됐고, command enum 값은 `SummarizeBaselineHistory`로 고정한다.
- baseline history report command 구현 계획은 `docs/superpowers/plans/2026-06-23-baseline-history-report-command.md`에 있다.
  계획은 parser contract, history reader, aggregate writer, Program wiring/smoke 의 4개 커밋 단위로 나뉜다.
- baseline history report command 구현 계획은 2026-06-23 리뷰 의견을 반영해 history `hard-passed`,
  `failed-session-count`, 누락 p99 표현(JSON `null`/Markdown `-`) 계약까지 보정했다.
- baseline history report command Task 1(parser contract)이 완료됐다.
  `--summarize-baseline-history <baseline-root> --history <output-json> [--history-md <output-md>]`를 parser 가 인식하고,
  `BenchmarkCommandLine`에 history input/output path 를 보존한다. 실행 wiring 은 아직 Task 4 범위다.
- baseline history report command Task 2(history domain/reader)가 완료됐다.
  `BaselineHistoryReader.ReadSessions(...)`는 date root 와 parent baseline root 를 bounded discovery 로 읽고,
  legacy root `summary.json`은 `session-01(root)`로, `session-NN/summary.json`은 해당 session 으로 기록한다.
- baseline history report command Task 3(history aggregate/writer)이 완료됐다.
  `BaselineHistoryGenerator`는 session `hard-passed` AND 와 `failed-session-count`를 계산하고,
  `BaselineHistoryWriter`/`BaselineHistoryMarkdownWriter`는 JSON `null`/Markdown `-` p99 누락 표현을 유지한다.
- baseline history report command Task 4(Program wiring/smoke)가 완료됐다.
  `--summarize-baseline-history <baseline-root> --history <output-json> [--history-md <output-md>]`는 실제 CLI에서
  history JSON과 선택 Markdown을 생성하고, session hard gate 결과에 따라 exit code 0/1을 반환한다.
- baseline history report command Task 1~4 구현 검토를 완료했다.
  새 Blocker/Major finding 은 없고, CLI 오류 메시지 정밀화와 Program-level date-root smoke 는 비차단 후속으로 남겼다.
  상세는 `docs/agent-state/reviews/2026-06-23-baseline-history-command-implementation-review.md`를 본다.
- Phase 4 backlog 재평가 결과, 다음 구현 후보는 CI workflow/warning-as-failure 가 아니라 benchmark runner identity/environment metadata 로 좁혔다.
  설계는 `docs/superpowers/specs/2026-06-23-benchmark-runner-identity-design.md`에 있고, D079로 raw report schema v1 additive 관측 필드
  방식을 수락했다.
- benchmark runner identity 구현 계획은 `docs/superpowers/plans/2026-06-23-benchmark-runner-identity.md`에 있다.
  계획은 identity model, raw report writer metadata, raw report reader legacy compatibility 의 3개 커밋 단위로 나뉜다.
- benchmark runner identity Task 1 model 이 완료됐다.
  `BenchmarkRunIdentity.CaptureDefault()`는 privacy 우선 기본값과 `HPS_BENCHMARK_RUNNER_ID`/`HPS_BENCHMARK_RUNNER_KIND`
  명시 override 만 사용한다.
- benchmark runner identity Task 2 raw report writer metadata 가 완료됐다.
  `TcpLoopbackRunResult`는 `BenchmarkRunIdentity`를 보존하고, `TcpLoopbackReportWriter`는 raw report schema v1 top-level 에
  runner/environment metadata field 를 additive 로 기록한다.
- benchmark runner identity Task 3 raw report reader/legacy compatibility 가 완료됐다.
  `BaselineReport`는 `BenchmarkRunIdentity`를 보존하고, `BaselineReportReader`는 신규 metadata field 를 읽는다.
  metadata 가 없는 legacy raw report 는 `BenchmarkRunIdentity.Unknown`으로 보존한다.
- benchmark runner identity Task 1~3 구현 검토가 완료됐다.
  새 Blocker/Major finding 은 없고, writer output 에 대한 architecture field roundtrip/assertion 보강은 비차단 test-hardening 으로 남겼다.
  상세는 `docs/agent-state/reviews/2026-06-23-benchmark-runner-identity-implementation-review.md`를 본다.
- benchmark writer metadata roundtrip test-hardening 이 완료됐다.
  `TcpLoopbackReportWriter`가 쓴 raw report 를 `BaselineReportReader`로 다시 읽어 D079 runner/environment metadata 전체,
  특히 `os-architecture`와 `process-architecture` field 이름 drift 를 조기에 잡는다.
- generated baseline summary/history artifact 재생성이 완료됐다.
  2026-06-18 세 session summary artifact 와 date-level history artifact 가 현재 D080 comparison schema 를 포함한다.
  재생성 중 발견한 local absolute source path 출력은 `BaselineReportReader`에서 입력 directory 기준 상대 경로로 보정했다.
- 2026-06-24 `session-01`/`session-02`/`session-03` baseline artifact 를 현재 D079/D080 schema 로 새로 생성했다.
  각 session 은 raw report 6개, `summary.json`/`summary.md`를 가지며, date-level `history.json`/`history.md`는
  세 session 을 집계한다. comparison-compatible true, unknown runner 0, mismatch 0 이다.
- 2026-06-24 compatible baseline 3개를 D082 reference latency envelope 로 채택했다.
  load p99 max 는 1020.4 us, open-loop p99 max 는 1006.5 us 이므로 1 ms hard latency SLO 는 현재 baseline 과 맞지 않는다.
  warning-as-failure 와 CI latency failure 는 명시적 runner id 와 여러 날짜 root 의 compatible baseline 이 쌓일 때까지 보류한다.
  현재 2026-06-24 baseline 은 `runner-id=local-unspecified`이므로 gate 승격 표본 count 에 산입하지 않고,
  envelope 초과 기록도 자동 failure 가 아니라 수동 리뷰 메모로 남긴다.
- D082 이후 Phase 4 다음 후보를 재평가했고, D083으로 explicit runner baseline 을 기존 date root 에 바로 섞지 않기로 했다.
- explicit runner baseline 저장 구조와 수집 정책을 D084로 확정했다.
  명시적 runner baseline 은 `docs/benchmarks/baselines/runners/<runner-id>/YYYY-MM-DD/session-NN/` 아래에 저장하고,
  기존 top-level date roots 는 legacy/local-unspecified baseline 으로 보존한다.
- 첫 explicit runner baseline date root 인 `local-win-x64-01/2026-06-24`를 3-session 으로 확장했다.
  runner/date history 는 session-count 3, hard-passed true, warning-count 0, comparison-compatible true 다.
  explicit runner reference envelope 는 load p99 max 870.7 us, open-loop p99 max 1051.5 us 이며,
  같은 runner 의 date root 가 아직 1개뿐이므로 D082 warning-as-failure 승격 조건에는 산입하지 않는다.
- explicit runner 3-session 이후 Phase 4 다음 후보를 재평가했고, 다음 단위는
  `local-win-x64-01/2026-06-25/session-01` 수집으로 정했다(D085).
  CI/warning-as-failure 설계는 같은 runner 의 date root 를 더 쌓은 뒤 다시 평가한다.
- `local-win-x64-01/2026-06-25/session-01` explicit runner baseline 을 수집했다.
  2026-06-25 date root 는 session-count 1, hard-passed true, warning-count 0, comparison-compatible true 다.
  runner root history 는 4-session, hard-passed true, warning-count 0, comparison-compatible true 다.
  explicit runner envelope 는 load p99 max 921.1 us, open-loop p99 max 1077.4 us 이며,
  두 번째 date root 는 아직 1-session 뿐이므로 D082 warning-as-failure 승격 조건에는 산입하지 않는다(D086).
- `local-win-x64-01/2026-06-25/session-02` explicit runner baseline 을 수집했다.
  2026-06-25 date root 는 session-count 2, hard-passed true, warning-count 0, comparison-compatible true 다.
  runner root history 는 5-session, hard-passed true, warning-count 0, comparison-compatible true 다.
  explicit runner envelope 는 load p99 max 935.6 us, open-loop p99 max 1077.4 us 이며,
  두 번째 date root 는 아직 2-session 뿐이므로 D082 warning-as-failure 승격 조건에는 산입하지 않는다(D087).
- `local-win-x64-01/2026-06-25/session-03` explicit runner baseline 을 수집했다.
  2026-06-25 date root 는 session-count 3, hard-passed true, warning-count 0, comparison-compatible true 다.
  runner root history 는 6-session, hard-passed true, warning-count 0, comparison-compatible true 다.
  explicit runner envelope 는 load p99 max 935.6 us, open-loop p99 max 1077.4 us 이며,
  같은 runner 의 두 date root 가 각각 3-session reference 를 갖췄다(D088).
  gate 구현은 자동 진행하지 않고 다음 단위에서 D082 warning-as-failure/CI gate 후보를 재평가한다.
- explicit runner 2-date-root/6-session evidence 이후 gate 승격 후보를 재평가했다.
  D082의 서로 다른 date root 3개 이상 조건과 별도 warning threshold 검토 조건은 아직 충족되지 않았으므로
  warning-as-failure 와 CI latency hard gate 는 계속 보류한다(D089).
  다음 단위는 CI workflow 구현이 아니라 CI runner identity, artifact 저장 위치, local/CI baseline 분리,
  exit code 정책을 먼저 닫는 CI artifact-only benchmark 정책 설계다.
- 이후 `local-win-x64-01/2026-06-29` 세 session 을 추가해 3-date-root/9-session evidence 조건을 충족했다(D123).
  D124 기준으로 이 envelope 는 runner-local 수동 리뷰 기준으로 채택하지만,
  현재 warning threshold 는 runner/profile scoped 가 아닌 전역 상수이므로 warning-as-failure 와 CI latency hard gate 는 계속 보류한다.
- CI artifact-only benchmark 정책을 설계했다(D090).
  권장 CI runner id 는 `ci-windows-x64-01`, runner kind 는 `ci`다.
  CI 매 실행 artifact 는 docs baseline 과 섞지 않고 `artifacts/benchmarks/runners/<ci-runner-id>/...` 같은
  CI artifact 영역에 둔다. latency/HWM/warning 은 report-only 로 유지하고,
  CI 실패 조건은 build/test, command usage/write failure, delivery/drop/leak hard gate 실패로 제한한다.
- summary/history comparison signal 설계를 완료했다.
  설계는 `docs/superpowers/specs/2026-06-23-summary-history-comparison-signal-design.md`에 있고,
  D080으로 comparison signal 을 hard gate/기존 warning-count 와 분리된 non-failing compatibility artifact 로 둔다.
  summary comparison key 는 `load`/`open-loop` scenario 차이를 허용하기 위해 `result-name`별 `cases` 배열로 표현한다.
- summary/history comparison signal 구현 계획을 완료했다.
  계획은 `docs/superpowers/plans/2026-06-24-summary-history-comparison-signal.md`에 있고,
  `BaselineReport` payload/target settings, summary comparison 계산, summary 출력, history 집계, history 출력/CLI smoke 의
  5개 커밋 단위로 나뉜다.
- summary/history comparison signal Task 1이 완료됐다.
  `BaselineReport`와 `BaselineReportReader`가 raw report 의 `payload-bytes`, `target-rate-hz`,
  `target-duration-seconds`를 보존한다. 이 값은 다음 Task 2에서 summary comparison key 의 workload case 입력으로 사용한다.
- summary/history comparison signal Task 2가 완료됐다.
  `BaselineComparisonCase`, `BaselineComparisonKey`, `BaselineComparisonMismatch`, `BaselineComparisonResult`를 추가했고,
  `BaselineSummaryGenerator`가 source report 목록에서 compatible 여부, unknown runner count, mismatch 목록,
  result-name별 canonical cases 를 계산한다.
- summary/history comparison signal Task 3이 완료됐다.
  `BaselineSummaryWriter`는 summary JSON top-level 에 comparison-compatible/key/mismatch field 를 쓰고,
  `BaselineSummaryMarkdownWriter`는 사람이 비교 기준과 mismatch 를 바로 볼 수 있는 `## Comparison` section 을 출력한다.
- summary/history comparison signal Task 4가 완료됐다.
  `BaselineHistoryReader`가 session summary comparison field 를 읽고, legacy summary 는
  `legacy-summary-without-comparison` mismatch 로 표시한다. `BaselineHistoryGenerator`는 session comparison key 를 집계해
  history-level compatible/mismatch result 를 계산하되 hard gate/warning-count 는 바꾸지 않는다.
- summary/history comparison signal Task 5가 완료됐다.
  `BaselineHistoryWriter`와 `BaselineHistoryMarkdownWriter`가 history comparison output 을 쓰고,
  Program smoke 로 comparison mismatch-only history 가 hard gate success exit code 를 유지함을 확인했다.
- 2026-06-24 summary/history comparison signal 계획 리뷰의 High/Medium 지적을 test-hardening 으로 반영했다.
  Summary Markdown null-key/legacy unknown 경로와 partial unknown identity 판정이 회귀 테스트로 고정됐고,
  hard comparison identity field 중 하나라도 `unknown`이면 `unknown-runner`로 처리한다는 기준을 문서화했다.
- UDP stale remote cleanup 은 Broker/Server 소유의 선택적 lease cleanup 으로 설계했고, 기본 idle expiry 는 비활성화한다(D072).
- `SubscriptionTable.UnsubscribeAll(IUdpEndpoint, EndPoint)`로 특정 UDP remote target 만 모든 topic 에서 제거할 수 있다(D072).
- UDP idle lease tracker/sweep 은 Broker 소유·Server timer 트리거, 내부 options(기본 비활성), `TimeProvider` 시간 소스로
  설계했다(D073). 설계는 `docs/superpowers/specs/2026-06-22-udp-optional-lease-sweep-design.md`에 있다.
- UDP optional lease sweep 구현 계획은 `docs/superpowers/plans/2026-06-22-udp-optional-lease-sweep.md`에 있다.
- 내부 `UdpLeaseOptions` 타입과 `Hps.Broker.Tests` internal 접근 경계가 생겼다. 활성 옵션 factory 는 C# 멤버 이름 충돌을 피하기 위해
  `CreateEnabled(TimeSpan, TimeSpan)` 이름을 사용한다.
- 내부 `UdpRemoteLeaseTracker`가 생겼고, UDP remote subscribe/unsubscribe/publish activity 와 endpoint close cleanup 을
  lease table 과 `SubscriptionTable`에 같은 lock 경계로 반영한다.
- `UdpRemoteLeaseTracker.SweepExpired(DateTimeOffset)`가 생겼고, 만료된 remote target 을
  `SubscriptionTable.UnsubscribeAll(IUdpEndpoint, EndPoint)`로 모든 topic 에서 제거한다.
- `BrokerUdpDatagramHandler`가 SUBSCRIBE/UNSUBSCRIBE/PUBLISH/endpoint-close activity 를 tracker 로 위임하고,
  `SweepExpiredUdpLeases(DateTimeOffset)` 내부 entry point 를 제공한다.
- BrokerServer UDP lease host timer/public settings 설계는
  `docs/superpowers/specs/2026-06-22-broker-server-udp-lease-host-timer-design.md`에 있다(D074).
  기본은 disabled 이고, 활성화 시 idle timeout/sweep interval 은 명시 입력으로만 받는다.
- `BrokerServerOptions` public 설정 타입이 생겼다. `Default`는 UDP lease sweep disabled 이고,
  `CreateWithUdpLeaseSweep(...)`는 explicit timeout/interval 과 `TimeProvider`를 저장한다.
- `BrokerServer`가 UDP lease sweep enabled options 를 받으면 `StartUdpAsync` 성공 후 `TimeProvider.CreateTimer`로 sweep timer 를 만들고,
  `StopAsync`와 start 실패 cleanup 에서 timer 를 dispose 한다. timer callback 은 `BrokerUdpDatagramHandler.SweepExpiredUdpLeases(...)`를 호출한다.
- Stable subscriber identity / reconnect rebinding 정책은 D075로 정리했다.
  기본 v1 runtime target subscription 은 유지하고, 후속 stable identity 는 `REGISTER <subscriber-id>` 기반 opt-in Broker registry 로 설계한다.
  같은 id 재등록은 새 runtime target 이 이기며, disconnected 동안 payload 는 저장하지 않는다.
- Stable subscriber identity 구현 계획은
  `docs/superpowers/plans/2026-06-22-stable-subscriber-identity.md`에 있다.
  계획은 protocol decode, pure registry, TCP handler, UDP handler, Server opt-in wiring 의 5개 커밋 단위로 나뉜다.
- Protocol 계층은 `REGISTER <subscriber-id>`와 `UNREGISTER <subscriber-id>`를 token-only command 로 decode 한다.
  stable identity token 은 다음 Broker 단계에서 `TcpCommand.Topic` span view 를 해석해 사용한다.
- Broker 계층에는 내부 `SubscriberIdentity`, `SubscriberRegistrationResult`, `SubscriberRegistry` pure model 이 있다.
  registry 는 identity별 topic metadata 와 현재 online `BrokerSubscriber` target 을 연결하며 payload 는 저장하지 않는다.
- TCP `BrokerTcpFrameHandler`는 선택적 `SubscriberRegistry`가 주입되면 `REGISTER`/`UNREGISTER`와 registered
  target 의 subscribe/unsubscribe/close cleanup 을 registry 로 연결한다. 기본 public constructor 는 기존 runtime target 동작을 유지한다.
- UDP `BrokerUdpDatagramHandler`는 선택적 `SubscriberRegistry`가 주입되면 UDP `REGISTER`/`UNREGISTER`,
  registered remote subscribe/unsubscribe, same-id remote rebind, endpoint close cleanup 을 registry 와 lease tracker 로 연결한다.
- `BrokerServerOptions`는 stable subscriber identity opt-in 설정과 retention timeout 을 노출한다.
  `BrokerServer`는 enabled options 일 때 TCP/UDP handler 에 shared `SubscriberRegistry`를 주입하고,
  host 소유 retention timer 로 disconnected identity metadata 를 sweep 한다.
- Late `REGISTER`는 같은 runtime target 의 기존 runtime 구독을 stable identity metadata 로 자동 이관하지 않고 제거한다(D076).
  stable identity client 는 `REGISTER` 후 필요한 topic 을 다시 `SUBSCRIBE`해야 한다.
- UDP lease sweep 이 활성화된 경우에도 late `REGISTER` 성공 후 같은 remote 의 기존 runtime lease metadata 를 제거하거나
  stable identity topic set 으로 교체한다(D076).
- stable subscriber identity TCP reconnect/rebind 와 UDP remote rebind 는 실제 `SaeaTransport` loopback 에서도 검증됐다.
- 2026-06-23 stable identity post-implementation 교차검증에서 UDP must-fix 2건을 발견했다.
  1) UDP lease sweep 이 stable registry current target 을 disconnected 로 바꾸지 않는다.
  2) UDP invalid identity validation 예외가 handler 밖으로 escape 되어 shared endpoint close 로 이어질 수 있다.
  상세는 `docs/agent-state/reviews/2026-06-23-stable-subscriber-identity-cross-check.md`를 본다.
- UDP stable identity lease sweep 은 만료된 remote target 을 routing table 에서 제거한 뒤
  `SubscriberRegistry.RemoveTarget(...)`에도 전달해 current target 을 disconnected 상태로 바꾼다.
  이로써 retention sweep 이 idle stable UDP identity metadata 를 제거할 수 있다.
- UDP `REGISTER`/`UNREGISTER` identity token validation 실패는 handler 안에서 datagram drop 으로 격리한다.
  tab 같은 non-space whitespace 가 decoder 를 통과해도 `SubscriberIdentity.Create(...)` 예외가 SAEA receive loop 로 전파되지 않는다.
- 2026-06-23 F1/F2 수정분 리뷰에서 추가 must-fix 1건을 발견했다.
  UDP lease sweep 은 expired target snapshot 을 만든 뒤 registry cleanup 을 별도로 수행하므로, snapshot 이후 같은 target 이 재등록되면
  stale `RemoveTarget(...)`이 새 online 상태를 disconnected 로 덮을 수 있다.
  상세는 `docs/agent-state/reviews/2026-06-23-udp-stable-identity-f1-f2-review.md`를 본다.
- UDP lease sweep registry cleanup race 는 D077 기준으로 정리했다.
  `BrokerUdpDatagramHandler`는 UDP receive command/endpoint-close/sweep state mutation 을 handler gate 로 직렬화하고,
  `PUBLISH` fan-out 은 lease activity 갱신 뒤 lock 밖에서 수행한다.
- UDP lease sweep registry race guard 리뷰에서 새 Blocker/Major finding 은 나오지 않았다.
  상세는 `docs/agent-state/reviews/2026-06-23-udp-lease-sweep-race-guard-review.md`를 본다.

## 최근 완료 단위

- 이번 단위 — 2026-06-25 explicit runner baseline session-02 수집
  - `HPS_BENCHMARK_RUNNER_ID=local-win-x64-01`, `HPS_BENCHMARK_RUNNER_KIND=local`로
    `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/session-02/`에 raw report 6개를 생성했다.
  - 같은 session 에 `summary.json`/`summary.md`를 생성하고,
    2026-06-25 date root 의 `history.json`/`history.md`와 runner root `history.json`/`history.md`를 재생성했다.
  - date history 는 `session-count=2`, runner root history 는 `session-count=5`이며,
    둘 다 `hard-passed=true`, `warning-count=0`, `comparison-compatible=true`다.
  - `docs/benchmarks/baselines/index.md`의 runner date-level history, session row,
    explicit runner latency envelope 를 갱신했다.
  - D087로 두 번째 date root 2-session 표본과 gate 승격 보류 판단을 기록했다.
  - 검증: baseline suite pass, summary CLI source-report-count 6/hard-passed true/warning-count 0,
    date history CLI session-count 2/hard-passed true/warning-count 0,
    runner history CLI session-count 5/hard-passed true/warning-count 0.
    runner artifact local absolute path 검색 결과 없음. `Hps.Benchmarks.Tests` 67개 통과,
    `git diff --check` exit 0, restore asset 재생성 후 solution build 경고 0/오류 0,
    solution tests 269개 통과.

- 이번 단위 — 2026-06-25 explicit runner baseline session-01 수집
  - `HPS_BENCHMARK_RUNNER_ID=local-win-x64-01`, `HPS_BENCHMARK_RUNNER_KIND=local`로
    `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/session-01/`에 raw report 6개를 생성했다.
  - 같은 session 에 `summary.json`/`summary.md`를 생성하고,
    2026-06-25 date root 의 `history.json`/`history.md`를 생성했다.
  - 같은 runner 아래 date root 가 2개가 되어 runner root `history.json`/`history.md`도 생성했다.
  - date history 는 `session-count=1`, runner root history 는 `session-count=4`이며,
    둘 다 `hard-passed=true`, `warning-count=0`, `comparison-compatible=true`다.
  - `docs/benchmarks/baselines/index.md`의 runner group, runner date-level history, session row,
    explicit runner latency envelope 를 갱신했다.
  - D086으로 두 번째 date root 시작 표본과 gate 승격 보류 판단을 기록했다.
  - 검증: baseline suite pass, summary CLI source-report-count 6/hard-passed true/warning-count 0,
    date history CLI session-count 1/hard-passed true/warning-count 0,
    runner history CLI session-count 4/hard-passed true/warning-count 0.
    runner artifact local absolute path 검색 결과 없음. `Hps.Benchmarks.Tests` 67개 통과,
    `git diff --check` exit 0, restore asset 재생성 후 solution build 경고 0/오류 0,
    solution tests 269개 통과.

- 이번 단위 — explicit runner 3-session 이후 Phase 4 다음 후보 재평가
  - `docs/superpowers/specs/2026-06-25-phase4-after-explicit-runner-reference-reassessment.md`를 추가했다.
  - 후보 A(다음 date root 수집), B(CI/warning-as-failure 설계), C(RIO/io_uring 착수)를 비교했다.
  - 같은 runner 의 date root 가 아직 1개뿐이므로 D082 gate 승격 조건을 만족하지 못한다고 정리했다.
  - D085로 다음 단위를 `local-win-x64-01/2026-06-25/session-01` explicit runner baseline 수집으로 정했다.
  - 검증: `local-win-x64-01/2026-06-24/history.json`, `docs/benchmarks/baselines/index.md`,
    D082/D084, `.claude/review/`의 기존 benchmark 리뷰 의견을 대조했다.
    신규 spec placeholder 검색 결과 없음. `git diff --check` exit 0,
    solution build 경고 0/오류 0, solution tests 269개 통과.

- 이번 단위 — explicit runner baseline session-02/session-03 수집 및 문서 batch 완료
  - `HPS_BENCHMARK_RUNNER_ID=local-win-x64-01`, `HPS_BENCHMARK_RUNNER_KIND=local`로
    `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/session-02/`와 `session-03/`에
    raw report 를 각각 6개씩 생성했다.
  - 각 session 에 `summary.json`/`summary.md`를 생성하고, runner/date root 의 `history.json`/`history.md`를
    3-session 기준으로 재생성했다.
  - history 는 `session-count=3`, `hard-passed=true`, `warning-count=0`, `comparison-compatible=true`,
    unknown runner 0, mismatch 0 이다.
  - explicit runner reference envelope 는 load p99 max 870.7 us, open-loop p99 max 1051.5 us,
    TCP HWM max 2, dropped total 0, payload error total 0, pool rented max 0 이다.
  - `docs/benchmarks/baselines/index.md`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`,
    `DECISIONS.md`를 같은 기준으로 정렬했다.
  - 검증: session-02/session-03 baseline suite pass, 각 summary CLI source-report-count 6/hard-passed true/warning-count 0,
    history CLI session-count 3/hard-passed true/warning-count 0.
    runner artifact local absolute path 검색 결과 없음. `Hps.Benchmarks.Tests` 67개 통과,
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

- 이번 단위 — 첫 explicit runner baseline session-01 수집
  - `HPS_BENCHMARK_RUNNER_ID=local-win-x64-01`, `HPS_BENCHMARK_RUNNER_KIND=local`로
    `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/session-01/`에 raw report 6개를 생성했다.
  - 같은 session 에 `summary.json`/`summary.md`를 생성하고, runner/date root 에 `history.json`/`history.md`를 생성했다.
  - summary/history 는 `hard-passed=true`, `warning-count=0`, `comparison-compatible=true`, unknown runner 0, mismatch 0 이다.
  - `docs/benchmarks/baselines/index.md`에 runner group, runner date-level history, session row 를 추가했다.
  - 다음 단위는 같은 runner/date root 에 `session-02`를 수집하는 것이다.
  - 검증: baseline suite pass, summary CLI source-report-count 6/hard-passed true/warning-count 0,
    history CLI session-count 1/hard-passed true/warning-count 0.
    runner artifact local absolute path 검색 결과 없음. `Hps.Benchmarks.Tests` 67개 통과,
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.
- 이번 단위 — explicit runner baseline 저장 구조와 수집 정책 설계
  - `docs/superpowers/specs/2026-06-24-explicit-runner-baseline-storage-policy-design.md`를 추가했다.
  - 명시적 runner baseline 저장 위치를 `docs/benchmarks/baselines/runners/<runner-id>/YYYY-MM-DD/session-NN/`로 정했다(D084).
  - 현재 `BaselineHistoryReader` parent-root/date-root 규칙으로 runner root 와 runner/date root 를 모두 읽을 수 있음을 확인했다.
  - 기존 top-level `YYYY-MM-DD` roots 는 이동하지 않고 legacy/local-unspecified baseline 으로 유지한다.
  - 다음 단위는 첫 explicit runner baseline 을 새 runner group 구조에 수집하는 것이다.
  - 검증: D079/D080/D082/D083과 `BaselineHistoryReader` directory 규칙 대조 완료.
    신규 설계/결정/index 문서 임시 표기 검색 결과 없음.
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.
- 이번 단위 — D082 이후 Phase 4 다음 실행 후보 재평가
  - `D082` 이후 남은 후보를 CI/warning-as-failure, explicit runner baseline 수집, runner baseline 저장 구조 설계,
    RIO/io_uring 착수, server-level diagnostics 로 나눠 검토했다.
  - `BaselineHistoryReader`가 `YYYY-MM-DD`/`session-NN` 구조만 읽고, 2026-06-24 date root 는 이미
    `local-unspecified` session 3개로 history-compatible 하다는 점을 확인했다.
  - 명시적 runner id session 을 기존 date root 에 바로 추가하지 않고, D083으로 저장 구조 정책을 먼저 설계하기로 했다.
  - 다음 단위는 `docs/superpowers/specs/2026-06-24-explicit-runner-baseline-storage-policy-design.md` 작성이다.
  - 검증: D082/D079/D080 및 `BaselineHistoryReader` directory 규칙 대조 완료.
    신규 설계/결정 문서 임시 표기 검색 결과 없음.
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.
- 이번 단위 — D082 설계 리뷰 Low 명확성 보강
  - `.claude/review/2026-06-24-latency-envelope-and-gate-deferral-design-review.md`를 확인했다.
  - must-fix는 없고, Low 명확성 지적 2건과 info 1건을 문서 batch 로 반영했다.
  - 집계 방식은 session `summary.json`의 `by-kind` aggregate 를 세션 간 max/min 으로 다시 집계한다고 명시했다.
  - 2026-06-24 `local-unspecified` baseline 은 gate 승격 표본 count 에 산입하지 않고 reference 로만 사용한다고 명시했다.
  - envelope 초과 기록은 자동 schema/command output 이 아니라 수동 리뷰 메모라고 명시했다.
  - 검증: D082 리뷰 finding 1/2와 info 3 반영 여부 대조 완료.
    신규 설계/결정/index 문서 임시 표기 검색 결과 없음.
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.
- 이번 단위 — latency envelope 재산정과 gate 보류 조건 설계
  - 2026-06-24 compatible baseline 3개를 기준으로 D082 reference latency envelope 를 정리했다.
  - 현재 p99 max 가 1 ms 근처 또는 그 이상까지 관측되므로 hard latency gate 와 CI latency failure 는 계속 보류한다고 명시했다.
  - warning-as-failure 승격 조건을 명시적 runner id, 서로 다른 날짜 root 3개 이상, date root 당 compatible session 3개 이상으로 좁혔다.
  - 검증: 2026-06-24 history/session summary 수치 대조 완료.
    신규 설계/결정/index 문서 임시 표기 검색 결과 없음.
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.
- 이번 단위 — 문서 전용 작업 batch 규칙 명시
  - 구현/테스트/리팩터링 작업은 계속 작은 reviewable 기능 단위로 분리한다.
  - 문서 전용 작업은 설계/상태/결정/검토 문서의 정합성을 위해 관련 문서를 한 번에 갱신한다.
  - 문서 batch 에 코드/테스트 구현 변경은 섞지 않는다고 명시했다.
  - 검증: 관련 root 문서 용어 대조, `git diff --check` exit 0, solution build 경고 0/오류 0,
    solution tests 269개 통과.
- 이번 단위 — 2026-06-24 current-schema baseline session-03 추가
  - `--baseline-suite`로 2026-06-24 `session-03` raw report 6개(load 3회/open-loop 3회)를 새로 생성했다.
  - 같은 session 에 `summary.json`/`summary.md`를 생성하고, 2026-06-24 date root 의 `history.json`/`history.md`를
    3개 session 기준으로 재생성했다.
  - `session-03`은 D079 runner identity/environment metadata 를 포함하므로 summary/history comparison 이
    `comparison-compatible=true`, unknown runner 0, mismatch 0 으로 기록된다.
  - `docs/benchmarks/baselines/index.md`의 2026-06-24 session count 와 session row 를 갱신했다.
  - 검증: baseline suite 는 pass, summary CLI 는 source-report-count 6, hard-passed true, warning-count 0.
    history CLI 는 session-count 3, hard-passed true, warning-count 0.
    `docs/benchmarks/baselines/2026-06-24` 아래 local absolute path 검색은 매칭 없음이다.
    `Hps.Benchmarks.Tests` 67개 통과, `git diff --check` exit 0, solution build 경고 0/오류 0,
    solution tests 269개 통과.
- 이번 단위 — 2026-06-24 current-schema baseline session-02 추가
  - `--baseline-suite`로 2026-06-24 `session-02` raw report 6개(load 3회/open-loop 3회)를 새로 생성했다.
  - 같은 session 에 `summary.json`/`summary.md`를 생성하고, 2026-06-24 date root 의 `history.json`/`history.md`를
    2개 session 기준으로 재생성했다.
  - `session-02`는 D079 runner identity/environment metadata 를 포함하므로 summary/history comparison 이
    `comparison-compatible=true`, unknown runner 0, mismatch 0 으로 기록된다.
  - `docs/benchmarks/baselines/index.md`의 2026-06-24 session count 와 session row 를 갱신했다.
  - 검증: baseline suite 는 pass, summary CLI 는 source-report-count 6, hard-passed true, warning-count 0.
    history CLI 는 session-count 2, hard-passed true, warning-count 0.
    `docs/benchmarks/baselines/2026-06-24` 아래 local absolute path 검색은 매칭 없음이다.
    `Hps.Benchmarks.Tests` 67개 통과, `git diff --check` exit 0, solution build 경고 0/오류 0,
    solution tests 269개 통과.
- 이번 단위 — 2026-06-24 current-schema baseline session 추가
  - `--baseline-suite`로 2026-06-24 `session-01` raw report 6개(load 3회/open-loop 3회)를 새로 생성했다.
  - 같은 session 에 `summary.json`/`summary.md`를 생성하고, 2026-06-24 date root 에 `history.json`/`history.md`를 생성했다.
  - 이번 session 은 D079 runner identity/environment metadata 를 포함하므로 summary/history comparison 이
    `comparison-compatible=true`, unknown runner 0, mismatch 0 으로 기록된다.
  - `docs/benchmarks/baselines/index.md`에 date-level history row 와 session row 를 추가했다.
  - 검증: baseline suite 는 pass, summary CLI 는 source-report-count 6, hard-passed true, warning-count 0.
    history CLI 는 session-count 1, hard-passed true, warning-count 0.
    `docs/benchmarks/baselines/2026-06-24` 아래 local absolute path 검색은 매칭 없음이다.
    `Hps.Benchmarks.Tests` 67개 통과, `git diff --check` exit 0, solution build 경고 0/오류 0,
    solution tests 269개 통과.
- 이번 단위 — 2026-06-18 baseline summary/history artifact 재생성
  - 기존 raw benchmark JSON은 원본 측정값으로 유지하고, 파생 artifact 만 현재 schema 로 다시 생성했다.
  - summary/history mismatch `source-path`가 local workspace 절대 경로를 기록하지 않도록 `BaselineReportReader`를 상대 경로 기준으로 보정했다.
  - `summary.json`/`summary.md`를 root, `session-02`, `session-03`에서 재생성했다.
  - `history.json`/`history.md`를 2026-06-18 date root 에 새로 생성했다.
  - `docs/benchmarks/baselines/index.md`에 date-level history 링크와 D079 이전 legacy raw report 의
    `comparison-compatible=false` 해석을 기록했다.
  - 검증: summary CLI 3회 모두 source-report-count 6, hard-passed true, warning-count 0.
    history CLI 는 session-count 3, hard-passed true, warning-count 0.
    `docs/benchmarks/baselines/2026-06-18` 아래 `D:/`, `D:\`, `C:/`, `C:\Users` 검색 결과는 없다.
    `Hps.Benchmarks.Tests` 67개 통과, `git diff --check` exit 0, solution build 경고 0/오류 0,
    solution tests 269개 통과.
- 이번 단위 — Benchmark writer metadata roundtrip test 보강
  - `TODOS.md`의 P3_NICE benchmark writer metadata roundtrip test gap 을 해소했다.
  - `BaselineReportReaderWriterTests`에 실제 `TcpLoopbackReportWriter.Write(...)` output 을
    `BaselineReportReader.ReadDirectory(...)`로 다시 읽는 roundtrip 테스트를 추가했다.
  - test identity 에 `os-architecture=Arm64`, `process-architecture=X64`를 서로 다르게 넣어 두 field 가 섞이거나 누락되는
    회귀를 구분해서 잡게 했다.
  - Red 확인: `TcpLoopbackReportWriter`의 `process-architecture` field 이름을 임시로 바꿨을 때 새 테스트가
    `Expected: "X64", Actual: "unknown"` assertion failure 로 실패함을 확인했다.
  - Green/검증: 임시 mutation 을 되돌린 뒤 focused roundtrip test 1개가 통과했다.
    `Hps.Benchmarks.Tests` 66개 통과, `git diff --check` exit 0, solution build 경고 0/오류 0,
    solution tests 268개 통과.
- 이번 단위 — Summary/history comparison signal 리뷰 보강
  - `.claude/review/2026-06-24-summary-history-comparison-signal-plan-review.md`의 High/Medium 지적을 대조했다.
  - `BaselineSummaryMarkdownWriterTests`에 legacy/unknown identity summary 의 null-key Markdown 출력 회귀 테스트를 추가했다.
  - `BaselineSummaryGeneratorTests`에 partial unknown identity field 를 `unknown-runner`로 격리하는 테스트를 추가했다.
  - `docs/superpowers/plans/2026-06-24-summary-history-comparison-signal.md`와 `DECISIONS.md`에 unknown 판정 술어를 명시했다.
  - Red 확인: null-key 가드를 임시 제거했을 때 새 Markdown test 가 `NullReferenceException`으로 실패함을 확인했다.
  - Red 확인: partial unknown 판정을 임시 약화했을 때 새 generator test 가 `Assert.False()` failure 로 실패함을 확인했다.
  - Green/검증: focused 보강 tests 2개 통과, `Hps.Benchmarks.Tests` 65개 통과.
- 이번 단위 — Summary/history comparison signal Task 5
  - history JSON top-level 에 comparison-compatible/key/mismatch field 를 출력한다.
  - history JSON session entry 에 session-level comparison-compatible, unknown-runner-count, comparison-mismatch-count 를 출력한다.
  - history Markdown 에 `## Comparison` section 과 mismatch table 을 출력한다.
  - Program smoke 로 runner mismatch-only history 는 `hard-passed=true`, `warning-count=0`, exit code 0을 유지함을 검증했다.
  - Red: history JSON writer/Program tests 가 comparison field 부재로 `KeyNotFoundException`을 냄을 확인했다.
  - Red: Markdown writer test 가 `## Comparison` section 부재로 `Assert.Contains()` 실패함을 확인했다.
  - Green/검증: focused Task 5 tests 3개 통과, `Hps.Benchmarks.Tests` 63개 통과.
- 이번 단위 — Summary/history comparison signal Task 4
  - `BaselineHistorySession`과 `BaselineHistory`가 `Comparison`을 보존한다.
  - `BaselineHistoryReader`가 summary JSON의 comparison field/key/mismatch 를 읽고,
    comparison field 가 없는 legacy summary 는 incompatible session signal 로 변환한다.
  - `BaselineHistoryGenerator`가 session comparison key 를 비교해 history-level compatible/mismatch result 를 계산한다.
  - comparison mismatch 는 D080대로 기존 `HardPassed`, `FailedSessionCount`, `WarningCount`를 변경하지 않는다.
  - Red: history session/history property contract test 2개가 `Assert.NotNull()` 실패함을 확인했다.
  - Red: reader/generator behavior tests 5개가 stub comparison 에서 `Assert.True()`/`Assert.Single()` 실패함을 확인했다.
  - Green/검증: focused history reader/generator tests 12개 통과.
- 이번 단위 — Summary/history comparison signal Task 3
  - summary JSON/Markdown output 에 Task 2에서 계산한 `BaselineSummary.Comparison`을 기록한다.
  - `BaselineSummaryWriter`가 `comparison-compatible`, `comparison-key`, `unknown-runner-count`,
    `comparison-mismatch-count`, `comparison-mismatches`를 canonical JSON artifact 로 출력한다.
  - `BaselineSummaryMarkdownWriter`가 `## Comparison` section 과 result-name별 workload case table 을 출력한다.
  - Red: summary JSON writer test 가 `comparison-compatible` field 부재로 `KeyNotFoundException`을 냄을 확인했다.
  - Red: Markdown writer test 가 `## Comparison` section 부재로 `Assert.Contains()` 실패함을 확인했다.
  - Green/검증: focused JSON writer test 1개, focused Markdown writer tests 3개, `Hps.Benchmarks.Tests` 53개 통과.
- 이번 단위 — Summary/history comparison signal Task 2
  - summary comparison 내부 model 4종을 추가했다.
  - `BaselineSummary`가 `Comparison`을 보존하고, `BaselineSummaryGenerator`가 D080 compatible/unknown/mismatch/no-source 규칙을 계산한다.
  - comparison key 는 `processor-count`를 제외하고 runner/profile/backend/OS/framework/architecture field 와
    `result-name`별 workload case 를 포함한다.
  - Red: `BaselineSummary.Comparison` property 부재로 contract test 가 `Assert.NotNull()` 실패함을 확인했다.
  - Red: compatible behavior test 가 stub comparison 에서 `Expected: True, Actual: False`로 실패함을 확인했다.
  - Green/검증: focused `BaselineSummaryGeneratorTests` 8개 통과, `Hps.Benchmarks.Tests` 51개 통과.
- 이번 단위 — Summary/history comparison signal Task 1
  - `BaselineReport`에 `PayloadBytes`, `TargetRateHz`, `TargetDurationSeconds`를 추가했다.
  - `BaselineReportReader`가 raw report JSON의 `payload-bytes`, `target-rate-hz`, `target-duration-seconds`를 읽어 model 로 전달하게 했다.
  - direct `BaselineReport` 테스트 helper 호출부에는 현재 baseline 기본값 `4096`, `100.0`, `30`을 명시했다.
  - Red: `BaselineReport` payload/target property 부재로 contract test 가 `Assert.NotNull()` 실패함을 확인했다.
  - Red: reader behavior test 가 `Expected: 4096, Actual: 0`으로 실패함을 확인했다.
  - Green/검증: focused `BaselineReportReaderWriterTests` 8개 통과, focused `BaselineSummary*` 6개 통과,
    `Hps.Benchmarks.Tests` 46개 통과.
- 이번 단위 — Summary/history comparison signal 구현 계획
  - D080 설계를 실제 구현 가능한 5개 Task 로 분해했다.
  - Task 1은 `BaselineReport`/reader payload·target settings 전파만 다룬다.
  - Task 2는 summary comparison model/generator 를 추가한다.
  - Task 3은 summary JSON/Markdown output 에 comparison field/section 을 쓴다.
  - Task 4는 history reader/generator 가 session comparison 을 읽고 집계하게 한다.
  - Task 5는 history JSON/Markdown output 과 CLI smoke 로 comparison mismatch 가 hard gate exit code 를 바꾸지 않음을 확인한다.
  - 검증: D080 설계, 현재 benchmark model/writer/reader/test 구조를 대조해 touched files, Red/Green 경계,
    테스트 주석 요구, 커밋 경계를 계획서에 명시했다.
- 이번 단위 — Summary/history comparison signal 설계
  - D079 raw report metadata 이후 남은 summary/history 비교 가능성 표현을 설계했다.
  - `comparison-compatible`, `comparison-key`, `comparison-mismatch-count`, `comparison-mismatches`,
    `unknown-runner-count` field 를 summary/history additive schema 로 정리했다.
  - summary 안의 `load`와 `open-loop`은 서로 다른 `scenario`를 가질 수 있으므로, 단일 scenario key 대신
    `result-name`별 `cases` 배열을 comparison key 로 사용하기로 했다.
  - 결정: D080으로 comparison signal 은 기존 hard gate, `warning-count`, CLI exit code 에 영향을 주지 않는
    non-failing compatibility artifact 로 둔다.
  - 검증: 현재 `BaselineReport`, `BaselineSummary*`, `BaselineHistory*`, D079 raw writer/reader 구조를 대조했다.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 246개 통과.
- 이번 단위 — Benchmark runner identity Task 1~3 구현 검토
  - D079 설계, 구현 계획, `BenchmarkRunIdentity` model, raw writer, raw reader, focused tests 를 대조했다.
  - 새 Blocker/Major finding 은 없다.
  - Minor testing 관찰: 실제 writer shape test 가 `os-architecture`, `process-architecture` field 를 직접 assert하지 않아
    이후 field drift 를 더 강하게 잡으려면 writer-to-reader roundtrip test 가 유용하다.
  - 검증: 코드/테스트/문서 대조 완료. `git diff --check`, solution build 경고 0/오류 0, solution tests 246개 통과.
- 이번 단위 — Benchmark runner identity Task 3 raw report reader/legacy compatibility
  - `BaselineReport`가 `BenchmarkRunIdentity`를 보존하게 했다.
  - `BaselineReportReader`가 raw report metadata 를 optional field 로 읽고, metadata 가 없는 legacy report 는
    `BenchmarkRunIdentity.Unknown`으로 보존하게 했다.
  - Red: `BaselineReport.Identity` property 부재로 contract test 가 `Assert.NotNull()` 실패함을 확인했다.
  - Red: metadata 포함 raw report reader test 가 `Expected: tcp-loopback-saea-v1, Actual: unknown`으로 실패함을 확인했다.
  - Green: focused `BaselineReportReaderWriterTests` 6개 통과, `Hps.Benchmarks.Tests` 44개 통과.
  - 최종 검증: `git diff --check`, solution build 경고 0/오류 0, solution tests 246개 통과.
- 이번 단위 — Benchmark runner identity Task 2 raw report writer metadata
  - `TcpLoopbackRunResult`가 `BenchmarkRunIdentity`를 보존하게 했다.
  - `TcpLoopbackReportWriter`가 raw report top-level 에 `benchmark-profile`, `runner-id`, `runner-kind`,
    `transport-backend`, OS/framework/architecture/process architecture/processor count metadata 를 기록한다.
  - Red: writer metadata shape test 가 `benchmark-profile` 미기록으로 `Assert.True()` 실패함을 확인했다.
  - Green: focused writer metadata test 1개 통과, `Hps.Benchmarks.Tests` 41개 통과.
  - 최종 검증: `git diff --check`, solution build 경고 0/오류 0, solution tests 243개 통과.
- 이번 단위 — Benchmark runner identity Task 1 model
  - `BenchmarkRunIdentity` 내부 model 을 추가해 raw report metadata 의 공통 identity 타입을 만들었다.
  - 기본값은 `benchmark-profile=tcp-loopback-saea-v1`, `runner-id=local-unspecified`, `runner-kind=local`,
    `transport-backend=SaeaTransport`다.
  - `CaptureDefault()`는 runtime OS/framework/architecture/process architecture/processor count 를 수집하되,
    host name/user name/IP address 는 자동 수집하지 않는다.
  - Red: 타입 부재 contract test 1개 `Assert.NotNull()` 실패, behavior tests 2개가 `unknown` 반환으로 실패함을 확인했다.
  - Green: focused `BenchmarkRunIdentityTests` 3개 통과, `Hps.Benchmarks.Tests` 40개 통과.
  - 최종 검증: `git diff --check`, solution build 경고 0/오류 0, solution tests 242개 통과.
- 이번 단위 — Benchmark runner identity 구현 계획
  - D079 설계를 raw report identity capture/write/read 구현으로 좁혀 3개 Task 로 분해했다.
  - Task 1은 `BenchmarkRunIdentity` model 과 privacy 우선 `CaptureDefault()`를 고정한다.
  - Task 2는 `TcpLoopbackRunResult`와 raw report writer 에 metadata field 를 추가한다.
  - Task 3은 `BaselineReportReader`가 신규 metadata 와 legacy report 를 모두 읽게 한다.
  - summary/history comparison signal, warning-as-failure, latency hard gate 는 이번 계획 범위에서 제외했다.
  - 검증: 계획 placeholder scan 결과 없음, `git diff --check`, solution build 경고 0/오류 0, solution tests 239개 통과.
- 이번 단위 — Benchmark runner identity / baseline comparison readiness 설계
  - baseline history command 이후 남은 Phase 4 backlog 를 D069/D070/D071/D078 기준으로 다시 정렬했다.
  - CI workflow, warning-as-failure, latency hard gate 보다 먼저 raw report 에 runner/environment metadata 를 남겨야 한다고 판단했다.
  - D079로 `schema-version: 1` additive metadata field, privacy 우선 기본값, `HPS_BENCHMARK_RUNNER_ID` 명시 식별자 정책을 정리했다.
  - 검증: `git diff --check`, solution build 경고 0/오류 0, solution tests 239개 통과.
  - 다음 구현은 사용자 검토 후 raw report identity capture/write/read 계획부터 작성한다.
- 이번 단위 — Baseline history report command 구현 검토
  - Task 1~4 parser/reader/generator/writer/Program wiring 을 D078 계약과 대조했다.
  - 실제 baseline root CLI smoke 로 `session-count: 3`, `hard-passed: true`, `warning-count: 0`을 확인했다.
  - 새 Blocker/Major finding 은 없다.
  - 비차단 후속: CLI optional Markdown path 오류 메시지 정밀화, date root 직접 입력 Program smoke 추가 여부.
- 이번 단위 — Baseline history report command Task 4 Program wiring/smoke
  - `Program.Main`에 `BenchmarkCommand.SummarizeBaselineHistory` branch 를 연결했다.
  - CLI는 `BaselineHistoryReader` → `BaselineHistoryGenerator` → `BaselineHistoryWriter`/`BaselineHistoryMarkdownWriter` 경로를 사용한다.
  - warning-only history 는 soft signal 이므로 success exit code 를 유지하고, failed session 이 있으면 failed-run exit code 를 반환한다.
  - Red: focused Program tests 3개가 구현 전 usage error exit code 2 반환으로 실패함을 확인했다.
  - Green: focused Program tests 3개 통과, 실제 baseline root CLI smoke 는 session-count 3, hard-passed true, warning-count 0을 출력했다.
  - 최종 검증: `git diff --check`, solution build 경고 0/오류 0, solution tests 239개 통과.
- 이번 단위 — Baseline history report command Task 3 history aggregate/writer
  - `BaselineHistory`, `BaselineHistoryGenerator`, `BaselineHistoryWriter`, `BaselineHistoryMarkdownWriter`를 추가했다.
  - history JSON schema 는 `history-version`, `source-root`, `session-count`, `hard-passed`, `failed-session-count`, `warning-count`, `sessions`를 쓴다.
  - Markdown writer 는 session table 과 warning session list 를 보조 artifact 로 출력한다.
  - Red: reflection contract test 1개 실패, behavior tests 5개가 aggregate/writer stub 에서 실패함을 확인했다.
  - Green: focused generator/writer tests 5개 통과, solution tests 236개 통과.
- 이번 단위 — Baseline history report command Task 2 history domain/reader
  - `BaselineHistorySession`과 `BaselineHistoryReader.ReadSessions(...)`를 추가했다.
  - parent baseline root/date root 입력, legacy root summary, `session-NN/summary.json`, summary 없음, by-kind 누락 p99/null 경계를 검증했다.
  - Red: reflection contract test 1개 실패, behavior tests 4개가 stub `NotSupportedException`으로 실패함을 확인했다.
  - Green: focused reader tests 4개 통과, solution tests 231개 통과.
- 이번 단위 — Baseline history report command Task 1 parser contract
  - `BenchmarkCommand.SummarizeBaselineHistory`와 `BenchmarkCommandLine` history path 필드를 추가했다.
  - `BenchmarkCommandParser`가 `--summarize-baseline-history` command, 필수 `--history`, 선택 `--history-md`, `--report` 혼용 거부를 처리한다.
  - `Program.PrintUsage`에 history command usage 를 추가했지만, 실행 switch wiring 은 Task 4로 남겼다.
  - Red: focused parser tests 에서 새 history command 5개가 실패함을 확인했다.
  - Green: focused parser tests 15개 통과, solution tests 227개 통과.
- 이번 단위 — Baseline history report command 구현 계획 리뷰 보정
  - `.claude/review/2026-06-23-baseline-history-report-command-review.md`의 must-fix 성격 의견을 설계/계획에 반영했다.
  - history `hard-passed`는 모든 session summary 의 `hard-passed` AND 로 계산하고, 상위 실패 카운터는 `failed-session-count`로 기록한다.
  - load/open-loop p99 가 summary 에 없으면 `0`으로 위장하지 않고 JSON `null`, Markdown `-`로 드러낸다.
  - 다음 구현 단위는 변함없이 Task 1(parser contract)이다.
- 이번 단위 — Baseline history report command 구현 계획
  - D078 설계를 `docs/superpowers/plans/2026-06-23-baseline-history-report-command.md`로 구현 가능한 Task 1~4 단위로 쪼갰다.
  - Task 1은 parser/usage contract 만 다루고, 실행 wiring 은 Task 4로 보류해 첫 구현 커밋을 작게 유지한다.
  - 새 타입이 필요한 Task 2/3은 assertion-failure Red 를 만들기 위해 reflection contract Red → stub → behavior Red 순서로 계획했다.
  - 검증: 설계 문서, 리뷰 문서, benchmark parser/source, summary reader/writer 패턴을 대조했다.
- 이번 단위 — Baseline history report command 설계 리뷰
  - `docs/superpowers/specs/2026-06-23-baseline-history-report-command-design.md`를 D069/D070/D071, 현재 benchmark CLI/parser 구조,
    `docs/benchmarks/baselines/index.md`와 대조했다.
  - enum 이름 모호성(`HistoryBaseline` 또는 `SummarizeBaselineHistory`)과 parent baseline root/date root discovery 모호성을 발견해
    설계 문서에서 바로 보정했다.
  - D078로 history command 를 provider-independent aggregate artifact 로 두고 warning 은 계속 soft signal 로 유지한다고 기록했다.
  - 검증: benchmark CLI/source, summary writer/generator, baseline artifact 구조를 대조했다.
- 이번 단위 — Phase 4 backlog 재평가 및 baseline history command 설계
  - stable identity / UDP lease sweep must-fix 체인이 닫힌 뒤 Phase 4 backlog 를 다시 대조했다.
  - QoS/Server diagnostics 는 v1 보류 결정이 이미 있고, CI workflow/warning-as-failure 는 runner identity 가 부족해 아직 이르다고 판단했다.
  - 다음 구현 후보를 `--summarize-baseline-history <baseline-root> --history <output-json> [--history-md <output-md>]` command 로 좁혔다.
  - 검증: `PLAN.md`, `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`, baseline specs/plans/review, benchmark CLI source 를 대조했다.
- 이번 단위 — UDP lease sweep registry race guard 리뷰
  - `a817c6e`의 handler gate 직렬화, PUBLISH fan-out lock 범위, race regression test 를 검토했다.
  - Blocker/Major correctness finding 은 없고, race test 의 250ms scheduling window 는 비차단 Minor 관찰로 남겼다.
  - 검증: `git show`/`rg`/line review 로 코드·테스트·D077 정합성을 대조했다.
- 이번 단위 — UDP lease sweep registry race guard
  - F1 후속 must-fix 로 sweep expired snapshot 과 같은 target `REGISTER`가 겹칠 때 stale registry cleanup 이 새 online 상태를
    disconnected 로 덮는 race 를 막았다.
  - `BrokerUdpDatagramHandler`에 handler-local gate 를 두고 UDP receive command, endpoint-close cleanup, lease sweep state mutation 을
    직렬화했다. `PUBLISH` fan-out 은 lock 밖에서 유지한다.
  - deterministic race 테스트를 추가해 기존 구현의 `Assert.True()` failure 를 확인하고 Green 으로 전환했다.
  - 검증: focused race test Red/Green, focused UDP handler tests 17개 통과, Broker tests 73개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 222개 통과.
- 이번 단위 — UDP stable identity F1/F2 수정분 리뷰
  - `b85220f`, `8749c64`를 대상으로 lease sweep registry cleanup 과 invalid identity datagram isolation 을 검토했다.
  - F2는 의도대로 UDP shared endpoint close 를 막는다.
  - F1에는 timer sweep 과 UDP receive path 가 동시에 같은 stable target 을 갱신할 때 stale expired snapshot 이 새 등록 상태를
    disconnected 로 덮을 수 있는 race 가 남아 있음을 확인했다.
  - 검증: `rg` 기반 코드 경계 대조와 리뷰 문서 작성. `git diff --check`, solution build 경고 0/오류 0,
    solution tests 221개 통과.
- 이번 단위 — UDP invalid stable identity datagram isolation
  - F2 must-fix 로 UDP stable identity validation 실패가 handler 밖으로 escape 하지 않도록 수정했다.
  - `BrokerUdpDatagramHandler`는 `REGISTER`/`UNREGISTER` 처리 전에 identity token 을 비예외 방식으로 검사하고,
    실패 시 endpoint close 나 기존 subscription 변경 없이 해당 datagram 만 drop 한다.
  - 검증: Red assertion failure 2개 확인(`Assert.Null()` failure), focused invalid identity tests 2개 통과,
    focused UDP handler tests 16개 통과. `git diff --check`, solution build 경고 0/오류 0, solution tests 221개 통과.
- 이번 단위 — UDP stable identity lease sweep registry cleanup
  - F1 must-fix 로 UDP lease sweep 이 stable registry current target 을 disconnected 상태로 바꾸도록 수정했다.
  - `UdpRemoteLeaseTracker.SweepExpired(...)`는 기존 반환값을 routing 제거 수로 유지하면서 만료 target snapshot 을 선택적으로 채운다.
  - `BrokerUdpDatagramHandler.SweepExpiredUdpLeases(...)`는 registry 주입 경로에서 snapshot 을 받아 `RemoveTarget(...)`으로 수명 상태를 맞춘다.
  - 검증: Red assertion failure 1개 확인(`Expected: 1, Actual: 0`), focused UDP handler tests 14개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 219개 통과.
- 이번 단위 — Stable subscriber identity post-implementation cross-verification
  - D075/D076 설계, Protocol/Broker/Server 구현, TCP/UDP handler, loopback coverage 를 교차검증했다.
  - 코드 수정 없이 리뷰 문서로 must-fix 2건을 기록했다.
  - 검증: 문서 self-review 와 `rg` 기반 소스/테스트 대조를 수행했다.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 218개 통과.
- 이번 단위 — Stable subscriber identity UDP loopback coverage
  - stable identity UDP rebind 가 fake handler 단위뿐 아니라 실제 `BrokerServer` + `SaeaTransport` UDP datagram loopback 에서도
    동작하는지 검증하는 테스트를 추가했다.
  - old remote 가 `REGISTER device-a` 후 `SUBSCRIBE alpha`를 보내고, new remote 가 같은 id 로 `REGISTER`만 하면
    old remote routing target 이 제거되고 retained topic set 이 new remote 로 재바인딩되어 이후 publish payload 를 받는지 확인한다.
  - 검증: focused stable UDP loopback test 1개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 218개 통과.
- 이번 단위 — Stable subscriber identity TCP loopback coverage
  - stable identity 가 fake handler 단위뿐 아니라 실제 `BrokerServer` + `SaeaTransport` TCP accept/receive/send pump 에서도
    동작하는지 검증하는 loopback 테스트를 추가했다.
  - old subscriber 가 `REGISTER device-a` 후 `SUBSCRIBE alpha`를 보내고, new subscriber 가 같은 id 로 `REGISTER`만 하면
    old socket 이 닫히고 retained topic set 이 new socket 으로 재바인딩되어 이후 publish payload 를 받는지 확인한다.
  - 검증: focused stable TCP loopback test 1개 통과.
- 이번 단위 — Stable subscriber identity UDP late REGISTER lease cleanup
  - stable identity self-review 중 UDP remote 가 `SUBSCRIBE` 후 `REGISTER`하는 순서에서 routing 구독은 제거되지만,
    optional lease tracker 에 pre-register topic metadata 가 남을 수 있음을 확인했다.
  - `UdpRemoteLeaseTracker.ReplaceSubscribedTopics(...)`로 REGISTER 성공 후 같은 remote 의 lease metadata 를
    registry rebound topic set 으로 교체하고, topic 이 없으면 lease 를 제거하도록 보정했다.
  - 검증: `BrokerUdpDatagramHandlerTests` Red assertion failure 1개 확인, focused UDP handler tests 13개 통과.
- 이번 단위 — Stable subscriber identity late REGISTER stale subscription cleanup
  - self-review 중 `SUBSCRIBE` 후 `REGISTER` 순서에서 metadata 에 없는 runtime 구독이 close cleanup 이후 stale target 으로 남을 수 있음을 확인했다.
  - `SubscriberRegistry.Register(...)`가 새 target 을 stable identity 에 매핑하기 전에 같은 runtime target 의 기존 routing 구독을 제거하도록 보정했다.
  - D076으로 late REGISTER 정책을 기록하고 stable identity 설계 문서에 command ordering 기준을 추가했다.
  - 검증: `SubscriberRegistryTests` Red assertion failure 1개 확인, focused registry tests 10개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 215개 통과.
- 이번 단위 — Stable subscriber identity BrokerServer opt-in wiring
  - Task 5로 `BrokerServerOptions`에 stable identity enabled/retention 설정과 factory/with method 를 추가했다.
  - `BrokerServer`가 enabled options 일 때 shared `SubscriberRegistry`를 만들고 TCP/UDP handler 에 같은 registry 를 주입한다.
  - TCP 또는 UDP start 성공 후 retention timer 를 한 번만 만들고, StopAsync 에서 UDP lease timer 와 함께 dispose 한다.
  - 검증: stable identity Server/Options Red assertion failure 7개 확인, focused stable tests 7개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 214개 통과.
- 이번 단위 — Stable subscriber identity UDP handler wiring
  - Task 4로 `BrokerUdpDatagramHandler`에 optional `SubscriberRegistry` internal constructor 를 추가했다.
  - UDP `REGISTER`/`UNREGISTER`, registered remote subscribe/unsubscribe, same-id remote rebind,
    duplicate target datagram-drop, endpoint close retention 을 registry/lease tracker 경로로 연결했다.
  - `UdpRemoteLeaseTracker.RemoveRemote(...)`와 `MarkSubscribedTopics(...)`를 추가해 rebind 시 old lease 제거와 new lease topic 복구를 처리한다.
  - 검증: internal constructor 부재 Red assertion failure 4개 확인, focused UDP handler tests 12개 통과.
- 이번 단위 — Stable subscriber identity TCP handler wiring
  - Task 3으로 `BrokerTcpFrameHandler`에 optional `SubscriberRegistry`와 `TimeProvider` internal constructor 를 추가했다.
  - TCP `REGISTER`/`UNREGISTER`, registered target subscribe/unsubscribe, same-id reconnect rebind,
    same-target different-id reject/close, connection close cleanup 을 registry 경로로 연결했다.
  - 검증: internal constructor 부재 Red assertion failure 4개 확인, focused TCP handler tests 11개 통과.
- 이번 단위 — Stable subscriber identity pure registry
  - Task 2로 `SubscriberIdentity`, `SubscriberRegistrationResult`, `SubscriberRegistry`를 추가했다.
  - registry 는 registered target 의 subscribe/unsubscribe metadata, same-id rebind, duplicate target conflict,
    disconnect retention, explicit unregister, disconnected sweep, UDP endpoint cleanup 을 다룬다.
  - 검증: reflection contract Red assertion failure 2개, behavior Red assertion failure 10개 확인,
    focused broker identity/registry tests 15개 통과.
- 이번 단위 — Stable subscriber identity protocol decode
  - Task 1로 `TcpCommandKind.Register/Unregister`와 decoder 분기를 추가했다.
  - `REGISTER`/`UNREGISTER`는 기존 `SUBSCRIBE`/`UNSUBSCRIBE`와 같은 단일 token 문법을 사용한다.
  - 검증: Red assertion failure 9개 확인, focused protocol tests 24개 통과.
- 이번 단위 — Stable subscriber identity 구현 계획
  - D075 설계를 구현 가능한 5개 Task 로 분해했다.
  - 각 Task 는 Red-Green-Refactor, touched files, produced interfaces, 검증 명령, 커밋 경계를 포함한다.
  - 검증: 계획 self-review 로 spec coverage, placeholder, type consistency 를 확인했고,
    `git diff --check`, solution build/test 로 문서 변경이 빌드 상태를 깨지 않음을 확인한다.
- 이번 단위 — Stable subscriber identity / reconnect rebinding 정책 설계
  - D058/D059/D060 이후 남아 있던 stable subscriber identity 후속 방향을 D075로 정리했다.
  - 기본 runtime target 모델을 유지하고, 후속 opt-in `REGISTER` identity registry, duplicate/rebind, disconnect retention, 테스트 순서를 설계했다.
  - 검증: 실제 `BrokerSubscriber`, `SubscriptionTable`, TCP/UDP handler, 기존 identity policy 문서와 대조했고,
    `git diff --check`, solution build/test 로 문서 변경이 빌드 상태를 깨지 않음을 확인한다.
- 이번 단위 — BrokerServer UDP lease host timer wiring
  - D074 구현 두 번째 단위로 `BrokerServerOptions`를 `BrokerServer`와 `BrokerUdpDatagramHandler`에 연결했다.
  - UDP start 성공 시 sweep timer 를 만들고, Stop/start 실패 시 timer 수명을 정리한다.
  - 검증: Red assertion failure 2개 확인, focused tests 2개 통과, 생성자 reflection 제거 후 focused tests 2개 통과,
    solution build 경고 0/오류 0, solution tests 175개 통과.
- 이번 단위 — BrokerServerOptions public 설정 타입
  - D074 구현 첫 단위로 `BrokerServerOptions`를 추가했다.
  - 기본 disabled, 0 이하 timeout/interval 거부, explicit 값과 `TimeProvider` 보존을 테스트했다.
  - 검증: Red assertion failure 3개 확인, focused tests 3개 통과, reflection 제거 후 focused tests 3개 통과,
    solution build 경고 0/오류 0, solution tests 173개 통과.
- 이번 단위 — BrokerServer UDP lease host timer 설계
  - D074로 `BrokerServerOptions` public 설정 표면, 기본 disabled 정책, 명시 timeout/interval 입력, `TimeProvider` timer 수명,
    Broker friend assembly 경계를 확정했다.
  - 검증: 설계 self-review, `git diff --check`, solution build/test.
- 이번 단위 — UDP lease tracker handler wiring
  - D073 구현 Task 4로 `BrokerUdpDatagramHandler`를 `UdpRemoteLeaseTracker`에 연결했다.
  - public constructor 는 기존처럼 disabled lease options 를 사용해 기본 동작을 보존하고, internal constructor 로 테스트/후속 host wiring 이 options/time provider 를 주입한다.
  - 검증: Red assertion failure 2개 확인, focused handler tests 8개 통과, reflection 제거 후 focused handler tests 8개 통과,
    solution build 경고 0/오류 0, solution tests 170개 통과.
- 이번 단위 — UDP remote lease pure sweep
  - D073 구현 Task 3으로 `UdpRemoteLeaseTracker.SweepExpired(DateTimeOffset)`를 추가했다.
  - plan 예시의 survivor remote setup 은 같은 시점 구독이면 함께 만료되므로, survivor를 늦게 구독하도록 테스트를 보정했다.
  - 검증: Red assertion failure 3개 확인, focused tests 8개 통과, reflection 제거 후 focused tests 8개 통과,
    solution build 경고 0/오류 0, solution tests 168개 통과.
- 이번 단위 — UDP remote lease tracker activity
  - D073 구현 Task 2로 내부 `UdpRemoteLeaseTracker`와 focused 테스트를 추가했다.
  - 계획서의 compile-failure Red는 AGENTS의 assertion-failure Red 규칙에 맞춰 reflection 기반 타입 부재 assertion 실패로 보정했다.
  - tracker는 기본 비활성 options 에서 기존 subscription 동작을 보존하고, 활성 options 에서 `(IUdpEndpoint, EndPoint)`당 lease 1개를 추적한다.
  - 검증: Red assertion failure 5개 확인, focused tests 5개 통과, reflection 제거 후 focused tests 5개 통과,
    solution build 경고 0/오류 0, solution tests 165개 통과.
- 이번 단위 — UDP lease options
  - D073 구현 Task 1로 내부 `UdpLeaseOptions`와 테스트 assembly internal 접근 경계를 추가했다.
  - 계획서의 `Enabled(...)` factory 이름은 `Enabled` property 와 C# 멤버 이름이 충돌하므로 `CreateEnabled(...)`로 보정했다.
  - 검증: Red assertion failure 확인, focused tests 3개 통과, solution build 경고 0/오류 0, solution tests 160개 통과.
- 이번 단위 — UDP optional lease sweep 구현 계획
  - D073 설계를 내부 options, lease tracker activity, 순수 sweep, handler wiring 의 4개 커밋 단위로 쪼갰다.
  - host timer, public settings, 기본 timeout 값 확정은 별도 후속 범위로 남겼다.
  - 검증: 계획 self-review 완료, `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 157개 통과.
- 이번 단위 — UDP optional lease tracker / sweep owner 설계
  - lease/sweep owner(Broker 소유·Server 트리거), key, 내부 options 설정 표면(기본 비활성), `TimeProvider` 시간 추상화,
    sweep 의 `UnsubscribeAll(IUdpEndpoint, EndPoint)` 사용 방식을 D073으로 확정했다.
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 157개 통과.
- 이번 단위 — UDP remote-wide unsubscribe primitive
  - D072 idle sweep 선행 API로 `(IUdpEndpoint, EndPoint)` 조합을 모든 topic 에서 제거하는 `SubscriptionTable` overload 를 추가했다.
  - 검증: focused Red/Green/Refactor 완료, solution build 경고 0/오류 0, solution tests 157개 통과.
- 이번 단위 — UDP stale remote idle expiry 설계
  - UDP remote cleanup owner, key, activity 갱신 규칙, sweep 범위, 다음 최소 구현 단위를 정리했다.
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 156개 통과.
- 이번 단위 — baseline history index 추가
  - 2026-06-18 baseline root/session-02/session-03 summary artifact 를 전역 index 에 연결했다.
  - report history 와 warning soft-signal 정책을 D071로 확정했다.
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 156개 통과.
- 이번 단위 — baseline report history/warning 정책 설계
  - baseline summary 이후 report history 단위, summary artifact 역할, warning-as-failure 보류 조건을 provider-independent 설계로 정리했다.
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 156개 통과.
- `6a3d747 docs: add baseline summary markdown artifacts`
  - 2026-06-18 baseline root/session-02/session-03 에 `summary.md` 보조 artifact 를 생성했다.
  - 검증: build 0/0, solution tests 156개 통과, `git diff --check` 통과.
- `17df6d0 feat: wire baseline summary markdown cli`
  - `--summarize-baseline`에 `--summary-md <output-md>` 선택 옵션을 연결했다.
  - JSON summary 는 canonical artifact 로 유지하고 Markdown 은 리뷰용 보조 출력으로 둔다.
- `8a48faa feat: write baseline summary markdown`
  - `BaselineSummaryMarkdownWriter`를 추가했다.

## 다음 단일 작업 단위

첫 CI artifact repository baseline 채택 이후 Phase 4 재평가를 완료했다(D096).
`ci-windows-x64-01/2026-06-25/session-01`은 hard-passed true, warning-count 0,
comparison-compatible true 인 좋은 reference signal 이지만, date root 1개/session 1개뿐이므로
latency hard gate 또는 warning-as-failure 로 승격하지 않는다.
CI runner evidence 는 future push-triggered run 이 더 쌓일 때 D095 checklist 로 수동 채택 여부를 다시 판단한다.

Phase 5 Windows RIO backend boundary 설계를 완료했다(D097).
RIO backend 는 TCP-first 로 진행하되, 첫 구현 task 는 TCP pump 가 아니라 project skeleton,
Windows capability probe, native function table wrapper 로 분리한다.
기본 `TransportFactory.CreateDefault()`는 SAEA를 유지하고, RIO는 명시 opt-in/test path 로 먼저 검증한다.

RIO 구현 계획을 `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`에 작성했다.
계획은 D097 설계를 6개 task 로 쪼갠다: project skeleton/capability probe, native function table loader,
registered buffer owner, TCP queue owner, TCP opt-in guard, TCP pump/contract test reuse.

RIO 구현 계획 Task 1(project skeleton/capability probe)을 완료했다.
`Hps.Transport.Rio`와 `Hps.Transport.Rio.Tests` project 를 solution 에 추가했고,
`RioCapabilityProbe`, `RioCapabilityStatus`, `RioTransport` skeleton 을 만들었다.
기본 `TransportFactory.CreateDefault()`는 계속 SAEA를 반환한다.

RIO 구현 계획 Task 2(native function table loader)를 완료했다.
`RioNative` 타입을 추가했고, `RioCapabilityProbe.GetStatus()`가 Windows에서 예외 없이
`Available` 또는 `Unavailable`로 수렴하도록 native loader 경계를 연결했다.
현재 loader 는 아직 실제 `WSAIoctl` marshalling 을 수행하지 않고 fallback 가능한 `Unavailable` 경계만 고정한다.

RIO 구현 계획 Task 3(registered buffer owner)을 완료했다.
`RioRegisteredBufferPool`은 outstanding request 가 완료되기 전에는 pinned block 을 반환하지 않고,
completion 이 중복 호출되어도 buffer 를 한 번만 release 한다.
RIO test project 는 `InternalsVisibleTo`로 direct internal API 검증을 사용하도록 정리했다.

RIO 구현 계획 Task 4(TCP queue owners)를 완료했다.
`RioRequestQueue`는 receive/send outstanding quota 를 각각 독립적으로 제한하고,
completion 호출 후 같은 quota 를 다시 예약할 수 있다. `RioCompletionQueue`는 native CQ 연결 전
Dispose 경계를 가진 skeleton 으로 추가했다.

RIO 구현 계획 Task 5(TCP opt-in transport guard)를 완료했다.
`RioTransport.ListenTcpAsync`/`ConnectTcpAsync`는 실행 중 lifecycle 확인 뒤 RIO capability 를 먼저 검사하고,
현재 환경에서 Windows RIO function table 을 사용할 수 없으면 명시적인 `NotSupportedException`으로 실패한다.
기본 `TransportFactory.CreateDefault()`/SAEA 경로는 계속 변경하지 않았다.

RIO 구현 계획 Task 5.5(native function table loader hardening)를 완료했다(D098).
`RioNative`는 Windows에서 `WSAIoctl(SIO_GET_MULTIPLE_EXTENSION_FUNCTION_POINTER, WSAID_MULTIPLE_RIO)`를 호출해
`RIO_EXTENSION_FUNCTION_TABLE`을 실제로 얻고, receive/send, completion queue, request queue, dequeue,
notify, register/deregister buffer 필수 pointer 가 비어 있지 않을 때만 `Available`로 판정한다.
현재 Windows 개발 환경에서 `RioCapabilityProbe.GetStatus()`는 `Available`로 검증됐다.

RIO 구현 계획 Task 5.6(native buffer registration delegate)를 완료했다.
`RioNative.RegisterBuffer(...)`/`DeregisterBuffer(...)`가 loaded function table 의
`RIORegisterBuffer`/`RIODeregisterBuffer` delegate 를 호출하며, `PinnedBlockMemoryPool` block 을 실제로
등록/해제하는 focused 테스트로 검증했다.

RIO 구현 계획 Task 5.7(native completion queue delegate)를 완료했다.
`RioNative.CreateCompletionQueue(...)`/`CloseCompletionQueue(...)`가 loaded function table 의
`RIOCreateCompletionQueue`/`RIOCloseCompletionQueue` delegate 를 호출하며, null notification completion 기반
polling CQ 생성/해제를 focused 테스트로 검증했다.

RIO 구현 계획 Task 5.8(native request queue delegate)를 완료했다.
`RioNative.CreateTcpSocket()`은 `WSASocketW`에 `WSA_FLAG_OVERLAPPED | WSA_FLAG_REGISTERED_IO`를 지정해
RIO request queue 가 붙을 수 있는 TCP socket 을 만들고, `RioNative.CreateRequestQueue(...)`는
`RIOCreateRequestQueue` delegate 를 호출한다. 일반 .NET `Socket`으로는 RQ handle 이 0으로 실패함을 확인하고
registered I/O socket 생성 경계로 보정했다.

RIO 구현 계획 Task 5.9(native completion dequeue delegate)를 완료했다.
`RioNative.DequeueCompletion(...)`은 loaded function table 의 `RIODequeueCompletion` delegate 를 호출하고,
SDK `RIORESULT`에 맞춘 `RioResult` struct 배열에 completion 을 받는다.
빈 CQ에서 0개 completion 이 반환되는 focused 테스트로 marshalling 을 검증했다.

RIO 구현 계획 Task 5.10(native receive/send posting delegate surface)를 완료했다.
`RioNative.Receive(...)`/`Send(...)`는 loaded function table 의 `RIOReceive`/`RIOSend` delegate 를 공유 posting helper 로 호출하고,
SDK `RIO_BUF`에 맞춘 `RioBufferSegment` struct 를 사용한다. 이번 단위는 operation surface 와 argument validation 만 고정했고,
실제 connected posting completion 은 다음 단위로 분리한다.

RIO 구현 계획 Task 5.11(connected native posting completion verification)을 완료했다.
registered I/O TCP socket 과 일반 peer socket 을 loopback 으로 연결해 `RIOReceive` post→peer send→CQ completion→registered buffer write,
`RIOSend` post→CQ completion→peer receive 경로가 모두 동작함을 focused 테스트로 검증했다.
production 변경 없이 Task 5.6~5.10 native operation boundary 가 함께 맞물리는지 확인한 test-hardening 단위다.

직전 작업은 계획 Task 6인 `RioTransport` TCP pump/contract test reuse 다.
native function table, registered I/O socket, CQ/RQ, buffer registration, dequeue, receive/send posting completion 이 모두 검증됐으므로,
`RioTransport.ListenTcpAsync`/`ConnectTcpAsync`와 receive/send pump 를 실제 transport contract 에 연결했다.

RIO 구현 계획 Task 6(TCP pump/contract test reuse)을 완료했다.
`RioTransport`는 opt-in backend 로 TCP listen/connect/accept 를 만들고, 각 connection 에 RIO CQ/RQ 기반 receive/send pump 를 붙인다.
`TransportConnection` pending send queue 를 재사용하기 위해 `Hps.Transport`는 `Hps.Transport.Rio`에 internal 접근을 허용한다.
일반 accepted socket 은 RIO request queue 생성이 실패하므로, listener 는 registered I/O accept socket 을 미리 만들어
`AcceptAsync(Socket, CancellationToken)`에 전달한다(D099).
RIO available Windows 환경에서 `TrySend` payload 가 peer receive handler 로 도착하는 loopback test 로 검증했다.
전체 테스트 1차 실행 중 CQ close 와 background dequeue 경합이 native access violation 으로 드러났고,
`RioConnectionResource`가 `RIODequeueCompletion`과 `RIOCloseCompletionQueue`를 같은 gate 로 직렬화하도록 보정했다.

다음 작업은 RIO Task 6 구현 self-review 와 hardening 단위 확정이다.
방금 추가한 pump 를 SAEA 기준선의 close notify, length-prefix send, pending/in-flight release, receive block 반환,
completion polling 수명과 대조해 즉시 보강할 항목과 deferred 항목을 분리한다.

RIO Task 6 구현 self-review 를 완료했다.
상세는 `docs/agent-state/reviews/2026-06-25-rio-task6-self-review.md`에 기록했다.
최소 opt-in RIO TCP loopback 은 만족하지만, SAEA 기준선과 비교하면 RIO send partial completion loop 와
outstanding request close-drain owner 가 다음 hardening 대상이다.
focused RIO tests 10회 반복 실행은 모두 통과했다.

다음 작업은 RIO TCP pump hardening 설계와 구현이다.
먼저 send partial completion loop, close drain owner, 테스트 경계를 설계 문서로 고정한 뒤,
Red-Green으로 실행 가능한 부분부터 구현한다.

RIO TCP pump hardening 설계와 구현을 완료했다.
설계는 `docs/superpowers/specs/2026-06-25-rio-tcp-pump-hardening-design.md`,
실행 계획은 `docs/superpowers/plans/2026-06-25-rio-tcp-pump-hardening.md`에 기록했다.
`RioTransport` send path 는 RIO completion byte count 를 기준으로 remaining loop 를 돌며,
length prefix 와 payload 모두 같은 helper 를 사용한다.
RIO tests 는 raw payload, 4096-byte payload, length-prefixed stream send 를 검증한다.
close-drain full owner 재구조화는 현재 반복 테스트에서 flake/crash 가 재현되지 않아 deferred 로 유지한다.

다음 작업은 RIO TCP close/churn stress 또는 default factory 승격 전 contract suite 확장 중 우선순위를 재평가하는 것이다.
현재 evidence 기준으로는 factory default 변경보다 close/churn stress test 와 outstanding request owner 설계를 먼저 보는 편이 안전하다.

RIO TCP close/churn stress coverage 를 추가했다.
`TcpLoopback_WhenRioAvailable_RepeatedCloseAfterAcceptDoesNotCrash`는 connect/accept 직후 close 를 25회 반복해
receive pump 가 outstanding RIOReceive 를 가진 상태에서 socket/CQ 정리와 경합해도 testhost crash 없이 끝나는지 검증한다.
focused RIO tests 22개와 10회 반복 실행이 모두 통과했으므로, full outstanding request owner 재구조화는 현재는 deferred 로 유지한다.

다음 작업은 RIO default factory 승격이 아니라 RIO contract suite 확장 여부 재평가다.
구체적으로는 send queue ownership/drop-oldest, handler exception close notify, unavailable fallback 정책을 RIO 전용 테스트로
더 고정할지 판단한다.

RIO handler exception close notify 계약은 테스트로 고정했다.
`ReceivePump_WhenRioAvailable_HandlerThrowsClosesConnectionAndNotifiesHandler`는 client 가 payload 를 보내고
server receive handler 가 예외를 던질 때 RIO receive pump 가 해당 connection close notification 으로 수렴하는지 검증한다.
현재 RIO 구현은 이미 UDP/SAEA와 같은 정책을 만족해 production 변경 없이 focused RIO tests 23개가 통과했다.

다음 작업은 RIO send queue ownership/drop-oldest 계약을 live loopback 으로 의미 있게 검증할 수 있는지 확인하는 것이다.
실제 socket pump 가 빠르게 drain 하면 queue saturation 을 재현하기 어려우므로, 테스트가 brittle 해질 경우 forced queue owner 나
runtime 공통 계약 테스트 재사용으로 범위를 줄인다.

RIO send queue/drop-oldest 는 D100으로 범위를 닫았다.
RIO는 `TransportBase.CreateConnection()`이 만든 shared `TransportConnection` pending queue 를 그대로 쓰므로,
drop-oldest ownership, in-flight release, close drain, diagnostics callback 은
`tests/Hps.Transport.Tests/Runtime/TransportSendQueueTests.cs`를 source of truth 로 둔다.
live RIO loopback saturation 은 OS socket drain 속도에 의존해 flake 가능성이 높으므로 별도 테스트로 추가하지 않는다.

다음 작업은 RIO opt-in phase 의 default factory policy 를 최종 재확인하는 것이다.
이미 `CreateDefault_DuringRioOptInPhase_ReturnsSaeaTransport`가 존재하므로, 필요한 작업은 추가 코드보다
현재 상태 문서와 decision 이 factory 승격 보류를 명확히 가리키는지 확인하는 쪽이다.

RIO default factory opt-in policy 정합성 재평가를 완료했다.
`src/Hps.Transport/Runtime/TransportFactory.cs`는 계속 `SaeaTransport`를 반환하고,
`tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`의
`CreateDefault_DuringRioOptInPhase_ReturnsSaeaTransport`가 이 정책을 고정한다.
D097/D098/D100도 RIO를 명시 opt-in/test path 로 유지한다고 일관되게 설명하므로 production 변경은 필요 없다.

다음 작업은 Phase 5의 남은 검증 항목인 SAEA vs RIO benchmark 비교를 설계하는 것이다.
기존 Phase 4 benchmark report schema 와 runner/baseline 정책을 재사용하되, RIO는 아직 default factory 가 아니므로
명시 backend 선택 경로 또는 별도 RIO benchmark command 가 필요한지 먼저 설계로 닫는다.

SAEA vs RIO benchmark comparison 설계를 완료했다(D101).
설계 문서는 `docs/superpowers/specs/2026-06-25-saea-rio-benchmark-comparison-design.md`다.
결정은 benchmark 내부 `--backend <saea|rio>` selector 를 추가하되 `TransportFactory.CreateDefault()`는 건드리지 않는 것이다.
raw report schema 는 유지하고 `benchmark-profile`, `transport-backend`, `scenario` 값을 backend 별로 다르게 채워
summary/history comparison 이 SAEA/RIO artifact 혼합을 감지하게 한다.

다음 작업은 benchmark backend selector 의 첫 구현 단위다.
parser/options/result identity 경계부터 TDD로 고정하고, 실제 `TcpLoopbackScenarioRunner` RIO 생성은 그 다음 단위에서 진행한다.

benchmark backend selector 구현을 완료했다.
`--backend <saea|rio>`는 `--smoke`, `--load`, `--load-open-loop`, `--baseline-suite`에서 파싱되며,
summary/history 명령에서는 허용하지 않는다.
`TcpLoopbackScenarioRunner`는 backend 에 따라 `SaeaTransport` 또는 opt-in `RioTransport`를 생성하고,
raw report identity 는 `tcp-loopback-saea-v1`/`SaeaTransport` 또는 `tcp-loopback-rio-v1`/`RioTransport`로 분리된다.
SAEA/RIO smoke CLI 를 각각 실행해 report scenario/profile/backend 값이 분리됨을 확인했다.

다음 작업은 SAEA/RIO 비교 artifact 수집이다.
처음에는 repository baseline 채택 없이 임시 artifact directory 에 smoke/load/open-loop report 를 생성하고,
summary/history comparison 이 backend mismatch 를 감지하는지 확인한다.

SAEA/RIO comparison artifact 수집을 완료했다.
출력은 ignored scratch 경로 `artifacts/benchmarks/rio-comparison/2026-06-25/session-01/`에 남겼다.
SAEA/RIO load/open-loop 모두 delivery/drop/leak hard gate 는 pass 했지만, RIO latency 가 SAEA보다 크게 높다.
관측값은 SAEA load p99 890.8 us, SAEA open-loop p99 872.7 us,
RIO load p99 16654.0 us, RIO open-loop p99 16826.6 us 다.
RIO closed-loop load 는 elapsed 46501 ms 로 actual-rate 64.5 Hz 에 그쳤고, summary warning 으로
`load-p99-latency-high`, `actual-rate-low`, `open-loop-p99-latency-high`가 발생했다.
mixed summary 는 comparison-compatible false 와 6개 comparison-key mismatch 를 기록해 SAEA/RIO artifact 혼합 감지가 동작함을 확인했다.

다음 작업은 RIO completion polling latency 를 개선할 설계를 작성하는 것이다.
현재 `RioTransport`는 notification 없이 `RIODequeueCompletion` polling 과 `Task.Delay(1)`을 사용하므로,
benchmark p99 약 16 ms 의 주된 원인 후보가 completion wake 모델이다.

RIO completion wake latency 개선 설계를 완료했다(D102).
설계 문서는 `docs/superpowers/specs/2026-06-25-rio-completion-wake-latency-design.md`다.
이번 구현 방향은 IOCP/RIONotify 재구조화 전에 `WaitForCompletionAsync(...)`에서 bounded `Task.Yield()` polling 을 먼저 적용하고,
일정 횟수 이후에만 기존 `Task.Delay(1)` fallback 을 사용하는 것이다.
목표는 Windows timer granularity 로 인한 15~16 ms wake 지연을 제거하면서 close/churn 수명 구조를 크게 흔들지 않는 것이다.

다음 작업은 D102 구현이다.
`RioTransport.WaitForCompletionAsync(...)`에 fast yield polling budget 을 추가하고,
RIO focused tests, close/churn 반복, SAEA/RIO smoke 및 comparison benchmark 로 개선 여부를 검증한다.

D102 bounded yield polling 구현을 완료했다.
`RioTransport.WaitForCompletionAsync(...)`는 completion 이 비어 있을 때 4096회까지 `Task.Yield()`로 재시도한 뒤
기존 `Task.Delay(1)` fallback 으로 내려간다.
이 변경 과정에서 receive/send pump 가 동시에 close 를 관측하면 `OnConnectionClosed`가 중복 호출될 수 있는 경합이 드러나,
`TransportConnection.TryClose()`를 추가하고 SAEA/RIO notify 경로를 close 전이에 성공한 pump 만 handler 를 호출하도록 정렬했다.
Red evidence 는 RIO small payload wake 테스트가 기존 구현에서 16.199/10.392/14.022 ms sample 로 실패한 것이다.
4096 budget 이후 RIO load 는 actual-rate 99.8 Hz, p50 198.8 us 로 회복했지만 p99 는 16689.0 us 로 여전히 16ms대 tail 이 남았다.
따라서 bounded polling 은 throughput/median hardening 으로 닫고, p99 tail 제거는 D103 IOCP/RIONotify completion wait 설계로 넘긴다.

다음 작업은 RIO IOCP/RIONotify completion wait 설계다.
bounded polling 을 더 키우는 방식은 idle CPU 비용을 키우므로, p99 tail 을 제거하려면 native notification 기반 wait 로 전환할지,
전용 completion pump/thread 모델을 둘지 설계에서 결정한다.

RIO IOCP/RIONotify completion wait 설계를 완료했다(D104).
설계 문서는 `docs/superpowers/specs/2026-06-25-rio-iocp-notification-completion-wait-design.md`다.
결정은 CQ별 event handle 이 아니라 `RioTransport`당 shared IOCP pump 를 두고,
receive/send CQ별 `RioCompletionSignal`만 깨우는 구조다.
이 방향은 per-connection event handle 증가를 피하고, RIO p99 tail 제거와 후속 shared completion pump 확장에 맞다.

다음 작업은 D104 구현 계획 작성이다.
`RioNative` notification/IOCP P/Invoke, `RioCompletionPort`, `RioCompletionSignal`,
`RioConnectionResource` wiring, hardening/benchmark 를 TDD 가능한 task 로 나눈다.

D104 구현 계획을 완료했다.
계획 문서는 `docs/superpowers/plans/2026-06-25-rio-iocp-notification-completion-wait.md`다.
Task 는 native notification shape, completion port/signal owner, RIONotify+IOCP wiring,
benchmark observation/state update 의 4개 커밋 단위로 나뉜다.

다음 작업은 계획 Task 1인 native notification shape 구현이다.
먼저 RIO available 환경에서 `RioNative`가 notification function 을 노출하는지 실패 테스트로 고정한 뒤,
`RIONotify`, notification CQ creation, IOCP P/Invoke shape 를 추가한다.

D104 구현 계획 Task 1 native notification shape 를 완료했다.
`RioNative`는 이제 `RIONotify` delegate, notification CQ creation overload,
IOCP 관련 P/Invoke/struct shape, `SupportsCompletionNotification` probe 를 가진다.
Red는 `TryLoadFunctionTable_WhenRioAvailable_ExposesNotificationFunctions`가
`SupportsCompletionNotification` property 부재로 assertion failure 를 낸 것이다.
focused RIO tests 25개와 solution build 0경고/0오류를 확인했다.

다음 작업은 계획 Task 2 completion port/signal owner 구현이다.
아직 RIO native wait 에 연결하지 말고, 먼저 `RioCompletionPort`/`RioCompletionSignal`의 managed lifecycle,
wait wake, dispose wake 를 테스트로 고정한다.

D104 구현 계획 Task 2 completion port/signal owner 를 완료했다.
`RioCompletionPort`는 signal registry 와 dispose wake 를 소유하고,
`RioCompletionSignal`은 waiter wake, pump fault, dispose wake 를 관리한다.
아직 native IOCP handle 과 `RIONotify`에는 연결하지 않았다.
Red는 `RioCompletionPortTests`가 타입 부재 `Assert.NotNull` failure 를 낸 것이다.
focused completion port tests 2개, focused RIO tests 27개, solution build 0경고/0오류를 확인했다.

다음 작업은 계획 Task 3 RIONotify + IOCP wiring 구현이다.
`RioCompletionPort`에 실제 IOCP handle/pump 를 붙이고,
`RioConnectionResource`가 receive/send CQ를 notification CQ로 만들도록 연결한다.

D104 구현 계획 Task 3 RIONotify + IOCP wiring 을 완료했다.
`RioCompletionPort`는 실제 IOCP handle 과 pump task 를 소유하고,
`RioCompletionSignal`은 CQ별 notification memory, completion key, waiter wake 를 관리한다.
`RioConnectionResource`는 receive/send CQ를 notification CQ로 생성하고,
`WaitForCompletionAsync(...)`는 polling fallback 없이 `RIONotify` arm 후 signal wait 로 completion 을 기다린다.
검증은 focused RIO tests 27개, close/wake 핵심 테스트 10회 반복, solution build/test 전체 통과다.
benchmark session-04에서 RIO load p99 는 739.5 us, open-loop p99 는 948.8 us 로 내려갔다(D105).

다음 작업은 계획 Task 4 benchmark observation/state update 마무리다.
이미 session-04 artifact 는 수집했으므로, 남은 일은 final state doc 정리와 필요 시 후속 최적화 후보를 deferred 로 분리하는 것이다.

RIO IOCP/RIONotify completion wait Task 4 state update 는 `58c3c05` 커밋에서 함께 완료됐다.
session-04 benchmark 결과와 D105 결정이 상태 문서에 기록됐고, p99 tail 은 completion wait 관점에서 해소됐다.

다음 작업은 RIO registered buffer reuse 설계다.
현재 RIO receive/send path 는 operation 마다 `RIORegisterBuffer`/`RIODeregisterBuffer`를 호출하므로,
completion wait 다음 병목 후보를 buffer registration lifetime 으로 좁혀 설계한다.

RIO registered buffer reuse 설계를 완료했다(D106).
설계 문서는 `docs/superpowers/specs/2026-06-25-rio-registered-buffer-reuse-design.md`다.
다음 구현은 receive block 과 length-prefix block 을 connection resource lifetime 에 등록하는 Task A로 제한한다.
payload `RefCountedBuffer` registration cache 는 pool/array/native provider lifetime 이 얽히므로 별도 단위로 분리한다.

다음 작업은 D106 Task A 구현 계획 작성이다.
receive/prefix per-operation registration 제거를 TDD 가능한 task 로 나누고,
payload path 는 per-operation registration 유지 또는 별도 cache task 로 명시한다.

D106 Task A 구현 계획을 완료했다.
계획 문서는 `docs/superpowers/plans/2026-06-25-rio-registered-buffer-reuse-task-a.md`다.
작업은 receive block resource registration, length-prefix resource registration,
verification/benchmark observation 의 3개 task 로 나뉜다.

다음 작업은 D106 Task A 구현이다.
먼저 receive block 을 `RioConnectionResource` lifetime 에 대여/등록하고,
그 다음 length-prefix block 을 resource lifetime 에 등록한다.

RIO registered buffer reuse Task A 구현을 완료했다.
`RioConnectionResource`는 이제 receive block 과 TCP length-prefix block 을 connection resource lifetime 에서 한 번만
`RIORegisterBuffer`로 등록하고, dispose 시 `RIODeregisterBuffer`와 receive pool 반환을 수행한다.
payload `RefCountedBuffer` send path 는 D106에 따라 per-operation registration 을 유지한다.
Red evidence 는 신규 RIO loopback diagnostic tests 2개가 `RioNative` registration diagnostic 경계 부재로
`Assert.NotNull` 실패한 것이다.
Task A 이후 session-05 RIO benchmark 는 load actual-rate 99.8 Hz/p50 281.6 us/p99 866.6 us,
open-loop actual-rate 99.8 Hz/p50 315.8 us/p99 936.4 us 다.

다음 작업은 payload `RefCountedBuffer` registration cache 설계 여부를 재평가하는 것이다.
receive/prefix 쪽 per-operation registration 은 제거됐으므로, 남은 register/deregister 비용은 payload send path 에 집중된다.
단, payload cache 는 pool/array/native provider lifetime 과 fan-out ownership 경계가 얽히므로 바로 구현하지 않고
D106 Task B 설계로 먼저 닫는다.

RIO payload registration cache 설계를 완료했다(D107).
설계 문서는 `docs/superpowers/specs/2026-06-25-rio-payload-registration-cache-design.md`다.
결정은 transport-wide shared cache 가 아니라 `RioConnectionResource`가 소유하는 bounded cache 로 먼저 구현하는 것이다.
fan-out 최적화 폭은 작지만 close/dispose owner 가 명확하고, outstanding send 중 deregister 하지 않는 규칙을 작은 owner tests 로
검증할 수 있다. transport-wide shared cache 는 fan-out evidence 가 더 쌓인 뒤 별도 설계로 승격한다.

다음 작업은 D107 구현 계획 작성이다.
`RioPayloadRegistrationCache` pure owner, connection resource wiring, payload send path cache lease 전환,
verification/benchmark observation 을 TDD 가능한 task 로 나눈다.

D107 구현 계획을 완료했다.
계획 문서는 `docs/superpowers/plans/2026-06-25-rio-payload-registration-cache.md`다.
작업은 pure payload registration cache owner, payload send path cache lease, verification/benchmark/state update 의
3개 task 로 나뉜다.

다음 작업은 계획 Task 1인 `RioPayloadRegistrationCache` pure owner 구현이다.
먼저 fake registrar 기반 Red tests 로 cache hit, idle eviction, outstanding dispose delay, fallback lease 를 고정한다.

RIO payload registration cache Task 1 pure owner 구현을 완료했다.
`RioPayloadRegistrationCache`는 backing `byte[]` object identity 로 buffer id 를 cache 하고,
idle LRU eviction, outstanding lease dispose 지연, all-outstanding capacity fallback lease 를 처리한다.
Red evidence 는 reflection 기반 type boundary test 가 `Assert.NotNull` 실패한 것이다.
Green 이후 direct internal API tests 로 cache hit/eviction/dispose/fallback 4개 behavior 를 고정했다.

다음 작업은 계획 Task 2 payload send path cache lease 전환이다.
`RioConnectionResource`에 payload cache 를 소유시키고, `SendInFlightAsync(...)` payload 경로가
`RioPayloadRegistrationCache.Acquire(...)` lease 로 `SendRegisteredBufferAsync(...)`를 호출하게 한다.

RIO payload registration cache Task 2/3 구현과 검증을 완료했다.
`RioConnectionResource`는 connection-local bounded payload cache 를 소유하고,
payload send path 는 backing `byte[]` cache lease 로 `SendRegisteredBufferAsync(...)`를 호출한다.
기존 per-operation `SendRegisteredArrayAsync(...)` helper 는 제거했다.
Red evidence 는 같은 backing payload block 을 두 번 보내는 RIO loopback test 가 기존 구현에서
`Expected: 1, Actual: 2` registration count 로 실패한 것이다.
session-06 RIO benchmark 는 load actual-rate 99.8 Hz/p50 288.4 us/p99 906.9 us,
open-loop actual-rate 99.8 Hz/p50 293.8 us/p99 920.5 us 다.

다음 작업은 RIO payload cache 구현 self-review 다.
특히 connection-local cache capacity 64, dispose-delayed deregister, fallback lease, transport-wide cache defer 결정이
실제 fan-out/close 경로와 모순 없는지 소스와 테스트를 다시 대조한다.

## 이번 단위의 검증 경로

이번 cycle 은 RIO payload cache 구현 self-review 를 준비한다.

- 범위: `src/Hps.Transport.Rio/`, `src/Hps.Transport/Properties/AssemblyInfo.cs`,
  `tests/Hps.Transport.Rio.Tests/`, RIO hardening 설계/상태 문서.
- 검증: source/test/spec 대조, focused RIO tests 필요 시 재실행, `git diff --check`.

## 이번 작업에서 건드리지 않는 범위

- RIO UDP 구현 코드
- `TransportFactory` runtime 선택 코드 변경
- SAEA transport 동작 변경
- latency hard gate 또는 warning-as-failure 정책 구현
- CI artifact 자동 채택, pull_request trigger, schedule trigger
- Linux io_uring backend 구현
- stable identity 인증/권한 검증, persistence, payload replay

RIO UDP Task 3 receive loop 구현을 완료했다.
`RioUdpEndpoint`는 UDP 전용 RQ/CQ, remote address registered buffer, receive pool 을 소유하고,
`RioTransport.BindUdpAsync(...)`는 endpoint 등록 후 receive pump 를 시작한다.
receive pump 는 datagram 마다 `RefCountedBuffer`를 대여해 `RIOReceiveEx` data buffer 로 등록하고,
completion 이후 remote `SOCKADDR_INET`을 `EndPoint`로 decode 한 뒤 `ITransportDatagramHandler`에 owned datagram 을 전달한다.
handler 예외 또는 native/socket 오류는 SAEA UDP와 같이 endpoint close notification 으로 수렴한다.
첫 receive post 는 `BindUdpAsync(...)` 반환 전에 수행해 RIO UDP post 이전 datagram race 를 피하고,
UDP v1 completion wait 는 IOCP notification 이 아니라 bounded dequeue polling 으로 둔다.
Red evidence 는 `UdpReceive_WhenRawClientSendsDatagram_DeliversOwnedRefCountedBuffer`가 기존 skeleton 에서 timeout 실패한 것이다.
RIO native integration tests 는 같은 process 안의 provider/CQ 자원을 공유하므로
`Hps.Transport.Rio.Tests` test collection parallelization 을 비활성화했다.

RIO UDP Task 5 diagnostics parity 구현을 완료했다.
`RioTransport`는 `ITransportEndpointDiagnostics`를 구현하고, TCP connection 과 RIO UDP endpoint snapshot 을 함께 반환한다.
`RioUdpEndpoint.CreateSnapshot()`은 SAEA UDP와 같은 endpoint id, transport kind, state, pending send count,
pending send queue high-watermark, dropped pending send count 를 제공한다.
Red evidence 는 `GetEndpointSnapshots_WhenUdpEndpointIsOpen_ReturnsUdpSnapshot`가 기존 RIO transport 에서
`ITransportEndpointDiagnostics` assignability failure 로 실패한 것이다.

다음 작업은 RIO UDP backend self-review/default promotion readiness 재평가다.
D109의 native Ex, endpoint owner, receive loop, send loop, diagnostics parity 가 닫혔으므로
D108의 default backend promotion gate 중 남은 기능 parity/fallback/contract matrix 조건을 소스와 테스트 기준으로 재평가한다.

## 이번 단위의 검증 경로

이번 cycle 은 RIO UDP backend self-review/default promotion readiness 재평가를 수행한다.

- 범위: `src/Hps.Transport.Rio/`, `src/Hps.Transport/Runtime/TransportFactory.cs`,
  RIO/SAEA transport tests, D108/D109 결정 문서, root 상태 문서.
- 검증: source/test/decision matrix 대조, 필요 시 focused RIO/SAEA tests, solution build/test, `git diff --check`.

## 이번 작업에서 건드리지 않는 범위

- `TransportFactory` 기본 선택 코드 변경
- SAEA transport 동작 변경
- latency hard gate 또는 warning-as-failure 정책 구현
- CI artifact 자동 채택, pull_request trigger, schedule trigger
- Linux io_uring backend 구현
- stable identity 인증/권한 검증, persistence, payload replay

RIO UDP Task 1 native Ex operation shape 구현을 완료했다.
`RioNative`는 이제 `SupportsDatagramOperations` capability 와 `ReceiveEx`/`SendEx` wrapper 를 제공한다.
Ex wrapper 는 optional `RioBufferSegment`를 pinned `RIO_BUF` pointer 또는 null 로 marshalling 하고,
초기 범위에서는 control context, flags buffer, RIO flags 를 null/0 으로 고정한다.
Red evidence 는 focused tests 2개가 각각 `SupportsDatagramOperations` property 및 `ReceiveEx` method 부재로
`Assert.NotNull()` 실패한 것이다.
Green 이후 Ex argument validation test 를 추가해 null request queue 가 managed boundary 에서 `ArgumentException`으로 차단됨을 확인했다.

다음 작업은 RIO UDP Task 2 endpoint owner skeleton 계획 또는 구현이다.
`BindUdpAsync_WhenRioAvailable_ReturnsEndpointWithLocalEndPoint` Red 로 시작해 registered UDP socket bind,
endpoint tracking, close/unregister, unsupported 환경 명시 실패를 작은 단위로 닫는다.

## 이번 단위의 검증 경로

이번 cycle 은 RIO UDP Task 2 endpoint owner skeleton 을 계획하거나 바로 TDD 구현한다.

- 범위: `src/Hps.Transport.Rio/RioTransport.cs`, 신규 `RioUdpEndpoint` 후보,
  `tests/Hps.Transport.Rio.Tests/`, root 상태 문서.
- 검증: focused RIO UDP tests, focused RIO tests 전체, solution build/test, `git diff --check`.

## 이번 작업에서 건드리지 않는 범위

- RIO UDP receive/send loopback
- RIO UDP diagnostics parity 전체
- `TransportFactory` 기본 선택 코드 변경
- SAEA transport 동작 변경
- latency hard gate 또는 warning-as-failure 정책 구현
- CI artifact 자동 채택, pull_request trigger, schedule trigger
- Linux io_uring backend 구현
- stable identity 인증/권한 검증, persistence, payload replay

RIO UDP Task 1 native Ex operation shape 구현 계획을 완료했다.
계획 문서는 `docs/superpowers/plans/2026-06-25-rio-udp-native-ex-operation-shape.md`다.
범위는 `RioNative`의 `ReceiveEx`/`SendEx` delegate binding, `SupportsDatagramOperations`,
nullable `RIO_BUF` pointer marshalling, capability/argument validation tests 로 제한한다.
UDP endpoint, socket bind, receive/send loopback 은 후속 task 로 남긴다.

다음 작업은 계획 Task 1 Red tests 작성이다.
먼저 reflection 기반 `SupportsDatagramOperations` shape test 로 compile failure 가 아니라 assertion failure 를 만든다.

## 이번 단위의 검증 경로

이번 cycle 은 RIO UDP Task 1 Red tests 를 작성하고 실패를 확인한다.

- 범위: `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`.
- 검증: focused Red command 로 `Assert.NotNull(property)` 실패 확인.

## 이번 작업에서 건드리지 않는 범위

- RIO UDP endpoint 구현 코드
- RIO UDP loopback receive/send
- `TransportFactory` 기본 선택 코드 변경
- SAEA transport 동작 변경
- latency hard gate 또는 warning-as-failure 정책 구현
- CI artifact 자동 채택, pull_request trigger, schedule trigger
- Linux io_uring backend 구현
- stable identity 인증/권한 검증, persistence, payload replay

RIO UDP backend boundary 설계를 완료했다(D109).
설계 문서는 `docs/superpowers/specs/2026-06-25-rio-udp-backend-boundary-design.md`다.
결정은 RIO UDP를 TCP `RioConnectionResource`에 끼워 넣지 않고 UDP 전용 `RioUdpEndpoint` owner 로 설계하는 것이다.
`RIOSendEx`/`RIOReceiveEx`는 payload 뿐 아니라 local/remote address 도 registered buffer slice 로 다루므로,
completion 전까지 data/address buffer id 와 backing memory 가 유효해야 한다.
초기 v1은 SAEA UDP와 같은 no-prefetch receive, endpoint-local pending queue/drop-oldest,
`MaxOutstandingSend = 1`을 유지한다.

다음 작업은 RIO UDP Task 1 native Ex operation shape 구현 계획 작성이다.
`RioNative`의 `ReceiveEx`/`SendEx` delegate, nullable `RIO_BUF` marshalling, `SupportsDatagramOperations`
capability, capability tests 를 TDD 가능한 단위로 쪼갠다.

## 이번 단위의 검증 경로

이번 cycle 은 RIO UDP Task 1 native Ex operation shape 구현 계획을 작성한다.

- 범위: `src/Hps.Transport.Rio/RioNative.cs`, `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`,
  D109 설계 문서, root 상태 문서.
- 검증: D109 coverage self-review, placeholder scan, `git diff --check`.

## 이번 작업에서 건드리지 않는 범위

- RIO UDP endpoint 구현 코드
- `TransportFactory` 기본 선택 코드 변경
- SAEA transport 동작 변경
- latency hard gate 또는 warning-as-failure 정책 구현
- CI artifact 자동 채택, pull_request trigger, schedule trigger
- Linux io_uring backend 구현
- stable identity 인증/권한 검증, persistence, payload replay

RIO backend default promotion readiness 설계를 완료했다(D108).
설계 문서는 `docs/superpowers/specs/2026-06-25-rio-default-promotion-readiness-design.md`다.
결정은 `TransportFactory.CreateDefault()`를 RIO로 바꾸지 않는 것이다.
현재 RIO는 TCP opt-in path 만 구현했고 UDP `BindUdpAsync(...)` parity 가 없으므로,
Interface Server 기본 backend 로 승격하면 TCP/UDP endpoint 발행 목표와 충돌한다.
기본 factory 승격은 기능 parity, no-throw fallback, backend contract matrix, benchmark evidence,
운영/문서 경계가 모두 닫힌 뒤 별도 결정으로만 진행한다.
`TransportFactory` XML doc 도 현재 D108 opt-in 정책에 맞게 갱신했다.

다음 작업은 RIO UDP backend 설계다.
기본 backend 승격을 위해서는 먼저 RIO UDP parity 를 닫는 방향이 composite backend 보다 구조적으로 단순하다.
다음 설계에서는 `RIOSendEx`/`RIOReceiveEx`, remote endpoint buffer ownership, endpoint close notify,
datagram backpressure/drop-oldest, diagnostics parity 를 다룬다.

## 이번 단위의 검증 경로

이번 cycle 은 RIO UDP backend boundary 를 설계한다.

- 범위: `src/Hps.Transport.Rio/`, `src/Hps.Transport/Saea/SaeaUdpEndpoint.cs`,
  `src/Hps.Transport/Abstractions/IUdpEndpoint.cs`, SAEA UDP tests, RIO native surface.
- 검증: SAEA UDP behavior 대조, RIO datagram operation shape 대조, 설계 문서 placeholder scan,
  `git diff --check`.

## 이번 작업에서 건드리지 않는 범위

- RIO UDP 구현 코드
- `TransportFactory` 기본 선택 코드 실제 변경
- SAEA transport 동작 변경
- latency hard gate 또는 warning-as-failure 정책 구현
- CI artifact 자동 채택, pull_request trigger, schedule trigger
- Linux io_uring backend 구현
- stable identity 인증/권한 검증, persistence, payload replay

RIO payload cache 구현 self-review 를 완료했다.
검토 문서는 `docs/agent-state/reviews/2026-06-25-rio-payload-cache-self-review.md`다.
self-review 중 idle eviction 의 native deregister 가 cache lock 내부에서 호출되는 minor issue 를 발견했고,
정상 eviction 경로는 buffer id 를 수집한 뒤 lock 밖에서 deregister 하도록 리팩터했다.
새 registration 실패 경로에서는 제거한 idle registration 이 누수되지 않도록 예외 정리를 추가했다.
Major/blocker finding 은 없으며, transport-wide shared payload cache 와 cache capacity diagnostics 는 deferred 유지다.

다음 작업은 RIO backend default promotion readiness 설계다.
현재 RIO는 opt-in/test path 로 충분히 검증되고 있지만 `TransportFactory` 기본 후보로 승격하려면
capability probe, fallback 조건, SAEA contract parity, 운영 리스크, 검증 gate 를 먼저 문서로 닫아야 한다.

## 이번 단위의 검증 경로

이번 cycle 은 RIO backend default promotion readiness 를 설계한다.

- 범위: `src/Hps.Transport/Runtime/TransportFactory.cs`, `src/Hps.Transport.Rio/`,
  RIO tests, 관련 decisions/state 문서.
- 검증: factory/runtime 선택 흐름 소스 대조, RIO/SAEA contract test coverage 대조,
  설계 문서 placeholder scan, `git diff --check`.

## 이번 작업에서 건드리지 않는 범위

- `TransportFactory` 기본 선택 코드 실제 변경
- RIO UDP 구현 코드
- SAEA transport 동작 변경
- latency hard gate 또는 warning-as-failure 정책 구현
- CI artifact 자동 채택, pull_request trigger, schedule trigger
- Linux io_uring backend 구현
- stable identity 인증/권한 검증, persistence, payload replay

RIO UDP parity/default promotion readiness 검토를 완료했다(D110).
검토 문서는 `docs/agent-state/reviews/2026-06-26-rio-udp-parity-default-readiness-review.md`다.
RIO UDP native Ex, endpoint owner, receive loop, send loop, diagnostics parity 는 완료됐지만,
IPv4-only UDP, 공유 contract matrix 부족, fallback/default selection policy 부재, RIO UDP benchmark evidence 부족 때문에
`TransportFactory.CreateDefault()`는 계속 `SaeaTransport`를 반환한다.
RIO는 opt-in/test/benchmark backend 로 유지하고, default 승격은 shared contract matrix 와 benchmark/fallback/IPv6 판단 이후
별도 결정으로만 재평가한다.

다음 작업은 RIO/SAEA backend contract matrix 보강이다.
우선 RIO UDP close drain, drop-oldest/high-watermark, handler exception close notify,
no-prefetch/pool ownership 이 SAEA UDP와 같은 의미로 검증되는지 테스트 구조를 대조하고,
공유 가능한 계약 테스트부터 Red-Green 으로 추가한다.

## 이번 단위의 검증 경로

이번 cycle 은 RIO/SAEA backend contract matrix 보강을 설계하거나 첫 Red test 로 착수한다.

- 범위: `src/Hps.Transport/`, `src/Hps.Transport.Rio/`, `tests/Hps.Transport.Tests/`,
  `tests/Hps.Transport.Rio.Tests/`, RIO/SAEA UDP endpoint tests.
- 검증: 기존 SAEA/RIO UDP tests 대조, contract matrix 후보 정리, 첫 Red test 실패 확인,
  focused transport tests, focused RIO tests, 필요 시 solution build/test, `git diff --check`.

## 이번 작업에서 건드리지 않는 범위

- `TransportFactory` 기본 선택 코드 변경
- RIO UDP benchmark artifact 수집
- RIO unavailable fallback/default selection policy 구현
- IPv6 UDP RIO 지원 구현
- latency hard gate 또는 warning-as-failure 정책 구현
- CI artifact 자동 채택, pull_request trigger, schedule trigger
- Linux io_uring backend 구현
- stable identity 인증/권한 검증, persistence, payload replay

RIO/SAEA backend contract matrix 보강을 RIO UDP edge tests 로 완료했다.
`RioTransportUdpTests`는 이제 handler exception close notify, no-prefetch/pool ownership,
endpoint close-drain, drop-oldest release/diagnostics/high-watermark 를 검증한다.
Red에서는 blocked handler 중 보낸 두 번째 datagram 을 unblock 뒤 보장 수신한다고 기대한 no-prefetch 테스트가 timeout 으로 실패했다.
D111에 따라 RIO UDP no-prefetch 는 pool ownership/backpressure 경계이며,
blocked-window datagram retention 은 RIO v1 계약으로 주장하지 않는다.
최종 테스트는 pool 대여 미증가와 unblock 이후 loop 생존을 검증한다.

RIO UDP benchmark artifact 수집 범위와 command shape 설계를 완료했다(D112).
설계 문서는 `docs/superpowers/specs/2026-06-26-rio-udp-benchmark-artifact-design.md`다.
기존 `--smoke`, `--load`, `--load-open-loop`, `--baseline-suite` 실행 명령에
`--protocol <tcp|udp>` selector 를 additive option 으로 추가하고, 기본값은 `tcp`로 유지한다.
UDP raw report 는 기존 schema 를 재사용하며 `benchmark-profile`/`scenario`를 `udp-loopback-...` 계열로 채워 구분한다.
첫 RIO UDP evidence 는 repository baseline 이 아니라 `artifacts/benchmarks/rio-udp/...` scratch 영역에 수집한다.

RIO UDP benchmark Task 1 protocol selector model/parser 구현을 완료했다.
runner/baseline-suite command 는 이제 `--protocol <tcp|udp>`를 파싱해 `BenchmarkCommandLine.LoopbackProtocol`에 보존한다.
summary/history/help/target 또는 runner 없는 위치에서는 `--protocol`을 usage error 로 막는다.
UDP runner 가 아직 연결되지 않았기 때문에 Program 은 `--protocol udp` 실행을 실패 처리해 TCP smoke report 가
UDP evidence 로 잘못 저장되는 중간 상태를 막는다.

RIO UDP benchmark Task 2/3 UDP loopback runner dispatch 와 SAEA UDP smoke 구현을 완료했다.
`--smoke --protocol udp --backend saea --report <path>`는 이제 `BrokerServer.StartUdpAsync(...)`와
UDP `SUBSCRIBE`/`PUBLISH` datagram loopback 을 실행해 `udp-loopback-saea-baseline-smoke` raw report 를 만든다.
report schema 는 기존 writer 를 재사용하고, identity 는 `benchmark-profile=udp-loopback-saea-v1`,
`transport-backend=SaeaTransport`를 기록한다.

RIO UDP benchmark load/open-loop/baseline-suite 구현을 완료했다.
`--protocol udp --load`, `--load-open-loop`, `--baseline-suite`는 이제 UDP runner 로 dispatch 되고,
closed-loop/open-loop 모두 기존 raw report schema 를 재사용한다.
SAEA UDP CLI smoke 에서 4096B x 100Hz x 30초 load/open-loop 와 1-run baseline-suite 가
sent/received 3000/3000, dropped 0, pool-rented 0 으로 통과했다.

RIO/SAEA UDP benchmark scratch artifact 수집을 완료했다.
D112 기준으로 repository baseline 에 넣지 않고 ignored scratch 영역
`artifacts/benchmarks/rio-udp/2026-06-26/session-01/` 아래 backend별 raw report 와 summary 를 생성했다.
SAEA UDP summary 는 hard-passed true, warning 0 이다.
RIO UDP는 D113 fix 이후 smoke 와 closed-loop load delivery 는 통과했지만,
open-loop 는 sent 3000 / received 2263 / payload-errors 0 으로 hard gate 실패다.
RIO closed-loop/open-loop 모두 p99 가 약 16.7ms 로 남아 있어 UDP completion wait/no-prefetch window 가 다음 병목이다.

RIO UDP receive window hardening 설계 초안을 작성하고 2026-06-26 설계 리뷰 의견을 반영했다.
설계 문서는 `docs/superpowers/specs/2026-06-26-rio-udp-receive-window-hardening-design.md`다.
권장안은 handler dispatch 전에 다음 receive 를 먼저 post 하는 one-deep pre-post 이며,
handler 병렬 호출과 configurable receive depth 는 이번 범위에서 제외한다.
리뷰에서 지적된 close-drain blocker 는 `Close()`를 shutdown requester 로 제한하고 receive CQ close 를
receive loop drain 이후로 미루는 소유권 분리로 보정했다.
receive operation resource 는 receive loop 단일 소유, handler exception 중 이미 pre-post 된 next operation cleanup 은
receive loop 경로, remote address block 은 endpoint lifetime shared block + decode-before-next-post 로 고정한다.
one-deep pre-post 구현 계획은 `docs/superpowers/plans/2026-06-26-rio-udp-receive-window-hardening.md`에 작성했다.
계획은 close-safe one-deep receive loop 구현(Task 1)과 benchmark/D114 문서화(Task 2)로 나뉜다.
Task 1 close-safe one-deep receive loop 구현을 완료했다.
`RioUdpReceiveOperation`이 receive datagram 과 data buffer registration id 를 단일 소유하고,
`RioUdpEndpoint.Close()`는 shutdown request 로 제한되며 receive/send native resource 는 각 pump drain 이후 정리된다.
Red evidence 는 one-deep 기대 테스트 2개가 기존 D111 no-prefetch 구현에서 `Expected: 2, Actual: 1`로 실패한 것이다.
Task 2 benchmark/D114 문서화도 완료했다.
D114로 RIO UDP receive window 는 close-safe one-deep pre-post 정책으로 수락했고, D111 no-prefetch 정책은 superseded 됐다.
2026-06-26 `session-02/rio` scratch benchmark 에서 closed-loop load 는
sent 3000 / received 3000 / dropped 0 / pool-rented 0 / actual-rate 99.7 Hz 로 delivery hard gate 를 통과했다.
다만 open-loop 는 sent 3000 / received 2409 / actual-rate 85.7 Hz / p99 16709.1 us 로 hard gate 실패다.
summary 는 hard-passed false, warning 3(load p99 high, open-loop p99 high, actual-rate low)이다.
따라서 one-deep pre-post 는 수명/소유권 정책으로 수락하지만, RIO UDP 4096B x 100Hz open-loop 목표 달성은 아직 주장하지 않는다.
RIO UDP open-loop residual loss/tail 재평가 설계도 완료했다.
설계 문서는 `docs/superpowers/specs/2026-06-26-rio-udp-open-loop-residual-loss-tail-design.md`다.
D115로 다음 구현 후보는 receive depth 확대가 아니라 UDP completion wait 의 IOCP/RIONotify parity 로 정했다.
근거는 RIO UDP p99 16.7ms tail 이 현재 `WaitForUdpCompletionAsync(...)`의 `Task.Delay(1)` fallback 및 Windows timer quantum 과 맞고,
TCP RIO는 이미 CQ notification pointer + `RIONotify` + IOCP signal wait pattern 을 사용한다는 점이다.
D115 구현 계획도 완료했다.
계획 문서는 `docs/superpowers/plans/2026-06-26-rio-udp-completion-notification-wait.md`다.
계획은 Task 1 endpoint notification resource shape, Task 2 UDP wait notification 전환, Task 3 scratch benchmark/D116 판단으로 나뉜다.
Task 1 endpoint notification resource shape 구현을 완료했다.
`RioUdpEndpoint`는 receive/send `RioCompletionSignal`을 소유하고, UDP receive/send CQ를 notification completion pointer 로 생성한다.
`RioTransport.BindUdpAsync(...)`는 TCP RIO와 같은 shared `RioCompletionPort`를 endpoint 에 넘긴다.
Red evidence 는 `BindUdpAsync_WhenRioDatagramAvailable_CreatesUdpCompletionSignals`가 기존 endpoint 에서 `Assert.NotNull()` failure 로 실패한 것이다.
Task 2 wait path 전환도 완료했다.
`RioUdpEndpoint.ArmNotification(...)`은 CQ drain 과 같은 lock 에서 `RIONotify`를 arm 하고,
`WaitForUdpCompletionAsync(...)`는 open 상태에서 `Task.Delay(1)` polling 없이 signal wait 로 대기한다.
close-drain fallback 은 owner cleanup 을 위해 제한적으로 유지한다.
Red evidence 는 `RioUdpEndpoint_WhenNotificationWaitIsExpected_ExposesArmNotificationHelper`가 기존 endpoint 에서
`Assert.NotNull()` failure 로 실패한 것이다.
검증은 focused Red/Green, `RioTransportUdpTests` 15개, `Hps.Transport.Rio.Tests` 52개,
solution build 경고 0/오류 0, solution test 333개 통과로 마쳤다.
Task 3 scratch benchmark 와 D116 판단도 완료했다.
RIO `session-03/load`는 sent/received 3000/3000, p99 481 us 로 통과했다.
RIO `session-03/open-loop`는 sent/received 3000/2373, p99 647.6 us 로 p99 tail 은 해결됐지만 delivery hard gate 는 실패했다.
D116은 partial 로 기록했다. 다음 작업은 RIO UDP open-loop delivery loss 를 receive-side 관점에서 설계하는 것이다.
receive-side 후속 설계와 구현 계획도 완료했다.
설계 문서는 `docs/superpowers/specs/2026-06-26-rio-udp-bounded-receive-window-design.md`이고,
구현 계획은 `docs/superpowers/plans/2026-06-26-rio-udp-bounded-receive-window.md`다.
D117로 receive payload registration reuse 가 아니라 bounded receive slot window 를 다음 구현 후보로 결정했다.
첫 depth 는 2, completion mapping 은 `RioResult.RequestContext`, remote address 는 slot-local registered buffer,
payload data buffer 는 D113대로 datagram 마다 등록하고 completion 직후 deregister 한다.
bounded receive window Task 1 depth-2 receive behavior 구현도 완료했다.
`UdpReceive_WhenHandlerIsBlocked_PreservesTwoQueuedDatagramsWithBoundedWindow`는 기존 one-deep 구현에서
`Expected: 3`, `Actual: 2`로 실패했고, Green 이후 focused test 1개, `RioTransportUdpTests` 16개,
`Hps.Transport.Rio.Tests` 53개가 통과했다.
Task 2 close/drain cleanup hardening 은 별도 production 변경 없이 Task 1 slot cleanup 구현으로 닫았다.
focused cleanup tests 2개가 통과했고, receive loop finally 는 slot 배열을 dispose 한 뒤 endpoint receive CQ를 닫는다.
Task 3 scratch benchmark 와 D118 판단도 완료했다.
RIO `session-04/load`는 sent/received 3000/3000, p99 831.8 us 로 통과했다.
RIO `session-04/open-loop`은 sent/received 3000/3000, p99 889.4 us 로 통과했다.
summary 는 hard-passed true, warning 0이다.
D118로 bounded receive window 를 RIO UDP open-loop delivery hard gate 를 닫은 기준선으로 수락했다.
RIO UDP gate 이후 default selection policy 설계도 완료했다(D119).
설계 문서는 `docs/superpowers/specs/2026-06-26-rio-default-selection-policy-after-udp-design.md`다.
결정은 `TransportFactory.CreateDefault()`를 계속 deterministic SAEA default 로 유지하고,
RIO preferred fallback 정책은 `Hps.Transport` base assembly 가 아니라 RIO assembly 를 참조할 수 있는
host/composition layer 또는 별도 selector package 에 두는 것이다.
reflection 기반 default RIO loading 은 배포/버전/관측성 문제를 숨기므로 채택하지 않는다.
explicit RIO benchmark 또는 explicit RIO backend 선택은 unavailable 시 SAEA로 fallback 하지 않고 실패한다.
host/composition transport selection policy 설계도 완료했다(D120).
설계 문서는 `docs/superpowers/specs/2026-06-26-host-composition-transport-selection-policy-design.md`다.
결정은 첫 적용 대상을 `samples/Hps.Sample.BrokerServer`로 두고, 기존 positional arguments 를 유지하면서
optional `--transport <saea|rio|auto>`를 추가하는 것이다. 기본값은 `saea`, explicit `rio`는 unavailable 시 실패,
`auto`는 RIO available 시 RIO를 쓰고 unavailable/unsupported 시 관측 가능한 SAEA fallback 을 수행한다.
sample broker transport selector 구현 계획도 완료했다.
계획 문서는 `docs/superpowers/plans/2026-06-26-sample-broker-transport-selector.md`다.
계획은 Task 1 parser/model, Task 2 transport selector policy, Task 3 Program wiring/smoke 검증으로 나눈다.
Task 1 parser/model 구현도 완료했다.
새 `Hps.Sample.BrokerServer.Tests` project 를 solution 에 추가했고,
sample broker host 전용 `SampleTransportMode`, `SampleBrokerServerCommandLine`,
`SampleBrokerServerCommandParser`를 추가했다.
기존 3 positional args 는 SAEA mode 로 유지되고, optional `--transport rio|auto`는 parser model 에 보존된다.
`--transport` 값 누락과 unknown value 는 broker start 전 usage error 로 구분한다.
Task 2 selection policy 구현도 완료했다.
sample broker server project 가 `Hps.Transport.Rio`를 참조하고,
`SampleTransportSelection`/`SampleTransportSelector`가 `saea`, explicit `rio`, preferred `auto` 정책을 구현한다.
selector 는 RIO capability probe 와 transport factory delegate 를 주입받으므로 tests 가 실제 OS/RIO availability 에 의존하지 않는다.
explicit `rio`는 unavailable 시 runtime failure `1`로 실패하고, `auto`는 unavailable/unsupported 시 SAEA fallback notice 를 반환한다.
Task 3 Program wiring/smoke 구현도 완료했다.
`samples/Hps.Sample.BrokerServer/Program.cs`는 `SampleBrokerServerCommandParser`와 `SampleTransportSelector`를 사용해
transport 를 만들고, startup output 에 selected backend 를 표시한다.
usage 는 `[--transport <saea|rio|auto>]`를 포함하며, invalid transport option 은 broker start 전에 exit code 2로 종료한다.
sample broker transport selector 구현 self-review 도 완료했다.
검토 문서는 `docs/agent-state/reviews/2026-06-26-sample-broker-transport-selector-self-review.md`다.
Blocker/Major finding 은 없고, self-review 중 발견한 minor 2건을 같은 단위에서 TDD로 보정했다.
parser 는 invalid port/max-frame-bytes 에 대해 구체적인 오류 메시지를 반환하고,
selector 는 정의되지 않은 `SampleTransportMode` 값을 auto fallback 으로 오해하지 않고 `ArgumentOutOfRangeException`으로 드러낸다.
검증은 focused Red/Green, sample broker server tests 15개, solution build 경고 0/오류 0,
solution tests 349개 통과, `git diff --check`로 마쳤다.
RIO UDP IPv6 support gate 설계도 완료했다(D121).
설계 문서는 `docs/superpowers/specs/2026-06-26-rio-udp-ipv6-support-gate-design.md`다.
결정은 RIO UDP v1을 IPv4-only opt-in backend 로 유지하고, IPv6는 default promotion gate 로 남기는 것이다.
지금 즉시 full IPv6를 구현하지 않고, unsupported IPv6 local/remote endpoint 를 RIO UDP public boundary 에서
명시적으로 막는 guard 를 다음 구현 단위로 좁혔다.
구현 계획은 `docs/superpowers/plans/2026-06-26-rio-udp-ipv6-unsupported-guard.md`에 있다.
RIO UDP IPv6 unsupported boundary guard 구현도 완료했다.
`BindUdpAsync(...)`는 IPv6 local endpoint 를 명시적 `NotSupportedException`으로 거부하고,
`TrySendTo(...)`는 IPv6 remote endpoint 를 enqueue 하지 않고 `false`로 반환한다.
Red evidence 는 기존 구현에서 bind 가 `SocketException`으로 실패하고 send 가 `true`를 반환한 것이다.
검증은 focused Red/Green, `Hps.Transport.Rio.Tests` 55개, solution build 경고 0/오류 0,
solution tests 351개 통과, `git diff --check`로 마쳤다.
RIO address-family-aware selection policy 설계와 구현도 완료했다(D122).
설계 문서는 `docs/superpowers/specs/2026-06-29-rio-address-family-aware-selection-policy-design.md`,
구현 계획은 `docs/superpowers/plans/2026-06-29-rio-address-family-aware-selection.md`다.
결정은 RIO backend 의 현재 public support matrix 를 TCP/UDP IPv4 `IPEndPoint` 전용으로 명시하고,
full IPv6 RIO 구현은 default promotion gate 로 남기는 것이다.
`RioTransport.ListenTcpAsync(...)`와 `ConnectTcpAsync(...)`는 IPv6 endpoint 를 socket bind/connect 전에
명시적 `NotSupportedException`으로 거부한다.
sample broker `--transport auto`는 IPv6/non-IPv4 listen endpoint 에서 RIO available 여부와 무관하게
SAEA fallback notice 를 반환하고, explicit `--transport rio`는 runtime failure 를 반환한다.
검증은 focused Red/Green, `Hps.Transport.Rio.Tests` 57개,
`Hps.Sample.BrokerServer.Tests` 17개, solution build 경고 0/오류 0,
solution tests 355개 통과, `git diff --check` 통과로 마쳤다.
2026-06-29 `local-win-x64-01` explicit runner baseline 도 세 session 으로 수집했다(D123).
2026-06-29 date root 는 session-count 3, hard-passed true, warning-count 0, comparison-compatible true 다.
runner root history 는 2026-06-24/2026-06-25/2026-06-29 세 date root, 총 9-session 을 묶고
hard-passed true, warning-count 0, comparison-compatible true 를 유지한다.
explicit runner envelope 는 load p99 max 935.6 us, open-loop p99 max 1077.4 us 로 기존 maxima 를 유지한다.
D082의 explicit runner 3-date-root evidence 조건은 충족했지만, warning-as-failure 또는 CI latency gate 승격은
threshold/운영 정책을 별도 단위에서 재평가한 뒤 결정한다.
local 3-date-root evidence 기반 gate 승격 정책도 재평가했다(D124).
결론은 `local-win-x64-01` 9-session envelope 를 runner-local reference envelope 로 채택하되,
기존 `BaselineSummaryGenerator` warning threshold 는 그대로 두고 warning-as-failure/CI latency gate 는 계속 보류하는 것이다.
현재 warning threshold 는 runner/profile scoped 가 아닌 전역 상수이므로 local SAEA TCP loopback 수치를 전역 threshold 로 낮추면
CI/RIO/UDP benchmark 에도 같은 기준이 적용된다.
runner/profile scoped warning envelope model 설계도 완료했다(D125).
결정은 기존 `warning-count`와 summary/history hard gate 의미를 유지하고,
reference history 와 candidate summary/history 를 읽는 별도 envelope comparison artifact 를 추가하는 것이다.
이 artifact 는 `envelope-compatible`, `envelope-signal-count`, kind별 reference/limit/candidate metric 을 기록하지만,
초기에는 process failure, CI failure, warning-as-failure 로 승격하지 않는다.
검증은 D125 spec placeholder scan, `git diff --check`, solution build 경고 0/오류 0,
solution tests 355개 통과로 마쳤다.
runner/profile scoped envelope comparison command 구현 계획도 작성했다.
계획 문서는 `docs/superpowers/plans/2026-06-29-runner-profile-envelope-comparison.md`이며,
Task 1 parser contract, Task 2 source reader, Task 3 generator, Task 4 writer/Program wiring 으로 나뉜다.
검증은 plan placeholder/type consistency scan, `git diff --check`, solution build 경고 0/오류 0,
solution tests 355개 통과로 마쳤다.
runner/profile scoped envelope comparison Task 1 parser contract 구현을 완료했다.
`--compare-baseline-envelope <candidate-json> --reference-history <reference-history-json> --envelope <output-json> [--envelope-md <output-md>]`
command 를 parser 가 인식하고, `BenchmarkCommandLine`에 candidate/reference/output path 를 보존한다.
`--report`, `--backend`, `--protocol` 같은 실행 runner option 은 envelope comparison 과 함께 쓰면 usage error 로 막는다.
Red evidence 는 compare envelope parser tests 7개가 기존 parser 에서 `parsed=false` 또는 `Command=None`으로 실패한 것이다.
Green 확인은 compare envelope tests 7개와 전체 `BenchmarkCommandParserTests` 29개 통과로 마쳤다.
표준 검증은 `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 362개 통과로 마쳤다.
runner/profile scoped envelope comparison Task 2 source reader 구현을 완료했다.
`BaselineEnvelopeSourceReader.Read(...)`는 candidate summary/history 와 reference history 를 같은 source model 로 수렴시키고,
history 의 `sessions[].summary-path`를 history 파일 directory 기준으로 다시 열어 full `by-kind` aggregate 를 읽는다.
`BaselineComparisonJsonReader`를 추가해 summary/history comparison JSON parsing 을 공유하고,
기존 `BaselineHistoryReader`의 legacy summary incompatible 처리 의미를 유지한다.
Red evidence 는 reader contract type 부재 `Assert.NotNull()` failure 와, stub reader 의 `NotSupportedException` behavior failure 3건이다.
Green 확인은 `BaselineEnvelopeSourceReaderTests` 4개와 `BaselineHistoryReaderTests` 7개 통과로 마쳤다.
표준 검증은 `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 366개 통과로 마쳤다.
runner/profile scoped envelope comparison Task 3 generator 구현을 완료했다.
`BaselineEnvelopeComparisonGenerator.Generate(...)`는 reference history 와 candidate source 의 comparison key 를 먼저 대조하고,
hard-passed/compatible reference summary 만 eligible envelope 재료로 사용한다.
kind별 metric row 는 D125 기준으로 upper-bound/lower-bound limit 을 계산하고,
초과·미달은 기존 `warning-count`가 아니라 `BaselineEnvelopeSignal`로 분리해 기록한다.
Red evidence 는 generator type 부재 `Assert.NotNull()` failure 와, stub generator 의 compatible/key/signal/no-reference 단언 실패 5건이다.
Green 확인은 `BaselineEnvelopeComparisonGeneratorTests` 6개 통과로 마쳤다.
표준 검증은 `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 372개 통과로 마쳤다.
runner/profile scoped envelope comparison Task 4 writer/Program wiring 구현을 완료했다.
`BaselineEnvelopeComparisonWriter`는 `envelope-version: 1` JSON artifact 를 쓰고,
`BaselineEnvelopeComparisonMarkdownWriter`는 같은 comparison model 을 사람이 읽는 Markdown 표로 쓴다.
`Program.Main`은 `--compare-baseline-envelope <candidate-json> --reference-history <reference-history-json> --envelope <output-json> [--envelope-md <output-md>]`
command 를 reader/generator/writer 경로로 연결하며, envelope signal/mismatch 가 있어도 artifact 생성 성공 시 exit code 0을 유지한다.
Red evidence 는 writer type 부재 `Assert.NotNull()` failure 2건, writer stub `NotSupportedException` failure 2건,
Program switch 미연결 exit code 2 failure 2건이다.
Green 확인은 writer tests 4개, Program tests 2개, envelope 관련 tests 16개 통과와 실제 local runner artifact CLI smoke exit code 0으로 마쳤다.
표준 검증은 `git diff --check` 통과, .NET 9.0.314 MSBuild 기준 solution build 통과
(`NU1900` vulnerability feed 조회 경고 1건), solution tests 378개 통과로 마쳤다.
현재 로컬 기본 SDK 10.0.203에서는 BenchmarkDotNet transitive package metadata `CS0006`가 재현되므로,
다음 self-review 에서 SDK pin 또는 검증 환경 재현성 보강을 별도 인프라 후보로 평가한다.
runner/profile scoped envelope comparison command 구현 self-review 를 완료했다.
D125 schema 와 구현을 대조해 `reference-source-path`/`candidate-source-path`/`mismatches` field drift 를 발견했고,
writer schema 를 `reference-history-path`/`candidate-path`/`candidate-kind`/summary count/`envelope-mismatches`/signal `code`로 정렬했다.
상세 리뷰는 `docs/agent-state/reviews/2026-06-29-envelope-command-self-review.md`를 본다.
검증은 writer tests 4개, Program tests 2개, envelope 관련 tests 16개, local runner artifact CLI smoke,
`git diff --check`, .NET 9.0.314 MSBuild 기준 solution build, solution tests 378개 통과로 마쳤다.
SDK 선택 재현성 hardening 도 완료했다(D126).
루트 `global.json`이 기본 `dotnet` SDK 선택을 9.0 계열로 고정하고,
stale restore 산출물은 `dotnet restore --ignore-failed-sources`로 현재 사용자 package root 기준으로 재생성했다.
기본 `dotnet --version`은 9.0.314를 반환하고, 기본 `dotnet build HighPerformanceSocket.slnx --no-restore`와
`dotnet test HighPerformanceSocket.slnx --no-build --no-restore`가 통과한다.
CI benchmark workflow 의 report-only envelope comparison artifact 연결도 완료했다(D127).
`benchmark-artifacts.yml`은 summary/history 생성 뒤 repository reference history 가 있을 때
`envelope.json`과 `envelope.md`를 date root 에 생성해 upload artifact 에 포함한다.
reference history 가 없으면 bootstrap 상태로 보고 skip 하며, envelope mismatch/signal 은 계속 CI failure 가 아니다.
D127 이후 다음 후보 재평가도 완료했다(D128).
`.claude/review/2026-06-29-next-scope-decision-review.md`의 local runner 2-date-root 전제는 D123 이후 stale 하며,
RIO full IPv6와 server diagnostics 는 계속 deferred 로 유지한다.
D130 workflow 보강 뒤 사용자 push 로 생성된 run `28350456434`를 검증했다(D131).
이 run 은 remote `master` head SHA `384f3c5932c1a2b22ff92116068bfcda22f56778`와 일치했고,
workflow conclusion 은 success 였다. upload artifact 는 raw report 6개, `summary.json`, `summary.md`,
`history.json`, `history.md`, `envelope.json`, `envelope.md`를 모두 포함했다.
run `28350456434` artifact 는 D095 checklist 를 통과해
`docs/benchmarks/baselines/runners/ci-windows-x64-01/2026-06-29/session-01/`로 수동 채택했다(D131).
runner root history 는 2-session, hard-passed true, warning-count 0, comparison-compatible true 다.
업로드 artifact envelope 는 이전 1-session CI reference 대비 p99 upper-bound signal 2개를 기록했지만,
D125/D127 기준 report-only signal 이므로 CI failure, warning-count, 채택 차단 조건으로 처리하지 않는다.
D131 이후 다음 후보 재평가도 완료했다(D132).
CI gate 승격, RIO default promotion/full IPv6, server-level diagnostics public API는 지금 열지 않고,
다음 후보를 Phase 6 Linux io_uring backend boundary 설계와 첫 구현 계획으로 정했다.
현재 Windows 환경에서는 Linux native integration 을 바로 검증할 수 없으므로,
첫 구현 후보는 `Hps.Transport.IoUring` skeleton, capability probe, non-Linux unsupported boundary,
default SAEA 유지 regression 으로 제한한다.
Phase 6 Linux io_uring boundary 구현 계획도 작성했다.
계획 문서는 `docs/superpowers/plans/2026-06-29-iouring-boundary.md`이며,
Task 1 project skeleton/capability probe, Task 2 `IoUringTransport` lifecycle/unsupported boundary,
Task 3 state docs/full verification 으로 나뉜다.
Task 1 project skeleton/capability probe 를 완료했다.
`Hps.Transport.IoUring` source/test project 를 solution 에 추가했고,
`IoUringCapabilityStatus`와 `IoUringCapabilityProbe.GetStatus()`를 추가했다.
non-Linux 는 `UnsupportedOperatingSystem`, Linux 는 native syscall probe 전까지 `Unavailable`로 반환한다.
Task 2 `IoUringTransport` lifecycle/unsupported boundary 도 완료했다.
`IoUringTransport` root type 은 native 자원을 아직 열지 않는 opt-in shell 이며,
`StartAsync`/`StopAsync` no-op lifecycle 과 TCP listen/connect, UDP bind 의 명시적 unsupported boundary 를 제공한다.
Windows/non-Linux 에서는 `NotSupportedException`으로 수렴하고, `TransportFactory.CreateDefault()`는 계속 SAEA 기준선을 유지한다.
Task 3 state docs/full verification 도 완료했다(D133).
Phase 6 첫 io_uring 구현은 skeleton/probe/unsupported boundary 까지로 제한하고,
native syscall wrapper 와 TCP/UDP pump 는 후속 설계/구현 task 로 분리한다.
최신 검증은 solution build 경고 0/오류 0, solution tests 387개 통과, `git diff --check` 통과다.
Linux io_uring native wrapper shape 설계와 구현 계획도 완료했다(D134).
설계 문서는 `docs/superpowers/specs/2026-06-29-iouring-native-wrapper-shape-design.md`이고,
구현 계획은 `docs/superpowers/plans/2026-06-29-iouring-native-wrapper-shape.md`다.
결정은 `IoUringNative` syscall adapter, `IoUringQueue` fd/mmap owner,
`IoUringRegisteredBufferSet` fixed buffer registration owner 로 나누는 것이다.
native wrapper shape Task 1도 완료했다.
`IoUringNative` internal type 과 platform/architecture guard 를 추가했고,
non-Linux 에서는 `UnsupportedOperatingSystem` 또는 명시적 `NotSupportedException`으로 수렴한다.
최신 검증은 solution build 경고 0/오류 0, solution tests 390개 통과, `git diff --check` 통과다.

## 이번 단위의 검증 경로

다음 cycle 은 Linux io_uring native wrapper shape Task 2 queue setup owner 를 TDD로 구현한다.

- 범위: `docs/superpowers/plans/2026-06-29-iouring-native-wrapper-shape.md` Task 2,
  `IoUringSafeHandle`, `IoUringMemoryMap`, `IoUringQueue`, queue probe result.
- 검증: reflection 기반 Red, focused `IoUringQueueTests` Green, io_uring test project, 필요 시 solution build/test/diff check.
- 현재 상태: io_uring source/test project, capability probe, opt-in transport root type 이 존재한다.
  `IoUringNative` platform guard 는 존재하지만 실제 `io_uring_setup`, SQ/CQ mmap, fixed buffer registration,
  TCP/UDP pump 는 아직 구현하지 않았다.
- 다음 산출물: setup fd 와 SQ/CQ/SQE mmap 수명을 감싸는 queue owner boundary.

## 이번 작업에서 건드리지 않는 범위

- `TransportFactory` 기본 선택 코드 변경
- 별도 selector package 생성
- full IPv6 UDP RIO 지원 구현
- latency hard gate 또는 warning-as-failure 구현
- `BaselineSummaryGenerator` threshold 상수 즉시 변경
- CI artifact 자동 채택, pull_request trigger, schedule trigger
- Linux io_uring backend 구현
- stable identity 인증/권한 검증, persistence, payload replay
