# CHANGELOG_AGENT.md

## Archive

긴 변경 이력 원문은 `docs/agent-state/changelog/2026-06.md`에 보존했다.
이 파일은 최근 작업 단위와 현재 진입점에 필요한 내용만 유지한다.

## 2026-07-09 (Codex - D219 fixed send buffer registry)

### 작업 단위
- D218 Task 1 pure fixed send buffer registry contract 를 구현했다.

### 변경 내용
- `IoUringFixedSendBufferSlot`과 `IoUringFixedSendBufferRegistry`를 추가했다.
- registry 는 registered payload backing `byte[]` reference identity 를 fixed buffer index 로 조회한다.
- registry owner 는 등록된 `RefCountedBuffer`마다 guard ref 를 하나 유지하고, dispose 때 registration owner 와 guard ref 를 정리한다.
- capacity 를 초과한 send buffer 는 기존 slot 을 evict 하지 않고 miss 로 남긴다.
- production TCP payload send path 와 native `RegisterBuffers` 연결은 변경하지 않았다.

### 검증
- Red: `RegistryContract_WhenInspected_ExposesFixedSendLookupSurface`가 registry type 부재로 `Assert.NotNull() Failure`를 냈다.
- Red: slot metadata reflection 확장 후 property 부재로 `Assert.NotNull() Failure`를 냈다.
- Red: behavior tests 는 skeleton 의 lookup miss 때문에 `Assert.True() Failure`를 냈다.
- Green: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringFixedSendBufferRegistryTests -v minimal`
  통과, 3개.
- Relevant: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal`
  통과, 76개.

### 결과
- 다음 실행 지점은 D220 Task 2 native registration factory 와 rollback contract 다.
- fixed-write production 재연결, registration cache, zero-copy send, default backend promotion 은 계속 제외한다.

## 2026-07-09 (Codex - D218 fixed send registration lifetime plan)

### 작업 단위
- D217 설계를 TDD 구현 계획으로 쪼갰다.

### 변경 내용
- 구현 계획 `docs/superpowers/plans/2026-07-09-iouring-fixed-send-registration-lifetime.md`를 추가했다.
- 계획은 Task 1 pure registry, Task 2 native factory/rollback, Task 3 TCP resource ownership,
  Task 4 opt-in helper shape, Task 5 full local/remote gate documentation 으로 구성했다.
- 상태 문서의 current TODO 를 Task 1 pure fixed send buffer registry contract 구현으로 갱신했다.

### 검증
- 계획은 실제 `IoUringTcpConnectionResource`, `IoUringRegisteredBufferSet`,
  `IoUringFixedSendLease`, `IoUringTransport.SendInFlightAsync` 시그니처를 기준으로 작성했다.
- plan self-review 로 spec coverage, placeholder scan, type consistency 를 확인한다.
- `git diff --check`로 문서 변경 whitespace 를 확인한다.

### 결과
- 다음 실행 지점은 D219 Task 1 pure fixed send buffer registry contract 구현이다.
- production TCP payload `WRITE_FIXED` 재연결, UDP fixed-buffer send, zero-copy send,
  default backend promotion 은 계속 제외한다.

## 2026-07-09 (Codex - D217 fixed send registration lifetime design)

### 작업 단위
- D216 evidence 기준으로 io_uring 후속 후보를 재평가하고 다음 설계 단위를 확정했다.

### 확인 내용
- D216은 hang diagnostics 관측성 gate 이며 production fixed-write 재연결 근거가 아니다.
- 현재 production TCP payload path 는 `IoUringTransport.SendInFlightAsync`에서 `SendArrayAsync` baseline 을 유지한다.
- `IoUringFixedSendLease.CreateForSendPump(...)`는 send pump 전용 extra ref 는 잡지만,
  registration factory 는 여전히 per-send `IoUringRegisteredBufferSet.Register(...)`를 호출할 수 있는 shape 다.
- `IoUringRegisteredBufferSet.Dispose()`는 queue 전체 fixed buffer table 을 unregister 한다.
- 따라서 이 shape 를 그대로 production pump 에 다시 붙이면 D210의 active send 중 registration churn 실패 패턴을 반복할 수 있다.

### 변경 내용
- 설계 문서 `docs/superpowers/specs/2026-07-09-iouring-fixed-send-registration-lifetime-design.md`를 추가했다.
- D217 결정으로 다음 단위를 TCP connection-scoped fixed send registration lifetime owner 로 정했다.
- 상태 문서의 current TODO 를 D218 구현 계획 작성으로 갱신했다.

### 검증
- `rg`로 실제 `SendInFlightAsync`, `CreateForSendPump`, `IoUringRegisteredBufferSet`,
  `IoUringTcpConnectionResource` 경계를 대조했다.
- placeholder/self-review scan 을 수행했다.
- `git diff --check`로 문서 변경 whitespace 를 확인한다.

### 결과
- 다음 실행 지점은 D217 설계를 pure registry, lifetime guard, resource wiring,
  opt-in shape, remote gate task 로 나누는 구현 계획 작성이다.
- production fixed-write 재연결, zero-copy send, UDP fixed-buffer send, default backend promotion 은 계속 제외한다.

## 2026-07-08 (Codex - D216 contract hang diagnostics remote gate)

### 작업 단위
- D215 hang diagnostics workflow 의 원격 Linux contract gate 를 검토했다.

### 확인 내용
- 사용자 push 이후 `iouring-linux-contract.yml` run `28916879277`을 실행했다.
- head SHA 는 `df1cdf55d49b0f9ff21313efa9bcd20560e23e5e`이다.
- workflow/job conclusion 은 success 다.
- artifact `iouring-linux-contract-2026-07-08-github-28916879277-1`를 내려받았다.
- artifact 는 `summary.md`, `dotnet-info.txt`, `iouring-tests.trx`, `vstest-diag.log`,
  host/datacollector diag log 를 포함한다.
- summary 는 test exit code 0, `VSTest diag: vstest-diag.log`,
  `Hang diagnostics: blame-hang timeout 2m, dump none`을 기록했다.
- TRX counters 는 total 73, executed 73, passed 73, failed 0, notExecuted 0이다.
- `TcpLoopback_WhenIoUringAvailable_SendsQueuedPayloadToPeer`는 Passed 다.
- `Lease_WhenLinuxCapabilityAvailable_WritesRegisteredPayloadSliceToSocketPair`는 Passed 다.
- `WriteFixed_WhenLinuxCapabilityAvailable_WritesRegisteredBufferSliceToSocketPair`는 Passed 다.
- stdout 은 `io_uring capability status: Available`, `fixed socket write completion result: 2`를 포함한다.

### 결과
- D215 workflow hang diagnostics contract 는 원격 Linux runner 에서 artifact evidence 를 남기며 통과했다.
- 이 gate 는 관측성 보강을 닫는 것이며, fixed-write production 재연결, registration cache,
  zero-copy send, default backend promotion 의 직접 근거로 확장하지 않는다.
- 다음 실행 지점은 D216 evidence 기준으로 io_uring 후속 후보를 재평가하는 것이다.

## 2026-07-08 (Codex - D215 contract hang diagnostics)

### 작업 단위
- `iouring-linux-contract.yml`에 test hang diagnostics 를 추가하고 full local verification 까지 완료했다.

### 변경 내용
- `BenchmarkArtifactWorkflowTests`에 Linux contract workflow 가 `--blame-hang`, 2분 hang timeout,
  dump 없음, VSTest diag log, summary evidence line 을 포함하는지 검증하는 static contract test 를 추가했다.
- `iouring-linux-contract.yml`의 `dotnet test` command 에 `--blame-hang`,
  `--blame-hang-timeout 2m`, `--blame-hang-dump-type none`,
  `--diag "$IOURING_CONTRACT_ROOT/vstest-diag.log"`를 추가했다.
- summary artifact 에 `VSTest diag: vstest-diag.log`와
  `Hang diagnostics: blame-hang timeout 2m, dump none`을 기록하게 했다.
- production transport code 는 변경하지 않았다.

### 검증
- Red: focused static contract test 가 `--blame-hang` 부재로 `Assert.Contains() Failure`를 냈다.
- Green: `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~IoUringLinuxContractWorkflow -v minimal`
  통과, 2개.
- Relevant: `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj -v minimal` 통과, 116개.
- Relevant: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal` 통과, 73개.
- Full: `dotnet build HighPerformanceSocket.slnx -v minimal` 경고 0/오류 0.
- Full: `dotnet test HighPerformanceSocket.slnx -v minimal` 전체 통과.
- Full: `git diff --check` whitespace 오류 없음.

### 결과
- 다음 실행 지점은 push 이후 원격 `iouring-linux-contract.yml`을 실행해
  `summary.md`, `iouring-tests.trx`, `vstest-diag.log` artifact 와 TRX counters failed 0을 확인하는 것이다.
- fixed-write production 재연결, registration cache, zero-copy send, default backend promotion 은 계속 제외한다.

## 2026-07-08 (Codex - D214 D213 implementation plan)

### 작업 단위
- D213 io_uring Linux contract hang diagnostics 구현 계획을 작성했다.

### 변경 내용
- 구현 계획 `docs/superpowers/plans/2026-07-08-iouring-contract-hang-diagnostics.md`를 추가했다.
- 계획은 Task 1 workflow hang diagnostics contract, Task 2 full local verification,
  Task 3 remote contract gate review 로 구성했다.
- 상태 문서의 현재 실행 지점을 Task 1 구현으로 갱신했다.

### 검증
- 계획은 실제 workflow path 와 기존 workflow static test file 을 기준으로 작성했다.
- plan placeholder scan 과 `git diff --check`로 문서 품질을 확인한다.

### 결과
- 다음 실행 지점은 D214 Task 1 workflow hang diagnostics contract 구현이다.

## 2026-07-08 (Codex - D213 post-D212 next scope)

### 작업 단위
- D212 rollback green evidence 이후 io_uring 후속 후보를 재평가했다.

### 확인 내용
- D210 direct production fixed-write 연결은 run `28907016232`에서 20분 timeout/cancelled 로 실패했고 TRX가 남지 않았다.
- D211 rollback 후 run `28908440081`은 success 로 복귀했다.
- 현재 production TCP payload path 는 다시 `TrySubmitSend` baseline 이다.
- `IoUringFixedSendLease.CreateForSendPump(...)` ownership boundary 는 유지되어 있지만,
  active queue 에서 per-send `RegisterBuffers`/`UnregisterBuffers`를 직접 붙인 D210 방식은 재시도하지 않는다.
- `dotnet test --help` 기준 `--blame-hang-timeout`, `--blame-hang-dump-type none`, `--diag` 옵션을 사용할 수 있다.

### 변경 내용
- 다음 단위를 `iouring-linux-contract.yml` hang diagnostics 보강으로 정했다.
- 설계 문서 `docs/superpowers/specs/2026-07-08-iouring-contract-hang-diagnostics-design.md`를 추가했다.
- D213 결정/현재 TODO/완료 이력을 상태 문서에 반영했다.

### 검증
- 문서 전용 변경이므로 placeholder scan 과 `git diff --check`로 검증한다.

### 결과
- 다음 실행 지점은 D213 구현 계획 작성이다.
- fixed-write production 재시도, registration cache, zero-copy send, default backend promotion 은 계속 보류한다.

## 2026-07-08 (Codex - D212 rollback remote gate)

### 작업 단위
- D211 rollback 이후 원격 Linux contract gate 가 green 으로 복귀했는지 확인했다.

### 확인 내용
- 사용자 push 이후 `iouring-linux-contract.yml` run `28908440081`을 실행했다.
- head SHA 는 `a20acf2791ae6c3194ed90ce160b7b46e49d0544`이다.
- workflow/job conclusion 은 success 다.
- artifact `iouring-linux-contract-2026-07-08-github-28908440081-1`를 내려받았다.
- summary test exit code 는 0이다.
- TRX counters 는 total 73, executed 73, passed 73, failed 0, notExecuted 0이다.
- `TcpLoopback_WhenIoUringAvailable_SendsQueuedPayloadToPeer`는 Passed 다.
- `Lease_WhenLinuxCapabilityAvailable_WritesRegisteredPayloadSliceToSocketPair`는 Passed 다.
- `WriteFixed_WhenLinuxCapabilityAvailable_WritesRegisteredBufferSliceToSocketPair`는 Passed 다.
- capability stdout 은 `io_uring capability status: Available`이고,
  socket fixed-write stdout 은 `fixed socket write completion result: 2`다.

### 결과
- D211 rollback 으로 원격 Linux contract baseline 은 green 으로 복귀했다.
- D210 direct production payload `WRITE_FIXED` 연결은 failed attempt 로 남긴다.
- 같은 per-send registration + active send pump fixed-write 패턴을 바로 재시도하지 않는다.
- 다음 실행 지점은 D212 evidence 기준 후속 후보 재평가다.

## 2026-07-08 (Codex - D211 remote fixed payload gate failure)

### 작업 단위
- D210 TCP payload fixed-write helper 의 원격 Linux contract gate 를 실행하고, timeout failure 에 대응했다.

### 확인 내용
- 사용자 push 이후 `iouring-linux-contract.yml` run `28907016232`를 실행했다.
- head SHA 는 `e7417c680d28ca7c4a8fafe90ee7db1ac014be36`이다.
- Restore, Build, dotnet info capture 단계는 성공했다.
- `Run io_uring tests` 단계는 20분 동안 완료되지 않아 `cancelled`가 됐다.
- artifact `iouring-linux-contract-2026-07-07-github-28907016232-1`는 `summary.md`와 `dotnet-info.txt`만 포함했고,
  test exit code 는 `not-run`이다. TRX 는 test 단계가 완료되지 않아 남지 않았다.
- job cleanup 은 orphan `dotnet` process 여러 개를 종료했다.

### 변경 내용
- `IoUringTransport.SendInFlightAsync` payload path 를 기존 `SendArrayAsync(...)`/`TrySubmitSend(...)`로 rollback 했다.
- `SendFixedPayloadAsync(...)` helper 와 해당 shape test 를 제거했다.
- `IoUringFixedSendLease.CreateForSendPump(...)` ownership boundary 와 tests 는 유지했다.
- D211 결정 문서에 D210 direct production 연결 실패와 rollback 이유를 기록했다.

### 검증
- Remote Red: `iouring-linux-contract.yml` run `28907016232` timeout/cancelled.
- Local rollback validation 은 focused io_uring tests, solution build/test, `git diff --check`로 수행한다.

### 결과
- broken production payload fixed-write path 는 유지하지 않는다.
- 다음 실행 지점은 rollback 커밋 push 이후 원격 Linux contract gate green 복귀 확인이다.
- 이후 fixed-write production 재시도는 active queue 에서 per-send registration 을 쓰지 않고,
  queue/transport lifetime registration 또는 별도 isolated registration/completion boundary 를 먼저 설계해야 한다.

## 2026-07-08 (Codex - D210 TCP payload fixed-write helper)

### 작업 단위
- D208 Task 2 TCP payload fixed-write helper 를 구현했다.

### 변경 내용
- `IoUringTransport.SendInFlightAsync`의 non-empty payload 전송 구간을 `SendFixedPayloadAsync(...)` 호출로 바꿨다.
- `SendFixedPayloadAsync(...)`는 `IoUringFixedSendLease.CreateForSendPump(...)`로 lease-owned ref 와 fixed buffer registration 을 잡고,
  `IoUringQueue.TrySubmitWriteFixed(...)` completion loop 로 payload 를 전송한다.
- TCP length prefix 는 기존 `LengthPrefixBlock` + `SendArrayAsync(...)` + `TrySubmitSend(...)` 경로를 유지했다.
- `IoUringSendPumpShapeTests`에 fixed payload helper shape test 를 추가했다.
- D210 결정 문서에 payload-only fixed-write 전환과 remote Linux gate 필요성을 기록했다.

### 검증
- Red: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringSendPumpShapeTests -v minimal`
  실행 시 `SendFixedPayloadAsync` 부재로 `Assert.NotNull() Failure`가 발생했다.
- Green: 같은 focused command 통과, 3개.
- Green: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal` 통과, 74개.
- Full: `dotnet build HighPerformanceSocket.slnx -v minimal` 경고 0/오류 0.
- Full: `dotnet test HighPerformanceSocket.slnx -v minimal` 전체 통과.
- Full: `git diff --check` whitespace 오류 없음.

### 결과
- production TCP send pump 의 payload 구간은 fixed-write path 로 연결됐다.
- Linux native evidence 는 아직 local Windows에서 직접 실행되지 않았으므로,
  다음 실행 지점은 push 이후 원격 `iouring-linux-contract.yml` gate 검토다.
- 이 결과는 zero-copy send, registration cache, UDP fixed-buffer send, default backend promotion 근거가 아니다.

## 2026-07-08 (Codex - D209 send pump lease ref acquisition)

### 작업 단위
- D208 Task 1 send pump lease ref acquisition 을 구현했다.

### 변경 내용
- `IoUringFixedSendLease.CreateForSendPump(IoUringQueue, TransportSendBuffer)`를 추가했다.
- `IoUringFixedSendLease.CreateForSendPump(TransportSendBuffer, Func<TransportSendBuffer, IIoUringFixedBufferRegistration>)`
  test seam overload 를 추가했다.
- factory 는 lease-owned payload ref 를 내부에서 `AddRef`로 획득하고,
  registration 또는 lease 생성 실패 시 `Release`로 rollback 한다.
- `IoUringFixedSendLeaseTests`에 send pump factory shape, dispose 시 lease-owned ref 반환,
  registration 실패 rollback tests 를 추가했다.

### 검증
- Red: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringFixedSendLeaseTests -v minimal`
  실행 시 `CreateForSendPump` 부재로 `CS0117` 컴파일 오류가 발생했다.
  계획에는 허용된 Red였지만, 프로젝트 선호는 assertion failure Red 이므로 다음 task 에서는 reflection Red 를 우선한다.
- Green: 같은 focused command 통과, 9개.
- Green: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal` 통과, 73개.

### 결과
- production send pump 가 사용할 수 있는 lease 전용 ref acquisition 경계를 만들었다.
- 다음 실행 지점은 Task 2 TCP payload fixed-write helper 구현이다.

## 2026-07-08 (Codex - D208 D207 implementation plan)

### 작업 단위
- D207 TCP payload fixed-write integration 설계를 구현 가능한 TDD task 로 쪼갰다.

### 확인 내용
- `IoUringFixedSendLease`는 현재 dispose 시 `_sendBuffer.Buffer.Release()`를 수행한다.
- production `IoUringTransport.SendLoopAsync`는 `InFlightSend`를 `using`으로 잡고, `SendInFlightAsync` 완료 후
  `inFlight.Complete()`와 `InFlightSend.Dispose()` 경계에서 transport-owned ref 를 반환한다.
- 따라서 payload fixed-write path 는 기존 ref 를 공유하지 않고 send pump 전용 extra ref 를 획득해야 한다.
- `SendInFlightAsync`는 현재 prefix 와 payload 모두 `SendArrayAsync`로 보내며, 계획에서는 prefix path 를 유지하고
  payload path 만 `SendFixedPayloadAsync`로 바꾼다.

### 변경 내용
- 구현 계획 `docs/superpowers/plans/2026-07-08-iouring-tcp-payload-fixed-write-integration.md`를 추가했다.
- 계획은 Task 1 send pump lease ref acquisition, Task 2 TCP payload fixed-write helper,
  Task 3 remote Linux contract gate documentation 으로 구성했다.
- 상태 문서의 현재 실행 지점을 Task 1 구현으로 갱신했다.

### 검증
- 계획은 실제 코드의 현재 method names 와 test files 를 기준으로 작성했다.
- `rg` 기반 placeholder scan 과 `git diff --check`로 문서 품질을 확인한다.

### 결과
- 다음 실행 지점은 D208 Task 1 `CreateForSendPump` factory 와 AddRef rollback tests 구현이다.

## 2026-07-08 (Codex - D207 io_uring post-D206 next scope)

### 작업 단위
- D206 원격 Linux contract evidence 이후 io_uring 후속 후보를 재평가했다.

### 확인 내용
- D206 evidence 는 `TcpLoopback_WhenIoUringAvailable_SendsQueuedPayloadToPeer`와
  `Lease_WhenLinuxCapabilityAvailable_WritesRegisteredPayloadSliceToSocketPair`가 모두 Linux capability `Available`
  환경에서 통과했음을 보여준다.
- production TCP send pump 는 아직 length prefix 와 payload 를 모두 `TrySubmitSend` 계열로 보낸다.
- `IoUringFixedSendLease`는 dispose 시 payload ref 를 release 하므로,
  production pump 에서 기존 `InFlightSend` ref 를 그대로 lease 에 넘기면 double release 위험이 있다.

### 변경 내용
- D207 설계 문서 `docs/superpowers/specs/2026-07-08-iouring-post-d206-next-scope-design.md`를 추가했다.
- D207 결정/현재 TODO/완료 이력을 상태 문서에 반영했다.

### 결과
- 다음 구현 후보는 TCP payload fixed-write integration 으로 정했다.
- 단, 첫 구현 계획은 send pump 전용 lease ref 획득/rollback 경계를 먼저 고정해야 한다.
- TCP length prefix fixed-write, UDP fixed-buffer send, zero-copy send, registration cache,
  default backend promotion 은 이번 범위에서 제외한다.

## 2026-07-07 (Codex - D206 D205 remote gate)

### 작업 단위
- D205 TCP send pump task tracking fix 의 원격 `iouring-linux-contract.yml` artifact gate 를 검토했다.

### 확인 내용
- 사용자 push 이후 `iouring-linux-contract.yml` run `28842952688`을 실행했다.
- workflow conclusion 은 success 이고 job `io_uring contract (linux)`도 success 다.
- run metadata:
  head SHA 는 `6e9e14d679740235cfe79f10faae02fc3e356b09`, branch 는 `master`다.
- artifact:
  `iouring-linux-contract-2026-07-07-github-28842952688-1`는 `summary.md`, `dotnet-info.txt`,
  `iouring-tests.trx`를 포함한다.
- summary:
  Ubuntu 24.04 runner, .NET SDK 9.0.315, test exit code 0.
- TRX:
  counters 는 total 70, executed 70, passed 70, failed 0, notExecuted 0이다.
  `TcpLoopback_WhenIoUringAvailable_SendsQueuedPayloadToPeer`는 outcome Passed 로,
  D204/D205에서 반복됐던 pool `RentedCount` leak 단언이 재발하지 않았다.
  `Lease_WhenLinuxCapabilityAvailable_WritesRegisteredPayloadSliceToSocketPair`도 outcome Passed 다.
  capability evidence 는 `io_uring capability status: Available`을 출력했다.
  socket fixed-write evidence 는 `fixed socket write completion result: 2`를 출력했다.

### 결과
- D205 send pump shutdown tracking fix 와 D203 fixed-send lease native evidence 는 원격 Linux contract gate 를 통과했다.
- 이 evidence 는 shutdown ownership race 와 lease native write contract 를 닫는 것이며,
  production TCP pump fixed-write 연결, zero-copy send, default promotion, latency hard gate 의 직접 근거로 즉시 확장하지 않는다.
- 다음 실행 지점은 D206 evidence 기준으로 io_uring 후속 후보를 재평가하는 것이다.

## 2026-07-07 (Codex - D205 io_uring send pump task tracking)

### 작업 단위
- D204 remote gate 재실행 실패를 분석하고, close 로 unregister 된 connection 의 TCP send pump task 추적을 보강했다.

### 확인 내용
- D204 push 이후 `iouring-linux-contract.yml` run `28841586637`을 실행했다.
- workflow 는 동일하게 `Fail if io_uring tests failed` 단계에서 실패했다.
- artifact `iouring-linux-contract-2026-07-07-github-28841586637-1`의 TRX counters 는
  total 69, executed 69, passed 68, failed 1이다.
- 실패 테스트는 여전히 `TcpLoopback_WhenIoUringAvailable_SendsQueuedPayloadToPeer`이고,
  pool `RentedCount` 단언이 expected 0 / actual 1로 실패했다.
- `Lease_WhenLinuxCapabilityAvailable_WritesRegisteredPayloadSliceToSocketPair`는 capability `Available` 상태로 Passed 였다.
- 추가 원인: 테스트는 payload 수신 후 `server.Close()`를 먼저 호출한다.
  이때 connection 이 transport `_connections` 목록에서 unregister 되어 `StopAsync`의 open connection snapshot 에서 빠진다.

### 변경 내용
- `IoUringTransport`:
  TCP send pump task 를 connection list 와 별도로 추적하는 `_connectionSendPumpTasks`와
  `TrackConnectionSendPumpTask`/`RemoveConnectionSendPumpTask`를 추가했다.
- `IoUringTransport.StopAsync`/`Dispose`:
  `StopCore()` snapshot 의 send pump task 들을 기다린 뒤 TCP in-flight send drain 과 native owner dispose 를 수행한다.
- `IoUringSendPumpShapeTests`:
  tracked send pump task 가 완료되기 전에는 `StopAsync`가 완료되지 않는지 검증하는 behavior test 를 추가했다.
- 상태 문서:
  D205 결정/현재 TODO/다음 remote gate 를 반영했다.

### 검증
- Red: `StopAsync_WhenTcpSendPumpTaskIsTracked_WaitsForTaskCompletion`이
  `Assert.NotNull() Failure`로 tracking surface 부재를 확인했다.
- Green: focused tracking test 통과.
- Green: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal`
  통과, 70개.
