# Benchmark Runner Identity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** benchmark raw report 에 runner/environment metadata 를 기록하고 legacy report 를 깨지 않고 읽는다.

**Architecture:** D079의 첫 구현 범위만 다룬다. 먼저 `BenchmarkRunIdentity` 내부 model 로 metadata capture 규칙을 고정하고, `TcpLoopbackRunResult`와 raw JSON writer 에 연결한 뒤, `BaselineReportReader`가 신규 metadata 와 legacy report 를 모두 읽게 한다. summary/history comparison signal 은 원천 raw metadata 가 들어간 뒤 별도 단위에서 다룬다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, `System.Text.Json`, `System.Runtime.InteropServices`, 기존 `tests/Hps.Benchmarks` report writer/reader 패턴.

## Global Constraints

- TFM 은 `net9.0`, LangVersion 은 C# 8.0 이며 file-scoped namespace, record, target-typed `new()` 를 쓰지 않는다.
- 모든 문서와 주석은 한국어로 작성한다. 테스트에는 무엇을 검증하는지 한국어 주석을 붙인다.
- 코드 변경은 Red-Green-Refactor 를 따른다. 컴파일 실패 Red 가 아니라 assertion failure Red 를 먼저 확인한다.
- 작업은 기능별 작은 단위로 나누고, 각 Task 는 별도 커밋으로 끝낸다.
- D079에 따라 raw report `schema-version`은 1로 유지하고 runner/environment metadata 는 additive field 로만 추가한다.
- host name, user name, full path, IP address 는 자동으로 기록하지 않는다.
- 기본 `runner-id`는 `local-unspecified`, 기본 `runner-kind`는 `local` 이다.
- 명시 runner 식별자는 `HPS_BENCHMARK_RUNNER_ID`, runner 종류는 `HPS_BENCHMARK_RUNNER_KIND` 환경 변수에서만 읽는다.
- summary/history comparison signal, warning-as-failure, latency hard gate, CI workflow, generated index 자동 갱신은 이번 계획 범위가 아니다.

---

## File Structure

- `tests/Hps.Benchmarks/BenchmarkRunIdentity.cs`
  - raw report 에 기록할 runner/environment metadata 를 보존한다.
  - `CaptureDefault()`는 privacy 우선 기본값과 runtime metadata 를 수집한다.
  - `Unknown`은 legacy report read 결과를 표현한다.
- `tests/Hps.Benchmarks/TcpLoopbackRunResult.cs`
  - 각 run 결과가 `BenchmarkRunIdentity`를 함께 보존한다.
  - 기존 생성자 호출은 optional identity 로 호환한다.
- `tests/Hps.Benchmarks/TcpLoopbackReportWriter.cs`
  - raw report JSON top-level 에 D079 metadata field 를 쓴다.
- `tests/Hps.Benchmarks/BaselineReport.cs`
  - summary 입력으로 읽은 raw report 의 identity 를 보존한다.
- `tests/Hps.Benchmarks/BaselineReportReader.cs`
  - raw report metadata 를 읽고, field 가 없는 legacy report 는 `BenchmarkRunIdentity.Unknown`으로 보존한다.
- `tests/Hps.Benchmarks.Tests/BenchmarkRunIdentityTests.cs`
  - identity model, 환경 변수 override, privacy 기본값을 검증한다.
- `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`
  - raw JSON writer shape 와 reader 호환성을 검증한다.
- Root state docs
  - `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`를 각 Task 완료마다 갱신한다.

---

### Task 1: BenchmarkRunIdentity Model

