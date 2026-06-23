# Baseline History Report Command Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 여러 baseline session `summary.json`을 읽어 `history.json`과 선택적 `history.md`를 생성하는 `--summarize-baseline-history` command 를 추가한다.

**Architecture:** 기존 `--summarize-baseline` 흐름처럼 parser, domain/reader, generator/writer, Program wiring 을 분리한다. history command 는 per-run raw JSON과 per-session summary 를 대체하지 않는 파생 aggregate artifact 만 만들고, warning 은 exit code 에 영향을 주지 않는 soft signal 로 유지한다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, `System.Text.Json`, 기존 `tests/Hps.Benchmarks` command/parser/writer 패턴.

## Global Constraints

- TFM 은 `net9.0`, LangVersion 은 C# 8.0 이며 file-scoped namespace, record, target-typed `new()` 를 쓰지 않는다.
- 모든 문서와 주석은 한국어로 작성한다. 테스트에는 무엇을 검증하는지 한국어 주석을 붙인다.
- 코드 변경은 Red-Green-Refactor 를 따른다. 컴파일 실패 Red 가 아니라 assertion failure Red 를 먼저 확인한다.
- 작업은 기능별 작은 단위로 나누고, 각 Task 는 별도 커밋으로 끝낸다.
- D078에 따라 history command 는 provider-independent aggregate artifact 다. CI workflow, warning-as-failure, latency hard gate 는 구현하지 않는다.
- `BenchmarkCommand` enum 값은 `SummarizeBaselineHistory`로 고정한다.
- 입력 root 는 parent baseline root 와 특정 날짜 root 를 모두 허용하되, 무제한 recursive scan 은 하지 않는다.
- 기존 `docs/benchmarks/baselines/index.md`는 자동 덮어쓰지 않는다.

---

## File Structure

- `tests/Hps.Benchmarks/BenchmarkCommand.cs`
  - `SummarizeBaselineHistory` command 값을 추가한다.
- `tests/Hps.Benchmarks/BenchmarkCommandLine.cs`
  - history input/output 경로를 parser 결과로 보존한다.
- `tests/Hps.Benchmarks/BenchmarkCommandParser.cs`
  - `--summarize-baseline-history <baseline-root> --history <output-json> [--history-md <output-md>]`를 parse 한다.
- `tests/Hps.Benchmarks/Program.cs`
  - usage text 와 마지막 실행 wiring 을 담당한다. Task 1에서는 usage text 만 갱신한다.
- `tests/Hps.Benchmarks/BaselineHistorySession.cs`
  - summary 1개에서 읽은 history row 값을 보존한다.
- `tests/Hps.Benchmarks/BaselineHistory.cs`
  - 여러 session aggregate 와 computed count 를 보존한다.
- `tests/Hps.Benchmarks/BaselineHistoryReader.cs`
  - parent root/date root discovery 와 summary JSON schema v1 reading 을 담당한다.
- `tests/Hps.Benchmarks/BaselineHistoryGenerator.cs`
  - session 목록을 hard/warning aggregate 로 변환한다.
- `tests/Hps.Benchmarks/BaselineHistoryWriter.cs`
  - stable JSON schema 를 쓴다.
- `tests/Hps.Benchmarks/BaselineHistoryMarkdownWriter.cs`
  - 사람이 읽는 table artifact 를 쓴다.
- `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`
  - parser contract regression 을 담당한다.
- `tests/Hps.Benchmarks.Tests/BaselineHistoryReaderTests.cs`
  - discovery, summary parsing, date root/root summary 호환을 검증한다.
- `tests/Hps.Benchmarks.Tests/BaselineHistoryGeneratorWriterTests.cs`
  - aggregate, JSON shape, Markdown table 을 검증한다.
- `tests/Hps.Benchmarks.Tests/BaselineHistoryProgramTests.cs`
  - CLI wiring 의 exit code 와 file output 을 검증한다.
- Root state docs
  - `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`를 각 Task 완료마다 갱신한다.

---

### Task 1: Parser Contract

