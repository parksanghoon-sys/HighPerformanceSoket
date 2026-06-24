# Summary/history Comparison Signal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` when implementing this plan task-by-task. This repository still follows the local rule of one small reviewed work unit and one commit at a time.

**Goal:** D080 설계에 따라 `summary.json`과 `history.json`에 비교 가능성 신호를 추가한다. 이 신호는 hard gate, 기존 `warning-count`, CLI exit code 에 영향을 주지 않는 non-failing artifact 로 유지한다.

**Architecture:** raw run report 의 D079 metadata 를 `BaselineReport`까지 올리고, summary 단계에서 같은 runner/profile/case 구성인지 계산한다. history 단계는 각 session summary 의 comparison signal 을 다시 읽어 session 간 비교 가능성을 집계한다. JSON 이 canonical output 이고 Markdown 은 리뷰 보조 output 이다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, `System.Text.Json`, 기존 `tests/Hps.Benchmarks` benchmark artifact pipeline.

## Global Constraints

- 모든 구현은 Red-Green-Refactor 순서로 진행한다. 새 production code 는 먼저 실패하는 assertion 기반 test 가 있어야 한다.
- 각 Task 는 별도 커밋으로 끝낸다. 한 번에 여러 Task 를 섞지 않는다.
- 새 테스트에는 무엇을 검증하는지 한국어 주석을 남긴다. 특히 comparison signal 이 hard gate 와 분리된다는 의도를 테스트 주석에 적는다.
- `summary-version`과 `history-version`은 1로 유지한다. 새 field 는 additive top-level field 로만 추가한다.
- comparison mismatch 는 `warning-count`에 합산하지 않는다.
- `processor-count`는 diagnostic 으로만 유지하고 compatibility key 에 넣지 않는다.
- legacy raw report 또는 legacy summary 는 compatible 로 추정하지 않는다.
- `load`와 `open-loop`은 같은 summary 안에서도 서로 다른 `scenario`를 가질 수 있으므로 comparison key 는 단일 scenario 가 아니라 `cases` 배열로 표현한다.
- C# 8.0 문법만 사용한다. file-scoped namespace, record, target-typed `new()`를 쓰지 않는다.

## File Structure

- `tests/Hps.Benchmarks/BaselineReport.cs`
  - raw report 의 payload/target 설정을 summary input model 로 보존한다.
- `tests/Hps.Benchmarks/BaselineReportReader.cs`
  - `payload-bytes`, `target-rate-hz`, `target-duration-seconds`를 읽는다.
- `tests/Hps.Benchmarks/BaselineComparisonCase.cs`
  - `result-name`별 scenario/payload/target case 를 보존한다.
- `tests/Hps.Benchmarks/BaselineComparisonKey.cs`
  - runner/environment key 와 case 목록을 보존한다.
- `tests/Hps.Benchmarks/BaselineComparisonMismatch.cs`
  - summary/history mismatch 를 machine-readable entry 로 보존한다.
- `tests/Hps.Benchmarks/BaselineComparisonResult.cs`
  - compatible 여부, key, unknown count, mismatch 목록을 묶는다.
- `tests/Hps.Benchmarks/BaselineSummary.cs`
  - `BaselineComparisonResult Comparison`을 보존한다.
- `tests/Hps.Benchmarks/BaselineSummaryGenerator.cs`
  - source report 목록에서 summary comparison signal 을 계산한다.
- `tests/Hps.Benchmarks/BaselineSummaryWriter.cs`
  - summary JSON comparison field 를 출력한다.
- `tests/Hps.Benchmarks/BaselineSummaryMarkdownWriter.cs`
  - summary Markdown comparison section 을 출력한다.
- `tests/Hps.Benchmarks/BaselineHistorySession.cs`
  - session summary 에서 읽은 comparison signal 을 보존한다.
- `tests/Hps.Benchmarks/BaselineHistory.cs`
  - history aggregate comparison signal 을 보존한다.
- `tests/Hps.Benchmarks/BaselineHistoryReader.cs`
  - summary JSON comparison field 를 optional 로 읽고 legacy summary 를 incompatible 로 변환한다.
- `tests/Hps.Benchmarks/BaselineHistoryGenerator.cs`
  - session 간 comparison key 일치 여부를 계산한다.
