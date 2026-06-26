# CHANGELOG_AGENT.md

## Archive

긴 변경 이력 원문은 `docs/agent-state/changelog/2026-06.md`에 보존했다.
이 파일은 최근 작업 단위와 현재 진입점에 필요한 내용만 유지한다.

## 2026-06-26 (Codex - host composition transport selection policy)

### 작업 단위
- D119 이후 host/composition transport selection policy 를 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-26-host-composition-transport-selection-policy-design.md`:
  RIO preferred selection 을 base factory 가 아니라 실행 host composition 책임으로 두는 설계를 작성했다.
  첫 적용 대상은 `samples/Hps.Sample.BrokerServer`이고, optional `--transport <saea|rio|auto>` 정책을 정했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D120을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  host/composition selection policy 설계를 완료로 기록하고 다음 실행 지점을 sample broker server transport selector 구현 계획으로 옮겼다.

### 검증
- `BrokerServer`가 injected `ITransport`만 받는 현재 경계를 확인했다.
- `samples/Hps.Sample.BrokerServer`가 현재 `TransportFactory.CreateDefault()`만 사용하고 RIO assembly 를 참조하지 않음을 확인했다.
- benchmark project 의 `--backend saea|rio` explicit selector 선례와 fallback 금지 semantics 를 대조했다.

## 2026-06-26 (Codex - RIO default selection policy)

### 작업 단위
- RIO UDP gate 이후 default selection policy 를 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-26-rio-default-selection-policy-after-udp-design.md`:
  D118 이후에도 base `TransportFactory.CreateDefault()`를 RIO로 바꾸지 않는 이유를 정리했다.
  RIO preferred fallback 정책은 host/composition layer 또는 별도 selector package 에 두고,
  reflection 기반 default RIO loading 은 채택하지 않는다.
- `docs/superpowers/specs/2026-06-25-rio-default-promotion-readiness-design.md`:
  D108 당시 readiness 문서임을 표시하고 최신 판단 문서로 연결했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D119를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  default selection policy 설계를 완료로 기록하고 다음 실행 지점을 host/composition transport selection policy 설계로 옮겼다.

### 검증
- `TransportFactory.CreateDefault()`가 base `Hps.Transport` assembly 안에서 SAEA를 반환하는 현재 구조를 확인했다.
- `RioCapabilityProbe.GetStatus()`가 unsupported/unavailable/available 상태를 명시 반환하는 것을 확인했다.
- benchmark `--backend rio`는 explicit RIO path 이며 unavailable 시 SAEA fallback 으로 오염시키지 않는다는 정책을 유지했다.
- D118 RIO UDP scratch evidence(load/open-loop 3000/3000, p99 831.8/889.4 us)를 default 승격의 성능 근거로만 사용하고,
  assembly dependency/fallback observability 문제는 별도 D119 판단으로 분리했다.

## 2026-06-26 (Codex - RIO UDP bounded receive benchmark)

### 작업 단위
- RIO UDP bounded receive window Task 3 scratch benchmark 와 D118 판단을 수행했다.

### 변경 내용
- `artifacts/benchmarks/rio-udp/2026-06-26/session-04/rio/`:
  RIO UDP `load-01.json`, `open-loop-01.json`, `summary.json`, `summary.md`를 생성했다.
  scratch artifact 이므로 stage 하지 않는다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D118을 추가했다. RIO UDP bounded receive window 는 open-loop delivery hard gate 를 닫은 기준선으로 수락한다.
- `docs/superpowers/plans/2026-06-26-rio-udp-bounded-receive-window.md`,
  `CURRENT_PLAN.md`, `TODOS.md`:
  Task 3 측정 결과와 다음 실행점인 RIO unavailable fallback/default selection policy 설계를 반영했다.

### 검증
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --baseline-suite artifacts\benchmarks\rio-udp\2026-06-26\session-04\rio --runs 1 --protocol udp --backend rio`:
  exit 0, raw report 2개 생성, `baseline-suite-result: pass`.
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --summarize-baseline artifacts\benchmarks\rio-udp\2026-06-26\session-04\rio --summary artifacts\benchmarks\rio-udp\2026-06-26\session-04\rio\summary.json --summary-md artifacts\benchmarks\rio-udp\2026-06-26\session-04\rio\summary.md`:
  exit 0, `hard-passed: true`, `warning-count: 0`, `source-report-count: 2`.
- RIO `session-04/load`: sent/received 3000/3000, dropped 0, payload-errors 0, pool-rented 0,
  actual-rate 99.7 Hz, p50 245.5 us, p99 831.8 us, UDP HWM 1, passed true.
- RIO `session-04/open-loop`: sent/received 3000/3000, dropped 0, payload-errors 0, pool-rented 0,
  actual-rate 100 Hz, p50 250.4 us, p99 889.4 us, UDP HWM 2, passed true.
- `git diff --check`: 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 334개 통과.

## 2026-06-26 (Codex - RIO UDP bounded receive cleanup)

### 작업 단위
- RIO UDP bounded receive window Task 2 close/drain cleanup hardening 을 확인했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-26-rio-udp-bounded-receive-window.md`:
  Task 2를 별도 production 변경 없이 Task 1 slot cleanup 구현과 focused cleanup tests 로 닫았다고 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 2 완료와 다음 Task 3 scratch benchmark/D118 판단 진입점을 반영했다.

### 검증
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~UdpReceive_WhenEndpointClosesWithPrePostedReceive|FullyQualifiedName~UdpReceive_WhenHandlerThrowsWithPrePostedReceive"`:
  2개 통과.

## 2026-06-26 (Codex - RIO UDP bounded receive window Task 1)

### 작업 단위
- RIO UDP bounded receive window Task 1 depth-2 receive behavior 를 TDD로 구현했다.

### 변경 내용
- `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`:
  `UdpReceive_WhenHandlerIsBlocked_PreservesTwoQueuedDatagramsWithBoundedWindow`를 추가했다.
  기존 blocked handler tests 의 rented count 기대값과 주석을 depth 2 receive window 정책에 맞췄다.
- `src/Hps.Transport.Rio/RioUdpEndpoint.cs`:
  request queue receive depth 를 2로 올리고, receive remote address block 을 endpoint shared resource 에서 제거했다.
  receive slot 이 사용할 remote address block 대여/반환 helper 를 추가했다.
- `src/Hps.Transport.Rio/RioTransport.cs`:
  UDP receive loop 를 current/next operation 모델에서 `RioResult.RequestContext` 기반 `RioUdpReceiveSlot[]` 모델로 전환했다.
  각 slot 은 slot-local remote address registered buffer 를 소유하고,
  payload data buffer 는 D113대로 datagram 마다 등록 후 completion 직후 deregister 한다.
- `docs/superpowers/plans/2026-06-26-rio-udp-bounded-receive-window.md`,
  `CURRENT_PLAN.md`, `TODOS.md`:
  Task 1 완료와 다음 Task 2 close/drain cleanup hardening 진입점을 반영했다.

### 검증
- Red: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~UdpReceive_WhenHandlerIsBlocked_PreservesTwoQueuedDatagramsWithBoundedWindow"`가
  기존 one-deep 구현에서 `Expected: 3`, `Actual: 2`로 실패했다.
- Green: 같은 focused test 1개 통과.
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~RioTransportUdpTests"`:
  16개 통과.
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`:
  53개 통과.
- `git diff --check`: 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 334개 통과.

## 2026-06-26 (Codex - RIO UDP bounded receive window design)

### 작업 단위
- RIO UDP open-loop delivery loss 의 receive-side 후속 설계와 구현 계획을 작성했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-26-rio-udp-bounded-receive-window-design.md`:
  D116 이후 남은 delivery loss 를 trace-only, receive payload registration reuse, bounded receive slot window 후보로 비교했다.
  D113 때문에 receive payload registration reuse 는 단독 다음 구현으로 제외하고, request-context 기반 depth 2 receive slot window 를 채택했다.
- `docs/superpowers/plans/2026-06-26-rio-udp-bounded-receive-window.md`:
  Task 1 depth-2 receive behavior, Task 2 close/drain cleanup, Task 3 scratch benchmark/D118 판단으로 구현 단위를 나눴다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D117을 추가했다. RIO UDP open-loop delivery loss 는 receive payload registration reuse 가 아니라 bounded receive slot window 로 먼저 다룬다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  설계/계획 완료와 다음 실행점인 bounded receive window Task 1 Red test 진입점을 반영했다.

### 검증
- D116/D115/D114/D113 decision consistency 를 대조했다.
- 현재 `RioTransport.UdpReceiveLoopAsync(...)`, `RioUdpReceiveOperation`, `RioUdpEndpoint` request queue/remote address ownership 을 확인했다.
- `RioResult.RequestContext` field 가 이미 native result shape 에 있어 slot mapping 에 사용할 수 있음을 확인했다.

## 2026-06-26 (Codex - RIO UDP completion benchmark decision)

### 작업 단위
- RIO UDP completion notification wait Task 3 scratch benchmark 와 D116 판단을 수행했다.

### 변경 내용
- `artifacts/benchmarks/rio-udp/2026-06-26/session-03/rio/`:
  RIO UDP `load-01.json`, `open-loop-01.json`, `summary.json`, `summary.md`를 생성했다.
  scratch artifact 이므로 stage 하지 않는다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D116 partial decision 을 추가했다. UDP IOCP/RIONotify wait 는 16.7ms p99 wake tail 을 해소했지만,
  open-loop delivery loss 는 receive-side 후속으로 남긴다.
- `docs/superpowers/plans/2026-06-26-rio-udp-completion-notification-wait.md`,
  `CURRENT_PLAN.md`, `TODOS.md`:
  Task 3 측정 결과와 다음 실행점인 RIO UDP open-loop delivery loss receive-side 설계를 반영했다.

### 검증
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --baseline-suite artifacts\benchmarks\rio-udp\2026-06-26\session-03\rio --runs 1 --protocol udp --backend rio`:
  exit 1, raw report 2개 생성, `baseline-suite-result: fail`.
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --summarize-baseline artifacts\benchmarks\rio-udp\2026-06-26\session-03\rio --summary artifacts\benchmarks\rio-udp\2026-06-26\session-03\rio\summary.json --summary-md artifacts\benchmarks\rio-udp\2026-06-26\session-03\rio\summary.md`:
  exit 1, `hard-passed: false`, `warning-count: 1`, `source-report-count: 2`.
- RIO `session-03/load`: sent/received 3000/3000, dropped 0, payload-errors 0, pool-rented 0,
  actual-rate 99.8 Hz, p50 201.2 us, p99 481 us, UDP HWM 1, passed true.
- RIO `session-03/open-loop`: sent/received 3000/2373, dropped 0, payload-errors 0, pool-rented 0,
  actual-rate 85.7 Hz, p50 229.1 us, p99 647.6 us, UDP HWM 2, passed false.
- 비교: RIO `session-02/open-loop`은 sent/received 3000/2409, p99 16709.1 us였고,
  SAEA `session-01/open-loop`은 sent/received 3000/3000, p99 852.2 us였다.
- `git diff --check`: 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 333개 통과.

## 2026-06-26 (Codex - RIO UDP completion notification wait)

### 작업 단위
- RIO UDP completion notification wait Task 2 wait path 전환을 TDD로 구현했다.

### 변경 내용
- `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`:
  `RioUdpEndpoint_WhenNotificationWaitIsExpected_ExposesArmNotificationHelper`를 추가했다.
  UDP wait path 가 TCP RIO처럼 notification arm helper 를 갖는지 고정한다.
- `src/Hps.Transport.Rio/RioUdpEndpoint.cs`:
  `ArmNotification(...)`을 추가해 CQ drain 과 같은 lock 에서 `RIONotify` arm 을 직렬화한다.
  `WSAEALREADY`는 TCP RIO 경로와 같은 benign race 로 처리한다.
- `src/Hps.Transport.Rio/RioTransport.cs`:
  UDP receive/send wait 호출부가 각각 `ReceiveSignal`/`SendSignal`을 넘기고,
  `WaitForUdpCompletionAsync(...)`가 open 상태에서 `Task.Delay(1)` polling 대신 signal wait 를 사용한다.
  close-drain fallback 은 owner cleanup 을 위해 제한적으로 유지한다.
- `docs/superpowers/plans/2026-06-26-rio-udp-completion-notification-wait.md`,
  `CURRENT_PLAN.md`, `TODOS.md`:
  Task 2 완료와 다음 Task 3 scratch benchmark/D116 판단 진입점을 반영했다.

### 검증
- Red: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~RioUdpEndpoint_WhenNotificationWaitIsExpected_ExposesArmNotificationHelper"`가
  기존 endpoint 에서 `Assert.NotNull()` failure 로 실패했다.
- Green: 같은 focused test 1개 통과.
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~RioTransportUdpTests"`:
  15개 통과.
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`:
  52개 통과.
- `git diff --check`: 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 333개 통과.

## 2026-06-26 (Codex - RIO UDP completion signal shape)

### 작업 단위
- RIO UDP completion notification wait Task 1 endpoint signal resource shape 를 TDD로 구현했다.

### 변경 내용
- `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`:
  `BindUdpAsync_WhenRioDatagramAvailable_CreatesUdpCompletionSignals`를 추가했다.
  endpoint 가 receive/send `RioCompletionSignal` resource 를 갖는지 먼저 고정한다.
- `src/Hps.Transport.Rio/RioUdpEndpoint.cs`:
  receive/send `RioCompletionSignal`을 소유하고, UDP receive/send CQ를 notification completion pointer 로 생성한다.
  receive/send drain 에서 각 signal 을 dispose 한다.
- `src/Hps.Transport.Rio/RioTransport.cs`:
  `BindUdpAsync(...)`가 TCP RIO와 같은 shared `RioCompletionPort`를 UDP endpoint 에 넘긴다.
- `docs/superpowers/plans/2026-06-26-rio-udp-completion-notification-wait.md`:
  새 테스트 Red 실행에는 새 테스트 컴파일이 필요하므로 Task 1 Red command 에서 `--no-build`를 제거했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 1 완료와 다음 Task 2 wait path 전환 진입점을 반영했다.

### 검증
- Red: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~BindUdpAsync_WhenRioDatagramAvailable_CreatesUdpCompletionSignals"`가
  기존 endpoint 에서 `Assert.NotNull()` failure 로 실패했다.
- Green: 같은 focused test 1개 통과.
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~RioTransportUdpTests"`:
  14개 통과.
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`:
  51개 통과.

## 2026-06-26 (Codex - RIO UDP completion notification wait plan)

### 작업 단위
- D115 설계를 Red-Green 가능한 구현 계획으로 분리했다.
- 구현은 아직 하지 않고, 다음 Task 1 Red test 진입점까지 정리했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-26-rio-udp-completion-notification-wait.md`:
  endpoint signal resource shape, UDP wait notification 전환, scratch benchmark/D116 판단의 3개 task 로 나눴다.
  각 task 에 Red 기대 실패, Green 구현 shape, 검증 명령, 커밋 범위를 명시했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  구현 계획 완료와 다음 Task 1 endpoint signal shape 구현 진입점을 반영했다.

### 검증
- D115 설계 coverage self-review 를 수행했다.
- TCP RIO `RioConnectionResource`의 completion signal/CQ notification pointer/`RIONotify` wait pattern 과 계획을 대조했다.
- 계획 문서 placeholder scan 으로 신규 미정 항목이 없는지 확인한다.

## 2026-06-26 (Codex - RIO UDP open-loop residual loss/tail design)

### 작업 단위
- D114 이후 남은 RIO UDP open-loop delivery loss 와 p99 tail 을 source/benchmark evidence 로 재평가했다.
- 구현은 하지 않고, 다음 구현 후보를 결정 문서와 설계 문서로 좁혔다.

### 변경 내용
- `docs/superpowers/specs/2026-06-26-rio-udp-open-loop-residual-loss-tail-design.md`:
  RIO UDP `session-02/rio`와 SAEA UDP `session-01/saea` scratch 결과를 비교하고,
  receive depth 확대, UDP IOCP/RIONotify wait, receive registration reuse 후보를 평가했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D115를 추가했다. 다음 구현 후보는 receive depth 확대가 아니라 UDP CQ completion wait 를
  TCP RIO와 같은 IOCP/RIONotify pattern 으로 맞추는 것이다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  residual loss/tail 설계를 완료로 기록하고, 다음 실행 지점을 D115 구현 계획 작성으로 옮겼다.

### 검증
- RIO UDP `session-02/rio`: closed-loop sent/received 3000/3000, p99 16719.2 us, open-loop sent 3000 / received 2409,
  p99 16709.1 us, elapsed 35003ms, hard-passed false.
- SAEA UDP `session-01/saea`: closed-loop sent/received 3000/3000, p99 814.2 us, open-loop sent/received 3000/3000,
  p99 852.2 us, hard-passed true.
- `src/Hps.Transport.Rio/RioTransport.cs`: UDP wait 는 bounded `Task.Yield()` 이후 `Task.Delay(1)` fallback 을 사용한다.
- `src/Hps.Transport.Rio/RioTransport.cs`: TCP RIO wait 는 CQ notification pointer, `RIONotify`, IOCP signal wait 를 사용한다.

## 2026-06-26 (Codex - RIO UDP receive window benchmark decision)

### 작업 단위
- RIO UDP receive window hardening Task 2를 수행했다.
- Task 1 one-deep pre-post 구현 뒤 RIO UDP scratch benchmark 를 재수집하고, D114 결정으로 현재 receive policy 를 문서화했다.

### 변경 내용
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D114를 추가했다. RIO UDP receive window 는 close-safe one-deep pre-post 로 전환하고,
  `Close()`는 shutdown requester 로 제한하며 receive native resource 는 receive loop drain 뒤 닫는다.
  D111 no-prefetch receive window 정책은 superseded 로 표시했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 2 완료와 benchmark evidence 를 반영하고, 다음 실행 지점을 RIO UDP open-loop residual loss/tail 재평가 설계로 옮겼다.
- ignored scratch `artifacts/benchmarks/rio-udp/2026-06-26/session-02/rio/`:
  RIO UDP raw report 2개와 summary JSON/Markdown을 생성했다. repository baseline 으로 채택하지 않는다.

### 검증
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --baseline-suite artifacts\benchmarks\rio-udp\2026-06-26\session-02\rio --runs 1 --protocol udp --backend rio`:
  raw report 2개 생성, baseline-suite-result fail, exit code 1.
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --summarize-baseline artifacts\benchmarks\rio-udp\2026-06-26\session-02\rio --summary artifacts\benchmarks\rio-udp\2026-06-26\session-02\rio\summary.json --summary-md artifacts\benchmarks\rio-udp\2026-06-26\session-02\rio\summary.md`:
  source-report-count 2, hard-passed false, warning-count 3, exit code 1.
- load raw report: sent/received 3000/3000, dropped 0, payload-errors 0, pool-rented 0, actual-rate 99.7 Hz,
  p50 172.2 us, p99 16719.2 us, passed true.
- open-loop raw report: sent 3000 / received 2409, dropped 0, payload-errors 0, pool-rented 0,
  actual-rate 85.7 Hz, p50 378.4 us, p99 16709.1 us, passed false.
- summary warnings: `load-p99-latency-high`, `open-loop-p99-latency-high`, `actual-rate-low`.

## 2026-06-26 (Codex - RIO UDP receive one-deep prepost)

### 작업 단위
- RIO UDP receive loop 를 D111 no-prefetch 에서 close-safe one-deep pre-post 로 전환했다.
- endpoint close/resource owner 를 분리해 `Close()`는 shutdown request 만 수행하고, receive/send native resource 는 각 pump drain 이후 정리한다.

### 변경 내용
- `src/Hps.Transport.Rio/RioTransport.cs`:
  `RioUdpReceiveOperation` owner 를 추가하고, handler dispatch 전에 다음 `RIOReceiveEx`를 하나 pre-post 한다.
  handler exception, socket/native failure, endpoint close 경로에서 current/next receive operation 을 receive loop cleanup 으로 수렴시킨다.
  UDP send loop 는 종료 시 `CompleteSendDrain()`을 호출한다.
- `src/Hps.Transport.Rio/RioUdpEndpoint.cs`:
  `RequestClose()`, `CompleteReceiveDrain()`, `CompleteSendDrain()`을 분리했다.
  detached endpoint 는 pump 가 없으므로 public `Close()`에서 즉시 receive/send drain 을 완료하고,
  bound endpoint 는 receive/send pump 가 drain 을 마친 뒤 CQ/address/payload cache/signal resource 를 정리한다.
- `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`:
  기존 no-prefetch 테스트를 one-deep pre-post 기대 테스트로 교체하고,
  close 중 pre-post 된 receive cleanup, handler exception 중 pre-post 된 receive cleanup 을 검증하는 테스트를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 1 완료와 다음 Task 2 benchmark/D114 문서화 진입점을 반영했다.

### 검증
- Red: focused one-deep receive/close 테스트 2개가 기존 no-prefetch 구현에서 `Expected: 2, Actual: 1`로 실패했다.
- Green: focused one-deep tests 3개 통과.
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~RioTransportUdpTests"`: 13개 통과.
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 50개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 331개 통과.

## 2026-06-26 (Codex - RIO UDP receive window implementation plan)

### 작업 단위
- 리뷰 반영된 `docs/superpowers/specs/2026-06-26-rio-udp-receive-window-hardening-design.md`를 구현 가능한 계획으로 분리했다.
- close-drain blocker 때문에 one-deep receive loop 와 endpoint resource split 을 같은 Task 1 구현 단위로 묶었다.

### 변경 내용
- `docs/superpowers/plans/2026-06-26-rio-udp-receive-window-hardening.md`:
  Task 1 close-safe one-deep receive loop 구현과 Task 2 benchmark/D114 문서화 절차를 작성했다.
  각 Task 에 Red 테스트, expected failure, Green 구현 shape, 검증 명령, 커밋 범위를 명시했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  현재 진입점을 계획 작성 완료에서 Task 1 Red 테스트 작성으로 갱신했다.

### 검증
- 설계 리뷰 B1~B5, D111/D113, RIO UDP scratch evidence, 기존 `RioTransportUdpTests` helper 구조와 계획을 대조했다.
- 계획 문서 placeholder scan 과 `git diff --check`로 문서 변경을 검증했다.

## 2026-06-26 (Codex - RIO UDP receive window design review alignment)

### 작업 단위
- `.claude/review/2026-06-26-rio-udp-one-deep-prepost-design-review.md`의 설계 리뷰를 검토하고,
  RIO UDP one-deep pre-post 설계의 close/resource ownership 블로커를 문서에 반영했다.
- 코드와 테스트 구현은 아직 시작하지 않고, 구현 계획 작성 전제만 정렬했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-26-rio-udp-receive-window-hardening-design.md`:
  `Close()`를 shutdown requester 로 제한하고, receive CQ/address registration 은 receive loop drain 이후 닫는 순서로 명시했다.
  receive operation resource 는 receive loop 단일 소유로 두고, handler exception 중 이미 pre-post 된 next operation cleanup 도
  같은 경로로 수렴시킨다.
  remote address block 은 endpoint lifetime shared block 으로 유지하되 decode-before-next-post 불변식으로 안전성을 설명했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  현재 진입점을 설계 리뷰 대기에서 리뷰 반영된 설계 기준 구현 계획 작성으로 옮겼다.

### 검증
- 설계 리뷰 B1~B5와 보정된 스펙 항목을 대조했다.
- D111 no-prefetch, D113 receive registration 보정, D114 예정 supersede 조건이 서로 충돌하지 않는지 확인했다.
- `git diff --check`로 문서 whitespace 를 검증했다.

## 2026-06-26 (Codex - RIO UDP benchmark scratch evidence)

### 작업 단위
- RIO/SAEA UDP benchmark scratch artifact 를 수집하고, 수집 중 발견된 RIO UDP receive/fan-out 경계 버그를 보정했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioTransport.cs`:
  `RIOReceiveEx` completion 후 handler dispatch 전에 receive buffer registration 을 해제한다.
  UDP handler 가 받은 datagram 을 즉시 fan-out send queue 로 넘길 때 같은 backing byte[]가 receive/send native registration 에 겹치지 않게 한다.
- `src/Hps.Transport.Rio/RioUdpEndpoint.cs`:
  UDP receive block 을 4096B에서 SAEA 기준선과 같은 8192B로 올렸다.
  D112 UDP publish datagram 은 command envelope + 4096B payload 이므로 4096B block 으로는 benchmark target 을 담을 수 없다.