**Files:**
- Modify: `tests/Hps.Benchmarks/BenchmarkCommand.cs`
- Modify: `tests/Hps.Benchmarks/BenchmarkCommandLine.cs`
- Modify: `tests/Hps.Benchmarks/BenchmarkCommandParser.cs`
- Modify: `tests/Hps.Benchmarks/Program.cs`
- Modify: `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - Existing `BenchmarkCommandParser.TryParse(string[] args, out BenchmarkCommandLine commandLine, out string? errorMessage)`
- Produces:
  - `BenchmarkCommand.SummarizeBaselineHistory`
  - `BenchmarkCommandLine.HistoryInputRoot`
  - `BenchmarkCommandLine.HistoryOutputPath`
  - `BenchmarkCommandLine.HistoryMarkdownOutputPath`
  - Parser messages:
    - `MessageHistoryInputRequired`
    - `MessageHistoryOutputRequired`
    - `MessageHistoryMarkdownOutputRequired`
    - `MessageHistoryReportNotAllowed`

- [ ] **Step 1: Write the failing parser tests**

Append these tests to `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs` before `GetStringProperty`:

```csharp
        // history command 는 여러 session summary 를 읽는 별도 aggregate command 다.
        // parser 가 input root 와 JSON output path 를 보존해야 이후 reader/writer 단계가 같은 계약에 붙을 수 있다.
        [Fact]
        public void TryParse_WhenSummarizeBaselineHistoryHasInputAndHistory_ReturnsHistoryCommand()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--summarize-baseline-history", "docs/baselines", "--history", "out/history.json" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.Null(errorMessage);
            Assert.Equal("SummarizeBaselineHistory", commandLine.Command.ToString());
            Assert.Equal("docs/baselines", GetStringProperty(commandLine, "HistoryInputRoot"));
            Assert.Equal("out/history.json", GetStringProperty(commandLine, "HistoryOutputPath"));
            Assert.Null(GetStringProperty(commandLine, "HistoryMarkdownOutputPath"));
            Assert.Null(commandLine.ReportPath);
            Assert.Null(commandLine.BaselineOutputDirectory);
        }

        // Markdown history 는 JSON history 를 대체하지 않는 선택 보조 artifact 다.
        // parser 가 두 output path 를 분리해 보존해야 Program wiring 이 같은 aggregate 로 두 파일을 쓸 수 있다.
        [Fact]
        public void TryParse_WhenSummarizeBaselineHistoryHasMarkdown_ReturnsHistoryCommandWithMarkdownPath()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--summarize-baseline-history", "docs/baselines", "--history", "out/history.json", "--history-md", "out/history.md" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.Null(errorMessage);
            Assert.Equal("SummarizeBaselineHistory", commandLine.Command.ToString());
            Assert.Equal("docs/baselines", GetStringProperty(commandLine, "HistoryInputRoot"));
            Assert.Equal("out/history.json", GetStringProperty(commandLine, "HistoryOutputPath"));
            Assert.Equal("out/history.md", GetStringProperty(commandLine, "HistoryMarkdownOutputPath"));
        }

        // history command 는 output directory command 가 아니므로 --history JSON 파일 경로가 반드시 필요하다.
        // 이 검증이 없으면 사용자가 history artifact 를 기대했는데 아무 파일도 생기지 않는 사용성 오류가 생긴다.
        [Fact]
        public void TryParse_WhenSummarizeBaselineHistoryMissingHistory_ReturnsUsageError()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--summarize-baseline-history", "docs/baselines" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.NotNull(errorMessage);
            Assert.Equal("SummarizeBaselineHistory", commandLine.Command.ToString());
        }

        // --history-md 는 선택 옵션이지만 지정했다면 파일 경로가 반드시 필요하다.
        // 경로 없이 통과시키면 Markdown history 를 쓴다고 보고한 뒤 실제 출력 위치를 잃는다.
        [Fact]
        public void TryParse_WhenSummarizeBaselineHistoryMarkdownMissingPath_ReturnsUsageError()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--summarize-baseline-history", "docs/baselines", "--history", "out/history.json", "--history-md" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.NotNull(errorMessage);
            Assert.Equal("SummarizeBaselineHistory", commandLine.Command.ToString());
        }

        // --report 는 단일 runner raw JSON 출력용이고 history command 출력은 --history 로만 지정한다.
        // 두 옵션을 섞으면 raw report 와 aggregate history 의 의미가 충돌하므로 parser 단계에서 막는다.
        [Fact]
        public void TryParse_WhenSummarizeBaselineHistoryHasReport_ReturnsUsageError()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--summarize-baseline-history", "docs/baselines", "--report", "out/report.json" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.NotNull(errorMessage);
            Assert.Equal("SummarizeBaselineHistory", commandLine.Command.ToString());
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BenchmarkCommandParserTests
```

Expected: test execution succeeds but the new assertions fail. The first failure should be `Assert.True()` because `--summarize-baseline-history` is not parsed yet, or `Assert.NotNull()` from `GetStringProperty` before the new properties exist.

- [ ] **Step 3: Add parser surface**

Modify `tests/Hps.Benchmarks/BenchmarkCommand.cs`:

```csharp
        BaselineSuite,
        SummarizeBaseline,
        SummarizeBaselineHistory,
        Help
```

Modify `tests/Hps.Benchmarks/BenchmarkCommandLine.cs` by extending the constructor and properties:

```csharp
        public BenchmarkCommandLine(
            BenchmarkCommand command,
            string? reportPath,
            string? baselineOutputDirectory,
            int baselineRunCount,
            string? summaryInputDirectory,
            string? summaryOutputPath,
            string? summaryMarkdownOutputPath,
            string? historyInputRoot,
            string? historyOutputPath,
            string? historyMarkdownOutputPath)
        {
            Command = command;
            ReportPath = reportPath;
            BaselineOutputDirectory = baselineOutputDirectory;
            BaselineRunCount = baselineRunCount;
            SummaryInputDirectory = summaryInputDirectory;
            SummaryOutputPath = summaryOutputPath;
            SummaryMarkdownOutputPath = summaryMarkdownOutputPath;
            HistoryInputRoot = historyInputRoot;
            HistoryOutputPath = historyOutputPath;
            HistoryMarkdownOutputPath = historyMarkdownOutputPath;
        }
```

Add properties:

```csharp
        public string? HistoryInputRoot { get; }

        public string? HistoryOutputPath { get; }

        public string? HistoryMarkdownOutputPath { get; }
```

Update every existing `new BenchmarkCommandLine(...)` call in `BenchmarkCommandParser` to pass `null, null, null` for the new history arguments when the command is not the history command.

- [ ] **Step 4: Add history parser branch**

Add constants to `BenchmarkCommandParser`:

```csharp
        public const string MessageHistoryInputRequired = "--summarize-baseline-history 옵션에는 입력 baseline root 경로가 필요합니다.";
        public const string MessageHistoryOutputRequired = "--history 옵션에는 저장할 history JSON 파일 경로가 필요합니다.";
        public const string MessageHistoryMarkdownOutputRequired = "--history-md 옵션에는 저장할 history Markdown 파일 경로가 필요합니다.";
        public const string MessageHistoryReportNotAllowed = "--report 옵션은 --summarize-baseline-history 와 함께 사용할 수 없습니다.";
```

Add the branch before `--summarize-baseline`:

```csharp
            if (string.Equals(commandArg, "--summarize-baseline-history", StringComparison.OrdinalIgnoreCase))
                return ParseSummarizeBaselineHistory(args, out commandLine, out errorMessage);