- Green: `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter FullyQualifiedName~TransportSendQueueTests -v minimal`
  통과, 14개.
- Full: `dotnet build HighPerformanceSocket.slnx -v minimal` 경고 0/오류 0.
- Full: `dotnet test HighPerformanceSocket.slnx -v minimal` 전체 통과.
- Full: `git diff --check` whitespace 오류 없음.

### 결과
- `server.Close()`로 이미 unregister 된 connection 의 send pump unwind 도 transport shutdown 에서 기다릴 수 있게 됐다.
- 다음 실행 지점은 push 이후 `iouring-linux-contract.yml`을 다시 실행해 failed 0을 확인하는 것이다.
- remote gate 전에는 production TCP pump fixed-write 연결, zero-copy send, default promotion 으로 확장하지 않는다.

## 2026-07-07 (Codex - D204 io_uring TCP in-flight drain fix)

### 작업 단위
- D203 remote gate 실패를 분석하고 io_uring TCP send shutdown drain race 를 보정했다.

### 확인 내용
- 사용자 push 이후 `iouring-linux-contract.yml` run `28840613527`을 실행했다.
- workflow 는 `Fail if io_uring tests failed` 단계에서 실패했다.
- artifact `iouring-linux-contract-2026-07-07-github-28840613527-1`의 TRX counters 는
  total 69, executed 69, passed 68, failed 1이다.
- `Lease_WhenLinuxCapabilityAvailable_WritesRegisteredPayloadSliceToSocketPair`는 capability `Available` 상태로 Passed 였다.
- 실패 테스트는 `TcpLoopback_WhenIoUringAvailable_SendsQueuedPayloadToPeer`이고,
  pool `RentedCount` 단언이 expected 0 / actual 1로 실패했다.

### 변경 내용
- `TransportConnection`:
  pending queue 에서 dequeue 된 in-flight send count 를 추적하고,
  `WaitForInFlightSendsToDrainAsync()`로 pump finally/release 완료를 기다릴 수 있게 했다.
- `IoUringTransport`:
  `StopAsync`에서 close 이후 TCP connection 의 in-flight send ref 반환 완료를 기다리게 했다.
- `TransportSendQueueTests`:
  in-flight ref 가 남아 있으면 drain task 가 완료되지 않고,
  `InFlightSend.Dispose()` 후 완료되는 계약 테스트를 추가했다.
- 상태 문서:
  D203 remote gate 를 완료 항목으로 이동하고, D204 fix 이후 재실행해야 할 remote gate 를 current TODO 로 남겼다.

### 검증
- Red: `InFlightSendDrain_WhenSendIsStillInFlight_CompletesAfterHandleReleasesRef`가
  `Assert.NotNull() Failure`로 drain surface 부재를 확인했다.
- Green: focused drain test 통과.
- Green: `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter FullyQualifiedName~TransportSendQueueTests -v minimal`
  통과, 14개.
- Green: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal`
  통과, 69개.
- Full: `dotnet build HighPerformanceSocket.slnx -v minimal` 경고 0/오류 0.
- Full: `dotnet test HighPerformanceSocket.slnx -v minimal` 전체 통과.
- Full: `git diff --check` whitespace 오류 없음.

### 결과
- 원격 failure 는 새 lease native evidence 자체가 아니라 기존 TCP send shutdown 관측 race 로 분리됐다.
- 다음 실행 지점은 push 이후 `iouring-linux-contract.yml`을 다시 실행해 failed 0을 확인하는 것이다.
- remote gate 전에는 production TCP pump fixed-write 연결, zero-copy send, default promotion 으로 확장하지 않는다.

## 2026-07-07 (Codex - D203 fixed send lease native evidence)

### 작업 단위
- D200 Task 3 Linux native lease evidence 를 로컬 구현했다.

### 변경 내용
- `IoUringFixedSendLeaseTests`:
  test-only `LinuxSocketPair` helper 를 추가했다.
- `IoUringFixedSendLeaseTests`:
  `Lease_WhenLinuxCapabilityAvailable_WritesRegisteredPayloadSliceToSocketPair`를 추가했다.
  Linux capability available 환경에서 lease 가 소유한 registered buffer slice 를 `TrySubmitWriteFixed`로 stream socket fd 에 제출하고,
  peer socket 에서 `{20,30}`을 읽는다.
- 상태 문서:
  다음 실행 지점을 push 이후 원격 `iouring-linux-contract.yml` artifact gate 검토로 갱신했다.

### 검증
- Red: `LinuxSocketPair_HelperExistsForLeaseNativeEvidence`가 `Assert.NotNull() Failure`.
- Green: focused native evidence test 로컬 guard 통과.
- Green: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal` 통과, 69개.
- Full: `dotnet test HighPerformanceSocket.slnx -v minimal` 통과.

### 결과
- fixed-send lease native evidence test 는 로컬에서 준비됐고, 실제 Linux native body 는 원격 contract gate 에서 확인해야 한다.
- remote gate 전에는 production TCP pump fixed-write 연결 근거로 쓰지 않는다.

## 2026-07-07 (Codex - D202 fixed send lease factory)

### 작업 단위
- D200 Task 2 queue-based real registration factory 를 구현했다.

### 변경 내용
- `IoUringFixedSendLease.Create(IoUringQueue, TransportSendBuffer)`를 추가했다.
- factory 는 `TransportSendBuffer` payload slice 의 underlying array 를 `IoUringRegisteredBufferSet.Register(...)`로 등록하고,
  buffer index 0, payload offset/length 를 가진 lease 를 반환한다.
- `IoUringFixedSendLeaseTests`에 queue-based factory shape contract 를 추가했다.
- 상태 문서:
  다음 실행 지점을 D200 Task 3 Linux native lease evidence 로 갱신했다.

### 검증
- Red: `LeaseFactory_WhenInspected_ExposesQueueBasedCreateMethod`가 `Assert.NotNull() Failure`.
- Green: focused `IoUringFixedSendLeaseTests` 4개 통과.
- Green: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal` 통과, 67개.

### 결과
- real `IoUringRegisteredBufferSet` 기반 lease factory shape 가 생겼다.
- 아직 Linux native socket write evidence 와 production TCP pump 연결은 하지 않았다.

## 2026-07-07 (Codex - D201 fixed send lease ownership)

### 작업 단위
- D200 Task 1 pure lease ownership contract 를 구현했다.

### 변경 내용
- `IoUringFixedSendLease`:
  `TransportSendBuffer` slice metadata 와 registration owner 를 하나의 lease 로 묶고,
  dispose 시 registration owner 와 payload ref 를 정확히 1회 정리하게 했다.
- `IoUringRegisteredBufferSet`:
  `IIoUringFixedBufferRegistration` internal interface 를 구현하게 했다.
- `IoUringFixedSendLeaseTests`:
  reflection surface Red, ownership cleanup Red/Green, slice range surface 검증을 추가했다.
- 상태 문서:
  다음 실행 지점을 D200 Task 2 queue-based real registration factory 로 갱신했다.

### 검증
- Red: surface type 부재 `Assert.NotNull() Failure`.
- Red: behavior tests 2개 `NotImplementedException`.
- Green: focused `IoUringFixedSendLeaseTests` 3개 통과.
- Green: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal` 통과, 66개.

### 결과
- fixed-write production pump 연결 전 payload ref 와 registration owner 를 함께 정리하는 pure lease contract 가 생겼다.
- 아직 queue-based real registration factory, Linux native socket write evidence, production pump 연결은 하지 않았다.

## 2026-07-07 (Codex - D200 fixed send lease owner implementation plan)

### 작업 단위
- D199 설계에 따라 TCP fixed-send lease owner 구현 계획을 작성했다.

### 변경 내용
- `docs/superpowers/plans/2026-07-07-iouring-fixed-send-lease-owner.md`:
  pure lease ownership contract, queue-based real registration factory, Linux native lease evidence,
  remote contract gate documentation 의 4개 task 로 구현을 나눴다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 Task 1 pure lease ownership contract 로 갱신했다.

### 검증
- 계획 self-review 로 spec coverage, placeholder scan, type consistency 를 확인했다.
- `git diff --check`로 whitespace 오류가 없음을 확인했다.

### 결과
- 다음 구현은 production pump 변경이 아니라 `IoUringFixedSendLease` pure ownership contract 의 Red-Green 구현이다.
- production TCP pump fixed-write 연결, UDP fixed-buffer send, zero-copy send, default promotion, latency hard gate 는 계속 제외한다.

## 2026-07-07 (Codex - D199 io_uring post-D198 next scope)

### 작업 단위
- D198 socket fixed-write 원격 evidence 이후 `io_uring` 후속 후보를 재평가했다.

### 확인 내용
- D198은 stream socket fd 에 `IORING_OP_WRITE_FIXED`로 registered buffer slice 를 쓸 수 있음을 검증했다.
- 현재 production TCP send pump 는 length prefix scratch 전송 뒤 payload 를 `TrySubmitSend`로 보낸다.
- `IoUringRegisteredBufferSet`은 존재하지만 `TransportConnection.InFlightSend`와 `RefCountedBuffer` fan-out ownership lifetime 에 아직 연결되어 있지 않다.
- 따라서 바로 TCP/UDP pump 를 fixed-write 로 바꾸면 native contract, registration lifetime, close drain, length prefix framing 을 동시에 건드리게 된다.

### 변경 내용
- `docs/superpowers/specs/2026-07-07-iouring-post-d198-next-scope-design.md`:
  다음 단위를 TCP fixed-send lease owner 구현 계획으로 정했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-07.md`:
  D199 결정을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  D198 재평가를 완료하고 다음 실행 지점을 D200 구현 계획 작성으로 갱신했다.

### 검증
- 문서 전용 변경이므로 build/test 는 실행하지 않았다.
- `git diff --check`로 whitespace 오류가 없음을 확인했다.

### 결과
- 다음 작업은 `IoUringFixedSendLease` 또는 동등한 internal owner 의 TDD 구현 계획 작성이다.
- production pump fixed-write 연결, UDP fixed-buffer send, zero-copy send, default promotion, latency hard gate 는 계속 제외한다.

## 2026-07-07 (Codex - D198 socket fixed-write remote gate)

### 작업 단위
- D197 socket fixed-write evidence 의 원격 `iouring-linux-contract.yml` artifact gate 를 검토했다.

### 확인 내용
- 사용자 push 이후 `git fetch origin` 기준 로컬 `HEAD`와 `origin/master`가
  `84af508110a1c104c8b484cf138e05c83f8893d8`로 일치함을 확인했다.
- `gh workflow run iouring-linux-contract.yml --ref master`로 run `28837405462`를 실행했다.
- `gh run watch 28837405462 --exit-status`:
  workflow conclusion success, job `io_uring contract (linux)` success.
- run metadata:
  head SHA 는 `84af508110a1c104c8b484cf138e05c83f8893d8`, branch 는 `master`다.
- workflow log:
  Ubuntu 24.04 runner 에서 project-scoped restore/build 가 통과했고, build 는 warning 0/error 0이다.
  `Hps.Transport.IoUring.Tests`는 Failed 0, Passed 63, Skipped 0, Total 63으로 통과했다.
- artifact:
  `iouring-linux-contract-2026-07-07-github-28837405462-1`는 `summary.md`, `dotnet-info.txt`, `iouring-tests.trx`를 포함한다.
- TRX:
  counters 는 total 63, executed 63, passed 63, failed 0, notExecuted 0이다.
  `WriteFixed_WhenLinuxCapabilityAvailable_WritesRegisteredBufferSliceToSocketPair`는 outcome Passed,
  stdout 은 `io_uring capability status: Available`, `fixed socket write completion result: 2`를 기록했다.

### 결과
- D197 socket fixed-write native evidence gate 는 원격 Linux에서 충족됐다.
- 이 evidence 는 stream socket fd 에 `WRITE_FIXED`로 registered buffer slice 를 쓸 수 있음을 닫는 것이며,
  TCP/UDP pump fixed-buffer 연결, zero-copy send, default promotion, latency hard gate 의 직접 근거는 아니다.
- 다음 실행 지점은 D198 evidence 기준으로 io_uring 후속 후보를 재평가하는 것이다.

## 2026-07-07 (Codex - D197 io_uring socket fixed-write local evidence)

### 작업 단위
- D196 fixed-write socket fd contract evidence 를 로컬 구현했다.

### 변경 내용
- `IoUringFixedBufferSubmissionTests`:
  `LinuxSocketPair_HelperExistsForSocketFixedWriteEvidence` contract test 를 추가했다.
- `IoUringFixedBufferSubmissionTests`:
  test-only `LinuxSocketPair` helper 를 추가했다.
- `IoUringFixedBufferSubmissionTests`:
  `WriteFixed_WhenLinuxCapabilityAvailable_WritesRegisteredBufferSliceToSocketPair`를 추가했다.
  Linux capability available 환경에서 registered buffer `{10,20,30,40}` offset 1 length 2를
  `TrySubmitWriteFixed`로 stream socket fd 에 제출하고 peer socket 에서 `{20,30}`을 읽는다.
- 상태 문서:
  다음 실행 지점을 원격 `iouring-linux-contract.yml` artifact gate 검토로 넘겼다.

### 검증
- Red: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter LinuxSocketPair_HelperExistsForSocketFixedWriteEvidence -v minimal`
  실행 결과 `Assert.NotNull() Failure: Value is null`로 실패함을 확인했다.
- Green: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringFixedBufferSubmissionTests -v minimal`
  통과, 3개.
- Green: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal`
  통과, 63개.
- Full: `dotnet test HighPerformanceSocket.slnx -v minimal`
  통과, 전체 467개.

### 결과
- Windows/local에서는 native body 가 capability guard 로 early-return 한다.
- 실제 socket fd fixed-write evidence 는 사용자 push 이후 원격 Linux contract artifact 에서 확인해야 한다.
- TCP/UDP pump fixed-buffer 연결, zero-copy send, default promotion, latency hard gate 는 계속 제외한다.

## 2026-07-07 (Codex - D196 io_uring post-D195 next scope)

### 작업 단위
- D195 fixed-write 원격 evidence 이후 `io_uring` 후속 후보를 재평가했다.

### 확인 내용
- D195 run `28834265348`은 fixed-write helper 가 registered buffer slice 를 pipe fd 로 쓸 수 있음을 검증했다.
- 현재 production TCP send pump 는 아직 `TrySubmitSend`를 사용하고, UDP send pump 는 `TrySubmitSendMessage`를 사용한다.
- `IoUringRegisteredBufferSet`은 존재하지만 production `TransportConnection`/`RefCountedBuffer` lifetime 에 연결되어 있지 않다.
- 따라서 D195를 TCP/UDP pump fixed-buffer 연결, zero-copy send, default promotion, latency hard gate 근거로 확장하지 않는다.

