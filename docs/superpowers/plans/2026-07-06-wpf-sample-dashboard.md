# WPF Sample Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 사용자가 로컬 Windows 환경에서 WPF 앱을 실행해 Interface Server 의 TCP/UDP publish-subscribe smoke 와 transport diagnostics 를 확인할 수 있게 한다.

**Architecture:** 새 `samples/Hps.Sample.Dashboard` WPF project 는 View/ViewModel/Service/Model/Command 로 분리한다. `DashboardBrokerService`가 `ITransport`, `PinnedBlockMemoryPool`, `BrokerServer` lifecycle 을 소유하고, smoke service 와 diagnostics service 는 public transport/server surface 만 사용한다.

**Tech Stack:** .NET 9, WPF `net9.0-windows`, C# 8.0, xUnit, existing `Hps.Server`, `Hps.Transport`, `Hps.Buffers`.

---

## 파일 구조

- Create: `samples/Hps.Sample.Dashboard/Hps.Sample.Dashboard.csproj`
  - WPF sample project. 루트 `Directory.Build.props`의 `net9.0` 기본값을 `net9.0-windows`로 override 한다.
- Create: `samples/Hps.Sample.Dashboard/App.xaml`
  - WPF application resource entry.
- Create: `samples/Hps.Sample.Dashboard/App.xaml.cs`
  - WPF application partial class.
- Create: `samples/Hps.Sample.Dashboard/MainWindow.xaml`
  - 첫 화면 dashboard layout.
- Create: `samples/Hps.Sample.Dashboard/MainWindow.xaml.cs`
  - `DashboardViewModel` 생성과 `DataContext` 연결만 담당한다.
- Create: `samples/Hps.Sample.Dashboard/Commands/RelayCommand.cs`
  - 동기 command state binding.
- Create: `samples/Hps.Sample.Dashboard/Commands/AsyncRelayCommand.cs`
  - 중복 실행 방지와 async command state binding.
- Create: `samples/Hps.Sample.Dashboard/Models/DashboardStatus.cs`
  - UI 상태 enum.
- Create: `samples/Hps.Sample.Dashboard/Models/SmokeRunResult.cs`
  - TCP/UDP smoke 결과 모델.
- Create: `samples/Hps.Sample.Dashboard/Models/TransportMetricRow.cs`
  - diagnostics grid row 모델.
- Create: `samples/Hps.Sample.Dashboard/Services/DashboardBrokerService.cs`
  - `BrokerServer` lifecycle 과 shared `ITransport` ownership.
- Create: `samples/Hps.Sample.Dashboard/Services/TcpSmokeTestService.cs`
  - TCP loopback smoke 실행.
- Create: `samples/Hps.Sample.Dashboard/Services/UdpSmokeTestService.cs`
  - UDP datagram command smoke 실행.
- Create: `samples/Hps.Sample.Dashboard/Services/DiagnosticsSnapshotService.cs`
  - `ITransportDiagnostics`/`ITransportEndpointDiagnostics` snapshot 변환.
- Create: `samples/Hps.Sample.Dashboard/Services/IoUringEvidenceStatusService.cs`
  - local UI 에 표시할 `io_uring` evidence status message 제공.
- Create: `samples/Hps.Sample.Dashboard/ViewModels/DashboardViewModel.cs`
  - command orchestration, 상태, 로그, metric rows.
- Create: `tests/Hps.Sample.Dashboard.Tests/Hps.Sample.Dashboard.Tests.csproj`
  - WPF sample project 를 참조하는 `net9.0-windows` test project.
- Create: `tests/Hps.Sample.Dashboard.Tests/DashboardProjectContractTests.cs`
  - project file, solution inclusion, WPF build contract 를 검증한다.
- Create: `tests/Hps.Sample.Dashboard.Tests/DashboardViewModelTests.cs`
  - command state, bounded log, result mapping 을 검증한다.
- Create: `tests/Hps.Sample.Dashboard.Tests/DiagnosticsSnapshotServiceTests.cs`
  - transport diagnostics row 변환을 검증한다.
- Create: `tests/Hps.Sample.Dashboard.Tests/TcpSmokeTestServiceTests.cs`
  - 실제 `SaeaTransport`/`BrokerServer` 기반 TCP smoke 를 검증한다.
- Create: `tests/Hps.Sample.Dashboard.Tests/UdpSmokeTestServiceTests.cs`
  - 실제 UDP command loopback smoke 를 검증한다.
- Modify: `HighPerformanceSocket.slnx`
  - sample/test project 를 solution 에 포함한다.