```

Add the method:

```csharp
        private static bool ParseSummarizeBaselineHistory(
            string[] args,
            out BenchmarkCommandLine commandLine,
            out string? errorMessage)
        {
            string? inputRoot = args.Length >= 2 ? args[1] : null;
            commandLine = new BenchmarkCommandLine(
                BenchmarkCommand.SummarizeBaselineHistory,
                null,
                null,
                0,
                null,
                null,
                null,
                inputRoot,
                null,
                null);
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(inputRoot))
            {
                errorMessage = MessageHistoryInputRequired;
                return true;
            }

            if (ContainsReportOption(args))
            {
                errorMessage = MessageHistoryReportNotAllowed;
                return true;
            }

            if ((args.Length != 4 && args.Length != 6) || !string.Equals(args[2], "--history", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = MessageHistoryOutputRequired;
                return true;
            }

            if (string.IsNullOrWhiteSpace(args[3]))
            {
                errorMessage = MessageHistoryOutputRequired;
                return true;
            }

            string? historyMarkdownOutputPath = null;
            if (args.Length == 6)
            {
                if (!string.Equals(args[4], "--history-md", StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = MessageHistoryMarkdownOutputRequired;
                    return true;
                }

                if (string.IsNullOrWhiteSpace(args[5]))
                {
                    errorMessage = MessageHistoryMarkdownOutputRequired;
                    return true;
                }

                historyMarkdownOutputPath = args[5];
            }

            commandLine = new BenchmarkCommandLine(
                BenchmarkCommand.SummarizeBaselineHistory,
                null,
                null,
                0,
                null,
                null,
                null,
                inputRoot,
                args[3],
                historyMarkdownOutputPath);
            return true;
        }
```

- [ ] **Step 5: Add usage text without execution wiring**

Modify `Program.PrintUsage`:

```csharp
            writer.WriteLine("  Hps.Benchmarks --summarize-baseline <input-dir> --summary <output-json> [--summary-md <output-md>]");
            writer.WriteLine("  Hps.Benchmarks --summarize-baseline-history <baseline-root> --history <output-json> [--history-md <output-md>]");
            writer.WriteLine("  Hps.Benchmarks [BenchmarkDotNet arguments]");
```

Do not add the `Program.Main` switch execution branch in this Task. Until Task 4, a parsed history command may return the default usage error path if invoked. This keeps the first commit limited to parser contract and usage surface.

- [ ] **Step 6: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BenchmarkCommandParserTests
```

Expected: all parser tests pass.

- [ ] **Step 7: Run standard verification**

Run:

```powershell
git diff --check
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
```

Expected: whitespace check exit 0, build 경고 0/오류 0, all tests pass with nonzero discovered tests.

- [ ] **Step 8: Update state docs and commit**

Update root state docs to mark Task 1 complete and next work as Task 2. Stage only Task 1 files:

```powershell
git add tests\Hps.Benchmarks\BenchmarkCommand.cs tests\Hps.Benchmarks\BenchmarkCommandLine.cs tests\Hps.Benchmarks\BenchmarkCommandParser.cs tests\Hps.Benchmarks\Program.cs tests\Hps.Benchmarks.Tests\BenchmarkCommandParserTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: parse baseline history command"
```

---

### Task 2: History Domain And Reader

**Files:**
- Create: `tests/Hps.Benchmarks/BaselineHistorySession.cs`
- Create: `tests/Hps.Benchmarks/BaselineHistoryReader.cs`
- Create: `tests/Hps.Benchmarks.Tests/BaselineHistoryReaderTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - Summary JSON schema v1 from `BaselineSummaryWriter`
- Produces:
  - `internal sealed class BaselineHistorySession`
  - `internal static class BaselineHistoryReader`
  - `internal static IReadOnlyList<BaselineHistorySession> ReadSessions(string sourceRoot)`

- [ ] **Step 1: Write compile-safe reader contract test**

Create `tests/Hps.Benchmarks.Tests/BaselineHistoryReaderTests.cs`:

```csharp
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineHistoryReaderTests
    {
        // 새 reader 타입은 Task 2의 생산물이다.
        // 직접 타입 참조 전에 reflection 으로 존재 계약을 먼저 고정해 컴파일 실패가 아닌 assertion failure Red 를 만든다.
        [Fact]
        public void Contract_WhenBaselineHistoryReaderIsMissing_Fails()
        {
            Assert.NotNull(typeof(BenchmarkCommandParser).Assembly.GetType("Hps.Benchmarks.BaselineHistoryReader"));
        }
    }
}
```

- [ ] **Step 2: Run contract test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineHistoryReaderTests
```

Expected: `Assert.NotNull()` failure because `Hps.Benchmarks.BaselineHistoryReader` does not exist yet.

- [ ] **Step 3: Add minimal reader stubs**

Create `tests/Hps.Benchmarks/BaselineHistorySession.cs` and `tests/Hps.Benchmarks/BaselineHistoryReader.cs` with the public shape from the next steps, but make `ReadSessions` throw:

```csharp
public static IReadOnlyList<BaselineHistorySession> ReadSessions(string sourceRoot)
{
    throw new NotSupportedException("Baseline history reader behavior is not implemented yet.");
}
```

Run the same focused command again. Expected: contract test passes. Now replace the test file with the behavior tests in Step 4 and continue.

- [ ] **Step 4: Replace contract test with failing reader behavior tests**

Replace `tests/Hps.Benchmarks.Tests/BaselineHistoryReaderTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineHistoryReaderTests
    {
        // 날짜 root 를 직접 입력하는 경로는 2026-06-18 baseline 호환에 필요하다.
        // root summary 는 과거 구조에서 session-01 역할을 하므로 `session-01(root)`로 표시해야 한다.
        [Fact]
        public void ReadSessions_WhenInputIsDateRoot_ReadsRootSummaryAndChildSessions()
        {
            string dateRoot = CreateTempDirectory("2026-06-18");
            WriteSummary(Path.Combine(dateRoot, "summary.json"), true, 0, 6, 924.1, 1005.5, 2);
            string sessionTwo = Path.Combine(dateRoot, "session-02");
            Directory.CreateDirectory(sessionTwo);
            WriteSummary(Path.Combine(sessionTwo, "summary.json"), true, 1, 6, 512.1, 643.3, 3);
            File.WriteAllText(Path.Combine(dateRoot, "history.json"), "{}");

            BaselineHistorySession[] sessions = BaselineHistoryReader.ReadSessions(dateRoot).ToArray();

            Assert.Equal(2, sessions.Length);
            Assert.Equal("2026-06-18", sessions[0].Date);
            Assert.Equal("session-01(root)", sessions[0].Session);
            Assert.Equal("summary.json", sessions[0].SummaryPath);
            Assert.Equal("session-02", sessions[1].Session);
            Assert.Equal("session-02/summary.json", sessions[1].SummaryPath);
            Assert.Equal(1, sessions[1].WarningCount);
            Assert.Equal(3, sessions[1].TcpHighWatermarkMax);
        }

        // parent baseline root 입력은 여러 날짜 directory 를 훑되 한 단계 아래 날짜 directory 만 본다.
        // 무제한 recursive scan 을 허용하면 generated history 나 임시 복사본이 중복 집계될 수 있다.
        [Fact]
        public void ReadSessions_WhenInputIsParentRoot_ReadsImmediateDateChildrenOnly()
        {
            string parentRoot = CreateTempDirectory("baselines");
            string dateRoot = Path.Combine(parentRoot, "2026-06-18");
            Directory.CreateDirectory(dateRoot);
            WriteSummary(Path.Combine(dateRoot, "summary.json"), true, 0, 6, 924.1, 1005.5, 2);
            string ignored = Path.Combine(parentRoot, "notes");
            Directory.CreateDirectory(ignored);
            WriteSummary(Path.Combine(ignored, "summary.json"), true, 0, 6, 1.0, 1.0, 1);

            BaselineHistorySession[] sessions = BaselineHistoryReader.ReadSessions(parentRoot).ToArray();

            Assert.Single(sessions);
            Assert.Equal("2026-06-18", sessions[0].Date);
            Assert.Equal("2026-06-18/summary.json", sessions[0].SummaryPath);
        }

        // summary 가 하나도 없으면 성공한 빈 history 로 위장하지 않는다.
        // Program wiring 은 이 예외를 usage/data error exit code 2로 수렴시켜야 한다.
        [Fact]
        public void ReadSessions_WhenNoSummaryExists_ThrowsInvalidOperationException()
        {
            string parentRoot = CreateTempDirectory("baselines");

            Assert.Throws<InvalidOperationException>(
                delegate { BaselineHistoryReader.ReadSessions(parentRoot); });
        }

        private static string CreateTempDirectory(string leafName)
        {
            string directory = Path.Combine(Path.GetTempPath(), "hps-baseline-history-reader-tests", Path.GetRandomFileName(), leafName);
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void WriteSummary(string path, bool hardPassed, int warningCount, int reportCount, double loadP99, double openLoopP99, int tcpHwm)
        {
            string json = "{"
                + "\"summary-version\":1,"
                + "\"source-directory\":\"source\","
                + "\"source-report-count\":" + reportCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"hard-passed\":" + (hardPassed ? "true" : "false") + ","
                + "\"hard-failure-count\":" + (hardPassed ? "0" : "1") + ","
                + "\"warning-count\":" + warningCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"warnings\":[],"
                + "\"by-kind\":{"
                + "\"load\":{\"p99-max-us\":" + loadP99.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"tcp-hwm-max\":" + tcpHwm.ToString(System.Globalization.CultureInfo.InvariantCulture) + "},"
                + "\"open-loop\":{\"p99-max-us\":" + openLoopP99.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"tcp-hwm-max\":" + tcpHwm.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}"
                + "}"
                + "}";
            File.WriteAllText(path, json);
        }
    }
}
```

