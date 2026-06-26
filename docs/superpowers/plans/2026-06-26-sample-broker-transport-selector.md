# Sample Broker Transport Selector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `samples/Hps.Sample.BrokerServer`에 optional `--transport <saea|rio|auto>` selector 를 추가한다.

**Architecture:** base `TransportFactory.CreateDefault()`와 `Hps.Server`는 건드리지 않는다. sample host 내부에 CLI parser, selection policy, Program wiring 을 둔다. RIO capability 는 selector 에 주입 가능하게 만들어 tests 가 실제 OS/RIO availability 에 의존하지 않게 한다.

**Tech Stack:** .NET 9, C# 8, xUnit, `Hps.Transport`, `Hps.Transport.Rio`, `Hps.Sample.BrokerServer`.

---

## File Structure

- Create `tests/Hps.Sample.BrokerServer.Tests/Hps.Sample.BrokerServer.Tests.csproj`
  - sample broker host 의 CLI parser/selector 를 검증하는 xUnit test project.
- Modify `HighPerformanceSocket.slnx`
  - 새 sample test project 를 `/tests/` folder 에 추가한다.
- Create `tests/Hps.Sample.BrokerServer.Tests/SampleBrokerServerCommandParserTests.cs`
  - 기존 3 positional args 호환성과 `--transport` parser contract 를 검증한다.
- Create `tests/Hps.Sample.BrokerServer.Tests/SampleTransportSelectorTests.cs`
  - `saea`/`rio`/`auto` selection policy 를 capability status 별로 검증한다.
- Create `tests/Hps.Sample.BrokerServer.Tests/SampleBrokerServerProgramTests.cs`
  - Program usage error path 와 output wiring 을 검증한다.
- Create `samples/Hps.Sample.BrokerServer/SampleTransportMode.cs`
  - `Saea`, `Rio`, `Auto` enum.
- Create `samples/Hps.Sample.BrokerServer/SampleBrokerServerCommandLine.cs`
  - parsed host/port/max-frame-bytes/transport mode value object.
- Create `samples/Hps.Sample.BrokerServer/SampleBrokerServerCommandParser.cs`
  - CLI parser. Program 의 argument validation 을 여기로 이동한다.
- Create `samples/Hps.Sample.BrokerServer/SampleTransportSelection.cs`
  - selector 결과. success/failure, selected backend name, notice/error, transport owner 를 담는다.
- Create `samples/Hps.Sample.BrokerServer/SampleTransportSelector.cs`
  - `SampleTransportMode` + capability probe + factory delegates 로 concrete transport 를 선택한다.
- Modify `samples/Hps.Sample.BrokerServer/Hps.Sample.BrokerServer.csproj`
  - `src/Hps.Transport.Rio` project reference 를 추가한다.
- Modify `samples/Hps.Sample.BrokerServer/Program.cs`
  - parser/selector 를 사용해 transport 를 만든다.
  - startup output 에 selected backend 를 출력한다.

---

### Task 1: CLI parser/model 추가

**Files:**
- Create: `tests/Hps.Sample.BrokerServer.Tests/Hps.Sample.BrokerServer.Tests.csproj`
- Modify: `HighPerformanceSocket.slnx`
- Create: `tests/Hps.Sample.BrokerServer.Tests/SampleBrokerServerCommandParserTests.cs`
- Create: `samples/Hps.Sample.BrokerServer/SampleTransportMode.cs`
- Create: `samples/Hps.Sample.BrokerServer/SampleBrokerServerCommandLine.cs`
- Create: `samples/Hps.Sample.BrokerServer/SampleBrokerServerCommandParser.cs`

- [x] **Step 1: test project 를 추가한다**

`tests/Hps.Sample.BrokerServer.Tests/Hps.Sample.BrokerServer.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
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
    <ProjectReference Include="..\..\samples\Hps.Sample.BrokerServer\Hps.Sample.BrokerServer.csproj" />
  </ItemGroup>
</Project>
```

`HighPerformanceSocket.slnx`의 `/tests/` folder 에 아래 project 를 추가한다.

```xml
<Project Path="tests/Hps.Sample.BrokerServer.Tests/Hps.Sample.BrokerServer.Tests.csproj" />
```

- [x] **Step 2: failing parser tests 를 작성한다**

