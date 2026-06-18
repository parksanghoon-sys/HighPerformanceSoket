# Repeat Baseline Collection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `tests/Hps.Benchmarks`에 반복 baseline 수집 command 를 추가해 `--load`와 `--load-open-loop` raw JSON report 를 같은 session directory 에 재현 가능하게 남긴다.

**Architecture:** 기존 단일 runner command 는 유지하고, 새 `--baseline-suite <output-dir> [--runs <count>]` command 를 추가한다. CLI parser 와 suite runner 를 작은 internal class 로 분리해 xUnit 에서 빠르게 검증하고, 실제 30초 benchmark 실행은 최종 wiring smoke 로만 확인한다.

**Tech Stack:** .NET 9, C# 8, xUnit, System.Text.Json, 기존 `TcpLoopbackRunResult`/`TcpLoopbackReportWriter` 재사용.

---

## Scope

이번 구현은 D069의 첫 실행 단위다. raw JSON artifact 수집만 만든다.

포함한다:

- `--baseline-suite <output-dir> [--runs <count>]` CLI command.
- 기본 run count 는 3이다.
- 각 run 은 `load-01.json`, `load-02.json`, `open-loop-01.json`, `open-loop-02.json` 형식으로 저장한다.
- run count 가 10 이상이면 index 폭을 run count 자리수에 맞춘다.
- 반환 코드는 모든 per-run result 가 `Passed == true`일 때 0, 하나라도 실패하면 1이다.
- parser/runner 는 단위 테스트로 검증한다.

제외한다:

- p50/p99 hard threshold.
- soft warning 산출.
- summary JSON 또는 Markdown report.
- CI provider workflow.
- BenchmarkDotNet microbenchmark 실행 방식 변경.

## File Structure

- Create: `tests/Hps.Benchmarks.Tests/Hps.Benchmarks.Tests.csproj`
  - benchmark CLI parser 와 baseline suite runner 를 테스트하는 xUnit project.
- Create: `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`
  - 기존 command 와 새 baseline suite command parsing 을 검증한다.
- Create: `tests/Hps.Benchmarks.Tests/BaselineSuiteRunnerTests.cs`
  - fake benchmark runner 와 fake report writer 로 파일명, 실행 순서, 반환 코드를 검증한다.
- Create: `tests/Hps.Benchmarks/Properties/AssemblyInfo.cs`
  - `Hps.Benchmarks.Tests`에 internal test access 를 허용한다.
- Create: `tests/Hps.Benchmarks/BenchmarkCommand.cs`
  - 기존 `Program` nested enum 을 internal enum 으로 이동한다.
- Create: `tests/Hps.Benchmarks/BenchmarkCommandLine.cs`
  - parser 결과 값을 담는다.
- Create: `tests/Hps.Benchmarks/BenchmarkCommandParser.cs`
  - `Program`의 args parsing 을 분리한다.
- Create: `tests/Hps.Benchmarks/BaselineRunKind.cs`
  - suite runner 가 closed-loop load 와 open-loop load 를 구분한다.
- Create: `tests/Hps.Benchmarks/BaselineSuiteRunner.cs`
  - 반복 실행, report path 생성, pass/fail 집계를 담당한다.
- Modify: `tests/Hps.Benchmarks/Program.cs`
  - parser/runner 를 조립하고 usage 에 baseline suite command 를 추가한다.
- Modify: `HighPerformanceSocket.slnx`
  - `tests/Hps.Benchmarks.Tests` project 를 solution 에 포함한다.
- Modify: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`
  - 각 task 완료 시 상태와 검증 결과를 기록한다.

---

### Task 1: Benchmark CLI Test Seam And Existing Parser Extraction

**Files:**
- Create: `tests/Hps.Benchmarks.Tests/Hps.Benchmarks.Tests.csproj`
- Create: `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`
- Create: `tests/Hps.Benchmarks/Properties/AssemblyInfo.cs`
- Create: `tests/Hps.Benchmarks/BenchmarkCommand.cs`
- Create: `tests/Hps.Benchmarks/BenchmarkCommandLine.cs`
- Create: `tests/Hps.Benchmarks/BenchmarkCommandParser.cs`
- Modify: `tests/Hps.Benchmarks/Program.cs`
- Modify: `HighPerformanceSocket.slnx`

- [ ] **Step 1: Add the benchmark test project**

Create `tests/Hps.Benchmarks.Tests/Hps.Benchmarks.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- 공통 TFM/LangVersion 설정은 루트 Directory.Build.props 를 따른다. -->
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Hps.Benchmarks\Hps.Benchmarks.csproj" />
  </ItemGroup>