### 변경 내용
- `docs/superpowers/specs/2026-07-07-iouring-post-d195-next-scope-design.md`:
  D196 다음 단위를 fixed-write socket fd contract evidence 로 정했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-07.md`:
  D196 결정을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  현재 실행 지점을 Linux capability gated `socketpair(AF_UNIX, SOCK_STREAM)` evidence 구현으로 갱신했다.

### 결과
- 다음 구현은 production pump 변경이 아니라 test-only socketpair evidence 다.
- 이 구현은 registered buffer `{10,20,30,40}` offset 1 length 2를 `TrySubmitWriteFixed`로 stream socket fd 에 쓰고,
  peer socket 에서 `{20,30}`을 읽는지 검증한다.
- TCP/UDP pump fixed-buffer 연결, zero-copy send, default promotion, latency hard gate 는 계속 제외한다.

## 2026-07-07 (Codex - D195 D181 fixed-write remote gate)

### 작업 단위
- D194 workflow fix push 이후 D181 fixed-buffer SQE submission evidence 의 원격 `iouring-linux-contract.yml` artifact gate 를 검토했다.

### 확인 내용
- `gh workflow run iouring-linux-contract.yml --ref master`로 run `28834265348`을 실행했다.
- `gh run watch 28834265348 --exit-status`:
  workflow conclusion success, job `io_uring contract (linux)` success.
- run metadata:
  head SHA 는 `848ce55341945a83d61023d7e54add5906fd7590`, branch 는 `master`다.
- workflow log:
  Ubuntu 24.04 runner 에서 project-scoped restore/build 가 통과했고, build 는 warning 0/error 0이다.
  `Hps.Transport.IoUring.Tests`는 Failed 0, Passed 61, Skipped 0, Total 61로 통과했다.
- artifact:
  `iouring-linux-contract-2026-07-07-github-28834265348-1`는 `summary.md`, `dotnet-info.txt`, `iouring-tests.trx`를 포함한다.
- TRX:
  counters 는 total 61, executed 61, passed 61, failed 0, notExecuted 0이다.
  `WriteFixed_WhenLinuxCapabilityAvailable_WritesRegisteredBufferSliceToPipe`는 outcome Passed,
  stdout 은 `io_uring capability status: Available`, `fixed write completion result: 2`를 기록했다.
  테스트 본문은 registered buffer `{10,20,30,40}`의 offset 1 length 2를 WRITE_FIXED로 pipe 에 쓰고
  pipe payload `{20,30}`을 assertion 으로 검증한다.

### 결과
- D181 fixed-write SQE helper/native completion evidence gate 는 원격 Linux에서 충족됐다.
- 이 evidence 는 fixed-write SQE field mapping 과 kernel completion contract 를 닫는 것이며,
  TCP/UDP pump fixed-buffer 연결, zero-copy send, default promotion, latency hard gate 의 직접 근거는 아니다.
- 다음 실행 지점은 D195 evidence 기준으로 io_uring 후속 후보를 재평가하는 것이다.

## 2026-07-07 (Codex - D194 io_uring Linux contract workflow scope fix)

### 작업 단위
- D181 fixed-write 원격 gate 재실행 중 발견한 `iouring-linux-contract.yml` Linux restore 실패를 보정했다.

### 확인 내용
- 사용자 push 이후 `git fetch origin` 기준 로컬 `master`와 `origin/master`가 일치함을 확인했다.
- `gh workflow run iouring-linux-contract.yml --ref master`로 run `28833852810`을 실행했다.
- run `28833852810`은 `Restore` 단계에서 실패했다.
- 실패 원인은 Linux runner가 `dotnet restore HighPerformanceSocket.slnx`를 실행하면서
  WPF sample dashboard `net9.0-windows` project 를 restore 대상에 포함했고,
  `NETSDK1100: To build a project targeting Windows on this operating system, set the EnableWindowsTargeting property to true.`가 발생한 것이다.

### 변경 내용
- `.github/workflows/iouring-linux-contract.yml`:
  restore/build 범위를 solution 에서 `tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj`로 좁혔다.
- `BenchmarkArtifactWorkflowTests`:
  Linux contract workflow 가 io_uring test project 만 restore/build/test 하고,
  solution restore/build 또는 `EnableWindowsTargeting` 우회를 사용하지 않는지 static contract test 를 추가했다.
- `DECISIONS.md`:
  D194로 Linux contract workflow 의 project-scoped restore/build 경계를 기록했다.

### 검증
- Red: `IoUringLinuxContractWorkflow_WhenRunOnLinux_RestoresAndBuildsOnlyIoUringTestProject`가
  기존 workflow 의 project restore command 부재로 `Assert.Contains()` 실패함을 확인했다.
- Green: 같은 focused test 통과.
- Green: `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter BenchmarkArtifactWorkflowTests -v minimal` 통과, 7개.

### 결과
- D181 fixed-write evidence 자체는 아직 원격에서 검증되지 않았다.
- 다음 실행 지점은 D194 workflow fix 를 push 한 뒤 `iouring-linux-contract.yml`을 다시 실행해
  `WriteFixed_WhenLinuxCapabilityAvailable_WritesRegisteredBufferSliceToPipe`,
  `fixed write completion result: 2`, pipe payload `[20, 30]` evidence 를 확인하는 것이다.

## 2026-07-07 (Codex - D193 D181 remote gate status)

### 작업 단위
- D181 fixed-buffer SQE submission evidence 의 원격 `iouring-linux-contract.yml` artifact gate 가능 여부를 확인했다.

### 확인 내용
- `gh run list --workflow iouring-linux-contract.yml --limit 10`:
  최신 run 은 `28631346969`, conclusion `success`, event `workflow_dispatch`, created `2026-07-03T00:58:33Z`다.
- `gh run view 28631346969 --json ...`:
  head SHA 는 `19701fceaadff1feaf1bd1aa98421879937e4f4c`다.
- git ancestry 확인:
  `19701fce`는 `test(iouring): cover fixed buffer registration`이고,
  D181 핵심 커밋 `7109edd test(iouring): cover fixed buffer write submission`은 이 run 에 포함되지 않는다.
- `gh run view 28631346969 --log`:
  Linux run 자체는 test exit code 0, `Hps.Transport.IoUring.Tests` 58개 통과로 완료됐다.
  다만 이 통과는 D177 register/unregister gate 에 대한 것이며 D181 fixed-write gate 는 아니다.

### blocker
- 현재 로컬 브랜치는 `origin/master`보다 16커밋 앞서 있고 D181 커밋도 그 안에 있다.
- 이 세션에서 `git push`를 시도했지만 실행 정책이 `git push`를 거부했다.
- 따라서 D181 remote gate 는 사용자가 push 한 뒤 `iouring-linux-contract.yml`을 다시 실행해야 닫을 수 있다.

### 결과
- D181 remote gate 는 아직 미완료다.
- 다음 실행 지점은 push 이후 새 workflow run 의 TRX/summary 에서
  `WriteFixed_WhenLinuxCapabilityAvailable_WritesRegisteredBufferSliceToPipe`,
  `fixed write completion result: 2`, pipe payload `[20, 30]` evidence 를 확인하는 것이다.

## 2026-07-07 (Codex - D192 WPF dashboard smoke copy)

### 작업 단위
- WPF sample dashboard 의 TCP/UDP smoke 버튼이 self-contained transient server 를 사용하는 의미를 UI/README에 명확히 드러냈다.

### 변경 내용
- `MainWindow.xaml`:
  subtitle 을 `TCP/UDP 독립 loopback smoke와 transport diagnostics`로 변경했다.
- `README.md`:
  `TCP smoke`와 `UDP smoke`가 Start server와 별개로 임시 loopback server 를 만드는 독립 검증임을 명시했다.
- `DashboardProjectContractTests`:
  UI와 README copy 가 이 차이를 설명하는지 contract test 를 추가했다.
- 상태 문서:
  WPF sample dashboard current work 를 완료로 이동하고, deferred 되어 있던 D181 원격 `iouring-linux-contract.yml` artifact gate 를
  current TODO 로 다시 승격했다.

### 검증
- Red: `DashboardCopy_WhenInspected_ExplainsSmokeCommandsAreIndependentLoopbackChecks`가 `독립 loopback smoke` 문구 부재로 실패함을 확인했다.
- Green: focused copy contract test 통과.
- Green: `dotnet test tests\Hps.Sample.Dashboard.Tests\Hps.Sample.Dashboard.Tests.csproj -v minimal` 통과, 13개.
- Full: `dotnet build HighPerformanceSocket.slnx -v minimal` 경고 0/오류 0.
- Full: `dotnet test HighPerformanceSocket.slnx -v minimal` 전체 통과.
- GUI: WPF 앱을 실제 실행해 subtitle 변경이 화면에 표시됨을 확인했다.

### 결과
- Start server diagnostics 와 self-contained smoke 검증의 의미가 UI/README에서 분리됐다.
- 다음 실행 후보는 D181 fixed-buffer SQE submission evidence 의 원격 Linux contract artifact gate 검토다.

## 2026-07-07 (Codex - D191 WPF dashboard runtime review fix)

### 작업 단위
- WPF sample dashboard 를 실제 실행해 UI/버튼 동작을 검토하고, TCP/UDP 상태 카드가 마지막 smoke 결과를 공유하는 오류를 수정했다.

### 변경 내용
- `DashboardViewModel`:
  `TcpSmokeSummary`와 `UdpSmokeSummary`를 추가하고 `ApplySmokeResult`가 protocol 별 summary 를 각각 갱신하게 했다.
  기존 `LastSmokeSummary`는 마지막 smoke 결과 요약 호환 surface 로 유지했다.
- `MainWindow.xaml`:
  TCP 카드와 UDP 카드가 더 이상 `LastSmokeSummary`를 공유하지 않고 각각 `TcpSmokeSummary`, `UdpSmokeSummary`를 표시한다.
- `DashboardViewModelTests`:
  TCP smoke 실행 후 UDP summary 가 비어 있고, UDP smoke 실행 후 TCP summary 가 보존되는 회귀 테스트를 추가했다.

### 검증
- GUI: WPF 앱을 실행하고 Start server, TCP smoke, UDP smoke, Stop server 를 직접 눌러 확인했다.
- Red: `SmokeCommands_WhenExecuted_UpdateProtocolSpecificSummaries`가 `TcpSmokeSummary` 속성 부재로 `Assert.NotNull()` 실패함을 확인했다.
- Green: focused test 통과.
- Green: `dotnet test tests\Hps.Sample.Dashboard.Tests\Hps.Sample.Dashboard.Tests.csproj -v minimal` 통과, 12개.
- Full: `dotnet build HighPerformanceSocket.slnx -v minimal` 경고 0/오류 0.
- Full: `dotnet test HighPerformanceSocket.slnx -v minimal` 전체 통과.
- GUI 재검증: TCP 카드에는 TCP 결과, UDP 카드에는 UDP 결과가 따로 표시됨을 확인했다.

### 결과
- TCP/UDP smoke 기능은 기존처럼 성공하면서, 프로토콜별 상태 카드의 의미가 실제 UI에서 맞게 표시된다.
- 남은 개선 후보는 smoke 버튼이 self-contained transient server 를 사용하는 의미를 UI 문구/README에서 명확히 하는 것이다.

## 2026-07-06 (Codex - D190 WPF sample dashboard Task 6)

### 작업 단위
- D184 계획 Task 6 WPF UI binding, run instructions, full verification 을 구현했다.

### 변경 내용
- `DashboardViewModel`:
  기본 생성자에서 `DashboardBrokerService`, `DiagnosticsSnapshotService`, `TcpSmokeTestService`,
  `UdpSmokeTestService`, `IoUringEvidenceStatusService`를 연결하고 TCP/UDP smoke 결과를 log/summary 에 반영한다.
- `MainWindow.xaml`, `MainWindow.xaml.cs`:
  WPF DataContext, Start/Stop/TCP smoke/UDP smoke 버튼, server/TCP/UDP/io_uring status,
  diagnostics grid, run log 를 바인딩했다.
- `IoUringEvidenceStatusService`, `README.md`:
  Windows WPF 앱에서는 Linux native `io_uring` path 를 직접 실행하지 않고 원격 contract gate 로 확인한다는 상태와
  사용자가 직접 실행할 명령을 기록했다.
- `DashboardViewModelTests`:
  UI command 가 service 결과를 log/summary 에 반영하는 orchestration test 를 추가했다.

### 검증
- Red: `RunTcpSmokeCommand_WhenExecuted_AddsResultToLog`가 `CreateForTests` 부재로 `Assert.NotNull()` 실패함을 확인했다.
- Green: `dotnet test tests\Hps.Sample.Dashboard.Tests\Hps.Sample.Dashboard.Tests.csproj -v minimal` 통과, 11개 통과.
- Green: `dotnet build samples\Hps.Sample.Dashboard\Hps.Sample.Dashboard.csproj -v minimal` 경고 0/오류 0.
- Full: `dotnet build HighPerformanceSocket.slnx -v minimal` 경고 0/오류 0.
- Full: `dotnet test HighPerformanceSocket.slnx -v minimal` 전체 통과.
- Whitespace: `git diff --check` 통과.

### 결과
- 사용자가 `dotnet run --project samples\Hps.Sample.Dashboard\Hps.Sample.Dashboard.csproj`로 WPF dashboard 를 실행해
  Interface Server TCP/UDP smoke 와 diagnostics 표시를 직접 확인할 수 있게 됐다.
- GUI 앱은 장시간 실행되는 대화형 프로세스라 자동 실행하지 않았고, 다음 지점은 사용자 수동 UI 검토다.

## 2026-07-06 (Codex - D189 WPF sample dashboard Task 5)

### 작업 단위
- D184 계획 Task 5 UDP smoke service 를 구현했다.

### 변경 내용
- `UdpSmokeTestService`:
  실제 SAEA UDP endpoint receive/send pump 와 Broker datagram handler fan-out 으로 UDP SUBSCRIBE/PUBLISH loopback 을 실행한다.
- `UdpSmokeTestServiceTests`:
  sent=1, received=1, dropped=0, payload-errors=0, pool-rented=0 결과를 검증한다.
- 상태 문서와 구현 계획 체크박스를 Task 5 완료, Task 6 UI binding/full verification 진입점으로 갱신했다.

### 검증
- Red: `UdpSmokeTestService` type 부재 assertion failure 를 확인했다.
- Green: `dotnet test tests\Hps.Sample.Dashboard.Tests\Hps.Sample.Dashboard.Tests.csproj --filter UdpSmokeTestServiceTests -v minimal` 통과.
- Green: `dotnet build samples\Hps.Sample.Dashboard\Hps.Sample.Dashboard.csproj -v minimal` 경고 0/오류 0.

### 결과
- WPF dashboard 에서 호출할 수 있는 UDP end-to-end smoke service 가 준비됐다.
- 다음 실행 지점은 Task 6 UI binding, run instructions, full verification 이다.

## 2026-07-06 (Codex - D188 WPF sample dashboard Task 4)

### 작업 단위
- D184 계획 Task 4 TCP smoke service 를 구현했다.

### 변경 내용
- `TcpSmokeTestService`:
  실제 SAEA TCP listener/receive/send pump 와 Broker fan-out 으로 TCP SUBSCRIBE/PUBLISH loopback 을 실행한다.
- `TcpSmokeTestServiceTests`:
  sent=1, received=1, dropped=0, payload-errors=0, pool-rented=0 결과를 검증한다.
- 상태 문서와 구현 계획 체크박스를 Task 4 완료, Task 5 UDP smoke 진입점으로 갱신했다.

### 검증
- Red: `TcpSmokeTestService` type 부재 assertion failure 를 확인했다.
- Green: `dotnet test tests\Hps.Sample.Dashboard.Tests\Hps.Sample.Dashboard.Tests.csproj --filter TcpSmokeTestServiceTests -v minimal` 통과.
- Green: `dotnet build samples\Hps.Sample.Dashboard\Hps.Sample.Dashboard.csproj -v minimal` 경고 0/오류 0.

### 결과
- WPF dashboard 에서 호출할 수 있는 TCP end-to-end smoke service 가 준비됐다.
- 다음 실행 지점은 Task 5 UDP smoke service 다.

## 2026-07-06 (Codex - D187 WPF sample dashboard Task 3)

### 작업 단위
- D184 계획 Task 3 Broker lifecycle 와 diagnostics service 를 구현했다.

### 변경 내용
- `DashboardBrokerService`:
  SAEA transport, pinned pool, `BrokerServer` lifecycle 을 묶고 diagnostics source 로 같은 transport 참조를 노출한다.
- `DiagnosticsSnapshotService`:
  `ITransportDiagnostics` aggregate snapshot 을 TCP/UDP `TransportMetricRow`로 변환한다.
- `DiagnosticsSnapshotServiceTests`:
  diagnostics service 와 broker lifecycle public boundary 를 assertion Red 로 검증했다.
- 상태 문서와 구현 계획 체크박스를 Task 3 완료, Task 4 TCP smoke 진입점으로 갱신했다.

### 검증
- Red: diagnostics service 와 broker service type 부재 assertion failure 를 확인했다.
- Green: `dotnet test tests\Hps.Sample.Dashboard.Tests\Hps.Sample.Dashboard.Tests.csproj --filter DiagnosticsSnapshotServiceTests -v minimal` 통과.
- Green: `dotnet build samples\Hps.Sample.Dashboard\Hps.Sample.Dashboard.csproj -v minimal` 경고 0/오류 0.

### 결과
- WPF dashboard 가 server lifecycle 과 diagnostics snapshot 을 연결할 service 경계를 갖췄다.
- 다음 실행 지점은 Task 4 TCP smoke service 다.

## 2026-07-06 (Codex - D186 WPF sample dashboard Task 2)

### 작업 단위
- D184 계획 Task 2 MVVM command/model/ViewModel core 를 구현했다.

### 변경 내용
- `samples/Hps.Sample.Dashboard/Commands`:
  `RelayCommand`, `AsyncRelayCommand`를 추가했다.
- `samples/Hps.Sample.Dashboard/Models`:
  `DashboardStatus`, `SmokeRunResult`, `TransportMetricRow`를 추가했다.
- `samples/Hps.Sample.Dashboard/ViewModels/DashboardViewModel.cs`:
  초기 상태, command binding, bounded log, smoke summary mapping 을 추가했다.
- `tests/Hps.Sample.Dashboard.Tests/DashboardViewModelTests.cs`:
  MVVM core contract 를 assertion Red 로 검증했다.
- 상태 문서와 구현 계획 체크박스를 Task 2 완료, Task 3 diagnostics service 진입점으로 갱신했다.

### 검증
- Red: compile-failure 형태를 reflection assertion Red 로 보정한 뒤 ViewModel/command/model type 부재 assertion failure 를 확인했다.
- Green: `dotnet test tests\Hps.Sample.Dashboard.Tests\Hps.Sample.Dashboard.Tests.csproj --filter DashboardViewModelTests -v minimal` 통과.
- Green: `dotnet build samples\Hps.Sample.Dashboard\Hps.Sample.Dashboard.csproj -v minimal` 경고 0/오류 0.

### 결과
- WPF dashboard 의 service 연결 전 순수 MVVM core 가 준비됐다.
- 다음 실행 지점은 Task 3 Broker lifecycle 와 diagnostics service 다.

## 2026-07-06 (Codex - D185 WPF sample dashboard Task 1)

### 작업 단위
- D184 계획 Task 1 WPF project contract 와 solution inclusion 을 구현했다.

### 변경 내용
- `tests/Hps.Sample.Dashboard.Tests`:
  WPF project contract 와 solution inclusion 을 검증하는 Red test 를 추가했다.
- `samples/Hps.Sample.Dashboard`:
  `net9.0-windows`, `UseWPF=true`, `OutputType=WinExe`를 명시한 minimal WPF shell 을 추가했다.
- `HighPerformanceSocket.slnx`:
  WPF sample project 와 dashboard test project 를 solution 에 포함했다.
- 상태 문서와 구현 계획 체크박스를 Task 1 완료, Task 2 MVVM core 진입점으로 갱신했다.

### 검증
- Red: WPF project file 부재와 solution inclusion 부재 assertion failure 를 확인했다.
- Green: `dotnet test tests\Hps.Sample.Dashboard.Tests\Hps.Sample.Dashboard.Tests.csproj -v minimal` 통과.
- Green: `dotnet build samples\Hps.Sample.Dashboard\Hps.Sample.Dashboard.csproj -v minimal` 경고 0/오류 0.
- 비고: 첫 병렬 build/test 실행은 동일 dependency obj 파일 lock 으로 build 가 실패했으므로, 이후 검증은 순차 실행했다.

### 결과
- WPF sample dashboard 의 build contract blocker 를 test-first 로 닫았다.
- 다음 실행 지점은 Task 2 MVVM command/model/ViewModel core 다.

## 2026-07-06 (Codex - D184 WPF sample dashboard 구현 계획)

### 작업 단위
- D183 WPF sample dashboard 설계를 TDD 구현 계획으로 쪼갰다.

### 변경 내용
- `docs/superpowers/plans/2026-07-06-wpf-sample-dashboard.md`:
  project contract, MVVM core, broker diagnostics, TCP smoke, UDP smoke, UI wiring/run docs 의 6개 task 로 분리했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 Task 1 WPF project contract 와 solution inclusion 구현으로 갱신했다.

### 검증
- 기존 `HighPerformanceSocket.slnx`, sample/test project 구조, `BrokerServer` TCP/UDP public surface,
  benchmark/server loopback helper 흐름과 대조했다.
- 계획 self-review 로 spec coverage, placeholder, type consistency 를 점검했다.

### 결과
- 다음 구현은 WPF shell 부터가 아니라 project contract test Red 로 시작한다.

## 2026-07-06 (Codex - D183 WPF sample dashboard 설계)

### 작업 단위
- 사용자가 직접 실행해 Interface Server 동작을 확인할 WPF/MVVM sample dashboard 설계를 문서화했다.

### 변경 내용
- `docs/superpowers/specs/2026-07-06-wpf-sample-dashboard-design.md`:
  WPF 선택 이유, MVVM folder/service boundary, TCP/UDP smoke, diagnostics 표시, `io_uring` evidence status 범위를 정리했다.
- 설계 리뷰(`.claude/review/2026-07-06-wpf-sample-dashboard-design-review.md`)를 반영해
  WPF project 의 `net9.0-windows`/`UseWPF`/`WinExe` override, test project TFM, C# 8 제약,
  diagnostics transport 공유, UDP public entry 확인 경로를 명시했다.
- `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`:
  다음 실행 지점을 WPF sample dashboard 구현 계획 작성으로 전환하고,
  D181 원격 Linux contract gate 는 self-contained deferred backlog 로 보존했다.

### 검증
- 문서 self-review: WinUI 제외 이유, production API 확장 제한, 다음 구현 계획 위치를 확인했다.
- 리뷰 지적을 실제 `Directory.Build.props`, `BrokerServer`, `ITransportDiagnostics`,
  `ITransportEndpointDiagnostics`와 대조했다.

### 결과
- WPF sample dashboard 구현 전에 검토 가능한 설계 문서가 생겼다.
- 다음 실행 지점은 `docs/superpowers/plans/2026-07-06-wpf-sample-dashboard.md` 구현 계획 작성이다.

## 2026-07-06 (Codex - D182 fixed-buffer submission 전체 예제)

### 작업 단위
- D181 fixed-buffer registration/write-fixed 흐름을 사용하는 전체 예제를 문서화했다.

### 변경 내용
- `docs/examples/iouring-fixed-buffer-submission-example.md`:
  capability 확인, queue 생성, managed buffer 준비, fixed buffer registration,
  `TrySubmitWriteFixed`, completion 확인, pipe payload 검증, unregister/dispose 순서를 end-to-end 로 설명했다.
- 예제는 public `ITransport` 사용법이 아니라 `Hps.Transport.IoUring` internal backend contributor 용임을 명시했다.
- `CURRENT_PLAN.md`, `TODOS.md`, `docs/agent-state/changelog/2026-07.md`:
  D182 예제 문서 추가와 현재 원격 contract gate TODO 유지 상태를 기록했다.

### 검증
- 문서 self-review: production pump 에 바로 연결하지 않는 이유, 소유권/해제 순서, 관련 파일 목록을 확인했다.
- `git diff --check`로 whitespace 를 확인한다.

### 결과
- fixed-buffer submission 흐름을 한 파일에서 읽을 수 있는 전체 예제가 생겼다.
- 다음 실행 지점은 변함없이 사용자 push 이후 원격 `iouring-linux-contract.yml` gate 검토다.

## 2026-07-03 (Codex - D181 fixed-buffer SQE submission evidence 구현)

### 작업 단위
- D180 계획 기준으로 fixed-buffer SQE submission contract evidence 를 local 구현했다.

### 변경 내용
- `IoUringNative`: `OperationWriteFixed` opcode 를 추가했다.
- `IoUringQueue`: `TrySubmitWriteFixed(...)` helper 를 추가해 `Address`, `Length`, `BufferIndex`, `UserData`를 SQE에 채운다.
- `IoUringSubmissionShapeTests`: fixed-write opcode shape 와 helper shape tests 를 추가했다.
- `IoUringFixedBufferSubmissionTests`: Linux capability available 환경에서 registered buffer slice 를 pipe write fd 로
  `WRITE_FIXED` 제출하고 completion result 와 pipe payload 를 검증하는 test 를 추가했다.
- 상태 문서를 D181 기준과 다음 원격 contract gate 로 갱신했다.

### 검증
- Red: `NativeSubmissionTypes_WhenInspected_ExposeFixedWriteShape`가 `OperationWriteFixed` 부재로 `Assert.NotNull()` 실패.
- Red: `Queue_WhenInspected_ExposesFixedWriteSubmissionHelper`가 `TrySubmitWriteFixed` 부재로 `Assert.NotNull()` 실패.
- Green: fixed-write shape focused tests 2개 통과.
- Green: `IoUringFixedBufferSubmissionTests` focused test 1개 통과.
- `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal`: 61개 통과.
- `dotnet test HighPerformanceSocket.slnx -v minimal`: 전체 통과.

### 결과
- fixed-buffer SQE field mapping 과 native completion evidence test 가 준비됐다.
- local/Windows 에서는 capability guard 로 native path 를 실행하지 않으므로, 실제 Linux completion 은 다음 원격 `iouring-linux-contract.yml`에서 확인한다.
- TCP/UDP pump fixed-buffer 연결, zero-copy send, default promotion, latency hard gate 는 계속 범위 밖이다.

## 2026-07-03 (Codex - D180 fixed-buffer SQE submission 구현 계획)

### 작업 단위
- D179 설계를 구현 가능한 TDD 커밋 단위로 나눴다.

### 변경 내용
- `docs/superpowers/plans/2026-07-03-iouring-fixed-buffer-submission-evidence.md`:
  fixed-write opcode shape 와 fixed-write helper/native completion evidence 의 2개 task 로 분리했다.
- Task 1은 `OperationWriteFixed` opcode shape 를 assertion failure Red 로 고정한다.
- Task 2는 `TrySubmitWriteFixed` helper 부재를 reflection assertion failure 로 먼저 고정한 뒤,
  Linux pipe 기반 native completion evidence test 를 추가한다.
- `CURRENT_PLAN.md`, `TODOS.md`, `docs/agent-state/changelog/2026-07.md`:
  D180 계획과 다음 실행 지점을 기록했다.

### 검증
- 계획 self-review: spec coverage, placeholder scan, type consistency 를 확인했다.
- AGENTS Red 규칙에 맞게 Task 2의 helper 부재 Red 를 컴파일 실패가 아니라 reflection assertion failure 로 보정했다.

### 결과
- 다음 실행 지점은 D180 계획 Task 1 실행이다.
- TCP/UDP pump fixed-buffer 연결과 zero-copy send 는 계속 범위 밖이다.

## 2026-07-03 (Codex - D179 post-D178 후속 후보 재평가)

### 작업 단위
- D178 fixed buffer registration 원격 contract gate 이후 `io_uring` 다음 작업 후보를 재평가했다.

### 변경 내용
- `docs/superpowers/specs/2026-07-03-iouring-post-d178-next-scope-design.md`:
  TCP/UDP pump fixed-buffer 직행, zero-copy send 직행, fixed-buffer SQE submission contract evidence 를 비교했다.
- 결론은 production pump 변경이 아니라 `IORING_OP_WRITE_FIXED` 중심 fixed-buffer SQE 제출 계약을 먼저 검증하는 것이다.
- `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`,
  `docs/agent-state/changelog/2026-07.md`, `docs/agent-state/decisions/2026-07.md`:
  D179 결정과 다음 구현 지점을 기록했다.

### 검증
- 실제 `IoUringQueue`, `IoUringNative`, TCP/UDP send path, D176/D178 상태와 대조했다.
- man7/liburing 문서에서 registered buffer, `read_fixed`/`write_fixed`, `send_zc_fixed`의 buffer index/range 및 CQE 소유권 차이를 확인했다.

### 결과
- 다음 구현 단위는 fixed-write opcode/helper shape 와 Linux capability gated native completion test 다.
- TCP/UDP pump fixed-buffer 연결, zero-copy send, default promotion, latency hard gate 는 계속 별도 설계 전까지 열지 않는다.

## 2026-07-03 (Codex - D178 fixed buffer registration 원격 contract gate)

### 작업 단위
- D177 fixed buffer registration evidence test 가 원격 Linux contract artifact 에서 실제로 실행·통과했는지 검토했다.

### 변경 내용
- GitHub Actions run `28631346969`를 `iouring-linux-contract.yml` `workflow_dispatch`로 실행했다.
- artifact `iouring-linux-contract-2026-07-03-github-28631346969-1`를 다운로드해 root summary 와 TRX 결과를 확인했다.
- `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`,
  `docs/agent-state/changelog/2026-07.md`, `docs/agent-state/decisions/2026-07.md`:
  D178 원격 contract gate 결과와 다음 후속 후보 재평가 지점을 기록했다.

### 검증
- workflow conclusion: success.
- root `summary.md`: test exit code 0.
- TRX counters: total 58, executed 58, passed 58, failed 0, notExecuted 0.
- `Register_WhenLinuxCapabilityAvailable_RegistersAndUnregistersMultipleBuffers`: outcome Passed.
- test output: `io_uring capability status: Available`, `registered fixed buffer count: 2`.

### 결과
- `IoUringRegisteredBufferSet`의 Linux native register/unregister owner evidence 가 원격 contract gate 에서 확인됐다.
- 이 증거는 fixed buffer 등록 owner 의 native 계약을 닫는 것이며, TCP/UDP pump fixed-buffer 연결이나 zero-copy/default promotion 근거로 바로 확장하지 않는다.
- 다음 실행 지점은 D178 evidence 기준으로 `io_uring` 후속 후보를 재평가하는 것이다.

## 2026-07-03 (Codex - D177 fixed buffer registration evidence test)

### 작업 단위
- D176 설계 기준으로 `IoUringRegisteredBufferSet` Linux native register/unregister evidence test 를 구현했다.

### 변경 내용
- `IoUringRegisteredBufferSet`: `RegisteredBufferCount` internal 관측값을 추가했다.
- `IoUringRegisteredBufferSetTests`: registration count shape test 와 Linux/capability gated native register/unregister test 를 추가했다.
  Linux available 환경에서는 작은 io_uring queue 에 2개 byte[] buffer 를 register/dispose 한다.
- `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`,
  `docs/agent-state/changelog/2026-07.md`, `docs/agent-state/decisions/2026-07.md`:
  D177 구현 결과와 다음 원격 contract gate 를 기록했다.

### 검증
- Red: `IoUringRegisteredBufferSet_WhenInspected_ExposesRegisteredBufferCount`가 property 부재로 `Assert.NotNull()` 실패.
- Green: `IoUringRegisteredBufferSetTests` 4개 통과.
- `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal`: 58개 통과.
- `dotnet test HighPerformanceSocket.slnx -v minimal`: 전체 통과.

### 결과
- fixed buffer registration owner 가 등록 table 크기를 내부적으로 증명할 수 있게 됐다.
- 다음 실행 지점은 push 이후 원격 `iouring-linux-contract.yml` artifact 로 Linux native register/unregister test 실행을 확인하는 것이다.

## 2026-07-03 (Codex - D176 post-D175 후속 후보 재평가)

### 작업 단위
- D175 원격 artifact gate 성공 이후 `io_uring` 다음 작업 후보를 재평가했다.

### 변경 내용
- `docs/superpowers/specs/2026-07-03-iouring-post-d175-next-scope-design.md`:
  reference 추가 채택, fixed/zero-copy pump 직행, fixed buffer registration native contract evidence 를 비교했다.
- 결론은 fixed/zero-copy pump 연결이 아니라 `IoUringRegisteredBufferSet` Linux native register/unregister evidence 를
  `iouring-linux-contract.yml` artifact 로 먼저 고정하는 것이다.
- `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`,
  `docs/agent-state/changelog/2026-07.md`, `docs/agent-state/decisions/2026-07.md`:
  D176 결정과 다음 실행 지점을 기록했다.

### 검증
- spec self-review: placeholder/파일명/범위 모순을 점검했고 `TODOs.md` 표기를 `TODOS.md`로 정정했다.
- `rg -n "TBD|TODO|나중|미정|\?\?" docs\superpowers\specs\2026-07-03-iouring-post-d175-next-scope-design.md` 확인.

### 결과
- 다음 구현 단위는 `IoUringRegisteredBufferSetTests`에 Linux/capability gated native register/unregister test 를 추가하는 것이다.
- TCP/UDP pump fixed-buffer 연결, zero-copy send, default promotion, latency hard gate 는 계속 후속 결정 전까지 열지 않는다.

## 2026-07-03 (Codex - D175 D174 fix 원격 artifact gate)

### 작업 단위
- D174 `io_uring` shutdown stale completion fix 가 원격 Linux benchmark artifact gate 에서 TCP baseline exit 134를 해소했는지 검토했다.

### 변경 내용
- GitHub Actions run `28627435853`를 `iouring-benchmark-artifacts.yml` `workflow_dispatch`로 실행했다.
- artifact `iouring-benchmark-artifacts-2026-07-02-github-28627435853-1`를 다운로드해 root summary,
  TCP/UDP session summary, protocol history, envelope JSON을 확인했다.
- `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`,
  `docs/agent-state/changelog/2026-07.md`, `docs/agent-state/decisions/2026-07.md`:
  D175 원격 gate 결과와 다음 후속 후보 재평가 지점을 기록했다.

### 검증
- workflow conclusion: success.
- root `summary.md`: TCP/UDP baseline, summary, history, envelope exit code 모두 0.
- TCP raw report count 6, source-report-count 6, hard-passed true, warning-count 6.
- UDP raw report count 6, source-report-count 6, hard-passed true, warning-count 2.
- TCP/UDP load/open-loop aggregate 모두 dropped-total 0, payload-error-total 0, pool-rented-max 0.
- TCP envelope: compatible true, reference-summary-count 6, signal-count 0.
- UDP envelope: compatible true, reference-summary-count 9, signal-count 0.

### 결과
- D174 fix 이후 TCP baseline exit 134는 원격 artifact gate 에서 재발하지 않았다.
- D173 기준 확장된 TCP 6-session/UDP 9-session protocol reference 가 workflow envelope step 에서 정상 사용됐다.
- fixed registration, zero-copy send, latency hard gate, default promotion 은 바로 열지 않고,
  다음 단위에서 D175 evidence 기준으로 후속 후보를 재평가한다.

## 2026-07-02 (Codex - D174 io_uring shutdown stale completion fix)

### 작업 단위
- D173 reference 확장 이후 원격 `iouring-benchmark-artifacts.yml` artifact gate 실패를 검토하고,
  TCP baseline exit 134의 원인이 된 `io_uring` shutdown stale completion 처리 경계를 수정했다.

### 변경 내용
- GitHub Actions run `28570636117`은 UDP 경로는 모두 통과했지만 TCP baseline suite 가
  open-loop stop 중 `등록되지 않은 io_uring operation token입니다.`로 exit 134를 반환했다.
- `IoUringCompletionLoop`: `BeginShutdown()` 경계를 추가하고, shutdown 이후 registry 에 없는 completion token 만 no-op으로 처리한다.
  shutdown 전 unknown token 은 기존처럼 `InvalidOperationException`으로 남겨 실제 mapping bug 를 숨기지 않는다.
- `IoUringTransport.StopCore()`: connection/endpoint close 로 context 를 unregister 하기 전에 completion loop 를 shutdown 모드로 전환한다.
- `IoUringCompletionLoopTests`: shutdown 후 unregister 된 token 의 늦은 CQE가 stop 실패로 전파되지 않는 회귀 테스트를 추가했다.
- 상태 문서를 D174 기준으로 갱신하고, 다음 실행 지점을 fix push 이후 원격 artifact gate 재검토로 바꿨다.

### 검증
- Red: `DispatchCompletion_WhenShutdownStartedAndTokenWasUnregistered_IgnoresStaleCompletion`이
  `BeginShutdown` 부재로 `Assert.NotNull()` 실패.
- Green: `IoUringCompletionLoopTests` 5개 통과.
- `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal`: 56개 통과.
- `dotnet test HighPerformanceSocket.slnx -v minimal`: 전체 통과.

### 결과
- shutdown 중 늦게 도착한 CQE가 이미 unregister 된 token 을 참조해도 transport stop 이 실패하지 않는다.
- 정상 운영 중 unknown token 은 여전히 fatal mapping 오류로 처리한다.
- 다음 실행 지점은 사용자 push 이후 원격 `iouring-benchmark-artifacts.yml`을 다시 실행해
  TCP baseline exit 134가 재발하지 않는지 확인하는 것이다.

## 2026-07-02 (Codex - D167 reference date 원격 artifact gate)

### 작업 단위
- D167로 확장한 `ci-linux-iouring-x64-01` TCP/UDP protocol reference history 가 원격 workflow envelope step 에서
  실제 reference 로 쓰이는지 검토했다.

### 변경 내용
- GitHub Actions run `28568500822`를 `iouring-benchmark-artifacts.yml` `workflow_dispatch`로 실행했다.
- artifact `iouring-benchmark-artifacts-2026-07-02-github-28568500822-1`를 다운로드해 root summary,
  TCP/UDP session summary, protocol history, envelope JSON을 확인했다.
- `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`,
  `docs/agent-state/changelog/2026-07.md`, `docs/agent-state/decisions/2026-07.md`:
  D168 원격 gate 결과와 다음 재평가 지점을 기록했다.

### 검증
- workflow conclusion: success.
- root `summary.md`: TCP/UDP baseline, summary, history, envelope exit code 모두 0.
- TCP raw report count 6, source-report-count 6, hard-passed true, warning-count 6.
- UDP raw report count 6, source-report-count 6, hard-passed true, warning-count 3.
- TCP/UDP load/open-loop aggregate 모두 dropped-total 0, payload-error-total 0, pool-rented-max 0.
- TCP envelope: compatible true, reference-summary-count 4, signal-count 0.
- UDP envelope: compatible true, reference-summary-count 7, signal-count 0.

### 결과
- D167 두 date root reference history 가 원격 workflow 에서 실제 envelope reference 로 정상 사용됨을 확인했다.
- fixed registration, zero-copy send, latency hard gate, default promotion 은 계속 자동 진행하지 않고,
  다음 단위에서 D168 evidence 기준으로 후속 후보를 재평가한다.

### D169 post-D168 후속 후보 재평가
- D168 artifact 는 correctness/reliability failure 가 아니라 두 date root reference 기준 signal 0 passing evidence 로 판단했다.
- `docs/superpowers/specs/2026-07-02-iouring-post-d168-reference-date-continuation-design.md`를 추가했다.
- 결정: fixed registration, zero-copy send, latency hard gate, default promotion 을 열지 않고,
  D168 raw report 를 protocol별 `2026-07-02/session-02` reference 로 수동 채택한다.
- 다음 실행 지점은 TCP/UDP raw report 6개씩을 `session-02`로 복사하고 summary/history/index 와 envelope smoke 를 재검증하는 것이다.

### D170 D168 raw report session-02 채택
- run `28568500822` artifact 의 TCP/UDP raw report 6개씩을 protocol별 `2026-07-02/session-02` reference 로 수동 채택했다.
- TCP/UDP `summary.json`/`summary.md`, 2026-07-02 date-level `history.json`/`history.md`,
  protocol root `history.json`/`history.md`를 repository 경로 기준으로 재생성했다.
- `docs/benchmarks/baselines/index.md`의 runner latest date, date-level history, session row,
  `ci-linux-iouring-x64-01 io_uring Protocol Reference` 표를 갱신했다.
- TCP protocol root history 는 session-count 5, hard-passed true, warning-count 30, comparison-compatible true 다.
- UDP protocol root history 는 session-count 8, hard-passed true, warning-count 16, comparison-compatible true 다.
- 최신 session 기준 envelope smoke 는 TCP/UDP 모두 `envelope-compatible=true`, `envelope-signal-count=0`이다.
- baseline path absolute path scan 매칭 없음, `Hps.Benchmarks.Tests` 114개 통과, `git diff --check` 통과.
- 다음 실행 지점은 사용자 push 이후 D170 reference 를 쓰는 원격 artifact gate 검토다.

### D171 D170 reference 확장 원격 artifact gate
- 사용자 push 이후 run `28569649366`을 `iouring-benchmark-artifacts.yml` `workflow_dispatch`로 실행했다.
- workflow conclusion 은 success 이고, artifact 는 `iouring-benchmark-artifacts-2026-07-02-github-28569649366-1`이다.
- root `summary.md` 기준 TCP/UDP baseline, summary, history, envelope exit code 는 모두 0이다.
- TCP raw report count 6, source-report-count 6, hard-passed true, warning-count 6이다.
- UDP raw report count 6, source-report-count 6, hard-passed true, warning-count 2이다.
- TCP/UDP load/open-loop aggregate 모두 dropped-total 0, payload-error-total 0, pool-rented-max 0이다.
- TCP envelope 는 compatible true, reference-summary-count 5, signal-count 0이다.
- UDP envelope 는 compatible true, reference-summary-count 8, signal-count 0이다.
- 다음 실행 지점은 D171 evidence 기준으로 추가 reference 확장과 최적화 구현 후보를 재평가하는 것이다.

### D172 post-D171 후속 후보 재평가
- D171 artifact 는 correctness/reliability failure 가 아니라 두 date root reference 기준 signal 0 passing evidence 로 판단했다.
- `docs/superpowers/specs/2026-07-02-iouring-post-d171-second-date-completion-design.md`를 추가했다.
- 결정: fixed registration, zero-copy send, latency hard gate, default promotion 을 열지 않고,
  D171 raw report 를 protocol별 `2026-07-02/session-03` reference 로 수동 채택한다.
- 다음 실행 지점은 TCP/UDP raw report 6개씩을 `session-03`으로 복사하고 summary/history/index 와 envelope smoke 를 재검증하는 것이다.

### D173 D171 raw report session-03 채택
- run `28569649366` artifact 의 TCP/UDP raw report 6개씩을 protocol별 `2026-07-02/session-03` reference 로 수동 채택했다.
- TCP/UDP `summary.json`/`summary.md`, 2026-07-02 date-level `history.json`/`history.md`,
  protocol root `history.json`/`history.md`를 repository 경로 기준으로 재생성했다.
- `docs/benchmarks/baselines/index.md`의 date-level history, session row,
  `ci-linux-iouring-x64-01 io_uring Protocol Reference` 표를 갱신했다.
- TCP protocol root history 는 session-count 6, hard-passed true, warning-count 36, comparison-compatible true 다.
- UDP protocol root history 는 session-count 9, hard-passed true, warning-count 18, comparison-compatible true 다.
- 최신 session 기준 envelope smoke 는 TCP/UDP 모두 `envelope-compatible=true`, `envelope-signal-count=0`이다.
- baseline path absolute path scan 매칭 없음, `Hps.Benchmarks.Tests` 114개 통과, `git diff --check` 통과.
- 다음 실행 지점은 사용자 push 이후 D173 reference 를 쓰는 원격 artifact gate 검토다.

## 2026-07-01 (Codex - io_uring benchmark artifact workflow)

### 작업 단위
- D147 기준으로 Linux `io_uring` benchmark artifact 를 수집하는 수동 GitHub Actions workflow 를 추가했다.

### 변경 내용
- `.github/workflows/iouring-benchmark-artifacts.yml`:
  `workflow_dispatch` 전용 `ubuntu-latest` workflow 를 추가했다.
  TCP/UDP 각각 `--backend iouring --runs 1` baseline suite 를 실행하고,
  protocol 별 summary/history 와 root summary/dotnet info 를 artifact 로 업로드한다.
- `tests/Hps.Benchmarks.Tests/BenchmarkArtifactWorkflowTests.cs`:
  Linux 수동 trigger, runner identity, TCP/UDP io_uring command, artifact upload, final failure gate 순서를 고정하는
  workflow static tests 를 추가했다.
- `docs/superpowers/specs/2026-07-01-iouring-benchmark-artifact-workflow-design.md`,
  `docs/superpowers/plans/2026-07-01-iouring-benchmark-artifact-workflow.md`:
  D147 설계와 구현 계획을 작성하고 Task 1 Red/Green 상태를 반영했다.
- `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`,
  `docs/agent-state/changelog/2026-07.md`, `docs/agent-state/decisions/2026-07.md`:
  현재 실행 지점을 원격 workflow artifact 검토로 갱신했다.

### 검증
- Red: `BenchmarkArtifactWorkflowTests` focused run 에서 새 테스트 2개가 missing workflow 로 실패하는 것을 확인했다.
- Green: `.github/workflows/iouring-benchmark-artifacts.yml` 추가 후 focused workflow tests 4개 통과.

### 결과
- `--backend iouring` TCP/UDP benchmark 를 Linux runner 에서 artifact 로 남길 수 있는 원격 evidence 경로가 준비됐다.
- 보정 후 원격 artifact 검토까지 완료해 D147 evidence gate 를 충족했다(D148).

### 추가 확인
- 사용자 push 이후 run `28485295725`를 실행했다.
- baseline suite 와 summary command 는 TCP/UDP 모두 exit 0이었고 raw report/summary artifact 는 업로드됐다.
- history command 는 TCP/UDP 모두 exit 2로 실패했다.
  로그의 원인은 `baseline history summary.json 을 찾지 못했습니다`였다.
- workflow artifact 구조가 `date/protocol/session`이라 `BaselineHistoryReader`의 parent-root 아래 date child discovery 규칙과 맞지 않았다.
- workflow 를 `runner/<protocol>/<yyyy-mm-dd>/session-01` 구조로 보정하고,
  static workflow test 로 history input root 구조를 고정했다.

### 보정 후 원격 artifact 검토
- 사용자 push 이후 run `28486254926`을 실행했고 workflow conclusion success 를 확인했다.
- artifact `iouring-benchmark-artifacts-2026-07-01-github-28486254926-1`에는 root `summary.md`,
  `dotnet-info.txt`, TCP/UDP raw report, summary, history 가 모두 포함됐다.
- root `summary.md` 기준 TCP/UDP baseline, summary, history exit code 는 모두 0이다.
- TCP summary 는 source-report-count 2, hard-passed true, warning-count 2,
  dropped-total 0, payload-error-total 0, pool-rented-max 0이다.
- UDP summary 는 source-report-count 2, hard-passed true, warning-count 0,
  dropped-total 0, payload-error-total 0, pool-rented-max 0이다.
- TCP p99 warning 2개는 evidence-only report data 로 남기며, latency hard gate 나 warning-as-failure 로 승격하지 않았다.
- 다음 실행 지점은 D148 이후 io_uring 후속 후보를 재평가하고 다음 최소 설계 단위를 확정하는 것이다.

### D149 반복 benchmark artifact 보정
- D148 이후 후보를 재평가했고, fixed registration/zero-copy/IPv6/default promotion 은 아직 열지 않기로 했다.
  단일 run artifact 는 최적화 필요성 판단 근거로 부족하므로, 먼저 반복 benchmark summary 품질을 올린다.
- `docs/superpowers/specs/2026-07-01-iouring-repeat-benchmark-artifact-design.md`와
  `docs/superpowers/plans/2026-07-01-iouring-repeat-benchmark-artifact.md`를 추가했다.
- `.github/workflows/iouring-benchmark-artifacts.yml`:
  TCP/UDP baseline suite 를 각각 `--runs 3`으로 바꾸고 root `summary.md`에 `Runs per protocol: 3`을 기록하게 했다.
- `tests/Hps.Benchmarks.Tests/BenchmarkArtifactWorkflowTests.cs`:
  io_uring workflow static test 가 TCP/UDP `--runs 3` command 를 고정하도록 갱신했다.
- Red: focused workflow test 1개가 기존 `--runs 1` 때문에 assertion failure 로 실패하는 것을 확인했다.
- Green: workflow 보정 후 `BenchmarkArtifactWorkflowTests` focused test 5개 통과를 확인했다.
- 다음 실행 지점은 사용자 push 이후 `--runs 3` 원격 artifact 를 검토하는 것이다.

### D150 반복 benchmark artifact 검토
- 사용자 push 이후 run `28489104828`을 실행했고 workflow conclusion success 를 확인했다.
- artifact `iouring-benchmark-artifacts-2026-07-01-github-28489104828-1`은 root `summary.md`,
  `dotnet-info.txt`, TCP/UDP raw report 6개씩, protocol 별 `summary.json`/`summary.md`,
  `history.json`/`history.md`를 포함한다.
- root `summary.md` 기준 `Runs per protocol: 3`이고 TCP/UDP baseline, summary, history exit code 는 모두 0이다.
- TCP summary 는 source-report-count 6, hard-passed true, warning-count 6,
  load p99 max 4570.8 us, open-loop p99 max 4604.5 us, dropped/payload-error/pool-rented 0,
  TCP HWM max 1이다.
- UDP summary 는 source-report-count 6, hard-passed true, warning-count 2,
  load p99 max 1506.4 us, open-loop p99 max 1349.3 us, dropped/payload-error/pool-rented 0,
  UDP HWM max 0이다.
- warning 은 모두 p99 latency soft signal 이므로 workflow failure 로 승격하지 않았다.
- 다음 실행 지점은 D150 p99 warning 을 분석하고 후속 설계 단위를 확정하는 것이다.

### D151 io_uring envelope comparison artifact
- D150 p99 warning 을 fixed registration/zero-copy/default promotion 의 직접 근거로 보지 않고,
  D125의 runner/profile scoped envelope comparison artifact 로 먼저 해석하기로 했다.
- `.github/workflows/iouring-benchmark-artifacts.yml`:
  TCP/UDP history 생성 뒤 protocol별 `--compare-baseline-envelope` step 을 추가했다.
  reference history 는 `docs/benchmarks/baselines/runners/${HPS_BENCHMARK_RUNNER_ID}/tcp/history.json`,
  `.../udp/history.json`를 사용한다.
- reference history 가 없으면 해당 envelope step 은 skip 하고 `IOURING_*_ENVELOPE_EXIT=0`으로 기록한다.
- root summary 와 final gate 에 TCP/UDP envelope exit code 를 포함했다.
- `tests/Hps.Benchmarks.Tests/BenchmarkArtifactWorkflowTests.cs`:
  protocol별 envelope step 순서, reference path, output path, exit env var 를 고정하는 static test 를 추가했다.
- Red: focused workflow static test 1개가 envelope step 부재로 실패함을 확인했다.
- Green: `BenchmarkArtifactWorkflowTests` focused test 6개 통과.
- 전체 검증: solution build 경고 0/오류 0, solution tests 445개 통과,
  `git diff --check` whitespace 오류 없음(CRLF 경고만 있음).
- 다음 실행 지점은 커밋 후, 사용자 push 이후 원격 workflow artifact 로 envelope skip/generation 경로를 확인하는 것이다.

### D152 envelope artifact 원격 검토
- 사용자 push 이후 run `28492234252`를 실행했고 workflow conclusion success 를 확인했다.
- artifact `iouring-benchmark-artifacts-2026-07-01-github-28492234252-1`은 root `summary.md`,
  `dotnet-info.txt`, TCP/UDP raw report 6개씩, protocol 별 `summary.json`/`summary.md`,
  `history.json`/`history.md`를 포함한다.
- root `summary.md` 기준 TCP/UDP baseline, summary, history, envelope exit code 는 모두 0이다.
- repository reference history 가 아직 없어 protocol별 `envelope.json`/`envelope.md`는 생성되지 않았다.
  이는 D151의 reference 없음 skip 정책과 맞고, skip 경로는 exit 0으로 수렴했다.
- TCP summary 는 source-report-count 6, hard-passed true, warning-count 6,
  load p99 max 4298.8 us, open-loop p99 max 5588.6 us, dropped/payload-error/pool-rented 0,
  TCP HWM max 1이다.
- UDP summary 는 source-report-count 6, hard-passed true, warning-count 3,
  load p99 max 1623.8 us, open-loop p99 max 1322.0 us, dropped/payload-error/pool-rented 0,
  UDP HWM max 0이다.
- 의미: D151 protocol별 envelope step 의 원격 artifact/skip gate 를 충족했다.
- 다음 실행 지점은 자동 baseline 채택이 아니라, `ci-linux-iouring-x64-01/tcp`와 `.../udp` repository reference baseline 을
  수동 채택할지 판단하는 정책 설계다.

### D153 io_uring protocol별 reference baseline 수동 채택 정책
- `docs/superpowers/specs/2026-07-01-iouring-protocol-reference-baseline-adoption-policy-design.md`를 추가했다.
- D095의 수동 채택 절차를 재사용하되, `io_uring` workflow 의 protocol split 구조를 반영해
  `ci-linux-iouring-x64-01/tcp`와 `.../udp`를 별도 reference root 로 둔다.
- D152 artifact 는 TCP/UDP 모두 hard-passed true, comparison-compatible true, dropped/payload-error/pool-rented 0이라
  첫 provisional reference 후보로 수동 채택할 수 있다고 판단했다.
- D095와 달리 초기 `io_uring` reference 의 warning-count > 0은 채택 차단 조건으로 보지 않는다.
  해당 warning 은 runner/profile/protocol envelope 기준이 아니라 D070 전역 soft threshold signal 이기 때문이다.
- 채택된 reference 는 latency hard gate, warning-as-failure, default backend promotion 근거가 아니라
  future run 의 protocol별 envelope comparison 을 시작하기 위한 report-only 기준점으로 제한한다.
- 다음 실행 지점은 run `28492234252` artifact 를 protocol별 repository baseline 구조로 수동 채택하고,
  regenerated history 를 reference 로 envelope command smoke 를 실행하는 것이다.

### D154 io_uring protocol별 provisional reference baseline 채택
- run `28492234252` artifact 를 `ci-linux-iouring-x64-01` TCP/UDP protocol별 provisional repository reference baseline 으로 채택했다.
- TCP raw report 6개를
  `docs/benchmarks/baselines/runners/ci-linux-iouring-x64-01/tcp/2026-07-01/session-01/`에 보관했다.
- UDP raw report 6개를
  `docs/benchmarks/baselines/runners/ci-linux-iouring-x64-01/udp/2026-07-01/session-01/`에 보관했다.
- TCP/UDP session summary, date history, protocol root history 를 repository 경로 기준으로 재생성했다.
- `docs/benchmarks/baselines/index.md`에 protocol별 runner group, date row, session row, provisional reference 해석을 추가했다.
- protocol별 envelope command smoke 는 TCP/UDP 모두 `envelope-compatible=true`, `envelope-signal-count=0`으로 통과했다.
- warning-count 는 TCP 6, UDP 3이며 D153 기준 provisional reference signal 로 기록하고,
  latency hard gate 또는 warning-as-failure 로 승격하지 않는다.
- 다음 실행 지점은 사용자 push 이후 원격 `iouring-benchmark-artifacts.yml`에서 reference-present 상태의
  `envelope.json`/`envelope.md` 생성 경로를 검토하는 것이다.

### D155 reference-present envelope artifact 원격 검토
- 사용자 push 이후 run `28493590950`을 실행했고 workflow conclusion success 를 확인했다.
- artifact `iouring-benchmark-artifacts-2026-07-01-github-28493590950-1`은 root `summary.md`,
  `dotnet-info.txt`, TCP/UDP raw report 6개씩, protocol 별 `summary.json`/`summary.md`,
  `history.json`/`history.md`, `envelope.json`/`envelope.md`를 포함한다.
- root `summary.md` 기준 TCP/UDP baseline, summary, history, envelope exit code 는 모두 0이다.
- TCP envelope 는 `envelope-compatible=true`, `envelope-signal-count=0`이다.
- UDP envelope 는 `envelope-compatible=false`, `envelope-signal-count=2`다.
  signal 은 load `p99-max-us` upper bound 초과와 open-loop `p50-median-us` upper bound 초과다.
- D151 reference-present envelope artifact path 는 검증됐다.
  UDP signal 은 D153 provisional reference 상태의 report-only triage 대상으로 남긴다.
- 다음 실행 지점은 UDP signal 을 즉시 최적화 구현 근거로 볼지,
  추가 session/date root 를 먼저 쌓아 reference envelope 를 안정화할지 정책으로 정하는 것이다.

### D156 UDP envelope signal triage 정책
- `docs/superpowers/specs/2026-07-01-iouring-udp-envelope-signal-triage-policy-design.md`를 추가했다.
- D155 UDP signal 은 즉시 fixed registration, zero-copy, default promotion 으로 연결하지 않기로 했다.
- D154 reference 는 아직 1-session provisional baseline 이므로 measurement variance 와 구조 문제를 분리하기 어렵다.
- 다음 단위는 `iouring-benchmark-artifacts.yml`을 2회 더 실행해 D155 포함 총 3개 reference-present candidate 를 만드는 것이다.
- 같은 UDP metric 이 2회 이상 반복 signal 이면 UDP latency triage 설계를 열고,
  signal 이 흩어지거나 사라지면 provisional reference envelope 안정화 정책을 설계한다.
- candidate raw report 는 자동 repository baseline 으로 채택하지 않는다.

### D157 reference-present candidate 추가 수집
- D156 기준으로 `iouring-benchmark-artifacts.yml`을 두 번 더 실행했다.
- run `28494135787`: workflow success, TCP envelope signal 0, UDP envelope signal 2.
  UDP signals 는 load `p99-growth-ratio-max`, open-loop `p50-median-us`다.
- run `28494404015`: workflow success, TCP envelope signal 0, UDP envelope signal 1.
  UDP signal 은 open-loop `p50-median-us`다.
- D155 포함 3개 candidate 모두 UDP open-loop `p50-median-us` signal 을 반복했다.
- 모든 run 은 UDP hard-passed true, drop/payload-error/pool-rented 0이라 correctness/reliability failure 로 보지 않는다.
- D157 기준으로 다음 단위는 UDP open-loop p50 median 반복 signal triage 설계다.
  fixed registration, zero-copy, latency hard gate 는 아직 열지 않는다.

### D158 UDP open-loop p50 반복 signal triage 설계
- `docs/superpowers/specs/2026-07-01-iouring-udp-open-loop-p50-triage-design.md`를 추가했다.
- D157의 반복 signal 은 transport correctness/reliability failure 보다 1-session provisional reference 의
  p50 envelope 가 지나치게 얇은 문제로 판단했다.
- reference session open-loop p50 은 154.2, 158.6, 1229.0 us로 bimodal 이지만,
  이후 candidate raw run 9개는 모두 1135~1252 us 범위다.
- p99, hard gate, drop, payload error, pool rented 값은 안정적이므로 fixed registration/zero-copy 구현을
  바로 열지 않는다.
- 다음 실행 지점은 D155~D157 UDP candidate raw report 를 `session-02..04`로 수동 채택하고,
  UDP summary/history/index 와 updated reference envelope smoke 를 확인하는 것이다.

### D158 UDP provisional reference stabilization 실행
- D155~D157 UDP candidate raw report 를
  `docs/benchmarks/baselines/runners/ci-linux-iouring-x64-01/udp/2026-07-01/session-02..04/`에 수동 채택했다.
- 각 session 의 `summary.json`/`summary.md`, UDP date-level `history.json`/`history.md`,
  UDP protocol root `history.json`/`history.md`를 repository 경로 기준으로 재생성했다.
- UDP protocol root history 는 session-count 4, hard-passed true, warning-count 8,
  comparison-compatible true 상태다.
- updated reference history 로 `session-04` summary 를 envelope smoke 비교했고,
  `envelope-compatible=true`, `envelope-signal-count=0`을 확인했다.
- `docs/benchmarks/baselines/index.md`에 UDP session rows 와 updated protocol reference 값을 반영했다.

### D159 post-D158 후속 범위 재평가
- `docs/superpowers/specs/2026-07-01-iouring-post-d158-next-scope-design.md`를 추가했다.
- D158 이후 바로 fixed registration, zero-copy send, UDP pump 구조 변경, latency hard gate 를 열지 않기로 했다.
- 근거: UDP p50 반복 signal 은 updated reference smoke 에서 signal 0으로 닫혔고,
  drop/payload-error/pool-rented/hard gate failure 가 남아 있지 않다.
- 다음 단위는 push 이후 updated reference 를 반영한 원격 `iouring-benchmark-artifacts.yml` artifact gate 검토다.

### D160 updated reference 원격 artifact gate 검토
- 사용자 push 이후 `iouring-benchmark-artifacts.yml`을 `master` 기준으로 수동 실행했다.
- run `28495804466`은 workflow success 로 완료됐고,
  artifact `iouring-benchmark-artifacts-2026-07-01-github-28495804466-1`을 생성했다.
- root `summary.md` 기준 TCP/UDP baseline, summary, history, envelope exit code 는 모두 0이다.
- TCP/UDP raw report count 는 각각 6이다.
- TCP envelope 는 compatible true, signal-count 0이고, UDP envelope 도 compatible true, signal-count 0이다.
- D155~D157에서 반복됐던 UDP open-loop p50 signal 은 D158 updated reference 기준에서 재발하지 않았다.

### D161 benchmark Markdown HWM label 정리
- `BaselineSummaryMarkdownWriter`와 `BaselineHistoryMarkdownWriter`의 table header 를
  `TCP HWM max`에서 `send queue HWM max`로 바꿨다.
- JSON schema field 인 `tcp-hwm-*`와 warning code 는 호환성을 위해 변경하지 않았다.
- 기존 generated baseline Markdown artifact 와 `docs/benchmarks/baselines/index.md`도 같은 label 로 정렬했다.
- Red: focused Markdown writer tests 2개가 기존 header 때문에 실패함을 확인했다.
- Green: focused tests 15개 통과, `Hps.Benchmarks.Tests` 114개 통과, `git diff --check` 통과.

### D162 D161 원격 artifact gate 검토
- 사용자 push 이후 `iouring-benchmark-artifacts.yml`을 `master` 기준으로 수동 실행했다.
- run `28497147332`은 workflow success 로 완료됐고,
  artifact `iouring-benchmark-artifacts-2026-07-01-github-28497147332-1`을 생성했다.
- root `summary.md` 기준 TCP/UDP baseline, summary, history, envelope exit code 는 모두 0이다.
- TCP/UDP raw report count 는 각각 6이다.
- TCP/UDP envelope 는 모두 compatible true, signal-count 0이다.
- TCP/UDP summary/history Markdown 에 `send queue HWM max` label 이 반영됐다.

### D163 post-D162 protocol reference 확장 설계
- `docs/superpowers/specs/2026-07-01-iouring-post-d162-reference-expansion-design.md`를 추가했다.
- D160/D162 artifact 는 failure artifact 가 아니라 TCP/UDP envelope signal 0 passing artifact 로 판단했다.
- 다음 단위는 fixed registration, zero-copy send, latency hard gate 가 아니라
  D160/D162 raw report 를 protocol별 provisional reference session 으로 수동 채택하는 것이다.
- TCP는 `session-02..03`, UDP는 `session-05..06`으로 확장한다.

### D164 protocol reference 확장 실행
- D160/D162 TCP raw report 를 `ci-linux-iouring-x64-01/tcp/2026-07-01/session-02..03`으로 수동 채택했다.
- D160/D162 UDP raw report 를 `ci-linux-iouring-x64-01/udp/2026-07-01/session-05..06`으로 수동 채택했다.
- 각 session summary, TCP/UDP date-level history, TCP/UDP protocol root history 를 repository 경로 기준으로 재생성했다.
- TCP protocol root history 는 session-count 3, hard-passed true, warning-count 18,
  comparison-compatible true 상태다.
- UDP protocol root history 는 session-count 6, hard-passed true, warning-count 12,
  comparison-compatible true 상태다.
- 최신 session 기준 envelope smoke 는 TCP/UDP 모두 `envelope-compatible=true`,
  `envelope-signal-count=0`으로 통과했다.
- 다음 단위는 사용자 push 이후 확장된 reference history 기준 원격 `iouring-benchmark-artifacts.yml` artifact gate 검토다.

### D165 D164 reference 확장 원격 artifact gate 검토
- 사용자 push 이후 `iouring-benchmark-artifacts.yml`을 `master` 기준으로 수동 실행했다.
- run `28566385562`는 workflow success 로 완료됐고,
  artifact `iouring-benchmark-artifacts-2026-07-02-github-28566385562-1`을 생성했다.
- root `summary.md` 기준 TCP/UDP baseline, summary, history, envelope exit code 는 모두 0이다.
- TCP/UDP raw report count 는 각각 6이고, hard-passed true, drop/payload-error/pool-rented 0이다.
- TCP envelope 는 reference-summary-count 3, compatible true, signal-count 0이다.
- UDP envelope 는 reference-summary-count 6, compatible true, signal-count 0이다.
- D164 확장 reference history 가 원격 workflow artifact 에서 실제 envelope reference 로 사용됨을 확인했다.

### D166 post-D165 reference date 확장 설계
- `docs/superpowers/specs/2026-07-02-iouring-post-d165-reference-date-expansion-design.md`를 추가했다.
- D165 artifact 는 failure artifact 가 아니라 expanded reference 기준 signal 0 passing artifact 로 판단했다.
- 다음 단위는 fixed registration, zero-copy send, default promotion, latency hard gate 가 아니라
  D165 raw report 를 protocol별 두 번째 date root reference 로 수동 채택하는 것이다.
- TCP/UDP 모두 `2026-07-02/session-01`로 확장한다.

### D167 protocol reference date 확장 실행
- D165 TCP raw report 를 `ci-linux-iouring-x64-01/tcp/2026-07-02/session-01`로 수동 채택했다.
- D165 UDP raw report 를 `ci-linux-iouring-x64-01/udp/2026-07-02/session-01`로 수동 채택했다.
- 각 session summary, TCP/UDP date-level history, TCP/UDP protocol root history 를 repository 경로 기준으로 재생성했다.
- TCP protocol root history 는 session-count 4, hard-passed true, warning-count 24,
  comparison-compatible true 상태다.
- UDP protocol root history 는 session-count 7, hard-passed true, warning-count 13,
  comparison-compatible true 상태다.
- 최신 session 기준 envelope smoke 는 TCP/UDP 모두 `envelope-compatible=true`,
  `envelope-signal-count=0`으로 통과했다.
- 다음 단위는 사용자 push 이후 두 date root reference 기준 원격 `iouring-benchmark-artifacts.yml` artifact gate 검토다.

## 2026-06-30 (Codex - io_uring benchmark backend selector implementation)

### 작업 단위
- D145 구현 계획에 따라 benchmark CLI 에 `--backend iouring` opt-in selector 를 추가했다.

### 변경 내용
- `BenchmarkCommandParser`, `TcpLoopbackTransportBackend`, `BenchmarkRunIdentity`:
  `iouring` parser 값, `IoUring` enum, TCP/UDP io_uring profile/backend identity 를 추가했다.
- `TcpLoopbackScenarioRunner`, `UdpLoopbackScenarioRunner`, `Program`, `Hps.Benchmarks.csproj`:
  `IoUringTransport` project reference, Linux/capability gated transport factory, TCP/UDP scenario key, help text 를 연결했다.
- `Hps.Benchmarks.Tests`:
  parser/identity/help/scenario key contract 테스트를 추가하고 각 테스트에 검증 의도를 주석으로 기록했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D146으로 opt-in benchmark selector 구현 범위와 default promotion 보류를 기록했다.

### 검증
- Red: parser/identity focused test 에서 `iouring` invalid backend 및 enum 누락으로 3개 실패 확인.
- Green: parser/identity focused test 36개 통과.
- Red: scenario/help focused test 에서 TCP/UDP scenario key 와 help text 3개 실패 확인.
- Green: scenario/help focused test 7개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 전체 통과.

### 결과
- io_uring benchmark raw report 를 기존 schema 로 만들 수 있는 local CLI wiring 이 준비됐다.
- 다음 evidence 는 사용자 push 이후 Linux available runner 에서 `--backend iouring` TCP/UDP benchmark artifact 를 수집해 검토하는 것이다.

## 2026-06-30 (Codex - io_uring benchmark backend selector design)

### 작업 단위
- D144 이후 io_uring 후속 후보를 재평가하고 benchmark backend selector 를 다음 구현 단위로 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-30-iouring-benchmark-backend-selector-design.md`:
  fixed registration, zero-copy, IPv6, default promotion 후보를 비교하고 `--backend iouring`을 다음 단위로 선택했다.