`tests/Hps.Sample.BrokerServer.Tests/SampleBrokerServerCommandParserTests.cs`:

```csharp
using System;
using System.Reflection;
using Xunit;

namespace Hps.Sample.BrokerServer.Tests
{
    public sealed class SampleBrokerServerCommandParserTests
    {
        // 기존 sample 실행 명령은 transport option 없이도 SAEA mode 로 해석되어야 한다.
        // 이 호환성이 깨지면 기존 사용자가 RIO 선택 기능 추가만으로 sample 을 실행하지 못한다.
        [Fact]
        public void TryParse_WhenTransportOptionIsOmitted_ReturnsSaeaMode()
        {
            object commandLine;
            string? errorMessage;

            bool parsed = TryParse(
                new[] { "127.0.0.1", "5000", "65536" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.Null(errorMessage);
            Assert.Equal("127.0.0.1", GetProperty(commandLine, "Host"));
            Assert.Equal(5000, GetProperty(commandLine, "Port"));
            Assert.Equal(65536, GetProperty(commandLine, "MaxFrameBytes"));
            Assert.Equal("Saea", GetProperty(commandLine, "TransportMode")!.ToString());
        }

        // RIO 명시 선택은 parser 단계에서 보존되어야 한다.
        // 실제 RIO availability 판단은 selector 가 맡고, parser 는 사용자의 의도를 잃지 않아야 한다.
        [Fact]
        public void TryParse_WhenTransportRioIsProvided_ReturnsRioMode()
        {
            object commandLine;
            string? errorMessage;

            bool parsed = TryParse(
                new[] { "loopback", "5000", "65536", "--transport", "rio" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.Null(errorMessage);
            Assert.Equal("Rio", GetProperty(commandLine, "TransportMode")!.ToString());
        }

        // auto 는 fallback 을 허용하는 preferred policy 이므로 explicit rio 와 다른 값으로 보존해야 한다.
        [Fact]
        public void TryParse_WhenTransportAutoIsProvided_ReturnsAutoMode()
        {
            object commandLine;
            string? errorMessage;

            bool parsed = TryParse(
                new[] { "loopback", "5000", "65536", "--transport", "auto" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.Null(errorMessage);
            Assert.Equal("Auto", GetProperty(commandLine, "TransportMode")!.ToString());
        }

        // option 값 누락은 broker 시작 전에 usage error 로 멈춰야 한다.
        [Fact]
        public void TryParse_WhenTransportValueIsMissing_ReturnsError()
        {
            object commandLine;
            string? errorMessage;

            bool parsed = TryParse(
                new[] { "127.0.0.1", "5000", "65536", "--transport" },
                out commandLine,
                out errorMessage);

            Assert.False(parsed);
            Assert.Equal("--transport 옵션에는 saea, rio 또는 auto 값이 필요합니다.", errorMessage);
        }

        // 알 수 없는 transport 값은 fallback 하지 않고 usage error 로 처리한다.
        [Fact]
        public void TryParse_WhenTransportValueIsUnknown_ReturnsError()
        {
            object commandLine;
            string? errorMessage;

            bool parsed = TryParse(
                new[] { "127.0.0.1", "5000", "65536", "--transport", "fast" },
                out commandLine,
                out errorMessage);

            Assert.False(parsed);
            Assert.Equal("--transport 옵션은 saea, rio 또는 auto 값만 사용할 수 있습니다.", errorMessage);
        }

        private static bool TryParse(string[] args, out object commandLine, out string? errorMessage)
        {
            Assembly assembly = Assembly.Load("Hps.Sample.BrokerServer");
            Type? parserType = assembly.GetType("Hps.Sample.BrokerServer.SampleBrokerServerCommandParser");
            Assert.NotNull(parserType);
            Type? commandLineType = assembly.GetType("Hps.Sample.BrokerServer.SampleBrokerServerCommandLine");
            Assert.NotNull(commandLineType);

            object?[] parameters = new object?[] { args, null, null };
            bool parsed = (bool)parserType!.GetMethod("TryParse", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, parameters)!;
            commandLine = parameters[1]!;
            errorMessage = (string?)parameters[2];
            return parsed;
        }

        private static object? GetProperty(object instance, string name)
        {
            return instance.GetType().GetProperty(name)!.GetValue(instance);
        }
    }
}
```