- `tests/Hps.Benchmarks/BaselineHistoryWriter.cs`
  - history JSON comparison field 를 출력한다.
- `tests/Hps.Benchmarks/BaselineHistoryMarkdownWriter.cs`
  - history Markdown comparison section 을 출력한다.
- `tests/Hps.Benchmarks.Tests/*`
  - 각 단계의 Red/Green tests 를 추가한다.
- Root state docs
  - `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`를 각 Task 완료마다 갱신한다.

## Data Contracts

### BaselineReport 확장

`BaselineReport` 생성자는 기존 `scenario` 뒤에 다음 값을 받는다.

```csharp
int payloadBytes,
double targetRateHz,
int targetDurationSeconds,
```

노출 property 는 다음과 같다.

```csharp
public int PayloadBytes { get; }
public double TargetRateHz { get; }
public int TargetDurationSeconds { get; }
```

`BaselineReportReader`는 raw report field 를 그대로 읽는다.

```csharp
GetInt(root, "payload-bytes")
GetDouble(root, "target-rate-hz")
GetInt(root, "target-duration-seconds")
```

### Comparison model

초기 구현 model 은 모두 `internal sealed`로 둔다.

```csharp
internal sealed class BaselineComparisonCase
{
    public BaselineComparisonCase(
        string resultName,
        string scenario,
        int payloadBytes,
        double targetRateHz,
        int targetDurationSeconds);

    public string ResultName { get; }
    public string Scenario { get; }
    public int PayloadBytes { get; }
    public double TargetRateHz { get; }
    public int TargetDurationSeconds { get; }
}
```

```csharp
internal sealed class BaselineComparisonKey
{
    public BaselineComparisonKey(
        string benchmarkProfile,
        string runnerId,
        string runnerKind,
        string transportBackend,
        string osDescription,
        string osArchitecture,
        string processArchitecture,
        string frameworkDescription,
        IReadOnlyList<BaselineComparisonCase> cases);

    public string BenchmarkProfile { get; }
    public string RunnerId { get; }
    public string RunnerKind { get; }
    public string TransportBackend { get; }
    public string OsDescription { get; }
    public string OsArchitecture { get; }
    public string ProcessArchitecture { get; }
    public string FrameworkDescription { get; }
    public IReadOnlyList<BaselineComparisonCase> Cases { get; }
}
```

```csharp
internal sealed class BaselineComparisonMismatch
{
    public BaselineComparisonMismatch(
        string code,
        string field,
        string expected,
        string actual,
        string? sourcePath,
        string? session,
        string? summaryPath);

    public string Code { get; }
    public string Field { get; }
    public string Expected { get; }
    public string Actual { get; }
    public string? SourcePath { get; }
    public string? Session { get; }
    public string? SummaryPath { get; }
}
```

```csharp
internal sealed class BaselineComparisonResult
{
    public BaselineComparisonResult(
        bool compatible,
        BaselineComparisonKey? key,
        int unknownRunnerCount,
        IReadOnlyList<BaselineComparisonMismatch> mismatches);

    public bool Compatible { get; }
    public BaselineComparisonKey? Key { get; }
    public int UnknownRunnerCount { get; }
    public IReadOnlyList<BaselineComparisonMismatch> Mismatches { get; }
    public int MismatchCount { get; }
}
```

`Comparison.Key`는 trusted key 를 만들 수 없으면 `null`이다. trusted base key 가 있고 일부 report/session 이 mismatch 인 경우에는 base key 를 보존해 사람이 어떤 기준에서 벗어났는지 볼 수 있게 한다.

### Summary JSON 추가 field

```json
{
  "comparison-compatible": true,
  "comparison-key": {
    "benchmark-profile": "tcp-loopback-saea-v1",
    "runner-id": "local-unspecified",
    "runner-kind": "local",
    "transport-backend": "SaeaTransport",
    "os-description": "Microsoft Windows ...",
    "os-architecture": "X64",
    "process-architecture": "X64",
    "framework-description": ".NET 9.0...",
    "cases": [
      {
        "result-name": "load",
        "scenario": "tcp-loopback-saea-baseline",
        "payload-bytes": 4096,
        "target-rate-hz": 100,
        "target-duration-seconds": 30
      }
    ]
  },
  "unknown-runner-count": 0,
  "comparison-mismatch-count": 0,
  "comparison-mismatches": []
}
```