- `docs/superpowers/plans/2026-06-30-iouring-benchmark-backend-selector.md`:
  parser/identity, scenario runner wiring, state docs/verification 3개 task 로 구현 계획을 작성했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D145로 io_uring benchmark backend selector 결정을 기록했다.

### 결과
- 다음 작업은 구현 계획 Task 1 parser/identity contract 를 TDD로 시작하는 것이다.

## 2026-06-30 (Codex - io_uring udp receive window artifact)

### 작업 단위
- 최신 `master` HEAD 기준 `iouring-linux-contract` workflow artifact 로 D143 bounded receive window native path 를 검토했다.

### 확인 내용
- `git fetch origin master` 후 로컬 `HEAD`와 `origin/master`는 모두
  `c426e23532a169401407412567b234eae8981d20`이다.
- 기존 run `28421177310`은 `ff4421d5dd5544f686f3eb87ee67b743d4c36746` 기준이라 D143 receive window 변경을 포함하지 않았다.
- 현재 HEAD 기준으로 `iouring-linux-contract` workflow 를 `workflow_dispatch`로 실행했고,
  run `28424009519`가 success 로 완료됐다.
- artifact `iouring-linux-contract-2026-06-30-github-28424009519-1`은
  `summary.md`, `dotnet-info.txt`, `iouring-tests.trx`를 포함한다.