- `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`:
  two-remote fan-out slice, 4096B 초과 datagram receive coverage 를 추가했다.
- `tests/Hps.Benchmarks/UdpLoopbackScenarioRunner.cs`,
  `tests/Hps.Benchmarks.Tests/UdpLoopbackScenarioRunnerTests.cs`:
  closed-loop timeout 도 failed raw report 로 남기고, open-loop sequence gap 을 payload corruption 과 분리한다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, `CURRENT_PLAN.md`, `TODOS.md`:
  D113과 scratch evidence, 다음 RIO UDP receive window hardening 설계 진입점을 기록했다.
- `docs/superpowers/specs/2026-06-26-rio-udp-receive-window-hardening-design.md`:
  RIO UDP no-prefetch 유지, one-deep pre-post, bounded outstanding receive queue 를 비교하고
  one-deep pre-post 를 첫 구현 후보로 제안했다.

### 검증
- Red: `--smoke --protocol udp --backend rio`가 timeout.
- Red: `UdpSendTo_WhenSecondRemoteTriggersSendToFirstRemote_FirstRemoteReceivesSlice`가 first remote receive timeout.
- Red: `UdpReceive_WhenDatagramExceedsPayloadSizeButFitsBaselineEnvelope_DeliversFullDatagram`이 receive 대신 endpoint close 로 실패.
- Red: `PayloadMatchesSequencePattern_WhenSequenceGapExistsButBytesMatch_ReturnsTrue`가 false 반환으로 실패.
- Green: focused RIO UDP fan-out/large datagram tests 2개 통과.
- Green: focused `UdpLoopbackScenarioRunnerTests` 3개 통과.
- 실제 CLI: `--smoke --protocol udp --backend rio` pass.
- Scratch artifact:
  `artifacts/benchmarks/rio-udp/2026-06-26/session-01/saea` summary 는 hard-passed true, warning 0.
  `artifacts/benchmarks/rio-udp/2026-06-26/session-01/rio` summary 는 hard-passed false, warning 3,
  open-loop sent/received 3000/2263, payload-errors 0.
- 설계 문서 placeholder scan 통과.

## 2026-06-26 (Codex - RIO UDP benchmark load runners)

### 작업 단위
- RIO UDP benchmark load/open-loop/baseline-suite 를 구현했다.

### 변경 내용
- `tests/Hps.Benchmarks/UdpLoopbackScenarioRunner.cs`:
  smoke/load/open-loop 를 하나의 scenario core 로 일반화했다.
  closed-loop 는 publish 뒤 receive 를 기다리고, open-loop 는 receive task 와 publish schedule 을 분리한다.
  UDP open-loop 에서 timeout/drop 이 생기면 runner 예외가 아니라 failed raw report 로 남기도록 했다.
- `tests/Hps.Benchmarks/Program.cs`:
  `--load --protocol udp`, `--load-open-loop --protocol udp`,
  `--baseline-suite ... --protocol udp`를 UDP runner 로 dispatch 한다.
- `tests/Hps.Benchmarks.Tests/UdpLoopbackScenarioRunnerTests.cs`:
  30초 CLI workload 를 unit test 에 넣지 않고 작은 message count 로 closed-loop/open-loop result shape 를 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  load/open-loop 구현 완료와 다음 RIO/SAEA UDP scratch artifact 수집 진입점을 기록했다.

### 검증
- Red: focused `UdpLoopbackScenarioRunnerTests` 2개가 기존 private test entry point 부재로 `Assert.NotNull()` 실패.
- Green: focused UDP runner tests 2개 통과.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore`: 78개 통과.
- 실제 CLI: `--load --protocol udp --backend saea --report <temp>` pass,
  scenario `udp-loopback-saea-baseline`, profile `udp-loopback-saea-v1`,
  sent/received 3000/3000, dropped 0, pool-rented 0 확인.
- 실제 CLI: `--load-open-loop --protocol udp --backend saea --report <temp>` pass,
  scenario `udp-loopback-saea-baseline-open-loop`, sent/received 3000/3000,
  dropped 0, pool-rented 0 확인.
- 실제 CLI: `--baseline-suite <temp> --runs 1 --protocol udp --backend saea` pass,
  `load-01.json`, `open-loop-01.json` 생성 확인.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build`: 325개 통과.

## 2026-06-26 (Codex - RIO UDP benchmark smoke runner)

### 작업 단위
- RIO UDP benchmark Task 2/3 UDP loopback runner dispatch 와 SAEA UDP smoke 를 구현했다.

### 변경 내용
- `tests/Hps.Benchmarks/UdpLoopbackScenarioRunner.cs`:
  `BrokerServer.StartUdpAsync(...)` 기반 UDP `SUBSCRIBE`/`PUBLISH` smoke loopback runner 를 추가했다.
  subscriber outbound 는 TCP frame 이 아니라 raw payload datagram 으로 수신해 기존 payload layout 을 검증한다.
- `tests/Hps.Benchmarks/Program.cs`:
  `--smoke --protocol udp`를 UDP runner 로 dispatch 한다.
  UDP load/open-loop/baseline-suite 는 다음 단위 전까지 계속 실패 처리한다.
- `tests/Hps.Benchmarks/BenchmarkRunIdentity.cs`:
  UDP SAEA/RIO benchmark profile helper 를 추가해 raw report 가 `udp-loopback-...` profile 을 기록하게 했다.
- `tests/Hps.Benchmarks.Tests/BenchmarkProgramProtocolTests.cs`:
  SAEA UDP smoke CLI가 raw report 를 쓰고 UDP scenario/profile/backend/delivery/drop/leak field 를 보존하는지 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  UDP smoke 완료와 다음 load/open-loop/baseline-suite 구현 진입점을 기록했다.

### 검증
- Red: Program protocol test 가 기존 guard 때문에 exit code 1로 실패.
- Green: focused Program protocol test 1개 통과.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore`: 76개 통과.
- 실제 CLI: `--smoke --protocol udp --backend saea --report <temp>` pass,
  scenario `udp-loopback-saea-baseline-smoke`, profile `udp-loopback-saea-v1`,
  sent/received 8/8, dropped 0, pool-rented 0 확인.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build`: 323개 통과.

## 2026-06-26 (Codex - RIO UDP benchmark protocol selector)

### 작업 단위
- RIO UDP benchmark Task 1 protocol selector model/parser 를 구현했다.

### 변경 내용
- `tests/Hps.Benchmarks/LoopbackProtocol.cs`:
  benchmark runner protocol selector enum 을 추가했다. 기본은 TCP이고 UDP는 D112 artifact 경로의 명시 선택값이다.
- `tests/Hps.Benchmarks/BenchmarkCommandLine.cs`, `BenchmarkCommandParser.cs`:
  runner/baseline-suite command 에서 `--protocol <tcp|udp>`를 파싱해 보존한다.
  summary/history/help/target 또는 runner 없는 위치에서는 `--protocol`을 usage error 로 막는다.
- `tests/Hps.Benchmarks/Program.cs`:
  UDP runner 연결 전까지 `--protocol udp` 실행은 실패 처리해 TCP smoke report 가 UDP evidence 로 잘못 저장되지 않게 했다.
- `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`, `BenchmarkProgramProtocolTests.cs`:
  protocol selector parsing, aggregate command 차단, invalid protocol error, Program guard 를 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 1 완료와 다음 UDP loopback runner/SAEA smoke 구현 진입점을 기록했다.

### 검증
- Red: focused parser tests 4개가 `--protocol` 미인식/invalid protocol 메시지 부재로 실패.
- Red: Program guard test 가 `--smoke --protocol udp --report ...` exit code 0으로 실패.
- Green: focused parser tests 22개 통과, Program guard test 1개 통과.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore`: 76개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build`: 323개 통과.

## 2026-06-26 (Codex - RIO UDP benchmark artifact design)

### 작업 단위
- RIO UDP benchmark artifact 수집 범위와 command shape 를 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-26-rio-udp-benchmark-artifact-design.md`:
  기존 benchmark runner 명령에 `--protocol <tcp|udp>` selector 를 추가하고,
  UDP closed-loop/open-loop artifact 를 기존 raw report schema 로 수집하는 설계를 작성했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D112를 추가했다. UDP report 는 새 schema field 없이 `benchmark-profile`/`scenario` 값으로 TCP와 구분한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  설계 완료를 기록하고 다음 실행 지점을 protocol selector model/parser 구현으로 이동했다.

### 검증
- benchmark CLI/result/schema source 를 대조했다.
- 설계 문서 placeholder scan 통과.
- `git diff --check`: 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build`: 318개 통과.

## 2026-06-26 (Codex - RIO UDP contract matrix)

### 작업 단위
- RIO/SAEA backend contract matrix 를 RIO UDP edge tests 로 보강했다.

### 변경 내용
- `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`:
  handler exception close notify, no-prefetch/pool ownership, endpoint close-drain,
  drop-oldest release/diagnostics/high-watermark 테스트를 추가했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D111을 추가했다. RIO UDP no-prefetch 는 pool ownership/backpressure 경계이며,
  handler blocked-window datagram retention 을 보장하는 계약은 아니라고 정리했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  contract matrix 보강 완료를 기록하고, 다음 실행 지점을 RIO UDP benchmark artifact 설계로 이동했다.
  bounded receive prefetch 는 UDP benchmark evidence 이후 별도 설계 후보로 deferred 했다.

### 검증
- Red: 최초 `UdpReceive_WhenHandlerIsBlocked_DoesNotPrefetchAdditionalDatagrams`는
  blocked handler 중 보낸 두 번째 datagram 을 unblock 뒤 보장 수신한다고 기대해 timeout 으로 실패.
- Green: D111 기준으로 no-prefetch 테스트를 pool 대여 미증가와 unblock 이후 loop 생존 검증으로 보정.
- focused `RioTransportUdpTests` 8개 통과.
- focused RIO tests 45개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build`: 318개 통과.

## 2026-06-26 (Codex - RIO UDP default readiness review)

### 작업 단위
- RIO UDP parity 이후 default backend 승격 가능성을 재검토했다.

### 변경 내용
- `docs/agent-state/reviews/2026-06-26-rio-udp-parity-default-readiness-review.md`:
  D109 RIO UDP 구현 이후에도 default backend 승격을 보류해야 하는 근거와 material failure mode 를 기록했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D110을 추가했다. RIO UDP parity 이후에도 `TransportFactory.CreateDefault()`는 계속 `SaeaTransport`를 반환하고,
  다음 작업은 RIO/SAEA backend contract matrix 보강으로 둔다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  현재 실행 지점을 RIO/SAEA backend contract matrix 보강으로 이동하고,
  RIO UDP benchmark artifact, fallback/default selection policy, IPv6 지원 판단을 deferred backlog 로 분리했다.

### 검증
- source/test/decision matrix 를 대조했다.
- `git diff --check`: 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build`: 314개 통과.
- 문서 전용 변경이므로 프로덕션 코드와 테스트 코드는 수정하지 않았다.

## 2026-06-26 (Codex - RIO UDP diagnostics parity)

### 작업 단위
- RIO UDP Task 5 diagnostics parity 를 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioTransport.cs`:
  `ITransportEndpointDiagnostics`를 구현하고 TCP/RIO UDP endpoint snapshot 을 집계한다.
- `src/Hps.Transport.Rio/RioUdpEndpoint.cs`:
  SAEA UDP와 같은 endpoint id, state, pending send count, high-watermark, dropped pending send count snapshot 을 만든다.
- `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`:
  bind 된 RIO UDP endpoint 가 open UDP snapshot 으로 노출되는지 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 RIO UDP backend self-review/default promotion readiness 재평가로 이동했다.

### 검증
- Red: `GetEndpointSnapshots_WhenUdpEndpointIsOpen_ReturnsUdpSnapshot`가 `ITransportEndpointDiagnostics` assignability failure 로 실패.
- focused diagnostics test 통과.
- focused RIO tests 41개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build`: 통과.

## 2026-06-26 (Codex - RIO UDP send loop)

### 작업 단위
- RIO UDP Task 4 send loop 를 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioTransport.cs`:
  `TrySendTo(...)`, UDP send pump, `RIOSendEx` post/completion wait, IPv4 `SOCKADDR_INET` encode 를 추가했다.
- `src/Hps.Transport.Rio/RioUdpEndpoint.cs`:
  endpoint-local bounded pending send queue/drop-oldest, send address registered buffer,
  payload registration cache lease owner 를 추가했다.
- `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`:
  RIO UDP echo loopback 테스트를 추가해 handler 가 `TrySendTo(...)`로 queue 한 datagram 이 raw UDP client 로 돌아오는지 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 RIO UDP diagnostics parity 로 이동했다.

### 검증
- Red: `UdpEcho_WhenDatagramHandlerQueuesResponse_ClientReceivesSamePayload`가 client receive timeout 으로 실패.
- focused UDP echo test 통과.
- focused RIO tests 40개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build`: 통과.

## 2026-06-26 (Codex - RIO UDP receive loop)

### 작업 단위
- RIO UDP Task 3 receive loop 를 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioTransport.cs`:
  `BindUdpAsync(...)` 이후 RIO UDP receive pump 를 시작하고,
  `RIOReceiveEx` completion 을 기다린 뒤 remote `SOCKADDR_INET`을 `EndPoint`로 decode 해 datagram handler 에 전달한다.
  첫 receive post 는 bind 반환 전에 수행하고, UDP v1 completion wait 는 bounded dequeue polling 으로 둔다.
- `src/Hps.Transport.Rio/RioUdpEndpoint.cs`:
  UDP 전용 RQ/CQ, remote address registered buffer, receive pool, completion dequeue resource owner 를 추가했다.
- `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`:
  raw UDP client datagram 이 RIO endpoint handler 에 owned `RefCountedBuffer`로 도착하는 loopback 테스트를 추가했다.
- `tests/Hps.Transport.Rio.Tests/Properties/AssemblyInfo.cs`:
  RIO native integration tests 가 같은 provider/CQ 자원을 공유하므로 test collection parallelization 을 비활성화했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 RIO UDP send loop 로 이동했다.

### 검증
- Red: `UdpReceive_WhenRawClientSendsDatagram_DeliversOwnedRefCountedBuffer`가 기존 skeleton 에서 5초 timeout 으로 실패.
- focused UDP receive test 통과.
- focused RIO tests 39개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build -m:1`: 통과.
- `dotnet test HighPerformanceSocket.slnx --no-build`: 통과.

## 2026-06-25 (Codex - RIO UDP endpoint skeleton)

### 작업 단위
- RIO UDP Task 2 endpoint owner skeleton 을 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioNative.cs`:
  `WSA_FLAG_REGISTERED_IO` UDP socket 생성 helper 를 추가했다.
- `src/Hps.Transport.Rio/RioTransport.cs`:
  `BindUdpAsync(...)`가 RIO datagram capability 를 확인하고, UDP socket bind 후 endpoint 를 tracking 한다.
- `src/Hps.Transport.Rio/RioUdpEndpoint.cs`:
  bind 된 UDP socket 의 close/unregister owner 를 추가했다.
- `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`:
  RIO datagram available 환경에서 bind 된 endpoint 가 local endpoint 를 노출하는지 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 RIO UDP receive loop 설계/Red test 로 이동했다.

### 검증
- Red: 신규 UDP bind test 가 기존 `TransportBase.BindUdpAsync`의 `NotImplementedException`으로 실패.
- Green: focused UDP test 1개 통과.
- focused RIO tests 38개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build`: 통과.

## 2026-06-25 (Codex - RIO UDP native Ex shape)

### 작업 단위
- RIO UDP Task 1 native Ex operation shape 를 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioNative.cs`:
  `SupportsDatagramOperations`, `ReceiveEx`, `SendEx`, optional `RioBufferSegment` pinning helper 를 추가했다.
  control context, flags buffer, RIO flags 는 초기 UDP parity 범위에서 null/0 으로 고정한다.
- `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`:
  datagram capability property, Ex wrapper method shape, null request queue validation 을 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 RIO UDP Task 2 endpoint owner skeleton 으로 이동했다.

### 검증
- Red: focused tests 2개가 property/method 부재로 `Assert.NotNull()` 실패.
- Green: focused Ex tests 3개 통과.
- focused RIO tests 37개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build`: 통과.

## 2026-06-25 (Codex - RIO UDP native Ex plan)

### 작업 단위
- RIO UDP Task 1 native Ex operation shape 구현 계획을 작성했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-25-rio-udp-native-ex-operation-shape.md`:
  `RioNative`의 `ReceiveEx`/`SendEx` wrapper, `SupportsDatagramOperations`,
  nullable `RIO_BUF` marshalling, Red/Green 검증 경로를 계획했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 RIO UDP Task 1 Red tests 작성으로 이동했다.

### 검증
- D109 설계 coverage 를 대조했다.
- placeholder scan: 매칭 없음.
- `git diff --check`: whitespace error 없음.

## 2026-06-25 (Codex - RIO UDP backend boundary)

### 작업 단위
- RIO UDP backend boundary 를 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-25-rio-udp-backend-boundary-design.md`:
  RIO UDP native operation shape, UDP endpoint owner, receive/send buffer lifetime,
  backpressure/diagnostics parity, 구현 순서를 정리했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D109를 추가했다. RIO UDP는 TCP resource 를 재사용하지 않고 UDP endpoint owner 로 설계한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 RIO UDP Task 1 native Ex operation shape 구현 계획으로 이동했다.

### 검증
- SAEA UDP endpoint/handler 계약, RIO native function table shape, Microsoft Learn `RIOSendEx`/`RIOReceiveEx` 문서를 대조했다.
- `git diff --check`: whitespace error 없음.

## 2026-06-25 (Codex - RIO default promotion readiness)

### 작업 단위
- RIO backend default promotion readiness 를 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-25-rio-default-promotion-readiness-design.md`:
  RIO default 승격 조건을 기능 parity, fallback, contract matrix, benchmark evidence, 운영/문서 gate 로 정리했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D108을 추가했다. 현재 RIO는 TCP opt-in path 만 구현했으므로 기본 backend 로 승격하지 않는다.
- `src/Hps.Transport/Runtime/TransportFactory.cs`:
  오래된 Phase 2 factory XML doc 을 D108 opt-in 정책에 맞게 갱신했다. behavior 는 계속 SAEA default 다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 RIO UDP backend boundary 설계로 이동했다.

### 검증
- factory 현재 behavior, RIO capability/benchmark opt-in path, RIO TCP tests 와 SAEA UDP/Broker coverage 를 대조했다.
- `git diff --check`: whitespace error 없음.

## 2026-06-25 (Codex - RIO payload cache self-review)

### 작업 단위
- RIO payload cache 구현 self-review 를 완료했다.

### 변경 내용
- `docs/agent-state/reviews/2026-06-25-rio-payload-cache-self-review.md`:
  D107 구현과 source/test/spec 를 대조한 self-review 결과를 기록했다.
- `src/Hps.Transport.Rio/RioPayloadRegistrationCache.cs`:
  idle eviction 의 정상 경로에서 native deregister 를 cache lock 밖으로 이동했다.
  새 registration 실패 경로에서는 이미 제거한 idle registration 이 누수되지 않도록 예외 정리를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 RIO backend default promotion readiness 설계로 이동했다.

### 검증
- focused cache owner tests 4개 통과.
- focused RIO tests 34개 통과.
- common close/wake/pending tests 19개 통과.
- RIO close/handler close tests 2개를 10회 반복 실행해 모두 통과.
- `git diff --check`: whitespace error 없음.

## 2026-06-25 (Codex - RIO payload registration cache wiring)

### 작업 단위
- RIO payload registration cache Task 2/3 send path cache lease 와 검증을 완료했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioTransport.cs`:
  `RioConnectionResource`가 connection-local `RioPayloadRegistrationCache`를 소유하고,
  payload send path 가 backing `byte[]` cache lease 로 `SendRegisteredBufferAsync(...)`를 호출한다.
  기존 per-operation `SendRegisteredArrayAsync(...)` helper 는 제거했다.
- `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`:
  같은 backing payload block 을 같은 RIO connection 으로 두 번 보낼 때 payload registration 이 한 번만 발생하는지 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 RIO payload cache 구현 self-review 로 이동했다.

### 검증/관측
- Red: 신규 payload loopback test 가 기존 구현에서 `Expected: 1, Actual: 2` registration count 로 실패.
- Green: focused payload reuse test 통과, registration reuse tests 3개 통과, focused RIO tests 34개 통과.
- close/wake 핵심 RIO tests 10회 반복 통과.
- `dotnet test HighPerformanceSocket.slnx --no-restore`: 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
  최초 build/test 병렬 실행에서는 `obj` 파일 잠금 경합으로 build만 실패했고, 단독 build 재실행으로 정상 확인했다.
- benchmark session-06:
  RIO load actual-rate 99.8 Hz, p50 288.4 us, p99 906.9 us.
  RIO open-loop actual-rate 99.8 Hz, p50 293.8 us, p99 920.5 us.

## 2026-06-25 (Codex - RIO payload registration cache owner)

### 작업 단위
- RIO payload registration cache Task 1 pure owner 를 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioPayloadRegistrationCache.cs`:
  backing `byte[]` object identity 기반 cache, outstanding lease count, idle LRU eviction,
  dispose-delayed deregister, all-outstanding capacity fallback lease 를 추가했다.
- `tests/Hps.Transport.Rio.Tests/RioPayloadRegistrationCacheTests.cs`:
  cache hit, idle eviction, outstanding dispose 지연, fallback lease 를 fake registrar 로 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 Task 2 payload send path cache lease 전환으로 이동했다.

### 검증
- Red: type boundary reflection test 가 `RioPayloadRegistrationCache` 부재로 `Assert.NotNull` 실패.
- Green/Refactor: direct internal API 기반 focused cache owner tests 4개 통과.
- focused RIO tests 33개 통과.

## 2026-06-25 (Codex - RIO payload registration cache plan)

### 작업 단위
- RIO payload registration cache 구현 계획을 작성했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-25-rio-payload-registration-cache.md`:
  D107 설계를 pure owner, payload send path cache lease, verification/benchmark/state update 의 3개 task 로 나눴다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 Task 1 `RioPayloadRegistrationCache` pure owner 구현으로 이동했다.

### 검증
- D107 spec coverage self-review, placeholder scan, type consistency scan 을 수행했다.
- `git diff --check` 통과.

## 2026-06-25 (Codex - RIO payload registration cache design)

### 작업 단위
- RIO payload `RefCountedBuffer` registration cache 를 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-25-rio-payload-registration-cache-design.md`:
  payload backing `byte[]` object identity 기반 cache, outstanding lease, dispose-delayed deregister,
  capacity fallback 정책을 설계했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D107로 connection resource bounded cache 를 먼저 구현하고 transport-wide shared cache 는 후속으로 둔다고 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 D107 구현 계획 작성으로 이동했다.

### 검증
- current payload send path, `RefCountedBuffer` release/pool return, `PinnedBlockMemoryPool` array reuse,
  D106 Task A 결과를 대조했다.
- placeholder scan 과 `git diff --check`로 문서 품질을 확인한다.

## 2026-06-25 (Codex - RIO registered buffer reuse Task A)

### 작업 단위
- RIO registered buffer reuse Task A 를 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioTransport.cs`:
  `RioConnectionResource`가 receive block 과 TCP length-prefix block 을 connection resource lifetime 에서 한 번 등록해 재사용한다.
  payload `RefCountedBuffer` send path 는 D106에 따라 기존 per-operation registration 을 유지한다.
- `src/Hps.Transport.Rio/RioNative.cs`:
  RIO buffer registration 재사용 여부를 테스트에서만 관측할 수 있는 internal diagnostic counter 를 추가했다.
- `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`:
  같은 connection 에서 receive/prefix registration 이 payload send 두 번 동안 반복되지 않는지 검증하는 loopback tests 를 추가했다.
  handler exception close notify test 는 peer close notify 순서에 의존하지 않고 server connection close 를 기다리도록 보정했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task A 완료와 다음 후보인 payload registration cache 설계 진입점을 기록했다.