**Files:**
- Create: `tests/Hps.Benchmarks/BenchmarkRunIdentity.cs`
- Create: `tests/Hps.Benchmarks.Tests/BenchmarkRunIdentityTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Produces:
  - `internal sealed class BenchmarkRunIdentity`
  - `public const string DefaultBenchmarkProfile = "tcp-loopback-saea-v1"`
  - `public const string DefaultRunnerId = "local-unspecified"`
  - `public const string DefaultRunnerKind = "local"`
  - `public const string DefaultTransportBackend = "SaeaTransport"`
  - `public static BenchmarkRunIdentity Unknown { get; }`
  - `public static BenchmarkRunIdentity CaptureDefault()`
  - Properties: `BenchmarkProfile`, `RunnerId`, `RunnerKind`, `TransportBackend`, `OsDescription`, `OsArchitecture`, `ProcessArchitecture`, `FrameworkDescription`, `ProcessorCount`

- [ ] **Step 1: Write the failing contract test**

Create `tests/Hps.Benchmarks.Tests/BenchmarkRunIdentityTests.cs`:

```csharp
using System;
using System.Reflection;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BenchmarkRunIdentityTests
    {
        // runner identity 는 raw report schema 확장의 원천 model 이다.
        // 새 타입을 먼저 계약으로 고정해야 writer/reader 단계가 문자열 상수 중복 없이 같은 field 를 사용할 수 있다.
        [Fact]
        public void Contract_BenchmarkRunIdentityTypeExists()
        {
            Type? type = Type.GetType("Hps.Benchmarks.BenchmarkRunIdentity, Hps.Benchmarks");

            Assert.NotNull(type);
            Assert.NotNull(type!.GetProperty("BenchmarkProfile", BindingFlags.Instance | BindingFlags.Public));
            Assert.NotNull(type.GetMethod("CaptureDefault", BindingFlags.Static | BindingFlags.Public));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BenchmarkRunIdentityTests
```

Expected: FAIL with `Assert.NotNull()` because `BenchmarkRunIdentity` does not exist.

- [ ] **Step 3: Add stub type for assertion-failure Red cycle**

Create `tests/Hps.Benchmarks/BenchmarkRunIdentity.cs`:

```csharp
namespace Hps.Benchmarks
{
    internal sealed class BenchmarkRunIdentity
    {
        public const string DefaultBenchmarkProfile = "tcp-loopback-saea-v1";
        public const string DefaultRunnerId = "local-unspecified";
        public const string DefaultRunnerKind = "local";
        public const string DefaultTransportBackend = "SaeaTransport";

        public BenchmarkRunIdentity(
            string benchmarkProfile,
            string runnerId,
            string runnerKind,
            string transportBackend,
            string osDescription,
            string osArchitecture,
            string processArchitecture,
            string frameworkDescription,
            int processorCount)
        {
            BenchmarkProfile = benchmarkProfile;
            RunnerId = runnerId;
            RunnerKind = runnerKind;
            TransportBackend = transportBackend;
            OsDescription = osDescription;
            OsArchitecture = osArchitecture;
            ProcessArchitecture = processArchitecture;
            FrameworkDescription = frameworkDescription;
            ProcessorCount = processorCount;
        }

        public static BenchmarkRunIdentity Unknown
        {
            get
            {
                return new BenchmarkRunIdentity("unknown", "unknown", "unknown", "unknown", "unknown", "unknown", "unknown", "unknown", 0);
            }
        }

        public string BenchmarkProfile { get; }
        public string RunnerId { get; }
        public string RunnerKind { get; }
        public string TransportBackend { get; }
        public string OsDescription { get; }
        public string OsArchitecture { get; }
        public string ProcessArchitecture { get; }
        public string FrameworkDescription { get; }
        public int ProcessorCount { get; }

        public static BenchmarkRunIdentity CaptureDefault()
        {
            return Unknown;
        }
    }
}
```

- [ ] **Step 4: Run contract test to verify stub passes**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BenchmarkRunIdentityTests
```

Expected: PASS for the single contract test.

- [ ] **Step 5: Add behavior tests**

Append these tests inside `BenchmarkRunIdentityTests`:

```csharp
        // 기본 capture 는 privacy 를 우선한다.
        // host name/user name/IP 를 쓰지 않고 명시 runner id 가 없으면 local-unspecified 로 남겨야 한다.
        [Fact]
        public void CaptureDefault_WhenEnvironmentValuesAreMissing_UsesPrivacyPreservingDefaultsAndRuntimeInfo()
        {
            string? oldRunnerId = Environment.GetEnvironmentVariable("HPS_BENCHMARK_RUNNER_ID");
            string? oldRunnerKind = Environment.GetEnvironmentVariable("HPS_BENCHMARK_RUNNER_KIND");
            try
            {
                Environment.SetEnvironmentVariable("HPS_BENCHMARK_RUNNER_ID", null);
                Environment.SetEnvironmentVariable("HPS_BENCHMARK_RUNNER_KIND", null);

                BenchmarkRunIdentity identity = BenchmarkRunIdentity.CaptureDefault();

                Assert.Equal(BenchmarkRunIdentity.DefaultBenchmarkProfile, identity.BenchmarkProfile);
                Assert.Equal(BenchmarkRunIdentity.DefaultRunnerId, identity.RunnerId);
                Assert.Equal(BenchmarkRunIdentity.DefaultRunnerKind, identity.RunnerKind);
                Assert.Equal(BenchmarkRunIdentity.DefaultTransportBackend, identity.TransportBackend);
                Assert.False(string.IsNullOrWhiteSpace(identity.OsDescription));
                Assert.False(string.IsNullOrWhiteSpace(identity.OsArchitecture));
                Assert.False(string.IsNullOrWhiteSpace(identity.ProcessArchitecture));
                Assert.False(string.IsNullOrWhiteSpace(identity.FrameworkDescription));
                Assert.True(identity.ProcessorCount > 0);
            }
            finally
            {
                Environment.SetEnvironmentVariable("HPS_BENCHMARK_RUNNER_ID", oldRunnerId);
                Environment.SetEnvironmentVariable("HPS_BENCHMARK_RUNNER_KIND", oldRunnerKind);
            }
        }

        // 서로 다른 장비를 비교군에서 분리하려면 사용자가 runner id 를 명시해야 한다.
        // 자동 machine name 수집 대신 환경 변수만 허용해 로컬/사설 환경 정보 노출을 막는다.
        [Fact]
        public void CaptureDefault_WhenEnvironmentValuesExist_UsesExplicitRunnerIdentity()
        {
            string? oldRunnerId = Environment.GetEnvironmentVariable("HPS_BENCHMARK_RUNNER_ID");
            string? oldRunnerKind = Environment.GetEnvironmentVariable("HPS_BENCHMARK_RUNNER_KIND");
            try
            {
                Environment.SetEnvironmentVariable("HPS_BENCHMARK_RUNNER_ID", "dev-box-a");
                Environment.SetEnvironmentVariable("HPS_BENCHMARK_RUNNER_KIND", "self-hosted");

                BenchmarkRunIdentity identity = BenchmarkRunIdentity.CaptureDefault();

                Assert.Equal("dev-box-a", identity.RunnerId);
                Assert.Equal("self-hosted", identity.RunnerKind);
            }
            finally
            {
                Environment.SetEnvironmentVariable("HPS_BENCHMARK_RUNNER_ID", oldRunnerId);
                Environment.SetEnvironmentVariable("HPS_BENCHMARK_RUNNER_KIND", oldRunnerKind);
            }
        }
```

- [ ] **Step 6: Run behavior tests to verify they fail**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BenchmarkRunIdentityTests
```

Expected: FAIL because `CaptureDefault()` still returns `Unknown`.

- [ ] **Step 7: Implement runtime capture**

Replace `BenchmarkRunIdentity.cs` with:

```csharp
using System;
using System.Runtime.InteropServices;

namespace Hps.Benchmarks
{
    internal sealed class BenchmarkRunIdentity
    {
        public const string DefaultBenchmarkProfile = "tcp-loopback-saea-v1";
        public const string DefaultRunnerId = "local-unspecified";
        public const string DefaultRunnerKind = "local";
        public const string DefaultTransportBackend = "SaeaTransport";

        private const string RunnerIdEnvironmentVariable = "HPS_BENCHMARK_RUNNER_ID";
        private const string RunnerKindEnvironmentVariable = "HPS_BENCHMARK_RUNNER_KIND";

        public BenchmarkRunIdentity(
            string benchmarkProfile,
            string runnerId,
            string runnerKind,
            string transportBackend,
            string osDescription,
            string osArchitecture,
            string processArchitecture,
            string frameworkDescription,
            int processorCount)
        {
            BenchmarkProfile = NormalizeRequired(benchmarkProfile, nameof(benchmarkProfile));
            RunnerId = NormalizeRequired(runnerId, nameof(runnerId));
            RunnerKind = NormalizeRequired(runnerKind, nameof(runnerKind));
            TransportBackend = NormalizeRequired(transportBackend, nameof(transportBackend));
            OsDescription = NormalizeRequired(osDescription, nameof(osDescription));
            OsArchitecture = NormalizeRequired(osArchitecture, nameof(osArchitecture));
            ProcessArchitecture = NormalizeRequired(processArchitecture, nameof(processArchitecture));
            FrameworkDescription = NormalizeRequired(frameworkDescription, nameof(frameworkDescription));
            ProcessorCount = processorCount;
        }

        public static BenchmarkRunIdentity Unknown
        {
            get
            {
                return new BenchmarkRunIdentity("unknown", "unknown", "unknown", "unknown", "unknown", "unknown", "unknown", "unknown", 0);
            }
        }

        public string BenchmarkProfile { get; }
        public string RunnerId { get; }
        public string RunnerKind { get; }
        public string TransportBackend { get; }
        public string OsDescription { get; }
        public string OsArchitecture { get; }
        public string ProcessArchitecture { get; }
        public string FrameworkDescription { get; }
        public int ProcessorCount { get; }

        public static BenchmarkRunIdentity CaptureDefault()
        {
            return new BenchmarkRunIdentity(
                DefaultBenchmarkProfile,
                GetEnvironmentOrDefault(RunnerIdEnvironmentVariable, DefaultRunnerId),
                GetEnvironmentOrDefault(RunnerKindEnvironmentVariable, DefaultRunnerKind),
                DefaultTransportBackend,
                RuntimeInformation.OSDescription,
                RuntimeInformation.OSArchitecture.ToString(),
                RuntimeInformation.ProcessArchitecture.ToString(),
                RuntimeInformation.FrameworkDescription,
                Environment.ProcessorCount);
        }

        private static string GetEnvironmentOrDefault(string variable, string fallback)
        {
            string? value = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            return value.Trim();
        }

        private static string NormalizeRequired(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("benchmark identity 값은 비어 있을 수 없습니다.", parameterName);

            return value.Trim();
        }
    }
}
```

- [ ] **Step 8: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BenchmarkRunIdentityTests
```

Expected: PASS, 3 tests.

- [ ] **Step 9: Run benchmark tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 10: Update state docs and commit**

Update `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md` with:
- Task 1 completed.
- Red failure, focused green, benchmark test result.
- Next execution point: Task 2 raw report writer integration.

Commit:

```powershell
git add tests\Hps.Benchmarks\BenchmarkRunIdentity.cs tests\Hps.Benchmarks.Tests\BenchmarkRunIdentityTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: add benchmark run identity model"
```

---

### Task 2: Raw Report Writer Metadata

**Files:**
- Modify: `tests/Hps.Benchmarks/TcpLoopbackRunResult.cs`
- Modify: `tests/Hps.Benchmarks/TcpLoopbackReportWriter.cs`
- Modify: `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - `BenchmarkRunIdentity`
  - `BenchmarkRunIdentity.CaptureDefault()`
- Produces:
  - `TcpLoopbackRunResult.Identity`
  - raw JSON top-level fields:
    - `benchmark-profile`
    - `runner-id`
    - `runner-kind`
    - `transport-backend`
    - `os-description`
    - `os-architecture`
    - `process-architecture`
    - `framework-description`
    - `processor-count`

- [ ] **Step 1: Write failing writer shape test**

Append this test to `BaselineReportReaderWriterTests` before `CreateTempDirectory`:

```csharp
        // raw report 가 runner identity 를 원천 artifact 로 남겨야 summary/history 단계가 비교 가능성을 판단할 수 있다.
        // schema-version 은 그대로 1이고 metadata 는 top-level additive field 로만 추가한다.
        [Fact]
        public void Write_WhenRunResultIsWritten_IncludesRunnerIdentityMetadata()
        {
            string directory = CreateTempDirectory();
            string path = Path.Combine(directory, "load-01.json");
            TcpLoopbackRunResult result = new TcpLoopbackRunResult(
                "load",
                "tcp-loopback-saea-baseline",
                4096,
                100,
                30,
                3000,
                3000,
                3000,
                0,
                2,
                0,
                0,
                0,
                240.0,
                500.0,
                450.0,
                550.0,
                30000);

            TcpLoopbackReportWriter.Write(path, result);

            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement root = document.RootElement;
                JsonElement benchmarkProfile;
                JsonElement runnerId;
                JsonElement runnerKind;
                JsonElement transportBackend;
                JsonElement osDescription;
                JsonElement frameworkDescription;
                JsonElement processorCount;
                Assert.Equal(1, root.GetProperty("schema-version").GetInt32());
                Assert.True(root.TryGetProperty("benchmark-profile", out benchmarkProfile));
                Assert.True(root.TryGetProperty("runner-id", out runnerId));
                Assert.True(root.TryGetProperty("runner-kind", out runnerKind));
                Assert.True(root.TryGetProperty("transport-backend", out transportBackend));
                Assert.True(root.TryGetProperty("os-description", out osDescription));
                Assert.True(root.TryGetProperty("framework-description", out frameworkDescription));
                Assert.True(root.TryGetProperty("processor-count", out processorCount));
                Assert.Equal("tcp-loopback-saea-v1", benchmarkProfile.GetString());
                Assert.False(string.IsNullOrWhiteSpace(runnerId.GetString()));
                Assert.False(string.IsNullOrWhiteSpace(runnerKind.GetString()));
                Assert.Equal("SaeaTransport", transportBackend.GetString());
                Assert.False(string.IsNullOrWhiteSpace(osDescription.GetString()));
                Assert.False(string.IsNullOrWhiteSpace(frameworkDescription.GetString()));
                Assert.True(processorCount.GetInt32() > 0);
            }
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineReportReaderWriterTests.Write_WhenRunResultIsWritten_IncludesRunnerIdentityMetadata
```

Expected: FAIL with `Assert.True()` because `benchmark-profile` is not written yet.

- [ ] **Step 3: Add identity to run result**

Modify `TcpLoopbackRunResult.cs` constructor signature by adding the optional last parameter:

```csharp
            double secondHalfP99LatencyMicroseconds,
            long elapsedMilliseconds,
            BenchmarkRunIdentity? identity = null)
```

Inside the constructor, after `ElapsedMilliseconds = elapsedMilliseconds;` add:

```csharp
            Identity = identity ?? BenchmarkRunIdentity.CaptureDefault();
```

Add property near `ElapsedMilliseconds`:

```csharp
        public BenchmarkRunIdentity Identity { get; }
```

- [ ] **Step 4: Write metadata in raw JSON**

In `TcpLoopbackReportWriter.Write(...)`, after `writer.WriteString("scenario", result.Scenario);` add:

```csharp
                    writer.WriteString("benchmark-profile", result.Identity.BenchmarkProfile);
                    writer.WriteString("runner-id", result.Identity.RunnerId);
                    writer.WriteString("runner-kind", result.Identity.RunnerKind);
                    writer.WriteString("transport-backend", result.Identity.TransportBackend);
                    writer.WriteString("os-description", result.Identity.OsDescription);
                    writer.WriteString("os-architecture", result.Identity.OsArchitecture);
                    writer.WriteString("process-architecture", result.Identity.ProcessArchitecture);
                    writer.WriteString("framework-description", result.Identity.FrameworkDescription);
                    writer.WriteNumber("processor-count", result.Identity.ProcessorCount);
```

- [ ] **Step 5: Run focused writer test**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineReportReaderWriterTests.Write_WhenRunResultIsWritten_IncludesRunnerIdentityMetadata
```

Expected: PASS.

- [ ] **Step 6: Run benchmark tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 7: Update state docs and commit**

Update `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md` with:
- Task 2 completed.
- Red missing-property failure and focused green result.
- Next execution point: Task 3 raw report reader/legacy compatibility.

Commit:

```powershell
git add tests\Hps.Benchmarks\TcpLoopbackRunResult.cs tests\Hps.Benchmarks\TcpLoopbackReportWriter.cs tests\Hps.Benchmarks.Tests\BaselineReportReaderWriterTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: write benchmark runner metadata"
```

---

### Task 3: Raw Report Reader And Legacy Compatibility

**Files:**
- Modify: `tests/Hps.Benchmarks/BaselineReport.cs`
- Modify: `tests/Hps.Benchmarks/BaselineReportReader.cs`
- Modify: `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - `BenchmarkRunIdentity.Unknown`
- Produces:
  - `BaselineReport.Identity`
  - `BaselineReportReader` optional metadata parsing
  - legacy report fallback to `BenchmarkRunIdentity.Unknown`

- [ ] **Step 1: Write failing BaselineReport identity contract test**

Append this test to `BaselineReportReaderWriterTests` before `CreateTempDirectory`:

```csharp
        // summary generator 는 raw report reader 를 통해서만 runner identity 를 볼 수 있다.
        // BaselineReport 에 identity property 가 없으면 이후 comparison signal 을 만들 수 없다.
        [Fact]
        public void Contract_BaselineReportExposesIdentity()
        {
            System.Reflection.PropertyInfo? property = typeof(BaselineReport).GetProperty("Identity");

            Assert.NotNull(property);
            Assert.Equal(typeof(BenchmarkRunIdentity), property!.PropertyType);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineReportReaderWriterTests.Contract_BaselineReportExposesIdentity
```

Expected: FAIL with `Assert.NotNull()` because `BaselineReport.Identity` does not exist.

- [ ] **Step 3: Add BaselineReport identity property with legacy default**

Modify `BaselineReport.cs` constructor signature by adding optional last parameter:

```csharp
            int tcpPendingSendQueueHighWatermark,
            int udpPendingSendQueueHighWatermark,
            BenchmarkRunIdentity? identity = null)
```

Inside the constructor, after `UdpPendingSendQueueHighWatermark = udpPendingSendQueueHighWatermark;` add:

```csharp
            Identity = identity ?? BenchmarkRunIdentity.Unknown;
```

Add property near high-watermark properties:

```csharp
        public BenchmarkRunIdentity Identity { get; }
```

- [ ] **Step 4: Run contract test**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineReportReaderWriterTests.Contract_BaselineReportExposesIdentity
```

Expected: PASS.

- [ ] **Step 5: Add reader behavior tests**

Append these tests to `BaselineReportReaderWriterTests`:

```csharp
        // reader 는 신규 raw report 의 runner metadata 를 보존해야 한다.
        // 이 값이 사라지면 summary/history 단계에서 서로 다른 runner 값을 같은 비교군으로 오판할 수 있다.
        [Fact]
        public void ReadDirectory_WhenRunReportHasRunnerIdentity_ReadsIdentityMetadata()
        {
            string directory = CreateTempDirectory();
            WriteRunJsonWithIdentity(Path.Combine(directory, "load-01.json"), "load", "dev-box-a", "self-hosted");

            BaselineReport report = BaselineReportReader.ReadDirectory(directory).Single();

            Assert.Equal("tcp-loopback-saea-v1", report.Identity.BenchmarkProfile);
            Assert.Equal("dev-box-a", report.Identity.RunnerId);
            Assert.Equal("self-hosted", report.Identity.RunnerKind);
            Assert.Equal("SaeaTransport", report.Identity.TransportBackend);
            Assert.Equal("Windows", report.Identity.OsDescription);
            Assert.Equal("X64", report.Identity.OsArchitecture);
            Assert.Equal("X64", report.Identity.ProcessArchitecture);
            Assert.Equal(".NET 9.0", report.Identity.FrameworkDescription);
            Assert.Equal(16, report.Identity.ProcessorCount);
        }

        // 과거 baseline artifact 는 metadata field 가 없다.
        // legacy report 를 읽을 때 crash 하거나 값을 지어내지 말고 unknown identity 로 보존해야 history 재생성이 안전하다.
        [Fact]
        public void ReadDirectory_WhenLegacyRunReportHasNoRunnerIdentity_UsesUnknownIdentity()
        {
            string directory = CreateTempDirectory();
            WriteRunJson(Path.Combine(directory, "load-01.json"), "load", 500.0, 1, 0, 3000);

            BaselineReport report = BaselineReportReader.ReadDirectory(directory).Single();

            Assert.Equal("unknown", report.Identity.BenchmarkProfile);
            Assert.Equal("unknown", report.Identity.RunnerId);
            Assert.Equal("unknown", report.Identity.RunnerKind);
            Assert.Equal(0, report.Identity.ProcessorCount);
        }
```

Add helper below `WriteRunJson`:

```csharp
        private static void WriteRunJsonWithIdentity(string path, string resultName, string runnerId, string runnerKind)
        {
            string json = "{"
                + "\"schema-version\":1,"
                + "\"result-name\":\"" + resultName + "\","
                + "\"passed\":true,"
                + "\"scenario\":\"tcp-loopback-saea-baseline\","
                + "\"benchmark-profile\":\"tcp-loopback-saea-v1\","
                + "\"runner-id\":\"" + runnerId + "\","
                + "\"runner-kind\":\"" + runnerKind + "\","
                + "\"transport-backend\":\"SaeaTransport\","
                + "\"os-description\":\"Windows\","
                + "\"os-architecture\":\"X64\","
                + "\"process-architecture\":\"X64\","
                + "\"framework-description\":\".NET 9.0\","
                + "\"processor-count\":16,"
                + "\"payload-bytes\":4096,"
                + "\"target-rate-hz\":100,"
                + "\"target-duration-seconds\":30,"
                + "\"planned-message-count\":3000,"
                + "\"sent\":3000,"
                + "\"received\":3000,"
                + "\"dropped\":0,"
                + "\"tcp-pending-send-queue-high-watermark\":1,"
                + "\"udp-pending-send-queue-high-watermark\":0,"
                + "\"payload-errors\":0,"
                + "\"pool-rented\":0,"
                + "\"actual-rate-hz\":99.9,"
                + "\"p50-latency-us\":240.0,"
                + "\"p99-latency-us\":500.0,"
                + "\"first-half-p99-latency-us\":500.0,"
                + "\"second-half-p99-latency-us\":500.0,"
                + "\"p99-latency-growth-ratio\":1.0,"
                + "\"elapsed-ms\":30000"
                + "}";
            File.WriteAllText(path, json);
        }
```

- [ ] **Step 6: Run reader behavior tests to verify they fail**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineReportReaderWriterTests.ReadDirectory_When
```

Expected: at least `ReadDirectory_WhenRunReportHasRunnerIdentity_ReadsIdentityMetadata` fails because reader still returns `Unknown`.

- [ ] **Step 7: Parse optional identity fields**

In `BaselineReportReader.TryReadReport(...)`, create identity before `return new BaselineReport(...)`:

```csharp
                BenchmarkRunIdentity identity = ReadIdentity(root);
```

Pass it as the last constructor argument:

```csharp
                    GetInt(root, "tcp-pending-send-queue-high-watermark"),
                    GetInt(root, "udp-pending-send-queue-high-watermark"),
                    identity);