- TRX counters 는 total 55, executed 55, passed 55, failed 0, error 0, timeout 0 이다.
- `IoUringCapabilityEvidenceTests` stdout 은 `io_uring capability status: Available`,
  OS `Ubuntu 24.04.4 LTS`, architecture `X64`, process architecture `X64`를 기록했다.
- UDP receive/echo/endpoint diagnostics tests 와
  `UdpReceive_WhenHandlerIsBlocked_PreservesWindowedDatagrams`가 Passed 였다.

### 결과
- D143 bounded receive slot window 의 Linux native artifact gate 를 충족한 것으로 판단했다.
- D144로 bounded receive window artifact 수락 결정을 기록했다.
- 다음 작업은 fixed registration, zero-copy send, default promotion 을 바로 구현하는 것이 아니라
  D144 이후 후속 후보를 별도 설계 단위로 재평가하는 것이다.

## 2026-06-30 (Codex - io_uring udp receive window)

### 작업 단위
- D142 artifact gate 이후 후속 후보를 재평가하고, io_uring UDP receive pump 를 bounded receive slot window 로 확장했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-30-iouring-udp-receive-window-design.md`와
  `docs/superpowers/plans/2026-06-30-iouring-udp-receive-window.md`를 추가했다.
- `IoUringUdpEndpoint`에 `ReceiveWindowSize = 4`와 slot 별 receive context/message buffer/in-flight datagram owner 를 추가했다.
- `IoUringTransport.UdpReceiveLoopAsync`가 startup 시 모든 receive slot 을 post 하고,
  completion 후 handler dispatch 전에 같은 slot 을 repost 하도록 변경했다.
- `IoUringUdpEndpointShapeTests`에 receive window slot shape/pump boundary 테스트를 추가했다.
- `IoUringTransportUdpTests`에 Linux-gated blocked handler burst 테스트
  `UdpReceive_WhenHandlerIsBlocked_PreservesWindowedDatagrams`를 추가했다.

### 검증
- Red: `UdpEndpoint_WhenConstructed_CreatesBoundedReceiveWindowSlots`가 `ReceiveWindowSize` 누락으로 실패.
- Green: focused receive slot shape test 통과.
- Red: `UdpReceiveSlot_WhenInspected_ExposesPumpStateBoundary`가 slot pump helper 누락으로 실패.
- Green: focused slot pump boundary test 통과.
- `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringUdpEndpointShapeTests -v minimal` 통과.
- `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringTransportUdpTests -v minimal` 통과.
- `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --no-restore -v minimal` 55개 통과.

### 결과
- D143으로 bounded receive slot window 결정을 기록했다.
- 다음 작업은 사용자 push 이후 `iouring-linux-contract` artifact 로 Linux native bounded receive window path 를 검토하는 것이다.

## 2026-06-30 (Codex - io_uring udp linux contract artifact)

### 작업 단위
- 최신 `master` HEAD 기준 `iouring-linux-contract` workflow artifact 를 검토했다.

### 확인 내용
- `git fetch origin master` 후 로컬 `HEAD`와 `origin/master`는 모두
  `ff4421d5dd5544f686f3eb87ee67b743d4c36746`이다.
- 기존 run `28411459951`은 `a4d42ddfd62f750551520c33ea756151f524d332` 기준이라 현재 UDP pump 상태를 포함하지 않았다.
- 현재 HEAD 기준으로 `iouring-linux-contract` workflow 를 `workflow_dispatch`로 실행했고,
  run `28421177310`이 success 로 완료됐다.
- artifact `iouring-linux-contract-2026-06-30-github-28421177310-1`은
  `summary.md`, `dotnet-info.txt`, `iouring-tests.trx`를 포함한다.
- TRX counters 는 total 52, executed 52, passed 52, failed 0, error 0, timeout 0 이다.
- `IoUringCapabilityEvidenceTests` stdout 은 `io_uring capability status: Available`,
  OS `Ubuntu 24.04.4 LTS`, architecture `X64`를 기록했다.
- UDP 핵심 경로인 `UdpReceive_WhenIoUringAvailable_DeliversOwnedRefCountedBuffer`와
  `UdpEcho_WhenIoUringAvailable_QueuesResponseAndClientReceivesPayload`가 Passed 였다.

### 결과
- D140 UDP v1의 Linux native `recvmsg`/`sendmsg` artifact gate 를 충족한 것으로 판단했다.
- D142로 UDP pump native 검증 gate 충족 결정을 기록했다.
- 다음 작업은 fixed registration, zero-copy, receive window, default promotion 을 바로 구현하는 것이 아니라
  artifact 이후 후속 후보를 별도 설계 단위로 재평가하는 것이다.

## 2026-06-30 (Codex - io_uring endpoint diagnostics)

### 작업 단위
- `io_uring` backend 의 endpoint diagnostics snapshot surface 를 SAEA/RIO와 맞췄다.

### 변경 내용
- `src/Hps.Transport.IoUring/IoUringTransport.cs`:
  `ITransportEndpointDiagnostics`를 구현하고 TCP connection/UDP endpoint registry snapshot 을 반환하도록 했다.
- `tests/Hps.Transport.IoUring.Tests/IoUringTransportUdpTests.cs`:
  등록된 UDP endpoint 가 `EndpointSnapshot`으로 보이고, endpoint close 뒤 snapshot 목록에서 제거되는지 검증했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D141로 io_uring endpoint diagnostics parity 결정을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `docs/agent-state/changelog/2026-06.md`:
  remote artifact blocker 와 이번 로컬 diagnostics parity 완료 상태를 갱신했다.

### 검증
- Red: focused diagnostics test 가 `Assert.IsAssignableFrom()` failure 로 실패.
- Green: focused diagnostics test 1개 통과.
- `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --no-restore -v minimal` 통과, 52개 통과.

## 2026-06-30 (Codex - io_uring udp remote artifact recheck)

### 작업 단위
- 남은 `iouring-linux-contract` UDP artifact 검토 가능 여부를 원격 상태 기준으로 재확인했다.

### 확인 내용
- `git fetch origin master` 후에도 로컬 `master`는 `origin/master`보다 8 commits ahead 상태다.
- GitHub Actions 최신 `iouring-linux-contract` run 은 `28411459951`이며,
  `headSha=a4d42ddfd62f750551520c33ea756151f524d332`에서 실행됐다.
- 현재 로컬 HEAD는 `a685364e660cebecfd1971ce2a0793455ce766f3`이므로,
  최신 원격 artifact 는 현재 UDP pump 및 로컬 계약 보강 커밋을 포함하지 않는다.

### 결과
- 현재 run artifact 로는 Linux `recvmsg`/`sendmsg` UDP native path 검증을 완료할 수 없다.
- D140 제외 범위인 fixed registration, zero-copy send, receive window depth 확장, default backend promotion 은 계속 열지 않는다.

## 2026-06-30 (Codex - io_uring udp local contract hardening)

### 작업 단위
- 원격 UDP artifact 검토 대기 전, 로컬에서 검증 가능한 `io_uring` UDP 계약 보강 테스트를 추가했다.

### 변경 내용
- `tests/Hps.Transport.IoUring.Tests/IoUringUdpEndpointShapeTests.cs`:
  reflection 중심 setup 을 직접 내부 타입 기반 helper 로 정리하고,
  `TrySendTo` 성공/거절 소유권, closed endpoint/IPv6 remote 거절, drop-oldest diagnostics 를 검증했다.
- `tests/Hps.Transport.IoUring.Tests/IoUringUdpMessageShapeTests.cs`:
  `IoUringUdpMessageBuffer`의 send metadata roundtrip 과 Dispose 이후 재사용 거절을 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`, `docs/agent-state/changelog/2026-06.md`:
  원격 artifact 검토가 여전히 다음 핵심 검증 축이고, 이번 단위는 프로덕션 코드 변경 없는 로컬 계약 보강임을 기록했다.