- Modify: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`
  - task 진행 상태와 검증 결과를 기록한다.

## Task 1: WPF project contract 와 solution inclusion

**Files:**
- Create: `tests/Hps.Sample.Dashboard.Tests/Hps.Sample.Dashboard.Tests.csproj`
- Create: `tests/Hps.Sample.Dashboard.Tests/DashboardProjectContractTests.cs`
- Create: `samples/Hps.Sample.Dashboard/Hps.Sample.Dashboard.csproj`
- Create: `samples/Hps.Sample.Dashboard/App.xaml`
- Create: `samples/Hps.Sample.Dashboard/App.xaml.cs`
- Create: `samples/Hps.Sample.Dashboard/MainWindow.xaml`
- Create: `samples/Hps.Sample.Dashboard/MainWindow.xaml.cs`
- Modify: `HighPerformanceSocket.slnx`

- [x] **Step 1: Write the failing project contract test**

Create `tests/Hps.Sample.Dashboard.Tests/Hps.Sample.Dashboard.Tests.csproj`.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- WPF sample project 를 직접 참조하므로 테스트 project 도 Windows TFM 을 사용한다. -->
  <PropertyGroup>
    <TargetFramework>net9.0-windows</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

</Project>
```

Create `tests/Hps.Sample.Dashboard.Tests/DashboardProjectContractTests.cs`.

```csharp
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Hps.Sample.Dashboard.Tests
{
    public sealed class DashboardProjectContractTests
    {
        [Fact]
        public void DashboardProject_WhenInspected_UsesWpfWindowsBuildContract()
        {
            // WPF project 는 루트 net9.0 기본값을 그대로 상속하면 빌드되지 않는다.
            // 이 테스트는 sample 구현 전에 Windows TFM, UseWPF, WinExe 계약을 먼저 고정한다.
            string projectPath = Path.GetFullPath(Path.Combine(
                "..",
                "..",
                "..",
                "..",
                "samples",
                "Hps.Sample.Dashboard",
                "Hps.Sample.Dashboard.csproj"));

            Assert.True(File.Exists(projectPath), "WPF sample project file 이 존재해야 한다.");

            XDocument document = XDocument.Load(projectPath);
            XElement project = document.Root!;

            Assert.Equal("Microsoft.NET.Sdk", (string?)project.Attribute("Sdk"));
            Assert.Equal("net9.0-windows", ReadProperty(project, "TargetFramework"));
            Assert.Equal("true", ReadProperty(project, "UseWPF"));
            Assert.Equal("WinExe", ReadProperty(project, "OutputType"));
            Assert.Equal("disable", ReadProperty(project, "ImplicitUsings"));
        }

        [Fact]
        public void Solution_WhenInspected_IncludesDashboardProjects()
        {
            // 새 sample 과 테스트 project 가 solution 에 빠지면 전체 build/test 검증에서 제외된다.
            string solutionPath = Path.GetFullPath(Path.Combine(
                "..",
                "..",
                "..",
                "..",
                "HighPerformanceSocket.slnx"));

            string solution = File.ReadAllText(solutionPath);

            Assert.Contains("samples/Hps.Sample.Dashboard/Hps.Sample.Dashboard.csproj", solution);
            Assert.Contains("tests/Hps.Sample.Dashboard.Tests/Hps.Sample.Dashboard.Tests.csproj", solution);
        }

        private static string? ReadProperty(XElement project, string name)
        {
            return project
                .Elements("PropertyGroup")
                .Elements(name)
                .Select(element => element.Value)
                .FirstOrDefault();
        }
    }
}
```

- [x] **Step 2: Run Red**

Run:

```powershell
dotnet test tests\Hps.Sample.Dashboard.Tests\Hps.Sample.Dashboard.Tests.csproj -v minimal
```

Expected: FAIL. `DashboardProject_WhenInspected_UsesWpfWindowsBuildContract` fails with `WPF sample project file 이 존재해야 한다.`

- [x] **Step 3: Add minimal WPF project shell**

Create `samples/Hps.Sample.Dashboard/Hps.Sample.Dashboard.csproj`.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- WPF 는 Windows TFM, UseWPF, WinExe 가 필요하므로 루트 net9.0 기본값을 명시적으로 override 한다. -->
  <PropertyGroup>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <OutputType>WinExe</OutputType>
    <ImplicitUsings>disable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Hps.Buffers\Hps.Buffers.csproj" />
    <ProjectReference Include="..\..\src\Hps.Server\Hps.Server.csproj" />
    <ProjectReference Include="..\..\src\Hps.Transport\Hps.Transport.csproj" />
  </ItemGroup>

