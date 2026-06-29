# Runner/Profile Envelope Comparison Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` when implementing this plan task-by-task. This repository currently allows continuing without extra user checkpoints unless a material question appears, but each coherent task still ends with its own focused verification and commit.

**Goal:** D125 설계에 따라 reference history 와 candidate summary/history 를 비교하는 `--compare-baseline-envelope` command 와 JSON/Markdown artifact 를 추가한다.

**Architecture:** 기존 summary/history command 는 그대로 두고, envelope comparison 을 별도 command 와 별도 artifact 로 둔다. Reader 는 `summary.json` 또는 `history.json`을 envelope source 로 읽고, history 의 session `summary-path`를 다시 열어 full metric aggregate 를 만든다. Generator 는 D080 `comparison-key`가 같은 경우에만 kind별 reference/limit/candidate metric 을 계산하고, signal 은 기존 `warning-count`와 분리한다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, `System.Text.Json`, 기존 `tests/Hps.Benchmarks` benchmark artifact pipeline.

## Global Constraints

- C# 8.0 문법만 사용한다. file-scoped namespace, record, target-typed `new()`는 쓰지 않는다.
- 모든 새 테스트에는 무엇을 검증하는지 한국어 주석을 둔다.
- Red-Green-Refactor 를 지킨다. 컴파일 실패 Red 대신 reflection 또는 기존 public surface 를 활용한 assertion failure Red 를 우선한다.
- 기존 `summary-version`과 `history-version`은 1로 유지한다. 새 envelope artifact 는 `envelope-version: 1`을 사용한다.
- 기존 `warning-count`, `hard-passed`, summary/history command exit code 는 변경하지 않는다.
- envelope signal 은 `envelope-signal-count`로만 기록하며 기존 warning 에 합산하지 않는다.
- `--compare-baseline-envelope`는 artifact 생성 성공 시 signal/mismatch 여부와 무관하게 exit code 0을 반환한다.
- JSON parse/schema/write/usage 오류는 exit code 2를 반환한다.
- CI failure, warning-as-failure, latency hard gate 는 이번 계획에서 구현하지 않는다.

---

## File Structure

- `tests/Hps.Benchmarks/BenchmarkCommand.cs`
  - `CompareBaselineEnvelope` command 값을 추가한다.
- `tests/Hps.Benchmarks/BenchmarkCommandLine.cs`
  - candidate/reference/output path 를 보존한다.
- `tests/Hps.Benchmarks/BenchmarkCommandParser.cs`
  - `--compare-baseline-envelope <candidate-json> --reference-history <reference-history-json> --envelope <output-json> [--envelope-md <output-md>]`를 parse 한다.
- `tests/Hps.Benchmarks/BaselineComparisonJsonReader.cs`
  - 기존 summary/history comparison JSON field 를 공유 reader 로 읽는다.
- `tests/Hps.Benchmarks/BaselineEnvelopeSourceKind.cs`
  - `Summary`, `History` source kind 를 구분한다.
- `tests/Hps.Benchmarks/BaselineEnvelopeSummary.cs`
  - summary 1개의 path, hard/warning state, comparison, by-kind aggregate 를 보존한다.
- `tests/Hps.Benchmarks/BaselineEnvelopeSource.cs`
  - candidate/reference 입력 artifact 하나와 그 안의 summary 목록을 보존한다.
- `tests/Hps.Benchmarks/BaselineEnvelopeSourceReader.cs`
  - summary/history JSON 을 읽고 history 의 relative summary path 를 history file directory 기준으로 해석한다.
- `tests/Hps.Benchmarks/BaselineEnvelopeComparison*.cs`
  - comparison result, kind row, metric row, mismatch, signal model 을 둔다.
- `tests/Hps.Benchmarks/BaselineEnvelopeComparisonGenerator.cs`
  - D125 limit 계산과 metric comparison 을 담당한다.
- `tests/Hps.Benchmarks/BaselineEnvelopeComparisonWriter.cs`
  - envelope JSON artifact 를 쓴다.
