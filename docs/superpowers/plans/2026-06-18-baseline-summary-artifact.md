# Baseline Summary Artifact Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 기존 per-run benchmark JSON directory 를 읽어 hard gate 결과와 soft warning 후보를 `summary.json`으로 저장하는 `--summarize-baseline <input-dir> --summary <output-json>` command 를 추가한다.

**Architecture:** 기존 `tests/Hps.Benchmarks` CLI/parser/report writer 구조를 확장한다. per-run JSON schema v1은 그대로 두고, summary 계산은 `BaselineSummaryGenerator`, 파일 입력은 `BaselineReportReader`, 출력은 `BaselineSummaryWriter`로 분리해 빠른 xUnit 테스트와 실제 CLI smoke 를 모두 가능하게 한다.

**Tech Stack:** .NET 9, C# 8, xUnit, `System.Text.Json`, 기존 `BenchmarkCommandParser`/`TcpLoopbackReportWriter` 패턴 재사용.

---

## Scope

포함한다:

- `--summarize-baseline <input-dir> --summary <output-json>` CLI parser.
- per-run JSON v1 파일 읽기.
- summary JSON v1 쓰기.
- D070 기준 hard gate 집계.
- D070 기준 non-failing soft warning 산출.
- fake JSON 기반 xUnit 테스트.
- 실제 baseline artifact directory 를 입력으로 쓰는 CLI smoke.

제외한다:

- Markdown report.
- CI provider workflow.
- warning 을 exit code 실패로 승격하는 정책.
- p50/p99 hard latency threshold.
- 기존 per-run JSON schema v1 변경.
- benchmark 실행 로직 변경.

## File Structure

- Modify: `tests/Hps.Benchmarks/BenchmarkCommand.cs`
  - `SummarizeBaseline` command 값을 추가한다.
- Modify: `tests/Hps.Benchmarks/BenchmarkCommandLine.cs`
  - summary input/output path 를 담는 property 를 추가한다.
- Modify: `tests/Hps.Benchmarks/BenchmarkCommandParser.cs`
  - `--summarize-baseline <input-dir> --summary <output-json>`를 해석한다.
- Create: `tests/Hps.Benchmarks/BaselineReport.cs`
  - per-run JSON의 최소 분석 필드를 담고 hard gate 를 재계산한다.
- Create: `tests/Hps.Benchmarks/BaselineKindSummary.cs`
  - `load`와 `open-loop`별 min/max 집계 값을 담는다.
- Create: `tests/Hps.Benchmarks/BaselineWarning.cs`
  - soft warning code, kind, metric, value, threshold 를 담는다.
- Create: `tests/Hps.Benchmarks/BaselineSummary.cs`
  - 전체 summary 결과 object 를 담는다.
- Create: `tests/Hps.Benchmarks/BaselineSummaryGenerator.cs`
  - `IEnumerable<BaselineReport>`를 summary 로 변환한다.
- Create: `tests/Hps.Benchmarks/BaselineReportReader.cs`
  - directory 안의 per-run JSON v1 파일을 읽는다.
- Create: `tests/Hps.Benchmarks/BaselineSummaryWriter.cs`
  - summary JSON v1 파일을 쓴다.
- Modify: `tests/Hps.Benchmarks/Program.cs`
  - summary command 를 실행 경로에 연결하고 usage 를 갱신한다.
- Modify: `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`
  - summary parser 계약을 검증한다.
- Create: `tests/Hps.Benchmarks.Tests/BaselineSummaryGeneratorTests.cs`
  - hard pass/fail, kind별 min/max, soft warning 을 검증한다.
- Create: `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`
  - fake JSON file set 으로 reader/writer 를 검증한다.
- Modify: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`
  - 각 task 완료 후 현재 실행 지점, 완료 이력, 검증 결과를 갱신한다.

---

### Task 1: Summary CLI Parser Contract

**Files:**
- Modify: `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`
- Modify: `tests/Hps.Benchmarks/BenchmarkCommand.cs`
- Modify: `tests/Hps.Benchmarks/BenchmarkCommandLine.cs`
- Modify: `tests/Hps.Benchmarks/BenchmarkCommandParser.cs`
- Modify: `tests/Hps.Benchmarks/Program.cs`
- Modify: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`

- [ ] **Step 1: Write the failing parser tests**

Append to `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`:

```csharp
using System.Reflection;
```

Add these tests inside `BenchmarkCommandParserTests`:

```csharp
        // summary command 는 기존 per-run JSON directory 를 입력으로 받고 별도 summary JSON 파일을 출력한다.
        // 아직 Program wiring 전이라도 parser 가 command 와 두 경로를 정확히 보존해야 이후 실행 단위가 흔들리지 않는다.
        [Fact]
        public void TryParse_WhenSummarizeBaselineHasInputAndSummary_ReturnsSummaryCommand()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--summarize-baseline", "docs/baseline", "--summary", "out/summary.json" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.Null(errorMessage);
            Assert.Equal("SummarizeBaseline", commandLine.Command.ToString());
            Assert.Equal("docs/baseline", GetStringProperty(commandLine, "SummaryInputDirectory"));
            Assert.Equal("out/summary.json", GetStringProperty(commandLine, "SummaryOutputPath"));
            Assert.Null(commandLine.ReportPath);
            Assert.Null(commandLine.BaselineOutputDirectory);
            Assert.Equal(0, commandLine.BaselineRunCount);
        }

        // summary command 는 output directory command 가 아니므로 --summary 파일 경로가 반드시 필요하다.
        // 이 검증이 없으면 사용자는 summary 파일이 생겼다고 생각하지만 실제로는 usage error 없이 다른 경로로 흐를 수 있다.
        [Fact]
        public void TryParse_WhenSummarizeBaselineMissingSummary_ReturnsUsageError()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--summarize-baseline", "docs/baseline" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.NotNull(errorMessage);
            Assert.Equal("SummarizeBaseline", commandLine.Command.ToString());
        }

        // --report 는 단일 runner raw JSON 출력용이고, summary command 의 출력은 --summary 로만 지정한다.
        // 두 옵션을 섞으면 입력/출력 artifact 의 의미가 불명확해지므로 parser 단계에서 막는다.
        [Fact]
        public void TryParse_WhenSummarizeBaselineHasReport_ReturnsUsageError()
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            bool parsed = BenchmarkCommandParser.TryParse(
                new[] { "--summarize-baseline", "docs/baseline", "--report", "out/report.json" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.NotNull(errorMessage);
            Assert.Equal("SummarizeBaseline", commandLine.Command.ToString());
        }

        private static string? GetStringProperty(BenchmarkCommandLine commandLine, string propertyName)
        {
            PropertyInfo? property = typeof(BenchmarkCommandLine).GetProperty(propertyName);
            Assert.NotNull(property);
            return (string?)property!.GetValue(commandLine, null);
        }
```