</Project>
```

Create `samples/Hps.Sample.Dashboard/App.xaml`.

```xml
<Application x:Class="Hps.Sample.Dashboard.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
    </Application.Resources>
</Application>
```

Create `samples/Hps.Sample.Dashboard/App.xaml.cs`.

```csharp
using System.Windows;

namespace Hps.Sample.Dashboard
{
    public partial class App : Application
    {
    }
}
```

Create `samples/Hps.Sample.Dashboard/MainWindow.xaml`.

```xml
<Window x:Class="Hps.Sample.Dashboard.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="HPS Interface Server Dashboard"
        Width="1180"
        Height="760"
        MinWidth="960"
        MinHeight="620">
    <Grid Margin="16">
        <TextBlock Text="HPS Interface Server Dashboard"
                   FontSize="20"
                   FontWeight="SemiBold" />
    </Grid>
</Window>
```

Create `samples/Hps.Sample.Dashboard/MainWindow.xaml.cs`.

```csharp
using System.Windows;

namespace Hps.Sample.Dashboard
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
    }
}
```

Modify `HighPerformanceSocket.slnx`:

```xml
  <Folder Name="/samples/">
    <Project Path="samples/Hps.Sample.BrokerServer/Hps.Sample.BrokerServer.csproj" />
    <Project Path="samples/Hps.Sample.Dashboard/Hps.Sample.Dashboard.csproj" />
    <Project Path="samples/Hps.Sample.Publisher/Hps.Sample.Publisher.csproj" />
    <Project Path="samples/Hps.Sample.Subscriber/Hps.Sample.Subscriber.csproj" />
  </Folder>
```

```xml
  <Folder Name="/tests/">
    <Project Path="tests/Hps.Benchmarks/Hps.Benchmarks.csproj" />
    <Project Path="tests/Hps.Benchmarks.Tests/Hps.Benchmarks.Tests.csproj" />
    <Project Path="tests/Hps.Broker.Tests/Hps.Broker.Tests.csproj" />
    <Project Path="tests/Hps.Buffers.Tests/Hps.Buffers.Tests.csproj" />
    <Project Path="tests/Hps.Protocol.Tests/Hps.Protocol.Tests.csproj" />
    <Project Path="tests/Hps.Sample.BrokerServer.Tests/Hps.Sample.BrokerServer.Tests.csproj" />
    <Project Path="tests/Hps.Sample.Dashboard.Tests/Hps.Sample.Dashboard.Tests.csproj" />
    <Project Path="tests/Hps.Server.Tests/Hps.Server.Tests.csproj" />
    <Project Path="tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj" />
    <Project Path="tests/Hps.Transport.Rio.Tests/Hps.Transport.Rio.Tests.csproj" />
    <Project Path="tests/Hps.Transport.Tests/Hps.Transport.Tests.csproj" />
  </Folder>
```

- [x] **Step 4: Run Green**

Run:

```powershell
dotnet test tests\Hps.Sample.Dashboard.Tests\Hps.Sample.Dashboard.Tests.csproj -v minimal
dotnet build samples\Hps.Sample.Dashboard\Hps.Sample.Dashboard.csproj -v minimal
```

Expected: PASS. The sample project builds on Windows.

- [x] **Step 5: Commit**

```powershell
git add HighPerformanceSocket.slnx samples\Hps.Sample.Dashboard tests\Hps.Sample.Dashboard.Tests
git commit -m "test(sample): add dashboard project contract"
```

## Task 2: MVVM command/model/ViewModel core

**Files:**
- Create: `samples/Hps.Sample.Dashboard/Commands/RelayCommand.cs`
- Create: `samples/Hps.Sample.Dashboard/Commands/AsyncRelayCommand.cs`
- Create: `samples/Hps.Sample.Dashboard/Models/DashboardStatus.cs`
- Create: `samples/Hps.Sample.Dashboard/Models/SmokeRunResult.cs`
- Create: `samples/Hps.Sample.Dashboard/Models/TransportMetricRow.cs`
- Create: `samples/Hps.Sample.Dashboard/ViewModels/DashboardViewModel.cs`
- Create: `tests/Hps.Sample.Dashboard.Tests/DashboardViewModelTests.cs`

- [x] **Step 1: Write failing ViewModel tests**

Create `tests/Hps.Sample.Dashboard.Tests/DashboardViewModelTests.cs`.

```csharp
using System.Linq;
using System.Threading.Tasks;
using Hps.Sample.Dashboard.Models;
using Hps.Sample.Dashboard.ViewModels;
using Xunit;