### 검증
- `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --no-restore -v minimal` 통과, 51개 통과.

## 2026-06-30 (Codex - io_uring udp pump state docs)

### 작업 단위
- Phase 6 io_uring UDP pump 구현 계획 Task 5 State Docs And Verification 을 수행했다.

### 변경 내용
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 1~5 완료 상태와 다음 실행 지점인 원격 `iouring-linux-contract` UDP artifact 검토를 반영했다.
- `docs/superpowers/plans/2026-06-30-iouring-udp-pump.md`:
  Task 5 체크리스트를 완료 처리했다.
- `CHANGELOG_AGENT.md`, `docs/agent-state/changelog/2026-06.md`:
  최종 state docs 정렬 작업을 기록했다.

### 검증
- `rg -n "D140|IPv4 one-deep|recvmsg|sendmsg" DECISIONS.md docs\agent-state\decisions\2026-06.md` 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과, 426개 통과.
- `git diff --check` 통과.

## 2026-06-30 (Codex - io_uring udp send pump)

### 작업 단위
- Phase 6 io_uring UDP pump 구현 계획 Task 4 UDP Send Pump And Ownership 을 TDD로 구현했다.

### 변경 내용
- `src/Hps.Transport.IoUring/IoUringTransport.cs`:
  `TrySendTo(...)`, endpoint send loop, one-deep `sendmsg` submit/wait path 를 추가했다.
- `tests/Hps.Transport.IoUring.Tests/IoUringTransportUdpTests.cs`:
  send pump shape Red 와 Linux-gated UDP echo loopback 검증을 추가했다.
- `tests/Hps.Transport.IoUring.Tests/IoUringUdpEndpointShapeTests.cs`:
  pending queue capacity 초과 시 drop-oldest ref 반환과 close drain 을 검증했다.
- `CURRENT_PLAN.md`, `TODOS.md`, `docs/superpowers/plans/2026-06-30-iouring-udp-pump.md`:
  Task 4 완료와 Task 5 진입점을 반영했다.

### 검증
- Red: `IoUringTransportUdpTests` send shape test 가 `TrySendTo` override/send pump 경계 부재로 실패.
- Green: focused Task 4 tests 3개 통과.
- `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --no-build --no-restore -v minimal` 통과, 46개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과, 426개 통과.
- `git diff --check` 통과.

## 2026-06-30 (Codex - io_uring udp receive pump)

### 작업 단위
- Phase 6 io_uring UDP pump 구현 계획 Task 3 UDP Bind And Receive Pump 를 TDD로 구현했다.

### 변경 내용
- `src/Hps.Transport.IoUring/IoUringTransport.cs`:
  IPv4 UDP `BindUdpAsync(...)`, endpoint 등록/해제, transport stop dispose, one-deep `recvmsg` receive loop 를 추가했다.
- `src/Hps.Transport.IoUring/IoUringUdpEndpoint.cs`:
  public close 시 transport endpoint 목록에서 제거되도록 연결했다.
- `tests/Hps.Transport.IoUring.Tests/IoUringTransportUdpTests.cs`:
  receive pump shape Red 와 Linux-gated UDP receive loopback 검증을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`, `docs/superpowers/plans/2026-06-30-iouring-udp-pump.md`:
  Task 3 완료와 Task 4 진입점을 반영했다.

### 검증
- Red: `IoUringTransportUdpTests` shape test 가 `_udpEndpoints`/receive pump 경계 부재로 실패.
- Green: focused `IoUringTransportUdpTests` 2개 통과.
- `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --no-build --no-restore -v minimal` 통과, 43개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과, 423개 통과.
- `git diff --check` 통과.

## 2026-06-30 (Codex - io_uring udp endpoint resource)

### 작업 단위
- Phase 6 io_uring UDP pump 구현 계획 Task 2 UDP Endpoint Resource And Message Buffer 를 TDD로 구현했다.

### 변경 내용
- `src/Hps.Transport.IoUring/IoUringUdpMessageBuffer.cs`:
  UDP `recvmsg`/`sendmsg`가 completion 전까지 참조하는 `msghdr`/`iovec`/sockaddr scratch 수명을 고정했다.
- `src/Hps.Transport.IoUring/IoUringUdpEndpoint.cs`:
  UDP socket, receive pool, receive/send operation context, pending send queue, close drain ownership 을 소유하는 endpoint resource 를 추가했다.
- `tests/Hps.Transport.IoUring.Tests/IoUringUdpEndpointShapeTests.cs`:
  endpoint/message buffer type shape 와 close drain 시 queued send ref 반환을 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`, `docs/superpowers/plans/2026-06-30-iouring-udp-pump.md`:
  Task 2 완료와 Task 3 진입점을 반영했다.

### 검증
- Red: `IoUringUdpEndpointShapeTests` 2개가 endpoint/message buffer type 부재로 실패.
- Green: focused `IoUringUdpEndpointShapeTests` 2개 통과.
- `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --no-build --no-restore -v minimal` 통과, 41개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과, 421개 통과.
- `git diff --check` 통과.

## 2026-06-30 (Codex - io_uring udp message shape)

### 작업 단위
- Phase 6 io_uring UDP pump 구현 계획 Task 1 Native UDP Message Shape 를 TDD로 구현했다.

### 변경 내용
- `src/Hps.Transport.IoUring/IoUringNative.cs`:
  `IORING_OP_RECVMSG`, `IORING_OP_SENDMSG`, Linux `IoUringMessageHeader` shape 를 추가했다.
- `src/Hps.Transport.IoUring/IoUringQueue.cs`:
  `TrySubmitReceiveMessage`, `TrySubmitSendMessage` helper 를 추가했다.
- `src/Hps.Transport.IoUring/IoUringOperationKind.cs`:
  UDP receive/send operation kind 를 추가했다.
- `src/Hps.Transport.IoUring/IoUringSockaddr.cs`:
  IPv4 sockaddr encode/decode helper 를 추가했다.
- `tests/Hps.Transport.IoUring.Tests/IoUringUdpMessageShapeTests.cs`:
  UDP message shape 와 IPv4 sockaddr roundtrip 을 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`, `docs/superpowers/plans/2026-06-30-iouring-udp-pump.md`:
  Task 1 완료와 Task 2 진입점을 반영했다.

### 검증
- Red: `IoUringUdpMessageShapeTests` 2개가 missing type/member 로 실패.
- Green: focused `IoUringUdpMessageShapeTests` 2개 통과.
- `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --no-build --no-restore -v minimal` 통과, 39개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 통과, 경고 0/오류 0.

## 2026-06-30 (Codex - io_uring udp pump design)

### 작업 단위
- Phase 6 io_uring UDP pump 설계와 TDD 구현 계획을 작성했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-30-iouring-udp-pump-design.md`:
  D139 이후 첫 UDP 구현을 IPv4 one-deep `recvmsg`/`sendmsg` pump 로 제한하는 설계를 작성했다.
- `docs/superpowers/plans/2026-06-30-iouring-udp-pump.md`:
  Native UDP message shape, endpoint resource, receive pump, send pump, state docs 의 5개 task 로 구현 계획을 작성했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D140을 추가해 IPv4 one-deep UDP v1 boundary 를 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 구현 계획 Task 1 Native UDP Message Shape 로 갱신했다.

### 검증
- spec/plan self-review 로 D137/D139 정합성, SAEA/RIO UDP 선례, excluded scope 를 확인했다.
- 다음 검증은 Task 1 Red test 부터 시작한다.

## 2026-06-30 (Codex - io_uring linux contract artifact review)

### 작업 단위
- 원격 `iouring-linux-contract` workflow 를 실행하고 artifact 를 검토했다.

### 변경 내용
- GitHub Actions run `28411459951`:
  `workflow_dispatch`로 `master`의 `a4d42dd`에서 실행했고 success 로 완료됐다.
- artifact `iouring-linux-contract-2026-06-30-github-28411459951-1`:
  `summary.md`, `dotnet-info.txt`, `iouring-tests.trx`를 확인했다.
  Ubuntu 24.04 x64 runner, Linux kernel `6.17.0-1018-azure`, .NET SDK `9.0.315` 환경이다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D139를 추가해 D138 Linux contract gate 충족을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  원격 artifact 검토 항목과 Linux available host TCP loopback 검증을 완료 처리하고,
  다음 실행 지점을 io_uring UDP pump 설계와 TDD 구현 계획으로 갱신했다.

### 검증
- workflow conclusion success, test exit code 0.
- TRX counters: total 37, executed 37, passed 37, failed 0.
- `IoUringCapabilityEvidenceTests`: `io_uring capability status: Available`,
  OS `Ubuntu 24.04.4 LTS`, architecture `X64`.
- capability available 상태에서 `TcpLoopback_WhenIoUringAvailable_DeliversReceivedBytes`와
  `TcpLoopback_WhenIoUringAvailable_SendsQueuedPayloadToPeer`가 통과했다.

## 2026-06-29 (Codex - io_uring linux contract remote gate probe)

### 작업 단위
- 원격 `iouring-linux-contract` artifact 검토 가능 여부를 확인했다.

### 변경 내용
- `CURRENT_PLAN.md`, `TODOS.md`:
  원격 확인 결과를 반영했다. `git fetch origin master` 이후에도 로컬 `master`는 `origin/master`보다 24 commits ahead 이며,
  `gh run list --workflow iouring-linux-contract.yml`은 원격 기본 브랜치에서 workflow 를 찾지 못했다.
- D138 gate 는 유지한다. 원격 workflow commit 과 `workflow_dispatch` artifact 가 확인되기 전에는
  UDP pump/zero-copy 구현으로 넘어가지 않는다.

### 검증
- `git fetch origin master` 통과.
- `git status --short --branch`에서 `master...origin/master [ahead 24]`를 확인했다.
- `gh run list --workflow iouring-linux-contract.yml`은 HTTP 404로 workflow 미반영 상태를 확인했다.

## 2026-06-29 (Codex - io_uring linux contract gate state)

### 작업 단위
- Linux io_uring contract gate Task 3 state documents and decision 을 수행했다.

### 변경 내용
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D138이 active/archive decision 에 기록된 상태를 유지했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  workflow local validation 완료와 remote `workflow_dispatch` artifact 미실행 상태를 명시했다.
  다음 실행 지점은 원격 `iouring-linux-contract` artifact 검토다.
- `CHANGELOG_AGENT.md`, `docs/agent-state/changelog/2026-06.md`:
  이번 state 정리와 검증 결과를 기록했다.

### 검증
- D138/state consistency scan 으로 D138, workflow path, current TODO 연결을 확인한다.
- `git diff --check` 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과, 전체 417개 통과.
- 원격 GitHub workflow 실행은 아직 수행하지 않았다.

## 2026-06-29 (Codex - io_uring linux contract workflow)

### 작업 단위
- Linux io_uring contract gate Task 2 Linux contract workflow 를 추가했다.

### 변경 내용
- `.github/workflows/iouring-linux-contract.yml`:
  `workflow_dispatch` 전용 `ubuntu-latest` workflow 를 추가했다.
  workflow 는 `Hps.Transport.IoUring.Tests`를 실행하고 TRX, `dotnet-info.txt`, `summary.md`를 upload artifact 로 남긴다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 2 완료와 다음 Task 3 state documents and decision 진입점을 반영했다.

### 검증
- workflow marker scan: `workflow_dispatch`, `ubuntu-latest`, `IOURING_CONTRACT_ROOT`, `iouring-tests.trx`, `upload-artifact` 확인.
- `git diff --check` 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과, 전체 417개 통과.
- 원격 GitHub workflow 실행은 아직 수행하지 않았다.

## 2026-06-29 (Codex - io_uring capability evidence test)

### 작업 단위
- Linux io_uring contract gate Task 1 capability evidence test 를 추가했다.

### 변경 내용
- `tests/Hps.Transport.IoUring.Tests/IoUringCapabilityEvidenceTests.cs`:
  `IoUringCapabilityProbe.GetStatus()` 결과와 OS/process architecture 를 xUnit output 으로 남긴다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 1 완료와 다음 Task 2 Linux contract workflow 진입점을 반영했다.

### 검증
- `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringCapabilityEvidenceTests -v minimal` 통과, 1개 통과.
- `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --no-build --no-restore -v minimal` 통과, 37개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과, 전체 417개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.

## 2026-06-29 (Codex - io_uring linux contract gate design)

### 작업 단위
- Phase 6 io_uring 후속 후보를 재평가하고 Linux contract gate 설계와 구현 계획을 작성했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-29-iouring-linux-contract-gate-design.md`:
  UDP pump/zero-copy 최적화 전에 Linux contract evidence gate 를 먼저 두는 정책을 정리했다.
- `docs/superpowers/plans/2026-06-29-iouring-linux-contract-gate.md`:
  capability evidence test, Linux workflow, D138 state docs 의 3개 구현 task 로 나눴다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D138을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 capability evidence test 로 갱신했다.

### 검증
- spec/plan self-review 로 placeholder, scope, decision consistency 를 확인했다.
- `git diff --check`로 문서 공백 오류를 확인한다.

## 2026-06-29 (Codex - io_uring tcp pump boundary docs)

### 작업 단위
- Phase 6 TCP-first io_uring queue/pump Task 7 state documents and full verification 을 수행했다.

### 변경 내용
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D137로 TCP-first io_uring pump 구현 boundary 를 수락했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 7 완료, Linux actual host 검증 한계, 다음 Phase 6 후속 후보 재평가 진입점을 기록했다.
- `CHANGELOG_AGENT.md`, `docs/agent-state/changelog/2026-06.md`:
  이번 문서 정렬과 검증 결과를 남겼다.

### 검증
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과, 전체 416개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.

## 2026-06-29 (Codex - io_uring tcp send pump)

### 작업 단위
- Phase 6 TCP-first io_uring queue/pump Task 6 TCP send pump and ownership 을 TDD로 구현했다.

### 변경 내용
- `src/Hps.Transport.IoUring/IoUringQueue.cs`:
  `TrySubmitSend(...)`를 추가해 SEND SQE submit 경계를 제공한다.
- `src/Hps.Transport.IoUring/IoUringTransport.cs`:
  기존 `TransportConnection` pending queue/in-flight ownership 을 drain 하는 send loop 를 추가했다.
  length-prefix 는 pinned prefix block 을 먼저 보내고 payload 는 `TransportSendBuffer` slice 를 사용한다.
- `tests/Hps.Transport.IoUring.Tests/IoUringSendPumpShapeTests.cs`,
  `tests/Hps.Transport.IoUring.Tests/IoUringTransportTcpTests.cs`:
  Windows에서 실행 가능한 send pump shape Red와 Linux-available gated send loopback을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 6 완료와 다음 Task 7 state docs/full verification 진입점을 반영했다.

### 검증
- Red: focused `IoUringSendPumpShapeTests` 실행으로 send pump queue/transport shape 부재 `Assert.NotNull()` failure 1개를 확인했다.
- Green: focused `IoUringSendPumpShapeTests` 1개 통과.
- Project: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal` 36개 통과.
- 실제 Linux io_uring syscall send loopback 은 현재 Windows 환경에서 실행되지 않아 deferred 로 기록했다.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과, 전체 416개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.

## 2026-06-29 (Codex - io_uring tcp receive pump)

### 작업 단위
- Phase 6 TCP-first io_uring queue/pump Task 5 TCP receive pump 를 TDD로 구현했다.

### 변경 내용
- `src/Hps.Transport.IoUring/IoUringQueue.cs`:
  `TrySubmitReceive(...)`와 `TryDequeueCompletion(...)`을 추가해 RECV SQE submit 과 CQE drain 경계를 제공한다.
- `src/Hps.Transport.IoUring/IoUringCompletionLoop.cs`:
  queue completion 을 polling drain 해 registry context 로 dispatch 하는 background loop 를 추가했다.
- `src/Hps.Transport.IoUring/IoUringTcpConnectionResource.cs`:
  receive pump 가 사용할 socket fd 와 queue 접근자를 제공한다.
- `src/Hps.Transport.IoUring/IoUringTransport.cs`:
  connection 생성 후 receive loop를 시작하고, positive completion 을 receive handler 로 전달한다.
- `tests/Hps.Transport.IoUring.Tests/IoUringReceivePumpShapeTests.cs`,
  `tests/Hps.Transport.IoUring.Tests/IoUringTransportTcpTests.cs`:
  Windows에서 실행 가능한 receive pump shape Red와 Linux-available gated loopback을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 5 완료와 다음 Task 6 TCP send pump 진입점을 반영했고,
  실제 Linux available host 검증은 Deferred Backlog 로 남겼다.

### 검증
- Red: focused `IoUringReceivePumpShapeTests` 실행으로 receive pump queue/transport shape 부재 `Assert.NotNull()` failure 1개를 확인했다.
- Green: focused `IoUringReceivePumpShapeTests` 1개 통과.
- Project: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal` 34개 통과.
- 실제 Linux io_uring syscall loopback 은 현재 Windows 환경에서 실행되지 않아 deferred 로 기록했다.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과, 전체 414개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.

## 2026-06-29 (Codex - io_uring tcp resource boundary)

### 작업 단위
- Phase 6 TCP-first io_uring queue/pump Task 4 TCP resource and listener wiring 을 TDD로 구현했다.

### 변경 내용
- `src/Hps.Transport.IoUring/IoUringConnectionListener.cs`:
  .NET Socket accept control plane 과 `TransportConnection` 생성 경계를 분리했다.
- `src/Hps.Transport.IoUring/IoUringTcpConnectionResource.cs`:
  socket, receive/prefix pinned blocks, receive/send operation context 수명을 한 resource owner 로 묶었다.
- `src/Hps.Transport.IoUring/IoUringTransport.cs`:
  Linux available 상태에서 queue/registry/completion loop 를 준비하고 TCP listen/connect/accept boundary 를 연결했다.
  receive/send SQE pump 는 아직 시작하지 않는다.
- `src/Hps.Transport/Properties/AssemblyInfo.cs`:
  RIO backend 와 같은 방식으로 `Hps.Transport.IoUring`에 transport runtime internal 접근을 열었다.
- `tests/Hps.Transport.IoUring.Tests/IoUringTransportTcpTests.cs`:
  TCP listener/resource type, resource dispose ownership, non-Linux unsupported, default SAEA 유지 경계를 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 4 완료와 다음 Task 5 TCP receive pump 진입점을 반영했다.

### 검증
- Red: focused `IoUringTransportTcpTests` 실행으로 TCP listener/resource type 부재 `Assert.NotNull()` failure 2개를 확인했다.
- Green: focused `IoUringTransportTcpTests` 4개 통과.
- Project: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal` 32개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과, 전체 412개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.

## 2026-06-29 (Codex - io_uring completion loop)

### 작업 단위
- Phase 6 TCP-first io_uring queue/pump Task 3 shared completion loop boundary 를 TDD로 구현했다.

### 변경 내용
- `src/Hps.Transport.IoUring/IoUringCompletionLoop.cs`:
  CQE `user_data` token 을 `IoUringOperationRegistry` context 로 dispatch 하는 completion loop boundary 를 추가했다.
  현재 단계에서는 native CQ drain thread 없이 pure dispatch 와 lifecycle shell 만 제공한다.
- `tests/Hps.Transport.IoUring.Tests/IoUringCompletionLoopTests.cs`:
  completion loop type/method shape, matching token dispatch, missing token failure, waiter 없는 context failure 를 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 3 완료와 다음 Task 4 TCP resource/listener wiring 진입점을 반영했다.

### 검증
- Red: focused `IoUringCompletionLoopTests` 실행으로 completion loop type 부재 `Assert.NotNull()` failure 4개를 확인했다.
- Green: focused `IoUringCompletionLoopTests` 4개 통과.
- Project: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal` 28개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과, 전체 408개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.

## 2026-06-29 (Codex - io_uring operation registry)

### 작업 단위
- Phase 6 TCP-first io_uring queue/pump Task 2 operation registry and completion context 를 TDD로 구현했다.

### 변경 내용
- `src/Hps.Transport.IoUring/IoUringOperationKind.cs`:
  io_uring operation kind 를 receive/send/accept 로 구분한다.
- `src/Hps.Transport.IoUring/IoUringCompletion.cs`:
  CQE token/result/flags 를 managed value 로 보존한다.
- `src/Hps.Transport.IoUring/IoUringOperationContext.cs`:
  reusable operation context 의 token/kind/waiter 상태와 one-shot completion 계약을 추가했다.
- `src/Hps.Transport.IoUring/IoUringOperationRegistry.cs`:
  SQE `user_data` token 과 operation context mapping 을 단일 lock 으로 발급/조회/제거한다.
- `tests/Hps.Transport.IoUring.Tests/IoUringOperationRegistryTests.cs`:
  token routing, unregister, wait-before-complete, double-complete reject, reset 재사용을 reflection 기반으로 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 2 완료와 다음 Task 3 shared completion loop boundary 진입점을 반영했다.

### 검증
- Red: focused `IoUringOperationRegistryTests` 실행으로 registry/context type 부재 `Assert.NotNull()` failure 6개를 확인했다.
- Green: focused `IoUringOperationRegistryTests` 6개 통과.
- Project: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal` 24개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과, 전체 404개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.

## 2026-06-29 (Codex - io_uring tcp submission shape)

### 작업 단위
- Phase 6 TCP-first io_uring queue/pump Task 1 native SQE/CQE/enter shape 를 TDD로 구현했다.

### 변경 내용
- `src/Hps.Transport.IoUring/IoUringNative.cs`:
  TCP `SEND`/`RECV` opcode, `io_uring_enter` wrapper, SQE/CQE ABI struct 를 추가했다.
- `tests/Hps.Transport.IoUring.Tests/IoUringSubmissionShapeTests.cs`:
  SQE/CQE shape, opcode field, enter wrapper 존재를 reflection 기반으로 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 1 완료와 다음 Task 2 operation registry/context 진입점을 반영했다.

### 검증
- Red: focused `IoUringSubmissionShapeTests` 실행으로 SQE type 부재 `Assert.NotNull()` failure 1개를 확인했다.
- Green: focused `IoUringSubmissionShapeTests` 1개 통과.
- Project: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal` 18개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과, 전체 398개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.

## 2026-06-29 (Codex - io_uring tcp-first pump design)

### 작업 단위
- Phase 6 TCP-first io_uring queue/pump 설계와 구현 계획을 작성했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-29-iouring-tcp-first-pump-design.md`:
  transport shared queue/completion loop, reusable operation context, TCP receive/send 흐름, fixed buffer 사용 경계를 설계했다.
- `docs/superpowers/plans/2026-06-29-iouring-tcp-first-pump.md`:
  native SQE/CQE/enter shape, operation registry, completion loop, TCP resource/listener, receive pump, send pump,
  state docs/full verification 의 7개 구현 단위로 분해했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D136으로 TCP-first io_uring pump 설계 선택을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 implementation plan Task 1 native SQE/CQE/enter shape 로 넘겼다.

### 검증
- spec/plan placeholder scan 을 수행했다. `TODO` hits 는 파일 경로 `TODOS.md`만 해당한다.
- type consistency scan 으로 `IoUringOperationContext`, `IoUringOperationRegistry`, `IoUringCompletionLoop`,
  `IoUringTcpConnectionResource`, `IoUringConnectionListener` 이름 사용을 확인했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과, 전체 397개 통과.

## 2026-06-29 (Codex - io_uring native wrapper boundary record)

### 작업 단위
- Linux io_uring native wrapper shape Task 5 state documents and full verification 을 수행했다.