### 검증/관측
- Red: 신규 diagnostic tests 2개가 `RioNative` registration diagnostic 경계 부재로 `Assert.NotNull` 실패.
- Green: focused diagnostic tests 2개 통과, focused RIO tests 29개 통과.
- close/wake 핵심 RIO tests 10회 반복 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-restore`: 통과.
- benchmark session-05:
  RIO load actual-rate 99.8 Hz, p50 281.6 us, p99 866.6 us.
  RIO open-loop actual-rate 99.8 Hz, p50 315.8 us, p99 936.4 us.

## 2026-06-25 (Codex - RIO registered buffer reuse Task A plan)

### 작업 단위
- RIO registered buffer reuse Task A 구현 계획을 작성했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-25-rio-registered-buffer-reuse-task-a.md`:
  receive block 과 length-prefix block resource lifetime registration 구현을
  receive registration, prefix registration, verification/benchmark observation 의 3개 task 로 나눴다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 D106 Task A 구현으로 이동했다.

### 검증
- plan placeholder scan 을 수행했고, 실제 placeholder 는 발견하지 못했다.
- `git diff --check` 통과.

## 2026-06-25 (Codex - RIO registered buffer reuse design)

### 작업 단위
- RIO registered buffer reuse 설계를 완료했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-25-rio-registered-buffer-reuse-design.md`:
  receive/length-prefix resource lifetime registration 과 payload registration cache 분리를 설계했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D106으로 receive/prefix 먼저, payload cache 별도 단위 분리를 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 Task A 구현 계획 작성으로 이동했다.

### 검증
- Microsoft RIO register/deregister/send/receive 문서와 current RIO registration code 를 대조했다.

## 2026-06-25 (Codex - RIO next optimization entry)

### 작업 단위
- RIO completion wait 이후 다음 실행 지점을 정리했다.

### 변경 내용
- `CURRENT_PLAN.md`, `TODOS.md`:
  IOCP notification wait Task 4가 `58c3c05`에서 완료됐음을 반영하고,
  다음 작업을 RIO registered buffer reuse 설계로 이동했다.

### 검증
- 직전 커밋 기준 focused RIO tests, close/wake 반복, solution build/test, benchmark session-04 가 통과했다.

## 2026-06-25 (Codex - RIO IOCP notification wiring)

### 작업 단위
- RIO IOCP/RIONotify completion wait Task 3 RIONotify + IOCP wiring 을 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioCompletionPort.cs`:
  실제 IOCP handle, pump task, completion key 기반 signal lookup, shutdown wake 를 연결했다.
- `src/Hps.Transport.Rio/RioCompletionSignal.cs`:
  notification memory, completion key, pre-wait signal 보존, notify armed 상태를 추가했다.
- `src/Hps.Transport.Rio/RioTransport.cs`:
  receive/send CQ를 notification CQ로 생성하고,
  `WaitForCompletionAsync(...)`를 polling fallback 없는 `RIONotify` + signal wait 로 전환했다.
- `tests/Hps.Transport.Rio.Tests/RioCompletionPortTests.cs`:
  Red를 위한 reflection helper 를 제거하고 internal type 직접 테스트로 정리했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D105로 IOCP notification wait 가 RIO p99 tail 을 해소한 기준선이라고 기록했다.

### 검증/관측
- 기존 latency regression guard 확인 후 구현했다.
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 27개 통과.
- close/wake 핵심 RIO tests 10회 반복 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-restore`: 통과.
- benchmark session-04:
  RIO load actual-rate 99.8 Hz, p50 319.3 us, p99 739.5 us.
  RIO open-loop actual-rate 99.8 Hz, p50 323.2 us, p99 948.8 us.

## 2026-06-25 (Codex - RIO completion signal owners)

### 작업 단위
- RIO IOCP/RIONotify completion wait Task 2 completion port/signal owner 를 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioCompletionPort.cs`:
  transport-wide completion owner 의 signal registry 와 dispose wake 경계를 추가했다.
- `src/Hps.Transport.Rio/RioCompletionSignal.cs`:
  CQ별 waiter wake, pump fault, dispose wake 를 관리하는 signal owner 를 추가했다.
- `tests/Hps.Transport.Rio.Tests/RioCompletionPortTests.cs`:
  signal completion wake 와 dispose wake 를 managed lifecycle 테스트로 고정했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 Task 3 RIONotify + IOCP wiring 으로 이동했다.

### 검증
- Red: `RioCompletionPortTests`가 타입 부재 `Assert.NotNull` failure 를 냈다.
- focused completion port tests 2개 통과.
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 27개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.

## 2026-06-25 (Codex - RIO IOCP native notification shape)

### 작업 단위
- RIO IOCP/RIONotify completion wait Task 1 native notification shape 를 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioNative.cs`:
  `RIONotify` delegate, notification CQ creation overload, IOCP P/Invoke/struct shape,
  `SupportsCompletionNotification` probe 를 추가했다.
- `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`:
  RIO available function table 이 notification function 을 노출하는지 검증하는 테스트를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 Task 2 completion port/signal owner 구현으로 이동했다.

### 검증
- Red: `TryLoadFunctionTable_WhenRioAvailable_ExposesNotificationFunctions`가
  `SupportsCompletionNotification` property 부재로 assertion failure 를 냈다.
- focused test green.
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 25개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.

## 2026-06-25 (Codex - RIO IOCP notification wait plan)

### 작업 단위
- RIO IOCP/RIONotify completion wait 구현 계획을 작성했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-25-rio-iocp-notification-completion-wait.md`:
  D104 shared IOCP pump 설계를 native notification shape, completion port/signal owner,
  RIONotify+IOCP wiring, benchmark observation/state update 의 4개 task 로 분해했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 Task 1 `RioNative` notification shape 구현으로 이동했다.

### 검증
- plan placeholder scan 을 수행했고, 실제 placeholder 는 발견하지 못했다.

## 2026-06-25 (Codex - RIO IOCP notification wait design)

### 작업 단위
- RIO IOCP/RIONotify completion wait 설계를 완료했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-25-rio-iocp-notification-completion-wait-design.md`:
  D102 이후에도 남은 16ms대 p99 tail 을 제거하기 위한 native notification wait 설계를 작성했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D104로 CQ별 event handle 이 아니라 `RioTransport`당 shared IOCP pump 를 채택한다고 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 작업을 D104 구현 계획 작성으로 이동했다.

### 검증
- Microsoft `RIONotify`, `RIO_NOTIFICATION_COMPLETION`, `RIOCreateCompletionQueue`,
  `RIODequeueCompletion` 문서와 current `RioNative`/`RioConnectionResource` 구조를 대조했다.
- spec placeholder scan 에서 작업용 placeholder 는 발견하지 못했다.

## 2026-06-25 (Codex - RIO completion wake bounded polling)

### 작업 단위
- RIO completion wake bounded yield polling 을 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioTransport.cs`:
  `WaitForCompletionAsync(...)`가 빈 completion queue 를 만나면 4096회까지 `Task.Yield()`로 재시도한 뒤
  기존 `Task.Delay(1)` fallback 으로 내려가도록 변경했다.
- `src/Hps.Transport/Runtime/TransportConnection.cs`,
  `src/Hps.Transport/Saea/SaeaTransport.cs`, `src/Hps.Transport.Rio/RioTransport.cs`:
  receive/send pump 가 동시에 close 를 관측할 때 close notification 이 중복될 수 있는 경합을 막기 위해
  `TransportConnection.TryClose()` 전이에 성공한 pump 만 `OnConnectionClosed`를 호출하도록 정렬했다.
- `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`:
  small payload wake regression test 를 추가하고, handler exception close test 는 server connection 단위
  close count 를 검증하도록 보정했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  D102 결과와 남은 p99 tail 을 기록하고 다음 작업을 IOCP/RIONotify completion wait 설계로 이동했다.

### 검증/관측
- Red: 기존 구현에서 `TcpLoopback_WhenRioAvailable_DeliversSmallPayloadWithoutTimerScaleWake`가
  16.199/10.392/14.022 ms sample 로 실패했다.
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 24개 통과.
- RIO close/wake 핵심 테스트 10회 반복 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-restore`: 통과.
- benchmark: D102 전 RIO load actual-rate 64.5 Hz/p50 15735 us/p99 16654 us,
  4096 budget 후 RIO load actual-rate 99.8 Hz/p50 198.8 us/p99 16689.0 us.
  open-loop p50 은 397.2 us 로 개선됐지만 p99 는 16736.2 us 로 남았다.

## 2026-06-25 (Codex - RIO completion wake design)

### 작업 단위
- RIO completion wake latency 개선 설계를 완료했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-25-rio-completion-wake-latency-design.md`:
  SAEA/RIO comparison artifact 의 RIO p99 약 16 ms 병목을 바탕으로,
  bounded `Task.Yield()` polling 후 `Task.Delay(1)` fallback 을 적용하는 최소 개선안을 설계했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D102로 IOCP/RIONotify 전면 재구조화 전에 bounded yield polling 을 먼저 적용한다고 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 구현 단위를 `RioTransport.WaitForCompletionAsync(...)` bounded yield polling 으로 이동했다.

### 검증
- current RIO code, 기존 pump hardening design, SAEA/RIO comparison artifact evidence 를 대조했다.

## 2026-06-25 (Codex - SAEA/RIO comparison artifact)

### 작업 단위
- SAEA/RIO benchmark comparison artifact 를 수집했다.

### 변경 내용
- `artifacts/benchmarks/rio-comparison/2026-06-25/session-01/`:
  SAEA/RIO load/open-loop raw report 와 mixed summary 를 scratch artifact 로 생성했다.
- `.gitignore`:
  repository baseline 이 아닌 scratch/CI artifact 가 실수로 stage 되지 않도록 `artifacts/`를 ignore 했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  RIO latency 병목 후보를 다음 설계 단위로 승격했다.

### 검증/관측
- SAEA load: pass, p99 890.8 us, actual-rate 99.8 Hz.
- SAEA open-loop: pass, p99 872.7 us, actual-rate 99.9 Hz.
- RIO load: pass, p99 16654.0 us, actual-rate 64.5 Hz.
- RIO open-loop: pass, p99 16826.6 us, actual-rate 99.8 Hz.
- mixed summary: hard-passed true, warning-count 3, comparison-compatible false, comparison mismatch 6개.

## 2026-06-25 (Codex - benchmark backend selector)

### 작업 단위
- SAEA/RIO benchmark backend selector 를 구현했다.

### 변경 내용
- `tests/Hps.Benchmarks/BenchmarkCommandParser.cs`, `BenchmarkCommandLine.cs`,
  `TcpLoopbackTransportBackend.cs`:
  `--backend <saea|rio>`를 runner/baseline-suite 명령에만 허용하고 command line model 에 저장하도록 추가했다.
- `tests/Hps.Benchmarks/TcpLoopbackScenarioRunner.cs`, `Program.cs`, `Hps.Benchmarks.csproj`,
  `BenchmarkRunIdentity.cs`:
  benchmark runner 가 선택된 backend 에 따라 `SaeaTransport` 또는 `RioTransport`를 생성하고,
  raw report identity/scenario 를 backend 별로 분리하도록 연결했다.
- `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`, `BenchmarkRunIdentityTests.cs`:
  parser Red 와 identity Red 를 추가한 뒤 green 으로 전환했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 진입점을 SAEA/RIO comparison artifact 수집으로 이동했다.

### 검증
- Red: `--load --backend rio --report ...`와 `--baseline-suite ... --backend rio`가 unknown runner arg 로 실패함을 확인했다.
- Red: `BenchmarkRunIdentity.CaptureForBackend` 부재를 assertion failure 로 확인했다.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore`: 71개 통과.
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-restore -- --smoke --backend saea --report $env:TEMP\hps-saea-smoke.json`: pass.
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-restore -- --smoke --backend rio --report $env:TEMP\hps-rio-smoke.json`: pass.
- report JSON 에서 SAEA=`tcp-loopback-saea-v1`/`SaeaTransport`,
  RIO=`tcp-loopback-rio-v1`/`RioTransport`를 확인했다.

## 2026-06-25 (Codex - SAEA/RIO benchmark comparison design)

### 작업 단위
- SAEA vs RIO benchmark comparison 설계를 완료했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-25-saea-rio-benchmark-comparison-design.md`:
  benchmark 내부 `--backend <saea|rio>` selector, backend 별 report identity/scenario,
  RIO unavailable 처리, schema 유지 정책을 설계했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D101로 SAEA/RIO benchmark 비교는 benchmark 전용 backend selector 로 수행하고 default factory 는 유지한다고 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 구현 단위를 benchmark backend selector parser/options 로 좁혔다.

### 검증
- benchmark CLI, result identity, report writer, summary/history comparison source 를 대조했다.

## 2026-06-25 (Codex - RIO factory opt-in policy)

### 작업 단위
- RIO default factory opt-in policy 정합성을 재확인했다.

### 변경 내용
- `src/Hps.Transport/Runtime/TransportFactory.cs`,
  `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`:
  기본 factory 가 계속 `SaeaTransport`를 반환하고, RIO는 명시 opt-in/test path 로 유지됨을 확인했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  factory policy 항목을 완료하고 다음 진입점을 SAEA vs RIO benchmark comparison 설계로 이동했다.

### 검증
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-build --no-restore`: 23개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 292개 통과.

## 2026-06-25 (Codex - RIO drop-oldest contract decision)

### 작업 단위
- RIO send queue/drop-oldest live saturation 테스트 후보를 D100으로 정리했다.

### 변경 내용
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  RIO TCP drop-oldest ownership 은 shared `TransportConnection` runtime 계약 테스트를 source of truth 로 두고,
  live RIO loopback saturation 테스트는 flake 위험 때문에 추가하지 않는다고 명시했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 진입점을 RIO default factory opt-in policy 문서/테스트 정합성 재평가로 이동했다.

### 검증
- `tests/Hps.Transport.Tests/Runtime/TransportSendQueueTests.cs`의 drop-oldest, in-flight release,
  close drain, diagnostics callback coverage 를 확인했다.

## 2026-06-25 (Codex - RIO handler exception contract)

### 작업 단위
- RIO TCP receive handler 예외 close-notify 계약을 테스트로 고정했다.

### 변경 내용
- `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`:
  RIO available 환경에서 client payload 수신 중 handler 가 예외를 던지면,
  receive pump 가 server connection close notification 으로 수렴하는지 검증하는 테스트를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  handler exception close notify 보강 완료와 다음 후보인 RIO send queue/drop-oldest contract 재평가를 반영했다.

### 검증
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 23개 통과.

## 2026-06-25 (Codex - RIO TCP close churn stress)

### 작업 단위
- RIO TCP pump close/churn stress coverage 를 추가했다.

### 변경 내용
- `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`:
  RIO available 환경에서 connect/accept 직후 close 를 25회 반복하는 테스트를 추가했다.
  이 테스트는 receive pump 가 outstanding `RIOReceive`를 가진 상태에서 socket/CQ 정리와 경합해도
  testhost crash 없이 끝나는지 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  close/churn stress 완료와 다음 후보인 RIO contract suite 확장 재평가를 반영했다.

### 검증
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 22개 통과.
- focused RIO tests 10회 반복 통과.

## 2026-06-25 (Codex - RIO TCP pump hardening)

### 작업 단위
- RIO Task 6 self-review 후 send completion byte-count loop 와 contract coverage 를 보강했다.

### 변경 내용
- `docs/agent-state/reviews/2026-06-25-rio-task6-self-review.md`:
  Task 6 구현을 SAEA 기준선과 대조하고, send partial completion 과 close-drain owner 를 hardening 후보로 기록했다.
- `docs/superpowers/specs/2026-06-25-rio-tcp-pump-hardening-design.md`,
  `docs/superpowers/plans/2026-06-25-rio-tcp-pump-hardening.md`:
  send completion byte-count loop 를 이번 구현 범위로 정하고, full close-drain owner 는 반복 테스트 증거가 생길 때 별도 승격하기로 정리했다.
- `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`:
  raw payload helper 를 expected length 누적 방식으로 바꾸고,
  4096-byte payload 와 length-prefixed stream send loopback coverage 를 추가했다.
- `src/Hps.Transport.Rio/RioTransport.cs`:
  RIO send completion 의 `BytesTransferred`를 기준으로 `remaining`이 0이 될 때까지 반복 send 한다.
  0 byte, error status, requested remaining 초과 completion 은 connection close 경로로 수렴한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  hardening 완료와 다음 후보인 close/churn stress 재평가를 반영했다.

### 검증
- Red: length-prefixed loopback 이 첫 callback 에서 prefix 4 bytes 만 받아 `Assert.Equal()` mismatch 로 실패함을 확인했다.
- Green: receive helper 를 expected length 누적 방식으로 보정하고 focused RIO tests 21개 통과.
- Repetition: focused RIO tests 10회 반복 통과.

## 2026-06-25 (Codex - RIO Task 6 TCP pump/contract path)

### 작업 단위
- Windows RIO backend Task 6으로 opt-in `RioTransport` TCP listen/connect/accept/receive/send pump 를 실제 transport contract 에 연결했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioTransport.cs`:
  RIO TCP listen/connect, per-connection CQ/RQ resource, receive pump, send pump, length-prefix send 보조 경로,
  close notification, pending send queue drain 연계를 추가했다.
  전체 테스트 중 connection close 가 CQ close 와 background dequeue 사이에서 경합하면 native access violation 이 날 수 있어,
  `RioConnectionResource`가 dequeue 와 CQ close 를 같은 gate 로 직렬화하도록 보정했다.
- `src/Hps.Transport.Rio/RioConnectionListener.cs`:
  RIO listener accept 경계를 추가했다. 일반 accepted socket 은 RIO RQ 생성이 실패하므로,
  `RioNative.CreateTcpSocket()`으로 만든 registered accept socket 을 `AcceptAsync(Socket, CancellationToken)`에 전달한다(D099).
- `src/Hps.Transport/Properties/AssemblyInfo.cs`:
  RIO backend 가 기존 `TransportConnection` pending queue/refcount 규칙을 재사용할 수 있도록 `Hps.Transport.Rio` friend assembly 를 추가했다.
- `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`:
  RIO available Windows 환경에서 `TrySend` payload 가 peer receive handler 로 도착하는 TCP loopback 테스트를 추가했다.
  receive helper 는 completion 누락을 무한 대기로 숨기지 않도록 timeout 을 둔다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, `CURRENT_PLAN.md`, `TODOS.md`,
  `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`:
  Task 6 완료, D099, 다음 self-review/hardening 진입점을 기록했다.

### 검증
- Red: `TcpLoopback_WhenRioAvailable_DeliversPayload`가 기존 `ListenTcpAsync` 미구현 `NotSupportedException`으로 실패함을 확인했다.
- Green 중 일반 accepted socket 에서는 RIO request queue handle 이 0으로 실패함을 확인했고,
  registered accept socket 제공 경로로 보정했다.
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 19개 통과.
- 전체 테스트 1차 실행 중 `RIODequeueCompletion` access violation 을 확인했고,
  CQ close/dequeue 직렬화 후 `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 288개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `git diff --check`: 통과.

## 2026-06-25 (Codex - RIO Task 5.11 connected posting verification)

### 작업 단위
- Windows RIO TCP pump 선행 하위 단위로 connected native receive/send posting completion 을 검증했다.

### 변경 내용
- `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`:
  registered I/O TCP socket 과 normal peer socket 을 loopback 으로 연결해
  `RIOReceive` post→peer send→CQ completion→registered buffer write 경로를 검증했다.
  같은 방식으로 `RIOSend` post→CQ completion→peer receive 경로도 검증했다.
- `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`:
  TCP pump 전에 native posting completion 을 검증하는 Task 5.11을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 5.11 완료와 다음 `RioTransport` TCP pump/contract test reuse 진입점을 반영했다.

### 검증
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 18개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 287개 통과, 실패 0.
- `git diff --check`: 통과.

## 2026-06-25 (Codex - RIO Task 5.10 send/receive delegate surface)

### 작업 단위
- Windows RIO TCP pump 선행 하위 단위로 native receive/send posting delegate surface 를 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioNative.cs`:
  loaded RIO function table 의 `RIOReceive`/`RIOSend` pointer 를 shared posting delegate 로 marshal 하고,
  SDK `RIO_BUF` layout 에 맞춘 `RioBufferSegment` struct 와 `Receive(...)`/`Send(...)` operation 을 추가했다.
- `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`:
  receive/send operation boundary Red 이후 direct internal API argument validation 으로 테스트를 정리했다.
- `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`:
  TCP pump 전에 receive/send delegate surface 를 검증하는 Task 5.10을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 5.10 완료와 다음 connected RIO send/receive posting completion 진입점을 반영했다.

### 검증
- Red: `Receive`/`Send` operation boundary 부재로 `Assert.NotNull() Failure: Value is null`을 확인했다.
- Green/refactor: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 16개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 285개 통과, 실패 0.
- `git diff --check`: 통과.

## 2026-06-25 (Codex - RIO Task 5.9 completion dequeue delegate)

### 작업 단위
- Windows RIO TCP pump 선행 하위 단위로 native completion dequeue delegate 를 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioNative.cs`:
  loaded RIO function table 의 `RIODequeueCompletion` pointer 를 delegate 로 marshal 하고,
  SDK `RIORESULT` layout 에 맞춘 `RioResult` struct 와 `DequeueCompletion(...)` operation 을 추가했다.
- `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`:
  RIO available 환경에서 빈 CQ를 dequeue 하면 0개 completion 이 반환되는지 검증한다.
- `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`:
  TCP pump 전에 dequeue delegate 를 검증하는 Task 5.9를 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 5.9 완료와 다음 receive/send posting native delegate boundary 진입점을 반영했다.

### 검증
- Red: `DequeueCompletion` operation boundary 부재로 `Assert.NotNull() Failure: Value is null`을 확인했다.
- Green/refactor: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 15개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 284개 통과, 실패 0.
- `git diff --check`: 통과.

## 2026-06-25 (Codex - RIO Task 5.8 request queue delegate)

### 작업 단위
- Windows RIO TCP pump 선행 하위 단위로 native request queue delegate 를 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioNative.cs`:
  `WSASocketW` + `WSA_FLAG_OVERLAPPED | WSA_FLAG_REGISTERED_IO` 기반 `CreateTcpSocket()`을 추가하고,
  loaded RIO function table 의 `RIOCreateRequestQueue` pointer 를 delegate 로 marshal 했다.
- `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`:
  RIO available 환경에서 registered I/O TCP socket 과 CQ 로 RQ handle 을 실제 생성하는 테스트를 추가했다.
- `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`:
  TCP pump 전에 RQ delegate 를 검증하는 Task 5.8을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 5.8 완료와 다음 receive/send/dequeue native delegate boundary 진입점을 반영했다.

### 검증
- Red: `CreateRequestQueue` operation boundary 부재로 `Assert.NotNull() Failure: Value is null`을 확인했다.
- Green 중 일반 .NET `Socket`으로는 RQ handle 이 0으로 실패함을 확인했고,
  `WSA_FLAG_REGISTERED_IO` socket factory 로 보정했다.
- Green/refactor: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 14개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 283개 통과, 실패 0.
- `git diff --check`: 통과.

## 2026-06-25 (Codex - RIO Task 5.7 completion queue delegates)

### 작업 단위
- Windows RIO TCP pump 선행 하위 단위로 native completion queue delegate 를 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioNative.cs`:
  loaded RIO function table 의 `RIOCreateCompletionQueue`/`RIOCloseCompletionQueue` pointer 를 delegate 로 marshal 하고,
  `CreateCompletionQueue(...)`/`CloseCompletionQueue(...)` internal operation 으로 노출했다.
- `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`:
  RIO available 환경에서 null notification completion 기반 CQ 를 실제로 생성/해제하는 테스트를 추가했다.
- `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`:
  TCP pump 전에 CQ delegate 를 검증하는 Task 5.7을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 5.7 완료와 다음 RQ native delegate boundary 진입점을 반영했다.

### 검증
- Red: `CreateCompletionQueue` operation boundary 부재로 `Assert.NotNull() Failure: Value is null`을 확인했다.
- Green/refactor: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 13개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 282개 통과, 실패 0.
- `git diff --check`: 통과.

## 2026-06-25 (Codex - RIO Task 5.6 buffer registration delegates)

### 작업 단위
- Windows RIO TCP pump 선행 하위 단위로 native buffer registration delegate 를 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioNative.cs`:
  loaded RIO function table 의 `RIORegisterBuffer`/`RIODeregisterBuffer` pointer 를 delegate 로 marshal 하고,
  `RegisterBuffer(...)`/`DeregisterBuffer(...)` internal operation 으로 노출했다.