- [x] **Step 3: Red test 를 실행해 parser type 부재 실패를 확인한다**

Run:

```powershell
dotnet test tests\Hps.Sample.BrokerServer.Tests\Hps.Sample.BrokerServer.Tests.csproj --no-restore --filter "FullyQualifiedName~SampleBrokerServerCommandParserTests"
```

Expected:

```text
Assert.NotNull() Failure: Value is null
```

- [x] **Step 4: minimal parser/model 을 구현한다**

`samples/Hps.Sample.BrokerServer/SampleTransportMode.cs`:

```csharp
namespace Hps.Sample.BrokerServer
{
    public enum SampleTransportMode
    {
        Saea,
        Rio,
        Auto
    }
}
```

`samples/Hps.Sample.BrokerServer/SampleBrokerServerCommandLine.cs`:

```csharp
namespace Hps.Sample.BrokerServer
{
    public sealed class SampleBrokerServerCommandLine
    {
        internal SampleBrokerServerCommandLine(string host, int port, int maxFrameBytes, SampleTransportMode transportMode)
        {
            Host = host;
            Port = port;
            MaxFrameBytes = maxFrameBytes;
            TransportMode = transportMode;
        }

        public string Host { get; }

        public int Port { get; }

        public int MaxFrameBytes { get; }

        public SampleTransportMode TransportMode { get; }
    }
}
```

`samples/Hps.Sample.BrokerServer/SampleBrokerServerCommandParser.cs`:

```csharp
using System;

namespace Hps.Sample.BrokerServer
{
    public static class SampleBrokerServerCommandParser
    {
        public const string MessageTransportValueRequired = "--transport 옵션에는 saea, rio 또는 auto 값이 필요합니다.";
        public const string MessageTransportValueInvalid = "--transport 옵션은 saea, rio 또는 auto 값만 사용할 수 있습니다.";

        public static bool TryParse(string[] args, out SampleBrokerServerCommandLine? commandLine, out string? errorMessage)
        {
            commandLine = null;
            errorMessage = null;

            if (args.Length != 3 && args.Length != 5)
                return false;

            int port;
            if (!int.TryParse(args[1], out port) || port <= 0 || port > 65535)
                return false;

            int maxFrameBytes;
            if (!int.TryParse(args[2], out maxFrameBytes) || maxFrameBytes <= 0)
                return false;

            SampleTransportMode transportMode = SampleTransportMode.Saea;
            if (args.Length == 5)
            {
                if (!string.Equals(args[3], "--transport", StringComparison.OrdinalIgnoreCase))
                    return false;

                if (!TryParseTransportMode(args[4], out transportMode, out errorMessage))
                    return false;
            }

            commandLine = new SampleBrokerServerCommandLine(args[0], port, maxFrameBytes, transportMode);
            return true;
        }

        private static bool TryParseTransportMode(string value, out SampleTransportMode mode, out string? errorMessage)
        {
            mode = SampleTransportMode.Saea;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                errorMessage = MessageTransportValueRequired;
                return false;
            }

            if (string.Equals(value, "saea", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(value, "rio", StringComparison.OrdinalIgnoreCase))
            {
                mode = SampleTransportMode.Rio;
                return true;
            }

            if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
            {
                mode = SampleTransportMode.Auto;
                return true;
            }

            errorMessage = MessageTransportValueInvalid;
            return false;
        }
    }
}
```

- [x] **Step 5: parser tests 를 통과시킨다**

Run:

```powershell
dotnet test tests\Hps.Sample.BrokerServer.Tests\Hps.Sample.BrokerServer.Tests.csproj --no-restore --filter "FullyQualifiedName~SampleBrokerServerCommandParserTests"
```

Expected:

```text
Passed!  - Failed: 0, Passed: 5
```

- [x] **Step 6: Task 1 commit**

```powershell
git add HighPerformanceSocket.slnx samples\Hps.Sample.BrokerServer\SampleTransportMode.cs samples\Hps.Sample.BrokerServer\SampleBrokerServerCommandLine.cs samples\Hps.Sample.BrokerServer\SampleBrokerServerCommandParser.cs tests\Hps.Sample.BrokerServer.Tests\Hps.Sample.BrokerServer.Tests.csproj tests\Hps.Sample.BrokerServer.Tests\SampleBrokerServerCommandParserTests.cs
git commit -m "test: add sample broker transport parser contract"
```