Run the focused command again. Expected: `NotSupportedException` from the stub reader.

- [ ] **Step 5: Add `BaselineHistorySession`**

Create `tests/Hps.Benchmarks/BaselineHistorySession.cs`:

```csharp
namespace Hps.Benchmarks
{
    internal sealed class BaselineHistorySession
    {
        public BaselineHistorySession(
            string date,
            string session,
            string summaryPath,
            string? humanReportPath,
            int sourceReportCount,
            bool hardPassed,
            int hardFailureCount,
            int warningCount,
            double loadP99MaxMicroseconds,
            double openLoopP99MaxMicroseconds,
            int tcpHighWatermarkMax)
        {
            Date = date;
            Session = session;
            SummaryPath = summaryPath;
            HumanReportPath = humanReportPath;
            SourceReportCount = sourceReportCount;
            HardPassed = hardPassed;
            HardFailureCount = hardFailureCount;
            WarningCount = warningCount;
            LoadP99MaxMicroseconds = loadP99MaxMicroseconds;
            OpenLoopP99MaxMicroseconds = openLoopP99MaxMicroseconds;
            TcpHighWatermarkMax = tcpHighWatermarkMax;
        }

        public string Date { get; }

        public string Session { get; }

        public string SummaryPath { get; }

        public string? HumanReportPath { get; }

        public int SourceReportCount { get; }

        public bool HardPassed { get; }

        public int HardFailureCount { get; }

        public int WarningCount { get; }

        public double LoadP99MaxMicroseconds { get; }

        public double OpenLoopP99MaxMicroseconds { get; }

        public int TcpHighWatermarkMax { get; }
    }
}
```

- [ ] **Step 6: Add reader implementation**

Create `tests/Hps.Benchmarks/BaselineHistoryReader.cs` with these responsibilities:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hps.Benchmarks
{
    internal static class BaselineHistoryReader
    {
        private static readonly Regex DateDirectoryPattern = new Regex(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.CultureInvariant);
        private static readonly Regex SessionDirectoryPattern = new Regex(@"^session-\d{2}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static IReadOnlyList<BaselineHistorySession> ReadSessions(string sourceRoot)
        {
            if (string.IsNullOrWhiteSpace(sourceRoot))
                throw new ArgumentException("baseline history input root 는 비어 있을 수 없습니다.", nameof(sourceRoot));

            string fullRoot = Path.GetFullPath(sourceRoot);
            List<BaselineHistorySession> sessions = new List<BaselineHistorySession>();

            if (IsDateDirectory(fullRoot))
                AddDateRoot(fullRoot, fullRoot, sessions);
            else
                AddDateChildren(fullRoot, sessions);

            if (sessions.Count == 0)
                throw new InvalidOperationException("baseline history summary.json 을 찾지 못했습니다.");

            return sessions;
        }
    }
}
```

Fill helper methods in the same file:

```csharp
        private static void AddDateChildren(string parentRoot, List<BaselineHistorySession> sessions)
        {
            string[] directories = Directory.GetDirectories(parentRoot);
            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < directories.Length; i++)
            {
                if (IsDateDirectory(directories[i]))
                    AddDateRoot(parentRoot, directories[i], sessions);
            }
        }

        private static void AddDateRoot(string inputRoot, string dateRoot, List<BaselineHistorySession> sessions)
        {
            string rootSummary = Path.Combine(dateRoot, "summary.json");
            if (File.Exists(rootSummary))
                sessions.Add(ReadSummary(inputRoot, dateRoot, "session-01(root)", rootSummary));

            string[] directories = Directory.GetDirectories(dateRoot);
            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < directories.Length; i++)
            {
                string sessionName = Path.GetFileName(directories[i]);
                if (!SessionDirectoryPattern.IsMatch(sessionName))
                    continue;

                string summaryPath = Path.Combine(directories[i], "summary.json");
                if (File.Exists(summaryPath))
                    sessions.Add(ReadSummary(inputRoot, dateRoot, sessionName, summaryPath));
            }
        }

        private static bool IsDateDirectory(string path)
        {
            return DateDirectoryPattern.IsMatch(Path.GetFileName(path));
        }
