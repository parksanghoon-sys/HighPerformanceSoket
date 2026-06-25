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

Phase 4 — 벤치마크 하니스, SAEA 기준선 수치 기록, Interface Server endpoint/send-side 관측성 설계.

## 현재 상태 요약

- Phase 1~3의 핵심 TCP broker 경로는 완료됐다. TCP/UDP transport, protocol, broker, server 통합 경로가 존재한다.
- TCP subscriber outbound 는 D065에 따라 `4-byte big-endian length prefix + payload` frame 으로 전송한다.
- TCP/UDP pending send queue 는 capacity 16 bounded drop-oldest 정책을 유지한다(D064, D067).
- drop 관측은 transport/endpoint snapshot 기반 pull diagnostics 로 유지한다(D062, D066).
- Phase 4 benchmark 는 delivery/drop/leak hard gate 와 latency/HWM 관측값을 분리한다(D063).
- latency hard gate 전에는 반복 baseline artifact 와 summary/soft warning 을 우선한다(D069, D070).
- 문서 전용 작업은 D081 기준으로 관련 설계/상태/결정 문서를 한 coherent documentation cycle 에서 같이 정렬한다.
  코드/테스트 구현 변경은 계속 작은 기능 단위로 분리한다.
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

Summary/history comparison signal 계획의 Task 1~5, benchmark writer metadata roundtrip test-hardening,
2026-06-18 generated baseline artifact 재생성, 2026-06-24 current-schema baseline session-01/session-02/session-03 추가,
D082 설계와 리뷰 보강, Phase 4 다음 후보 재평가, explicit runner 3-session reference 수집,
explicit runner 3-session 이후 다음 후보 재평가, 2026-06-25 explicit runner session-01/session-02/session-03 수집,
explicit runner 2-date-root reference 이후 gate 승격 후보 재평가, CI artifact-only benchmark 정책 설계,
CI artifact-only workflow skeleton 구현 계획, CI artifact-only workflow skeleton 구현,
CI workflow command sequence local smoke, 첫 GitHub Actions manual run 검증, Node 24 action version 갱신,
갱신 후 두 번째 GitHub Actions manual run 검증, manual run 2회 이후 Phase 4 재평가는 완료됐다.

다음 작업은 첫 CI artifact 결과를 기준으로 Phase 4 다음 후보를 재평가하는 것이다.
workflow 는 `.github/workflows/benchmark-artifacts.yml`에 있으며 D090/D091/D092에 따라 latency warning 을 실패로 올리지 않고
artifact upload 만 구성한다. 첫 manual run `28143728630`은 성공했고, artifact 이름은
`benchmark-artifacts-ci-windows-x64-01-2026-06-25-github-28143728630-1`이다. GitHub run id 는 upload artifact 이름에만 넣으며,
업로드 내부 디렉터리는 `artifacts/benchmarks/runners/ci-windows-x64-01/<yyyy-mm-dd>/session-01/` 구조를 유지한다.
benchmark CLI command 는 workflow 앞단 restore/build/test 결과를 재사용하도록 모두 `--no-build --no-restore`로 고정했다.
첫 run summary/history 는 `hard-passed=true`, `comparison-compatible=true`, `unknown-runner-count=0`,
`warning-count=1`이다. warning 은 `open-loop-01.json`의 `p99-growth-ratio-high`이며 D090 기준 report-only 다.
첫 run 의 Node.js 20 deprecation annotation 은 `actions/checkout@v7`, `actions/setup-dotnet@v5.3.0`,
`actions/upload-artifact@v7.0.1`로 갱신해 처리했다. 갱신 후 manual run `28144480160`도 성공했고,
artifact 이름은 `benchmark-artifacts-ci-windows-x64-01-2026-06-25-github-28144480160-1`이다.
로그에서 Node deprecation 또는 이전 `actions/*@v4` 문자열은 확인되지 않았다.
두 번째 run summary/history 는 `hard-passed=true`, `comparison-compatible=true`, `unknown-runner-count=0`,
`warning-count=0`이다. 이 결과도 D090 기준으로 docs baseline 에 자동 채택하지 않고 CI artifact evidence 로만 둔다.

두 번의 CI artifact-only manual run 이후에도 즉시 gate 로 올릴 근거는 아직 부족하다고 판단했다(D093).
CI runner 는 같은 날짜의 artifact-only evidence 만 있고, D082의 latency gate 승격 조건은 여전히 충족하지 않는다.
따라서 latency gate, warning-as-failure, docs baseline 자동 채택, push/PR 자동 trigger 는 승격하지 않는다.

다음 작업은 CI artifact trigger policy 설계다.
자동 실행 event(`workflow_dispatch` 유지, `push` to `master`, `pull_request`, `schedule`, path filter),
실행 비용/노이즈, artifact retention, failure policy, docs baseline 채택 경계를 먼저 정한다.

## 이번 단위의 검증 경로

이번 cycle 은 두 번의 CI artifact-only manual run 결과를 근거로 Phase 4 다음 후보를 재평가하고,
D093으로 gate/trigger 승격 보류와 다음 trigger policy 설계 단위를 기록한다.

- 범위: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`,
  `docs/agent-state/decisions/2026-06.md`,
  `docs/superpowers/specs/2026-06-25-ci-artifact-after-manual-runs-reassessment.md`.
- 검증: run `28143728630`, run `28144480160` log/artifact, D090/D091/D092,
  `docs/benchmarks/baselines/index.md`, current backlog 대조, `git diff --check`.

## 이번 작업에서 건드리지 않는 범위

- 코드/테스트 구현 변경
- 2026-06-18 legacy raw report 수정
- warning-as-failure 정책 구현
- latency hard gate 확정
- RIO/io_uring backend 구현
- stable identity 인증/권한 검증, persistence, payload replay, diagnostics friendly-name 노출