namespace Hps.Sample.Dashboard.Tests
{
    public sealed class DashboardViewModelTests
    {
        [Fact]
        public void Constructor_WhenCreated_ExposesInitialDashboardState()
        {
            // UI 첫 렌더링 전에 service 실행 없이도 안정적인 초기 상태와 command binding 이 준비되어야 한다.
            DashboardViewModel viewModel = new DashboardViewModel();

            Assert.Equal(DashboardStatus.Stopped, viewModel.ServerStatus);
            Assert.Equal("중지됨", viewModel.ServerStatusText);
            Assert.True(viewModel.StartServerCommand.CanExecute(null));
            Assert.False(viewModel.StopServerCommand.CanExecute(null));
            Assert.True(viewModel.RunTcpSmokeCommand.CanExecute(null));
            Assert.True(viewModel.RunUdpSmokeCommand.CanExecute(null));
        }

        [Fact]
        public void AddLog_WhenMoreThanMaximumEntries_RemovesOldestEntries()
        {
            // sample dashboard 는 장시간 켜둘 수 있으므로 log collection 이 무한 증가하지 않아야 한다.
            DashboardViewModel viewModel = new DashboardViewModel(3);

            viewModel.AddLog("one");
            viewModel.AddLog("two");
            viewModel.AddLog("three");
            viewModel.AddLog("four");

            Assert.Equal(new[] { "two", "three", "four" }, viewModel.LogEntries.ToArray());
        }

        [Fact]
        public void ApplySmokeResult_WhenResultContainsCounters_UpdatesSummaryText()
        {
            // TCP/UDP smoke 결과는 UI에서 sent/received/drop/error/leak 를 한 줄로 비교할 수 있어야 한다.
            DashboardViewModel viewModel = new DashboardViewModel();
            SmokeRunResult result = new SmokeRunResult("TCP", true, 1, 1, 0, 0, 0, "ok");

            viewModel.ApplySmokeResult(result);

            Assert.Equal("TCP: sent=1, received=1, dropped=0, payload-errors=0, pool-rented=0", viewModel.LastSmokeSummary);
        }

        [Fact]
        public async Task AsyncCommand_WhenRunning_DisablesConcurrentExecution()
        {
            // 사용자가 smoke 버튼을 연타해도 같은 작업이 동시에 중복 실행되면 안 된다.
            int executionCount = 0;
            AsyncRelayCommand command = new AsyncRelayCommand(async delegate
            {
                executionCount++;
                await Task.Delay(50);
            });

            Task first = command.ExecuteAsync();
            Assert.False(command.CanExecute(null));

            command.Execute(null);
            await first;

            Assert.Equal(1, executionCount);
            Assert.True(command.CanExecute(null));
        }
    }
}
```

- [x] **Step 2: Run Red**

Run:

```powershell
dotnet test tests\Hps.Sample.Dashboard.Tests\Hps.Sample.Dashboard.Tests.csproj --filter DashboardViewModelTests -v minimal
```

Expected: FAIL due to missing `DashboardViewModel`, `DashboardStatus`, `SmokeRunResult`, `AsyncRelayCommand`.

- [x] **Step 3: Implement minimal MVVM core**

Create the listed command/model/ViewModel files. Use classic namespace syntax and explicit `using` statements.

`SmokeRunResult` constructor signature:

```csharp
public SmokeRunResult(
    string protocol,
    bool succeeded,
    int sent,
    int received,
    long dropped,
    int payloadErrors,
    int poolRented,
    string message)