---

### Task 2: transport selector policy 추가

**Files:**
- Modify: `samples/Hps.Sample.BrokerServer/Hps.Sample.BrokerServer.csproj`
- Create: `samples/Hps.Sample.BrokerServer/SampleTransportSelection.cs`
- Create: `samples/Hps.Sample.BrokerServer/SampleTransportSelector.cs`
- Create: `tests/Hps.Sample.BrokerServer.Tests/SampleTransportSelectorTests.cs`

- [x] **Step 1: failing selector tests 를 작성한다**

`tests/Hps.Sample.BrokerServer.Tests/SampleTransportSelectorTests.cs`:

```csharp
using System;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hps.Transport;
using Xunit;

namespace Hps.Sample.BrokerServer.Tests
{
    public sealed class SampleTransportSelectorTests
    {
        // saea mode 는 capability probe 없이 SAEA factory 만 호출해야 한다.
        [Fact]
        public void Select_WhenModeIsSaea_ReturnsSaeaTransport()
        {
            object selection = Select("Saea", RioCapabilityStatus.Available);

            Assert.True((bool)GetProperty(selection, "Succeeded")!);
            Assert.Equal("SaeaTransport", GetProperty(selection, "SelectedBackendName"));
            Assert.Null(GetProperty(selection, "ErrorMessage"));
        }

        // explicit rio 는 available 일 때만 RIO backend 를 선택한다.
        [Fact]
        public void Select_WhenModeIsRioAndAvailable_ReturnsRioTransport()
        {
            object selection = Select("Rio", RioCapabilityStatus.Available);

            Assert.True((bool)GetProperty(selection, "Succeeded")!);
            Assert.Equal("RioTransport", GetProperty(selection, "SelectedBackendName"));
        }

        // explicit rio 는 unavailable 시 fallback 하지 않고 실패한다.
        [Fact]
        public void Select_WhenModeIsRioAndUnavailable_ReturnsFailure()
        {
            object selection = Select("Rio", RioCapabilityStatus.Unavailable);

            Assert.False((bool)GetProperty(selection, "Succeeded")!);
            Assert.Equal(1, GetProperty(selection, "ExitCode"));
            Assert.Contains("RIO transport를 사용할 수 없습니다.", (string)GetProperty(selection, "ErrorMessage")!);
        }

        // auto 는 RIO available 시 RIO를 선택한다.
        [Fact]
        public void Select_WhenModeIsAutoAndAvailable_ReturnsRioTransport()
        {
            object selection = Select("Auto", RioCapabilityStatus.Available);

            Assert.True((bool)GetProperty(selection, "Succeeded")!);
            Assert.Equal("RioTransport", GetProperty(selection, "SelectedBackendName"));
            Assert.Null(GetProperty(selection, "NoticeMessage"));
        }

        // auto 는 unsupported/unavailable 시 SAEA로 fallback 하고 그 사실을 notice 로 남긴다.
        [Fact]
        public void Select_WhenModeIsAutoAndUnsupported_ReturnsSaeaWithNotice()
        {
            object selection = Select("Auto", RioCapabilityStatus.UnsupportedOperatingSystem);

            Assert.True((bool)GetProperty(selection, "Succeeded")!);
            Assert.Equal("SaeaTransport", GetProperty(selection, "SelectedBackendName"));
            Assert.Contains("RIO unavailable", (string)GetProperty(selection, "NoticeMessage")!);
        }

        private static object Select(string modeName, RioCapabilityStatus status)
        {
            Assembly assembly = Assembly.Load("Hps.Sample.BrokerServer");
            Type? modeType = assembly.GetType("Hps.Sample.BrokerServer.SampleTransportMode");
            Type? selectorType = assembly.GetType("Hps.Sample.BrokerServer.SampleTransportSelector");
            Assert.NotNull(modeType);
            Assert.NotNull(selectorType);

            object mode = Enum.Parse(modeType!, modeName);
            Func<RioCapabilityStatus> probe = delegate { return status; };
            Func<ITransport> createSaea = delegate { return new FakeTransport("SaeaTransport"); };
            Func<ITransport> createRio = delegate { return new FakeTransport("RioTransport"); };
            return selectorType!.GetMethod("Select", BindingFlags.Public | BindingFlags.Static)!.Invoke(
                null,
                new object[] { mode, probe, createSaea, createRio })!;
        }

        private static object? GetProperty(object instance, string name)
        {
            return instance.GetType().GetProperty(name)!.GetValue(instance);
        }

        private sealed class FakeTransport : TransportBase
        {
            public FakeTransport(string name)
            {
                Name = name;
            }

            public string Name { get; }

            public override ValueTask StartAsync(CancellationToken cancellationToken = default)
            {
                return new ValueTask();
            }

            public override ValueTask StopAsync(CancellationToken cancellationToken = default)
            {
                return new ValueTask();
            }

            public override ValueTask<IConnectionListener> ListenTcpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public override ValueTask<IConnection> ConnectTcpAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
        }
    }
}
```