- `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`:
  RIO available 환경에서 `PinnedBlockMemoryPool` block 을 실제 RIO buffer 로 등록/해제하는 테스트를 추가했다.
- `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`:
  TCP pump 전에 buffer registration delegate 를 검증하는 Task 5.6을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 5.6 완료와 다음 CQ/RQ native delegate boundary 진입점을 반영했다.

### 검증
- Red: `RegisterBuffer` operation boundary 부재로 `Assert.NotNull() Failure: Value is null`을 확인했다.
- Green/refactor: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 12개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 281개 통과, 실패 0.
- `git diff --check`: 통과.

## 2026-06-25 (Codex - RIO Task 5.5 native loader hardening)

### 작업 단위
- Windows RIO backend Task 6 전 선행 보정으로 실제 native function table loader 를 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioNative.cs`:
  `WSAIoctl(SIO_GET_MULTIPLE_EXTENSION_FUNCTION_POINTER, WSAID_MULTIPLE_RIO)` 호출로
  `RIO_EXTENSION_FUNCTION_TABLE`을 얻고 필수 function pointer 를 검증한다.
- `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`:
  Windows 환경에서 `RioCapabilityProbe.GetStatus()`가 실제 `Available`로 수렴해야 함을 검증한다.
- `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`,
  `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D098과 Task 5.5를 기록해 TCP pump 전에 실제 native loader 를 완료하도록 순서를 보정했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 5.5 완료와 다음 Task 6 TCP pump/contract test reuse 진입점을 반영했다.

### 검증
- Red: Windows에서 `GetStatus_WhenWindows_LoadsRioFunctionTable`이 `Expected: Available`, `Actual: Unavailable`로 실패함을 확인했다.
- Green/refactor: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 11개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 280개 통과, 실패 0.
- `git diff --check`: 통과.

## 2026-06-25 (Codex - RIO Task 5 TCP opt-in guard)

### 작업 단위
- Windows RIO backend Task 5로 TCP opt-in transport guard 를 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioTransport.cs`:
  `ListenTcpAsync`/`ConnectTcpAsync`가 실행 중 lifecycle 확인 뒤 RIO capability 를 먼저 검사하도록 했다.
  현재 환경에서 Windows RIO function table 을 사용할 수 없으면 실제 TCP wiring 미구현 메시지보다 먼저
  명시적인 `NotSupportedException`으로 실패한다.
- `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`:
  RIO unavailable 환경에서 opt-in TCP listen 이 function table failure 를 노출하는지 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 5 완료와 Task 6 진입 전 native loader gap 재평가 필요성을 반영했다.

### 검증
- Red: unavailable guard 테스트가 기존 미구현 메시지 때문에 `Assert.Contains()` 실패함을 확인했다.
- Green/refactor: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 10개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 279개 통과, 실패 0.
- `git diff --check`: 통과.

## 2026-06-25 (Codex - RIO Task 4 queue owners)

### 작업 단위
- Windows RIO backend Task 4로 TCP queue owner skeleton 을 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioRequestQueue.cs`:
  receive/send outstanding quota reservation 과 completion accounting 을 추가했다.
- `src/Hps.Transport.Rio/RioCompletionQueue.cs`:
  native CQ 연결 전 수명 owner skeleton 을 추가했다.
- `tests/Hps.Transport.Rio.Tests/RioQueueOwnerTests.cs`:
  receive/send quota 초과와 completion 후 재예약 가능성을 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 4 완료와 다음 Task 5 TCP opt-in guard 진입점을 반영했다.

### 검증
- Red: queue owner 타입 부재로 `Assert.NotNull() Failure: Value is null` 2개를 확인했다.
- Green/refactor: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 9개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 278개 통과, 실패 0.
- `git diff --check`: 통과.

## 2026-06-25 (Codex - RIO Task 3 registered buffer owner)

### 작업 단위
- Windows RIO backend Task 3으로 registered buffer owner 수명 규칙을 구현했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioRegisteredBufferPool.cs`:
  outstanding request 완료 전에는 pinned block 을 반환하지 않고, completion 중복 호출은 한 번만 release 하도록 했다.
- `src/Hps.Transport.Rio/Properties/AssemblyInfo.cs`:
  RIO test assembly 에 internal 접근을 허용했다.
- `tests/Hps.Transport.Rio.Tests/RioRegisteredBufferPoolTests.cs`,
  `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`:
  Red 확인 후 reflection 중심 테스트를 direct internal API 테스트로 정리했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 3 완료와 다음 Task 4 TCP queue owner 진입점을 반영했다.

### 검증
- Red: `RioRegisteredBufferPool_TypeExists`가 `Assert.NotNull() Failure: Value is null`로 실패함을 확인했다.
- Green/refactor: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 7개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 276개 통과, 실패 0.
- `git diff --check`: 통과.

## 2026-06-25 (Codex - RIO Task 2 native loader boundary)

### 작업 단위
- Windows RIO backend Task 2로 native function table loader 경계를 추가했다.

### 변경 내용
- `src/Hps.Transport.Rio/RioNative.cs`:
  RIO native function table load 를 숨기는 internal boundary 를 추가했다.
- `src/Hps.Transport.Rio/RioCapabilityProbe.cs`:
  Windows probe 가 `RioNative.TryLoadFunctionTable(...)` 결과를 통해 `Available` 또는 `Unavailable`로 수렴하도록 연결했다.
- `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`:
  `RioNative` 타입 존재와 Windows probe non-throw 경로를 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 2 완료와 다음 Task 3 registered buffer owner 진입점을 반영했다.

### 검증
- Red: `RioNative_TypeExists`가 `Assert.NotNull() Failure: Value is null`로 실패함을 확인했다.
- Green: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 6개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 275개 통과, 실패 0.
- `git diff --check`: 통과.

## 2026-06-25 (Codex - RIO Task 1 skeleton/probe)

### 작업 단위
- Windows RIO backend Task 1로 project skeleton 과 capability probe public surface 를 추가했다.

### 변경 내용
- `src/Hps.Transport.Rio/`:
  `Hps.Transport.Rio.csproj`, `RioCapabilityStatus`, `RioCapabilityProbe`, `RioTransport` skeleton 을 추가했다.
- `tests/Hps.Transport.Rio.Tests/`:
  reflection 기반 Red를 사용하는 capability probe tests 를 추가했다.
- `HighPerformanceSocket.slnx`:
  RIO source/test projects 를 solution 에 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 1 완료와 다음 Task 2 native function table loader 진입점을 반영했다.

### 검증
- Red: `RioCapabilityProbe_TypeExists`가 `Assert.NotNull() Failure: Value is null`로 실패함을 확인했다.
- Green: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 4개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 273개 통과, 실패 0.
- `git diff --check`: 통과.

## 2026-06-25 (Codex - Windows RIO backend implementation plan)

### 작업 단위
- D097 Windows RIO backend boundary 설계를 구현 가능한 계획으로 분해했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`:
  RIO project skeleton/probe, native function table loader, registered buffer owner,
  TCP queue owner, TCP opt-in guard, TCP pump/contract test reuse 의 6개 task 를 작성했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  계획 작성 완료 상태와 다음 실행 단위인 RIO Task 1 skeleton/probe 구현을 반영했다.

### 검증
- 계획 self-review 로 D097 spec coverage, task boundary, type naming consistency 를 확인했다.
- placeholder scan 결과 신규 plan 에 미정 항목 없음. 검색에 잡힌 항목은 파일명 문자열 또는 기존 archive/changelog 문맥이다.
- `git diff --check`: 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - Windows RIO backend boundary design)

### 작업 단위
- Phase 5 Windows RIO backend 의 책임 경계와 첫 구현 순서를 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-25-windows-rio-backend-boundary-design.md`:
  RIO backend 를 TCP-first 로 진행하되, 첫 task 를 project skeleton, capability probe,
  native function table wrapper 로 분리하는 설계를 기록했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D097로 RIO TCP-first/probe-first 정책과 SAEA default 유지 방침을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  RIO 설계 완료 상태와 다음 실행 단위인 RIO 구현 계획 작성 진입점을 반영했다.

### 검증
- `TransportFactory`, `TransportBase`, `TransportConnection`, `SaeaTransport` 구조와 설계가 충돌하지 않는지 대조했다.
- Microsoft Learn RIO request queue, completion queue, buffer registration, notification/dequeue 문서를 확인했다.
- placeholder scan 결과 신규 spec/current state 에 미정 항목 없음. 검색에 잡힌 항목은 기존 archive/changelog 문맥이다.
- `git diff --check`: 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - after CI baseline adoption reassessment)

### 작업 단위
- 첫 CI repository baseline 채택 이후 Phase 4 다음 후보를 재평가했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-25-after-ci-baseline-adoption-reassessment-design.md`:
  CI baseline adoption 이후 gate 승격 보류와 Phase 5 RIO 설계 진입 판단을 기록했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D096으로 첫 CI baseline 이후에도 latency hard gate, warning-as-failure, CI artifact 자동 채택을 승격하지 않는다고 기록했다.
- `docs/benchmarks/baselines/index.md`:
  CI runner envelope 가 1-session reference signal 이며 gate 조건이 아님을 명시했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  완료된 Phase 4 재평가를 정리하고 다음 실행 단위를 Phase 5 Windows RIO backend 설계로 갱신했다.

### 검증
- CI runner root history 와 session summary 를 대조했다.
- D082/D090/D095와 D096 판단이 충돌하지 않는지 확인했다.
- `git diff --check`: 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - CI artifact baseline adoption)

### 작업 단위
- D095 절차에 따라 push-triggered run `28145025444` artifact 를 첫 CI repository baseline 으로 수동 채택했다.

### 변경 내용
- `docs/benchmarks/baselines/runners/ci-windows-x64-01/2026-06-25/session-01/`:
  raw report 6개를 보존하고 `summary.json`, `summary.md`를 repository 경로 기준으로 재생성했다.
- `docs/benchmarks/baselines/runners/ci-windows-x64-01/2026-06-25/history.json`,
  `history.md`, `docs/benchmarks/baselines/runners/ci-windows-x64-01/history.json`, `history.md`:
  date-level/runner-level history 를 생성했다.
- `docs/benchmarks/baselines/index.md`:
  CI runner group, date-level history, session row, CI runner reference envelope 를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  채택 완료 상태와 다음 Phase 4 재평가 진입점을 기록했다.

### 검증
- D095 checklist 를 통과했다: raw report 6개, hard-passed true, warning-count 0,
  comparison-compatible true, unknown-runner-count 0, runner metadata 일치.
- summary/history 재생성 결과: session-count 1, hard-passed true, warning-count 0,
  comparison-compatible true.
- absolute path scan 결과 없음.
- `git diff --check`: exit 0.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-build --no-restore`: 67개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - CI artifact adoption policy)

### 작업 단위
- CI artifact 를 어떤 조건과 절차로 repository baseline 에 수동 채택할지 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-25-ci-artifact-adoption-policy-design.md`:
  채택 조건, 금지 항목, raw report 복사와 summary/history 재생성 절차를 정리했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D095로 CI artifact 수동 채택 정책을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 작업을 run `28145025444` artifact 의 repository baseline 채택으로 갱신했다.

### 검증
- D090/D093/D094, `docs/benchmarks/baselines/index.md`, downloaded artifact 구조를 대조했다.
- spec placeholder scan 에서 신규 미정 항목 없음.
- `git diff --check`: exit 0.

## 2026-06-25 (Codex - CI artifact push trigger verification)

### 작업 단위
- D094 `push` to `master` path trigger 가 원격에서 자동으로 `Benchmark Artifacts` run 을 생성하는지 확인했다.

### 변경 내용
- `CURRENT_PLAN.md`, `TODOS.md`:
  push-triggered run `28145025444` 결과와 다음 실행 후보를 기록했다.

### 검증
- `git status -sb`: local `master`와 `origin/master`가 일치함을 확인했다.
- `gh run list --workflow "Benchmark Artifacts" --limit 5`:
  push event run `28145025444`가 생성됐음을 확인했다.
- `gh run watch 28145025444 --exit-status`: 성공, job duration 약 4분 7초.
- 로그 확인:
  `actions/checkout@v7`, `actions/setup-dotnet@v5.3.0`, `actions/upload-artifact@v7.0.1` 다운로드 및 실행을 확인했다.
- Node annotation 확인:
  `deprecation`, `Node.js 20`, `node20`, 이전 `actions/*@v4` 문자열 검색 결과 없음.
- artifact upload:
  `benchmark-artifacts-ci-windows-x64-01-2026-06-25-github-28145025444-1`,
  artifact id `7868207312`, uploaded files 10개, final size 6407 bytes.
- downloaded artifact 확인:
  raw report 6개, `summary.json`, `summary.md`, `history.json`, `history.md`.
- `summary.json`: source-report-count 6, hard-passed true, warning-count 0,
  comparison-compatible true, unknown-runner-count 0.
- `history.json`: session-count 1, hard-passed true, warning-count 0, comparison-compatible true.

## 2026-06-25 (Codex - CI artifact trigger policy)

### 작업 단위
- `Benchmark Artifacts` workflow 의 자동 실행 trigger 정책을 설계하고 workflow 에 반영했다.

### 변경 내용
- `.github/workflows/benchmark-artifacts.yml`:
  `workflow_dispatch`를 유지하고, `push` to `master` + code/benchmark/build path filter 를 추가했다.
- `docs/superpowers/specs/2026-06-25-ci-artifact-trigger-policy-design.md`:
  PR/schedule 은 제외하고 master push path filter 를 채택하는 근거를 정리했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D094로 trigger 정책을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 검증 지점을 D094 workflow 변경 push 후 자동 run 확인으로 갱신했다.

### 검증
- workflow marker scan 으로 `workflow_dispatch`, `push`, `branches: master`, path filter 를 확인했다.
- workflow scan 에서 `pull_request`, `schedule`, warning-as-failure, latency failure logic 이 없음을 확인했다.
- `git diff --check`: exit 0.

## 2026-06-25 (Codex - CI artifact follow-up reassessment)