```

`DashboardViewModel` must expose:

```csharp
public DashboardStatus ServerStatus { get; private set; }
public string ServerStatusText { get; }
public string LastSmokeSummary { get; private set; }
public ObservableCollection<string> LogEntries { get; }
public ObservableCollection<TransportMetricRow> Metrics { get; }
public ICommand StartServerCommand { get; }
public ICommand StopServerCommand { get; }
public ICommand RunTcpSmokeCommand { get; }
public ICommand RunUdpSmokeCommand { get; }
public void AddLog(string message)
public void ApplySmokeResult(SmokeRunResult result)
```

`AsyncRelayCommand` must expose `Task ExecuteAsync()` so tests can await the command without WPF dispatcher coupling.

- [x] **Step 4: Run Green**

Run:

```powershell
dotnet test tests\Hps.Sample.Dashboard.Tests\Hps.Sample.Dashboard.Tests.csproj --filter DashboardViewModelTests -v minimal
```

Expected: PASS.

- [x] **Step 5: Commit**

```powershell
git add samples\Hps.Sample.Dashboard\Commands samples\Hps.Sample.Dashboard\Models samples\Hps.Sample.Dashboard\ViewModels tests\Hps.Sample.Dashboard.Tests\DashboardViewModelTests.cs
git commit -m "feat(sample): add dashboard mvvm core"
```

## Task 3: Broker lifecycle 와 diagnostics service

**Files:**
- Create: `samples/Hps.Sample.Dashboard/Services/DashboardBrokerService.cs`
- Create: `samples/Hps.Sample.Dashboard/Services/DiagnosticsSnapshotService.cs`
- Create: `tests/Hps.Sample.Dashboard.Tests/DiagnosticsSnapshotServiceTests.cs`
- Modify: `samples/Hps.Sample.Dashboard/ViewModels/DashboardViewModel.cs`

- [x] **Step 1: Write failing service tests**

Create `tests/Hps.Sample.Dashboard.Tests/DiagnosticsSnapshotServiceTests.cs`.

```csharp
using Hps.Sample.Dashboard.Services;
using Hps.Transport;
using Xunit;

namespace Hps.Sample.Dashboard.Tests
{
    public sealed class DiagnosticsSnapshotServiceTests
    {
        [Fact]
        public void CreateRows_WhenTransportHasAggregateSnapshot_ReturnsTcpAndUdpRows()
        {
            // BrokerServer 는 diagnostics API 를 직접 노출하지 않으므로 service 는 공유 transport 참조에서 snapshot 을 읽어야 한다.
            FakeDiagnosticsTransport transport = new FakeDiagnosticsTransport(
                new TransportDiagnosticsSnapshot(
                    tcpDroppedPendingSendCount: 2,
                    udpDroppedPendingSendCount: 3,
                    tcpPendingSendQueueHighWatermark: 4,
                    udpPendingSendQueueHighWatermark: 5));
            DiagnosticsSnapshotService service = new DiagnosticsSnapshotService();

            var rows = service.CreateRows(transport);

            Assert.Collection(
                rows,
                row =>
                {
                    Assert.Equal("TCP", row.Name);
                    Assert.Equal(2, row.DroppedPendingSendCount);
                    Assert.Equal(4, row.PendingSendQueueHighWatermark);
                },
                row =>
                {
                    Assert.Equal("UDP", row.Name);
                    Assert.Equal(3, row.DroppedPendingSendCount);
                    Assert.Equal(5, row.PendingSendQueueHighWatermark);
                });
        }

        private sealed class FakeDiagnosticsTransport : ITransportDiagnostics
        {
            private readonly TransportDiagnosticsSnapshot _snapshot;

            internal FakeDiagnosticsTransport(TransportDiagnosticsSnapshot snapshot)
            {
                _snapshot = snapshot;
            }

            public TransportDiagnosticsSnapshot GetDiagnosticsSnapshot()
            {
                return _snapshot;
            }
        }
    }
}
```

- [x] **Step 2: Run Red**

Run:

```powershell
dotnet test tests\Hps.Sample.Dashboard.Tests\Hps.Sample.Dashboard.Tests.csproj --filter DiagnosticsSnapshotServiceTests -v minimal
```

Expected: FAIL due to missing `DiagnosticsSnapshotService`.

- [x] **Step 3: Implement services**

Implement `DiagnosticsSnapshotService.CreateRows(object? transport)` so it checks `transport as ITransportDiagnostics`.

Implement `DashboardBrokerService` with this public surface:

```csharp
public sealed class DashboardBrokerService : IDisposable
{
    public EndPoint? TcpLocalEndPoint { get; }
    public EndPoint? UdpLocalEndPoint { get; }
    public object? DiagnosticsSource { get; }
    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    public void Dispose()
}
```

`StartAsync` creates:

```csharp
SaeaTransport transport = new SaeaTransport();
PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(65536);
BrokerServer server = new BrokerServer(transport, pool, 65536);
await server.StartTcpAsync(new IPEndPoint(IPAddress.Loopback, 0), cancellationToken).ConfigureAwait(false);
await server.StartUdpAsync(new IPEndPoint(IPAddress.Loopback, 0), cancellationToken).ConfigureAwait(false);
```

The service keeps the same `transport` reference for diagnostics.

- [x] **Step 4: Run Green**

Run:

```powershell
dotnet test tests\Hps.Sample.Dashboard.Tests\Hps.Sample.Dashboard.Tests.csproj --filter DiagnosticsSnapshotServiceTests -v minimal
dotnet build samples\Hps.Sample.Dashboard\Hps.Sample.Dashboard.csproj -v minimal
```

Expected: PASS.

- [x] **Step 5: Commit**

```powershell
git add samples\Hps.Sample.Dashboard\Services samples\Hps.Sample.Dashboard\ViewModels\DashboardViewModel.cs tests\Hps.Sample.Dashboard.Tests\DiagnosticsSnapshotServiceTests.cs
git commit -m "feat(sample): add dashboard broker diagnostics services"
```

## Task 4: TCP smoke service

**Files:**
- Create: `samples/Hps.Sample.Dashboard/Services/TcpSmokeTestService.cs`
- Create: `tests/Hps.Sample.Dashboard.Tests/TcpSmokeTestServiceTests.cs`
- Modify: `samples/Hps.Sample.Dashboard/ViewModels/DashboardViewModel.cs`

- [x] **Step 1: Write failing TCP smoke test**

Create `tests/Hps.Sample.Dashboard.Tests/TcpSmokeTestServiceTests.cs`.

```csharp
using System.Threading.Tasks;
using Hps.Sample.Dashboard.Services;
using Xunit;

