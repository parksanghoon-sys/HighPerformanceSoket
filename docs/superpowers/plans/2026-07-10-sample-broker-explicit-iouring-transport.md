# Sample Broker Explicit io_uring Transport Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Linux 사용자가 `Hps.Sample.BrokerServer`에서 `--transport iouring`을 명시해 io_uring TCP broker를 실행할 수 있게 한다.

**Architecture:** 기존 sample parser와 delegate 기반 selector를 확장한다. explicit io_uring은 capability가 `Available`일 때만 `IoUringTransport`를 만들고 그 외에는 exit code 1로 fail-closed 처리하며, 기존 `saea`, `rio`, `auto`와 `TransportFactory.CreateDefault()` 의미는 유지한다. Linux contract workflow는 solution/WPF 대신 io_uring tests와 sample broker 두 Linux-safe project만 명시적으로 restore/build한다.

**Tech Stack:** .NET 9, C# 8.0, xUnit 2.9.3, PowerShell, GitHub Actions, `Hps.Transport`, `Hps.Transport.Rio`, `Hps.Transport.IoUring`.

## Global Constraints

- TFM은 `net9.0`, 언어 버전은 C# 8.0이며 global using, file-scoped namespace, record, target-typed `new()`를 쓰지 않는다.
- 새 외부 NuGet 의존성을 추가하지 않는다.
- 새 test method 바로 위에는 검증 목적과 보호하는 계약을 설명하는 한국어 주석을 둔다.
- 각 프로덕션 변경은 컴파일되는 assertion failure Red를 먼저 확인한 뒤 최소 Green과 Refactor 순서로 진행한다.
- `auto`는 계속 RIO preferred/SAEA fallback이며 `TransportFactory.CreateDefault()`는 계속 SAEA다.
- explicit `iouring` unavailable은 SAEA로 fallback하지 않고 exit code 1을 반환한다.
- sample broker는 TCP listener만 시작하며 UDP CLI를 추가하지 않는다.
- `.claude/review/`의 기존 미추적 파일은 stage하거나 수정하지 않는다.
- 각 Task는 자체 검증 뒤 해당 파일만 별도 커밋한다. 예상과 다른 실패나 범위 확대가 생기면 다음 Task로 넘어가지 않는다.

---

## File Structure

- Modify `samples/Hps.Sample.BrokerServer/SampleTransportMode.cs`
  - 기존 numeric value를 유지한 채 마지막에 `IoUring` mode를 추가한다.
- Modify `samples/Hps.Sample.BrokerServer/SampleBrokerServerCommandParser.cs`
  - `iouring` token과 갱신된 오류 메시지를 처리한다.
- Modify `samples/Hps.Sample.BrokerServer/SampleTransportSelector.cs`
  - io_uring probe/factory를 받는 7-argument full overload와 explicit fail-closed 분기를 추가한다.
- Modify `samples/Hps.Sample.BrokerServer/Program.cs`
  - full selector overload에 `IoUringCapabilityProbe`와 `IoUringTransport` factory를 주입한다.
- Modify `samples/Hps.Sample.BrokerServer/Hps.Sample.BrokerServer.csproj`
  - `Hps.Transport.IoUring` project reference를 추가한다.
- Modify `tests/Hps.Sample.BrokerServer.Tests/Hps.Sample.BrokerServer.Tests.csproj`
  - selector tests가 io_uring capability 타입을 직접 사용할 수 있도록 동일 project reference를 추가한다.
- Modify `tests/Hps.Sample.BrokerServer.Tests/SampleBrokerServerCommandParserTests.cs`
  - compile-safe parser Red와 오류 문자열 회귀를 검증한다.
- Modify `tests/Hps.Sample.BrokerServer.Tests/SampleTransportSelectorTests.cs`
  - explicit available/unavailable, probe/factory isolation, IPv6, legacy overload 호환성을 검증한다.
- Create `tests/Hps.Sample.BrokerServer.Tests/SampleBrokerServerProjectContractTests.cs`
  - sample project reference와 Program composition source contract를 검증한다.
- Modify `tests/Hps.Sample.BrokerServer.Tests/SampleBrokerServerProgramTests.cs`
  - usage output이 `iouring`을 포함하는지 검증한다.
- Modify `tests/Hps.Benchmarks.Tests/BenchmarkArtifactWorkflowTests.cs`
  - Linux workflow가 명시한 두 project만 restore/build하는지 검증한다.
- Modify `.github/workflows/iouring-linux-contract.yml`
  - sample broker Linux restore/build step을 추가하고 native runtime test 범위는 유지한다.
- Modify `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`, `docs/agent-state/decisions/2026-07.md`
  - local implementation gate와 remote evidence 대기 상태를 기록한다.

---

### Task 1: CLI parser에 explicit `iouring` mode 추가

**Files:**
- Modify: `tests/Hps.Sample.BrokerServer.Tests/SampleBrokerServerCommandParserTests.cs`
- Modify: `samples/Hps.Sample.BrokerServer/SampleTransportMode.cs`
- Modify: `samples/Hps.Sample.BrokerServer/SampleBrokerServerCommandParser.cs`

**Interfaces:**
- Consumes: 기존 `SampleBrokerServerCommandParser.TryParse(string[], out SampleBrokerServerCommandLine?, out string?)`.
- Produces: `SampleTransportMode.IoUring`과 대소문자를 구분하지 않는 `iouring` parser token.

- [ ] **Step 1: compile-safe failing parser test를 작성한다**