- `tests/Hps.Benchmarks/BaselineEnvelopeComparisonMarkdownWriter.cs`
  - envelope Markdown 보조 artifact 를 쓴다.
- `tests/Hps.Benchmarks/Program.cs`
  - command 실행 wiring 과 usage text 를 추가한다.
- `tests/Hps.Benchmarks.Tests/*`
  - parser, reader, generator, writer, Program smoke tests 를 추가한다.
- Root state docs
  - 각 Task 완료마다 `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`를 갱신한다.

---

## Task 1: Parser Contract

**Files:**
- Modify: `tests/Hps.Benchmarks/BenchmarkCommand.cs`
- Modify: `tests/Hps.Benchmarks/BenchmarkCommandLine.cs`
- Modify: `tests/Hps.Benchmarks/BenchmarkCommandParser.cs`
- Modify: `tests/Hps.Benchmarks/Program.cs`
- Modify: `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Produced interfaces:**
- `BenchmarkCommand.CompareBaselineEnvelope`
- `BenchmarkCommandLine.EnvelopeCandidatePath`
- `BenchmarkCommandLine.EnvelopeReferenceHistoryPath`
- `BenchmarkCommandLine.EnvelopeOutputPath`
- `BenchmarkCommandLine.EnvelopeMarkdownOutputPath`
- parser messages:
  - `MessageEnvelopeCandidateRequired`
  - `MessageEnvelopeReferenceHistoryRequired`
  - `MessageEnvelopeOutputRequired`
  - `MessageEnvelopeMarkdownOutputRequired`
  - `MessageEnvelopeExecutionOptionNotAllowed`

- [ ] **Step 1: Write failing parser tests**

Add to `BenchmarkCommandParserTests`:

```csharp
// envelope comparison 은 summary/history 생성과 분리된 별도 artifact command 다.
// parser 가 candidate, reference history, output path 를 보존해야 이후 단계가 기존 summary command 를 오염시키지 않는다.
[Fact]
public void TryParse_WhenCompareEnvelopeHasCandidateReferenceAndOutput_ReturnsEnvelopeCommand()
{
    BenchmarkCommandLine commandLine;
    string? errorMessage;

    bool parsed = BenchmarkCommandParser.TryParse(
        new[]
        {
            "--compare-baseline-envelope",
            "candidate/summary.json",
            "--reference-history",
            "reference/history.json",
            "--envelope",
            "out/envelope.json"
        },
        out commandLine,
        out errorMessage);

    Assert.True(parsed);
    Assert.Null(errorMessage);
    Assert.Equal("CompareBaselineEnvelope", commandLine.Command.ToString());
    Assert.Equal("candidate/summary.json", GetStringProperty(commandLine, "EnvelopeCandidatePath"));
    Assert.Equal("reference/history.json", GetStringProperty(commandLine, "EnvelopeReferenceHistoryPath"));
    Assert.Equal("out/envelope.json", GetStringProperty(commandLine, "EnvelopeOutputPath"));
    Assert.Null(GetStringProperty(commandLine, "EnvelopeMarkdownOutputPath"));
}
```

```csharp
// Markdown envelope 는 JSON envelope 를 대체하지 않는 보조 artifact 다.
// 두 output path 를 분리해 보존해야 Program 이 같은 comparison model 로 두 파일을 쓸 수 있다.
[Fact]
public void TryParse_WhenCompareEnvelopeHasMarkdown_ReturnsEnvelopeCommandWithMarkdownPath()
```

```csharp
// reference history 가 없으면 runner/profile scoped 비교 기준이 없다.
// 이 상태를 허용하면 candidate 를 전역 상수와 비교하는 과거 문제로 되돌아간다.
[Fact]
public void TryParse_WhenCompareEnvelopeMissingReferenceHistory_ReturnsUsageError()
```

```csharp
// envelope JSON output 은 canonical machine-readable artifact 이므로 반드시 필요하다.
// output path 없이 통과시키면 command 가 성공했는데 아무 기준 artifact 도 남지 않는다.
[Fact]
public void TryParse_WhenCompareEnvelopeMissingEnvelopeOutput_ReturnsUsageError()
```

```csharp
// --report/--backend/--protocol 은 실행 runner option 이고 envelope comparison 과 섞이면 의미가 충돌한다.
// parser 단계에서 막아야 candidate artifact 와 새 실행 workload 를 혼동하지 않는다.
[Theory]
[InlineData("--report", "out/run.json")]
[InlineData("--backend", "rio")]
[InlineData("--protocol", "udp")]
public void TryParse_WhenCompareEnvelopeHasExecutionOption_ReturnsUsageError(string option, string value)
```

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BenchmarkCommandParserTests.TryParse_WhenCompareEnvelope
```