namespace Hps.Sample.Dashboard.Tests
{
    public sealed class TcpSmokeTestServiceTests
    {
        [Fact]
        public async Task RunAsync_WhenBrokerLoopbackRuns_DeliversPayloadWithoutLeak()
        {
            // 실제 SAEA TCP listener/receive/send pump 와 Broker fan-out 을 묶어 샘플 버튼이 의미 있는 end-to-end 검증이 되게 한다.
            TcpSmokeTestService service = new TcpSmokeTestService();

            var result = await service.RunAsync();

            Assert.True(result.Succeeded, result.Message);
            Assert.Equal("TCP", result.Protocol);
            Assert.Equal(1, result.Sent);
            Assert.Equal(1, result.Received);
            Assert.Equal(0, result.Dropped);
            Assert.Equal(0, result.PayloadErrors);
            Assert.Equal(0, result.PoolRented);
        }
    }
}
```

- [x] **Step 2: Run Red**

Run:

```powershell
dotnet test tests\Hps.Sample.Dashboard.Tests\Hps.Sample.Dashboard.Tests.csproj --filter TcpSmokeTestServiceTests -v minimal
```

Expected: FAIL due to missing `TcpSmokeTestService`.

- [x] **Step 3: Implement TCP smoke**

Use the same flow as `TcpLoopbackScenarioRunner`:

- create `SaeaTransport`
- create `PinnedBlockMemoryPool(65536)`
- create `BrokerServer`
- `StartTcpAsync(new IPEndPoint(IPAddress.Loopback, 0))`
- create subscriber and publisher sockets
- send `SUBSCRIBE alpha` as TCP frame
- wait until subscription count is 1 by reflecting `_subscriptions`
- send `PUBLISH alpha <payload>` as TCP frame
- receive length-prefixed outbound payload from subscriber
- stop server and wait until `pool.RentedCount == 0`

Keep these helper names in `TcpSmokeTestService`:

```csharp
private static byte[] CreatePublishCommand(string topic, byte[] payload)
private static Task SendFrameAsync(Socket socket, byte[] payload)
private static Task<byte[]> ReceiveFrameAsync(Socket socket)
private static Task WaitForSubscriberCountAsync(BrokerServer server, string topic, int expected)
```

- [x] **Step 4: Run Green**

Run:

```powershell
dotnet test tests\Hps.Sample.Dashboard.Tests\Hps.Sample.Dashboard.Tests.csproj --filter TcpSmokeTestServiceTests -v minimal
```

Expected: PASS.

- [x] **Step 5: Commit**

```powershell
git add samples\Hps.Sample.Dashboard\Services\TcpSmokeTestService.cs samples\Hps.Sample.Dashboard\ViewModels\DashboardViewModel.cs tests\Hps.Sample.Dashboard.Tests\TcpSmokeTestServiceTests.cs
git commit -m "feat(sample): add tcp smoke service"
```

## Task 5: UDP smoke service

**Files:**
- Create: `samples/Hps.Sample.Dashboard/Services/UdpSmokeTestService.cs`
- Create: `tests/Hps.Sample.Dashboard.Tests/UdpSmokeTestServiceTests.cs`
- Modify: `samples/Hps.Sample.Dashboard/ViewModels/DashboardViewModel.cs`

- [x] **Step 1: Write failing UDP smoke test**

Create `tests/Hps.Sample.Dashboard.Tests/UdpSmokeTestServiceTests.cs`.

```csharp
using System.Threading.Tasks;
using Hps.Sample.Dashboard.Services;
using Xunit;