- [ ] **Step 2: Run focused Red**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore --filter BenchmarkCommandParserTests
```

Expected: existing tests pass, new summary tests fail by assertion. The failure should show `commandLine.Command.ToString()` is not `SummarizeBaseline` or summary path properties are missing.

- [ ] **Step 3: Add the parser surface**

Modify `tests/Hps.Benchmarks/BenchmarkCommand.cs`:

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
        SummarizeBaseline,
        Help
    }
}
```

Modify `tests/Hps.Benchmarks/BenchmarkCommandLine.cs`:

```csharp
namespace Hps.Benchmarks
{
    internal sealed class BenchmarkCommandLine
    {
        public BenchmarkCommandLine(
            BenchmarkCommand command,
            string? reportPath,
            string? baselineOutputDirectory,
            int baselineRunCount,
            string? summaryInputDirectory,
            string? summaryOutputPath)
        {
            Command = command;
            ReportPath = reportPath;
            BaselineOutputDirectory = baselineOutputDirectory;
            BaselineRunCount = baselineRunCount;
            SummaryInputDirectory = summaryInputDirectory;
            SummaryOutputPath = summaryOutputPath;
        }

        public BenchmarkCommand Command { get; }

        public string? ReportPath { get; }

        public string? BaselineOutputDirectory { get; }

        public int BaselineRunCount { get; }

        public string? SummaryInputDirectory { get; }

        public string? SummaryOutputPath { get; }
    }
}
```

Modify every existing `new BenchmarkCommandLine(...)` call in `BenchmarkCommandParser.cs` to pass `null, null` for the new summary path arguments unless the command is `SummarizeBaseline`.

Add constants to `BenchmarkCommandParser.cs`:

```csharp
        public const string MessageSummaryInputRequired = "--summarize-baseline 옵션에는 입력 directory 경로가 필요합니다.";
        public const string MessageSummaryOutputRequired = "--summary 옵션에는 저장할 summary JSON 파일 경로가 필요합니다.";
        public const string MessageSummaryReportNotAllowed = "--report 옵션은 --summarize-baseline 과 함께 사용할 수 없습니다.";
```

Add this command branch before runner command checks:

```csharp
            if (string.Equals(commandArg, "--summarize-baseline", StringComparison.OrdinalIgnoreCase))
                return ParseSummarizeBaseline(args, out commandLine, out errorMessage);
```

Add this parser method:

```csharp
        private static bool ParseSummarizeBaseline(
            string[] args,
            out BenchmarkCommandLine commandLine,
            out string? errorMessage)
        {
            string? inputDirectory = args.Length >= 2 ? args[1] : null;
            commandLine = new BenchmarkCommandLine(BenchmarkCommand.SummarizeBaseline, null, null, 0, inputDirectory, null);
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(inputDirectory))
            {
                errorMessage = MessageSummaryInputRequired;
                return true;
            }

            if (ContainsReportOption(args))
            {
                errorMessage = MessageSummaryReportNotAllowed;
                return true;
            }

            if (args.Length != 4 || !string.Equals(args[2], "--summary", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = MessageSummaryOutputRequired;
                return true;
            }

            if (string.IsNullOrWhiteSpace(args[3]))
            {
                errorMessage = MessageSummaryOutputRequired;
                return true;
            }

            commandLine = new BenchmarkCommandLine(BenchmarkCommand.SummarizeBaseline, null, null, 0, inputDirectory, args[3]);
            return true;
        }
```

In `Program.PrintUsage`, add:

```csharp
            writer.WriteLine("  Hps.Benchmarks --summarize-baseline <input-dir> --summary <output-json>");
```

Do not add the `Program` switch case yet. In this task the command parses but execution still returns the default usage error until Task 4.

- [ ] **Step 4: Run focused Green**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore --filter BenchmarkCommandParserTests
```

Expected: all parser tests pass.

- [ ] **Step 5: Run proportional verification**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-build --no-restore
git diff --check
```

Expected:

- build exit code 0, warning 0, error 0.
- benchmark tests pass with non-zero discovered test count.
- `git diff --check` has no whitespace errors; CRLF conversion warnings are acceptable.

- [ ] **Step 6: Update state docs and commit**

Update `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md` with Task 1 result and verification.

Commit only Task 1 files:

```powershell
git add tests\Hps.Benchmarks.Tests\BenchmarkCommandParserTests.cs tests\Hps.Benchmarks\BenchmarkCommand.cs tests\Hps.Benchmarks\BenchmarkCommandLine.cs tests\Hps.Benchmarks\BenchmarkCommandParser.cs tests\Hps.Benchmarks\Program.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: parse baseline summary command"
```

---

### Task 2: Summary Domain Model And Warning Rules