```

Read JSON with explicit keys:

```csharp
        private static BaselineHistorySession ReadSummary(string inputRoot, string dateRoot, string session, string summaryPath)
        {
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(summaryPath)))
            {
                JsonElement root = document.RootElement;
                int summaryVersion = root.GetProperty("summary-version").GetInt32();
                if (summaryVersion != 1)
                    throw new InvalidOperationException("지원하지 않는 baseline summary version 입니다.");

                string? humanReportPath = null;
                string summaryDirectory = Path.GetDirectoryName(summaryPath)!;
                string markdownPath = Path.Combine(summaryDirectory, "summary.md");
                if (File.Exists(markdownPath))
                    humanReportPath = ToRelativePath(inputRoot, markdownPath);

                double loadP99 = GetKindDouble(root, "load", "p99-max-us");
                double openLoopP99 = GetKindDouble(root, "open-loop", "p99-max-us");
                int loadHwm = GetKindInt(root, "load", "tcp-hwm-max");
                int openLoopHwm = GetKindInt(root, "open-loop", "tcp-hwm-max");

                return new BaselineHistorySession(
                    Path.GetFileName(dateRoot),
                    session,
                    ToRelativePath(inputRoot, summaryPath),
                    humanReportPath,
                    root.GetProperty("source-report-count").GetInt32(),
                    root.GetProperty("hard-passed").GetBoolean(),
                    root.GetProperty("hard-failure-count").GetInt32(),
                    root.GetProperty("warning-count").GetInt32(),
                    loadP99,
                    openLoopP99,
                    Math.Max(loadHwm, openLoopHwm));
            }
        }
```

Add helper readers:

```csharp
        private static double GetKindDouble(JsonElement root, string kindName, string propertyName)
        {
            JsonElement byKind;
            JsonElement kind;
            JsonElement value;
            if (!root.TryGetProperty("by-kind", out byKind) || !byKind.TryGetProperty(kindName, out kind) || kind.ValueKind == JsonValueKind.Null)
                return 0;

            if (!kind.TryGetProperty(propertyName, out value))
                return 0;

            if (value.ValueKind == JsonValueKind.Number)
                return value.GetDouble();

            return double.Parse(value.GetString()!, CultureInfo.InvariantCulture);
        }

        private static int GetKindInt(JsonElement root, string kindName, string propertyName)
        {
            JsonElement byKind;
            JsonElement kind;
            JsonElement value;
            if (!root.TryGetProperty("by-kind", out byKind) || !byKind.TryGetProperty(kindName, out kind) || kind.ValueKind == JsonValueKind.Null)
                return 0;

            if (!kind.TryGetProperty(propertyName, out value))
                return 0;

            return value.GetInt32();
        }

        private static string ToRelativePath(string root, string path)
        {
            Uri rootUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(root)));
            Uri pathUri = new Uri(Path.GetFullPath(path));
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('\\', '/');
        }

        private static string AppendDirectorySeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                return path;

            return path + Path.DirectorySeparatorChar;
        }
```

- [ ] **Step 7: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineHistoryReaderTests
```

Expected: reader tests pass.

- [ ] **Step 8: Run standard verification and commit**

Run:

```powershell
git diff --check
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
```

Expected: whitespace check exit 0, build 경고 0/오류 0, all tests pass.

Commit only Task 2 files:

```powershell
git add tests\Hps.Benchmarks\BaselineHistorySession.cs tests\Hps.Benchmarks\BaselineHistoryReader.cs tests\Hps.Benchmarks.Tests\BaselineHistoryReaderTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: read baseline history summaries"
```

---

### Task 3: History Aggregate And Writers