</Project>
```

Create `tests/Hps.Benchmarks/Properties/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Hps.Benchmarks.Tests")]
```

Add this project to `HighPerformanceSocket.slnx` under `/tests/`:

```xml
    <Project Path="tests/Hps.Benchmarks.Tests/Hps.Benchmarks.Tests.csproj" />
```

- [ ] **Step 2: Write the bootstrap Red test**

Create `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`:

```csharp
using System;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BenchmarkCommandParserTests
    {
        // parser 를 Program 에서 분리하기 전 Red 용 bootstrap 검사다.
        // 새 타입을 직접 참조하면 컴파일 실패가 되어 TDD Red 신호가 흐려지므로,
        // 첫 실패는 "아직 parser seam 이 없다"는 assertion 실패로만 만든다.
        [Fact]
        public void BenchmarkCommandParser_TypeExists()
        {
            Type? parserType = Type.GetType("Hps.Benchmarks.BenchmarkCommandParser, Hps.Benchmarks");
            Assert.NotNull(parserType);
        }
    }
}
```

- [ ] **Step 3: Run the focused Red check**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore
```

Expected: fail with `Assert.NotNull(parserType)` because `Type.GetType("Hps.Benchmarks.BenchmarkCommandParser, Hps.Benchmarks")` returns null before parser extraction.

- [ ] **Step 4: Extract parser without behavior change**

Create `tests/Hps.Benchmarks/BenchmarkCommand.cs`:

```csharp
namespace Hps.Benchmarks
{
    internal enum BenchmarkCommand
    {
        None,
        Target,
        Smoke,
        Load,
        LoadOpenLoop,
        BaselineSuite,
        Help
    }
}
```

Create `tests/Hps.Benchmarks/BenchmarkCommandLine.cs`:

```csharp
namespace Hps.Benchmarks
{
    internal sealed class BenchmarkCommandLine
    {
        public BenchmarkCommandLine(
            BenchmarkCommand command,
            string? reportPath,
            string? baselineOutputDirectory,
            int baselineRunCount)
        {
            Command = command;
            ReportPath = reportPath;
            BaselineOutputDirectory = baselineOutputDirectory;
            BaselineRunCount = baselineRunCount;
        }

        public BenchmarkCommand Command { get; }

        public string? ReportPath { get; }

        public string? BaselineOutputDirectory { get; }

        public int BaselineRunCount { get; }
    }
}
```

Create `tests/Hps.Benchmarks/BenchmarkCommandParser.cs` with the existing `Program` parsing behavior moved as-is:

```csharp
using System;

namespace Hps.Benchmarks
{
    internal static class BenchmarkCommandParser
    {
        public const string MessageReportOnlyWithRuns = "--report 옵션은 --smoke, --load, --load-open-loop 뒤에서만 사용할 수 있습니다.";
        public const string MessageUnknownRunnerArgs = "알 수 없는 benchmark runner 인자입니다.";
        public const string MessageReportPathRequired = "--report 옵션에는 저장할 파일 경로가 필요합니다.";
        public const string MessageReportExecutionOnly = "--report 옵션은 benchmark 실행 명령에서만 사용할 수 있습니다.";

        public static bool TryParse(string[] args, out BenchmarkCommandLine commandLine, out string? errorMessage)
        {
            commandLine = new BenchmarkCommandLine(BenchmarkCommand.None, null, null, 0);
            errorMessage = null;

            if (args.Length == 0)
                return false;

            string commandArg = args[0];

            if (string.Equals(commandArg, "--help", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = ValidateNoReportOption(args);
                commandLine = new BenchmarkCommandLine(BenchmarkCommand.Help, null, null, 0);
                return true;
            }

            if (string.Equals(commandArg, "--target", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = ValidateNoReportOption(args);
                commandLine = new BenchmarkCommandLine(BenchmarkCommand.Target, null, null, 0);
                return true;
            }

            if (string.Equals(commandArg, "--smoke", StringComparison.OrdinalIgnoreCase))
                return ParseRunner(args, BenchmarkCommand.Smoke, out commandLine, out errorMessage);

            if (string.Equals(commandArg, "--load", StringComparison.OrdinalIgnoreCase))
                return ParseRunner(args, BenchmarkCommand.Load, out commandLine, out errorMessage);

            if (string.Equals(commandArg, "--load-open-loop", StringComparison.OrdinalIgnoreCase))
                return ParseRunner(args, BenchmarkCommand.LoadOpenLoop, out commandLine, out errorMessage);

            if (ContainsReportOption(args))
            {
                errorMessage = MessageReportOnlyWithRuns;
                return true;
            }

            return false;
        }

        private static bool ParseRunner(
            string[] args,
            BenchmarkCommand command,
            out BenchmarkCommandLine commandLine,
            out string? errorMessage)
        {
            string? reportPath;
            ParseOptionalReport(args, out reportPath, out errorMessage);
            commandLine = new BenchmarkCommandLine(command, reportPath, null, 0);
            return true;
        }

        private static void ParseOptionalReport(string[] args, out string? reportPath, out string? errorMessage)
        {
            reportPath = null;
            errorMessage = null;

            if (args.Length == 1)
                return;

            if (args.Length != 3 || !string.Equals(args[1], "--report", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = MessageUnknownRunnerArgs;
                return;
            }

            if (string.IsNullOrWhiteSpace(args[2]))
                errorMessage = MessageReportPathRequired;
            else
                reportPath = args[2];
        }

        private static string? ValidateNoReportOption(string[] args)
        {
            if (args.Length == 1)
                return null;

            if (ContainsReportOption(args))
                return MessageReportExecutionOnly;

            return MessageUnknownRunnerArgs;
        }

        private static bool ContainsReportOption(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--report", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
```

Modify `Program.cs` to call `BenchmarkCommandParser.TryParse(args, out commandLine, out errorMessage)` and remove the duplicated nested enum/parser helpers.

- [ ] **Step 5: Replace the bootstrap test with direct behavior tests**

Replace `BenchmarkCommandParserTests.cs`:

```csharp
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BenchmarkCommandParserTests
    {
        // 기존 --load --report 계약을 먼저 고정한다.
        // baseline suite 추가 중 기존 단일 runner command 가 BenchmarkDotNet fallback 으로 밀려나면
        // report artifact 생성 경로가 조용히 깨질 수 있다.
        [Fact]
        public void TryParse_WhenLoadHasReport_ReturnsLoadCommandWithReportPath()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--load", "--report", "out/load.json" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.Null(errorMessage);
            Assert.Equal(BenchmarkCommand.Load, commandLine.Command);
            Assert.Equal("out/load.json", commandLine.ReportPath);
            Assert.Null(commandLine.BaselineOutputDirectory);
            Assert.Equal(0, commandLine.BaselineRunCount);
        }

        // --report 단독 사용은 실행할 runner 가 없으므로 usage error 로 남아야 한다.
        // 이 경계가 풀리면 사용자가 report 를 기대했는데 BenchmarkDotNet 인자로 해석될 수 있다.
        [Fact]
        public void TryParse_WhenReportHasNoRunner_ReturnsUsageError()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--report", "out/load.json" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.NotNull(errorMessage);
            Assert.Equal(BenchmarkCommand.None, commandLine.Command);
        }
    }
}
```

- [ ] **Step 6: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore
```

Expected: pass with 2 tests.

- [ ] **Step 7: Run solution build/test**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
```

Expected: build warning 0/error 0 and all tests pass with the new benchmark tests included.

- [ ] **Step 8: Update state docs and commit**

Update:

- `CURRENT_PLAN.md`
- `TODOS.md`
- `CHANGELOG_AGENT.md`

Commit:

```powershell
git add -- tests/Hps.Benchmarks.Tests tests/Hps.Benchmarks/Properties/AssemblyInfo.cs tests/Hps.Benchmarks/BenchmarkCommand.cs tests/Hps.Benchmarks/BenchmarkCommandLine.cs tests/Hps.Benchmarks/BenchmarkCommandParser.cs tests/Hps.Benchmarks/Program.cs HighPerformanceSocket.slnx CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "test: cover benchmark command parsing"
```

---

### Task 2: Parse Baseline Suite Command

**Files:**
- Modify: `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`
- Modify: `tests/Hps.Benchmarks/BenchmarkCommandParser.cs`
- Modify: `tests/Hps.Benchmarks/Program.cs`

- [ ] **Step 1: Add failing parser tests for baseline suite**

Add to `BenchmarkCommandParserTests.cs`:

```csharp
        // 반복 baseline command 는 단일 --report 파일이 아니라 directory 단위 artifact 를 만든다.
        // run count 를 명시하면 다음 단계의 suite runner 가 같은 횟수로 load/open-loop 를 모두 실행해야 한다.
        [Fact]
        public void TryParse_WhenBaselineSuiteHasOutputAndRuns_ReturnsBaselineSuiteCommand()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--baseline-suite", "out/baseline", "--runs", "2" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.Null(errorMessage);
            Assert.Equal(BenchmarkCommand.BaselineSuite, commandLine.Command);
            Assert.Equal("out/baseline", commandLine.BaselineOutputDirectory);
            Assert.Equal(2, commandLine.BaselineRunCount);
            Assert.Null(commandLine.ReportPath);
        }

        // run count 를 생략하면 D069 기준 최소 session 수집 단위인 3회를 기본값으로 사용한다.
        // 이 기본값이 바뀌면 로컬/CI baseline 비교 단위가 흔들리므로 parser 에서 고정한다.
        [Fact]
        public void TryParse_WhenBaselineSuiteHasNoRuns_UsesDefaultRunCount()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--baseline-suite", "out/baseline" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.Null(errorMessage);
            Assert.Equal(BenchmarkCommand.BaselineSuite, commandLine.Command);
            Assert.Equal(3, commandLine.BaselineRunCount);
        }

        // baseline suite 는 directory 안에 per-run JSON을 직접 만든다.
        // --report 와 섞으면 단일 파일 report 인지 suite directory 인지 불명확해지므로 usage error 로 막는다.
        [Fact]
        public void TryParse_WhenBaselineSuiteHasReport_ReturnsUsageError()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--baseline-suite", "out/baseline", "--report", "out.json" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.NotNull(errorMessage);
            Assert.Equal(BenchmarkCommand.BaselineSuite, commandLine.Command);
        }
```

- [ ] **Step 2: Run Red**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore --filter BenchmarkCommandParserTests
```

Expected: baseline-suite tests fail because parser does not recognize `--baseline-suite`.

- [ ] **Step 3: Add parser support**

Add constants and parsing branch to `BenchmarkCommandParser.cs`:

```csharp
        public const int DefaultBaselineRunCount = 3;
        public const string MessageBaselineOutputRequired = "--baseline-suite 옵션에는 report directory 경로가 필요합니다.";
        public const string MessageBaselineRunsInvalid = "--runs 옵션에는 1 이상의 정수가 필요합니다.";
        public const string MessageBaselineReportNotAllowed = "--report 옵션은 --baseline-suite 와 함께 사용할 수 없습니다.";
```

Add before the `--smoke` branch:

```csharp
            if (string.Equals(commandArg, "--baseline-suite", StringComparison.OrdinalIgnoreCase))
                return ParseBaselineSuite(args, out commandLine, out errorMessage);