**Files:**
- Create: `tests/Hps.Benchmarks/BaselineReport.cs`
- Create: `tests/Hps.Benchmarks/BaselineKindSummary.cs`
- Create: `tests/Hps.Benchmarks/BaselineWarning.cs`
- Create: `tests/Hps.Benchmarks/BaselineSummary.cs`
- Create: `tests/Hps.Benchmarks/BaselineSummaryGenerator.cs`
- Create: `tests/Hps.Benchmarks.Tests/BaselineSummaryGeneratorTests.cs`
- Modify: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`

- [ ] **Step 1: Add bootstrap Red**

Create `tests/Hps.Benchmarks.Tests/BaselineSummaryGeneratorTests.cs`:

```csharp
using System;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineSummaryGeneratorTests
    {
        // summary 계산은 JSON reader/writer 와 분리된 순수 집계 단계여야 한다.
        // 먼저 타입 존재를 assertion failure 로 고정해, 이후 동작 테스트가 compile Red 로 흐르지 않게 한다.
        [Fact]
        public void BaselineSummaryGenerator_TypeExists()
        {
            Type? generatorType = Type.GetType("Hps.Benchmarks.BaselineSummaryGenerator, Hps.Benchmarks");
            Assert.NotNull(generatorType);
        }
    }
}
```

- [ ] **Step 2: Run bootstrap Red**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore --filter BaselineSummaryGeneratorTests
```

Expected: fail with `Assert.NotNull(generatorType)`.

- [ ] **Step 3: Add minimal domain types**

Create `tests/Hps.Benchmarks/BaselineReport.cs`:

```csharp
namespace Hps.Benchmarks
{
    internal sealed class BaselineReport
    {
        public BaselineReport(
            string sourcePath,
            string resultName,
            string scenario,
            int plannedMessageCount,
            int sent,
            int received,
            long dropped,
            int payloadErrors,
            int poolRented,
            double actualRateHz,
            double p50LatencyMicroseconds,
            double p99LatencyMicroseconds,
            double p99LatencyGrowthRatio,
            int tcpPendingSendQueueHighWatermark,
            int udpPendingSendQueueHighWatermark)
        {
            SourcePath = sourcePath;
            ResultName = resultName;
            Scenario = scenario;
            PlannedMessageCount = plannedMessageCount;
            Sent = sent;
            Received = received;
            Dropped = dropped;
            PayloadErrors = payloadErrors;
            PoolRented = poolRented;
            ActualRateHz = actualRateHz;
            P50LatencyMicroseconds = p50LatencyMicroseconds;
            P99LatencyMicroseconds = p99LatencyMicroseconds;
            P99LatencyGrowthRatio = p99LatencyGrowthRatio;
            TcpPendingSendQueueHighWatermark = tcpPendingSendQueueHighWatermark;
            UdpPendingSendQueueHighWatermark = udpPendingSendQueueHighWatermark;
        }

        public string SourcePath { get; }
        public string ResultName { get; }
        public string Scenario { get; }
        public int PlannedMessageCount { get; }
        public int Sent { get; }
        public int Received { get; }
        public long Dropped { get; }
        public int PayloadErrors { get; }
        public int PoolRented { get; }
        public double ActualRateHz { get; }
        public double P50LatencyMicroseconds { get; }
        public double P99LatencyMicroseconds { get; }
        public double P99LatencyGrowthRatio { get; }
        public int TcpPendingSendQueueHighWatermark { get; }
        public int UdpPendingSendQueueHighWatermark { get; }

        public bool HardPassed
        {
            get
            {
                return Sent == PlannedMessageCount
                    && Sent == Received
                    && Dropped == 0
                    && PayloadErrors == 0
                    && PoolRented == 0;
            }
        }
    }
}
```

Create `tests/Hps.Benchmarks/BaselineKindSummary.cs`:

```csharp
namespace Hps.Benchmarks
{
    internal sealed class BaselineKindSummary
    {
        public BaselineKindSummary(
            string kind,
            int runCount,
            double p50Min,
            double p50Max,
            double p99Min,
            double p99Max,
            double p99GrowthRatioMin,
            double p99GrowthRatioMax,
            double actualRateMin,
            double actualRateMax,
            int tcpHighWatermarkMin,
            int tcpHighWatermarkMax,
            long droppedTotal,
            int payloadErrorTotal,
            int poolRentedMax)
        {
            Kind = kind;
            RunCount = runCount;
            P50Min = p50Min;
            P50Max = p50Max;
            P99Min = p99Min;
            P99Max = p99Max;
            P99GrowthRatioMin = p99GrowthRatioMin;
            P99GrowthRatioMax = p99GrowthRatioMax;
            ActualRateMin = actualRateMin;
            ActualRateMax = actualRateMax;
            TcpHighWatermarkMin = tcpHighWatermarkMin;
            TcpHighWatermarkMax = tcpHighWatermarkMax;
            DroppedTotal = droppedTotal;
            PayloadErrorTotal = payloadErrorTotal;
            PoolRentedMax = poolRentedMax;
        }

        public string Kind { get; }
        public int RunCount { get; }
        public double P50Min { get; }
        public double P50Max { get; }
        public double P99Min { get; }
        public double P99Max { get; }
        public double P99GrowthRatioMin { get; }
        public double P99GrowthRatioMax { get; }
        public double ActualRateMin { get; }
        public double ActualRateMax { get; }
        public int TcpHighWatermarkMin { get; }
        public int TcpHighWatermarkMax { get; }
        public long DroppedTotal { get; }
        public int PayloadErrorTotal { get; }
        public int PoolRentedMax { get; }
    }
}
```

Create `tests/Hps.Benchmarks/BaselineWarning.cs`:

```csharp
namespace Hps.Benchmarks
{
    internal sealed class BaselineWarning
    {
        public BaselineWarning(string code, string kind, string metric, double value, double threshold, string sourcePath)
        {
            Code = code;
            Kind = kind;
            Metric = metric;
            Value = value;
            Threshold = threshold;
            SourcePath = sourcePath;
        }

        public string Code { get; }
        public string Kind { get; }
        public string Metric { get; }
        public double Value { get; }
        public double Threshold { get; }
        public string SourcePath { get; }
    }
}
```

