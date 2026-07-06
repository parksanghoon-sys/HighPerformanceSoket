# 2026-07-06 WPF sample dashboard design

## 상태

Proposed.

이 문서는 Interface Server 를 실제로 실행해 보고 TCP/UDP publish-subscribe 경로와 핵심 진단값을 확인할 수 있는
WPF 샘플 프로젝트의 설계 기준이다. 구현 계획 문서는 이 설계가 수락된 뒤
`docs/superpowers/plans/2026-07-06-wpf-sample-dashboard.md`에 별도로 작성한다.

## 배경

현재 저장소에는 TCP publisher/subscriber 샘플과 `io_uring` backend contributor 용 문서 예제가 있다.
하지만 사용자가 전체 기능을 눈으로 확인하기에는 실행 흐름이 분산되어 있고, TCP/UDP endpoint 상태,
drop/high-watermark/pending count 같은 운영 관측값을 한 화면에서 비교하기 어렵다.

샘플 UI는 production API 를 넓히기 위한 기능이 아니라, 지금까지 구현된 Interface Server 흐름을
검증하기 쉬운 형태로 묶는 실행 예제다.

## 결정

샘플 UI 는 WinUI 3가 아니라 WPF 로 만든다.

- WPF 는 .NET SDK 기반 `dotnet run` 실행 경로가 단순하고, Windows App SDK packaging/runtime 부담을 추가하지 않는다.
- 이 저장소의 목적은 UI framework 검증이 아니라 Interface Server 검증이다. 샘플은 최대한 적은 외부 전제 위에서 돌아야 한다.
- MVVM 구조를 적용하되, 샘플 앱에 과도한 framework 의존성을 추가하지 않는다.
- WPF 프로젝트는 Windows 전용 sample 로 제한하고, transport/backend production 선택 정책에는 영향을 주지 않는다.

## 목표

- 사용자가 로컬 Windows 환경에서 WPF 앱을 실행해 Interface Server 의 기본 동작을 확인할 수 있게 한다.
- TCP publisher/subscriber loopback smoke 를 앱 안에서 실행하고 결과를 표시한다.
- UDP endpoint publish-subscribe smoke 를 앱 안에서 실행하고 결과를 표시한다.
- transport diagnostics snapshot 기반으로 pending send count, high-watermark, dropped count 를 표시한다.
- `io_uring` fixed-buffer submission 은 Windows WPF 앱에서 직접 실행하지 않고, 현재 상태와 원격 Linux contract gate 필요성을 표시한다.

## 프로젝트 구조

새 sample 프로젝트를 추가한다.

```text
samples/Hps.Sample.Dashboard/
  Hps.Sample.Dashboard.csproj
  App.xaml
  App.xaml.cs
  MainWindow.xaml
  MainWindow.xaml.cs
  Commands/
    RelayCommand.cs
    AsyncRelayCommand.cs
  Models/
    DashboardStatus.cs
    SmokeRunResult.cs
    TransportMetricRow.cs
  Services/
    DashboardBrokerService.cs
    TcpSmokeTestService.cs
    UdpSmokeTestService.cs
    DiagnosticsSnapshotService.cs
    IoUringEvidenceStatusService.cs
  ViewModels/
    DashboardViewModel.cs
    MetricRowViewModel.cs
```

테스트가 필요한 순수 로직은 별도 test project 로 분리한다.

```text
tests/Hps.Sample.Dashboard.Tests/
  DashboardViewModelTests.cs
  TcpSmokeTestServiceTests.cs
  UdpSmokeTestServiceTests.cs
```

## 프로젝트 빌드 계약

루트 `Directory.Build.props`는 전체 저장소 기본값으로 `TargetFramework=net9.0`, `LangVersion=8.0`,
`ImplicitUsings=disable`을 적용한다. WPF sample 은 이 기본 TFM 을 그대로 상속하면 빌드될 수 없으므로,
sample project 에서 Windows 전용 TFM 과 WPF 속성을 명시적으로 override 한다.

```xml
<PropertyGroup>
  <TargetFramework>net9.0-windows</TargetFramework>
  <UseWPF>true</UseWPF>
  <OutputType>WinExe</OutputType>
</PropertyGroup>
```

테스트 프로젝트는 WPF sample project 를 참조해야 하므로 `net9.0-windows`를 사용한다. 단, 테스트 대상은
ViewModel/service/command 같은 순수 로직으로 제한하고 WPF UI automation 은 이번 범위에서 제외한다.

샘플도 저장소 공통 C# 8.0 규칙을 따른다. 따라서 record, file-scoped namespace, target-typed `new()`,
global using, `init` 접근자 같은 C# 9+ 문법은 사용하지 않는다.

## 서비스 경계

### DashboardBrokerService

- `BrokerServer` 또는 현재 sample host 가 제공하는 public surface 를 통해 서버 lifecycle 을 담당한다.
- UI thread 와 transport thread 경계를 섞지 않는다.
- start/stop 은 idempotent 하게 유지하고, 실패 결과는 ViewModel 이 표시할 수 있는 result model 로 변환한다.
- diagnostics 를 위해 생성한 `ITransport` instance 를 보관하고 `DiagnosticsSnapshotService`에 같은 참조를 공유한다.
  `BrokerServer` 자체는 diagnostics public API 를 직접 노출하지 않으므로, UI가 server 를 통해 snapshot 을 읽는 구조로 설계하지 않는다.