Expected Red: command/properties do not exist, so reflection assertions fail.

- [ ] **Step 2: Implement parser surface**

Implementation notes:

- Add enum value after `SummarizeBaselineHistory`.
- Extend `BenchmarkCommandLine` constructor with four nullable envelope path properties.
- Existing constructor call sites pass `null` for new envelope paths.
- Parser shape is strict:

```text
--compare-baseline-envelope <candidate-json> --reference-history <reference-history-json> --envelope <output-json> [--envelope-md <output-md>]
```

- Reject `--report`, `--backend`, `--protocol`.
- Add usage line in `Program.PrintUsage`.
- Do not add `Program.Main` execution switch in this task. The parser contract is the deliverable.

- [ ] **Step 3: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BenchmarkCommandParserTests
```

Expected: parser tests pass.

- [ ] **Step 4: Verify and commit**

Run:

```powershell
git diff --check
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
```

Commit:

```powershell
git add tests\Hps.Benchmarks\BenchmarkCommand.cs tests\Hps.Benchmarks\BenchmarkCommandLine.cs tests\Hps.Benchmarks\BenchmarkCommandParser.cs tests\Hps.Benchmarks\Program.cs tests\Hps.Benchmarks.Tests\BenchmarkCommandParserTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: parse baseline envelope command"
```

---

## Task 2: Envelope Source Reader

**Files:**
- Create: `tests/Hps.Benchmarks/BaselineComparisonJsonReader.cs`
- Create: `tests/Hps.Benchmarks/BaselineEnvelopeSourceKind.cs`
- Create: `tests/Hps.Benchmarks/BaselineEnvelopeSummary.cs`
- Create: `tests/Hps.Benchmarks/BaselineEnvelopeSource.cs`
- Create: `tests/Hps.Benchmarks/BaselineEnvelopeSourceReader.cs`
- Modify: `tests/Hps.Benchmarks/BaselineHistoryReader.cs`
- Create: `tests/Hps.Benchmarks.Tests/BaselineEnvelopeSourceReaderTests.cs`
- Modify: `tests/Hps.Benchmarks.Tests/BaselineHistoryReaderTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Produced interfaces:**
- `BaselineEnvelopeSourceReader.Read(string path)`
- `BaselineEnvelopeSource.Kind`
- `BaselineEnvelopeSource.Summaries`
- shared `BaselineComparisonJsonReader.Read(...)`

- [ ] **Step 1: Write failing reader contract test**

Create `BaselineEnvelopeSourceReaderTests`:

```csharp
// envelope source reader 는 새 command 의 input boundary 다.
// 타입이 없으면 reference history 와 candidate summary/history 를 같은 model 로 다룰 수 없다.
[Fact]
public void Contract_BaselineEnvelopeSourceReaderExists()
{
    Assert.NotNull(typeof(BenchmarkCommandParser).Assembly.GetType("Hps.Benchmarks.BaselineEnvelopeSourceReader"));
}
```

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineEnvelopeSourceReaderTests.Contract_BaselineEnvelopeSourceReaderExists
```

Expected Red: type is missing.

- [ ] **Step 2: Add source model stubs**

Add source types and a stub reader that throws `NotSupportedException`.
Then replace the contract test with behavior tests.

- [ ] **Step 3: Add failing behavior tests**

Add tests:

```csharp
// summary input 은 candidate 로 자주 쓰는 최소 단위다.
// reader 가 by-kind aggregate 와 comparison key 를 보존해야 generator 가 raw report 를 다시 열지 않는다.
[Fact]
public void Read_WhenPathIsSummary_ReadsSingleSummarySource()
```

Assertions:
- `Kind == Summary`
- `Summaries.Count == 1`
- `summary.HardPassed == true`
- `summary.Comparison.Compatible == true`
- `summary.Load!.P99Max == 900.0`
- `summary.OpenLoop!.TcpHighWatermarkMax == 2`

```csharp
// reference 는 runner root history 를 입력으로 받는다.
// history.json 에는 full metric 이 없으므로 session summary-path 를 history file directory 기준으로 다시 열어야 한다.
[Fact]
public void Read_WhenPathIsHistory_ResolvesSessionSummaryPathsRelativeToHistoryDirectory()
```

Assertions:
- `Kind == History`
- two summaries are read.
- `Summaries[0].SummaryPath` is normalized relative or full path consistently.
- load/open-loop metrics come from each referenced summary.

```csharp
// history summary-path 가 깨졌으면 envelope 비교는 진행할 수 없다.
// 조용히 summary 를 건너뛰면 reference envelope 가 실제보다 느슨하거나 좁아진다.
[Fact]
public void Read_WhenHistoryReferencesMissingSummary_ThrowsInvalidOperationException()
```

```csharp
// 기존 BaselineHistoryReader 도 comparison JSON parsing 을 공유 helper 로 유지해야 한다.
// helper 추출이 legacy summary incompatible 처리 의미를 바꾸지 않는지 확인한다.
[Fact]
public void HistoryReader_WhenSummaryHasNoComparison_StillMarksLegacySummaryIncompatible()
```

- [ ] **Step 4: Implement shared comparison JSON reader**

Extract the comparison parsing currently inside `BaselineHistoryReader` into `BaselineComparisonJsonReader`.

Required API:

```csharp
internal static class BaselineComparisonJsonReader
{
    public static BaselineComparisonResult Read(
        JsonElement root,
        string? defaultSession,
        string? defaultSummaryPath,
        string legacyMissingCode);
}
```

Rules:
- Missing `comparison-compatible` creates incompatible result with code passed as `legacyMissingCode`.
- `comparison-key: null` returns `Key == null`.
- Optional mismatch fields `source-path`, `session`, `summary-path` are preserved, defaulting to supplied values.
- Existing `BaselineHistoryReader` behavior remains unchanged.

- [ ] **Step 5: Implement envelope source reader**

Reader rules:
- If root has `summary-version`, read one `BaselineEnvelopeSummary`.
- If root has `history-version`, read `sessions[].summary-path` and open each summary relative to `Path.GetDirectoryName(historyPath)`.
- History source top-level `Comparison` is read from the history root.
- Summary source top-level `Comparison` is the summary comparison.
- Unsupported version or missing kind throws `InvalidOperationException`.
- `by-kind.load` and `by-kind.open-loop` may be null; preserve null.

- [ ] **Step 6: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineEnvelopeSourceReaderTests
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineHistoryReaderTests
```

Expected: source reader and existing history reader tests pass.

- [ ] **Step 7: Verify and commit**

Run standard verification and commit:

```powershell
git diff --check
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
git add tests\Hps.Benchmarks\BaselineComparisonJsonReader.cs tests\Hps.Benchmarks\BaselineEnvelopeSourceKind.cs tests\Hps.Benchmarks\BaselineEnvelopeSummary.cs tests\Hps.Benchmarks\BaselineEnvelopeSource.cs tests\Hps.Benchmarks\BaselineEnvelopeSourceReader.cs tests\Hps.Benchmarks\BaselineHistoryReader.cs tests\Hps.Benchmarks.Tests\BaselineEnvelopeSourceReaderTests.cs tests\Hps.Benchmarks.Tests\BaselineHistoryReaderTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: read baseline envelope sources"
```