- [x] **Step 2: Red test 를 실행해 selector type 부재 실패를 확인한다**

Run:

```powershell
dotnet test tests\Hps.Sample.BrokerServer.Tests\Hps.Sample.BrokerServer.Tests.csproj --no-restore --filter "FullyQualifiedName~SampleTransportSelectorTests"
```

Expected:

```text
Assert.NotNull() Failure: Value is null
```

- [x] **Step 3: sample project 에 RIO reference 를 추가한다**

`samples/Hps.Sample.BrokerServer/Hps.Sample.BrokerServer.csproj`:

```xml
<ProjectReference Include="..\..\src\Hps.Transport.Rio\Hps.Transport.Rio.csproj" />
```

- [x] **Step 4: selection result 와 selector 를 구현한다**

`samples/Hps.Sample.BrokerServer/SampleTransportSelection.cs`:

```csharp
using Hps.Transport;

namespace Hps.Sample.BrokerServer
{
    public sealed class SampleTransportSelection
    {
        private SampleTransportSelection(
            bool succeeded,
            ITransport? transport,
            string? selectedBackendName,
            string? noticeMessage,
            string? errorMessage,
            int exitCode)
        {
            Succeeded = succeeded;
            Transport = transport;
            SelectedBackendName = selectedBackendName;
            NoticeMessage = noticeMessage;
            ErrorMessage = errorMessage;
            ExitCode = exitCode;
        }

        public bool Succeeded { get; }

        public ITransport? Transport { get; }

        public string? SelectedBackendName { get; }

        public string? NoticeMessage { get; }

        public string? ErrorMessage { get; }

        public int ExitCode { get; }

        public static SampleTransportSelection Success(ITransport transport, string selectedBackendName, string? noticeMessage)
        {
            return new SampleTransportSelection(true, transport, selectedBackendName, noticeMessage, null, 0);
        }

        public static SampleTransportSelection Failure(string errorMessage, int exitCode)
        {
            return new SampleTransportSelection(false, null, null, null, errorMessage, exitCode);
        }
    }
}
```

`samples/Hps.Sample.BrokerServer/SampleTransportSelector.cs`:

```csharp
using System;
using Hps.Transport;

namespace Hps.Sample.BrokerServer
{
    public static class SampleTransportSelector
    {
        public const int RuntimeFailureExitCode = 1;

        public static SampleTransportSelection Select(
            SampleTransportMode mode,
            Func<RioCapabilityStatus> getRioStatus,
            Func<ITransport> createSaea,
            Func<ITransport> createRio)
        {
            if (getRioStatus == null)
                throw new ArgumentNullException(nameof(getRioStatus));
            if (createSaea == null)
                throw new ArgumentNullException(nameof(createSaea));
            if (createRio == null)
                throw new ArgumentNullException(nameof(createRio));

            if (mode == SampleTransportMode.Saea)
                return SampleTransportSelection.Success(createSaea(), "SaeaTransport", null);

            RioCapabilityStatus status = getRioStatus();
            if (mode == SampleTransportMode.Rio)
            {
                if (status == RioCapabilityStatus.Available)
                    return SampleTransportSelection.Success(createRio(), "RioTransport", null);

                return SampleTransportSelection.Failure(
                    "RIO transport를 사용할 수 없습니다. status=" + status,
                    RuntimeFailureExitCode);
            }

            if (status == RioCapabilityStatus.Available)
                return SampleTransportSelection.Success(createRio(), "RioTransport", null);

            return SampleTransportSelection.Success(
                createSaea(),
                "SaeaTransport",
                "RIO unavailable; falling back to SaeaTransport. status=" + status);
        }
    }
}
```