```

Add helper methods:

```csharp
        private static BenchmarkRunIdentity ReadIdentity(JsonElement root)
        {
            JsonElement benchmarkProfile;
            if (!root.TryGetProperty("benchmark-profile", out benchmarkProfile))
                return BenchmarkRunIdentity.Unknown;

            return new BenchmarkRunIdentity(
                benchmarkProfile.GetString()!,
                GetOptionalString(root, "runner-id"),
                GetOptionalString(root, "runner-kind"),
                GetOptionalString(root, "transport-backend"),
                GetOptionalString(root, "os-description"),
                GetOptionalString(root, "os-architecture"),
                GetOptionalString(root, "process-architecture"),
                GetOptionalString(root, "framework-description"),
                GetOptionalInt(root, "processor-count"));
        }

        private static string GetOptionalString(JsonElement root, string name)
        {
            JsonElement value;
            if (!root.TryGetProperty(name, out value))
                return "unknown";

            string? text = value.GetString();
            if (string.IsNullOrWhiteSpace(text))
                return "unknown";

            return text;
        }

        private static int GetOptionalInt(JsonElement root, string name)
        {
            JsonElement value;
            if (!root.TryGetProperty(name, out value) || value.ValueKind != JsonValueKind.Number)
                return 0;

            return value.GetInt32();
        }