Create `tests/Hps.Benchmarks/BaselineSummary.cs`:

```csharp
using System.Collections.Generic;

namespace Hps.Benchmarks
{
    internal sealed class BaselineSummary
    {
        public BaselineSummary(
            string sourceDirectory,
            int sourceReportCount,
            bool hardPassed,
            int hardFailureCount,
            IReadOnlyList<BaselineWarning> warnings,
            BaselineKindSummary? load,
            BaselineKindSummary? openLoop)
        {
            SourceDirectory = sourceDirectory;
            SourceReportCount = sourceReportCount;
            HardPassed = hardPassed;
            HardFailureCount = hardFailureCount;
            Warnings = warnings;
            Load = load;
            OpenLoop = openLoop;
        }

        public string SourceDirectory { get; }
        public int SourceReportCount { get; }
        public bool HardPassed { get; }
        public int HardFailureCount { get; }
        public IReadOnlyList<BaselineWarning> Warnings { get; }
        public int WarningCount { get { return Warnings.Count; } }
        public BaselineKindSummary? Load { get; }
        public BaselineKindSummary? OpenLoop { get; }
    }
}
```

Create `tests/Hps.Benchmarks/BaselineSummaryGenerator.cs` with a minimal shell first:

```csharp
using System;
using System.Collections.Generic;

namespace Hps.Benchmarks
{
    internal static class BaselineSummaryGenerator
    {
        public static BaselineSummary Generate(string sourceDirectory, IEnumerable<BaselineReport> reports)
        {
            if (sourceDirectory == null)
                throw new ArgumentNullException(nameof(sourceDirectory));

            if (reports == null)
                throw new ArgumentNullException(nameof(reports));

            throw new NotImplementedException("baseline summary generation is not implemented yet.");
        }
    }
}
```

- [ ] **Step 4: Replace bootstrap with behavior Red**

Replace `BaselineSummaryGeneratorTests.cs` with:

```csharp
using System.Linq;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineSummaryGeneratorTests
    {
        // hard gate 는 D070에서 유지하기로 한 delivery/drop/leak 조건만 집계한다.
        // latency 는 warning 후보일 뿐이므로 정상 latency/queue 조건에서는 hard pass 가 true 로 남아야 한다.
        [Fact]
        public void Generate_WhenReportsPassHardGate_ReturnsKindRangesWithoutWarnings()
        {
            BaselineReport[] reports =
            {
                CreateReport("a/load-01.json", "load", 221.6, 471.0, 0.93, 99.8, 1, 0, 3000),
                CreateReport("a/load-02.json", "load", 256.7, 924.1, 1.16, 100.0, 1, 0, 3000),
                CreateReport("a/open-loop-01.json", "open-loop", 229.0, 502.6, 0.65, 99.9, 2, 0, 3000),
                CreateReport("a/open-loop-02.json", "open-loop", 274.3, 1005.5, 1.15, 100.0, 3, 0, 3000)
            };

            BaselineSummary summary = BaselineSummaryGenerator.Generate("a", reports);

            Assert.True(summary.HardPassed);
            Assert.Equal(4, summary.SourceReportCount);
            Assert.Equal(0, summary.HardFailureCount);
            Assert.Equal(0, summary.WarningCount);
            Assert.NotNull(summary.Load);
            Assert.NotNull(summary.OpenLoop);
            Assert.Equal(221.6, summary.Load!.P50Min, 1);
            Assert.Equal(924.1, summary.Load.P99Max, 1);
            Assert.Equal(2, summary.OpenLoop!.RunCount);
            Assert.Equal(3, summary.OpenLoop.TcpHighWatermarkMax);
        }

        // sent/received/drop/pool 조건 중 하나라도 깨지면 latency 와 무관하게 hard failure 로 집계한다.
        // summary command 의 exit code 는 이 hardPassed 값을 통해 Program wiring 에서 결정된다.
        [Fact]
        public void Generate_WhenReportFailsHardGate_CountsHardFailure()
        {
            BaselineReport[] reports =
            {
                CreateReport("a/load-01.json", "load", 230.0, 500.0, 1.0, 100.0, 1, 0, 3000),
                CreateReport("a/load-02.json", "load", 230.0, 500.0, 1.0, 100.0, 1, 1, 3000)
            };

            BaselineSummary summary = BaselineSummaryGenerator.Generate("a", reports);

            Assert.False(summary.HardPassed);
            Assert.Equal(1, summary.HardFailureCount);
        }

        // D070의 p99/HWM/actual-rate 기준은 hard failure 가 아니라 warning artifact 로만 남긴다.
        // warning 이 있어도 hard gate 조건을 만족하면 hardPassed 는 true 여야 한다.
        [Fact]
        public void Generate_WhenSoftLimitIsExceeded_EmitsWarningButKeepsHardPass()
        {
            BaselineReport[] reports =
            {
                CreateReport("a/open-loop-01.json", "open-loop", 240.0, 1600.0, 2.1, 94.9, 8, 0, 3000)
            };

            BaselineSummary summary = BaselineSummaryGenerator.Generate("a", reports);

            Assert.True(summary.HardPassed);
            Assert.True(summary.WarningCount >= 4);
            Assert.Contains(summary.Warnings, warning => warning.Code == "open-loop-p99-latency-high");
            Assert.Contains(summary.Warnings, warning => warning.Code == "p99-growth-ratio-high");
            Assert.Contains(summary.Warnings, warning => warning.Code == "actual-rate-low");
            Assert.Contains(summary.Warnings, warning => warning.Code == "open-loop-tcp-hwm-high");
        }

        private static BaselineReport CreateReport(
            string sourcePath,
            string resultName,
            double p50,
            double p99,
            double growth,
            double actualRate,
            int tcpHwm,
            long dropped,
            int received)
        {
            return new BaselineReport(
                sourcePath,
                resultName,
                "tcp-loopback-saea-baseline",
                3000,
                3000,
                received,
                dropped,
                0,
                0,
                actualRate,
                p50,
                p99,
                growth,
                tcpHwm,
                0);
        }
    }
}
```