`comparison-key`가 없으면 JSON `null`을 쓴다.

### History JSON 추가 field

Top-level history 에 summary 와 같은 comparison field 를 추가한다. 각 session entry 에도 session summary 에서 읽은 comparison 상태를 최소 field 로 남긴다.

```json
{
  "comparison-compatible": false,
  "comparison-key": null,
  "comparison-mismatch-count": 1,
  "comparison-mismatches": [
    {
      "code": "legacy-summary-without-comparison",
      "session": "session-01(root)",
      "summary-path": "2026-06-18/summary.json",
      "field": "comparison-compatible",
      "expected": "present",
      "actual": "missing"
    }
  ],
  "sessions": [
    {
      "comparison-compatible": false,
      "comparison-mismatch-count": 1
    }
  ]
}
```

## Task 1: BaselineReport Payload/Target Settings

**Files:**
- Modify: `tests/Hps.Benchmarks/BaselineReport.cs`
- Modify: `tests/Hps.Benchmarks/BaselineReportReader.cs`
- Modify: `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`
- Modify existing helper call sites in benchmark tests
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Produced interfaces:**
- `BaselineReport.PayloadBytes`
- `BaselineReport.TargetRateHz`
- `BaselineReport.TargetDurationSeconds`
- `BaselineReportReader` raw field parsing for the three values

- [ ] **Step 1: Write failing contract and reader tests**

Add tests to `BaselineReportReaderWriterTests`:

```csharp
// comparison key 는 payload size 와 target rate/duration 없이는 같은 부하 조건인지 판단할 수 없다.
// BaselineReport 가 이 값을 노출해야 summary 단계가 raw JSON을 다시 열지 않고 comparison signal 을 계산할 수 있다.
[Fact]
public void Contract_BaselineReportExposesPayloadAndTargetSettings()
{
    Assert.NotNull(typeof(BaselineReport).GetProperty("PayloadBytes"));
    Assert.NotNull(typeof(BaselineReport).GetProperty("TargetRateHz"));
    Assert.NotNull(typeof(BaselineReport).GetProperty("TargetDurationSeconds"));
}
```

```csharp
// raw report writer 는 이미 payload/target field 를 기록한다.
// reader 가 값을 버리면 이후 summary comparison key 가 모든 run 을 같은 조건으로 오판할 수 있다.
[Fact]
public void ReadDirectory_WhenRunReportHasPayloadAndTarget_ReadsSettings()
{
    string directory = CreateTempDirectory();
    WriteRunJson(Path.Combine(directory, "load-01.json"), "load", 500.0, 1, 0, 3000);

    BaselineReport report = BaselineReportReader.ReadDirectory(directory).Single();

    Assert.Equal(4096, report.PayloadBytes);
    Assert.Equal(100.0, report.TargetRateHz);
    Assert.Equal(30, report.TargetDurationSeconds);
}
```

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineReportReaderWriterTests.Contract_BaselineReportExposesPayloadAndTargetSettings
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineReportReaderWriterTests.ReadDirectory_WhenRunReportHasPayloadAndTarget_ReadsSettings
```

Expected Red:
- contract test fails because properties do not exist.
- reader test fails or cannot compile only after direct property use is added. If direct property compile failure blocks Red, keep the first Red as reflection assertion failure, then add properties and run the behavior Red.

- [ ] **Step 2: Implement the minimum model/reader change**

Modify `BaselineReport` constructor to accept the three values after `scenario` and assign properties. Modify `BaselineReportReader.TryReadReport(...)` to pass the raw JSON values.

Existing test helpers that construct `BaselineReport` directly should pass the current default benchmark settings:

```csharp
4096,
100.0,
30,
```

- [ ] **Step 3: Verify focused tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineReportReaderWriterTests
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineSummary
```

Expected: focused benchmark tests pass.

- [ ] **Step 4: Update state docs and commit**

Record Red/Green evidence and next Task 2 entry point.

Commit:

```powershell
git add tests\Hps.Benchmarks\BaselineReport.cs tests\Hps.Benchmarks\BaselineReportReader.cs tests\Hps.Benchmarks.Tests\BaselineReportReaderWriterTests.cs tests\Hps.Benchmarks.Tests\BaselineSummaryGeneratorTests.cs tests\Hps.Benchmarks.Tests\BaselineSummaryMarkdownWriterTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: read baseline comparison run settings"
```

## Task 2: Summary Comparison Model And Generator

**Files:**
- Create: `tests/Hps.Benchmarks/BaselineComparisonCase.cs`
- Create: `tests/Hps.Benchmarks/BaselineComparisonKey.cs`
- Create: `tests/Hps.Benchmarks/BaselineComparisonMismatch.cs`
- Create: `tests/Hps.Benchmarks/BaselineComparisonResult.cs`
- Modify: `tests/Hps.Benchmarks/BaselineSummary.cs`
- Modify: `tests/Hps.Benchmarks/BaselineSummaryGenerator.cs`
- Modify: `tests/Hps.Benchmarks.Tests/BaselineSummaryGeneratorTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Produced interfaces:**
- `BaselineSummary.Comparison`
- `BaselineSummaryGenerator` comparison calculation
- mismatch codes:
  - `no-source-reports`
  - `unknown-runner`
  - `comparison-key-mismatch`

- [ ] **Step 1: Write failing summary comparison contract test**

Add to `BaselineSummaryGeneratorTests`:

```csharp
// summary comparison signal 은 hard gate 와 별도의 artifact 품질 신호다.
// Summary model 이 comparison result 를 보존하지 않으면 writer/history 단계가 같은 계산을 중복 구현하게 된다.
[Fact]
public void Contract_BaselineSummaryExposesComparison()
{
    Assert.NotNull(typeof(BaselineSummary).GetProperty("Comparison"));
}
```

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineSummaryGeneratorTests.Contract_BaselineSummaryExposesComparison
```

Expected Red: `Assert.NotNull()` failure.

- [ ] **Step 2: Add model stubs and summary property**

Add the four model files and `BaselineSummary.Comparison`. For the first Green, `BaselineSummaryGenerator.Generate(...)` may return `new BaselineComparisonResult(false, null, 0, emptyList)` so the contract test passes.

- [ ] **Step 3: Write behavior tests**

Add tests:

```csharp
// 같은 runner 와 같은 result-name별 case 구성이면 summary 는 comparison-compatible 이어야 한다.
// load/open-loop scenario 는 서로 달라도 각각 별도 case 로 보존되어 정상 summary 를 mismatch 로 만들지 않는다.
[Fact]
public void Generate_WhenReportsShareRunnerAndEachKindHasStableCase_MarksComparisonCompatible()
```

Assertions:
- `summary.Comparison.Compatible == true`
- `summary.Comparison.UnknownRunnerCount == 0`
- `summary.Comparison.MismatchCount == 0`
- `summary.Comparison.Key!.RunnerId == "runner-a"`
- `summary.Comparison.Key.Cases.Count == 2`
- `load` case scenario and `open-loop` case scenario can differ.

```csharp
// legacy raw report 는 모든 metadata 가 unknown 으로 같아 보여도 비교 가능하다고 증명된 것이 아니다.
// unknown-runner-count 와 mismatch 를 남겨 history 단계가 legacy artifact 를 compatible 로 오판하지 않게 한다.
[Fact]
public void Generate_WhenReportIdentityIsUnknown_MarksComparisonIncompatible()
```

Assertions:
- `Compatible == false`
- `UnknownRunnerCount == 1`
- mismatch code `unknown-runner`
- existing `HardPassed` can remain true when delivery/drop/leak gate passed.

```csharp
// 일부 metadata field 만 unknown 인 raw report 도 같은 runner 라고 증명할 수 없다.
// runner-id 같은 hard key field 하나라도 unknown 이면 compatible 로 묶지 않고 unknown-runner 로 격리한다.
[Fact]
public void Generate_WhenIdentityHasPartialUnknownField_MarksComparisonIncompatible()
```

Assertions:
- `Compatible == false`
- `Key == null`
- `UnknownRunnerCount == 1`
- mismatch code `unknown-runner`