### 작업 단위
- `ci-windows-x64-01` artifact-only manual run 2회 결과를 기준으로 Phase 4 다음 후보를 재평가했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-25-ci-artifact-after-manual-runs-reassessment.md`:
  gate/trigger/baseline 채택 여부와 다음 후보를 정리했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D093으로 manual run 2회만으로는 gate/trigger 를 승격하지 않는다고 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 단위를 CI artifact trigger policy 설계로 갱신했다.

### 검증
- run `28143728630`, run `28144480160` log/artifact 값을 대조했다.
- D090/D091/D092와 `docs/benchmarks/baselines/index.md`를 대조했다.
- `git diff --check`로 문서 변경 상태를 검증한다.

## 2026-06-25 (Codex - CI workflow Node 24 manual run)

### 작업 단위
- `actions/*` Node 24 version 갱신 후 `Benchmark Artifacts` workflow 를 다시 manual `workflow_dispatch`로 실행하고 결과를 확인했다.

### 변경 내용
- `CURRENT_PLAN.md`, `TODOS.md`:
  run `28144480160` 결과, artifact 이름, summary/history 핵심 값, Node deprecation 제거 확인을 기록했다.

### 검증
- `gh workflow run "Benchmark Artifacts" --ref master`: run `28144480160` 생성.
- `gh run watch 28144480160 --exit-status`: 성공, job duration 약 4분 15초.
- 로그 확인:
  `actions/checkout@v7`, `actions/setup-dotnet@v5.3.0`, `actions/upload-artifact@v7.0.1` 다운로드 및 실행을 확인했다.
- Node annotation 확인:
  `deprecation`, `Node.js 20`, `node20`, 이전 `actions/*@v4` 문자열 검색 결과 없음.
- artifact upload:
  `benchmark-artifacts-ci-windows-x64-01-2026-06-25-github-28144480160-1`,
  artifact id `7868009214`, uploaded files 10개, final size 6399 bytes.
- downloaded artifact 확인:
  raw report 6개, `summary.json`, `summary.md`, `history.json`, `history.md`.
- `summary.json`: source-report-count 6, hard-passed true, warning-count 0,
  comparison-compatible true, unknown-runner-count 0.
- `history.json`: session-count 1, hard-passed true, warning-count 0, comparison-compatible true.
- 이번 결과도 D090 기준으로 docs baseline 에 자동 채택하지 않고 CI artifact evidence 로만 둔다.

## 2026-06-25 (Codex - CI workflow Node 24 action versions)

### 작업 단위
- 첫 GitHub Actions manual run 에서 확인된 Node.js 20 deprecation annotation 을 제거하기 위해 workflow action version 을 갱신했다.

### 변경 내용
- `.github/workflows/benchmark-artifacts.yml`:
  `actions/checkout@v7`, `actions/setup-dotnet@v5.3.0`, `actions/upload-artifact@v7.0.1`로 갱신했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D092로 Node 24 action runtime 갱신 결정을 기록했다.
- `docs/superpowers/plans/2026-06-25-ci-artifact-only-workflow-skeleton.md`,
  `docs/superpowers/specs/2026-06-25-ci-artifact-only-benchmark-policy-design.md`:
  action version 과 benchmark command sequence 문구를 현재 workflow 와 맞췄다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Node deprecation follow-up 을 처리된 상태로 정리하고, 다음 후보를 갱신된 workflow manual run 검증으로 좁혔다.

### 검증
- 공식 release/action metadata 확인 기준, 세 action version 은 `runs.using: node24`를 명시한다.
- `git diff --check`: exit 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 단독 재실행 기준 경고 0, 오류 0.
  비고: 최초 build/test 병렬 실행 때 테스트 프로세스와 DLL copy 가 겹쳐 MSB3026 copy retry 경고 1개가 발생했으나,
  테스트 종료 후 build 단독 재실행에서는 경고 없이 통과했다.

## 2026-06-25 (Codex - CI workflow first manual run)

### 작업 단위
- 원격 push 이후 `Benchmark Artifacts` workflow 를 manual `workflow_dispatch`로 실행하고 artifact 결과를 확인했다.

### 변경 내용
- `CURRENT_PLAN.md`, `TODOS.md`:
  첫 GitHub Actions run 결과, artifact 이름, summary/history 핵심 값, 남은 follow-up 을 기록했다.

### 검증
- `gh workflow list`: `Benchmark Artifacts` active, workflow id `301858085`.
- `gh workflow run "Benchmark Artifacts" --ref master`: run `28143728630` 생성.
- `gh run watch 28143728630 --exit-status`: 성공, job duration 약 4분 5초.
- artifact upload: `benchmark-artifacts-ci-windows-x64-01-2026-06-25-github-28143728630-1`,
  artifact id `7867724437`, uploaded files 10개, final size 6576 bytes.
- downloaded artifact 확인: raw report 6개, `summary.json`, `summary.md`, `history.json`, `history.md`.
- `summary.json`: source-report-count 6, hard-passed true, warning-count 1,
  comparison-compatible true, unknown-runner-count 0.
- `history.json`: session-count 1, hard-passed true, warning-count 1, comparison-compatible true.
- warning detail: `open-loop-01.json`의 `p99-growth-ratio-high`이며 D090 기준 report-only 다.
- non-blocking annotation: Node.js 20 deprecation 안내가 `actions/checkout@v4`, `actions/setup-dotnet@v4`,
  `actions/upload-artifact@v4`에 발생했다. workflow는 Node 24 강제 실행으로 성공했다.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - CI workflow command sequence smoke)

### 작업 단위
- CI artifact-only workflow 의 benchmark command sequence 를 로컬 임시 artifact root 에서 smoke 하고 no-restore 형태로 보정했다.

### 변경 내용
- `.github/workflows/benchmark-artifacts.yml`:
  benchmark CLI 실행 세 단계에 모두 `--no-build --no-restore`를 명시했다.
- `docs/superpowers/plans/2026-06-25-ci-artifact-only-workflow-skeleton.md`:
  workflow 예시 command 를 실제 구현과 같은 no-restore 형태로 맞췄다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  local command sequence smoke 결과와 남은 GitHub-hosted runner manual run 검증을 기록했다.

### 검증
- 최초 local full smoke: workflow command sequence 로 `--runs 3`을 실행해 raw report 6개,
  `summary.json`/`summary.md`, `history.json`/`history.md` 생성을 확인했다.
- 최초 smoke 관찰: 첫 benchmark `dotnet run`이 restore를 다시 시도해 `NU1900` package vulnerability data warning 을 냈다.
- 보정 후 local smoke: `--no-build --no-restore` command sequence 로 `--runs 1`을 실행해 raw report 2개,
  summary/history artifact 생성, `hard-passed=true`, `warning-count=0`을 확인했다.
- local smoke 후 sandbox NuGet cache 경로가 `project.assets.json`에 반영되어 최초 `dotnet build --no-restore`가
  누락 analyzer DLL로 실패했다. `dotnet restore HighPerformanceSocket.slnx`를 다시 실행해 실제 환경 cache 기준으로 복구했다.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: restore 후 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - CI artifact-only workflow skeleton)

### 작업 단위
- D090/D091 정책에 맞춰 GitHub Actions benchmark artifact-only workflow skeleton 을 추가했다.

### 변경 내용
- `.github/workflows/benchmark-artifacts.yml`:
  `workflow_dispatch` 전용 Windows workflow 를 추가했다.
- workflow job env:
  `HPS_BENCHMARK_RUNNER_ID=ci-windows-x64-01`, `HPS_BENCHMARK_RUNNER_KIND=ci`를 고정했다.
- workflow command sequence:
  restore, build, test, `--baseline-suite`, `--summarize-baseline`, `--summarize-baseline-history`, artifact upload 순서로 구성했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 workflow 검토 또는 첫 manual run 결과 반영으로 갱신했다.

### 검증
- workflow static marker scan: `workflow_dispatch`, `ci-windows-x64-01`, `HPS_BENCHMARK_RUNNER_KIND`,
  현재 workflow 기준 `actions/upload-artifact@v7.0.1` 존재를 확인했다.
- workflow out-of-scope scan: `push`, `pull_request`, `warning-count`, `latency` logic 이 workflow 에 없음을 확인했다.
- lightweight policy check: required marker 존재와 자동 trigger 부재를 확인했다.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - CI artifact-only workflow skeleton plan)

### 작업 단위
- D090 정책을 실제 GitHub Actions workflow skeleton 으로 옮기기 위한 구현 계획을 작성했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-25-ci-artifact-only-workflow-skeleton.md`:
  workflow trigger, runner identity, artifact path, benchmark CLI command sequence, upload policy 를 구현 단계로 정리했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D091로 GitHub run id 는 upload artifact 이름에 두고 내부 history-compatible directory 는 `session-01`로 유지하는 결정을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 workflow skeleton 구현으로 갱신했다.

### 검증
- D090 spec 의 artifact-only failure policy 와 runner identity 를 계획에 반영했다.
- `BaselineHistoryReader`가 date root 와 `session-NN` children 만 history source 로 읽는 현재 제약을 확인했다.
- `.github/workflows`가 아직 없음을 확인했다.
- placeholder scan 은 과거 archive 문구와 plan 내부 검증 스크립트 literal 만 잡았고, 신규 미정 항목은 없었다.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - CI artifact-only benchmark policy)

### 작업 단위
- CI workflow 구현 전에 CI artifact-only benchmark 정책을 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-25-ci-artifact-only-benchmark-policy-design.md`:
  CI runner id, artifact 저장 위치, local/CI baseline 분리, exit code 정책, report-only latency/HWM/warning 기준을 정리했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D090을 추가했다.
- `docs/benchmarks/baselines/index.md`:
  CI 매 실행 artifact 는 docs baseline 에 자동 추가하지 않고 artifact-only 영역에 둔다는 운영 원칙을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 CI artifact-only workflow skeleton 구현 계획으로 갱신했다.

### 검증
- `tests/Hps.Benchmarks/Program.cs`: `baseline-suite`, `summary`, `history`가 hard-passed 기반 exit code 를 쓰고
  `warning-count > 0`만으로 실패하지 않는 현재 규약을 대조했다.
- `tests/Hps.Benchmarks/BenchmarkRunIdentity.cs`: CI runner id/kind 를 환경 변수로 주입할 수 있고,
  host/user/IP를 자동 수집하지 않는 privacy 정책을 확인했다.
- `.github/workflows`가 아직 없음을 확인했다.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - Phase 4 gate promotion reassessment)

### 작업 단위
- explicit runner 2-date-root/6-session reference 이후 D082 warning-as-failure/CI latency gate 승격 후보를 재평가했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-25-phase4-gate-promotion-reassessment-design.md`:
  D082 조건 충족/미충족 상태, 선택지, gate 보류 결정, 다음 CI artifact-only 정책 설계 진입점을 정리했다.
- `docs/benchmarks/baselines/index.md`:
  `local-win-x64-01` 2-date-root reference 완료 후에도 D089 기준으로 gate 를 즉시 승격하지 않는다는 해석 메모를 추가했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D089를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 CI artifact-only benchmark 정책 설계로 갱신했다.

### 검증
- runner root `history.json`: session-count 6, hard-passed true, warning-count 0, comparison-compatible true 를 확인했다.
- `docs/benchmarks/baselines/index.md`: explicit runner envelope 수치와 D089 해석 메모를 대조했다.
- D082 조건 대조: 명시 runner id, 각 date root 3-session, hard/comparison pass 는 충족하지만 서로 다른 date root 3개 이상과
  별도 warning threshold 검토는 미충족임을 확인했다.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - explicit runner baseline date root 02 session 03)

### 작업 단위
- `local-win-x64-01/2026-06-25/session-03` explicit runner baseline 을 수집하고, 두 번째 explicit runner date root 를 3-session reference 로 완성했다.

### 변경 내용
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/session-03/`:
  `load-01..03.json`, `open-loop-01..03.json` raw report 6개를 생성했다.
- `session-03/summary.json`, `session-03/summary.md`:
  explicit runner summary artifact 를 생성했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/history.json`, `history.md`:
  2026-06-25 date-level history artifact 를 3-session 기준으로 재생성했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/history.json`, `history.md`:
  두 date root 를 묶는 runner root history artifact 를 6-session 기준으로 재생성했다.
- `docs/benchmarks/baselines/index.md`:
  runner date-level history, session row, explicit runner reference latency envelope 를 갱신했다.
- `DECISIONS.md`, `CURRENT_PLAN.md`, `TODOS.md`, `docs/agent-state/decisions/2026-06.md`:
  D088과 다음 Phase 4 gate 후보 재평가 진입점을 반영했다.

### 검증
- `--baseline-suite`: baseline-suite-result pass, raw report 6개 생성.
- `--summarize-baseline`: source-report-count 6, hard-passed true, warning-count 0.
- date root `--summarize-baseline-history`: session-count 3, hard-passed true, warning-count 0.
- runner root `--summarize-baseline-history`: session-count 6, hard-passed true, warning-count 0.
- `summary.json`/`history.json`: `comparison-compatible=true`, unknown runner 0, mismatch 0.
- explicit runner envelope: load p99 max 935.6 us, open-loop p99 max 1077.4 us, TCP HWM max 2,
  dropped total 0, payload error total 0, pool rented max 0.
- runner artifact local absolute path 검색 결과 없음.
- `Hps.Benchmarks.Tests`: 67개 통과, 실패 0.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 최초 실행은 stale restore asset 때문에
  `Microsoft.CodeAnalysis.Analyzers.dll` 경로 오류로 실패했고, `dotnet restore HighPerformanceSocket.slnx` 후 재실행해 경고 0, 오류 0으로 통과.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - explicit runner baseline date root 02 session 02)

### 작업 단위
- `local-win-x64-01/2026-06-25/session-02` explicit runner baseline 을 수집하고, 파생 문서를 갱신했다.

### 변경 내용
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/session-02/`:
  `load-01..03.json`, `open-loop-01..03.json` raw report 6개를 생성했다.
- `session-02/summary.json`, `session-02/summary.md`:
  explicit runner summary artifact 를 생성했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/history.json`, `history.md`:
  2026-06-25 date-level history artifact 를 2-session 기준으로 재생성했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/history.json`, `history.md`:
  두 date root 를 묶는 runner root history artifact 를 5-session 기준으로 재생성했다.
- `docs/benchmarks/baselines/index.md`:
  runner date-level history, session row, explicit runner reference latency envelope 를 갱신했다.
- `DECISIONS.md`, `CURRENT_PLAN.md`, `TODOS.md`, `docs/agent-state/decisions/2026-06.md`:
  D087과 다음 `session-03` 수집 진입점을 반영했다.

### 검증
- `--baseline-suite`: baseline-suite-result pass, raw report 6개 생성.
- `--summarize-baseline`: source-report-count 6, hard-passed true, warning-count 0.
- date root `--summarize-baseline-history`: session-count 2, hard-passed true, warning-count 0.
- runner root `--summarize-baseline-history`: session-count 5, hard-passed true, warning-count 0.
- `summary.json`/`history.json`: `comparison-compatible=true`, unknown runner 0, mismatch 0.
- runner artifact local absolute path 검색 결과 없음.
- `Hps.Benchmarks.Tests`: 67개 통과, 실패 0.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 최초 실행은 stale restore asset 때문에
  `Microsoft.CodeAnalysis.Analyzers.dll` 경로 오류로 실패했고, `dotnet restore HighPerformanceSocket.slnx` 후 재실행해 경고 0, 오류 0으로 통과.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - explicit runner baseline date root 02 session 01)

### 작업 단위
- `local-win-x64-01/2026-06-25/session-01` explicit runner baseline 을 수집하고, 파생 문서를 갱신했다.

### 변경 내용
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/session-01/`:
  `load-01..03.json`, `open-loop-01..03.json` raw report 6개를 생성했다.
- `session-01/summary.json`, `session-01/summary.md`:
  explicit runner summary artifact 를 생성했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/history.json`, `history.md`:
  2026-06-25 date-level history artifact 를 생성했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/history.json`, `history.md`:
  두 date root 를 묶는 runner root history artifact 를 생성했다.
- `docs/benchmarks/baselines/index.md`:
  runner group latest date root, runner date-level history, session row, explicit runner reference latency envelope 를 갱신했다.
- `DECISIONS.md`, `CURRENT_PLAN.md`, `TODOS.md`:
  D086과 다음 `session-02` 수집 진입점을 반영했다.

### 검증
- `--baseline-suite`: baseline-suite-result pass, raw report 6개 생성.
- `--summarize-baseline`: source-report-count 6, hard-passed true, warning-count 0.
- date root `--summarize-baseline-history`: session-count 1, hard-passed true, warning-count 0.
- runner root `--summarize-baseline-history`: session-count 4, hard-passed true, warning-count 0.
- `summary.json`/`history.json`: `comparison-compatible=true`, unknown runner 0, mismatch 0.
- runner artifact local absolute path 검색 결과 없음.
- `Hps.Benchmarks.Tests`: 67개 통과, 실패 0.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 최초 실행은 stale restore asset 때문에
  `Microsoft.CodeAnalysis.Analyzers.dll` 경로 오류로 실패했고, `dotnet restore HighPerformanceSocket.slnx` 후 재실행해 경고 0, 오류 0으로 통과.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-25 (Codex - Phase 4 next candidate after explicit runner reference)

### 작업 단위
- explicit runner 3-session reference 이후 Phase 4 다음 실행 후보를 재평가했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-25-phase4-after-explicit-runner-reference-reassessment.md`:
  다음 date root 수집, CI/warning-as-failure 설계, RIO/io_uring 착수 후보를 비교했다.
- `DECISIONS.md`:
  D085를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 `local-win-x64-01/2026-06-25/session-01` 수집으로 갱신했다.

### 검증
- `local-win-x64-01/2026-06-24/history.json`: session-count 3, hard-passed true, warning-count 0,
  comparison-compatible true 를 확인했다.
- `docs/benchmarks/baselines/index.md`: explicit runner date root 가 아직 1개뿐임을 확인했다.
- D082/D084와 `.claude/review/`의 기존 benchmark 리뷰 의견을 대조했다.
- 신규 spec placeholder 검색 결과 없음.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-24 (Codex - explicit runner baseline 3-session reference)

### 작업 단위
- `local-win-x64-01/2026-06-24` explicit runner baseline 을 3-session reference 로 확장하고 문서 batch 를 완료했다.

### 변경 내용
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/session-02/`:
  `load-01..03.json`, `open-loop-01..03.json` raw report 6개를 생성했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/session-03/`:
  `load-01..03.json`, `open-loop-01..03.json` raw report 6개를 생성했다.
- `session-02/summary.json`, `session-02/summary.md`, `session-03/summary.json`, `session-03/summary.md`:
  explicit runner summary artifact 를 생성했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/history.json`, `history.md`:
  runner/date-level history artifact 를 3-session 기준으로 재생성했다.
- `docs/benchmarks/baselines/index.md`:
  runner date-level history, session row, explicit runner reference latency envelope 를 갱신했다.
- `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`:
  3-session reference 완료 상태와 다음 Phase 4 재평가 진입점을 반영했다.

### 검증
- `session-02 --baseline-suite`: baseline-suite-result pass, raw report 6개 생성.
- `session-03 --baseline-suite`: baseline-suite-result pass, raw report 6개 생성.
- `session-02 --summarize-baseline`: source-report-count 6, hard-passed true, warning-count 0.
- `session-03 --summarize-baseline`: source-report-count 6, hard-passed true, warning-count 0.
- `--summarize-baseline-history`: session-count 3, hard-passed true, warning-count 0.
- `history.json`: `comparison-compatible=true`, unknown runner 0, mismatch 0.
- explicit runner envelope: load p99 max 870.7 us, open-loop p99 max 1051.5 us, TCP HWM max 2,
  dropped total 0, payload error total 0, pool rented max 0.
- runner artifact local absolute path 검색 결과 없음.
- `Hps.Benchmarks.Tests`: 67개 통과, 실패 0.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-24 (Codex - explicit runner baseline session-01)

### 작업 단위
- 첫 explicit runner baseline 을 D084 runner group 구조에 수집했다.

### 변경 내용
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/session-01/`:
  `load-01..03.json`, `open-loop-01..03.json` raw report 6개를 생성했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/session-01/summary.json`, `summary.md`:
  explicit runner summary artifact 를 생성했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/history.json`, `history.md`:
  runner/date-level history artifact 를 생성했다.
- `docs/benchmarks/baselines/index.md`:
  runner group, runner date-level history, session row 를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  이번 artifact 수집 완료와 다음 `session-02` 수집 진입점을 반영했다.

### 검증
- `--baseline-suite`: baseline-suite-result pass, raw report 6개 생성.
- `--summarize-baseline`: source-report-count 6, hard-passed true, warning-count 0.
- `--summarize-baseline-history`: session-count 1, hard-passed true, warning-count 0.
- `summary.json`: `runner-id=local-win-x64-01`, `runner-kind=local`, `comparison-compatible=true`,
  unknown runner 0, mismatch 0.
- runner artifact local absolute path 검색 결과 없음.
- `Hps.Benchmarks.Tests`: 67개 통과, 실패 0.
- `git diff --check`: exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 269개 통과, 실패 0.

## 2026-06-24 (Codex - explicit runner baseline storage policy)

### 작업 단위
- D083 이후 explicit runner baseline 저장 구조와 수집 정책을 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-24-explicit-runner-baseline-storage-policy-design.md`:
  runner group 아래 date/session 구조, runner id naming guide, history 입력 규칙, index 운영 정책을 작성했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D084를 추가했다.
- `docs/benchmarks/baselines/index.md`:
  명시 runner baseline 운영 원칙과 runner group 섹션을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 진입점을 첫 explicit runner baseline 수집으로 갱신했다.

### 검증
- D079/D080/D082/D083과 `BaselineHistoryReader` directory 규칙을 대조했다.
- 신규 설계/결정/index 문서 임시 표기 검색 결과 없음.
- `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

## 2026-06-24 (Codex - Phase 4 next candidate reassessment)

### 작업 단위
- D082 이후 Phase 4 다음 실행 후보를 재평가하고, 다음 단일 작업 단위를 explicit runner baseline 저장 구조 설계로 선정했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-24-phase4-next-candidate-reassessment.md`:
  후보 A~E를 비교하고, 기존 date root 에 explicit runner session 을 바로 섞지 않는 이유를 기록했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D083을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 진입점을 explicit runner baseline 저장 구조와 수집 정책 설계로 갱신했다.

### 검증
- D082/D079/D080, `BaselineHistoryReader` directory 규칙, `.claude/review/review-status-2026-06-18.md`의 남은 비차단 후속을 대조했다.
- 신규 설계/결정 문서 임시 표기 검색 결과 없음.
- `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

## 2026-06-24 (Codex - latency envelope design review response)

### 작업 단위
- D082 latency envelope/gate 보류 설계 리뷰의 Low 명확성 의견을 문서 batch 로 반영했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-24-latency-envelope-and-gate-deferral-design.md`:
  envelope 집계 방식, `local-unspecified` 표본의 gate 승격 표본 제외, envelope 초과 기록의 수동 리뷰 메모 성격을 명시했다.
- `docs/benchmarks/baselines/index.md`:
  reference envelope 표와 해석 메모에 같은 기준을 추가했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D082 판단 기준에 `local-unspecified` baseline 은 reference 전용이라는 문구를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  리뷰 반영 완료 상태와 다음 Phase 4 후보 재평가 진입점을 반영했다.

### 검증
- D082 review finding 1/2와 info 3 반영 여부를 대조했다.
- 신규 설계/결정/index 문서 임시 표기 검색 결과 없음.
- `git diff --check` 통과. whitespace 오류 없음.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 269개 통과/실패 0.

## 2026-06-24 (Codex - latency envelope and gate deferral design)

### 작업 단위
- 2026-06-24 compatible baseline 3개를 근거로 reference latency envelope 를 재산정하고, warning-as-failure/CI latency gate 보류 조건을 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-24-latency-envelope-and-gate-deferral-design.md`:
  D082 설계 문서를 추가했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D082를 추가했다.
- `docs/benchmarks/baselines/index.md`:
  2026-06-24 reference latency envelope 표와 해석 메모를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  이번 설계 완료 상태와 다음 검토 진입점을 반영했다.

### 검증
- 2026-06-24 `history.json`과 `session-01`/`session-02`/`session-03` `summary.json` 수치를 대조했다.
- 신규 설계/결정/index 문서 임시 표기 검색 결과 없음.
- `git diff --check` 통과. whitespace 오류 없음.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 269개 통과/실패 0.

## 2026-06-24 (Codex - document work batching rule)

### 작업 단위
- 문서 전용 작업은 관련 문서를 한 번에 정렬하고, 코드/테스트 구현 작업은 계속 작은 기능 단위로 유지한다는 실행 규칙을 명시했다.

### 변경 내용
- `AGENT_RULES.md`: 문서 전용 batch 예외와 코드/테스트 변경 분리 경계를 추가했다.
- `DECISIONS.md`: D081을 추가하고 현재 판단 기준에 문서 전용 작업 정렬 원칙을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 이번 문서 규칙 단위 완료 상태와 다음 작업 진입점을 유지했다.

### 검증
- 관련 root 문서에서 `문서 전용`, `D081`, `coherent documentation cycle` 용어 정합성을 대조했다.
- `git diff --check` 통과. whitespace 오류 없음.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 269개 통과/실패 0.

## 2026-06-24 (Codex - current schema baseline session-03)

### 작업 단위
- D079/D080 이후 schema 로 2026-06-24 세 번째 compatible baseline session 을 생성했다.
- 이 단위로 2026-06-24 date root 는 latency envelope 재산정 검토에 필요한 동일 runner compatible session 3개를 갖게 됐다.

### 변경 내용
- `docs/benchmarks/baselines/2026-06-24/session-03/`:
  `load-01..03.json`, `open-loop-01..03.json` raw report 6개를 생성했다.
- `docs/benchmarks/baselines/2026-06-24/session-03/summary.json`, `summary.md`:
  current schema summary artifact 를 생성했다.
- `docs/benchmarks/baselines/2026-06-24/history.json`, `history.md`:
  2026-06-24 date-level history artifact 를 3개 session 기준으로 재생성했다.
- `docs/benchmarks/baselines/index.md`:
  2026-06-24 history session count 와 `session-03` row 를 갱신했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 이번 artifact 단위 완료 상태와 다음 latency envelope 재산정 정책 설계 지점을 반영했다.

### 검증
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --baseline-suite docs\benchmarks\baselines\2026-06-24\session-03 --runs 3`
  결과: baseline-suite-result pass, raw report 6개 생성.
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build -- --summarize-baseline docs\benchmarks\baselines\2026-06-24\session-03 --summary docs\benchmarks\baselines\2026-06-24\session-03\summary.json --summary-md docs\benchmarks\baselines\2026-06-24\session-03\summary.md`
  결과: source-report-count 6, hard-passed true, warning-count 0.
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build -- --summarize-baseline-history docs\benchmarks\baselines\2026-06-24 --history docs\benchmarks\baselines\2026-06-24\history.json --history-md docs\benchmarks\baselines\2026-06-24\history.md`
  결과: session-count 3, hard-passed true, warning-count 0.
- `summary.json` 확인 결과: `comparison-compatible=true`, `unknown-runner-count=0`, `comparison-mismatch-count=0`.
- `docs/benchmarks/baselines/2026-06-24` 아래 local absolute path 검색은 매칭 없음.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 67개 통과/실패 0.
- `git diff --check` exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 269개 통과/실패 0.

## 2026-06-24 (Codex - current schema baseline session-02)

### 작업 단위
- D079/D080 이후 schema 로 2026-06-24 두 번째 compatible baseline session 을 생성했다.
- 이 단위는 새 기능 구현이 아니라, latency envelope 재산정 전 필요한 동일 runner baseline 표본을 하나 더 쌓는 artifact 수집이다.

### 변경 내용
- `docs/benchmarks/baselines/2026-06-24/session-02/`:
  `load-01..03.json`, `open-loop-01..03.json` raw report 6개를 생성했다.
- `docs/benchmarks/baselines/2026-06-24/session-02/summary.json`, `summary.md`:
  current schema summary artifact 를 생성했다.
- `docs/benchmarks/baselines/2026-06-24/history.json`, `history.md`:
  2026-06-24 date-level history artifact 를 2개 session 기준으로 재생성했다.
- `docs/benchmarks/baselines/index.md`:
  2026-06-24 history session count 와 `session-02` row 를 갱신했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 이번 artifact 단위 완료 상태와 다음 `session-03` 수집 지점을 반영했다.

### 검증
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --baseline-suite docs\benchmarks\baselines\2026-06-24\session-02 --runs 3`
  결과: baseline-suite-result pass, raw report 6개 생성.
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build -- --summarize-baseline docs\benchmarks\baselines\2026-06-24\session-02 --summary docs\benchmarks\baselines\2026-06-24\session-02\summary.json --summary-md docs\benchmarks\baselines\2026-06-24\session-02\summary.md`
  결과: source-report-count 6, hard-passed true, warning-count 0.
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build -- --summarize-baseline-history docs\benchmarks\baselines\2026-06-24 --history docs\benchmarks\baselines\2026-06-24\history.json --history-md docs\benchmarks\baselines\2026-06-24\history.md`
  결과: session-count 2, hard-passed true, warning-count 0.
- `summary.json` 확인 결과: `comparison-compatible=true`, `unknown-runner-count=0`, `comparison-mismatch-count=0`.
- `docs/benchmarks/baselines/2026-06-24` 아래 local absolute path 검색은 매칭 없음.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 67개 통과/실패 0.
- `git diff --check` exit 0.
- 첫 solution build 는 직전 testhost 파일 잠금으로 MSB3026 warning 이 있었고, testhost 종료 후 재실행한
  `dotnet build HighPerformanceSocket.slnx --no-restore`는 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 269개 통과/실패 0.

## 2026-06-24 (Codex - current schema baseline session)

### 작업 단위
- D079/D080 이후 schema 로 새 baseline session 을 실제 생성했다.
- 기존 2026-06-18 legacy raw report 는 수정하지 않고, 2026-06-24 date root 에 새 session 을 분리했다.

### 변경 내용
- `docs/benchmarks/baselines/2026-06-24/session-01/`:
  `load-01..03.json`, `open-loop-01..03.json` raw report 6개를 생성했다.
- `docs/benchmarks/baselines/2026-06-24/session-01/summary.json`, `summary.md`:
  current schema summary artifact 를 생성했다.
- `docs/benchmarks/baselines/2026-06-24/history.json`, `history.md`:
  2026-06-24 date-level history artifact 를 생성했다.
- `docs/benchmarks/baselines/index.md`:
  2026-06-24 history row 와 session row 를 추가하고, D079 metadata 이후 첫 comparison-compatible baseline 임을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 이번 artifact 단위 완료 상태와 다음 실행 지점을 반영했다.

### 검증
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --baseline-suite docs\benchmarks\baselines\2026-06-24\session-01 --runs 3`
  결과: baseline-suite-result pass, raw report 6개 생성.
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build -- --summarize-baseline docs\benchmarks\baselines\2026-06-24\session-01 --summary docs\benchmarks\baselines\2026-06-24\session-01\summary.json --summary-md docs\benchmarks\baselines\2026-06-24\session-01\summary.md`
  결과: source-report-count 6, hard-passed true, warning-count 0.
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build -- --summarize-baseline-history docs\benchmarks\baselines\2026-06-24 --history docs\benchmarks\baselines\2026-06-24\history.json --history-md docs\benchmarks\baselines\2026-06-24\history.md`
  결과: session-count 1, hard-passed true, warning-count 0.
- `summary.json` 확인 결과: `comparison-compatible=true`, `unknown-runner-count=0`, `comparison-mismatch-count=0`.
- `docs/benchmarks/baselines/2026-06-24` 아래 local absolute path 검색은 매칭 없음.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 67개 통과/실패 0.
- `git diff --check` exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 269개 통과/실패 0.

## 2026-06-24 (Codex - baseline generated artifact refresh)

### 작업 단위
- 2026-06-18 baseline 의 파생 summary/history artifact 를 현재 D079/D080 schema 로 재생성했다.
- raw 측정 JSON은 원본 artifact 로 보존하고, summary/history 산출물과 index 해석만 갱신했다.
- 재생성 중 발견한 local absolute `source-path` 출력은 reader 단계에서 입력 directory 기준 상대 경로로 보정했다.

### 변경 내용
- `tests/Hps.Benchmarks/BaselineReportReader.cs`:
  `BaselineReport.SourcePath`를 `ReadDirectory(...)` 입력 directory 기준 상대 경로로 보존하게 했다.
- `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`:
  reader 가 local absolute path 대신 상대 source path 를 반환하는지 검증했다.
- `docs/benchmarks/baselines/2026-06-18/summary.json`, `summary.md`:
  root session summary 를 현재 schema 로 재생성해 comparison field 를 포함했다.
- `docs/benchmarks/baselines/2026-06-18/session-02/summary.json`, `summary.md`,
  `docs/benchmarks/baselines/2026-06-18/session-03/summary.json`, `summary.md`:
  session summary artifact 를 현재 schema 로 재생성했다.
- `docs/benchmarks/baselines/2026-06-18/history.json`, `history.md`:
  세 session 을 묶는 date-level history artifact 를 새로 생성했다.
- `docs/benchmarks/baselines/index.md`:
  date-level history 링크와 D079 이전 raw report 의 `unknown-runner` comparison mismatch 해석을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`: artifact 재생성 완료 상태와 다음 실행 지점을 반영했다.

### 검증
- Red: 기존 reader 에서 `ReadDirectory_WhenRunReportIsRead_UsesRelativeSourcePath`가
  `Expected: "load-01.json"` / `Actual: "C:/Users/ADMIN/.../load-01.json"` assertion failure 로 실패함을 확인했다.
- Green: reader 를 상대 path 기준으로 보정한 뒤 focused test 1개 통과.
- root summary CLI: source-report-count 6, hard-passed true, warning-count 0.
- session-02 summary CLI: source-report-count 6, hard-passed true, warning-count 0.
- session-03 summary CLI: source-report-count 6, hard-passed true, warning-count 0.
- history CLI: session-count 3, hard-passed true, warning-count 0.
- `docs/benchmarks/baselines/2026-06-18` 아래 local absolute path 검색(`D:/`, `D:\`, `C:/`, `C:\Users`)은 매칭 없음.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 67개 통과/실패 0.
- `git diff --check` exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 269개 통과/실패 0.

## 2026-06-24 (Codex - benchmark writer metadata roundtrip test hardening)

### 작업 단위
- `TODOS.md`에 남아 있던 P3_NICE benchmark writer metadata roundtrip test gap 을 해소했다.
- 기능 동작 변경은 없고, writer/reader schema drift 를 더 빨리 잡는 테스트만 보강했다.

### 변경 내용
- `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`:
  `TcpLoopbackReportWriter.Write(...)`가 만든 raw report 를 `BaselineReportReader.ReadDirectory(...)`로 다시 읽어
  runner/environment metadata 전체가 roundtrip 되는지 검증했다.
- test identity 는 `os-architecture=Arm64`, `process-architecture=X64`를 서로 다르게 둬 두 field 가 누락되거나
  잘못된 key 로 기록되는 회귀를 구분해서 잡는다.
- `CURRENT_PLAN.md`, `TODOS.md`: deferred test-hardening 항목 완료와 다음 실행 지점을 반영했다.

### 검증
- Red: `TcpLoopbackReportWriter`의 `process-architecture` field 이름을 임시로 바꿨을 때
  `Write_WhenRunResultIsReadBack_PreservesFullRunnerIdentityMetadata`가
  `Expected: "X64", Actual: "unknown"` assertion failure 로 실패함을 확인했다.
- Green: 임시 mutation 을 되돌린 뒤 focused roundtrip test 1개 통과.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 66개 통과/실패 0.
- `git diff --check` exit 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 268개 통과/실패 0.

## 2026-06-24 (Codex - summary/history comparison signal review hardening)

### 작업 단위
- `.claude/review/2026-06-24-summary-history-comparison-signal-plan-review.md`의 High/Medium 지적을 현재 구현과 대조하고,
  test-hardening 과 판정 술어 문서화로 반영했다.
- 기능 동작 변경은 없고, 이미 구현된 null-key/unknown-runner 경로를 회귀 테스트로 고정했다.

### 변경 내용
- `tests/Hps.Benchmarks.Tests/BaselineSummaryMarkdownWriterTests.cs`:
  legacy/unknown identity summary 에서 `Comparison.Key == null`이어도 Markdown 이 NRE 없이
  `comparison-key: 없음`, `unknown-runner-count`, mismatch row 를 쓰는지 검증했다.
- `tests/Hps.Benchmarks.Tests/BaselineSummaryGeneratorTests.cs`:
  hard comparison identity field 일부만 `unknown`인 report 도 `unknown-runner`로 격리하는지 검증했다.
- `docs/superpowers/plans/2026-06-24-summary-history-comparison-signal.md`:
  partial unknown 판정 기준과 null-key Markdown test/출력 규칙을 보강했다.
- `DECISIONS.md`: hard comparison identity field 중 하나라도 `unknown`이면 compatible 로 추정하지 않는다고 명시했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 리뷰 보강 완료 상태와 검증 근거를 반영했다.

### 검증
- Red 1: `BaselineSummaryMarkdownWriter`의 null-key guard 를 임시 제거했을 때
  `Write_WhenComparisonKeyIsNull_WritesNullKeyAndUnknownRunnerMismatch`가 `NullReferenceException`으로 실패함을 확인했다.
- Red 2: `BaselineSummaryGenerator.IsUnknownIdentity(...)`를 benchmark-profile-only 판정으로 임시 약화했을 때
  `Generate_WhenIdentityHasPartialUnknownField_MarksComparisonIncompatible`가 `Assert.False()` failure 로 실패함을 확인했다.
- Green: 위 임시 mutation 을 되돌린 뒤 focused 보강 tests 2개 통과.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 65개 통과/실패 0.

## 2026-06-24 (Codex - summary/history comparison signal Task 5)

### 작업 단위
- D080 구현 계획의 마지막 단위로 history JSON/Markdown output 과 CLI smoke 에 comparison signal 을 연결했다.
- summary/history comparison signal Task 1~5 구현은 이 단위로 완료됐다.

### 변경 내용
- `tests/Hps.Benchmarks/BaselineHistoryWriter.cs`: history JSON top-level 에 comparison-compatible/key/mismatch field 를 쓰고,
  session entry 에 comparison-compatible, unknown-runner-count, comparison-mismatch-count 를 기록한다.
- `tests/Hps.Benchmarks/BaselineHistoryMarkdownWriter.cs`: history Markdown 에 `## Comparison` section,
  기준 key 요약, workload case table, mismatch table 을 출력한다.
- `tests/Hps.Benchmarks.Tests/BaselineHistoryGeneratorWriterTests.cs`: history JSON comparison field 와
  Markdown comparison section 을 검증했다.
- `tests/Hps.Benchmarks.Tests/BaselineHistoryProgramTests.cs`: runner mismatch-only history 가 hard gate success exit code 를
  유지하면서 comparison mismatch field 를 쓰는지 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 1~5 완료와 현재 실행 작업 없음 상태를 반영했다.

### 검증
- Red 1: history JSON writer test 가 comparison field 부재로 `KeyNotFoundException`을 냄을 확인했다.
- Red 2: Markdown writer test 가 `## Comparison` section 부재로 `Assert.Contains()` 실패함을 확인했다.
- Red 3: Program smoke test 가 history JSON comparison field 부재로 `KeyNotFoundException`을 냄을 확인했다.
- Green: focused Task 5 tests 3개 통과.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 63개 통과/실패 0.

## 2026-06-24 (Codex - summary/history comparison signal Task 4)

### 작업 단위
- D080 구현 계획의 네 번째 단위로 history reader/model/generator 가 comparison signal 을 보존·집계하게 했다.
- history JSON/Markdown output 과 CLI smoke 는 다음 Task 로 분리했다.

### 변경 내용
- `tests/Hps.Benchmarks/BaselineHistorySession.cs`: session 단위 `Comparison` property 를 추가했다.
- `tests/Hps.Benchmarks/BaselineHistory.cs`: history aggregate 단위 `Comparison` property 를 추가했다.
- `tests/Hps.Benchmarks/BaselineHistoryReader.cs`: summary JSON의 comparison field/key/mismatch 를 읽고,
  comparison field 가 없는 legacy summary 는 `legacy-summary-without-comparison` mismatch 로 변환한다.
- `tests/Hps.Benchmarks/BaselineHistoryGenerator.cs`: session comparison key 를 비교해 history-level compatible 여부와
  `history-comparison-key-mismatch`를 계산한다. 기존 hard gate/warning-count 계산은 변경하지 않았다.
- `tests/Hps.Benchmarks.Tests/BaselineHistoryReaderTests.cs`: comparison property contract, summary comparison read,
  legacy summary fallback 을 검증했다.
- `tests/Hps.Benchmarks.Tests/BaselineHistoryGeneratorWriterTests.cs`: compatible sessions, key mismatch,
  incompatible session aggregate 를 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 4 완료와 다음 Task 5 history output/CLI smoke 진입점을 반영했다.

### 검증
- Red 1: `BaselineHistorySession.Comparison`, `BaselineHistory.Comparison` property 부재로 contract tests 2개가
  `Assert.NotNull()` 실패함을 확인했다.
- Contract Green: model property 추가 후 focused contract tests 2개 통과.
- Red 2: reader/generator behavior tests 5개가 stub comparison 에서 `Assert.True()`/`Assert.Single()` 실패함을 확인했다.
- Green 2: focused history reader/generator tests 12개 통과.

## 2026-06-24 (Codex - summary/history comparison signal Task 3)

### 작업 단위
- D080 구현 계획의 세 번째 단위로 summary JSON/Markdown output 에 comparison signal 을 기록했다.
- history reader/generator aggregate 와 history output 은 다음 Task 로 분리했다.

### 변경 내용
- `tests/Hps.Benchmarks/BaselineSummaryWriter.cs`: summary JSON top-level 에 `comparison-compatible`,
  `comparison-key`, `unknown-runner-count`, `comparison-mismatch-count`, `comparison-mismatches`를 기록한다.
- `tests/Hps.Benchmarks/BaselineSummaryMarkdownWriter.cs`: 사람이 runner/case 기준과 mismatch 를 바로 볼 수 있도록
  `## Comparison` section 과 workload case table 을 출력한다.
- `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`: JSON writer comparison field shape 를 검증했다.
- `tests/Hps.Benchmarks.Tests/BaselineSummaryMarkdownWriterTests.cs`: Markdown comparison section 출력과 핵심 key field 를 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 3 완료와 다음 Task 4 history reader/generator 진입점을 반영했다.

### 검증
- Red 1: summary JSON writer test 가 `comparison-compatible` field 부재로 `KeyNotFoundException`을 냄을 확인했다.
- Green 1: focused JSON writer test 1개 통과.
- Red 2: Markdown writer test 가 `## Comparison` section 부재로 `Assert.Contains()` 실패함을 확인했다.
- Green 2: focused Markdown writer tests 3개 통과.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 53개 통과/실패 0.

## 2026-06-24 (Codex - summary/history comparison signal Task 2)

### 작업 단위
- D080 구현 계획의 두 번째 단위로 summary comparison model/generator 를 추가했다.
- summary JSON/Markdown output 과 history aggregation 은 다음 Task 로 분리했다.

### 변경 내용
- `tests/Hps.Benchmarks/BaselineComparisonCase.cs`: `result-name`별 scenario/payload/target case model 을 추가했다.
- `tests/Hps.Benchmarks/BaselineComparisonKey.cs`: runner/environment key 와 case 목록 model 을 추가했다.
- `tests/Hps.Benchmarks/BaselineComparisonMismatch.cs`: summary/history 공용 mismatch entry model 을 추가했다.
- `tests/Hps.Benchmarks/BaselineComparisonResult.cs`: compatible 여부, key, unknown runner count, mismatch 목록 aggregate model 을 추가했다.
- `tests/Hps.Benchmarks/BaselineSummary.cs`: `Comparison` property 를 추가했다.
- `tests/Hps.Benchmarks/BaselineSummaryGenerator.cs`: source report 목록에서 no-source, unknown-runner,
  runner/case mismatch, compatible key 를 계산한다. `processor-count`는 comparison key 에 포함하지 않는다.
- `tests/Hps.Benchmarks.Tests/BaselineSummaryGeneratorTests.cs`: compatible, unknown identity, runner mismatch,
  empty report comparison behavior 를 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 2 완료와 다음 Task 3 summary output 진입점을 반영했다.

### 검증
- Red 1: `BaselineSummary.Comparison` property 부재로 contract test 가 `Assert.NotNull()` 실패함을 확인했다.
- Contract Green: comparison model stubs 와 summary property 추가 후 focused contract test 1개 통과.
- Red 2: compatible behavior test 가 stub comparison 에서 `Expected: True, Actual: False`로 실패함을 확인했다.
- Green: focused `BaselineSummaryGeneratorTests` 8개 통과.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 51개 통과/실패 0.

## 2026-06-24 (Codex - summary/history comparison signal Task 1)

### 작업 단위
- D080 구현 계획의 첫 번째 단위로 raw report payload/target settings 를 `BaselineReport`까지 전파했다.
- summary comparison model/generator 와 JSON/Markdown 출력은 다음 Task 로 분리했다.

### 변경 내용
- `tests/Hps.Benchmarks/BaselineReport.cs`: `PayloadBytes`, `TargetRateHz`, `TargetDurationSeconds` property 를 추가했다.
- `tests/Hps.Benchmarks/BaselineReportReader.cs`: raw report 의 `payload-bytes`, `target-rate-hz`,
  `target-duration-seconds` field 를 읽어 `BaselineReport` 생성자로 전달한다.
- `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`: payload/target property contract 와 reader behavior test 를 추가했다.
- `tests/Hps.Benchmarks.Tests/BaselineSummaryGeneratorTests.cs`,
  `tests/Hps.Benchmarks.Tests/BaselineSummaryMarkdownWriterTests.cs`: direct `BaselineReport` helper 생성자 호출에
  현재 benchmark 기본값 `4096`, `100.0`, `30`을 명시했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 1 완료와 다음 Task 2 summary comparison model/generator 진입점을 반영했다.

### 검증
- Red 1: focused contract test 가 `BaselineReport` payload/target property 부재로 `Assert.NotNull()` 실패함을 확인했다.
- Contract Green: property surface 추가 후 focused contract test 1개 통과.
- Red 2: reader behavior test 가 `Expected: 4096, Actual: 0`으로 실패함을 확인했다.
- Green: focused reader behavior test 1개 통과.
- Refactor 검증: focused `BaselineReportReaderWriterTests` 8개 통과, focused `BaselineSummary*` 6개 통과.
  같은 csproj 대상 focused tests 를 병렬 실행했을 때 DLL lock 이 발생해, 두 번째 focused test 는 `--no-build --no-restore`로 순차 재실행했다.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 46개 통과/실패 0.

## 2026-06-24 (Codex - summary/history comparison signal implementation plan)

### 작업 단위
- D080 summary/history comparison signal 설계를 실제 구현 가능한 5개 작은 커밋 단위로 분해했다.
- 코드 구현과 generated baseline artifact 재생성은 이번 범위에서 제외했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-24-summary-history-comparison-signal.md`:
  `BaselineReport` payload/target settings, summary comparison model/generator, summary output,
  history reader/generator, history output/CLI smoke Task 를 작성했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 실행 지점을 Task 1 `BaselineReport` payload/target settings 구현으로 갱신했다.

### 검증
- D080 설계와 현재 `BaselineReport`, `BaselineReportReader`, `BaselineSummary*`, `BaselineHistory*`,
  관련 benchmark tests 구조를 대조했다.
- 각 Task 의 touched files, assertion-failure Red, focused test, 커밋 경계, 테스트 주석 요구를 계획에 명시했다.
- 전체 repository 검증은 계획 문서 placeholder scan, `git diff --check`, solution build/test 로 수행한다.

## 2026-06-23 (Codex - summary/history comparison signal design)

### 작업 단위
- D079 raw report metadata 이후 summary/history 가 비교 가능성을 어떻게 보존·표현할지 설계했다.
- 코드 구현, generated baseline artifact 재생성, warning-as-failure 정책은 이번 범위에서 제외했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-23-summary-history-comparison-signal-design.md`:
  summary/history comparison signal schema, mismatch 표현, Markdown 출력, 후속 구현 단위를 정리했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`: D080을 추가했다.
  comparison signal 은 hard gate, 기존 `warning-count`, CLI exit code 와 분리된 non-failing compatibility artifact 로 둔다.
- `CURRENT_PLAN.md`, `TODOS.md`: 설계 완료와 다음 구현 계획 작성 진입점을 반영했다.

### 검증
- current `BaselineReport`, `BaselineSummary*`, `BaselineHistory*`, D079 raw writer/reader 구조를 대조했다.
- summary 안의 `load`와 `open-loop`이 서로 다른 `scenario`를 가질 수 있어, comparison key 를 단일 scenario 가 아니라
  `result-name`별 `cases` 배열로 설계했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 246개 통과/실패 0.

## 2026-06-23 (Codex - benchmark runner identity implementation review)

### 작업 단위
- benchmark runner identity Task 1~3 구현을 D079 설계와 구현 계획 기준으로 검토했다.

### 변경 내용
- `docs/agent-state/reviews/2026-06-23-benchmark-runner-identity-implementation-review.md`:
  구현 검토 결과, Minor testing 관찰, deferred item, unresolved decision 을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 구현 검토 완료와 다음 summary/history comparison signal 설계 진입점을 반영했다.

### 검증
- D079 raw metadata field, privacy 기본값, writer/reader field name, legacy fallback, focused tests 를 소스와 문서로 대조했다.
- 새 Blocker/Major finding 은 없다.
- Minor testing 관찰: writer shape test 가 실제 writer output 의 architecture field 2개를 직접 assert하지 않아,
  future field drift 를 더 강하게 잡으려면 writer-to-reader roundtrip test 가 유용하다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 246개 통과/실패 0.

## 2026-06-23 (Codex - benchmark runner identity Task 3 raw report reader)

### 작업 단위
- benchmark runner identity 구현 계획의 세 번째 단위로 raw report reader 와 legacy compatibility 를 연결했다.

### 변경 내용
- `tests/Hps.Benchmarks/BaselineReport.cs`: raw report reader 결과가 `BenchmarkRunIdentity`를 보존하도록 `Identity` property 를 추가했다.
  metadata 가 없는 경우 기본값은 `BenchmarkRunIdentity.Unknown`이다.
- `tests/Hps.Benchmarks/BaselineReportReader.cs`: 신규 raw report 의 runner/environment metadata 를 optional field 로 읽고,
  legacy raw report 는 `Unknown` identity 로 유지한다.
- `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`: `BaselineReport.Identity` contract, metadata read,
  legacy fallback 을 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 3 완료와 다음 구현 검토 진입점을 반영했다.

### 검증
- Red 1: `BaselineReport.Identity` property 부재로 focused contract test 가 `Assert.NotNull()` 실패함을 확인했다.
- Contract Green: `BaselineReport.Identity` 추가 후 focused contract test 1개 통과.
- Red 2: metadata 포함 raw report reader test 가 `Expected: tcp-loopback-saea-v1, Actual: unknown`으로 실패함을 확인했다.
- Green: focused `BaselineReportReaderWriterTests` 6개 통과.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 44개 통과/실패 0.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 246개 통과/실패 0.

## 2026-06-23 (Codex - benchmark runner identity Task 2 raw report writer metadata)

### 작업 단위
- benchmark runner identity 구현 계획의 두 번째 단위로 raw report writer metadata 를 연결했다.

### 변경 내용
- `tests/Hps.Benchmarks/TcpLoopbackRunResult.cs`: optional `BenchmarkRunIdentity`를 보존하는 `Identity` property 를 추가했다.
  명시 identity 가 없으면 privacy 우선 `BenchmarkRunIdentity.CaptureDefault()`를 사용한다.
- `tests/Hps.Benchmarks/TcpLoopbackReportWriter.cs`: raw report schema v1 top-level 에 runner/environment metadata field 를 additive 로 기록한다.
- `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`: writer output 이 identity metadata 를 포함하는지 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 2 완료와 다음 Task 3(raw report reader/legacy compatibility) 진입점을 반영했다.

### 검증
- Red: focused writer metadata test 가 `benchmark-profile` 미기록으로 `Assert.True()` 실패함을 확인했다.
- Green: focused writer metadata test 1개 통과.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 41개 통과/실패 0.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 243개 통과/실패 0.

## 2026-06-23 (Codex - benchmark runner identity Task 1 model)

### 작업 단위
- benchmark runner identity 구현 계획의 첫 번째 단위로 `BenchmarkRunIdentity` model 을 추가했다.

### 변경 내용
- `tests/Hps.Benchmarks/BenchmarkRunIdentity.cs`: raw report metadata 에 사용할 benchmark profile, runner id/kind,
  transport backend, runtime OS/framework/architecture 정보를 보존하는 내부 model 을 추가했다.
- `tests/Hps.Benchmarks.Tests/BenchmarkRunIdentityTests.cs`: 타입 계약, privacy 우선 기본값, 환경 변수 override 를 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 1 완료와 다음 Task 2(raw report writer metadata) 진입점을 반영했다.

### 검증
- Red 1: focused contract test 가 타입 부재로 `Assert.NotNull()` 실패함을 확인했다.
- Stub Green: stub type 추가 후 focused contract test 1개 통과.
- Red 2: behavior tests 2개가 stub `unknown` 반환으로 실패함을 확인했다.
- Green: focused `BenchmarkRunIdentityTests` 3개 통과.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore` 통과, 40개 통과/실패 0.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 242개 통과/실패 0.

## 2026-06-23 (Codex - benchmark runner identity implementation plan)

### 작업 단위
- D079 benchmark runner identity 설계를 구현 가능한 3개 커밋 단위로 분해했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-23-benchmark-runner-identity.md`: identity model, raw report writer metadata,
  raw report reader/legacy compatibility Task 를 Red-Green-Refactor 경로와 커밋 단위로 작성했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 실행 지점을 Task 1 `BenchmarkRunIdentity` model 구현으로 갱신했다.

### 검증
- D079 설계 문서와 실제 `tests/Hps.Benchmarks` writer/reader/source model, 기존 benchmark test 패턴을 대조했다.
- 계획 self-review 로 D079 coverage, type consistency, commit boundary 를 확인했다.
- 계획 문서 placeholder scan 결과 없음.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 239개 통과/실패 0.

## 2026-06-23 (Codex - benchmark runner identity design)

### 작업 단위
- baseline history command 이후 남은 Phase 4 backlog 를 재평가하고, 다음 구현 후보를 benchmark runner identity/environment metadata 로 좁혔다.

### 변경 내용
- `docs/superpowers/specs/2026-06-23-benchmark-runner-identity-design.md`: raw report schema v1 additive metadata, privacy 우선 기본값,
  summary/history comparison signal 방향, 범위 밖 항목을 정리했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`: D079를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`: backlog 재평가 완료와 다음 구현 계획 진입점을 반영했다.

### 검증
- `PLAN.md`, `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`, baseline 관련 spec/review 와 benchmark writer/reader/source model 을 대조했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 239개 통과/실패 0.

## 2026-06-23 (Codex - baseline history command implementation review)

### 작업 단위
- baseline history report command Task 1~4 전체 구현을 D078 계약과 대조해 검토했다.

### 변경 내용
- `docs/agent-state/reviews/2026-06-23-baseline-history-command-implementation-review.md`: parser, reader, aggregate writer,
  Program wiring, tests, 실제 baseline root CLI smoke 를 기준으로 구현 검토 결과를 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 구현 검토 완료와 다음 Phase 4 backlog 재평가/설계 진입점을 반영했다.

### 검증
- 실제 CLI smoke 로 `--summarize-baseline-history docs\benchmarks\baselines` 실행 결과 `session-count: 3`,
  `hard-passed: true`, `warning-count: 0`을 확인했다.
- 생성 JSON에서 `history-version: 1`, `failed-session-count: 0`, `/` separator relative summary path 를 확인했다.
- 생성 Markdown은 `Get-Content -Encoding UTF8` 기준 한글 header 와 session table 이 정상 표시됨을 확인했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 239개 통과/실패 0.

## 2026-06-23 (Codex - baseline history report command Task 4 Program wiring)

### 작업 단위
- baseline history report command 의 네 번째 구현 단위로 `Program.Main` 실행 경로와 CLI smoke coverage 를 추가했다.

### 변경 내용
- `tests/Hps.Benchmarks/Program.cs`: `BenchmarkCommand.SummarizeBaselineHistory` branch 를 추가하고,
  `BaselineHistoryReader` → `BaselineHistoryGenerator` → `BaselineHistoryWriter`/`BaselineHistoryMarkdownWriter` 경로를 연결했다.
- `tests/Hps.Benchmarks.Tests/BaselineHistoryProgramTests.cs`: passing summary, failed summary, warning-only summary 의
  CLI exit code 와 artifact 생성을 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 4 완료와 다음 구현 검토 게이트를 반영했다.

### 검증
- Red: `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineHistoryProgramTests`
  에서 Program tests 3개가 usage error exit code 2 반환으로 실패함을 확인했다.
- Green: 같은 focused Program tests 3개 통과.
- CLI smoke: 첫 `dotnet run`은 restore 네트워크 접근 때문에 sandbox 에서 실패했고,
  `dotnet run --no-build --no-restore --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --summarize-baseline-history docs\benchmarks\baselines ...`
  로 재실행해 session-count 3, hard-passed true, warning-count 0 출력을 확인했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
  비고: Benchmark 프로젝트 assets 가 sandbox package folder 를 가리켜 처음에는 실패했으므로,
  로컬 NuGet cache 를 `--source`/`--packages`로 명시한 restore 후 재검증했다.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 239개 통과/실패 0.

## 2026-06-23 (Codex - baseline history report command Task 3 history writer)

### 작업 단위
- baseline history report command 의 세 번째 구현 단위로 history aggregate 와 JSON/Markdown writer 를 추가했다.

### 변경 내용
- `tests/Hps.Benchmarks/BaselineHistory.cs`: history root aggregate model 을 추가했다.
- `tests/Hps.Benchmarks/BaselineHistoryGenerator.cs`: session `hard-passed` AND, `failed-session-count`, warning count 집계를 구현했다.
- `tests/Hps.Benchmarks/BaselineHistoryWriter.cs`: stable JSON schema writer 를 추가했다.
- `tests/Hps.Benchmarks/BaselineHistoryMarkdownWriter.cs`: 사람이 읽는 session table/warning session list writer 를 추가했다.
- `tests/Hps.Benchmarks.Tests/BaselineHistoryGeneratorWriterTests.cs`: aggregate count, zero raw failure hard fail, JSON shape, null p99, Markdown table 을 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 3 완료와 다음 Task 4(Program wiring/smoke) 진입점을 반영했다.

### 검증
- Red 1: focused generator/writer contract test 에서 `BaselineHistoryGenerator` 타입 미존재로 `Assert.NotNull()` 실패를 확인했다.
- Stub Green: aggregate/writer stub 추가 후 focused contract test 1개 통과.
- Red 2: behavior tests 5개가 aggregate/writer stub 에서 실패함을 확인했다.
- Green: focused generator/writer tests 5개 통과.
- `git diff --check` 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 236개 통과/실패 0.

## 2026-06-23 (Codex - baseline history report command Task 2 history reader)

### 작업 단위
- baseline history report command 의 두 번째 구현 단위로 session domain model 과 summary reader 를 추가했다.

### 변경 내용
- `tests/Hps.Benchmarks/BaselineHistorySession.cs`: history session 의 date/session/path/count/pass/warning/p99/HWM 값을 보존하는 immutable model 을 추가했다.
- `tests/Hps.Benchmarks/BaselineHistoryReader.cs`: date root 와 parent baseline root 를 bounded discovery 로 읽고, summary JSON schema v1을 `BaselineHistorySession`으로 변환한다.
- `tests/Hps.Benchmarks.Tests/BaselineHistoryReaderTests.cs`: date root, parent root, by-kind 누락, summary 없음 경계를 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 2 완료와 다음 Task 3(history aggregate/writer) 진입점을 반영했다.

### 검증
- Red 1: focused reader contract test 에서 `BaselineHistoryReader` 타입 미존재로 `Assert.NotNull()` 실패를 확인했다.
- Stub Green: 타입/메서드 stub 추가 후 focused contract test 1개 통과.
- Red 2: behavior tests 4개가 stub `NotSupportedException`으로 실패함을 확인했다.
- Green: focused reader tests 4개 통과.
- `git diff --check` 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 231개 통과/실패 0.

## 2026-06-23 (Codex - baseline history report command Task 1 parser contract)

### 작업 단위
- baseline history report command 의 첫 구현 단위로 parser/usage contract 만 추가했다.

### 변경 내용
- `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`: history command 성공/Markdown 선택/필수 `--history`/`--history-md` 경계/`--report` 혼용 거부 테스트 5개를 추가했다.
- `tests/Hps.Benchmarks/BenchmarkCommand.cs`: `SummarizeBaselineHistory` command 값을 추가했다.
- `tests/Hps.Benchmarks/BenchmarkCommandLine.cs`: history input root, JSON output path, Markdown output path 보존 필드를 추가했다.
- `tests/Hps.Benchmarks/BenchmarkCommandParser.cs`: `--summarize-baseline-history <baseline-root> --history <output-json> [--history-md <output-md>]` parsing 을 추가했다.
- `tests/Hps.Benchmarks/Program.cs`: usage text 에 history command 를 추가했다. 실행 switch wiring 은 Task 4 범위로 남겼다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 1 완료와 다음 Task 2(history domain/reader) 진입점을 반영했다.

### 검증
- Red: `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BenchmarkCommandParserTests`
  에서 새 history command 테스트 5개 실패를 확인했다.
- Green: 같은 focused parser tests 15개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 227개 통과/실패 0.

## 2026-06-23 (Codex - baseline history report command 계획 리뷰 보정)

### 작업 단위
- baseline history report command 구현 계획에 대한 리뷰 의견을 구현 전 계약 보정으로 반영했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-23-baseline-history-report-command-design.md`: history root 실패 카운터를
  `failed-session-count`로 고정하고, 누락 p99 를 JSON `null`/Markdown `-`로 표현하도록 보정했다.
- `docs/superpowers/plans/2026-06-23-baseline-history-report-command.md`: reader/generator/writer/test 계획을
  session `hard-passed` AND, `failed-session-count`, nullable p99 계약에 맞춰 갱신했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`: D078 영향 범위에 위 계약을 명시했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 실행점은 Task 1(parser contract)로 유지하고, 보정된 history 계약을 추가했다.

### 검증
- `rg`로 output root 의 옛 `hard-failure-count`와 p99 `0` fallback 이 설계/계획에 남지 않았는지 확인했다.
  남은 `hard-failure-count`는 입력 summary schema 와 session raw field 읽기 용도다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 222개 통과/실패 0.

## 2026-06-23 (Codex - baseline history report command 구현 계획)

### 작업 단위
- D078 baseline history report command 설계를 실제 구현 가능한 Task 1~4 계획으로 분해했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-23-baseline-history-report-command.md`: parser contract, history reader, aggregate writer,
  Program wiring/smoke 의 4개 커밋 단위 계획을 추가했다.
- Task 1은 `BenchmarkCommandParser`와 usage text 만 다루고, 실행 wiring 은 Task 4로 분리했다.
- Task 2/3은 새 타입 도입 시 컴파일 실패 Red 를 피하기 위해 reflection contract Red → stub → behavior Red 순서를 명시했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 진입점을 Task 1(parser contract) 구현으로 갱신했다.

### 검증
- `docs/superpowers/specs/2026-06-23-baseline-history-report-command-design.md`, D078, 설계 리뷰 문서, benchmark parser/source,
  summary reader/writer/test 패턴을 대조했다.
- 계획 self-review 로 spec coverage, placeholder scan, type consistency, commit boundary 를 확인했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 222개 통과/실패 0.

## 2026-06-23 (Codex - baseline history report command 설계 리뷰)

### 작업 단위
- baseline history report command 설계를 구현 전 리뷰 게이트로 검토하고, 구현자가 흔들릴 수 있는 모호성을 닫았다.

### 변경 내용
- `docs/superpowers/specs/2026-06-23-baseline-history-report-command-design.md`: command enum 이름을
  `BenchmarkCommand.SummarizeBaselineHistory`로 고정하고, parent baseline root/date root 입력 discovery 규칙을 분리했다.
- `docs/agent-state/reviews/2026-06-23-baseline-history-report-command-design-review.md`: 설계 리뷰 결과와 보정한 finding 2건을 기록했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`: D078로 history command 를 provider-independent aggregate artifact 로 두고,
  warning 은 계속 soft signal 로 유지한다고 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 진입점을 baseline history report command 구현 계획 작성으로 갱신했다.

### 검증
- `BenchmarkCommand`, `BenchmarkCommandLine`, `BenchmarkCommandParser`, `Program`, summary writer/generator, baseline summary artifact 구조를 대조했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 222개 통과/실패 0.

## 2026-06-23 (Codex - Phase 4 backlog 재평가)

### 작업 단위
- stable identity / UDP lease sweep must-fix 체인 종료 후 Phase 4 backlog 를 재평가하고 다음 구현 후보를 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-23-baseline-history-report-command-design.md`: 여러 baseline session `summary.json`을 읽어
  `history.json`과 선택적 `history.md`를 생성하는 provider-independent command 설계를 추가했다.
- 다음 구현 후보는 `--summarize-baseline-history <baseline-root> --history <output-json> [--history-md <output-md>]`로 좁혔다.
- CI workflow, warning-as-failure, latency hard gate, 기존 `index.md` 자동 덮어쓰기는 범위 밖으로 남겼다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 진입점을 baseline history report command 설계 리뷰로 갱신했다.

### 검증
- `PLAN.md`, `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`를 읽고 현재 Phase 4 진입점을 대조했다.
- `docs/superpowers/specs/2026-06-18-repeat-baseline-policy-design.md`,
  `docs/superpowers/specs/2026-06-18-ci-repeat-baseline-policy-design.md`,
  `docs/superpowers/specs/2026-06-18-baseline-report-history-warning-policy-design.md`,
  `.claude/review/2026-06-18-repeat-baseline-policy-review.md`를 확인했다.
- `tests/Hps.Benchmarks`와 `tests/Hps.Benchmarks.Tests`의 현재 CLI/parser/summary 구조를 확인해 설계가 기존 경로를 재사용하는지 검토했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 222개 통과/실패 0.

## 2026-06-23 (Codex - UDP lease sweep registry race guard review gate)

### 작업 단위
- 직전 `a817c6e` UDP lease sweep registry race guard 수정분을 다음 구현 전 리뷰 게이트로 검토했다.

### 변경 내용
- `docs/agent-state/reviews/2026-06-23-udp-lease-sweep-race-guard-review.md`: handler gate 직렬화, PUBLISH fan-out lock 범위, race regression test 를 검토한 문서를 추가했다.
- Blocker/Major correctness finding 은 발견하지 못했다.
- race regression test 의 250ms scheduling window 는 fixed path 판단을 막지 않는 Minor 관찰로 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`: stable identity / UDP lease sweep must-fix 체인이 닫힌 상태와 다음 Phase 4 backlog 재평가 진입점을 반영했다.

### 검증
- `git show --stat --oneline a817c6e`와 `git show -- src\Hps.Broker\BrokerUdpDatagramHandler.cs`,
  `git show -- tests\Hps.Broker.Tests\BrokerUdpDatagramHandlerTests.cs`로 수정 범위를 확인했다.
- `rg`로 D077, handler gate, sweep/register race test, PUBLISH lock-outside 경계를 대조했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 222개 통과/실패 0.

## 2026-06-23 (Codex - UDP lease sweep registry race guard)

### 작업 단위
- F1 후속 must-fix 로 UDP lease sweep registry cleanup 의 stale snapshot race 를 막았다.

### 변경 내용
- `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`: sweep 이 expired target snapshot 을 만든 뒤 같은 stable target 이
  다시 `REGISTER`되는 interleave 를 deterministic 하게 재현하는 회귀 테스트를 추가했다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: UDP receive command, endpoint close cleanup, lease sweep state mutation 을
  handler-local gate 로 직렬화했다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: `PUBLISH`는 lease activity 만 gate 안에서 갱신하고, 실제 fan-out 은 lock 밖에서 수행한다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`: D077로 handler gate 선형화 결정을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`: race guard 완료와 다음 review gate 를 반영했다.

### 검증
- Red: focused race test 에서 `Assert.True()` failure 를 확인했다.
- Green: 같은 focused race test 통과.
- Focused regression: `BrokerUdpDatagramHandlerTests` 17개 통과, `Hps.Broker.Tests` 73개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 222개 통과/실패 0.

## 2026-06-23 (Codex - UDP stable identity F1/F2 review gate)

### 작업 단위
- 직전 UDP stable identity F1/F2 수정 커밋을 다음 구현 전 리뷰 게이트로 검토했다.

### 변경 내용
- `docs/agent-state/reviews/2026-06-23-udp-stable-identity-f1-f2-review.md`: F1/F2 수정분 리뷰 문서를 추가했다.
- F2 invalid identity datagram isolation 은 UDP shared endpoint close 를 막는 방향으로 정합하다고 판단했다.
- F1 lease sweep registry cleanup 에 stale snapshot race 가 남아 있음을 Major finding 으로 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 단일 작업을 stale snapshot race must-fix 로 갱신했다.

### 검증
- `rg`로 `BrokerServer` timer callback, `BrokerUdpDatagramHandler.SweepExpiredUdpLeases(...)`,
  `UdpRemoteLeaseTracker.SweepExpired(...)`, UDP `OnDatagramReceived(...)`/`RegisterUdpTarget(...)` 경계를 대조했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 221개 통과/실패 0.

## 2026-06-23 (Codex - UDP invalid stable identity datagram isolation)

### 작업 단위
- Stable subscriber identity 교차검증 F2 must-fix 를 처리했다.
- UDP `REGISTER`/`UNREGISTER` identity validation 실패가 handler 밖으로 escape 하지 않게 했다.

### 변경 내용
- `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`: tab 이 포함된 invalid identity token 을 가진
  `REGISTER`/`UNREGISTER` datagram 이 예외 없이 drop 되고, endpoint close 없이 기존 subscription 을 보존하는지 검증했다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: `REGISTER`/`UNREGISTER` 처리 전에 stable identity token 을
  비예외 방식으로 검사하는 `TryDecodeIdentity(...)`를 추가했다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: 검증된 `SubscriberIdentity`만 registry 경로로 넘기도록
  `RegisterUdpTarget(...)` 경계를 정리했다.
- `CURRENT_PLAN.md`, `TODOS.md`: F2 완료와 다음 review gate 를 반영했다.

### 검증
- Red: `dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter FullyQualifiedName~OnDatagramReceived_WhenStableIdentityTokenIsInvalid_DropsDatagramWithoutThrowingOrClosingEndpoint`
  에서 `REGISTER`/`UNREGISTER` 두 케이스 모두 `Assert.Null()` failure 를 확인했다.
- Green: 같은 focused invalid identity test 2개 통과.
- Focused regression: `dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter FullyQualifiedName~BrokerUdpDatagramHandlerTests`
  통과, 16개 통과/실패 0.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 221개 통과/실패 0.

## 2026-06-23 (Codex - UDP stable identity lease sweep registry cleanup)

### 작업 단위
- Stable subscriber identity 교차검증 F1 must-fix 를 처리했다.
- UDP lease sweep 이 만료 remote target 을 stable registry 에도 disconnected 로 반영하게 했다.

### 변경 내용
- `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`: registered UDP remote 가 idle sweep 으로 만료된 뒤
  retention sweep 대상이 되는지 검증하는 회귀 테스트를 추가했다.
- `src/Hps.Broker/UdpRemoteLeaseTracker.cs`: 기존 `SweepExpired(DateTimeOffset)` 반환값은 routing 제거 수로 유지하고,
  registry cleanup 용 expired target snapshot 을 선택적으로 채우는 overload 를 추가했다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: registry 주입 경로에서 만료 target snapshot 을 받아
  `SubscriberRegistry.RemoveTarget(...)`으로 current target 을 disconnected 상태로 전환한다.
- `CURRENT_PLAN.md`, `TODOS.md`: F1 완료와 다음 F2(UDP invalid identity datagram 격리) 진입점을 반영했다.

### 검증
- Red: `dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter FullyQualifiedName~SweepExpiredUdpLeases_WhenRegisteredRemoteExpires_MarksRegistryTargetDisconnected`
  에서 `Expected: 1, Actual: 0` assertion failure 를 확인했다.
- Green: 같은 focused test 1개 통과.
- Focused regression: `dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter FullyQualifiedName~BrokerUdpDatagramHandlerTests`
  통과, 14개 통과/실패 0.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 219개 통과/실패 0.

## 2026-06-23 (Codex - Stable subscriber identity post-implementation cross-verification)

### 작업 단위
- D075/D076 stable subscriber identity 구현 전체를 설계/코드/테스트 기준으로 교차검증했다.

### 변경 내용
- `docs/agent-state/reviews/2026-06-23-stable-subscriber-identity-cross-check.md`: post-implementation review 문서를 추가했다.
- UDP stable identity lease sweep 이 `SubscriberRegistry`를 disconnected 상태로 바꾸지 않는 must-fix 를 기록했다.
- UDP invalid stable identity command 예외가 shared UDP endpoint close 로 이어질 수 있는 must-fix 를 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 실행 단위를 F1 수정으로 갱신하고 F2를 그 다음 단위로 기록했다.

### 검증
- `rg`와 줄 번호 확인으로 stable identity 설계, 구현, 테스트 경계를 대조했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 218개 통과/실패 0.

## 2026-06-22 (Codex - Stable subscriber identity UDP loopback coverage)

### 작업 단위
- Stable subscriber identity UDP rebind 가 실제 UDP datagram loopback 에서도 유지되는지 coverage 를 추가했다.

### 변경 내용
- `tests/Hps.Server.Tests/BrokerServerTests.cs`: `BrokerServerOptions.CreateWithStableSubscriberIdentity(...)`를 켠
  실제 `BrokerServer` + `SaeaTransport` UDP loopback 테스트를 추가했다.
- 테스트는 old remote 가 `REGISTER device-a` 후 `SUBSCRIBE alpha`를 보내고, new remote 가 같은 id 로 `REGISTER`만 했을 때
  retained topic set 이 new remote 로 재바인딩되어 이후 publish payload 를 받는지 검증한다.
- UDP는 old remote 를 transport 차원에서 close 할 수 없으므로, routing table 에서 old remote target 만 제거하는 정책을
  실제 datagram 송수신 경로로 고정한다.
- `CURRENT_PLAN.md`, `TODOS.md`: 이번 coverage 단위와 다음 리뷰 대기 상태를 반영했다.

### 검증
- Focused: `dotnet test tests\Hps.Server.Tests\Hps.Server.Tests.csproj --filter FullyQualifiedName~UdpCommandLoopback_WhenStableSubscriberRemoteRebinds_RoutesPayloadToNewRemote` 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 218개 통과/실패 0.

## 2026-06-22 (Codex - Stable subscriber identity TCP loopback coverage)

### 작업 단위
- Stable subscriber identity 구현 완료 게이트를 강화하기 위해 실제 TCP loopback coverage 를 추가했다.

### 변경 내용
- `tests/Hps.Server.Tests/BrokerServerTests.cs`: `BrokerServerOptions.CreateWithStableSubscriberIdentity(...)`를 켠
  실제 `BrokerServer` + `SaeaTransport` loopback 테스트를 추가했다.
- 테스트는 old subscriber 가 `REGISTER device-a` 후 `SUBSCRIBE alpha`를 보내고, new subscriber 가 같은 id 로 `REGISTER`만 했을 때
  old socket 이 닫히고 new socket 이 이후 publish payload 를 받는지 검증한다.
- old socket close helper 는 Windows loopback 에서 FIN 대신 `ConnectionReset`이 올 수 있어 두 관측값을 close 완료로 처리한다.
- `CURRENT_PLAN.md`, `TODOS.md`: 이번 coverage 단위와 다음 리뷰 대기 상태를 반영했다.

### 검증
- Focused: `dotnet test tests\Hps.Server.Tests\Hps.Server.Tests.csproj --filter FullyQualifiedName~TcpCommandLoopback_WhenStableSubscriberReconnects_RebindsTopicToNewSocket` 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 217개 통과/실패 0.

## 2026-06-22 (Codex - Stable subscriber identity UDP late REGISTER lease cleanup)

### 작업 단위
- Stable subscriber identity self-review 중 발견한 UDP late `REGISTER` lease metadata 누수를 단일 TDD 보강으로 처리했다.

### 변경 내용
- `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`: UDP remote 가 `SUBSCRIBE` 후 `REGISTER`하면
  pre-register runtime lease 가 제거되는지 검증하는 회귀 테스트를 추가했다.
- `src/Hps.Broker/UdpRemoteLeaseTracker.cs`: 같은 remote 의 lease metadata 를 registry rebound topic set 으로
  완전히 교체하는 `ReplaceSubscribedTopics(...)`를 추가했다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: `REGISTER` 성공 후 UDP lease metadata 를 stable topic set 으로 교체한다.
- `docs/superpowers/specs/2026-06-22-stable-subscriber-identity-reconnect-policy-design.md`: D076 late `REGISTER` 정책에
  UDP lease metadata cleanup 기준을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 이번 보강 단위와 다음 리뷰 대기 상태를 반영했다.

### 검증
- Red: focused `BrokerUdpDatagramHandlerTests`에서 late `REGISTER` 이후 pre-register runtime lease 가 남는 assertion failure 1개 확인.
- Green/Refactor: focused `BrokerUdpDatagramHandlerTests` 13개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 216개 통과/실패 0.

## 2026-06-22 (Codex - Stable subscriber identity late REGISTER cleanup)

### 작업 단위
- Stable subscriber identity 구현분 self-review 중 발견한 late `REGISTER` stale subscription 결함을 단일 TDD 보강으로 처리했다.

### 변경 내용
- `tests/Hps.Broker.Tests/SubscriberRegistryTests.cs`: `SUBSCRIBE` 후 `REGISTER` 순서에서 기존 runtime 구독이 제거되는지 검증하는 회귀 테스트를 추가했다.
- `src/Hps.Broker/SubscriberRegistry.cs`: 새 target 을 stable identity 에 매핑하기 전, 같은 runtime target 의 기존 routing 구독을 제거한다.
- `docs/superpowers/specs/2026-06-22-stable-subscriber-identity-reconnect-policy-design.md`: late `REGISTER`는 기존 runtime 구독을 stable metadata 로 이관하지 않는다고 명시했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`: D076을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 이번 보강 단위와 다음 리뷰 대기 상태를 반영했다.

### 검증
- Red: focused `SubscriberRegistryTests`에서 late `REGISTER` 이후 pre-register runtime 구독이 남는 assertion failure 1개 확인.
- Green: focused `SubscriberRegistryTests` 10개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 215개 통과/실패 0.

## 2026-06-22 (Codex - Stable subscriber identity BrokerServer opt-in wiring)

### 작업 단위
- Stable subscriber identity 구현 계획 Task 5로 Server public options 와 host retention timer wiring 을 연결했다.

### 변경 내용
- `src/Hps.Server/BrokerServerOptions.cs`: stable identity enabled/retention timeout 속성,
  `CreateWithStableSubscriberIdentity(...)`, `WithStableSubscriberIdentity(...)`를 추가했다.
- `src/Hps.Server/BrokerServer.cs`: enabled options 일 때 shared `SubscriberRegistry`를 만들고 TCP/UDP handler 에 같은 registry 를 주입한다.
- `src/Hps.Server/BrokerServer.cs`: TCP 또는 UDP start 성공 후 stable identity retention timer 를 한 번만 생성하고,
  `StopAsync`에서 UDP lease sweep timer 와 함께 dispose 한다.
- `tests/Hps.Server.Tests/BrokerServerOptionsTests.cs`: 기본 disabled, retention timeout 검증, explicit values,
  UDP lease sweep 설정 보존을 검증했다.
- `tests/Hps.Server.Tests/BrokerServerTests.cs`: TCP handler registry wiring, expired disconnected identity sweep,
  retention timer dispose 를 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 5 완료와 stable identity 구현 계획 완료 후 리뷰 대기 상태를 반영했다.

### 검증
- Red: stable identity options/factory/timer wiring 부재로 focused Server/Options tests assertion failure 7개 확인.
- Green: focused stable Server/Options tests 7개 통과.
- Refactor: reflection bootstrap 테스트를 direct public API 호출로 정리한 뒤 focused stable Server/Options tests 7개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 214개 통과/실패 0.

## 2026-06-22 (Codex - Stable subscriber identity UDP handler wiring)

### 작업 단위
- Stable subscriber identity 구현 계획 Task 4로 UDP datagram handler 에 optional registry 경로를 연결했다.

### 변경 내용
- `src/Hps.Broker/UdpRemoteLeaseTracker.cs`: stable rebind 에 필요한 `RemoveRemote(...)`와 `MarkSubscribedTopics(...)`를 추가했다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: 기존 public/internal constructor 는 유지하고, registry 선택 주입 constructor 를 추가했다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: UDP `REGISTER`/`UNREGISTER` command 처리와 registered remote subscribe/unsubscribe 를 `SubscriberRegistry`와 lease tracker 로 연결했다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: same-id remote rebind 시 old remote lease/subscription 을 제거하고 rebound topic lease 를 새 remote 에 복구한다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: duplicate target different-id 는 UDP 정책대로 endpoint close 없이 datagram drop 으로 처리한다.
- `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`: remote rebind, duplicate registration drop, explicit unregister,
  endpoint close 후 reconnect topic restore 를 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 4 완료와 다음 Task 5 Server opt-in wiring 진입점을 반영했다.

### 검증
- Red: registry 주입 internal constructor 부재로 focused UDP handler tests assertion failure 4개 확인.
- Green/Refactor: focused UDP handler tests 12개 통과.

## 2026-06-22 (Codex - Stable subscriber identity TCP handler wiring)

### 작업 단위
- Stable subscriber identity 구현 계획 Task 3으로 TCP frame handler 에 optional registry 경로를 연결했다.

### 변경 내용
- `src/Hps.Broker/BrokerTcpFrameHandler.cs`: 기존 public constructor 는 유지하고, registry/time provider internal constructor 를 추가했다.
- `src/Hps.Broker/BrokerTcpFrameHandler.cs`: `REGISTER`/`UNREGISTER` command 처리와 registered target 의 subscribe/unsubscribe 를 `SubscriberRegistry`로 위임했다.
- `src/Hps.Broker/BrokerTcpFrameHandler.cs`: same-id reconnect 시 old TCP target 을 close 하고, duplicate target different-id 는 protocol error close 로 수렴한다.
- `src/Hps.Broker/BrokerTcpFrameHandler.cs`: close cleanup 은 registry 가 있으면 `RemoveTarget(..., now)`로, 없으면 기존 `UnsubscribeAll(connection)`으로 처리한다.
- `tests/Hps.Broker.Tests/BrokerTcpFrameHandlerTests.cs`: reconnect rebind, duplicate registration close, connection close retention,
  explicit unregister metadata 제거를 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 3 완료와 다음 Task 4 UDP handler wiring 진입점을 반영했다.

### 검증
- Red: registry 주입 internal constructor 부재로 focused TCP handler tests assertion failure 4개 확인.
- Green/Refactor: focused TCP handler tests 11개 통과.

## 2026-06-22 (Codex - Stable subscriber identity pure registry)

### 작업 단위
- Stable subscriber identity 구현 계획 Task 2로 Broker 내부 identity/registry pure model 을 구현했다.

### 변경 내용
- `src/Hps.Broker/SubscriberIdentity.cs`: non-empty/no-whitespace identity token validation 과 ordinal equality 를 추가했다.
- `src/Hps.Broker/SubscriberRegistrationResult.cs`: REGISTER 결과 enum 을 추가했다.
- `src/Hps.Broker/SubscriberRegistry.cs`: identity별 topic metadata, current target mapping, same-id rebind,
  same-target different-id conflict, disconnect retention, explicit unregister, disconnected sweep, UDP endpoint cleanup 을 구현했다.
- `tests/Hps.Broker.Tests/SubscriberIdentityTests.cs`, `tests/Hps.Broker.Tests/SubscriberRegistryTests.cs`:
  contract, validation, rebind, metadata retention, unregister, sweep, UDP endpoint cleanup 을 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 2 완료와 다음 Task 3 TCP handler wiring 진입점을 반영했다.

### 검증
- Red 1: 타입 부재 reflection contract assertion failure 2개 확인.
- Red 2: 스텁 추가 후 behavior assertion failure 10개 확인.
- Green/Refactor: focused broker identity/registry tests 15개 통과.

## 2026-06-22 (Codex - Stable subscriber identity protocol decode)

### 작업 단위
- Stable subscriber identity 구현 계획 Task 1로 protocol `REGISTER` / `UNREGISTER` command decode 를 구현했다.

### 변경 내용
- `src/Hps.Protocol/TcpCommandKind.cs`: `Register = 4`, `Unregister = 5` command kind 를 추가했다.
- `src/Hps.Protocol/TcpCommandDecoder.cs`: `REGISTER <subscriber-id>`와 `UNREGISTER <subscriber-id>`를 기존 token-only command 문법으로 decode 하도록 분기했다.
- `tests/Hps.Protocol.Tests/TcpCommandDecoderTests.cs`: command kind 계약, 정상 decode, malformed token 경계를 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 1 완료와 다음 Task 2 pure registry 진입점을 반영했다.

### 검증
- Red: `dotnet test tests\Hps.Protocol.Tests\Hps.Protocol.Tests.csproj --filter FullyQualifiedName~TcpCommandDecoderTests`에서
  enum 부재와 decoder 미지원으로 assertion failure 9개를 확인했다.
- Green/Refactor: 같은 focused protocol tests 24개 통과.

## 2026-06-22 (Codex - Stable subscriber identity implementation plan)

### 작업 단위
- D075 stable subscriber identity / reconnect rebinding 정책을 구현 가능한 Task 단위로 분해했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-22-stable-subscriber-identity.md`: protocol decode, pure registry, TCP handler,
  UDP handler, Server opt-in wiring 의 5개 작업 단위와 각 Red-Green-Refactor 검증/커밋 경계를 작성했다.
- `CURRENT_PLAN.md`: 다음 실행 지점을 구현 계획 리뷰로 갱신했다.
- `TODOS.md`: 구현 계획 작성 완료와 다음 Task 1 후보를 반영했다.

### 검증
- 계획 self-review: D075 spec coverage, placeholder, type consistency 를 확인했다.
- 기존 `TcpCommandDecoderTests`, `BrokerTcpFrameHandlerTests`, `BrokerUdpDatagramHandlerTests`, `BrokerServerTests` 구조를 기준으로 Task 경계를 맞췄다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 175개 통과/실패 0.

## 2026-06-22 (Codex - Stable subscriber identity policy)

### 작업 단위
- D058/D059 이후 deferred 상태였던 stable subscriber identity / reconnect rebinding 정책을 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-22-stable-subscriber-identity-reconnect-policy-design.md`: 기본 runtime target subscription 유지,
  opt-in `REGISTER <subscriber-id>` 기반 Broker registry, duplicate/rebind, disconnect retention, 테스트 순서를 정리했다.
- `DECISIONS.md`: D075를 추가하고 stable identity 를 후속 opt-in registry 로 구현한다는 기준을 active decision index 에 반영했다.
- `TODOS.md`: stable identity 설계 backlog 를 완료로 이동하고, 다음 current gate 를 설계 리뷰 대기로 갱신했다.
- `CURRENT_PLAN.md`: 현재 상태 요약, 최근 완료 단위, 다음 실행 지점, 검증 경로를 이번 설계 단위 기준으로 갱신했다.

### 검증
- 실제 `BrokerSubscriber`, `SubscriptionTable`, `BrokerTcpFrameHandler`, `BrokerUdpDatagramHandler`, `TcpCommandDecoder` 구조와 설계가 충돌하지 않는지 확인했다.
- 기존 `docs/superpowers/specs/2026-06-16-endpoint-identity-policy.md`와 D058/D059/D060 정책을 유지하는지 확인했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 175개 통과/실패 0.

## 2026-06-22 (Codex - BrokerServer UDP lease host timer wiring)

### 작업 단위
- D074 구현 두 번째 단위로 `BrokerServerOptions` enabled 설정을 실제 `BrokerServer` UDP 수명에 연결했다.

### 변경 내용
- `src/Hps.Broker/Properties/AssemblyInfo.cs`: `Hps.Server`가 내부 `BrokerUdpDatagramHandler` lease 생성자와 `UdpLeaseOptions`를 사용할 수 있도록 friend assembly 경계를 추가했다.
- `src/Hps.Server/BrokerServer.cs`: options 생성자를 추가하고 기본 생성자는 이 경로로 위임했다.
- `src/Hps.Server/BrokerServer.cs`: UDP start 성공 후 `TimeProvider.CreateTimer`로 sweep timer 를 만들고, timer callback 에서 `SweepExpiredUdpLeases(...)`를 호출한다.
- `src/Hps.Server/BrokerServer.cs`: `StopAsync`와 UDP start 실패 cleanup 에서 sweep timer 를 dispose 하도록 수명 경계를 맞췄다.
- `tests/Hps.Server.Tests/BrokerServerTests.cs`: enabled options 에서 timer 생성/만료 sweep, stop 시 timer dispose 를 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: host timer wiring 완료와 다음 리뷰 게이트를 반영했다.

### 검증
- Red: reflection 기반 `BrokerServerTests`가 options 생성자 부재로 `Assert.NotNull` 2개 실패.
- Green: focused `FullyQualifiedName~UdpLeaseSweepEnabled` tests 2개 통과.
- Refactor: 기본 생성자 위임과 direct public API 테스트로 정리한 뒤 focused tests 2개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 175개 통과/실패 0.

## 2026-06-22 (Codex - BrokerServerOptions)

### 작업 단위
- D074 구현 첫 단위로 `BrokerServerOptions` public 설정 타입을 추가했다.

### 변경 내용
- `src/Hps.Server/BrokerServerOptions.cs`: 기본 disabled options 와 UDP lease sweep 활성 options factory 를 추가했다.
- `tests/Hps.Server.Tests/BrokerServerOptionsTests.cs`: 기본 disabled, 0 이하 timeout/interval 거부, explicit 값과 `TimeProvider` 저장을 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 실행 지점을 실제 host timer wiring 으로 갱신했다.

### 검증
- Red: reflection 기반 `BrokerServerOptionsTests`가 타입 부재로 `Assert.NotNull` 3개 실패.
- Green: focused `BrokerServerOptionsTests` 3개 통과.
- Refactor: reflection 테스트를 direct public API 호출로 정리한 뒤 focused `BrokerServerOptionsTests` 3개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 173개 통과/실패 0.

## 2026-06-22 (Codex - BrokerServer UDP lease host timer design)

### 작업 단위
- UDP lease tracker/sweep core 이후 남은 `BrokerServer` host timer/public settings 설계를 작성했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-22-broker-server-udp-lease-host-timer-design.md`: `BrokerServerOptions`,
  기본 disabled 정책, explicit timeout/interval, `TimeProvider.CreateTimer`, `Hps.Broker` friend assembly 경계를 정리했다.
- `DECISIONS.md`: D074를 active decision index 에 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 실행 지점을 host timer 구현으로 갱신했다.

### 검증
- 설계 self-review: 기본값 미정 문제를 "활성화 시 explicit timeout/interval 요구"로 닫았고, Broker public lease options 를 늘리지 않는 방향으로 정리했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 170개 통과/실패 0.

## 2026-06-22 (Codex - UDP lease tracker handler wiring)

### 작업 단위
- UDP optional lease sweep 구현 계획의 Task 4를 수행했다.
- `BrokerUdpDatagramHandler`가 UDP command activity 를 `UdpRemoteLeaseTracker`로 위임하게 했다.

### 변경 내용
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: public constructor 는 disabled lease options 를 사용하는 기존 경로로 유지하고, internal constructor 에서 options/time provider 를 주입받아 tracker 를 생성한다.
- `src/Hps.Broker/BrokerUdpDatagramHandler.cs`: SUBSCRIBE/UNSUBSCRIBE/PUBLISH/endpoint-close 처리를 tracker 로 위임하고 `SweepExpiredUdpLeases(DateTimeOffset)` 내부 entry point 를 추가했다.
- `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`: command 로 생성된 lease 가 sweep 으로 제거되는지, PUBLISH activity 가 기존 lease 를 갱신해 sweep 에서 보존하는지 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: Task 1~4 core 완료와 host timer/public settings 후속 범위를 갱신했다.

### 검증
- Red: reflection 기반 handler wiring tests 가 internal constructor 부재로 `Assert.NotNull` 2개 실패.
- Green: focused `BrokerUdpDatagramHandlerTests` 8개 통과.
- Refactor: reflection helper 를 direct internal API 호출로 정리한 뒤 focused `BrokerUdpDatagramHandlerTests` 8개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 170개 통과/실패 0.

## 2026-06-22 (Codex - UDP remote lease pure sweep)

### 작업 단위
- UDP optional lease sweep 구현 계획의 Task 3을 수행했다.
- `UdpRemoteLeaseTracker.SweepExpired(DateTimeOffset)`로 만료된 UDP remote lease 를 routing table 에서 정리한다.

### 변경 내용
- `src/Hps.Broker/UdpRemoteLeaseTracker.cs`: idle timeout 을 초과한 `(IUdpEndpoint, EndPoint)` lease 를 찾아 `SubscriptionTable.UnsubscribeAll(IUdpEndpoint, EndPoint)`로 제거하는 순수 sweep 메서드를 추가했다.
- `tests/Hps.Broker.Tests/UdpRemoteLeaseTrackerTests.cs`: 만료 remote 제거, publish activity 갱신 보존, disabled options no-op 을 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 완료 단위와 다음 Task 4 handler wiring 진입점을 갱신했다.

### 검증
- Red: reflection 기반 sweep tests 가 `SweepExpired` 메서드 부재로 `Assert.NotNull` 3개 실패.
- Green: focused `UdpRemoteLeaseTrackerTests` 8개 통과.
- Refactor: reflection helper 를 direct internal API 호출로 정리한 뒤 focused `UdpRemoteLeaseTrackerTests` 8개 통과.
- 계획 보정: plan 예시의 survivor remote 는 expired remote 와 같은 시점에 구독하면 함께 만료되므로, survivor를 늦게 구독하도록 테스트 setup 을 보정했다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 168개 통과/실패 0.

## 2026-06-22 (Codex - UDP remote lease tracker activity)

### 작업 단위
- UDP optional lease sweep 구현 계획의 Task 2를 수행했다.
- 내부 `UdpRemoteLeaseTracker`로 UDP remote subscription activity 와 endpoint cleanup lease state 를 추적한다.

### 변경 내용
- `src/Hps.Broker/UdpRemoteLeaseTracker.cs`: `(IUdpEndpoint, EndPoint)` key 기반 lease table 을 추가하고 subscribe/unsubscribe/publish activity, endpoint close cleanup 을 처리한다.
- `tests/Hps.Broker.Tests/UdpRemoteLeaseTrackerTests.cs`: disabled options 보존, enabled remote당 lease 1개, 마지막 topic unsubscribe 시 lease 제거, publisher-only remote 미생성, endpoint close cleanup 을 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 완료 단위와 다음 Task 3 순수 sweep 진입점을 갱신했다.

### 검증
- Red: reflection 기반 `UdpRemoteLeaseTrackerTests`가 타입 부재로 `Assert.NotNull` 5개 실패. 계획서의 compile-failure Red는 AGENTS의 assertion-failure Red 규칙에 맞춰 보정했다.
- Green: focused `UdpRemoteLeaseTrackerTests` 5개 통과.
- Refactor: reflection 테스트를 direct internal API 호출로 정리한 뒤 focused `UdpRemoteLeaseTrackerTests` 5개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 165개 통과/실패 0.

## 2026-06-22 (Codex - UDP lease options)

### 작업 단위
- UDP optional lease sweep 구현 계획의 Task 1을 수행했다.
- 내부 `UdpLeaseOptions` 타입과 테스트 assembly internal 접근 경계를 추가했다.

### 변경 내용
- `src/Hps.Broker/UdpLeaseOptions.cs`: 기본 비활성 options 와 양수 idle timeout/sweep interval 을 받는 활성 options factory 를 추가했다.
- `src/Hps.Broker/Properties/AssemblyInfo.cs`: `Hps.Broker.Tests`에 internal 접근을 허용했다.
- `tests/Hps.Broker.Tests/UdpLeaseOptionsTests.cs`: 기본 비활성, 0 이하 interval 거부, 양수 interval 저장을 검증했다.
- `docs/superpowers/plans/2026-06-22-udp-optional-lease-sweep.md`: `Enabled` property 와 C# 멤버 이름이 충돌하는 factory 이름을 `CreateEnabled(...)`로 정정했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 완료 단위와 다음 Task 2 진입점을 갱신했다.

### 검증
- Red: reflection 기반 `UdpLeaseOptionsTests`가 타입 부재로 `Assert.NotNull` 3개 실패.
- Green: focused `UdpLeaseOptionsTests` 3개 통과.
- Refactor: reflection 테스트를 direct internal API 호출로 정리한 뒤 focused `UdpLeaseOptionsTests` 3개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 160개 통과/실패 0.

## 2026-06-22 (Codex - UDP optional lease sweep implementation plan)

### 작업 단위
- D073 설계를 구현 가능한 작은 Task 로 분해했다.
- 코드 변경 없이 내부 options, lease tracker activity, 순수 sweep, handler wiring 의 커밋 경계를 정했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-22-udp-optional-lease-sweep.md`: 각 Task 의 touched files, produced interfaces, Red-Green 테스트, 검증/커밋 명령을 작성했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 실행 지점을 구현 계획 리뷰와 Task 1 시작으로 갱신했다.

### 검증
- 실제 `BrokerUdpDatagramHandler`, `SubscriptionTable`, `BrokerServer`, `BrokerSubscriber` 구조와 계획의 시그니처가 맞는지 확인했다.
- 계획 self-review 로 D073 coverage, placeholder, type consistency 를 확인했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 157개 통과/실패 0.

## 2026-06-22 (Codex - UDP optional lease tracker / sweep owner design)

### 작업 단위
- UDP idle expiry 의 lease tracker/sweep owner, key, 설정 표면, clock/timer 추상화, sweep 의 `UnsubscribeAll` 사용 방식을 설계했다.
- 코드 변경 없이 owner 계층(Broker 소유·Server 트리거), 설정(내부 options·기본 비활성), 시간 소스(`TimeProvider`)를 확정했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-22-udp-optional-lease-sweep-design.md`: lease 모델, options 타입, sweep 정책, 다음 최소 구현 단위, 범위 밖을 정리했다.
- `DECISIONS.md`: D073을 active decision index 에 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 완료 단위와 다음 구현 후보(UDP lease tracker/sweep 구현)를 갱신하고, 해결된 결정과 남은 open question 을 분리했다.

### 검증
- 실제 `BrokerUdpDatagramHandler`, `SubscriptionTable`, `BrokerServer`, `BrokerSubscriber` 구조와 충돌하지 않음, D061/D067/D068/D072 정합성을 확인했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 157개 통과/실패 0.

## 2026-06-22 (Codex - UDP remote-wide unsubscribe primitive)

### 작업 단위
- D072 idle sweep 의 선행 API로 `SubscriptionTable.UnsubscribeAll(IUdpEndpoint, EndPoint)`를 구현했다.
- timer, idle timeout 설정, BrokerServer public API 는 추가하지 않았다.

### 변경 내용
- `SubscriptionTable`: 특정 UDP local endpoint/remote endpoint 조합만 모든 topic 에서 제거하는 overload 를 추가했다.
- `BrokerRoutingTests`: 같은 endpoint 의 다른 remote, 다른 endpoint 의 같은 remote, TCP subscriber 가 보존되는지 검증하는 Red-Green 테스트를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`: 다음 실행 지점과 deferred 항목을 갱신했다.

### 검증
- Red: focused test 가 API 부재로 `Assert.NotNull` 실패.
- Green/Refactor: focused test 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 157개 통과/실패 0.

## 2026-06-19 (Codex - UDP stale remote idle expiry design)

### 작업 단위
- UDP remote subscription 이 `UNSUBSCRIBE` 없이 stale 로 남는 경우의 cleanup owner 와 정책을 설계했다.
- Transport 계층에 idle 판단을 넣지 않고 Broker/Server 소유의 선택적 lease cleanup 으로 분리했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-19-udp-stale-remote-idle-expiry-design.md`: UDP stale remote cleanup key, activity 갱신 규칙, sweep 범위, 다음 최소 구현 단위를 정리했다.
- `DECISIONS.md`: D072를 active decision index 에 추가했다.
- `TODOS.md`: 기존 설계 backlog 를 완료로 이동하고 다음 구현 후보를 `UDP remote-wide unsubscribe primitive` 로 좁혔다.
- `CURRENT_PLAN.md`: 다음 리뷰 게이트를 UDP stale remote idle expiry 설계로 갱신했다.

### 검증
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 156개 통과/실패 0.

## 2026-06-18 (Codex - baseline history index)

### 작업 단위
- 반복 baseline session 을 한곳에서 찾기 위한 전역 history index 를 추가했다.
- 코드, benchmark schema, CI workflow 는 변경하지 않았다.

### 변경 내용
- `docs/benchmarks/baselines/index.md`: 2026-06-18 root/session-02/session-03 summary artifact 와 hard/warning 상태를 연결했다.
- `docs/superpowers/specs/2026-06-18-baseline-report-history-warning-policy-design.md`: 상태를 Accepted 로 갱신했다.
- `DECISIONS.md`: D071을 active decision index 에 추가했다.
- `CURRENT_PLAN.md`: 다음 리뷰 게이트를 baseline history index 로 갱신했다.
- `TODOS.md`: baseline history index P1 항목을 완료로 이동했다.

### 검증
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 156개 통과/실패 0.

## 2026-06-18 (Codex - baseline report history/warning policy design)

### 작업 단위
- baseline summary JSON/Markdown artifact 이후의 report history 단위와 warning 승격 정책을 설계했다.
- CI provider workflow, warning-as-failure 구현, latency hard gate 는 이번 범위에서 제외하고 provider-independent 정책만 정리했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-18-baseline-report-history-warning-policy-design.md`:
  baseline session directory 를 history 단위로 보고, raw JSON/summary JSON/summary Markdown 역할을 분리했다.
- `TODOS.md`: 기존 P1 설계 항목을 완료로 이동하고, 승인 이후의 다음 후보로 baseline history index 작업을 남겼다.
- `CURRENT_PLAN.md`: 다음 게이트를 새 설계 문서 리뷰로 갱신했다.

### 검증
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 156개 통과/실패 0.

## 2026-06-18 (Codex - state document compaction)

### 작업 단위
- root 상태 문서가 빠른 진입점 역할을 잃을 정도로 커져 `docs/agent-state/` archive 를 만들고 문서를 축약했다.
- 원문은 `docs/agent-state/snapshots/2026-06-18-pre-compaction/`와 domain archive 에 보존했다.

### 변경 내용
- `CURRENT_PLAN.md`: 현재 목표, 최신 완료 단위, 다음 실행 지점, 검증 경로만 남겼다.
- `TODOS.md`: current TODO, handoff-ready deferred backlog, 최근 완료 항목만 남겼다.
- `CHANGELOG_AGENT.md`: 최근 작업 단위 중심으로 축약하고 전체 원문 archive 링크를 추가했다.
- `DECISIONS.md`: active decision index 로 축약하고 상세 원문 archive 링크를 추가했다.

### 검증
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 156개 통과/실패 0.

## 2026-06-18 (Codex - baseline summary markdown artifacts)

### 작업 단위
- 이미 구현된 `--summarize-baseline <input-dir> --summary <output-json> --summary-md <output-md>` command 로
  2026-06-18 baseline root, `session-02`, `session-03` directory 의 `summary.md` 보조 artifact 를 생성했다.
- 코드 변경 없이 benchmark artifact 와 상태 문서만 갱신했다.

### 검증
- 세 directory 에 대해 `--summary-md` 포함 summary command 를 실행해 모두 exit-code 0,
  `source-report-count=6`, `hard-passed=true`, `warning-count=0`을 확인했다.
- 생성된 세 `summary.md`가 `# Baseline Summary`, load/open-loop row, `Warnings`, `- 없음`을 포함하는지 확인했다.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 156개 통과/실패 0.
- `git diff --check` 통과.

## 2026-06-18 (Codex - baseline summary markdown cli)

### 작업 단위
- `--summarize-baseline <input-dir> --summary <output-json>` command 에 선택 옵션
  `--summary-md <output-md>`를 연결했다.
- JSON summary 는 계속 필수 canonical artifact 로 유지하고, Markdown 은 같은 `BaselineSummary`에서 파생되는
  사람 리뷰용 보조 artifact 로만 생성한다.

### 검증
- parser Red-Green, CLI Red-Green 을 수행했다.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-build --no-restore` 통과, 20개 통과/실패 0.
- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과, 전체 156개 통과/실패 0.
- `git diff --check` 통과.

## 2026-06-18 (Codex - baseline summary markdown writer)

### 작업 단위
- `BaselineSummary`를 사람이 빠르게 리뷰할 Markdown 표로 쓰는 writer 를 추가했다.

### 검증
- writer bootstrap Red-Green 과 Markdown 내용 Red-Green 을 수행했다.
- focused writer tests 통과.

## 2026-06-18 (Codex - baseline summary artifacts)

### 작업 단위
- 이미 구현된 `--summarize-baseline <input-dir> --summary <output-json>` command 로
  2026-06-18 baseline root, `session-02`, `session-03` directory 의 canonical `summary.json`을 생성했다.

### 검증
- 세 directory 에 대해 summary command 를 실행해 모두 exit-code 0,
  `source-report-count=6`, `hard-passed=true`, `warning-count=0`을 확인했다.
- 생성된 세 `summary.json`을 `ConvertFrom-Json`으로 읽어 summary schema 와 run count 를 확인했다.

## 2026-06-18 (Codex - baseline summary artifact implementation)

### 작업 단위
- baseline summary parser, generator, reader/writer, Program wiring 을 4개 작은 단위로 구현했다.
- D070에 따라 latency hard gate 는 추가하지 않고 summary JSON + non-failing soft warning 을 먼저 만들었다.

### 검증
- 각 Task 별 Red-Green 을 수행했다.
- 마지막 Program wiring 후 root/session-02/session-03 CLI smoke 를 모두 통과했다.