```

Add helper:

```csharp
        private static bool ParseBaselineSuite(
            string[] args,
            out BenchmarkCommandLine commandLine,
            out string? errorMessage)
        {
            commandLine = new BenchmarkCommandLine(BenchmarkCommand.BaselineSuite, null, null, DefaultBaselineRunCount);
            errorMessage = null;

            if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
            {
                errorMessage = MessageBaselineOutputRequired;
                return true;
            }

            if (ContainsReportOption(args))
            {
                commandLine = new BenchmarkCommandLine(BenchmarkCommand.BaselineSuite, null, args[1], DefaultBaselineRunCount);
                errorMessage = MessageBaselineReportNotAllowed;
                return true;
            }

            int runCount = DefaultBaselineRunCount;
            if (args.Length == 4 && string.Equals(args[2], "--runs", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(args[3], out runCount) || runCount <= 0)
                    errorMessage = MessageBaselineRunsInvalid;
            }
            else if (args.Length != 2)
            {
                errorMessage = MessageUnknownRunnerArgs;
            }

            commandLine = new BenchmarkCommandLine(BenchmarkCommand.BaselineSuite, null, args[1], runCount);
            return true;
        }
```

Update `Program.PrintUsage`:

```csharp
            writer.WriteLine("  Hps.Benchmarks --baseline-suite <output-dir> [--runs <count>]");
```

- [ ] **Step 4: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore --filter BenchmarkCommandParserTests
```

Expected: all parser tests pass.

- [ ] **Step 5: Commit**

Run `git diff --check`, update state docs, then commit:

```powershell
git add -- tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs tests/Hps.Benchmarks/BenchmarkCommandParser.cs tests/Hps.Benchmarks/Program.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: parse repeat baseline command"
```

---

### Task 3: Baseline Suite Runner

**Files:**
- Create: `tests/Hps.Benchmarks/BaselineRunKind.cs`
- Create: `tests/Hps.Benchmarks/BaselineSuiteRunner.cs`
- Create: `tests/Hps.Benchmarks.Tests/BaselineSuiteRunnerTests.cs`

- [ ] **Step 1: Add the bootstrap Red test**

Create `tests/Hps.Benchmarks.Tests/BaselineSuiteRunnerTests.cs`:

```csharp
using System;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineSuiteRunnerTests
    {
        // runner 구현 전 Red 용 bootstrap 검사다.
        // 직접 타입 참조로 컴파일 실패를 만들지 않고, 아직 runner seam 이 없다는 assertion 실패만 확인한다.
        [Fact]
        public void BaselineSuiteRunner_TypeExists()
        {
            Type? runnerType = Type.GetType("Hps.Benchmarks.BaselineSuiteRunner, Hps.Benchmarks");
            Assert.NotNull(runnerType);
        }
    }
}
```