### 변경 내용
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D135로 native wrapper boundary 완료와 TCP/UDP pump 후속 분리를 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  현재 실행 지점을 TCP-first io_uring queue/pump 설계로 넘겼다.
- `CHANGELOG_AGENT.md`, `docs/agent-state/changelog/2026-06.md`:
  Task 5 결과와 검증 요약을 기록했다.

### 검증
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과, 전체 397개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.

## 2026-06-29 (Codex - io_uring fixed buffer registration owner)

### 작업 단위
- Linux io_uring native wrapper shape Task 4 fixed buffer registration owner boundary 를 TDD로 구현했다.

### 변경 내용
- `src/Hps.Transport.IoUring/IoUringNative.cs`:
  `io_uring_register` buffers/unregister syscall wrapper 와 `iovec` ABI struct 를 추가했다.
- `src/Hps.Transport.IoUring/IoUringQueue.cs`:
  registration owner 가 사용할 internal fd 접근자를 추가했다.
- `src/Hps.Transport.IoUring/IoUringRegisteredBufferSet.cs`:
  managed buffer pinning 과 kernel fixed buffer registration 수명을 함께 소유하는 owner boundary 를 추가했다.
- `tests/Hps.Transport.IoUring.Tests/IoUringRegisteredBufferSetTests.cs`:
  owner type 존재와 non-Linux unsupported boundary 를 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 4 완료와 다음 Task 5 state documents/full verification 진입점을 반영했다.

### 검증
- Red: focused `IoUringRegisteredBufferSetTests` 실행으로 type 부재 `Assert.NotNull()` failure 2개를 확인했다.
- Green: focused `IoUringRegisteredBufferSetTests` 2개 통과.
- Project: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal` 17개 통과.
- Full verification: 커밋 전 `dotnet build`, `dotnet test`, `git diff --check`로 수행한다.

## 2026-06-29 (Codex - io_uring capability probe wiring)

### 작업 단위
- Linux io_uring native wrapper shape Task 3 capability probe wiring 을 TDD로 구현했다.

### 변경 내용
- `src/Hps.Transport.IoUring/IoUringCapabilityProbe.cs`:
  platform guard 뒤에 `IoUringQueue.TryCreateForProbe(2)`를 호출해 Linux 에서 실제 작은 ring setup/close probe 를 수행한다.
- `tests/Hps.Transport.IoUring.Tests/IoUringCapabilityProbeTests.cs`:
  probe result mapping internal overload 와 Linux 예외 격리 경로를 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 3 완료와 다음 Task 4 fixed buffer registration owner boundary 진입점을 반영했다.

### 검증
- Red: focused `IoUringCapabilityProbeTests` 실행으로 internal `GetStatus(IoUringQueueProbeResult)` overload 부재
  `Assert.NotNull()` failure 1개를 확인했다.
- Green: focused `IoUringCapabilityProbeTests` 5개 통과.
- Project: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal` 15개 통과.
- Full verification: 커밋 전 `dotnet build`, `dotnet test`, `git diff --check`로 수행한다.

## 2026-06-29 (Codex - io_uring queue owner)

### 작업 단위
- Linux io_uring native wrapper shape Task 2 queue setup owner 를 TDD로 구현했다.

### 변경 내용
- `src/Hps.Transport.IoUring/IoUringNative.cs`:
  `io_uring_setup`, `mmap`, `munmap`, `close` adapter 와 ABI struct 를 추가했다.
- `src/Hps.Transport.IoUring/IoUringSafeHandle.cs`, `IoUringMemoryMap.cs`, `IoUringQueue.cs`:
  fd, mmap, queue setup owner 를 추가했다.
- `tests/Hps.Transport.IoUring.Tests/IoUringQueueTests.cs`:
  queue owner type 존재, non-Linux unsupported boundary, Linux probe result escape 방지를 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 2 완료와 다음 Task 3 capability probe wiring 진입점을 반영했다.

### 검증
- Red: focused `IoUringQueueTests` 실행으로 `IoUringQueue` type 부재 `Assert.NotNull()` failure 2개를 확인했다.
- Green: focused `IoUringQueueTests` 3개 통과.
- Project: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal` 13개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과, 전체 393개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.

## 2026-06-29 (Codex - io_uring native guard)

### 작업 단위
- Linux io_uring native wrapper shape Task 1 native ABI shell/platform guard 를 TDD로 구현했다.

### 변경 내용
- `src/Hps.Transport.IoUring/IoUringNative.cs`:
  internal native adapter shell 과 platform/architecture guard 를 추가했다.
- `src/Hps.Transport.IoUring/Properties/AssemblyInfo.cs`:
  `Hps.Transport.IoUring.Tests`에 internal shape 검증 접근을 허용했다.
- `tests/Hps.Transport.IoUring.Tests/IoUringNativeShapeTests.cs`:
  type existence, non-Linux platform status, unsupported exception boundary 를 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 1 완료와 다음 Task 2 queue setup owner 진입점을 반영했다.

### 검증
- Red: focused `IoUringNativeShapeTests` 실행으로 `IoUringNative` type 부재 `Assert.NotNull()` failure 3개를 확인했다.
- Green: focused `IoUringNativeShapeTests` 3개 통과.
- Project: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal` 10개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과, 전체 390개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.

## 2026-06-29 (Codex - io_uring native wrapper design)

### 작업 단위
- Linux io_uring native syscall wrapper shape 를 설계하고 후속 TDD 구현 계획을 작성했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-29-iouring-native-wrapper-shape-design.md`:
  native syscall adapter, queue owner, fixed buffer registration owner 분리 설계를 작성했다.
- `docs/superpowers/plans/2026-06-29-iouring-native-wrapper-shape.md`:
  native guard, queue setup owner, capability probe wiring, fixed buffer registration owner, 상태 문서 검증으로 나눈 구현 계획을 작성했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D134로 `IoUringNative`, `IoUringQueue`, `IoUringRegisteredBufferSet` 책임 분리를 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 implementation plan Task 1 native ABI shell/platform guard 로 넘겼다.

### 검증
- spec/plan placeholder scan 과 type consistency scan 을 수행했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.

## 2026-06-29 (Codex - io_uring boundary skeleton record)

### 작업 단위
- Phase 6 Linux io_uring boundary Task 3 state docs/full verification 을 완료했다.

### 변경 내용
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D133으로 Phase 6 첫 io_uring 구현을 skeleton/probe/unsupported boundary 까지로 제한하고,
  native syscall wrapper 와 TCP/UDP pump 를 후속 task 로 분리했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  현재 Phase 를 Phase 6로 갱신하고, 다음 실행 지점을 Linux io_uring native syscall wrapper shape 설계로 넘겼다.

### 검증
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 통과, 경고 0/오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과, 전체 387개 통과.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.

## 2026-06-29 (Codex - io_uring transport boundary)

### 작업 단위
- Phase 6 Linux io_uring boundary Task 2 `IoUringTransport` lifecycle/unsupported boundary 를 TDD로 구현했다.

### 변경 내용
- `src/Hps.Transport.IoUring/IoUringTransport.cs`:
  native 자원을 아직 열지 않는 opt-in transport root type 을 추가했다.
  `StartAsync`/`StopAsync`는 lifecycle shell 로 동작하고, TCP listen/connect 와 UDP bind 는 현재 boundary 에서
  명시적 `NotSupportedException`으로 수렴한다.
- `tests/Hps.Transport.IoUring.Tests/IoUringTransportTests.cs`:
  root type construction/start/stop, non-Linux TCP listen/connect, UDP bind unsupported boundary 를 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 2 완료와 다음 Task 3 state docs/full verification 진입점을 반영했다.

### 검증
- Red: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringTransportTests -v minimal`
  실행 결과 `IoUringTransport` type 부재 `Assert.NotNull()` failure 4개를 확인했다.
- Green: 같은 focused test 4개 통과.
- Project: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal` 7개 통과.

## 2026-06-29 (Codex - Phase 6 io_uring next candidate)

### 작업 단위
- D131 이후 다음 실행 후보를 재평가하고, 다음 방향을 Phase 6 Linux io_uring backend boundary 설계/계획으로 좁혔다.

### 변경 내용
- `docs/superpowers/specs/2026-06-29-phase6-iouring-boundary-next-candidate-design.md`:
  CI gate 승격, RIO default promotion/full IPv6, server-level diagnostics API를 지금 열지 않는 근거와,
  `Hps.Transport.IoUring` skeleton/capability probe/unsupported boundary 를 첫 구현 후보로 두는 결정을 기록했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D132를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 진입점을 Phase 6 io_uring boundary 첫 구현 계획 작성으로 갱신했다.

### 검증
- D131 CI baseline 상태, D090/D095/D119/D122/D125/D128 결정, `PLAN.md` Phase 6, 현재 project layout 을 대조했다.

## 2026-06-29 (Codex - Phase 6 io_uring boundary plan)

### 작업 단위
- D132 설계를 첫 구현 가능한 TDD task 로 분해했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-29-iouring-boundary.md`:
  Phase 6 io_uring boundary 구현을 Task 1 project skeleton/capability probe,
  Task 2 `IoUringTransport` lifecycle/unsupported boundary, Task 3 state docs/full verification 으로 나눴다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 Task 1 project skeleton/capability probe 구현으로 갱신했다.

### 검증
- D132 spec coverage, RIO skeleton/probe 기존 패턴, `TransportBase` abstract member, solution project layout 을 대조했다.

## 2026-06-29 (Codex - io_uring capability probe)

### 작업 단위
- Phase 6 Linux io_uring boundary Task 1 project skeleton/capability probe 를 TDD로 구현했다.

### 변경 내용
- `src/Hps.Transport.IoUring/`:
  새 source project 와 `IoUringCapabilityStatus`, `IoUringCapabilityProbe.GetStatus()`를 추가했다.
  non-Linux 는 `UnsupportedOperatingSystem`, Linux 는 native syscall probe 전까지 `Unavailable`로 반환한다.
- `tests/Hps.Transport.IoUring.Tests/`:
  새 test project 와 reflection 기반 capability probe/default factory regression tests 를 추가했다.
- `HighPerformanceSocket.slnx`:
  io_uring source/test project 를 solution 에 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 1 완료와 다음 Task 2 `IoUringTransport` lifecycle/unsupported boundary 진입점을 반영했다.

### 검증
- Red: source project 없이 reflection tests 를 먼저 추가해 `Assert.NotNull()` failure 2개를 확인했다.
- Green: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal` 3개 통과.

## 2026-06-29 (Codex - CI artifact remote validation and baseline adoption)

### 작업 단위
- D127/D130 push-triggered `Benchmark Artifacts` run 을 원격에서 검증하고, D095 checklist 를 통과한 artifact 를 두 번째 CI repository baseline 으로 채택했다.

### 변경 내용
- GitHub Actions run `28350456434` 확인:
  remote `master`와 head SHA가 `384f3c5932c1a2b22ff92116068bfcda22f56778`로 일치했고,
  `Build`, `Test`, baseline suite, summary/history/envelope 작성, upload, final hard gate 단계가 모두 success 로 끝났다.
- 다운로드한 artifact `benchmark-artifacts-ci-windows-x64-01-2026-06-29-github-28350456434-1` 확인:
  raw report 6개, `summary.json`, `summary.md`, `history.json`, `history.md`, `envelope.json`, `envelope.md`가 모두 포함됐다.
- `docs/benchmarks/baselines/runners/ci-windows-x64-01/2026-06-29/session-01/`:
  raw report 6개를 복사하고 repository 경로 기준으로 summary JSON/Markdown을 재생성했다.
- `docs/benchmarks/baselines/runners/ci-windows-x64-01/2026-06-29/`와 runner root:
  date-level history 와 runner root history 를 재생성했다.
  runner root history 는 2-session, hard-passed true, warning-count 0, comparison-compatible true 다.
- `docs/benchmarks/baselines/index.md`, `docs/superpowers/specs/2026-06-25-ci-artifact-adoption-policy-design.md`,
  `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서:
  D131과 새 CI baseline 상태를 반영했다.

### 검증
- 원격 run `28350456434`: conclusion success, job success.
- artifact file check: raw report 6개와 expected file 12개 모두 존재.
- D095 adoption checklist: raw metadata, summary hard/warning/comparison, history hard/warning/comparison 조건 통과.
- repository summary 재생성: `source-report-count=6`, `hard-passed=true`, `warning-count=0`.
- date-level history 재생성: `session-count=1`, `hard-passed=true`, `warning-count=0`.
- runner root history 재생성: `session-count=2`, `hard-passed=true`, `warning-count=0`.
- 업로드 artifact envelope 는 `envelope-compatible=false`, `envelope-signal-count=2`를 기록했지만,
  D125/D127 기준 report-only signal 이므로 CI failure 나 채택 차단 조건으로 처리하지 않았다.

## 2026-06-29 (Codex - CI hard gate artifact preservation)

### 작업 단위
- push-triggered `Benchmark Artifacts` run 이 benchmark hard gate 실패 시 raw report 만 남기고 분석 artifact 를 skip 하는 문제를 보정했다.

### 변경 내용
- GitHub Actions run `28349754067` 확인:
  D129 이후 `Test` 단계는 통과했지만, `Run baseline suite` 단계에서 `open-loop-03`이
  sent 3000 / received 2876 / dropped 124 / payload-errors 347로 실패해 exit code 1을 반환했다.
  기존 workflow 는 이 지점에서 summary/history/envelope 작성을 skip 하고 raw report 6개만 upload 했다.
- `.github/workflows/benchmark-artifacts.yml`:
  baseline suite, summary, history, envelope step 의 exit code 를 각각
  `BENCH_BASELINE_EXIT`, `BENCH_SUMMARY_EXIT`, `BENCH_HISTORY_EXIT`, `BENCH_ENVELOPE_EXIT`로 저장하고,
  artifact 작성 단계는 계속 진행하도록 했다.
  upload 이후 `Fail if benchmark hard gate failed` step 이 non-zero exit code 를 최종 job failure 로 복원한다.
- `BenchmarkArtifactWorkflowTests`:
  hard gate 실패 시에도 summary/history/envelope 작성과 upload 가 final failure step 보다 먼저 실행되는지 검증하는
  workflow 정적 회귀 테스트를 추가했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, `CURRENT_PLAN.md`, `TODOS.md`:
  D130 결정과 다음 원격 검증 지점을 반영했다.

### 검증
- Red: 새 workflow 테스트가 final failure step 부재로 `Assert.True()` 실패했다.
- Green: `BenchmarkArtifactWorkflowTests` 2개 통과.
- 실패 partial artifact 재현:
  run `28349754067` raw report 6개로 summary 는 `hard-passed=false`, `warning-count=5`, exit 1 상태에서 파일을 생성했다.
  date-root 구조에서 history 도 `hard-passed=false`, `warning-count=5`, exit 1 상태에서 파일을 생성했다.
  envelope 는 `envelope-compatible=false`, `envelope-signal-count=8`, exit 0 상태로 생성됐다.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj -v minimal`: 104개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal`: 전체 380개 통과/실패 0.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.

## 2026-06-29 (Codex - RIO send probe CI hardening)

### 작업 단위
- push-triggered `Benchmark Artifacts` run 실패 원인을 확인하고, hosted Windows 에서 취약한 RIO native send probe 순서를 보정했다.

### 변경 내용
- GitHub Actions run `28347437734` 확인:
  push trigger 와 head SHA 는 `e1822cf398918701057f135aa0c3b79aa4a465b3`로 일치했지만,
  artifact 단계 전 `Test` 단계에서 `RioCapabilityProbeTests.Send_WhenPosted_CompletesAndPeerReceivesByte`가
  RIO completion timeout 으로 실패했다.
- `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`:
  `RIOSend` post 뒤 peer receive 를 먼저 수행하고, 그 다음 RIO send completion 을 검증하도록 순서를 보정했다.
  RIO function table/probe 검증은 유지하고, peer 가 읽기 전 send completion 이 먼저 와야 한다는 불필요한 순서 제약만 제거했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D129로 RIO native send probe ordering 정책을 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  원격 CI 실패 원인, 로컬 보정 상태, 남은 push 후 artifact 검증 지점을 기록했다.

### 검증
- Red evidence: 원격 `Benchmark Artifacts` run `28347437734`의 `Test` 단계 실패.
- Focused build-included test:
  `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --filter "FullyQualifiedName~RioCapabilityProbeTests.Send_WhenPosted_CompletesAndPeerReceivesByte" -v minimal` 통과.
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj -v minimal`: 57개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal`: 전체 379개 통과/실패 0.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- 후속 원격 검증을 위해 `git push origin master`를 다시 시도했지만, 현재 실행 정책에서 `git push`가 거부됐다.
  현재 로컬 커밋이 원격 push 된 뒤 새 `Benchmark Artifacts` run 으로 artifact 포함 여부를 다시 확인한다.

## 2026-06-29 (Codex - phase4 next candidate after D127)

### 작업 단위
- D127 이후 최신 review/backlog 를 현재 상태와 대조하고 다음 실행 후보를 다시 확정했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-29-phase4-next-candidate-after-d127.md`:
  `.claude/review/2026-06-29-next-scope-decision-review.md`의 local runner 2-date-root 전제가 D123 이후 stale 임을 기록하고,
  D128로 다음 후보를 push-triggered CI artifact 검증으로 정했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D128을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 D127 workflow 의 원격 CI artifact 검증으로 갱신했다.

### 검증
- 최신 review, D123~D127 결정, TODO deferred backlog 를 대조했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal`: 전체 379개 통과/실패 0.
- 후속 원격 검증을 위해 `git push origin master`를 시도했지만, 현재 실행 정책에서 `git push`가 거부됐다.
  원격 push 이후 `Benchmark Artifacts` run 이 생성되면 D128 검증을 이어간다.

## 2026-06-29 (Codex - CI envelope comparison artifact)

### 작업 단위
- D125 envelope comparison command 를 CI benchmark artifact-only workflow 에 report-only 산출물로 연결했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-29-ci-envelope-comparison-artifact-design.md`:
  reference history 존재 시 CI artifact date root 에 `envelope.json`/`envelope.md`를 포함하는 정책을 D127로 정리했다.
- `.github/workflows/benchmark-artifacts.yml`:
  summary/history 생성 뒤 repository reference history 를 확인하고,
  존재하면 `--compare-baseline-envelope`를 실행해 envelope JSON/Markdown을 업로드 대상 date root 에 생성한다.
- `BenchmarkArtifactWorkflowTests`:
  workflow 가 history 생성 뒤 upload 전에 envelope comparison step 을 갖고,
  reference history guard 와 output path 를 유지하는지 검증한다.
- `docs/superpowers/specs/2026-06-29-runner-profile-warning-envelope-model-design.md`:
  D126 번호가 SDK 결정으로 사용됐으므로, gate 승격 표현을 특정 번호가 아닌 후속 gate 결정으로 보정했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, `docs/benchmarks/baselines/index.md`,
  `CURRENT_PLAN.md`, `TODOS.md`:
  D127과 완료/후속 상태를 반영했다.

### 검증
- Red: `BenchmarkArtifactWorkflowTests`가 기존 workflow 의 envelope step 부재로 `Assert.True()` 실패했다.
- Green: focused workflow test 1개 통과.
- CLI smoke: 현재 `ci-windows-x64-01` repository baseline 을 reference 로 envelope JSON/Markdown 생성,
  exit code 0, `envelope-compatible=true`, `envelope-signal-count=0`,
  `reference-summary-count=1`, `candidate-summary-count=1`.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal`: 전체 379개 통과/실패 0.

## 2026-06-29 (Codex - SDK selection reproducibility)

### 작업 단위
- envelope command self-review 뒤 발견한 기본 SDK 10.0.203 build failure 를 재현성 인프라 문제로 분리하고,
  repository 기본 SDK 선택을 net9.0 프로젝트에 맞게 고정했다.

### 변경 내용
- `global.json`:
  SDK `9.0.314`, `rollForward: latestFeature`를 추가해 저장소 루트 기본 `dotnet`이 10.0 계열로 넘어가지 않게 했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D126을 추가했다. SDK pin 과 stale restore 산출물 재생성 필요성을 함께 기록했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  SDK hardening 완료와 다음 실행 지점인 최신 review/backlog 재평가를 반영했다.