`SampleTransportMode.IoUring`을 아직 직접 참조하지 않고 문자열로 결과를 비교해 enum member 부재가 compile failure가 되지 않게 한다.

```csharp
// explicit io_uring 선택은 parser에서 보존되어야 하며 실제 OS capability 판단은 selector가 맡는다.
// mixed-case 입력도 기존 transport token과 동일하게 대소문자를 구분하지 않아야 한다.
[Fact]
public void TryParse_WhenTransportIoUringIsProvided_ReturnsIoUringMode()
{
    BrokerSample.SampleBrokerServerCommandLine? commandLine;
    string? errorMessage;

    bool parsed = BrokerSample.SampleBrokerServerCommandParser.TryParse(
        new[] { "loopback", "5000", "65536", "--transport", "IoUrInG" },
        out commandLine,
        out errorMessage);

    Assert.True(parsed);
    Assert.Null(errorMessage);
    Assert.NotNull(commandLine);
    Assert.Equal("IoUring", commandLine!.TransportMode.ToString());
}
```

- [ ] **Step 2: Red가 assertion failure인지 확인한다**

Run:

```powershell
dotnet test tests\Hps.Sample.BrokerServer.Tests\Hps.Sample.BrokerServer.Tests.csproj --filter "FullyQualifiedName~TryParse_WhenTransportIoUringIsProvided_ReturnsIoUringMode" -v minimal
```

Expected:

```text
Failed: 1
Assert.True() Failure
```

- [ ] **Step 3: enum과 parser를 최소 구현한다**

`SampleTransportMode.cs`의 기존 순서를 보존하고 마지막에 추가한다.

```csharp
public enum SampleTransportMode
{
    Saea,
    Rio,
    Auto,
    IoUring
}
```

`SampleBrokerServerCommandParser.cs`의 메시지와 token 분기를 다음과 같이 갱신한다.

```csharp
public const string MessageTransportValueRequired = "--transport 옵션에는 saea, rio, iouring 또는 auto 값이 필요합니다.";
public const string MessageTransportValueInvalid = "--transport 옵션은 saea, rio, iouring 또는 auto 값만 사용할 수 있습니다.";
```

```csharp
if (string.Equals(value, "iouring", StringComparison.OrdinalIgnoreCase))
{
    mode = SampleTransportMode.IoUring;
    return true;
}
```

`TryParse_WhenTransportValueIsMissing_ReturnsError`와 `TryParse_WhenTransportValueIsUnknown_ReturnsError`의 expected message도 같은 문자열로 갱신한다.

- [ ] **Step 4: parser Green과 기존 mode 회귀를 확인한다**

Run:

```powershell
dotnet test tests\Hps.Sample.BrokerServer.Tests\Hps.Sample.BrokerServer.Tests.csproj --filter "FullyQualifiedName~SampleBrokerServerCommandParserTests" -v minimal
```

Expected:

```text
Failed: 0, Passed: 8
```

- [ ] **Step 5: Task 1을 커밋한다**

```powershell
git add -- samples\Hps.Sample.BrokerServer\SampleTransportMode.cs samples\Hps.Sample.BrokerServer\SampleBrokerServerCommandParser.cs tests\Hps.Sample.BrokerServer.Tests\SampleBrokerServerCommandParserTests.cs
git diff --cached --check
git commit -m "feat(sample): parse explicit io_uring transport"
```

---

### Task 2: selector explicit io_uring fail-closed 정책 추가

**Files:**
- Modify: `tests/Hps.Sample.BrokerServer.Tests/Hps.Sample.BrokerServer.Tests.csproj`
- Create: `tests/Hps.Sample.BrokerServer.Tests/SampleBrokerServerProjectContractTests.cs`
- Modify: `tests/Hps.Sample.BrokerServer.Tests/SampleTransportSelectorTests.cs`
- Modify: `samples/Hps.Sample.BrokerServer/Hps.Sample.BrokerServer.csproj`
- Modify: `samples/Hps.Sample.BrokerServer/SampleTransportSelector.cs`

**Interfaces:**
- Consumes: Task 1의 `SampleTransportMode.IoUring`, `IoUringCapabilityStatus`, 기존 `SampleTransportSelection`.
- Produces: 아래 full overload. 기존 4/5-argument overload는 source-compatible하게 유지한다.

```csharp
public static SampleTransportSelection Select(
    SampleTransportMode mode,
    AddressFamily listenAddressFamily,
    Func<RioCapabilityStatus> getRioStatus,
    Func<IoUringCapabilityStatus> getIoUringStatus,
    Func<ITransport> createSaea,
    Func<ITransport> createRio,
    Func<ITransport> createIoUring)
```

- [ ] **Step 1: test project에 io_uring reference를 추가한다**

selector behavior test가 `IoUringCapabilityStatus`를 직접 사용하므로 test project에만 먼저 reference를 추가한다.

```xml
<ProjectReference Include="..\..\src\Hps.Transport.IoUring\Hps.Transport.IoUring.csproj" />
```

- [ ] **Step 2: sample project reference contract Red를 작성한다**