```csharp
// runner id 가 섞인 summary 는 같은 부하 수치라도 같은 비교군이 아니다.
// 이 mismatch 는 warning-count 에 합산하지 않고 comparison mismatch 로만 남긴다.
[Fact]
public void Generate_WhenRunnerIdentityDiffers_RecordsComparisonMismatchWithoutWarning()
```

Assertions:
- `Compatible == false`
- `summary.WarningCount == 0`
- mismatch code `comparison-key-mismatch`
- field `runner-id`

```csharp
// source report 가 하나도 없으면 hard-passed=false 인 기존 정책과 함께 comparison 도 incompatible 이어야 한다.
// 빈 summary 를 비교 가능한 baseline 으로 쓰면 이후 history trend 가 의미 없는 기준을 만들 수 있다.
[Fact]
public void Generate_WhenNoReports_MarksComparisonIncompatible()
```

Assertions:
- `Compatible == false`
- mismatch code `no-source-reports`

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineSummaryGeneratorTests.Generate_When
```

Expected Red: behavior tests fail because generator returns stub comparison.

- [ ] **Step 4: Implement comparison generation**

Implementation rules:
- Build base key from the first non-unknown report.
- Treat the report as `unknown-runner` if any hard comparison identity field equals `unknown`
  (`BenchmarkProfile`, `RunnerId`, `RunnerKind`, `TransportBackend`, `OsDescription`,
  `OsArchitecture`, `ProcessArchitecture`, `FrameworkDescription`).
  `ProcessorCount` remains diagnostic-only and does not affect compatibility.
- Compare identity fields except `ProcessorCount`.
- Group cases by `ResultName` case-insensitively.
- Within the same result-name group, compare `Scenario`, `PayloadBytes`, `TargetRateHz`, `TargetDurationSeconds`.
- Canonical case order is ordinal-ignore-case sorted `ResultName`.
- Add mismatch entries with `SourcePath` for report-level mismatches.
- Do not add comparison mismatches to `warnings`.

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineSummaryGeneratorTests
```

Expected: all summary generator tests pass.

- [ ] **Step 6: Update state docs and commit**

Commit:

```powershell
git add tests\Hps.Benchmarks\BaselineComparisonCase.cs tests\Hps.Benchmarks\BaselineComparisonKey.cs tests\Hps.Benchmarks\BaselineComparisonMismatch.cs tests\Hps.Benchmarks\BaselineComparisonResult.cs tests\Hps.Benchmarks\BaselineSummary.cs tests\Hps.Benchmarks\BaselineSummaryGenerator.cs tests\Hps.Benchmarks.Tests\BaselineSummaryGeneratorTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: compute baseline summary comparison"
```

## Task 3: Summary JSON And Markdown Output

**Files:**
- Modify: `tests/Hps.Benchmarks/BaselineSummaryWriter.cs`
- Modify: `tests/Hps.Benchmarks/BaselineSummaryMarkdownWriter.cs`
- Modify: `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`
- Modify: `tests/Hps.Benchmarks.Tests/BaselineSummaryMarkdownWriterTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Produced output:**
- summary JSON fields:
  - `comparison-compatible`
  - `comparison-key`
  - `unknown-runner-count`
  - `comparison-mismatch-count`
  - `comparison-mismatches`
- summary Markdown `## Comparison` section

- [ ] **Step 1: Write failing JSON writer test**

Add to `BaselineReportReaderWriterTests`:

```csharp
// summary JSON 은 downstream script 가 읽는 canonical artifact 다.
// comparison field 가 JSON 에 없으면 사람이 Markdown 을 봐야만 비교 가능성 문제를 알 수 있다.
[Fact]
public void Write_WhenSummaryHasComparison_WritesComparisonFields()
```