### 검증
- `dotnet --version`: 9.0.314.
- `dotnet restore HighPerformanceSocket.slnx --ignore-failed-sources -v minimal`: 성공.
  이후 `tests/Hps.Benchmarks/obj/project.assets.json`의 package root 가 현재 사용자 NuGet cache 로 정렬됨을 확인했다.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal`: 전체 378개 통과/실패 0.
- `git diff --check`: 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.

## 2026-06-29 (Codex - runner/profile envelope schema self-review)

### 작업 단위
- runner/profile scoped envelope comparison command Task 1~4 구현을 D125 설계와 대조하고,
  발견한 JSON schema drift 를 바로 보정했다.

### 변경 내용
- `docs/agent-state/reviews/2026-06-29-envelope-command-self-review.md`:
  self-review 결과와 Major schema finding, SDK 선택 재현성 후속 항목을 기록했다.
- `BaselineEnvelopeComparison`:
  candidate source kind 와 reference/candidate summary count 를 보존한다.
- `BaselineEnvelopeComparisonWriter`:
  D125 schema 에 맞춰 `reference-history-path`, `candidate-path`, `candidate-kind`,
  `reference-summary-count`, `candidate-summary-count`, `envelope-mismatches`, signal `code`를 쓴다.
- `BaselineEnvelopeComparisonWriterTests`, `BaselineEnvelopeProgramTests`:
  D125 top-level field 와 signal code 를 직접 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  self-review 완료, schema 보정, 다음 후보인 SDK 선택 재현성 hardening 을 반영했다.

### 검증
- Red: writer schema test 가 기존 `reference-history-path` 누락으로 `KeyNotFoundException` 실패했다.
- Green: `BaselineEnvelopeComparisonWriterTests` 4개 통과.
- Green: `BaselineEnvelopeProgramTests` 2개 통과.
- Green: envelope 관련 tests 16개 통과.
- CLI smoke: local runner artifact 기준 envelope JSON/Markdown 생성, exit code 0,
  `candidate-kind: summary`, `reference-summary-count: 9`, `candidate-summary-count: 1`,
  `envelope-mismatches` count 0 확인.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- .NET 9.0.314 MSBuild 기준 solution build 통과.
  NuGet vulnerability feed 조회 불가로 `NU1900` 경고 1건이 출력됐지만 컴파일 오류는 없다.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과, 전체 378개 통과/실패 0.

## 2026-06-29 (Codex - runner/profile envelope writers and CLI)

### 작업 단위
- runner/profile scoped envelope comparison command Task 4 writer/Program wiring 을 TDD로 구현했다.

### 변경 내용
- `BaselineEnvelopeComparisonWriter`:
  `envelope-version: 1` JSON artifact 를 쓰고, reference/candidate key, kind별 metric row,
  mismatch, signal array 를 기록한다.
- `BaselineEnvelopeComparisonMarkdownWriter`:
  같은 comparison model 을 사람이 읽는 Markdown summary, metric table, mismatch/signal table 로 쓴다.
- `Program`:
  `--compare-baseline-envelope <candidate-json> --reference-history <reference-history-json> --envelope <output-json> [--envelope-md <output-md>]`
  branch 를 reader/generator/writer 경로로 연결했다.
  D125 기준으로 signal/mismatch 여부와 무관하게 artifact 생성 성공 시 exit code 0을 반환한다.
- `BaselineEnvelopeComparisonWriterTests`, `BaselineEnvelopeProgramTests`:
  JSON/Markdown writer shape 와 실제 CLI artifact 생성/exit policy 를 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 4 완료와 다음 실행 지점인 envelope command self-review/Phase 4 후보 재평가를 반영했다.

### 검증
- Red: writer type 부재 `Assert.NotNull()` failure 2건을 확인했다.
- Red: writer stub 상태에서 JSON/Markdown behavior tests 2개가 `NotSupportedException`으로 실패했다.
- Red: Program switch 미연결 상태에서 CLI tests 2개가 exit code 2로 실패했다.
- Green: `BaselineEnvelopeComparisonWriterTests` 4개 통과.
- Green: `BaselineEnvelopeProgramTests` 2개 통과.
- Green: `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineEnvelope`:
  16개 통과.
- CLI smoke: local runner artifact 기준 `--compare-baseline-envelope ... --envelope ... --envelope-md ...` exit code 0,
  JSON/Markdown 생성, `envelope-compatible: true`, `envelope-signal-count: 0`.
  `dotnet run` 중 NuGet vulnerability feed 조회 경고 `NU1900`이 출력됐지만 command 자체는 성공했다.
- `git diff --check` 통과. CRLF 변환 경고만 있고 whitespace 오류는 없다.
- `dotnet exec 'C:\Program Files\dotnet\sdk\9.0.314\MSBuild.dll' HighPerformanceSocket.slnx /t:Build /p:Configuration=Debug /restore:false /v:minimal`
  통과. NuGet vulnerability feed 조회 불가로 `NU1900` 경고 1건이 출력됐지만 컴파일 오류는 없다.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과, 전체 378개 통과/실패 0.
- 참고: `global.json`이 없는 현재 로컬 CLI 기본값은 .NET SDK 10.0.203이고,
  이 SDK로 `dotnet build tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-restore`를 실행하면
  BenchmarkDotNet transitive package metadata 경로에서 `CS0006`가 재현된다.
  같은 restore 산출물은 9.0.314 MSBuild로 통과하므로 Task 4 코드 변경 문제가 아니라 SDK 선택 문제로 분리했다.

## 2026-06-29 (Codex - runner/profile envelope generator)

### 작업 단위
- runner/profile scoped envelope comparison command Task 3 generator 를 TDD로 구현했다.

### 변경 내용
- `BaselineEnvelopeComparison`, `BaselineEnvelopeKindComparison`, `BaselineEnvelopeMetricComparison`,
  `BaselineEnvelopeMismatch`, `BaselineEnvelopeSignal`:
  writer/Program 이 공유할 envelope comparison model 을 추가했다.
- `BaselineEnvelopeComparisonGenerator`:
  reference history/candidate source comparison key gate, eligible reference summary selection,
  kind별 metric row, D125 upper/lower limit, signal/mismatch 계산을 구현했다.
- `BaselineEnvelopeComparisonGeneratorTests`:
  compatible no-signal, runner key mismatch, p99 upper-bound signal, actual-rate lower-bound signal,
  eligible reference 없음 경로를 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 3 완료와 다음 실행 지점인 Task 4 writer/Program wiring 을 반영했다.

### 검증
- Red: generator type 부재로 `Assert.NotNull()` failure 1개를 확인했다.
- Red: stub generator 상태에서 compatible/key/signal/no-reference behavior tests 5개가 실패했다.
- Green: `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineEnvelopeComparisonGeneratorTests`:
  6개 통과.
- `git diff --check`: 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 372개 통과.

## 2026-06-29 (Codex - runner/profile envelope source reader)

### 작업 단위
- runner/profile scoped envelope comparison command Task 2 source reader 를 TDD로 구현했다.

### 변경 내용
- `BaselineEnvelopeSourceKind`, `BaselineEnvelopeSummary`, `BaselineEnvelopeSource`:
  summary/history 입력 artifact 를 generator 가 공통으로 소비할 source model 로 추가했다.
- `BaselineEnvelopeSourceReader`:
  summary.json 은 단일 source 로 읽고, history.json 은 `sessions[].summary-path`를 history file directory 기준으로 다시 열어
  full `by-kind` aggregate 를 보존한다.
- `BaselineComparisonJsonReader`:
  summary/history comparison JSON parsing 을 공유 helper 로 분리했다.
- `BaselineHistoryReader`:
  legacy summary incompatible 처리 의미를 유지하면서 shared comparison helper 를 사용하도록 정리했다.
- `BaselineEnvelopeSourceReaderTests`:
  summary source, history relative summary-path resolution, missing summary-path failure 를 검증한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 2 완료와 다음 실행 지점인 Task 3 generator 를 반영했다.

### 검증
- Red: source reader contract type 부재로 `Assert.NotNull()` failure 1개를 확인했다.
- Red: reader stub 상태에서 behavior tests 3개가 `NotSupportedException`/expected `InvalidOperationException` mismatch 로 실패했다.
- Green: `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineEnvelopeSourceReaderTests`:
  4개 통과.
- Green: `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineHistoryReaderTests`:
  7개 통과.
- `git diff --check`: 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 366개 통과.

## 2026-06-29 (Codex - runner/profile envelope parser)

### 작업 단위
- runner/profile scoped envelope comparison command Task 1 parser contract 를 TDD로 구현했다.

### 변경 내용
- `BenchmarkCommand`:
  `CompareBaselineEnvelope` command 값을 추가했다.
- `BenchmarkCommandLine`:
  candidate summary/history path, reference history path, envelope JSON path, 선택 Markdown path 를 보존한다.
- `BenchmarkCommandParser`:
  `--compare-baseline-envelope <candidate-json> --reference-history <reference-history-json> --envelope <output-json> [--envelope-md <output-md>]`
  command shape 를 parse 한다.
  `--report`, `--backend`, `--protocol`은 envelope comparison 과 함께 쓰면 usage error 로 막는다.
- `Program`:
  usage text 에 envelope comparison command 를 추가했다.
  실제 execution branch 는 Task 4 범위로 남겼다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  Task 1 완료와 다음 실행 지점인 Task 2 source reader 를 반영했다.

### 검증
- Red: `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BenchmarkCommandParserTests.TryParse_WhenCompareEnvelope`
  실행 시 7개 테스트가 기존 parser 에서 `parsed=false` 또는 `Command=None`으로 실패했다.
- Green: 같은 compare envelope parser tests 7개 통과.
- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BenchmarkCommandParserTests`:
  29개 통과.
- `git diff --check`: 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 362개 통과.

## 2026-06-29 (Codex - runner/profile envelope comparison plan)

### 작업 단위
- D125 설계를 구현 가능한 TDD task 로 분해했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-29-runner-profile-envelope-comparison.md`:
  envelope comparison command 구현을 parser contract, source reader, generator, writer/Program wiring 의 4개 task 로 나눴다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  구현 계획 완료와 다음 실행 지점인 Task 1 parser contract 를 반영했다.

### 검증
- D125 spec coverage, file structure, command shape, task 경계, placeholder/type consistency self-review 를 수행했다.
- `git diff --check`: 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 355개 통과.

## 2026-06-29 (Codex - runner/profile warning envelope model)

### 작업 단위
- D124 이후 runner/profile/workload scoped warning envelope model 을 설계했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-29-runner-profile-warning-envelope-model-design.md`:
  기존 `warning-count`를 유지하고 별도 envelope comparison artifact 를 두는 설계를 작성했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D125를 추가했다. runner/profile scoped 판단은 기존 summary warning 이 아니라
  reference history 와 candidate summary/history 를 비교하는 별도 artifact 로 분리한다.
- `docs/benchmarks/baselines/index.md`:
  D125의 report-only envelope comparison 해석과 다음 갱신 규칙을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  설계 완료와 다음 실행 지점인 envelope comparison command 구현 계획을 반영했다.

### 검증
- `BaselineSummaryGenerator`의 warning threshold 가 전역 상수이며 기존 summary/history `warning-count`에 합산됨을 확인했다.
- `history.json`은 full metric aggregate 가 아니라 session `summary-path`를 보존하므로,
  reference envelope 는 history 가 가리키는 summary 들에서 계산해야 함을 확인했다.
- D080/D090/D096/D123/D124와 D125가 충돌하지 않는지 대조했다.
- placeholder scan: 신규 spec/state 문서 매칭 없음.
- `git diff --check`: 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 355개 통과.

## 2026-06-29 (Codex - gate promotion policy after local 3-date roots)

### 작업 단위
- `local-win-x64-01` 3-date-root/9-session evidence 이후 warning-as-failure, latency hard gate,
  CI latency gate 를 지금 승격할 수 있는지 재평가했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-29-phase4-gate-promotion-policy-after-local-3-date-roots.md`:
  local 9-session evidence, CI 1-session 상태, 현재 global warning threshold 구조를 대조하고
  gate 승격 정책을 정리했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D124를 추가했다. `local-win-x64-01` envelope 는 runner-local reference 로 채택하지만,
  warning/gate 승격은 runner-scoped threshold 설계 뒤로 분리한다.
- `docs/benchmarks/baselines/index.md`:
  local explicit runner envelope 설명을 D124 기준의 수동 리뷰 기준으로 보정했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 runner/profile scoped warning envelope model 설계로 옮겼다.

### 검증
- local runner root history: session-count 9, hard-passed true, warning-count 0, comparison-compatible true.
- CI runner root history: session-count 1, hard-passed true, warning-count 0, comparison-compatible true.
- `BaselineSummaryGenerator`의 warning threshold 가 runner/profile scoped 가 아닌 전역 상수임을 확인했다.
- `git diff --check`: 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 355개 통과.

## 2026-06-29 (Codex - local explicit runner third date root)

### 작업 단위
- 검토 의견을 현재 D090-D096 상태와 대조한 뒤, stale 한 D090 skeleton 후보 대신
  `local-win-x64-01` explicit runner 의 세 번째 baseline date root 를 수집했다.

### 변경 내용
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-29/session-01..03/`:
  TCP loopback SAEA 4096B x 100Hz baseline-suite raw report, `summary.json`, `summary.md`를 추가했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-29/history.json`,
  `history.md`: 2026-06-29 date-level history 를 추가했다.
- `docs/benchmarks/baselines/runners/local-win-x64-01/history.json`,
  `history.md`: runner root history 를 9-session 으로 갱신했다.
- `docs/benchmarks/baselines/index.md`:
  2026-06-29 date-level row, session rows, 9-session explicit runner envelope 를 반영했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D123을 추가했다. D082의 explicit runner 3-date-root evidence 조건은 충족했지만,
  warning-as-failure/CI latency gate 승격은 별도 정책 재평가로 분리한다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  이번 baseline 수집 완료와 다음 실행점인 warning/gate promotion policy 재평가를 반영했다.

### 검증
- `--baseline-suite docs\benchmarks\baselines\runners\local-win-x64-01\2026-06-29\session-01 --runs 3`:
  raw report 6개 생성, `baseline-suite-result: pass`.
- `--baseline-suite docs\benchmarks\baselines\runners\local-win-x64-01\2026-06-29\session-02 --runs 3`:
  raw report 6개 생성, `baseline-suite-result: pass`.
- `--baseline-suite docs\benchmarks\baselines\runners\local-win-x64-01\2026-06-29\session-03 --runs 3`:
  raw report 6개 생성, `baseline-suite-result: pass`.
- 세 session `--summarize-baseline`: 각각 `source-report-count: 6`, `hard-passed: true`, `warning-count: 0`.
- 2026-06-29 date-level `--summarize-baseline-history`: session-count 3, hard-passed true, warning-count 0, comparison-compatible true.
- runner root `--summarize-baseline-history`: session-count 9, hard-passed true, warning-count 0, comparison-compatible true.
- 새 baseline artifact 절대 경로 검색: 매칭 없음.
- `git diff --check`: 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 355개 통과.

## 2026-06-29 (Codex - RIO address-family-aware selection)

### 작업 단위
- D122에 따라 RIO backend 의 현재 IPv4-only 지원 범위를 TCP public boundary 와 sample broker host selection 에 반영했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-29-rio-address-family-aware-selection-policy-design.md`:
  RIO backend 를 TCP/UDP IPv4 `IPEndPoint` 전용 opt-in 으로 두고,
  host `auto`가 IPv6/non-IPv4 endpoint 에서 SAEA fallback 을 수행하는 정책을 D122로 설계했다.
- `docs/superpowers/plans/2026-06-29-rio-address-family-aware-selection.md`:
  TCP guard, sample selector, state/verification 작업을 한 구현 계획으로 정리했다.
- `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`,
  `src/Hps.Transport.Rio/RioTransport.cs`:
  RIO TCP listen/connect 가 IPv6 endpoint 를 socket layer 전에 명시적 `NotSupportedException`으로 거부하도록 했다.
- `tests/Hps.Sample.BrokerServer.Tests/SampleTransportSelectorTests.cs`,
  `samples/Hps.Sample.BrokerServer/SampleTransportSelector.cs`,
  `samples/Hps.Sample.BrokerServer/Program.cs`:
  sample broker selector 에 listen `AddressFamily` 입력을 추가했다.
  `auto`는 IPv6/non-IPv4 listen endpoint 에서 SAEA fallback notice 를 반환하고,
  explicit `rio`는 runtime failure 를 반환한다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, `CURRENT_PLAN.md`, `TODOS.md`:
  D122와 완료/후속 상태를 반영했다.

### 검증
- Red: RIO TCP IPv6 listen 은 기존 구현에서 `SocketException`으로 실패했다.
- Red: RIO TCP IPv6 connect 는 기존 구현에서 `IPv4` 없는 protocol error message 로 실패했다.
- Red: sample selector IPv6 tests 는 새 address-family-aware overload 부재 `Assert.NotNull()` failure 로 실패했다.
- Green: focused RIO TCP guard tests 2개 통과.
- Green: focused sample selector tests 2개 통과.
- `dotnet test tests\Hps.Sample.BrokerServer.Tests\Hps.Sample.BrokerServer.Tests.csproj --no-restore`: 17개 통과.
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 57개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 355개 통과.
- `git diff --check`: 통과.

## 2026-06-26 (Codex - rio udp ipv6 unsupported guard)

### 작업 단위
- D121에 따라 RIO UDP v1의 IPv4-only 정책을 public boundary 에서 강제했다.

### 변경 내용
- `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`:
  IPv6 local bind 가 명시적 `NotSupportedException`으로 실패하는지,
  IPv6 remote send 가 enqueue 없이 `false`를 반환하는지 검증했다.
- `src/Hps.Transport.Rio/RioTransport.cs`:
  UDP endpoint address-family guard helper 를 추가했다.
  `BindUdpAsync(...)`는 IPv6 local endpoint 를 socket bind 전에 거부하고,
  `TrySendTo(...)`는 IPv6 remote endpoint 를 pending queue 에 넣지 않고 `false`로 반환한다.
- `docs/superpowers/plans/2026-06-26-rio-udp-ipv6-unsupported-guard.md`:
  Task 1 실행 체크박스를 완료로 갱신했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  guard 구현 완료와 남은 deferred backlog 를 반영했다.

### 검증
- Red: IPv6 local bind test 는 기존 구현에서 `SocketException`으로 실패했다.
- Red: IPv6 remote send test 는 기존 구현에서 `TrySendTo == true`로 실패했다.
- Green: focused guard tests 2개 통과.
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 55개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 351개 통과.
- `git diff --check`: 통과.

## 2026-06-26 (Codex - rio udp ipv6 support gate design)

### 작업 단위
- RIO UDP IPv6 지원 여부를 default promotion gate 관점에서 설계하고, 다음 unsupported guard 구현 계획을 작성했다.

### 변경 내용
- `docs/superpowers/specs/2026-06-26-rio-udp-ipv6-support-gate-design.md`:
  RIO UDP v1을 IPv4-only opt-in backend 로 유지하고 IPv6는 default promotion gate 로 남긴다는 D121 설계를 작성했다.
- `docs/superpowers/plans/2026-06-26-rio-udp-ipv6-unsupported-guard.md`:
  IPv6 local bind explicit unsupported, IPv6 remote send synchronous `false` reject, no-enqueue diagnostics 검증을
  한 작업 단위의 TDD 계획으로 정리했다.
- `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`:
  D121을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  다음 실행 지점을 D121 unsupported boundary guard 구현으로 옮겼다.

### 검증
- `RioNative.CreateUdpSocket()`이 `AF_INET` registered UDP socket 으로 고정된 것을 확인했다.
- `RioTransport`의 `EncodeSockaddrInet`/`DecodeSockaddrInet`가 IPv4 `IPEndPoint`만 지원하는 것을 확인했다.
- SAEA UDP가 endpoint `AddressFamily`로 socket 과 receive remote placeholder 를 만드는 것을 대조했다.
- D109/D110/D118/D119 결정과 충돌하지 않는지 확인했다.

## 2026-06-26 (Codex - sample broker transport selector self-review)

### 작업 단위
- D120 sample broker transport selector 구현을 self-review 하고 minor hardening 2건을 TDD로 보정했다.

### 변경 내용
- `docs/agent-state/reviews/2026-06-26-sample-broker-transport-selector-self-review.md`:
  D120 설계 대비 구현 coverage, findings, verification, deferred items 를 정리했다.
- `tests/Hps.Sample.BrokerServer.Tests/SampleBrokerServerCommandParserTests.cs`:
  invalid port 와 invalid max-frame-bytes 가 구체적인 parser error message 를 반환하는지 검증했다.
- `samples/Hps.Sample.BrokerServer/SampleBrokerServerCommandParser.cs`:
  `MessagePortInvalid`, `MessageMaxFrameBytesInvalid`를 추가해 Program validation 책임 이동 후에도
  기존 sample 의 구체적인 입력 오류 피드백을 유지한다.
- `tests/Hps.Sample.BrokerServer.Tests/SampleTransportSelectorTests.cs`,
  `samples/Hps.Sample.BrokerServer/SampleTransportSelector.cs`:
  정의되지 않은 `SampleTransportMode` 값이 `auto` fallback 으로 조용히 처리되지 않도록
  `ArgumentOutOfRangeException` guard 를 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  self-review 완료를 기록하고 다음 실행 지점을 RIO UDP IPv6 지원 여부 결정 설계로 옮겼다.

### 검증
- Red: invalid port/max-frame parser tests 2개가 `Assert.Equal()` failure 로 실패했다. Actual은 `null`.
- Red: undefined enum selector test 1개가 `Assert.Throws()` failure 로 실패했다.
- Green: focused parser/selector tests 13개 통과.
- Green: `dotnet test tests\Hps.Sample.BrokerServer.Tests\Hps.Sample.BrokerServer.Tests.csproj --no-restore` 통과, 15개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 349개 통과.
- `git diff --check`: 통과.

## 2026-06-26 (Codex - sample broker transport program wiring)

### 작업 단위
- sample broker transport selector Task 3 Program wiring/smoke 를 TDD로 구현했다.

### 변경 내용
- `tests/Hps.Sample.BrokerServer.Tests/SampleBrokerServerProgramTests.cs`:
  invalid `--transport` usage path 가 exit code 2와 transport usage text 를 반환하는지 검증했다.
- `samples/Hps.Sample.BrokerServer/Program.cs`:
  `SampleBrokerServerCommandParser`와 `SampleTransportSelector`를 사용해 transport 를 생성한다.
  startup output 에 selected backend 를 표시하고, usage 에 `[--transport <saea|rio|auto>]`를 추가했다.
- `docs/superpowers/plans/2026-06-26-sample-broker-transport-selector.md`,
  `CURRENT_PLAN.md`, `TODOS.md`:
  Task 3 완료와 다음 self-review 진입점을 반영했다.

### 검증
- Red: focused Program tests 2개가 기존 usage output 에 `--transport <saea|rio|auto>`가 없어 `Assert.Contains()` failure 로 실패했다.
- Green: focused Program tests 2개 통과.
- Focused sample tests 12개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 346개 통과.
- `git diff --check`: 통과.

## 2026-06-26 (Codex - sample broker transport selector policy)

### 작업 단위
- sample broker transport selector Task 2 selection policy 를 TDD로 구현했다.

### 변경 내용
- `samples/Hps.Sample.BrokerServer/Hps.Sample.BrokerServer.csproj`:
  RIO capability status 와 후속 Program wiring 을 위해 `Hps.Transport.Rio` project reference 를 추가했다.
- `tests/Hps.Sample.BrokerServer.Tests/Hps.Sample.BrokerServer.Tests.csproj`:
  selector tests 에서 `RioCapabilityStatus`를 직접 쓰기 위해 `Hps.Transport.Rio` project reference 를 추가했다.
- `tests/Hps.Sample.BrokerServer.Tests/SampleTransportSelectorTests.cs`:
  `saea`, explicit `rio`, `auto` selection policy 를 capability status 별로 검증했다.
- `samples/Hps.Sample.BrokerServer/SampleTransportSelection.cs`,
  `SampleTransportSelector.cs`:
  sample host composition 경계의 transport selection result 와 selector 를 추가했다.
- `docs/superpowers/plans/2026-06-26-sample-broker-transport-selector.md`,
  `CURRENT_PLAN.md`, `TODOS.md`:
  Task 2 완료와 다음 Task 3 Program wiring 진입점을 반영했다.

### 검증
- Red: 최초 selector tests 는 `RioCapabilityStatus` reference 누락 컴파일 오류를 보정한 뒤,
  selector type 부재 `Assert.NotNull()` failure 로 실패했다.
- Green: focused selector tests 5개 통과.
- Refactor: tests 를 reflection bootstrap 에서 direct public API 호출로 정리한 뒤 focused selector tests 5개,
  focused sample tests 10개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 344개 통과.
- `git diff --check`: 통과.

## 2026-06-26 (Codex - sample broker transport parser)

### 작업 단위
- sample broker transport selector Task 1 parser/model 을 TDD로 구현했다.

### 변경 내용
- `HighPerformanceSocket.slnx`:
  `tests/Hps.Sample.BrokerServer.Tests` project 를 추가했다.
- `tests/Hps.Sample.BrokerServer.Tests/SampleBrokerServerCommandParserTests.cs`:
  기존 3 positional args 호환, `--transport rio`, `--transport auto`,
  transport value 누락/unknown value 를 검증했다.
- `samples/Hps.Sample.BrokerServer/SampleTransportMode.cs`,
  `SampleBrokerServerCommandLine.cs`, `SampleBrokerServerCommandParser.cs`:
  sample broker host 전용 parser/model 을 추가했다.
- `docs/superpowers/plans/2026-06-26-sample-broker-transport-selector.md`,
  `CURRENT_PLAN.md`, `TODOS.md`:
  Task 1 완료와 다음 Task 2 selection policy 진입점을 반영했다.

### 검증
- Red: focused parser tests 5개가 parser type 부재 `Assert.NotNull()` failure 로 실패했다.
- Green: focused parser tests 5개 통과.
- Refactor: tests 를 reflection bootstrap 에서 direct public API 호출로 정리한 뒤 focused parser tests 5개 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0개, 오류 0개.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 339개 통과.
- `git diff --check`: 통과.

## 2026-06-26 (Codex - sample broker transport selector plan)

### 작업 단위
- D120 설계를 구현 가능한 Task 로 분리했다.

### 변경 내용
- `docs/superpowers/plans/2026-06-26-sample-broker-transport-selector.md`:
  sample broker transport selector 구현을 Task 1 parser/model, Task 2 selector policy,
  Task 3 Program wiring/smoke 검증으로 나눴다.
- `CURRENT_PLAN.md`, `TODOS.md`:
  구현 계획 완료를 기록하고 다음 실행 지점을 Task 1 parser/model TDD 구현으로 옮겼다.

### 검증
- D120 spec requirement 를 plan task 에 매핑했다.
- placeholder scan 과 type consistency self-review 를 수행했다.
- public command line property 와 enum 접근성, `TransportBase` fake abstract method, async helper `out` parameter 제약을 계획 단계에서 보정했다.

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