- [ ] **Step 5: Run behavior Red**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-build --no-restore --filter BaselineSummaryGeneratorTests
```

Expected: fail with `NotImplementedException`.

- [ ] **Step 6: Implement summary generation**

Replace `BaselineSummaryGenerator.cs` with:

```csharp
using System;
using System.Collections.Generic;

namespace Hps.Benchmarks
{
    internal static class BaselineSummaryGenerator
    {
        private const double LoadP99WarningThreshold = 1386.2;
        private const double OpenLoopP99WarningThreshold = 1508.3;
        private const double P99GrowthRatioWarningThreshold = 2.0;
        private const double ActualRateWarningThreshold = 95.0;
        private const int LoadTcpHighWatermarkWarningThreshold = 4;
        private const int OpenLoopTcpHighWatermarkWarningThreshold = 8;

        public static BaselineSummary Generate(string sourceDirectory, IEnumerable<BaselineReport> reports)
        {
            if (sourceDirectory == null)
                throw new ArgumentNullException(nameof(sourceDirectory));

            if (reports == null)
                throw new ArgumentNullException(nameof(reports));

            List<BaselineReport> allReports = new List<BaselineReport>(reports);
            List<BaselineWarning> warnings = new List<BaselineWarning>();
            int hardFailureCount = 0;

            for (int i = 0; i < allReports.Count; i++)
            {
                BaselineReport report = allReports[i];
                if (!report.HardPassed)
                    hardFailureCount++;

                AddWarnings(report, warnings);
            }

            BaselineKindSummary? load = CreateKindSummary("load", allReports);
            BaselineKindSummary? openLoop = CreateKindSummary("open-loop", allReports);

            return new BaselineSummary(
                sourceDirectory,
                allReports.Count,
                allReports.Count > 0 && hardFailureCount == 0,
                hardFailureCount,
                warnings,
                load,
                openLoop);
        }