---

## Task 3: Envelope Generator

**Files:**
- Create: `tests/Hps.Benchmarks/BaselineEnvelopeComparison.cs`
- Create: `tests/Hps.Benchmarks/BaselineEnvelopeKindComparison.cs`
- Create: `tests/Hps.Benchmarks/BaselineEnvelopeMetricComparison.cs`
- Create: `tests/Hps.Benchmarks/BaselineEnvelopeMismatch.cs`
- Create: `tests/Hps.Benchmarks/BaselineEnvelopeSignal.cs`
- Create: `tests/Hps.Benchmarks/BaselineEnvelopeComparisonGenerator.cs`
- Create: `tests/Hps.Benchmarks.Tests/BaselineEnvelopeComparisonGeneratorTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Produced interfaces:**
- `BaselineEnvelopeComparisonGenerator.Generate(BaselineEnvelopeSource reference, BaselineEnvelopeSource candidate)`
- `BaselineEnvelopeComparison.EnvelopeCompatible`
- `BaselineEnvelopeComparison.SignalCount`
- kind/metric rows with `reference`, `limit`, `candidate`, `direction`, `signaled`

- [ ] **Step 1: Write failing generator contract test**

```csharp
// generator 는 D125의 핵심 정책 위치다.
// writer 나 Program 에서 metric limit 을 계산하면 JSON/Markdown/CLI가 서로 다른 기준을 가질 수 있다.
[Fact]
public void Contract_BaselineEnvelopeComparisonGeneratorExists()
{
    Assert.NotNull(typeof(BenchmarkCommandParser).Assembly.GetType("Hps.Benchmarks.BaselineEnvelopeComparisonGenerator"));
}
```

Expected Red: type missing.

- [ ] **Step 2: Add model stubs**

Add model classes and a generator stub returning incompatible/no-source result.
Then add behavior tests.

- [ ] **Step 3: Add failing behavior tests**

```csharp
// 같은 comparison key 이고 candidate 가 reference 완충 limit 안에 있으면 signal 이 없어야 한다.
// 이 경로가 정상 baseline review 의 noise-free 기본값이다.
[Fact]
public void Generate_WhenCandidateIsInsideReferenceEnvelope_ReturnsCompatibleWithoutSignals()
```

Assert:
- `EnvelopeCompatible == true`
- `SignalCount == 0`
- load `p99-max-us` reference `935.6`, limit `1122.72`, candidate below limit.
- open-loop rows exist.

```csharp
// runner id 가 다르면 metric 값이 좋아도 같은 envelope 로 비교하지 않는다.
// 이 mismatch 는 warning-count 가 아니라 envelope mismatch 로만 남는다.
[Fact]
public void Generate_WhenCandidateKeyDiffers_ReturnsMismatchWithoutMetricSignals()
```

Assert:
- `EnvelopeCompatible == false`
- mismatch code `envelope-key-mismatch`
- field `runner-id`
- `SignalCount == 0`

```csharp
// upper-bound metric 이 limit 을 넘으면 envelope signal 로 기록한다.
// 이 signal 은 process failure 가 아니라 review artifact 로만 남는다.
[Fact]
public void Generate_WhenCandidateP99ExceedsLimit_AddsUpperBoundSignal()
```

```csharp
// actual rate 는 높을수록 좋은 lower-bound metric 이다.
// reference min 에서 1Hz 완충을 둔 limit 아래로 내려가면 signal 을 남긴다.
[Fact]
public void Generate_WhenCandidateActualRateFallsBelowLimit_AddsLowerBoundSignal()
```

```csharp
// reference history 에 compatible hard-passed summary 가 없으면 기준 envelope 를 만들 수 없다.
// 빈 기준을 0으로 계산하면 모든 candidate 가 regression 으로 보이므로 명시 mismatch 로 중단한다.
[Fact]
public void Generate_WhenReferenceHasNoEligibleSummaries_ReturnsNoReferenceMismatch()
```

- [ ] **Step 4: Implement D125 generator rules**

Rules:
- Reference source must be `History`; otherwise mismatch `reference-not-history`.
- Reference top-level comparison must be compatible and have key.
- Candidate source comparison must be compatible and have key.
- Compare reference/candidate `BaselineComparisonKey` field-by-field including cases.
- Eligible reference summaries: `HardPassed == true`, `Comparison.Compatible == true`, key equal to reference key.
- Candidate summaries are aggregated only after key compatibility is established.
- Metric directions and limits:
  - p50/p99/p99 median upper: `Math.Max(reference * 1.20, reference + 100.0)`
  - p99 growth ratio upper: `reference + 0.25`
  - actual rate lower: `Math.Max(95.0, reference - 1.0)`
  - TCP HWM upper: `reference + 2`
  - dropped total, payload error total, pool rented max upper: `0`
- Missing candidate kind creates mismatch `candidate-kind-missing`.
- Missing reference kind creates mismatch `reference-kind-missing`.

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineEnvelopeComparisonGeneratorTests
```