- [ ] **Step 2: Run Red**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore --filter BaselineSuiteRunnerTests
```

Expected: fail with `Assert.NotNull(runnerType)` because `Type.GetType("Hps.Benchmarks.BaselineSuiteRunner, Hps.Benchmarks")` returns null before runner extraction.

- [ ] **Step 3: Add runner implementation**

Create `tests/Hps.Benchmarks/BaselineRunKind.cs`:

```csharp
namespace Hps.Benchmarks
{
    internal enum BaselineRunKind
    {
        Load,
        OpenLoop
    }
}
```

Create `tests/Hps.Benchmarks/BaselineSuiteRunner.cs`:

```csharp
using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace Hps.Benchmarks
{
    internal sealed class BaselineSuiteRunner
    {
        private readonly Func<BaselineRunKind, Task<TcpLoopbackRunResult>> _runAsync;
        private readonly Action<string, TcpLoopbackRunResult> _writeReport;

        public BaselineSuiteRunner(
            Func<BaselineRunKind, Task<TcpLoopbackRunResult>> runAsync,
            Action<string, TcpLoopbackRunResult> writeReport)
        {
            if (runAsync == null)
                throw new ArgumentNullException(nameof(runAsync));

            if (writeReport == null)
                throw new ArgumentNullException(nameof(writeReport));

            _runAsync = runAsync;
            _writeReport = writeReport;
        }

        public async Task<bool> RunAsync(string outputDirectory, int runCount, TextWriter writer)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("baseline output directory 는 비어 있을 수 없습니다.", nameof(outputDirectory));

            if (runCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(runCount));

            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            bool allPassed = true;
            int padWidth = Math.Max(2, runCount.ToString(CultureInfo.InvariantCulture).Length);

            for (int index = 1; index <= runCount; index++)
            {
                TcpLoopbackRunResult load = await _runAsync(BaselineRunKind.Load).ConfigureAwait(false);
                allPassed &= load.Passed;
                string loadPath = BuildReportPath(outputDirectory, "load", index, padWidth);
                _writeReport(loadPath, load);
                writer.WriteLine("baseline-report: {0}", loadPath);

                TcpLoopbackRunResult openLoop = await _runAsync(BaselineRunKind.OpenLoop).ConfigureAwait(false);
                allPassed &= openLoop.Passed;
                string openLoopPath = BuildReportPath(outputDirectory, "open-loop", index, padWidth);
                _writeReport(openLoopPath, openLoop);
                writer.WriteLine("baseline-report: {0}", openLoopPath);
            }

            writer.WriteLine("baseline-suite-result: {0}", allPassed ? "pass" : "fail");
            return allPassed;
        }

        private static string BuildReportPath(string outputDirectory, string name, int index, int padWidth)
        {
            string fileName = string.Format(
                CultureInfo.InvariantCulture,
                "{0}-{1}.json",
                name,
                index.ToString("D" + padWidth.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));

            return Path.Combine(outputDirectory, fileName).Replace('\\', '/');
        }
    }
}
```

- [ ] **Step 4: Replace the bootstrap test with direct runner behavior tests**

Replace `BaselineSuiteRunnerTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineSuiteRunnerTests
    {
        // runner 는 raw JSON artifact 를 대체하지 않고 load/open-loop 각각의 per-run 파일을 남긴다.
        // 이 테스트는 파일 이름과 실행 순서가 D069 baseline session 정의와 일치하는지 고정한다.
        [Fact]
        public async Task RunAsync_WhenTwoRunsRequested_WritesLoadAndOpenLoopReports()
        {
            List<BaselineRunKind> kinds = new List<BaselineRunKind>();
            List<string> paths = new List<string>();
            BaselineSuiteRunner runner = new BaselineSuiteRunner(
                kind =>
                {
                    kinds.Add(kind);
                    return Task.FromResult(CreatePassingResult(kind));
                },
                (path, result) => paths.Add(path));

            bool passed = await runner.RunAsync("out/baseline", 2, TextWriter.Null);

            Assert.True(passed);
            Assert.Equal(
                new[] { BaselineRunKind.Load, BaselineRunKind.OpenLoop, BaselineRunKind.Load, BaselineRunKind.OpenLoop },
                kinds.ToArray());
            Assert.Equal(
                new[] { "out/baseline/load-01.json", "out/baseline/open-loop-01.json", "out/baseline/load-02.json", "out/baseline/open-loop-02.json" },
                paths.ToArray());
        }

        // 하나라도 delivery/drop/leak gate 를 통과하지 못하면 suite 전체 exit code 가 실패로 전파되어야 한다.
        // latency 값은 아직 hard gate 가 아니므로 Passed 값만 집계한다.
        [Fact]
        public async Task RunAsync_WhenAnyRunFails_ReturnsFalse()
        {
            int callIndex = 0;
            BaselineSuiteRunner runner = new BaselineSuiteRunner(
                kind =>
                {
                    callIndex++;
                    if (callIndex == 2)
                        return Task.FromResult(CreateFailingResult(kind));

                    return Task.FromResult(CreatePassingResult(kind));
                },
                (path, result) => { });

            bool passed = await runner.RunAsync("out/baseline", 1, TextWriter.Null);

            Assert.False(passed);
        }

        private static TcpLoopbackRunResult CreatePassingResult(BaselineRunKind kind)
        {
            string resultName = kind == BaselineRunKind.Load ? "load" : "open-loop";
            return new TcpLoopbackRunResult(resultName, resultName, 4096, 100, 30, 1, 1, 1, 0, 1, 0, 0, 0, 10, 20, 20, 20, 1000);
        }

        private static TcpLoopbackRunResult CreateFailingResult(BaselineRunKind kind)
        {
            string resultName = kind == BaselineRunKind.Load ? "load" : "open-loop";
            return new TcpLoopbackRunResult(resultName, resultName, 4096, 100, 30, 1, 1, 0, 0, 1, 0, 0, 0, 10, 20, 20, 20, 1000);
        }
    }
}
```

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore --filter BaselineSuiteRunnerTests
```