새 파일 `SampleBrokerServerProjectContractTests.cs`를 다음 내용으로 만든다.

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Hps.Sample.BrokerServer.Tests
{
    public sealed class SampleBrokerServerProjectContractTests
    {
        // selector가 io_uring public capability와 transport를 직접 사용하므로 sample project가 backend assembly를 명시 참조해야 한다.
        // 이 reference가 빠지면 Windows에서는 우연히 다른 경로가 통과해도 Linux sample composition build가 깨진다.
        [Fact]
        public void BrokerSampleProject_WhenInspected_ReferencesIoUringBackend()
        {
            string projectPath = Path.Combine(
                FindRepositoryRoot(),
                "samples",
                "Hps.Sample.BrokerServer",
                "Hps.Sample.BrokerServer.csproj");
            XDocument document = XDocument.Load(projectPath);
            IEnumerable<string> references = document
                .Descendants("ProjectReference")
                .Select(element => ((string?)element.Attribute("Include") ?? string.Empty).Replace('\\', '/'));

            Assert.Contains("../../src/Hps.Transport.IoUring/Hps.Transport.IoUring.csproj", references);
        }

        private static string FindRepositoryRoot()
        {
            string? current = AppContext.BaseDirectory;
            while (current != null)
            {
                if (File.Exists(Path.Combine(current, "HighPerformanceSocket.slnx")))
                    return current;

                current = Directory.GetParent(current)?.FullName;
            }

            throw new InvalidOperationException("HighPerformanceSocket.slnx 파일을 찾을 수 없습니다.");
        }
    }
}
```

- [ ] **Step 3: selector behavior Red를 reflection으로 작성한다**

`SampleTransportSelectorTests.cs` 상단에 `System.Linq`, `System.Reflection`을 추가한다. 새 overload가 없는 Red 상태에서도 컴파일되도록 7-argument overload는 reflection으로 찾는다.

기존 SAEA test body를 full-overload call isolation 검증으로 교체한다.

```csharp
// saea mode는 RIO와 io_uring capability probe를 모두 건너뛰고 SAEA factory만 호출해야 한다.
[Fact]
public void Select_WhenModeIsSaea_ReturnsSaeaTransport()
{
    SelectorCallCounts calls = new SelectorCallCounts();
    BrokerSample.SampleTransportSelection selection = SelectFull(
        BrokerSample.SampleTransportMode.Saea,
        RioCapabilityStatus.Available,
        IoUringCapabilityStatus.Available,
        AddressFamily.InterNetwork,
        calls);

    Assert.True(selection.Succeeded);
    Assert.Equal("SaeaTransport", selection.SelectedBackendName);
    Assert.Equal(0, calls.RioProbeCount);
    Assert.Equal(0, calls.IoUringProbeCount);
    Assert.Equal(1, calls.SaeaFactoryCount);
    Assert.Equal(0, calls.RioFactoryCount);
    Assert.Equal(0, calls.IoUringFactoryCount);
}
```

다음 다섯 test를 추가한다.

```csharp
// explicit io_uring은 capability가 available일 때만 io_uring factory를 호출해야 한다.
// 선택하지 않은 RIO/SAEA 경로가 평가되면 platform probe와 backend identity가 오염된다.
[Fact]
public void Select_WhenModeIsIoUringAndAvailable_ReturnsIoUringTransport()
{
    SelectorCallCounts calls = new SelectorCallCounts();
    BrokerSample.SampleTransportSelection selection = SelectFull(
        BrokerSample.SampleTransportMode.IoUring,
        RioCapabilityStatus.Available,
        IoUringCapabilityStatus.Available,
        AddressFamily.InterNetwork,
        calls);

    Assert.True(selection.Succeeded);
    Assert.Equal("IoUringTransport", selection.SelectedBackendName);
    Assert.Equal(0, calls.RioProbeCount);
    Assert.Equal(1, calls.IoUringProbeCount);
    Assert.Equal(0, calls.SaeaFactoryCount);
    Assert.Equal(0, calls.RioFactoryCount);
    Assert.Equal(1, calls.IoUringFactoryCount);
}

// non-Linux에서 explicit io_uring을 요청하면 SAEA fallback 없이 Linux 전용 오류와 exit code 1을 반환해야 한다.
[Fact]
public void Select_WhenModeIsIoUringAndOperatingSystemIsUnsupported_ReturnsFailure()
{
    SelectorCallCounts calls = new SelectorCallCounts();
    BrokerSample.SampleTransportSelection selection = SelectFull(
        BrokerSample.SampleTransportMode.IoUring,
        RioCapabilityStatus.Available,
        IoUringCapabilityStatus.UnsupportedOperatingSystem,
        AddressFamily.InterNetwork,
        calls);

    Assert.False(selection.Succeeded);
    Assert.Equal(1, selection.ExitCode);
    Assert.Contains("Linux", selection.ErrorMessage!);
    Assert.Equal(0, calls.SaeaFactoryCount);
    Assert.Equal(0, calls.IoUringFactoryCount);
}

// Linux이지만 kernel capability가 unavailable인 explicit 요청도 backend identity를 숨기지 않고 실패해야 한다.
[Fact]
public void Select_WhenModeIsIoUringAndCapabilityIsUnavailable_ReturnsFailure()
{
    SelectorCallCounts calls = new SelectorCallCounts();
    BrokerSample.SampleTransportSelection selection = SelectFull(
        BrokerSample.SampleTransportMode.IoUring,
        RioCapabilityStatus.Available,
        IoUringCapabilityStatus.Unavailable,
        AddressFamily.InterNetwork,
        calls);

    Assert.False(selection.Succeeded);
    Assert.Equal(1, selection.ExitCode);
    Assert.Contains("status=Unavailable", selection.ErrorMessage!);
    Assert.Equal(0, calls.SaeaFactoryCount);
    Assert.Equal(0, calls.IoUringFactoryCount);
}