Assertions:
- `comparison-compatible` exists.
- `comparison-key.cases[0].payload-bytes == 4096`
- `unknown-runner-count` exists.
- `comparison-mismatch-count` exists.
- `comparison-mismatches` exists.

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineReportReaderWriterTests.Write_WhenSummaryHasComparison_WritesComparisonFields
```

Expected Red: missing JSON property.

- [ ] **Step 2: Implement JSON output**

Add private writer helpers in `BaselineSummaryWriter`:
- `WriteComparison(Utf8JsonWriter writer, BaselineComparisonResult comparison)`
- `WriteComparisonKey(...)`
- `WriteComparisonCase(...)`
- `WriteComparisonMismatches(...)`

Rules:
- write `comparison-key: null` when `Comparison.Key == null`.
- summary mismatch entries write `source-path` only when present.
- history-only `session` and `summary-path` fields are not written by summary writer unless non-null.

- [ ] **Step 3: Write failing Markdown test**

Add to `BaselineSummaryMarkdownWriterTests`:

```csharp
// Markdown 은 리뷰 보조 artifact 이므로 comparison 여부와 기준 key 를 사람이 바로 볼 수 있어야 한다.
// JSON 값과 달라지지 않도록 같은 BaselineSummary.Comparison 에서 출력한다.
[Fact]
public void Write_WhenSummaryHasComparison_WritesComparisonSection()
```

Assertions:
- contains `## Comparison`
- contains `compatible: true`
- contains runner id/kind
- contains case row for `load`

```csharp
// 실제 legacy baseline artifact 처럼 identity metadata 가 없는 summary 는 comparison key 를 만들 수 없다.
// 이 경로가 NRE 없이 Markdown 에 `comparison-key: 없음`과 unknown-runner 원인을 남기는지 고정한다.
[Fact]
public void Write_WhenComparisonKeyIsNull_WritesNullKeyAndUnknownRunnerMismatch()
```

Assertions:
- contains `compatible: false`
- contains `unknown-runner-count: 1`
- contains `comparison-key: 없음`
- contains mismatch code `unknown-runner`

Run focused Markdown writer test and expect missing section failure.

- [ ] **Step 4: Implement Markdown section**

Add `WriteComparison(...)` in `BaselineSummaryMarkdownWriter`.

Output minimum:
- `## Comparison`
- `- compatible: true|false`
- when `Comparison.Key == null`, print `- comparison-key: 없음` and do not dereference the key.
- when key exists, print `- runner-id: ...`, `- runner-kind: ...`, and the case table:
  `| result | scenario | payload bytes | target rate hz | target duration seconds |`
- mismatch table when `MismatchCount > 0`; otherwise `- mismatch: 없음`

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineReportReaderWriterTests.Write_WhenSummaryHasComparison
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineSummaryMarkdownWriterTests
```

Expected: focused JSON/Markdown tests pass.

- [ ] **Step 6: Update state docs and commit**

Commit:

```powershell
git add tests\Hps.Benchmarks\BaselineSummaryWriter.cs tests\Hps.Benchmarks\BaselineSummaryMarkdownWriter.cs tests\Hps.Benchmarks.Tests\BaselineReportReaderWriterTests.cs tests\Hps.Benchmarks.Tests\BaselineSummaryMarkdownWriterTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: write baseline summary comparison"
```

## Task 4: History Reader And Aggregate Comparison

**Files:**
- Modify: `tests/Hps.Benchmarks/BaselineHistorySession.cs`
- Modify: `tests/Hps.Benchmarks/BaselineHistory.cs`
- Modify: `tests/Hps.Benchmarks/BaselineHistoryReader.cs`
- Modify: `tests/Hps.Benchmarks/BaselineHistoryGenerator.cs`
- Modify: `tests/Hps.Benchmarks.Tests/BaselineHistoryReaderTests.cs`
- Modify: `tests/Hps.Benchmarks.Tests/BaselineHistoryGeneratorWriterTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Produced interfaces:**
- `BaselineHistorySession.Comparison`
- `BaselineHistory.Comparison`
- legacy summary fallback to `legacy-summary-without-comparison`

- [ ] **Step 1: Write failing history reader tests**

Add to `BaselineHistoryReaderTests`:

```csharp
// history reader 는 summary 가 계산한 comparison signal 을 잃지 않아야 한다.
// 이 값을 잃으면 history generator 가 legacy/mismatch session 을 구분할 수 없다.
[Fact]
public void ReadSessions_WhenSummaryHasComparison_ReadsComparisonSignal()
```

Assertions:
- session comparison compatible true.
- key runner id and case payload are read.