- [x] **Step 5: selector tests 를 통과시킨다**

Run:

```powershell
dotnet test tests\Hps.Sample.BrokerServer.Tests\Hps.Sample.BrokerServer.Tests.csproj --no-restore --filter "FullyQualifiedName~SampleTransportSelectorTests"
```

Expected:

```text
Passed!  - Failed: 0, Passed: 5
```

- [x] **Step 6: Task 2 commit**

```powershell
git add samples\Hps.Sample.BrokerServer\Hps.Sample.BrokerServer.csproj samples\Hps.Sample.BrokerServer\SampleTransportSelection.cs samples\Hps.Sample.BrokerServer\SampleTransportSelector.cs tests\Hps.Sample.BrokerServer.Tests\SampleTransportSelectorTests.cs
git commit -m "feat: add sample broker transport selector"
```

---

### Task 3: Program wiring과 smoke 검증

**Files:**
- Modify: `samples/Hps.Sample.BrokerServer/Program.cs`
- Create: `tests/Hps.Sample.BrokerServer.Tests/SampleBrokerServerProgramTests.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

- [ ] **Step 1: failing Program usage tests 를 작성한다**

`tests/Hps.Sample.BrokerServer.Tests/SampleBrokerServerProgramTests.cs`:

```csharp
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Hps.Sample.BrokerServer.Tests
{
    public sealed class SampleBrokerServerProgramTests
    {
        // Program wiring 은 parser error 를 broker start 이전에 exit code 2로 반환해야 한다.
        // 이 테스트는 valid endpoint 를 열지 않으므로 Ctrl+C wait 경로에 들어가지 않는다.
        [Fact]
        public async Task Main_WhenTransportValueIsMissing_ReturnsInvalidArgumentsAndTransportUsage()
        {
            Tuple<int, string> result = await InvokeMainWithCapturedErrorAsync(
                new[] { "127.0.0.1", "5000", "65536", "--transport" });

            Assert.Equal(2, result.Item1);
            Assert.Contains("--transport <saea|rio|auto>", result.Item2);
        }

        // unknown transport 는 fallback 하지 않고 usage error 로 끝난다.
        [Fact]
        public async Task Main_WhenTransportValueIsUnknown_ReturnsInvalidArgumentsAndTransportUsage()
        {
            Tuple<int, string> result = await InvokeMainWithCapturedErrorAsync(
                new[] { "127.0.0.1", "5000", "65536", "--transport", "fast" });

            Assert.Equal(2, result.Item1);
            Assert.Contains("--transport <saea|rio|auto>", result.Item2);
        }

        private static async Task<Tuple<int, string>> InvokeMainWithCapturedErrorAsync(string[] args)
        {
            TextWriter originalError = System.Console.Error;
            using (StringWriter writer = new StringWriter())
            {
                System.Console.SetError(writer);
                try
                {
                    int exitCode = await InvokeMainAsync(args).ConfigureAwait(false);
                    return Tuple.Create(exitCode, writer.ToString());
                }
                finally
                {
                    System.Console.SetError(originalError);
                }
            }
        }

        private static async Task<int> InvokeMainAsync(string[] args)
        {
            Assembly assembly = Assembly.Load("Hps.Sample.BrokerServer");
            Type? programType = assembly.GetType("Hps.Sample.BrokerServer.Program");
            Assert.NotNull(programType);
            MethodInfo? main = programType!.GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(main);

            Task<int> task = (Task<int>)main!.Invoke(null, new object[] { args })!;
            return await task.ConfigureAwait(false);
        }
    }
}
```

- [ ] **Step 2: Red test 를 실행해 기존 args.Length==3 path 때문에 실패를 확인한다**

Run:

```powershell
dotnet test tests\Hps.Sample.BrokerServer.Tests\Hps.Sample.BrokerServer.Tests.csproj --no-restore --filter "FullyQualifiedName~SampleBrokerServerProgramTests"
```

Expected:

```text
Assert.Contains() Failure
Not found: --transport <saea|rio|auto>
```

- [ ] **Step 3: Program 을 parser/selector 로 wiring 한다**

Modify `samples/Hps.Sample.BrokerServer/Program.cs`:

```csharp
private const int RuntimeFailureExitCode = 1;
```

Replace the initial argument parsing with:

```csharp
SampleBrokerServerCommandLine? commandLine;
string? parseError;
if (!SampleBrokerServerCommandParser.TryParse(args, out commandLine, out parseError))
{
    if (parseError != null)
        Console.Error.WriteLine(parseError);

    PrintUsage();
    return InvalidArgumentsExitCode;
}