// io_uring TCP는 IPEndPoint의 IPv6 family를 사용할 수 있으므로 RIO의 IPv4-only guard를 재사용하면 안 된다.
[Fact]
public void Select_WhenModeIsIoUringAndListenAddressIsIpv6_ReturnsIoUringTransport()
{
    SelectorCallCounts calls = new SelectorCallCounts();
    BrokerSample.SampleTransportSelection selection = SelectFull(
        BrokerSample.SampleTransportMode.IoUring,
        RioCapabilityStatus.Available,
        IoUringCapabilityStatus.Available,
        AddressFamily.InterNetworkV6,
        calls);

    Assert.True(selection.Succeeded);
    Assert.Equal("IoUringTransport", selection.SelectedBackendName);
    Assert.Equal(1, calls.IoUringFactoryCount);
}

// 기존 overload는 source compatibility를 유지하되 새 mode를 받으면 준비되지 않은 factory를 호출하지 않고 명시 실패해야 한다.
[Fact]
public void Select_WhenLegacyOverloadReceivesIoUring_ReturnsFailure()
{
    BrokerSample.SampleTransportSelection selection = BrokerSample.SampleTransportSelector.Select(
        BrokerSample.SampleTransportMode.IoUring,
        delegate { return RioCapabilityStatus.Available; },
        delegate { return new FakeTransport("SaeaTransport"); },
        delegate { return new FakeTransport("RioTransport"); });

    Assert.False(selection.Succeeded);
    Assert.Equal(1, selection.ExitCode);
    Assert.Contains("Linux", selection.ErrorMessage!);
}
```

같은 test class에 reflection helper와 call counter를 추가한다.

```csharp
private static BrokerSample.SampleTransportSelection SelectFull(
    BrokerSample.SampleTransportMode mode,
    RioCapabilityStatus rioStatus,
    IoUringCapabilityStatus ioUringStatus,
    AddressFamily listenAddressFamily,
    SelectorCallCounts calls)
{
    MethodInfo? selectMethod = typeof(BrokerSample.SampleTransportSelector)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .SingleOrDefault(method => method.Name == "Select" && method.GetParameters().Length == 7);
    Assert.NotNull(selectMethod);

    Func<RioCapabilityStatus> getRioStatus = delegate
    {
        calls.RioProbeCount++;
        return rioStatus;
    };
    Func<IoUringCapabilityStatus> getIoUringStatus = delegate
    {
        calls.IoUringProbeCount++;
        return ioUringStatus;
    };
    Func<ITransport> createSaea = delegate
    {
        calls.SaeaFactoryCount++;
        return new FakeTransport("SaeaTransport");
    };
    Func<ITransport> createRio = delegate
    {
        calls.RioFactoryCount++;
        return new FakeTransport("RioTransport");
    };
    Func<ITransport> createIoUring = delegate
    {
        calls.IoUringFactoryCount++;
        return new FakeTransport("IoUringTransport");
    };

    return (BrokerSample.SampleTransportSelection)selectMethod!.Invoke(
        null,
        new object[]
        {
            mode,
            listenAddressFamily,
            getRioStatus,
            getIoUringStatus,
            createSaea,
            createRio,
            createIoUring
        })!;
}

private sealed class SelectorCallCounts
{
    public int RioProbeCount;
    public int IoUringProbeCount;
    public int SaeaFactoryCount;
    public int RioFactoryCount;
    public int IoUringFactoryCount;
}
```

- [ ] **Step 4: project/selector Red를 확인한다**

Run:

```powershell
dotnet test tests\Hps.Sample.BrokerServer.Tests\Hps.Sample.BrokerServer.Tests.csproj --filter "FullyQualifiedName~SampleBrokerServerProjectContractTests|FullyQualifiedName~SampleTransportSelectorTests" -v minimal
```

Expected:

```text
Failed tests include BrokerSampleProject_WhenInspected_ReferencesIoUringBackend
Failed tests include Select_WhenModeIsSaea_ReturnsSaeaTransport with Assert.NotNull() Failure
```

- [ ] **Step 5: sample project reference와 full selector를 구현한다**

sample project에 다음 reference를 추가한다.

```xml
<ProjectReference Include="..\..\src\Hps.Transport.IoUring\Hps.Transport.IoUring.csproj" />
```

기존 4/5-argument overload를 다음 delegation으로 바꾸고 7-argument overload를 추가한다.

```csharp
public static SampleTransportSelection Select(
    SampleTransportMode mode,
    Func<RioCapabilityStatus> getRioStatus,
    Func<ITransport> createSaea,
    Func<ITransport> createRio)
{
    return Select(
        mode,
        AddressFamily.InterNetwork,
        getRioStatus,
        GetUnsupportedIoUringStatus,
        createSaea,
        createRio,
        ThrowIoUringFactoryNotConfigured);
}

public static SampleTransportSelection Select(
    SampleTransportMode mode,
    AddressFamily listenAddressFamily,
    Func<RioCapabilityStatus> getRioStatus,
    Func<ITransport> createSaea,
    Func<ITransport> createRio)
{
    return Select(
        mode,
        listenAddressFamily,
        getRioStatus,
        GetUnsupportedIoUringStatus,
        createSaea,
        createRio,
        ThrowIoUringFactoryNotConfigured);
}