```csharp
// 과거 summary.json 은 comparison field 가 없다.
// 이런 summary 를 compatible 로 추정하면 history 전체가 비교 가능한 것처럼 보이므로 명시 mismatch 로 바꾼다.
[Fact]
public void ReadSessions_WhenSummaryHasNoComparison_MarksSessionComparisonIncompatible()
```

Assertions:
- session comparison compatible false.
- mismatch code `legacy-summary-without-comparison`
- mismatch session and summary path set.

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineHistoryReaderTests.ReadSessions_WhenSummaryHas
```

Expected Red: missing `Comparison` property or default behavior mismatch.

- [ ] **Step 2: Add session comparison and reader parsing**

Modify `BaselineHistorySession` constructor to accept `BaselineComparisonResult comparison`.

In `BaselineHistoryReader.ReadSummary(...)`:
- if `comparison-compatible` exists, parse comparison fields.
- if missing, create incompatible comparison result with one `legacy-summary-without-comparison` mismatch.
- use current relative `summaryPath` and `session` in mismatch entries.

- [ ] **Step 3: Write failing history generator tests**

Add to `BaselineHistoryGeneratorWriterTests`:

```csharp
// 모든 session summary 가 같은 comparison key 를 가져야 history 전체가 compatible 이다.
// hard gate 가 모두 PASS 여도 runner/case 가 다르면 history trend 로 비교하면 안 된다.
[Fact]
public void Generate_WhenSessionsHaveSameComparisonKey_MarksHistoryComparisonCompatible()
```

```csharp
// 한 session 이 다른 runner/case 를 가지면 history comparison mismatch 로만 기록한다.
// 이 mismatch 는 기존 warning-count 와 hard-passed 를 변경하지 않는다.
[Fact]
public void Generate_WhenSessionComparisonKeyDiffers_RecordsHistoryComparisonMismatchWithoutChangingWarningCount()
```

```csharp
// legacy summary 가 하나라도 섞이면 history comparison 은 incompatible 이다.
// 기존 history command exit code 는 hard gate 기준만 유지해야 하므로 comparison 은 별도 결과로 남긴다.
[Fact]
public void Generate_WhenSessionComparisonIsIncompatible_MarksHistoryComparisonIncompatible()
```

Run focused generator tests and expect Red.

- [ ] **Step 4: Implement aggregate comparison**

Implementation rules:
- If sessions list is empty, comparison incompatible with `no-source-reports`.
- If any session comparison is incompatible, history comparison incompatible and carries session mismatch entries.
- If all session comparisons are compatible but keys differ, add `history-comparison-key-mismatch`.
- History key excludes `processor-count` by reusing `BaselineComparisonKey`.
- `WarningCount`, `HardPassed`, `FailedSessionCount` calculations do not change.

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineHistoryReaderTests
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineHistoryGeneratorWriterTests.Generate_When
```

Expected: focused history reader/generator tests pass.

- [ ] **Step 6: Update state docs and commit**

Commit:

```powershell
git add tests\Hps.Benchmarks\BaselineHistorySession.cs tests\Hps.Benchmarks\BaselineHistory.cs tests\Hps.Benchmarks\BaselineHistoryReader.cs tests\Hps.Benchmarks\BaselineHistoryGenerator.cs tests\Hps.Benchmarks.Tests\BaselineHistoryReaderTests.cs tests\Hps.Benchmarks.Tests\BaselineHistoryGeneratorWriterTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: aggregate baseline history comparison"
```

## Task 5: History JSON/Markdown Output And CLI Smoke

**Files:**
- Modify: `tests/Hps.Benchmarks/BaselineHistoryWriter.cs`
- Modify: `tests/Hps.Benchmarks/BaselineHistoryMarkdownWriter.cs`
- Modify: `tests/Hps.Benchmarks.Tests/BaselineHistoryGeneratorWriterTests.cs`
- Modify: `tests/Hps.Benchmarks.Tests/BaselineHistoryProgramTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Produced output:**
- history JSON comparison top-level fields
- history session comparison summary fields
- history Markdown `## Comparison` section
- CLI smoke proving comparison mismatch does not change hard-gate exit code