        private static BaselineKindSummary? CreateKindSummary(string kind, List<BaselineReport> reports)
        {
            bool hasAny = false;
            int runCount = 0;
            double p50Min = 0;
            double p50Max = 0;
            double p99Min = 0;
            double p99Max = 0;
            double growthMin = 0;
            double growthMax = 0;
            double rateMin = 0;
            double rateMax = 0;
            int tcpHwmMin = 0;
            int tcpHwmMax = 0;
            long droppedTotal = 0;
            int payloadErrorTotal = 0;
            int poolRentedMax = 0;

            for (int i = 0; i < reports.Count; i++)
            {
                BaselineReport report = reports[i];
                if (!string.Equals(report.ResultName, kind, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!hasAny)
                {
                    p50Min = report.P50LatencyMicroseconds;
                    p50Max = report.P50LatencyMicroseconds;
                    p99Min = report.P99LatencyMicroseconds;
                    p99Max = report.P99LatencyMicroseconds;
                    growthMin = report.P99LatencyGrowthRatio;
                    growthMax = report.P99LatencyGrowthRatio;
                    rateMin = report.ActualRateHz;
                    rateMax = report.ActualRateHz;
                    tcpHwmMin = report.TcpPendingSendQueueHighWatermark;
                    tcpHwmMax = report.TcpPendingSendQueueHighWatermark;
                    poolRentedMax = report.PoolRented;
                    hasAny = true;
                }
                else
                {
                    p50Min = Math.Min(p50Min, report.P50LatencyMicroseconds);
                    p50Max = Math.Max(p50Max, report.P50LatencyMicroseconds);
                    p99Min = Math.Min(p99Min, report.P99LatencyMicroseconds);
                    p99Max = Math.Max(p99Max, report.P99LatencyMicroseconds);
                    growthMin = Math.Min(growthMin, report.P99LatencyGrowthRatio);
                    growthMax = Math.Max(growthMax, report.P99LatencyGrowthRatio);
                    rateMin = Math.Min(rateMin, report.ActualRateHz);
                    rateMax = Math.Max(rateMax, report.ActualRateHz);
                    tcpHwmMin = Math.Min(tcpHwmMin, report.TcpPendingSendQueueHighWatermark);
                    tcpHwmMax = Math.Max(tcpHwmMax, report.TcpPendingSendQueueHighWatermark);
                    poolRentedMax = Math.Max(poolRentedMax, report.PoolRented);
                }

                runCount++;
                droppedTotal += report.Dropped;
                payloadErrorTotal += report.PayloadErrors;
            }

            if (!hasAny)
                return null;

            return new BaselineKindSummary(kind, runCount, p50Min, p50Max, p99Min, p99Max, growthMin, growthMax, rateMin, rateMax, tcpHwmMin, tcpHwmMax, droppedTotal, payloadErrorTotal, poolRentedMax);
        }

        private static void AddWarnings(BaselineReport report, List<BaselineWarning> warnings)
        {
            bool openLoop = string.Equals(report.ResultName, "open-loop", StringComparison.OrdinalIgnoreCase);
            double p99Threshold = openLoop ? OpenLoopP99WarningThreshold : LoadP99WarningThreshold;
            int hwmThreshold = openLoop ? OpenLoopTcpHighWatermarkWarningThreshold : LoadTcpHighWatermarkWarningThreshold;
            string kind = openLoop ? "open-loop" : "load";

            if (report.P99LatencyMicroseconds > p99Threshold)
                warnings.Add(new BaselineWarning(kind + "-p99-latency-high", kind, "p99-latency-us", report.P99LatencyMicroseconds, p99Threshold, report.SourcePath));

            if (report.P99LatencyGrowthRatio > P99GrowthRatioWarningThreshold)
                warnings.Add(new BaselineWarning("p99-growth-ratio-high", kind, "p99-latency-growth-ratio", report.P99LatencyGrowthRatio, P99GrowthRatioWarningThreshold, report.SourcePath));

            if (report.ActualRateHz < ActualRateWarningThreshold)
                warnings.Add(new BaselineWarning("actual-rate-low", kind, "actual-rate-hz", report.ActualRateHz, ActualRateWarningThreshold, report.SourcePath));

            if (report.TcpPendingSendQueueHighWatermark >= hwmThreshold)
                warnings.Add(new BaselineWarning(kind + "-tcp-hwm-high", kind, "tcp-pending-send-queue-high-watermark", report.TcpPendingSendQueueHighWatermark, hwmThreshold, report.SourcePath));
        }
    }
}
```

- [ ] **Step 7: Run focused Green and proportional verification**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-build --no-restore --filter BaselineSummaryGeneratorTests
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-build --no-restore
git diff --check
```

Expected: focused tests pass, build passes warning 0/error 0, benchmark tests pass, whitespace check passes.

- [ ] **Step 8: Update state docs and commit**

Update `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.

Commit only Task 2 files:

```powershell
git add tests\Hps.Benchmarks\BaselineReport.cs tests\Hps.Benchmarks\BaselineKindSummary.cs tests\Hps.Benchmarks\BaselineWarning.cs tests\Hps.Benchmarks\BaselineSummary.cs tests\Hps.Benchmarks\BaselineSummaryGenerator.cs tests\Hps.Benchmarks.Tests\BaselineSummaryGeneratorTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: calculate baseline summary warnings"
```

---

### Task 3: Baseline JSON Reader And Summary Writer

**Files:**
- Create: `tests/Hps.Benchmarks/BaselineReportReader.cs`
- Create: `tests/Hps.Benchmarks/BaselineSummaryWriter.cs`
- Create: `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`
- Modify: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`

- [ ] **Step 1: Write reader/writer Red tests**

Create `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`:

```csharp
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineReportReaderWriterTests
    {
        // reader 는 per-run schema v1 JSON만 summary 입력으로 삼고, summary.json 같은 다른 artifact 는 건너뛴다.
        // 이 경계가 없으면 summary command 를 같은 directory 에 반복 실행할 때 이전 summary 를 run 으로 잘못 집계할 수 있다.
        [Fact]
        public void ReadDirectory_WhenDirectoryHasRunReportsAndSummary_ReadsOnlyRunReports()
        {
            string directory = CreateTempDirectory();
            WriteRunJson(Path.Combine(directory, "load-01.json"), "load", 500.0, 1, 0, 3000);
            WriteRunJson(Path.Combine(directory, "open-loop-01.json"), "open-loop", 600.0, 2, 0, 3000);
            File.WriteAllText(Path.Combine(directory, "summary.json"), "{ \"summary-version\": 1 }");

            BaselineReport[] reports = BaselineReportReader.ReadDirectory(directory).ToArray();

            Assert.Equal(2, reports.Length);
            Assert.Contains(reports, report => report.ResultName == "load");
            Assert.Contains(reports, report => report.ResultName == "open-loop");
        }

        // writer 는 자동화가 읽을 수 있는 안정적인 key 집합을 만든다.
        // summary-version 과 by-kind 구조가 없으면 CI artifact 소비자가 schema 를 식별할 수 없다.
        [Fact]
        public void Write_WhenSummaryHasWarnings_WritesStableJsonShape()
        {
            string directory = CreateTempDirectory();
            string path = Path.Combine(directory, "summary.json");
            BaselineReport[] reports =
            {
                new BaselineReport("open-loop-01.json", "open-loop", "scenario", 3000, 3000, 3000, 0, 0, 0, 94.0, 240.0, 1600.0, 2.1, 8, 0)
            };
            BaselineSummary summary = BaselineSummaryGenerator.Generate(directory, reports);

            BaselineSummaryWriter.Write(path, summary);

            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement root = document.RootElement;
                Assert.Equal(1, root.GetProperty("summary-version").GetInt32());
                Assert.True(root.GetProperty("hard-passed").GetBoolean());
                Assert.True(root.GetProperty("warning-count").GetInt32() >= 1);
                Assert.True(root.GetProperty("by-kind").TryGetProperty("open-loop", out JsonElement openLoop));
                Assert.Equal(1, openLoop.GetProperty("run-count").GetInt32());
                Assert.True(root.GetProperty("warnings").GetArrayLength() >= 1);
            }
        }

        private static string CreateTempDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "hps-baseline-summary-tests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void WriteRunJson(string path, string resultName, double p99, int tcpHwm, long dropped, int received)
        {
            string json = "{"
                + "\"schema-version\":1,"
                + "\"result-name\":\"" + resultName + "\","
                + "\"passed\":true,"
                + "\"scenario\":\"tcp-loopback-saea-baseline\","
                + "\"payload-bytes\":4096,"
                + "\"target-rate-hz\":100,"
                + "\"target-duration-seconds\":30,"
                + "\"planned-message-count\":3000,"
                + "\"sent\":3000,"
                + "\"received\":" + received.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"dropped\":" + dropped.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"tcp-pending-send-queue-high-watermark\":" + tcpHwm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"udp-pending-send-queue-high-watermark\":0,"
                + "\"payload-errors\":0,"
                + "\"pool-rented\":0,"
                + "\"actual-rate-hz\":99.9,"
                + "\"p50-latency-us\":240.0,"
                + "\"p99-latency-us\":" + p99.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"first-half-p99-latency-us\":500.0,"
                + "\"second-half-p99-latency-us\":500.0,"
                + "\"p99-latency-growth-ratio\":1.0,"
                + "\"elapsed-ms\":30000"
                + "}";
            File.WriteAllText(path, json);
        }
    }
}
```

- [ ] **Step 2: Run focused Red**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-build --no-restore --filter BaselineReportReaderWriterTests
```

Expected: fail because `BaselineReportReader` and `BaselineSummaryWriter` types do not exist.

- [ ] **Step 3: Implement reader**

Create `tests/Hps.Benchmarks/BaselineReportReader.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Hps.Benchmarks
{
    internal static class BaselineReportReader
    {
        public static IReadOnlyList<BaselineReport> ReadDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("baseline summary input directory 는 비어 있을 수 없습니다.", nameof(directory));

            string fullDirectory = Path.GetFullPath(directory);
            string[] files = Directory.GetFiles(fullDirectory, "*.json", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            List<BaselineReport> reports = new List<BaselineReport>();
            for (int i = 0; i < files.Length; i++)
            {
                BaselineReport? report = TryReadReport(files[i]);
                if (report != null)
                    reports.Add(report);
            }

            return reports;
        }

        private static BaselineReport? TryReadReport(string path)
        {
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement root = document.RootElement;
                if (!root.TryGetProperty("schema-version", out JsonElement schemaVersion))
                    return null;

                if (schemaVersion.GetInt32() != 1)
                    return null;

                if (!root.TryGetProperty("result-name", out JsonElement resultNameElement))
                    return null;

                return new BaselineReport(
                    path.Replace('\\', '/'),
                    resultNameElement.GetString()!,
                    GetString(root, "scenario"),
                    GetInt(root, "planned-message-count"),
                    GetInt(root, "sent"),
                    GetInt(root, "received"),
                    GetInt64(root, "dropped"),
                    GetInt(root, "payload-errors"),
                    GetInt(root, "pool-rented"),
                    GetDouble(root, "actual-rate-hz"),
                    GetDouble(root, "p50-latency-us"),
                    GetDouble(root, "p99-latency-us"),
                    GetDouble(root, "p99-latency-growth-ratio"),
                    GetInt(root, "tcp-pending-send-queue-high-watermark"),
                    GetInt(root, "udp-pending-send-queue-high-watermark"));
            }
        }

        private static string GetString(JsonElement root, string name)
        {
            return root.GetProperty(name).GetString()!;
        }

        private static int GetInt(JsonElement root, string name)
        {
            return root.GetProperty(name).GetInt32();
        }

        private static long GetInt64(JsonElement root, string name)
        {
            return root.GetProperty(name).GetInt64();
        }

        private static double GetDouble(JsonElement root, string name)
        {
            JsonElement value = root.GetProperty(name);
            if (value.ValueKind == JsonValueKind.Number)
                return value.GetDouble();

            return double.Parse(value.GetString()!, CultureInfo.InvariantCulture);
        }
    }
}
```

- [ ] **Step 4: Implement writer**

Create `tests/Hps.Benchmarks/BaselineSummaryWriter.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;