**Files:**
- Create: `tests/Hps.Benchmarks/BaselineHistory.cs`
- Create: `tests/Hps.Benchmarks/BaselineHistoryGenerator.cs`
- Create: `tests/Hps.Benchmarks/BaselineHistoryWriter.cs`
- Create: `tests/Hps.Benchmarks/BaselineHistoryMarkdownWriter.cs`
- Create: `tests/Hps.Benchmarks.Tests/BaselineHistoryGeneratorWriterTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - `BaselineHistorySession`
- Produces:
  - `internal sealed class BaselineHistory`
  - `internal static class BaselineHistoryGenerator`
  - `internal static BaselineHistory Generate(string sourceRoot, IReadOnlyList<BaselineHistorySession> sessions)`
  - `internal static class BaselineHistoryWriter`
  - `internal static void Write(string path, BaselineHistory history)`
  - `internal static class BaselineHistoryMarkdownWriter`
  - `internal static void Write(TextWriter writer, BaselineHistory history)`

- [ ] **Step 1: Write compile-safe generator/writer contract test**

Create `tests/Hps.Benchmarks.Tests/BaselineHistoryGeneratorWriterTests.cs`:

```csharp
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineHistoryGeneratorWriterTests
    {
        // generator/writer 타입은 Task 3의 생산물이다.
        // 직접 참조 전에 reflection 으로 존재 계약을 먼저 고정해 컴파일 실패가 아닌 assertion failure Red 를 만든다.
        [Fact]
        public void Contract_WhenBaselineHistoryGeneratorIsMissing_Fails()
        {
            Assert.NotNull(typeof(BenchmarkCommandParser).Assembly.GetType("Hps.Benchmarks.BaselineHistoryGenerator"));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineHistoryGeneratorWriterTests
```

Expected: `Assert.NotNull()` failure because `Hps.Benchmarks.BaselineHistoryGenerator` does not exist yet.

- [ ] **Step 3: Add minimal aggregate/writer stubs**

Create the Task 3 files with the public shape from the next steps. Keep the behavior incomplete:

```csharp
public static BaselineHistory Generate(string sourceRoot, IReadOnlyList<BaselineHistorySession> sessions)
{
    return new BaselineHistory(sourceRoot, sessions);
}
```

Make `BaselineHistoryWriter.Write(...)` and `BaselineHistoryMarkdownWriter.Write(...)` throw `NotSupportedException`. Run the focused command again. Expected: contract test passes.

- [ ] **Step 4: Replace contract test with failing behavior tests**

Replace `tests/Hps.Benchmarks.Tests/BaselineHistoryGeneratorWriterTests.cs`:

```csharp
using System.IO;
using System.Text.Json;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineHistoryGeneratorWriterTests
    {
        // history aggregate 의 hard gate 는 기존 summary 의 hard-passed 만 합산한다.
        // warning 은 soft signal 이므로 warning-count 를 보존하되 process failure 로 승격하지 않는다.
        [Fact]
        public void Generate_WhenSessionsContainFailureAndWarnings_AggregatesCounts()
        {
            BaselineHistory history = BaselineHistoryGenerator.Generate(
                "docs/baselines",
                new[]
                {
                    CreateSession("2026-06-18", "session-01(root)", true, 0, 0, 924.1, 1005.5, 2),
                    CreateSession("2026-06-19", "session-01", false, 1, 2, 1400.0, 1500.0, 16)
                });

            Assert.Equal("docs/baselines", history.SourceRoot);
            Assert.Equal(2, history.SessionCount);
            Assert.False(history.HardPassed);
            Assert.Equal(1, history.HardFailureCount);
            Assert.Equal(2, history.WarningCount);
        }

        // JSON writer 는 CI/provider 에 묶이지 않는 stable key 집합을 만든다.
        // 이 shape 가 흔들리면 이후 generated index 나 local script 가 history 를 읽지 못한다.
        [Fact]
        public void Write_WhenHistoryHasSessions_WritesStableJsonShape()
        {
            string directory = CreateTempDirectory();
            string path = Path.Combine(directory, "history.json");
            BaselineHistory history = BaselineHistoryGenerator.Generate(
                "docs/baselines",
                new[] { CreateSession("2026-06-18", "session-01(root)", true, 0, 0, 924.1, 1005.5, 2) });

            BaselineHistoryWriter.Write(path, history);

            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement root = document.RootElement;
                Assert.Equal(1, root.GetProperty("history-version").GetInt32());
                Assert.Equal("docs/baselines", root.GetProperty("source-root").GetString());
                Assert.Equal(1, root.GetProperty("session-count").GetInt32());
                Assert.True(root.GetProperty("hard-passed").GetBoolean());
                Assert.Equal(0, root.GetProperty("warning-count").GetInt32());
                JsonElement session = root.GetProperty("sessions")[0];
                Assert.Equal("2026-06-18", session.GetProperty("date").GetString());
                Assert.Equal("session-01(root)", session.GetProperty("session").GetString());
                Assert.Equal(924.1, session.GetProperty("load-p99-max-us").GetDouble());
                Assert.Equal(2, session.GetProperty("tcp-hwm-max").GetInt32());
            }
        }

        // Markdown writer 는 사람이 현재 index 와 같은 정보를 빠르게 보는 보조 artifact 다.
        // 자동화의 canonical 입력은 JSON 이므로 Markdown 은 session table 과 warning row 존재만 고정한다.
        [Fact]
        public void MarkdownWriter_WhenHistoryHasWarnings_WritesSessionTableAndWarningList()
        {
            BaselineHistory history = BaselineHistoryGenerator.Generate(
                "docs/baselines",
                new[] { CreateSession("2026-06-19", "session-01", true, 0, 2, 1400.0, 1500.0, 16) });
            StringWriter writer = new StringWriter();

            BaselineHistoryMarkdownWriter.Write(writer, history);

            string markdown = writer.ToString();
            Assert.Contains("# Baseline History", markdown);
            Assert.Contains("| 2026-06-19 | session-01 |", markdown);
            Assert.Contains("warning 이 있는 session", markdown);
        }

        private static BaselineHistorySession CreateSession(
            string date,
            string session,
            bool hardPassed,
            int hardFailureCount,
            int warningCount,
            double loadP99,
            double openLoopP99,
            int tcpHwm)
        {
            return new BaselineHistorySession(
                date,
                session,
                date + "/" + session + "/summary.json",
                date + "/" + session + "/summary.md",
                6,
                hardPassed,
                hardFailureCount,
                warningCount,
                loadP99,
                openLoopP99,
                tcpHwm);
        }

        private static string CreateTempDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "hps-baseline-history-writer-tests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
```

Run the focused command again. Expected: assertion failure for aggregate counts or `NotSupportedException` from the writer stubs.

- [ ] **Step 5: Add `BaselineHistory` and generator**

Create `tests/Hps.Benchmarks/BaselineHistory.cs`:

```csharp
using System.Collections.Generic;

namespace Hps.Benchmarks
{
    internal sealed class BaselineHistory
    {
        public BaselineHistory(string sourceRoot, IReadOnlyList<BaselineHistorySession> sessions)
        {
            SourceRoot = sourceRoot;
            Sessions = sessions;
        }

        public string SourceRoot { get; }

        public IReadOnlyList<BaselineHistorySession> Sessions { get; }

        public int SessionCount
        {
            get { return Sessions.Count; }
        }

        public bool HardPassed
        {
            get { return HardFailureCount == 0; }
        }

        public int HardFailureCount { get; internal set; }

        public int WarningCount { get; internal set; }
    }
}
```

Create `tests/Hps.Benchmarks/BaselineHistoryGenerator.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Hps.Benchmarks
{
    internal static class BaselineHistoryGenerator
    {
        public static BaselineHistory Generate(string sourceRoot, IReadOnlyList<BaselineHistorySession> sessions)
        {
            if (sourceRoot == null)
                throw new ArgumentNullException(nameof(sourceRoot));

            if (sessions == null)
                throw new ArgumentNullException(nameof(sessions));

            int hardFailureCount = 0;
            int warningCount = 0;
            for (int i = 0; i < sessions.Count; i++)
            {
                hardFailureCount += sessions[i].HardFailureCount;
                warningCount += sessions[i].WarningCount;
            }

            BaselineHistory history = new BaselineHistory(sourceRoot, sessions);
            history.HardFailureCount = hardFailureCount;
            history.WarningCount = warningCount;
            return history;
        }
    }
}
```

- [ ] **Step 6: Add JSON writer**

Create `tests/Hps.Benchmarks/BaselineHistoryWriter.cs` following `BaselineSummaryWriter`:

```csharp
using System;
using System.IO;
using System.Text.Json;

namespace Hps.Benchmarks
{
    internal static class BaselineHistoryWriter
    {
        public static void Write(string path, BaselineHistory history)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("history output path 는 비어 있을 수 없습니다.", nameof(path));

            if (history == null)
                throw new ArgumentNullException(nameof(history));

            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (FileStream stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                JsonWriterOptions options = new JsonWriterOptions { Indented = true };
                using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, options))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("history-version", 1);
                    writer.WriteString("source-root", history.SourceRoot);
                    writer.WriteNumber("session-count", history.SessionCount);
                    writer.WriteBoolean("hard-passed", history.HardPassed);
                    writer.WriteNumber("hard-failure-count", history.HardFailureCount);
                    writer.WriteNumber("warning-count", history.WarningCount);
                    writer.WritePropertyName("sessions");
                    writer.WriteStartArray();
                    for (int i = 0; i < history.Sessions.Count; i++)
                        WriteSession(writer, history.Sessions[i]);
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
            }
        }
    }
}
```

Add `WriteSession` in the same file:

```csharp
        private static void WriteSession(Utf8JsonWriter writer, BaselineHistorySession session)
        {
            writer.WriteStartObject();
            writer.WriteString("date", session.Date);
            writer.WriteString("session", session.Session);
            writer.WriteString("summary-path", session.SummaryPath);
            if (session.HumanReportPath == null)
                writer.WriteNull("human-report-path");
            else
                writer.WriteString("human-report-path", session.HumanReportPath);
            writer.WriteNumber("source-report-count", session.SourceReportCount);
            writer.WriteBoolean("hard-passed", session.HardPassed);
            writer.WriteNumber("warning-count", session.WarningCount);
            writer.WriteNumber("load-p99-max-us", session.LoadP99MaxMicroseconds);
            writer.WriteNumber("open-loop-p99-max-us", session.OpenLoopP99MaxMicroseconds);
            writer.WriteNumber("tcp-hwm-max", session.TcpHighWatermarkMax);
            writer.WriteEndObject();
        }