### TcpSmokeTestService

- WPF 앱 안에서 TCP subscriber/publisher loopback 을 실행한다.
- 기존 protocol framing/helper 를 재사용한다.
- 성공 조건은 publish payload 가 subscriber 에 도착하고, payload error 와 pool leak 이 없는 것이다.

### UdpSmokeTestService

- UDP register/subscribe/publish 경로를 실행한다.
- `BrokerServer.StartUdpAsync`, `UdpLocalEndPoint`, UDP command datagram 경로는 이미 public entry 로 존재한다.
- 구현 계획 단계에서는 `tests/Hps.Server.Tests`의 UDP command loopback helper 흐름을 대조해 register/subscribe/publish 조합을 확정한다.
- public surface 부족이 발견되더라도 production API 를 즉시 넓히지 않고, 부족한 경계를 별도 task 로 분리한다.

### DiagnosticsSnapshotService

- transport/endpoint diagnostics snapshot 을 UI 표시 모델로 변환한다.
- `DashboardBrokerService`가 생성·보관하는 동일한 `ITransport` 참조를 받아 `ITransportDiagnostics`와
  가능하면 `ITransportEndpointDiagnostics`로 좁혀 읽는다.
- drop count, pending count, high-watermark 를 그대로 보여주고, UI 계산 로직으로 의미를 바꾸지 않는다.

### IoUringEvidenceStatusService

- Windows WPF 앱에서는 Linux `io_uring` native path 를 실행하지 않는다.
- D181 결정(`DECISIONS.md`)과 D182 예제/상태 기록(`CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`,
  `docs/examples/iouring-fixed-buffer-submission-example.md`)을 바탕으로 local status 와 원격
  `iouring-linux-contract.yml` gate 필요성을 표시한다.
- production pump fixed-buffer 연결, zero-copy send, default promotion 을 암시하지 않는다.

## MVVM 규칙

- View 는 XAML binding 과 최소 code-behind 만 가진다.
- ViewModel 은 service orchestration 과 UI 상태만 가진다.
- service 는 WPF control type 을 참조하지 않는다.
- command 는 실행 중 중복 클릭을 막고, 완료/실패 후 상태를 갱신한다.
- 로그는 ObservableCollection 기반으로 보존하되, 샘플 앱이므로 bounded count 를 둔다.

## UI 구성

첫 화면은 실제 실행 도구여야 하며 landing page 로 만들지 않는다.

- 상단: server start/stop, TCP smoke, UDP smoke, clear log 버튼.
- 중앙: Server, TCP, UDP, io_uring evidence 상태 카드.
- 하단: diagnostics grid 와 실행 로그.
- TCP/UDP smoke 결과는 sent, received, dropped, payload errors, pool rented 를 한눈에 비교할 수 있게 표시한다.
- visual polish 는 과하지 않게 유지하고, 운영 도구처럼 읽기 쉬운 밀도와 명확한 상태 색상을 우선한다.

## 제외 범위

- WinUI 3 / Windows App SDK 추가.
- production transport API 를 UI 편의를 위해 즉시 확장.
- Linux `io_uring` native execution 을 WPF 앱에서 직접 수행.
- TCP/UDP pump fixed-buffer 연결, zero-copy send, default backend promotion.
- 인증, TLS, persistence, clustering.
- WPF UI automation test. 이번 단위에서는 ViewModel/service test 와 수동 실행 경로로 제한한다.

## 구현 순서

1. WPF project skeleton 과 solution inclusion 을 추가한다.
2. MVVM command/model/ViewModel 기본 뼈대를 만들고 ViewModel unit test 를 먼저 작성한다.
3. server lifecycle service 와 TCP smoke service 를 붙인다.
4. UDP smoke service 와 diagnostics snapshot 표시를 붙인다.
5. XAML 화면과 run instructions 를 정리한다.

각 단계는 별도 커밋 단위로 나누며, 구현 전 실패 테스트를 먼저 둔다. UI-only XAML 배치는 build 와 수동 실행 검증으로 보완한다.

## 검증 계획

- `dotnet build HighPerformanceSocket.slnx -v minimal`
- `dotnet test HighPerformanceSocket.slnx -v minimal`
- `dotnet run --project samples\Hps.Sample.Dashboard\Hps.Sample.Dashboard.csproj`

GUI 실행이 sandbox 에서 제한되면 build/test 를 완료하고, 사용자 로컬 실행 명령을 문서에 남긴다.

## 다음 단계

- 이 설계가 수락되면 `docs/superpowers/plans/2026-07-06-wpf-sample-dashboard.md`에 TDD 구현 계획을 작성한다.
- 구현 계획에서는 TCP/UDP smoke 가 실제로 사용할 public API 를 먼저 확인하고, public surface 부족분이 있으면
  생산 코드 확장 여부를 별도 task 로 분리한다.