Expected: generator tests pass.

- [ ] **Step 6: Verify and commit**

Run standard verification and commit:

```powershell
git diff --check
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
git add tests\Hps.Benchmarks\BaselineEnvelopeComparison.cs tests\Hps.Benchmarks\BaselineEnvelopeKindComparison.cs tests\Hps.Benchmarks\BaselineEnvelopeMetricComparison.cs tests\Hps.Benchmarks\BaselineEnvelopeMismatch.cs tests\Hps.Benchmarks\BaselineEnvelopeSignal.cs tests\Hps.Benchmarks\BaselineEnvelopeComparisonGenerator.cs tests\Hps.Benchmarks.Tests\BaselineEnvelopeComparisonGeneratorTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: compute baseline envelope comparison"
```

---

## Task 4: Envelope Writers And Program Wiring

**Files:**
- Create: `tests/Hps.Benchmarks/BaselineEnvelopeComparisonWriter.cs`
- Create: `tests/Hps.Benchmarks/BaselineEnvelopeComparisonMarkdownWriter.cs`
- Modify: `tests/Hps.Benchmarks/Program.cs`
- Create: `tests/Hps.Benchmarks.Tests/BaselineEnvelopeComparisonWriterTests.cs`
- Create: `tests/Hps.Benchmarks.Tests/BaselineEnvelopeProgramTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Produced output:**
- `envelope-version: 1` JSON artifact
- optional Markdown artifact
- runnable CLI:
  `--compare-baseline-envelope <candidate-json> --reference-history <reference-history-json> --envelope <output-json> [--envelope-md <output-md>]`

- [ ] **Step 1: Write failing writer tests**

```csharp
// envelope JSON 은 자동화가 읽을 canonical artifact 다.
// signal-count 와 metric row 가 없으면 warning-count 와 분리된 review signal 을 기계적으로 추적할 수 없다.
[Fact]
public void Write_WhenComparisonHasMetrics_WritesEnvelopeJsonShape()
```

Assert:
- `envelope-version == 1`
- `envelope-compatible`
- `envelope-signal-count`
- `reference-key`
- `candidate-key`
- `by-kind.load.p99-max-us.reference/limit/candidate/signaled`
- `signals` array

```csharp
// Markdown 은 사람이 regression signal 을 빠르게 읽는 보조 artifact 다.
// JSON 과 같은 comparison model 을 써야 수동 리뷰와 자동화가 다른 값을 보지 않는다.
[Fact]
public void MarkdownWriter_WhenComparisonHasSignals_WritesMetricAndSignalSections()
```

- [ ] **Step 2: Implement JSON/Markdown writers**

JSON rules:
- top-level fields from D125 schema.
- write keys as `null` when absent.
- write metric rows with `direction`, `reference`, `limit`, `candidate`, `signaled`.
- write mismatch entries and signal entries as arrays.

Markdown minimum:
- `# Baseline Envelope Comparison`
- reference/candidate paths
- compatible and signal count
- comparison key summary
- metric table
- mismatch table or `없음`
- signal table or `없음`