```

- [ ] **Step 7: Add Markdown writer**

Create `tests/Hps.Benchmarks/BaselineHistoryMarkdownWriter.cs`:

```csharp
using System;
using System.Globalization;
using System.IO;

namespace Hps.Benchmarks
{
    internal static class BaselineHistoryMarkdownWriter
    {
        public static void Write(TextWriter writer, BaselineHistory history)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            if (history == null)
                throw new ArgumentNullException(nameof(history));

            writer.WriteLine("# Baseline History");
            writer.WriteLine();
            WriteLine(writer, "- source root: `{0}`", history.SourceRoot);
            WriteLine(writer, "- session count: {0}", history.SessionCount);
            WriteLine(writer, "- hard gate: {0}", history.HardPassed ? "PASS" : "FAIL");
            WriteLine(writer, "- warning count: {0}", history.WarningCount);
            writer.WriteLine();
            writer.WriteLine("| 날짜 | session | summary | human report | raw reports | hard passed | warnings | load p99 max us | open-loop p99 max us | TCP HWM max |");
            writer.WriteLine("| --- | --- | --- | --- | ---: | --- | ---: | ---: | ---: | ---: |");

            for (int i = 0; i < history.Sessions.Count; i++)
                WriteSessionRow(writer, history.Sessions[i]);

            writer.WriteLine();
            writer.WriteLine("## warning 이 있는 session");
            writer.WriteLine();
            bool wroteWarning = false;
            for (int i = 0; i < history.Sessions.Count; i++)
            {
                if (history.Sessions[i].WarningCount == 0)
                    continue;

                wroteWarning = true;
                WriteLine(writer, "- `{0}` `{1}`: {2}", history.Sessions[i].Date, history.Sessions[i].Session, history.Sessions[i].WarningCount);
            }

            if (!wroteWarning)
                writer.WriteLine("- 없음");
        }
    }
}
```

Add helpers:

```csharp
        private static void WriteSessionRow(TextWriter writer, BaselineHistorySession session)
        {
            WriteLine(
                writer,
                "| {0} | {1} | `{2}` | {3} | {4} | {5} | {6} | {7} | {8} | {9} |",
                EscapeCell(session.Date),
                EscapeCell(session.Session),
                EscapeCode(session.SummaryPath),
                session.HumanReportPath == null ? "" : "`" + EscapeCode(session.HumanReportPath) + "`",
                session.SourceReportCount,
                session.HardPassed ? "true" : "false",
                session.WarningCount,
                FormatDouble(session.LoadP99MaxMicroseconds),
                FormatDouble(session.OpenLoopP99MaxMicroseconds),
                session.TcpHighWatermarkMax);
        }

        private static void WriteLine(TextWriter writer, string format, params object[] args)
        {
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture, format, args));
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string EscapeCell(string value)
        {
            return value.Replace("|", "\\|");
        }

        private static string EscapeCode(string value)
        {
            return value.Replace("`", "\\`");
        }
```

- [ ] **Step 8: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineHistoryGeneratorWriterTests
```

Expected: generator/writer tests pass.

- [ ] **Step 9: Run standard verification and commit**

Run:

```powershell
git diff --check
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
```

Expected: whitespace check exit 0, build 경고 0/오류 0, all tests pass.

Commit only Task 3 files:

```powershell
git add tests\Hps.Benchmarks\BaselineHistory.cs tests\Hps.Benchmarks\BaselineHistoryGenerator.cs tests\Hps.Benchmarks\BaselineHistoryWriter.cs tests\Hps.Benchmarks\BaselineHistoryMarkdownWriter.cs tests\Hps.Benchmarks.Tests\BaselineHistoryGeneratorWriterTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: write baseline history reports"
```

---

### Task 4: Program Wiring And Smoke