public static SampleTransportSelection Select(
    SampleTransportMode mode,
    AddressFamily listenAddressFamily,
    Func<RioCapabilityStatus> getRioStatus,
    Func<IoUringCapabilityStatus> getIoUringStatus,
    Func<ITransport> createSaea,
    Func<ITransport> createRio,
    Func<ITransport> createIoUring)
{
    if (getRioStatus == null)
        throw new ArgumentNullException(nameof(getRioStatus));
    if (getIoUringStatus == null)
        throw new ArgumentNullException(nameof(getIoUringStatus));
    if (createSaea == null)
        throw new ArgumentNullException(nameof(createSaea));
    if (createRio == null)
        throw new ArgumentNullException(nameof(createRio));
    if (createIoUring == null)
        throw new ArgumentNullException(nameof(createIoUring));

    if (mode != SampleTransportMode.Saea &&
        mode != SampleTransportMode.Rio &&
        mode != SampleTransportMode.Auto &&
        mode != SampleTransportMode.IoUring)
    {
        throw new ArgumentOutOfRangeException(nameof(mode));
    }

    if (mode == SampleTransportMode.Saea)
        return SampleTransportSelection.Success(createSaea(), "SaeaTransport", null);

    if (mode == SampleTransportMode.IoUring)
    {
        IoUringCapabilityStatus ioUringStatus = getIoUringStatus();
        if (ioUringStatus == IoUringCapabilityStatus.Available)
            return SampleTransportSelection.Success(createIoUring(), "IoUringTransport", null);

        if (ioUringStatus == IoUringCapabilityStatus.UnsupportedOperatingSystem)
        {
            return SampleTransportSelection.Failure(
                "io_uring transport는 Linux에서만 사용할 수 있습니다. status=" + ioUringStatus,
                RuntimeFailureExitCode);
        }

        return SampleTransportSelection.Failure(
            "io_uring transport를 사용할 수 없습니다. status=" + ioUringStatus,
            RuntimeFailureExitCode);
    }

    bool rioCanListenOnAddressFamily = listenAddressFamily == AddressFamily.InterNetwork;
    if (!rioCanListenOnAddressFamily)
    {
        if (mode == SampleTransportMode.Rio)
        {
            return SampleTransportSelection.Failure(
                "RIO transport는 현재 IPv4 listen endpoint 만 지원합니다. address-family=" + listenAddressFamily,
                RuntimeFailureExitCode);
        }

        return SampleTransportSelection.Success(
            createSaea(),
            "SaeaTransport",
            "RIO IPv4-only backend 는 IPv6/non-IPv4 listen endpoint 를 사용할 수 없어 SaeaTransport 로 fallback 합니다. address-family=" +
            listenAddressFamily);
    }

    RioCapabilityStatus rioStatus = getRioStatus();
    if (mode == SampleTransportMode.Rio)
    {
        if (rioStatus == RioCapabilityStatus.Available)
            return SampleTransportSelection.Success(createRio(), "RioTransport", null);

        return SampleTransportSelection.Failure(
            "RIO transport를 사용할 수 없습니다. status=" + rioStatus,
            RuntimeFailureExitCode);
    }

    if (rioStatus == RioCapabilityStatus.Available)
        return SampleTransportSelection.Success(createRio(), "RioTransport", null);

    return SampleTransportSelection.Success(
        createSaea(),
        "SaeaTransport",
        "RIO unavailable; falling back to SaeaTransport. status=" + rioStatus);
}

private static IoUringCapabilityStatus GetUnsupportedIoUringStatus()
{
    return IoUringCapabilityStatus.UnsupportedOperatingSystem;
}