IPAddress address;
if (!TryParseAddress(commandLine.Host, out address))
{
    Console.Error.WriteLine("host 는 IP 주소, localhost, loopback, any 또는 * 이어야 합니다.");
    return InvalidArgumentsExitCode;
}
```

Replace the transport creation with:

```csharp
SampleTransportSelection selection = SampleTransportSelector.Select(
    commandLine.TransportMode,
    RioCapabilityProbe.GetStatus,
    delegate { return new SaeaTransport(); },
    delegate { return new RioTransport(); });

if (!selection.Succeeded)
{
    Console.Error.WriteLine(selection.ErrorMessage);
    return selection.ExitCode == 0 ? RuntimeFailureExitCode : selection.ExitCode;
}

if (selection.NoticeMessage != null)
    Console.Error.WriteLine(selection.NoticeMessage);

using (ITransport transport = selection.Transport!)
{
    PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(commandLine.MaxFrameBytes);
    using (BrokerHost server = new BrokerHost(transport, pool, commandLine.MaxFrameBytes))
    {
        IPEndPoint listenEndPoint = new IPEndPoint(address, commandLine.Port);
        await server.StartTcpAsync(listenEndPoint).ConfigureAwait(false);

        Console.WriteLine("broker 시작: endpoint={0}, max-frame-bytes={1}, transport={2}", server.LocalEndPoint, commandLine.MaxFrameBytes, selection.SelectedBackendName);
        Console.WriteLine("종료하려면 Ctrl+C 를 누르십시오.");

        await WaitForCtrlCAsync().ConfigureAwait(false);
        await server.StopAsync().ConfigureAwait(false);
    }
}
```

Update `PrintUsage()`:

```csharp
Console.Error.WriteLine("사용법: Hps.Sample.BrokerServer <host> <port> <max-frame-bytes> [--transport <saea|rio|auto>]");
Console.Error.WriteLine("예시: Hps.Sample.BrokerServer 127.0.0.1 5000 65536");
Console.Error.WriteLine("예시: Hps.Sample.BrokerServer 127.0.0.1 5000 65536 --transport auto");
```

- [ ] **Step 4: focused sample tests 를 실행한다**

Run:

```powershell
dotnet test tests\Hps.Sample.BrokerServer.Tests\Hps.Sample.BrokerServer.Tests.csproj --no-restore
```

Expected:

```text
Passed!  - Failed: 0
```

- [ ] **Step 5: solution build/test 를 실행한다**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
git diff --check
```

Expected:

```text
Build succeeded.
Failed: 0
git diff --check exits 0
```

- [ ] **Step 6: 상태 문서를 갱신한다**

Update `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`:

- Task 1~3 완료 결과.
- Red/Green evidence.
- 다음 실행 후보.
- build/test/diff-check 결과.

- [ ] **Step 7: Task 3 commit**

```powershell
git add samples\Hps.Sample.BrokerServer\Program.cs tests\Hps.Sample.BrokerServer.Tests\SampleBrokerServerProgramTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat: wire sample broker transport selector"
```

---

## Self-Review Checklist

- Spec coverage:
  - optional `--transport <saea|rio|auto>`: Task 1, Task 3.
  - default `saea`: Task 1 parser test, Task 2 selector test.
  - explicit `rio` failure without fallback: Task 2 selector test.
  - `auto` fallback observability: Task 2 selector test, Task 3 Program output.
  - no base factory/server library change: file structure excludes those files.
- Placeholder scan:
  - 이 계획은 placeholder 없이 concrete file, method, command, expected result 를 포함한다.
- Type consistency:
  - `SampleTransportMode`, `SampleBrokerServerCommandLine`, `SampleBrokerServerCommandParser`, `SampleTransportSelection`, `SampleTransportSelector` 이름을 모든 task 에서 동일하게 사용한다.