- [ ] **Step 1: Write failing history JSON writer test**

Add to `BaselineHistoryGeneratorWriterTests`:

```csharp
// history JSON 은 여러 summary session 을 비교할 때 쓰는 canonical artifact 다.
// comparison field 가 없으면 session 간 runner/case mismatch 를 자동화가 감지할 수 없다.
[Fact]
public void Write_WhenHistoryHasComparison_WritesComparisonFields()
```

Assertions:
- top-level `comparison-compatible`
- top-level `comparison-key`
- top-level `comparison-mismatch-count`
- top-level `comparison-mismatches`
- session entry `comparison-compatible`
- session entry `comparison-mismatch-count`

Run focused writer test and expect missing property Red.

- [ ] **Step 2: Implement history JSON output**

Add writer helpers in `BaselineHistoryWriter` equivalent to summary writer. For history mismatch entries:
- write `session` when present.
- write `summary-path` when present.
- write `source-path` only when present.

- [ ] **Step 3: Write failing Markdown and Program tests**

Add Markdown test:

```csharp
// Markdown history 는 session table 을 열기 전에 전체 비교 가능성을 보여줘야 한다.
// runner/case mismatch 가 있을 때 사람이 JSON을 열지 않아도 바로 확인할 수 있어야 한다.
[Fact]
public void MarkdownWriter_WhenHistoryHasComparison_WritesComparisonSection()
```

Add Program smoke test:

```csharp
// comparison mismatch 는 아직 hard failure 가 아니다.
// legacy summary 처럼 comparison-compatible=false 여도 hard-passed=true 이면 CLI exit code 는 success 로 유지한다.
[Fact]
public void Main_WhenHistorySummaryHasComparisonMismatchOnly_ReturnsSuccessAndWritesComparisonSignal()
```

Program test setup should write one summary with `hard-passed=true`, `warning-count=0`, and missing comparison fields to reuse the legacy mismatch path.

- [ ] **Step 4: Implement Markdown section**

Add `WriteComparison(...)` to `BaselineHistoryMarkdownWriter`.

Output minimum:
- `## Comparison`
- `- compatible: true|false`
- baseline key summary when key exists
- mismatch table with `session`, `summary-path`, `code`, `field`, `expected`, `actual`

Program wiring should not change because `BaselineHistoryReader`/`Generator`/`Writer` already carry the new signal.

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineHistoryGeneratorWriterTests
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineHistoryProgramTests
```

Expected:
- history writer/Markdown tests pass.
- Program comparison mismatch-only test returns exit code 0 when hard gate is true.

- [ ] **Step 6: Run final verification**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore
git diff --check
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
```

Expected:
- benchmark tests pass.
- `git diff --check` reports no whitespace errors.
- solution build has warning 0/error 0.
- solution tests pass.

- [ ] **Step 7: Update state docs and commit**

Commit:

```powershell
git add tests\Hps.Benchmarks\BaselineHistoryWriter.cs tests\Hps.Benchmarks\BaselineHistoryMarkdownWriter.cs tests\Hps.Benchmarks.Tests\BaselineHistoryGeneratorWriterTests.cs tests\Hps.Benchmarks.Tests\BaselineHistoryProgramTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: write baseline history comparison"
```

## Plan Self-Review

- D080 coverage: summary JSON, summary Markdown, history JSON, history Markdown, legacy incompatibility, unknown runner incompatibility, per-result-name cases, hard gate 분리가 모두 Task 에 포함됐다.
- Scope control: CI workflow, warning-as-failure, latency hard threshold, generated baseline artifact 재생성은 제외했다.
- Reviewability: Task 1은 prerequisite field propagation, Task 2는 summary calculation, Task 3은 summary output, Task 4는 history aggregate, Task 5는 history output/CLI smoke 로 분리했다.
- Test comments: 새 테스트마다 검증 의도와 실패 시 의미를 한국어 주석으로 남기도록 명시했다.
- Commit boundary: 각 Task 는 별도 커밋으로 닫히며 root state docs 를 함께 갱신한다.
- Validation path: 각 Task 는 focused tests 를 먼저 실행하고 Task 5에서 benchmark tests, `git diff --check`, solution build/test 를 실행한다.