namespace Hps.Sample.Dashboard.Tests
{
    public sealed class UdpSmokeTestServiceTests
    {
        [Fact]
        public async Task RunAsync_WhenBrokerDatagramLoopbackRuns_DeliversPayloadWithoutLeak()
        {
            // 실제 UDP command handler, endpoint receive/send pump, Broker fan-out 을 묶어 UI smoke 의 신뢰도를 확보한다.
            UdpSmokeTestService service = new UdpSmokeTestService();

            var result = await service.RunAsync();

            Assert.True(result.Succeeded, result.Message);
            Assert.Equal("UDP", result.Protocol);
            Assert.Equal(1, result.Sent);
            Assert.Equal(1, result.Received);
            Assert.Equal(0, result.Dropped);
            Assert.Equal(0, result.PayloadErrors);
            Assert.Equal(0, result.PoolRented);
        }
    }
}
```

- [x] **Step 2: Run Red**

Run:

```powershell
dotnet test tests\Hps.Sample.Dashboard.Tests\Hps.Sample.Dashboard.Tests.csproj --filter UdpSmokeTestServiceTests -v minimal
```

Expected: FAIL due to missing `UdpSmokeTestService`.

- [x] **Step 3: Implement UDP smoke**

Use the same flow as `BrokerServerTests.UdpCommandLoopback_WhenSubscriberAndPublisherUseDatagramCommands_FansOutPayload`:

- create `SaeaTransport`
- create `PinnedBlockMemoryPool(128)`
- create `BrokerServer`
- `StartUdpAsync(new IPEndPoint(IPAddress.Loopback, 0))`
- create bound UDP subscriber and publisher sockets
- send `SUBSCRIBE alpha`
- wait until subscription count is 1 by reflecting `_subscriptions`
- send `PUBLISH alpha <payload>`
- receive datagram from subscriber
- stop server and assert pool rented count becomes 0

Keep these helper names in `UdpSmokeTestService`:

```csharp
private static byte[] CreatePublishCommand(string topic, byte[] payload)
private static Task SendDatagramAsync(Socket socket, EndPoint remoteEndPoint, byte[] payload)
private static Task<byte[]> ReceiveDatagramPayloadAsync(Socket socket, int maxLength)
private static Task WaitForSubscriberCountAsync(BrokerServer server, string topic, int expected)
```

- [x] **Step 4: Run Green**

Run:

```powershell
dotnet test tests\Hps.Sample.Dashboard.Tests\Hps.Sample.Dashboard.Tests.csproj --filter UdpSmokeTestServiceTests -v minimal
```

Expected: PASS.

- [x] **Step 5: Commit**

```powershell
git add samples\Hps.Sample.Dashboard\Services\UdpSmokeTestService.cs samples\Hps.Sample.Dashboard\ViewModels\DashboardViewModel.cs tests\Hps.Sample.Dashboard.Tests\UdpSmokeTestServiceTests.cs
git commit -m "feat(sample): add udp smoke service"
```

## Task 6: WPF UI binding, run instructions, full verification

**Files:**
- Modify: `samples/Hps.Sample.Dashboard/MainWindow.xaml`
- Modify: `samples/Hps.Sample.Dashboard/MainWindow.xaml.cs`
- Modify: `samples/Hps.Sample.Dashboard/ViewModels/DashboardViewModel.cs`
- Create: `samples/Hps.Sample.Dashboard/README.md`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

- [x] **Step 1: Write failing ViewModel orchestration test**

Append to `DashboardViewModelTests.cs`.

```csharp
[Fact]
public async Task RunTcpSmokeCommand_WhenExecuted_AddsResultToLog()
{
    // UI button 은 service 결과를 log 와 summary 에 반영해야 사용자가 성공/실패를 즉시 판단할 수 있다.
    DashboardViewModel viewModel = DashboardViewModel.CreateForTests(
        tcpSmoke: delegate { return Task.FromResult(new SmokeRunResult("TCP", true, 1, 1, 0, 0, 0, "ok")); },
        udpSmoke: delegate { return Task.FromResult(new SmokeRunResult("UDP", true, 0, 0, 0, 0, 0, "not-run")); });

    await ((AsyncRelayCommand)viewModel.RunTcpSmokeCommand).ExecuteAsync();

    Assert.Equal("TCP: sent=1, received=1, dropped=0, payload-errors=0, pool-rented=0", viewModel.LastSmokeSummary);
    Assert.Contains(viewModel.LogEntries, entry => entry.Contains("TCP smoke 성공"));
}
```

- [x] **Step 2: Run Red**

Run:

```powershell
dotnet test tests\Hps.Sample.Dashboard.Tests\Hps.Sample.Dashboard.Tests.csproj --filter RunTcpSmokeCommand_WhenExecuted_AddsResultToLog -v minimal
```

Expected: FAIL due to missing `CreateForTests` or command service wiring.

- [x] **Step 3: Wire ViewModel and UI**

Update `DashboardViewModel` so the default constructor creates:

```csharp
new DashboardBrokerService()
new TcpSmokeTestService()
new UdpSmokeTestService()
new DiagnosticsSnapshotService()
new IoUringEvidenceStatusService()
```

Update `MainWindow.xaml.cs`:

```csharp
public MainWindow()
{
    InitializeComponent();
    DataContext = new DashboardViewModel();
}
```

Update `MainWindow.xaml` to include:

- top toolbar buttons bound to `StartServerCommand`, `StopServerCommand`, `RunTcpSmokeCommand`, `RunUdpSmokeCommand`
- four status panels: server, TCP, UDP, io_uring
- `DataGrid` bound to `Metrics`
- `ListBox` bound to `LogEntries`

Create `samples/Hps.Sample.Dashboard/README.md`.

```markdown
# Hps.Sample.Dashboard