Expected: pass.

- [ ] **Step 6: Commit**

Run `git diff --check`, update state docs, then commit:

```powershell
git add -- tests/Hps.Benchmarks/BaselineRunKind.cs tests/Hps.Benchmarks/BaselineSuiteRunner.cs tests/Hps.Benchmarks.Tests/BaselineSuiteRunnerTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: collect repeat baseline reports"
```

---

### Task 4: Program Wiring And CLI Verification

**Files:**
- Modify: `tests/Hps.Benchmarks/Program.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

- [ ] **Step 1: Run CLI Red**

Run:

```powershell
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build -- --baseline-suite artifacts\baseline-red --runs 1
```

Expected: exit code 2 and no `artifacts\baseline-red\load-01.json` file because `Program` parses the command but has not wired execution yet.

- [ ] **Step 2: Wire baseline suite command in Program**

In `Program.Main`, use `BenchmarkCommandLine`:

```csharp
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            if (!BenchmarkCommandParser.TryParse(args, out commandLine, out errorMessage))
            {
                BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
                return SuccessExitCode;
            }

            if (errorMessage != null)
            {
                Console.Error.WriteLine(errorMessage);
                PrintUsage(Console.Error);
                return UsageErrorExitCode;
            }
```

Add switch case:

```csharp
                case BenchmarkCommand.BaselineSuite:
                    return CompleteBaselineSuite(commandLine.BaselineOutputDirectory!, commandLine.BaselineRunCount);
```

Add helper:

```csharp
        private static int CompleteBaselineSuite(string outputDirectory, int runCount)
        {
            BaselineSuiteRunner runner = new BaselineSuiteRunner(
                kind =>
                {
                    if (kind == BaselineRunKind.Load)
                        return TcpLoopbackScenarioRunner.RunLoadAsync();

                    return TcpLoopbackScenarioRunner.RunOpenLoopAsync();
                },
                TcpLoopbackReportWriter.Write);

            bool passed = runner.RunAsync(outputDirectory, runCount, Console.Out).GetAwaiter().GetResult();
            return passed ? SuccessExitCode : FailedRunExitCode;
        }
```

Update usage:

```csharp
            writer.WriteLine("  Hps.Benchmarks --baseline-suite <output-dir> [--runs <count>]");
```

- [ ] **Step 3: Run solution tests**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
```

Expected: build warning 0/error 0 and all tests pass.

- [ ] **Step 4: Run CLI verification**

Use a temporary output directory:

```powershell
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build -- --baseline-suite artifacts\baseline-smoke --runs 1
```

Expected:

- exit code 0
- `artifacts\baseline-smoke\load-01.json` exists
- `artifacts\baseline-smoke\open-loop-01.json` exists
- both JSON files have `"schema-version": 1`
- both JSON files have `"passed": true`

This command runs one closed-loop load and one open-loop load, so it can take about 60 seconds plus startup time.

- [ ] **Step 5: Commit**

Run `git diff --check`, update state docs, then commit:

```powershell
git add -- tests/Hps.Benchmarks/Program.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: wire repeat baseline cli"
```

---

## Final Verification

After Task 4:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build -- --baseline-suite artifacts\baseline-smoke --runs 1
git diff --check
```

Expected final state:

- new benchmark tests are discoverable and passing.
- existing solution tests remain green.
- baseline suite writes per-run raw JSON artifacts.
- no latency threshold is used as pass/fail.
- `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md` point to the next review gate.

## Self-Review

- Spec coverage: D069의 raw JSON artifact 축적, hard latency gate 보류, 기존 schema v1 재사용 요구를 Task 2~4가 다룬다.
- Scope check: summary JSON, Markdown report, CI provider workflow, latency threshold 는 제외 범위로 남겼다.
- Type consistency: `BenchmarkCommand`, `BenchmarkCommandLine`, `BaselineRunKind`, `BaselineSuiteRunner` 이름은 모든 task 에서 동일하다.