**Files:**
- Modify: `tests/Hps.Benchmarks/Program.cs`
- Create: `tests/Hps.Benchmarks.Tests/BaselineHistoryProgramTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - `BenchmarkCommand.SummarizeBaselineHistory`
  - `BenchmarkCommandLine.HistoryInputRoot`
  - `BenchmarkCommandLine.HistoryOutputPath`
  - `BenchmarkCommandLine.HistoryMarkdownOutputPath`
  - `BaselineHistoryReader.ReadSessions`
  - `BaselineHistoryGenerator.Generate`
  - `BaselineHistoryWriter.Write`
  - `BaselineHistoryMarkdownWriter.Write`
- Produces:
  - Runnable CLI: `Hps.Benchmarks --summarize-baseline-history <baseline-root> --history <output-json> [--history-md <output-md>]`

- [ ] **Step 1: Write failing Program tests**

Create `tests/Hps.Benchmarks.Tests/BaselineHistoryProgramTests.cs`:

```csharp
using System.Globalization;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineHistoryProgramTests
    {
        // Program wiring 은 parser, reader, generator, writer 를 실제 CLI 경로로 묶는다.
        // output 파일이 둘 다 생겨야 수동 baseline index 를 generated artifact 로 대체 검토할 수 있다.
        [Fact]
        public void Main_WhenHistoryCommandHasPassingSummaries_WritesJsonAndMarkdownAndReturnsSuccess()
        {
            string root = CreateTempDirectory("baselines");
            string dateRoot = Path.Combine(root, "2026-06-18");
            Directory.CreateDirectory(dateRoot);
            WriteSummary(Path.Combine(dateRoot, "summary.json"), true, 0);
            string historyJson = Path.Combine(root, "history.json");
            string historyMarkdown = Path.Combine(root, "history.md");

            int exitCode = Program.Main(new[] { "--summarize-baseline-history", root, "--history", historyJson, "--history-md", historyMarkdown });

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(historyJson));
            Assert.True(File.Exists(historyMarkdown));
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(historyJson)))
            {
                Assert.True(document.RootElement.GetProperty("hard-passed").GetBoolean());
                Assert.Equal(1, document.RootElement.GetProperty("session-count").GetInt32());
            }
        }

        // hard failure 는 기존 summary 의 delivery/drop/leak gate 결과를 aggregate 한다.
        // warning 은 soft signal 이지만 hard-passed false summary 가 하나라도 있으면 exit code 1이어야 한다.
        [Fact]
        public void Main_WhenHistoryCommandHasFailedSummary_ReturnsFailedRunExitCode()
        {
            string root = CreateTempDirectory("baselines");
            string dateRoot = Path.Combine(root, "2026-06-18");
            Directory.CreateDirectory(dateRoot);
            WriteSummary(Path.Combine(dateRoot, "summary.json"), false, 0);
            string historyJson = Path.Combine(root, "history.json");

            int exitCode = Program.Main(new[] { "--summarize-baseline-history", root, "--history", historyJson });

            Assert.Equal(1, exitCode);
            Assert.True(File.Exists(historyJson));
        }

        // warning 은 D078 기준 soft signal 이다.
        // warning-count 가 있어도 hard-passed 가 true 이면 command 는 성공 exit code 를 유지해야 한다.
        [Fact]
        public void Main_WhenHistoryCommandHasWarningsOnly_ReturnsSuccess()
        {
            string root = CreateTempDirectory("baselines");
            string dateRoot = Path.Combine(root, "2026-06-18");
            Directory.CreateDirectory(dateRoot);
            WriteSummary(Path.Combine(dateRoot, "summary.json"), true, 2);
            string historyJson = Path.Combine(root, "history.json");

            int exitCode = Program.Main(new[] { "--summarize-baseline-history", root, "--history", historyJson });

            Assert.Equal(0, exitCode);
        }

        private static string CreateTempDirectory(string leafName)
        {
            string directory = Path.Combine(Path.GetTempPath(), "hps-baseline-history-program-tests", Path.GetRandomFileName(), leafName);
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void WriteSummary(string path, bool hardPassed, int warningCount)
        {
            string json = "{"
                + "\"summary-version\":1,"
                + "\"source-directory\":\"source\","
                + "\"source-report-count\":6,"
                + "\"hard-passed\":" + (hardPassed ? "true" : "false") + ","
                + "\"hard-failure-count\":" + (hardPassed ? "0" : "1") + ","
                + "\"warning-count\":" + warningCount.ToString(CultureInfo.InvariantCulture) + ","
                + "\"warnings\":[],"
                + "\"by-kind\":{"
                + "\"load\":{\"p99-max-us\":924.1,\"tcp-hwm-max\":2},"
                + "\"open-loop\":{\"p99-max-us\":1005.5,\"tcp-hwm-max\":3}"
                + "}"
                + "}";
            File.WriteAllText(path, json);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineHistoryProgramTests
```

Expected: assertion failure because `Program.Main` has no `SummarizeBaselineHistory` switch branch yet and returns usage error instead of writing files.

- [ ] **Step 3: Wire Program execution**

Add switch branch in `Program.Main`:

```csharp
                case BenchmarkCommand.SummarizeBaselineHistory:
                    return CompleteBaselineHistory(commandLine.HistoryInputRoot!, commandLine.HistoryOutputPath!, commandLine.HistoryMarkdownOutputPath);
```

Add method in `Program`:

```csharp
        private static int CompleteBaselineHistory(string inputRoot, string historyPath, string? historyMarkdownPath)
        {
            try
            {
                System.Collections.Generic.IReadOnlyList<BaselineHistorySession> sessions = BaselineHistoryReader.ReadSessions(inputRoot);
                BaselineHistory history = BaselineHistoryGenerator.Generate(inputRoot, sessions);
                BaselineHistoryWriter.Write(historyPath, history);
                if (historyMarkdownPath != null)
                    WriteBaselineHistoryMarkdown(historyMarkdownPath, history);

                Console.Out.WriteLine("baseline-history: {0}", historyPath);
                if (historyMarkdownPath != null)
                    Console.Out.WriteLine("baseline-history-md: {0}", historyMarkdownPath);

                Console.Out.WriteLine("session-count: {0}", history.SessionCount);
                Console.Out.WriteLine("hard-passed: {0}", history.HardPassed ? "true" : "false");
                Console.Out.WriteLine("warning-count: {0}", history.WarningCount);
                return history.HardPassed ? SuccessExitCode : FailedRunExitCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("baseline-history-error: {0}", ex.Message);
                return ReportWriteFailedExitCode;
            }
        }
```

Add Markdown file helper:

```csharp
        private static void WriteBaselineHistoryMarkdown(string path, BaselineHistory history)
        {
            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (StreamWriter writer = new StreamWriter(fullPath, false))
            {
                BaselineHistoryMarkdownWriter.Write(writer, history);
            }
        }
```

- [ ] **Step 4: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineHistoryProgramTests
```

Expected: Program history tests pass.

- [ ] **Step 5: Run CLI smoke without committing generated artifacts**

Run:

```powershell
$historyJson = Join-Path $env:TEMP "hps-baseline-history.json"
$historyMd = Join-Path $env:TEMP "hps-baseline-history.md"
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --summarize-baseline-history docs\benchmarks\baselines --history $historyJson --history-md $historyMd
```

Expected:

```text
baseline-history: <temp>\hps-baseline-history.json
baseline-history-md: <temp>\hps-baseline-history.md
session-count: 3
hard-passed: true
warning-count: 0
```

Do not commit `$historyJson` or `$historyMd`.

- [ ] **Step 6: Run standard verification and commit**

Run:

```powershell
git diff --check
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
```

Expected: whitespace check exit 0, build 경고 0/오류 0, all tests pass.

Commit only Task 4 files:

```powershell
git add tests\Hps.Benchmarks\Program.cs tests\Hps.Benchmarks.Tests\BaselineHistoryProgramTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: wire baseline history command"
```

---

## Self-Review

- Spec coverage: D078 command name, parent root/date root discovery, JSON/Markdown output, exit code policy, warning soft signal, and no `index.md` overwrite are each mapped to a task.
- Placeholder scan: no unresolved placeholder text remains. File paths and method names are concrete.
- Type consistency: Task 1 produces `SummarizeBaselineHistory` and history path properties; Task 4 consumes those exact names. Task 2 produces `BaselineHistorySession` and `BaselineHistoryReader.ReadSessions`; Task 3 and Task 4 consume those exact names.
- Scope check: CI workflow, warning-as-failure, latency hard gate, runner identity metadata, and baseline regression 판정 are intentionally excluded.
- Commit boundary check: each Task ends with its own verification and commit command, and no Task stages unrelated files.