namespace Hps.Benchmarks
{
    internal static class BaselineSummaryWriter
    {
        public static void Write(string path, BaselineSummary summary)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("summary output path 는 비어 있을 수 없습니다.", nameof(path));

            if (summary == null)
                throw new ArgumentNullException(nameof(summary));

            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (FileStream stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("summary-version", 1);
                writer.WriteString("source-directory", summary.SourceDirectory);
                writer.WriteNumber("source-report-count", summary.SourceReportCount);
                writer.WriteBoolean("hard-passed", summary.HardPassed);
                writer.WriteNumber("hard-failure-count", summary.HardFailureCount);
                writer.WriteNumber("warning-count", summary.WarningCount);
                WriteWarnings(writer, summary);
                WriteByKind(writer, summary);
                writer.WriteEndObject();
            }
        }

        private static void WriteWarnings(Utf8JsonWriter writer, BaselineSummary summary)
        {
            writer.WritePropertyName("warnings");
            writer.WriteStartArray();
            for (int i = 0; i < summary.Warnings.Count; i++)
            {
                BaselineWarning warning = summary.Warnings[i];
                writer.WriteStartObject();
                writer.WriteString("code", warning.Code);
                writer.WriteString("kind", warning.Kind);
                writer.WriteString("metric", warning.Metric);
                writer.WriteNumber("value", warning.Value);
                writer.WriteNumber("threshold", warning.Threshold);
                writer.WriteString("source-path", warning.SourcePath);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        private static void WriteByKind(Utf8JsonWriter writer, BaselineSummary summary)
        {
            writer.WritePropertyName("by-kind");
            writer.WriteStartObject();
            WriteKind(writer, "load", summary.Load);
            WriteKind(writer, "open-loop", summary.OpenLoop);
            writer.WriteEndObject();
        }

        private static void WriteKind(Utf8JsonWriter writer, string propertyName, BaselineKindSummary? kind)
        {
            writer.WritePropertyName(propertyName);
            if (kind == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();
            writer.WriteNumber("run-count", kind.RunCount);
            writer.WriteNumber("p50-min-us", kind.P50Min);
            writer.WriteNumber("p50-max-us", kind.P50Max);
            writer.WriteNumber("p99-min-us", kind.P99Min);
            writer.WriteNumber("p99-max-us", kind.P99Max);
            writer.WriteNumber("p99-growth-ratio-min", kind.P99GrowthRatioMin);
            writer.WriteNumber("p99-growth-ratio-max", kind.P99GrowthRatioMax);
            writer.WriteNumber("actual-rate-min-hz", kind.ActualRateMin);
            writer.WriteNumber("actual-rate-max-hz", kind.ActualRateMax);
            writer.WriteNumber("tcp-hwm-min", kind.TcpHighWatermarkMin);
            writer.WriteNumber("tcp-hwm-max", kind.TcpHighWatermarkMax);
            writer.WriteNumber("dropped-total", kind.DroppedTotal);
            writer.WriteNumber("payload-error-total", kind.PayloadErrorTotal);
            writer.WriteNumber("pool-rented-max", kind.PoolRentedMax);
            writer.WriteEndObject();
        }
    }
}
```

- [ ] **Step 5: Run focused Green and proportional verification**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-build --no-restore --filter BaselineReportReaderWriterTests
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-build --no-restore
git diff --check
```

Expected: focused tests pass, build passes warning 0/error 0, benchmark tests pass, whitespace check passes.

- [ ] **Step 6: Update state docs and commit**

Update `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.

Commit only Task 3 files:

```powershell
git add tests\Hps.Benchmarks\BaselineReportReader.cs tests\Hps.Benchmarks\BaselineSummaryWriter.cs tests\Hps.Benchmarks.Tests\BaselineReportReaderWriterTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: read and write baseline summary json"
```

---

### Task 4: Program Wiring And CLI Smoke

**Files:**
- Modify: `tests/Hps.Benchmarks/Program.cs`
- Modify: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`

- [ ] **Step 1: Run CLI Red**

Run:

```powershell
if (Test-Path artifacts\baseline-summary-smoke.json) { Remove-Item -Force artifacts\baseline-summary-smoke.json }
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build -- --summarize-baseline docs\benchmarks\baselines\2026-06-18 --summary artifacts\baseline-summary-smoke.json
```

Expected: exit code 2 or non-zero, and `artifacts\baseline-summary-smoke.json` is not created because `Program` has no `SummarizeBaseline` case yet.

- [ ] **Step 2: Wire Program command**

In `Program.Main`, add a switch case:

```csharp
                case BenchmarkCommand.SummarizeBaseline:
                    return CompleteBaselineSummary(commandLine.SummaryInputDirectory!, commandLine.SummaryOutputPath!);
```

Add this method to `Program`:

```csharp
        private static int CompleteBaselineSummary(string inputDirectory, string summaryPath)
        {
            try
            {
                System.Collections.Generic.IReadOnlyList<BaselineReport> reports = BaselineReportReader.ReadDirectory(inputDirectory);
                BaselineSummary summary = BaselineSummaryGenerator.Generate(inputDirectory, reports);
                BaselineSummaryWriter.Write(summaryPath, summary);
                Console.Out.WriteLine("baseline-summary: {0}", summaryPath);
                Console.Out.WriteLine("source-report-count: {0}", summary.SourceReportCount);
                Console.Out.WriteLine("hard-passed: {0}", summary.HardPassed ? "true" : "false");
                Console.Out.WriteLine("warning-count: {0}", summary.WarningCount);
                return summary.HardPassed ? SuccessExitCode : FailedRunExitCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("baseline-summary-error: {0}", ex.Message);
                return ReportWriteFailedExitCode;
            }
        }
```

Keep `ReportWriteFailedExitCode` for read/write/summary command failures so current CLI error category remains simple.

- [ ] **Step 3: Run CLI Green**

Run:

```powershell
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build -- --summarize-baseline docs\benchmarks\baselines\2026-06-18 --summary artifacts\baseline-summary-smoke.json
```

Expected:

- exit code 0.
- stdout includes `baseline-summary: artifacts\baseline-summary-smoke.json`.
- stdout includes `source-report-count: 6` if the input directory is the root `2026-06-18` directory because only top-level `load-*.json` and `open-loop-*.json` are read.
- `artifacts\baseline-summary-smoke.json` exists.
- JSON has `summary-version: 1`, `hard-passed: true`, `source-report-count: 6`, `by-kind.load.run-count: 3`, `by-kind.open-loop.run-count: 3`.

Then run the same command for each nested session:

```powershell
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build -- --summarize-baseline docs\benchmarks\baselines\2026-06-18\session-02 --summary artifacts\baseline-summary-session-02.json
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build -- --summarize-baseline docs\benchmarks\baselines\2026-06-18\session-03 --summary artifacts\baseline-summary-session-03.json
```

Expected: both exit code 0 with `source-report-count: 6`.

- [ ] **Step 4: Run final verification**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
git diff --check
```

Expected:

- build exit code 0, warning 0, error 0.
- solution tests pass with non-zero discovered test count.
- whitespace check passes; CRLF conversion warnings are acceptable.

- [ ] **Step 5: Remove smoke artifacts or keep them out of commit**

Do not commit `artifacts\baseline-summary-smoke.json`, `artifacts\baseline-summary-session-02.json`, or `artifacts\baseline-summary-session-03.json`. If they are untracked, delete them or leave them untracked only if `.gitignore` already excludes `artifacts/`.

- [ ] **Step 6: Update state docs and commit**

Update `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.

Commit only Task 4 files:

```powershell
git add tests\Hps.Benchmarks\Program.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: wire baseline summary command"
```

---

## Self-Review

- Spec coverage:
  - D070 hard gate 보류: Task 2에서 `HardPassed`는 delivery/drop/leak 조건만 사용한다.
  - summary JSON artifact: Task 3에서 writer 가 `summary-version`, `hard-passed`, `warning-count`, `warnings`, `by-kind`를 쓴다.
  - non-failing soft warning: Task 2에서 warning 을 산출하지만 hard pass 를 바꾸지 않는다.
  - CLI command: Task 1 parser, Task 4 Program wiring 으로 닫는다.
  - Markdown/CI/hard latency gate 제외: Scope 와 Task 4에서 제외한다.
- Placeholder scan:
  - 미정 placeholder 없이 각 task 에 파일, 명령, 예상 결과를 적었다.
- Type consistency:
  - `BaselineReport`, `BaselineSummary`, `BaselineKindSummary`, `BaselineWarning` property 명칭은 generator, reader, writer, tests 에서 일치한다.
  - CLI path property 는 `SummaryInputDirectory`, `SummaryOutputPath`로 통일한다.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-06-18-baseline-summary-artifact.md`.

권장 실행 방식:

1. Subagent-Driven: 각 task 를 독립 작업자로 실행하고 task 사이마다 review 한다.
2. Inline Execution: 이 세션에서 `superpowers:executing-plans`로 task 를 순서대로 실행한다.

현재 프로젝트 규칙상 한 번에 너무 많은 수정은 피해야 하므로, 실제 구현은 Task 1만 먼저 진행하고 커밋한 뒤 사용자 리뷰를 받는 흐름이 맞다.