- [ ] **Step 3: Write failing Program tests**

```csharp
// Program wiring 은 parser, reader, generator, writer 를 실제 CLI 경로로 묶는다.
// envelope signal 이 없어도 JSON/Markdown artifact 가 생성되어야 한다.
[Fact]
public void Main_WhenEnvelopeCommandHasCompatibleCandidate_WritesArtifactsAndReturnsSuccess()
```

```csharp
// envelope signal 은 D125 기준 process failure 가 아니다.
// candidate p99 가 limit 을 넘어도 command 는 artifact 를 남기고 exit code 0을 유지해야 한다.
[Fact]
public void Main_WhenEnvelopeCommandHasSignals_ReturnsSuccessAndWritesSignalCount()
```

Test setup:
- Create temp reference root with `history.json` and referenced `summary.json`.
- Create temp candidate `summary.json`.
- Use same comparison key for compatible test.
- Raise candidate p99 for signal test.

Expected Red: no Program switch branch/writers.

- [ ] **Step 4: Wire Program execution**

Add switch branch:

```csharp
case BenchmarkCommand.CompareBaselineEnvelope:
    return CompleteBaselineEnvelope(
        commandLine.EnvelopeCandidatePath!,
        commandLine.EnvelopeReferenceHistoryPath!,
        commandLine.EnvelopeOutputPath!,
        commandLine.EnvelopeMarkdownOutputPath);
```

Add method:
- read reference and candidate via `BaselineEnvelopeSourceReader`.
- generate comparison.
- write JSON.
- optionally write Markdown.
- print:
  - `baseline-envelope: <path>`
  - `baseline-envelope-md: <path>`
  - `envelope-compatible: true|false`
  - `envelope-signal-count: N`
- return `SuccessExitCode` on successful generation, regardless of signal/mismatch.
- catch exception, print `baseline-envelope-error: ...`, return `ReportWriteFailedExitCode`.

- [ ] **Step 5: Run focused tests and CLI smoke**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineEnvelope
```

Optional local smoke against committed local runner artifacts:

```powershell
$envJson = Join-Path $env:TEMP "hps-baseline-envelope.json"
$envMd = Join-Path $env:TEMP "hps-baseline-envelope.md"
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --compare-baseline-envelope docs\benchmarks\baselines\runners\local-win-x64-01\2026-06-29\session-03\summary.json --reference-history docs\benchmarks\baselines\runners\local-win-x64-01\history.json --envelope $envJson --envelope-md $envMd
```

Expected:
- exit code 0
- envelope JSON and Markdown are created under `%TEMP%`
- no generated temp artifact is staged.

- [ ] **Step 6: Final verification and commit**

Run:

```powershell
git diff --check
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
```

Commit:

```powershell
git add tests\Hps.Benchmarks\BaselineEnvelopeComparisonWriter.cs tests\Hps.Benchmarks\BaselineEnvelopeComparisonMarkdownWriter.cs tests\Hps.Benchmarks\Program.cs tests\Hps.Benchmarks.Tests\BaselineEnvelopeComparisonWriterTests.cs tests\Hps.Benchmarks.Tests\BaselineEnvelopeProgramTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: write baseline envelope comparison"
```

---

## Self-Review

- Spec coverage: D125 command shape, separate artifact, reference history + summary-path reuse, comparison-key gating, signal limits, non-failing exit policy, JSON/Markdown output are all mapped to tasks.
- Scope control: no change to `BaselineSummaryGenerator` threshold constants, no warning-as-failure, no CI hard gate, no raw/summary/history schema version change.
- Type consistency: Task 1 path properties are consumed by Task 4 Program wiring; Task 2 source model is consumed by Task 3 generator; Task 3 comparison model is consumed by Task 4 writers.
- Placeholder scan: no unresolved placeholder text remains. All paths, method names, command names, and verification commands are concrete.
- Reviewability: parser, reader, generator, writer/Program are separate commits with focused tests and state doc updates.

