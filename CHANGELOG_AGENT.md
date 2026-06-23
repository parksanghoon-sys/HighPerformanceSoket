# CHANGELOG_AGENT.md

## Archive

긴 변경 이력 원문은 `docs/agent-state/changelog/2026-06.md`에 보존했다.
이 파일은 최근 작업 단위와 현재 진입점에 필요한 내용만 유지한다.

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
