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
- `--baseline-suite`로 closed-loop/open-loop raw JSON artifact 를 반복 수집할 수 있다.
- `--summarize-baseline <input-dir> --summary <output-json> [--summary-md <output-md>]`로 summary JSON과 사람이 읽는 Markdown 보조 artifact 를 생성할 수 있다.
- 2026-06-18 baseline root, `session-02`, `session-03`에는 `summary.json`과 `summary.md`가 모두 생성되어 있다.
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

Benchmark runner identity Task 1~3 구현 검토를 진행한다.

다음 작업은 D079 설계, 구현 계획, `BenchmarkRunIdentity`/raw writer/raw reader/test 를 대조해
Blocker/Major finding 이 있는지 확인하는 review 단위다. 이 검토가 green 이면 다음 후보는 summary/history comparison signal 설계다.

## 이번 단위의 검증 경로

다음 단위는 구현 검토다.

- 범위: D079 설계/계획과 Task 1~3 코드·테스트·상태 문서 정합성.
- 검증: 코드/테스트 대조, `git diff --check`, solution build/test.
- 완료 후 review 결과와 다음 후보를 상태 문서에 기록한다.

## 이번 작업에서 건드리지 않는 범위

- benchmark schema 구현
- CI workflow 또는 warning-as-failure 정책 구현
- latency hard gate 확정
- summary/history comparison signal 구현
- RIO/io_uring backend 구현
- stable identity 인증/권한 검증, persistence, payload replay, diagnostics friendly-name 노출