private static ITransport ThrowIoUringFactoryNotConfigured()
{
    throw new InvalidOperationException("이 selector overload에는 io_uring factory가 구성되지 않았습니다.");
}
```

public XML comments에는 full overload의 explicit-only 정책, old overload의 source compatibility, probe/factory 비호출 계약을 한국어로 명시한다.

- [ ] **Step 6: selector Green과 기존 RIO/Auto 회귀를 확인한다**

Run:

```powershell
dotnet test tests\Hps.Sample.BrokerServer.Tests\Hps.Sample.BrokerServer.Tests.csproj --filter "FullyQualifiedName~SampleBrokerServerProjectContractTests|FullyQualifiedName~SampleTransportSelectorTests" -v minimal
```

Expected:

```text
Failed: 0, Passed: 14
```

- [ ] **Step 7: Task 2를 커밋한다**

```powershell
git add -- samples\Hps.Sample.BrokerServer\Hps.Sample.BrokerServer.csproj samples\Hps.Sample.BrokerServer\SampleTransportSelector.cs tests\Hps.Sample.BrokerServer.Tests\Hps.Sample.BrokerServer.Tests.csproj tests\Hps.Sample.BrokerServer.Tests\SampleBrokerServerProjectContractTests.cs tests\Hps.Sample.BrokerServer.Tests\SampleTransportSelectorTests.cs
git diff --cached --check
git commit -m "feat(sample): select explicit io_uring transport"
```

---

### Task 3: Program에 io_uring probe/factory wiring 추가

**Files:**
- Modify: `tests/Hps.Sample.BrokerServer.Tests/SampleBrokerServerProgramTests.cs`
- Modify: `tests/Hps.Sample.BrokerServer.Tests/SampleBrokerServerProjectContractTests.cs`
- Modify: `samples/Hps.Sample.BrokerServer/Program.cs`

**Interfaces:**
- Consumes: Task 2의 7-argument `SampleTransportSelector.Select`.
- Produces: CLI `--transport iouring`에서 실제 `IoUringCapabilityProbe.GetStatus`와 `IoUringTransport`를 사용하는 Program composition.

- [ ] **Step 1: usage와 Program composition Red를 작성한다**

`SampleBrokerServerProgramTests.cs`의 두 usage assertion을 다음 문자열로 바꾼다.

```csharp
Assert.Contains("--transport <saea|rio|iouring|auto>", result.Item2);
```

`SampleBrokerServerProjectContractTests.cs`에 다음 test를 추가한다.

```csharp
// Program은 parser가 보존한 io_uring mode를 old compatibility overload로 보내지 않고 실제 probe와 factory에 연결해야 한다.
// source composition 검증은 Linux에서 장기 실행 broker process를 띄우지 않고도 이 wiring 누락을 잡는다.
[Fact]
public void BrokerSampleProgram_WhenInspected_InjectsIoUringProbeAndFactory()
{
    string programPath = Path.Combine(
        FindRepositoryRoot(),
        "samples",
        "Hps.Sample.BrokerServer",
        "Program.cs");
    string source = File.ReadAllText(programPath);

    Assert.Contains("IoUringCapabilityProbe.GetStatus", source);
    Assert.Contains("delegate { return new IoUringTransport(); }", source);
}
```

- [ ] **Step 2: Program Red가 assertion failure인지 확인한다**

Run:

```powershell
dotnet test tests\Hps.Sample.BrokerServer.Tests\Hps.Sample.BrokerServer.Tests.csproj --filter "FullyQualifiedName~SampleBrokerServerProgramTests|FullyQualifiedName~BrokerSampleProgram_WhenInspected_InjectsIoUringProbeAndFactory" -v minimal
```

Expected:

```text
Failed: 3
Assert.Contains() Failure
```

- [ ] **Step 3: Program을 full selector overload에 연결한다**

transport selection call을 다음 코드로 교체한다.

```csharp
SampleTransportSelection selection = SampleTransportSelector.Select(
    parsedCommandLine.TransportMode,
    address.AddressFamily,
    RioCapabilityProbe.GetStatus,
    IoUringCapabilityProbe.GetStatus,
    delegate { return new SaeaTransport(); },
    delegate { return new RioTransport(); },
    delegate { return new IoUringTransport(); });
```

usage와 examples를 다음과 같이 갱신한다.

```csharp
Console.Error.WriteLine("사용법: Hps.Sample.BrokerServer <host> <port> <max-frame-bytes> [--transport <saea|rio|iouring|auto>]");
Console.Error.WriteLine("예시: Hps.Sample.BrokerServer 127.0.0.1 5000 65536");
Console.Error.WriteLine("예시: Hps.Sample.BrokerServer 127.0.0.1 5000 65536 --transport auto");
Console.Error.WriteLine("예시: Hps.Sample.BrokerServer 127.0.0.1 5000 65536 --transport iouring");
```

- [ ] **Step 4: Program Green과 sample project build를 확인한다**

Run:

```powershell
dotnet test tests\Hps.Sample.BrokerServer.Tests\Hps.Sample.BrokerServer.Tests.csproj -v minimal
dotnet build samples\Hps.Sample.BrokerServer\Hps.Sample.BrokerServer.csproj --no-restore -v minimal
```

Expected:

```text
Sample broker tests: Failed: 0, Passed: 25
Sample broker build: 0 Warning(s), 0 Error(s)
```

- [ ] **Step 5: Windows fail-closed smoke를 확인한다**

```powershell
$output = & dotnet run --project samples\Hps.Sample.BrokerServer\Hps.Sample.BrokerServer.csproj --no-build -- 127.0.0.1 5000 65536 --transport iouring 2>&1
$exitCode = $LASTEXITCODE
if ($exitCode -ne 1) { throw "expected exit code 1, actual=$exitCode" }
if (($output -join "`n") -notmatch "Linux") { throw "Linux 전용 오류 메시지가 없습니다." }
```

Expected:

```text
process exit code: 1
output contains: io_uring transport는 Linux에서만 사용할 수 있습니다.
```

- [ ] **Step 6: Task 3을 커밋한다**

```powershell
git add -- samples\Hps.Sample.BrokerServer\Program.cs tests\Hps.Sample.BrokerServer.Tests\SampleBrokerServerProgramTests.cs tests\Hps.Sample.BrokerServer.Tests\SampleBrokerServerProjectContractTests.cs
git diff --cached --check
git commit -m "feat(sample): wire io_uring broker transport"
```

---

### Task 4: Linux contract workflow에 sample broker build 추가

**Files:**
- Modify: `tests/Hps.Benchmarks.Tests/BenchmarkArtifactWorkflowTests.cs`
- Modify: `.github/workflows/iouring-linux-contract.yml`

**Interfaces:**
- Consumes: Task 3의 Linux-buildable sample project composition.
- Produces: io_uring tests와 sample broker 두 project의 explicit Linux restore/build gate. Native runtime test와 TRX 계약은 기존 io_uring test project에 한정한다.

- [ ] **Step 1: workflow static contract Red를 작성한다**

기존 test 이름을 `IoUringLinuxContractWorkflow_WhenRunOnLinux_RestoresAndBuildsOnlyExplicitLinuxSafeProjects`로 바꾸고 body를 다음과 같이 갱신한다.

```csharp
// Linux contract workflow는 native tests와 실제 sample composition을 함께 빌드하되 solution/WPF로 범위를 넓히면 안 된다.
// runtime test는 기존 io_uring test project에만 남겨 장기 실행 broker process 없이 backend 계약을 검증한다.
[Fact]
public void IoUringLinuxContractWorkflow_WhenRunOnLinux_RestoresAndBuildsOnlyExplicitLinuxSafeProjects()
{
    string workflow = ReadIoUringLinuxContractWorkflow();

    Assert.Contains("dotnet restore tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj", workflow);
    Assert.Contains("dotnet restore samples/Hps.Sample.BrokerServer/Hps.Sample.BrokerServer.csproj", workflow);
    Assert.Contains("dotnet build tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj --no-restore", workflow);
    Assert.Contains("dotnet build samples/Hps.Sample.BrokerServer/Hps.Sample.BrokerServer.csproj --no-restore", workflow);
    Assert.Contains("dotnet test tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj", workflow);
    Assert.DoesNotContain("dotnet test samples/Hps.Sample.BrokerServer/Hps.Sample.BrokerServer.csproj", workflow);
    Assert.DoesNotContain("dotnet restore HighPerformanceSocket.slnx", workflow);
    Assert.DoesNotContain("dotnet build HighPerformanceSocket.slnx", workflow);
    Assert.DoesNotContain("EnableWindowsTargeting", workflow);
}
```

- [ ] **Step 2: workflow Red를 확인한다**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter "FullyQualifiedName~IoUringLinuxContractWorkflow_WhenRunOnLinux_RestoresAndBuildsOnlyExplicitLinuxSafeProjects" -v minimal
```