```

- [ ] **Step 8: Run focused reader tests**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter FullyQualifiedName~BaselineReportReaderWriterTests
```

Expected: PASS.

- [ ] **Step 9: Run benchmark tests and solution verification**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore
git diff --check
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
```

Expected:
- benchmark tests PASS.
- `git diff --check` has no whitespace errors. CRLF conversion warnings are acceptable.
- solution build: warning 0, error 0.
- solution tests: 239 or more tests pass, 0 fail.

- [ ] **Step 10: Update state docs and commit**

Update `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md` with:
- Task 3 completed.
- legacy report fallback behavior.
- final verification results.
- next candidate: summary/history comparison signal design or implementation planning.

Commit:

```powershell
git add tests\Hps.Benchmarks\BaselineReport.cs tests\Hps.Benchmarks\BaselineReportReader.cs tests\Hps.Benchmarks.Tests\BaselineReportReaderWriterTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: read benchmark runner metadata"
```

---

## Plan Self-Review

- Spec coverage: D079 raw metadata fields, privacy 기본값, environment override, schema-version 1 additive writer, legacy read fallback 이 Task 1~3에 매핑되어 있다.
- Scope control: summary/history comparison signal, warning-as-failure, CI workflow, latency hard gate 는 제외했다.
- Type consistency: `BenchmarkRunIdentity`, `TcpLoopbackRunResult.Identity`, `BaselineReport.Identity` 이름을 전 Task 에서 동일하게 사용한다.
- Commit boundary: model, writer, reader 를 각각 별도 커밋으로 나눴다.
- Validation path: 각 Task 는 assertion failure Red, focused green, state doc update, commit 을 포함한다.