WPF 기반 Interface Server 확인용 샘플이다.

## 실행

```powershell
dotnet run --project samples\Hps.Sample.Dashboard\Hps.Sample.Dashboard.csproj
```

## 확인 항목

- `Start Server`: TCP/UDP broker endpoint 를 loopback 에 bind 한다.
- `TCP Smoke`: TCP SUBSCRIBE/PUBLISH fan-out 을 실제 socket 으로 확인한다.
- `UDP Smoke`: UDP datagram SUBSCRIBE/PUBLISH fan-out 을 실제 socket 으로 확인한다.
- diagnostics grid: transport drop/high-watermark/pending 값을 표시한다.

`io_uring` fixed-buffer evidence 는 Windows WPF 앱에서 직접 실행하지 않는다.
Linux native path 는 원격 `iouring-linux-contract.yml` artifact gate 로 확인한다.
```

- [x] **Step 4: Run full verification**

Run:

```powershell
dotnet test tests\Hps.Sample.Dashboard.Tests\Hps.Sample.Dashboard.Tests.csproj -v minimal
dotnet build HighPerformanceSocket.slnx -v minimal
dotnet test HighPerformanceSocket.slnx -v minimal
```

Run manually when GUI launch is allowed:

```powershell
dotnet run --project samples\Hps.Sample.Dashboard\Hps.Sample.Dashboard.csproj
```

Expected:

- dashboard test project passes
- solution build passes
- solution tests pass
- WPF app opens locally and buttons can run TCP/UDP smoke

- [x] **Step 5: Update state docs and commit**

Update:

- `CURRENT_PLAN.md`: D183 implementation result and next execution point.
- `TODOS.md`: move WPF dashboard implementation plan/current tasks to Completed or next task.
- `CHANGELOG_AGENT.md`: verification commands and GUI limitation if any.

Commit:

```powershell
git add samples\Hps.Sample.Dashboard tests\Hps.Sample.Dashboard.Tests CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "feat(sample): wire wpf dashboard ui"
```

## Self-review 결과

- Spec coverage: WPF 선택, csproj override, MVVM 구조, TCP smoke, UDP smoke, diagnostics, `io_uring` status, run instructions 를 Task 1~6이 모두 커버한다.
- Placeholder scan: 계획에는 미정 placeholder 를 두지 않았다. public API 부족 가능성은 Task 4/5에서 기존 테스트 helper 흐름을 기준으로 검증한다.
- Type consistency: `SmokeRunResult`, `TransportMetricRow`, `DashboardViewModel`, `AsyncRelayCommand`, service class 이름을 전체 task 에서 동일하게 사용한다.
- Scope guard: WinUI 3, production API 확장, WPF 내부 Linux `io_uring` native 실행, fixed-buffer pump 연결, zero-copy send 는 포함하지 않는다.