Expected:

```text
Failed: 1
Assert.Contains() Failure for samples/Hps.Sample.BrokerServer/Hps.Sample.BrokerServer.csproj
```

- [ ] **Step 3: workflow에 explicit sample restore/build step을 추가한다**

기존 Restore/Build step을 다음 네 step으로 교체한다.

```yaml
      - name: Restore io_uring tests
        run: dotnet restore tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj

      - name: Restore sample broker
        run: dotnet restore samples/Hps.Sample.BrokerServer/Hps.Sample.BrokerServer.csproj

      - name: Build io_uring tests
        run: dotnet build tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj --no-restore

      - name: Build sample broker
        run: dotnet build samples/Hps.Sample.BrokerServer/Hps.Sample.BrokerServer.csproj --no-restore
```

`Run io_uring tests`, TRX, hang diagnostics, artifact upload, failure propagation step은 변경하지 않는다.

- [ ] **Step 4: workflow Green과 YAML scope를 확인한다**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter "FullyQualifiedName~BenchmarkArtifactWorkflowTests" -v minimal
git diff --check
```

Expected:

```text
Failed: 0
git diff --check exits 0
```

- [ ] **Step 5: Task 4를 커밋한다**

```powershell
git add -- .github\workflows\iouring-linux-contract.yml tests\Hps.Benchmarks.Tests\BenchmarkArtifactWorkflowTests.cs
git diff --cached --check
git commit -m "ci(iouring): build sample broker on Linux"
```

---

### Task 5: D235 local implementation gate와 상태 문서 정리

**Files:**
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/decisions/2026-07.md`

**Interfaces:**
- Consumes: Task 1~4의 네 구현 커밋.
- Produces: local gate evidence와 push 이후 D236 remote gate 진입점.

- [ ] **Step 1: focused contracts를 다시 실행한다**

```powershell
dotnet test tests\Hps.Sample.BrokerServer.Tests\Hps.Sample.BrokerServer.Tests.csproj -v minimal
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter "FullyQualifiedName~BenchmarkArtifactWorkflowTests" -v minimal
dotnet test tests\Hps.Server.Tests\Hps.Server.Tests.csproj --filter "FullyQualifiedName~TcpCommandLoopback_WhenSubscriberAndPublisherUseLengthPrefixedCommands_FansOutPayload" -v minimal
```

Expected:

```text
Sample broker tests: Failed: 0, Passed: 25
Workflow contract tests: Failed: 0
TCP broker loopback: Failed: 0, Passed: 1
```

- [ ] **Step 2: full solution gate를 실행한다**

```powershell
dotnet build HighPerformanceSocket.slnx -v minimal
dotnet test HighPerformanceSocket.slnx --no-build -v minimal
git diff --check
```

Expected:

```text
Build: 0 Warning(s), 0 Error(s)
Tests: Failed: 0, Passed: 510
git diff --check exits 0
```

- [ ] **Step 3: 상태 문서를 D235 local gate로 갱신한다**

- `CURRENT_PLAN.md`: D233 설계와 D234 계획 이후 Task 1~4 구현, focused/full 검증 결과, 다음 실행 지점 D236 remote gate를 기록한다.
- `TODOS.md`: D235를 Completed로 이동하고 Current TODO에 사용자 push 이후 `iouring-linux-contract.yml` artifact 검토를 둔다.
- `CHANGELOG_AGENT.md`: 네 커밋의 Red/Green evidence, Windows fail-closed smoke, build/test/diff-check 결과를 기록한다.
- `DECISIONS.md`: `D235 — sample broker explicit io_uring mode는 local gate를 통과했으며 default/auto 의미는 유지한다.`를 active index에 추가한다.
- `docs/agent-state/decisions/2026-07.md`: D235의 explicit fail-closed, old overload compatibility, Linux-safe workflow scope와 remote pending 경계를 기록한다.

- [ ] **Step 4: 상태 문서만 커밋한다**

```powershell
git add -- CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs\agent-state\decisions\2026-07.md
git diff --cached --check
git commit -m "docs: record explicit io_uring sample local gate"
```

- [ ] **Step 5: review checkpoint 상태를 확인한다**

```powershell
git status --short --branch
git log -5 --oneline
```

Expected:

```text
tracked working tree clean
.claude/review의 기존 untracked files만 남음
Task 1~5 commits가 분리되어 표시됨
```

---

### Task 6: D236 remote Linux artifact gate

**Files:**
- Modify after evidence: `CURRENT_PLAN.md`
- Modify after evidence: `TODOS.md`
- Modify after evidence: `CHANGELOG_AGENT.md`
- Modify after evidence: `DECISIONS.md`
- Modify after evidence: `docs/agent-state/decisions/2026-07.md`

**Interfaces:**
- Consumes: 사용자가 원격 `master`에 push한 D235 HEAD.
- Produces: sample broker Linux build evidence와 기존 native io_uring TRX evidence를 같은 run에서 확인한 D236 gate.

- [ ] **Step 1: 원격 HEAD와 local HEAD 일치를 확인한다**

```powershell
git fetch origin master
git rev-parse HEAD
git rev-parse origin/master
```

Expected: 두 SHA가 같다. 다르면 workflow를 실행하지 않고 사용자 push를 기다린다.

- [ ] **Step 2: Linux contract workflow를 실행하고 완료를 기다린다**

```powershell
$headSha = (git rev-parse HEAD).Trim()
gh workflow run iouring-linux-contract.yml --ref master
Start-Sleep -Seconds 5
$runId = gh run list --workflow iouring-linux-contract.yml --branch master --event workflow_dispatch --commit $headSha --limit 1 --json databaseId --jq '.[0].databaseId'
if ([string]::IsNullOrWhiteSpace($runId)) { throw "대상 HEAD의 workflow run을 찾을 수 없습니다." }
gh run watch $runId --exit-status
```

Expected: workflow/job conclusion `success`, `Build sample broker` step success.

- [ ] **Step 3: artifact와 TRX를 직접 검토한다**

```powershell
$headSha = (git rev-parse HEAD).Trim()
$runId = gh run list --workflow iouring-linux-contract.yml --branch master --event workflow_dispatch --commit $headSha --limit 1 --json databaseId --jq '.[0].databaseId'
if ([string]::IsNullOrWhiteSpace($runId)) { throw "대상 HEAD의 workflow run을 찾을 수 없습니다." }
$artifactRoot = Join-Path $env:TEMP ("hps-iouring-contract-" + $runId)
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
gh run download $runId --dir $artifactRoot
Get-ChildItem $artifactRoot -Recurse -File | Select-Object FullName, Length
Select-String -Path (Get-ChildItem $artifactRoot -Recurse -Filter summary.md).FullName -Pattern "Test exit code: 0"
Select-String -Path (Get-ChildItem $artifactRoot -Recurse -Filter iouring-tests.trx).FullName -Pattern "failed=\"0\"|error=\"0\"|timeout=\"0\"|aborted=\"0\"|registered payload fixed send path: hit|io_uring capability status: Available"
```

Expected:

```text
summary test exit code 0
TRX failed/error/timeout/aborted 0
capability Available
registered payload fixed send path: hit
```

- [ ] **Step 4: D236 evidence를 상태 문서에 기록하고 커밋한다**

- workflow run id, head SHA, artifact name, sample build step success를 기록한다.
- TRX total/executed/passed와 failed/error/timeout/aborted/notExecuted 값을 실제 artifact에서 읽어 기록한다.
- native TCP loopback과 registered payload fixed-send hit evidence가 유지됐는지 기록한다.
- 이 gate가 sample/default promotion, `auto` 변경, zero-copy 또는 성능 우위를 증명하지 않는다는 경계를 유지한다.

```powershell
git add -- CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs\agent-state\decisions\2026-07.md
git diff --cached --check
git commit -m "docs(iouring): record explicit sample remote gate"
```

---

## Self-Review Checklist

- Spec coverage:
  - explicit `iouring` parser token: Task 1.
  - unavailable fail-closed와 no fallback: Task 2.
  - 기존 `auto`, RIO IPv4 policy, old overload 호환성: Task 2 회귀 tests.
  - IPv6 io_uring composition: Task 2.
  - sample project reference와 Program probe/factory wiring: Task 2~3.
  - Linux-safe 두 project restore/build, io_uring tests only runtime gate: Task 4.
  - local build/test/smoke와 remote artifact evidence: Task 5~6.
- Excluded scope:
  - `TransportFactory.CreateDefault()`, OS-aware `auto`, WPF selector, UDP sample CLI, zero-copy/성능 주장은 어느 Task에서도 수정하지 않는다.
- TDD integrity:
  - Task 1은 enum symbol을 문자열로 비교해 assertion Red를 만든다.
  - Task 2는 새 overload를 reflection으로 찾아 assertion Red를 만든다.
  - Task 3은 runtime usage/source composition assertion Red다.
  - Task 4는 workflow path assertion Red다.
- Type consistency:
  - full selector overload의 parameter 순서는 mode, address family, RIO probe, io_uring probe, SAEA factory, RIO factory, io_uring factory로 전 Task에서 동일하다.
  - selected backend name은 `IoUringTransport`, CLI token은 `iouring`, enum member는 `IoUring`으로 구분한다.
- Placeholder scan:
  - 모든 edit step에 concrete file, code, command, expected result가 있고 미정 표식은 없다.
