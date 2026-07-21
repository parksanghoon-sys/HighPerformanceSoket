# Mixed TCP Workload Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 기존 4096B baseline을 변경하지 않고 data 10,240B x 100 Hz 이상과 control 2,560B x 100 Hz를 별도 TCP connection으로 동시에 전송하는 mixed open-loop hard gate를 구현한다.

**Architecture:** benchmark project 안에 목표 전용 options, result, JSON writer와 runner를 추가한다. runner는 기존 `BrokerServer`, backend selector, transport diagnostics를 재사용하되 범용 workload graph를 만들지 않고 data/control 두 stream만 직접 조율한다. subscriber별 percentile의 최댓값으로 latency를 판정하고, 실행 전 subscriber 수와 계측 메모리를 제한한다. 먼저 단일 논리 구독자를 검증하고 다음 리뷰 단위에서 같은 runner를 N명 fan-out으로 확장한 뒤, 마지막에만 CLI와 Linux artifact workflow에 노출한다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, `Socket` memory overload, `PinnedBlockMemoryPool`, `BrokerServer`, SAEA, Windows RIO, Linux io_uring, `Utf8JsonWriter`, GitHub Actions

## Global Constraints

- 저장소 루트의 `AGENTS.md`, `AGENT_RULES.md`, D009, D010, D011, D012, D013, D239, D241, D243을 따른다.
- production 변경 전에 assertion failure인 Red를 먼저 확인한다. 컴파일 실패, hang 또는 test discovery 0은 Red로 인정하지 않는다.
- C# 8.0 문법만 사용하고 TFM `net9.0`, nullable, explicit using 정책을 유지한다.
- 새 NuGet dependency를 추가하지 않는다.
- 기존 `BenchmarkTargets`, `TcpLoopbackRunResult`, schema-version 1 writer/reader, summary/history/envelope와 TCP/UDP baseline 동작을 변경하지 않는다.
- mixed raw JSON은 `report-kind: mixed-tcp-workload`, `schema-version: 2`를 사용한다. report kind가 문서 종류를 식별하고, 현재 `BaselineReportReader`는 version 1만 읽으므로 version 2가 즉시 적용되는 legacy aggregate 격리 경계다.
- mixed 경로는 TCP 전용이다. UDP block 확대, fragmentation/reassembly, reliability를 열지 않는다.
- benchmark가 실제 production 결함을 증명하기 전에는 `src/Hps.Broker`, `src/Hps.Protocol`, `src/Hps.Server`, `src/Hps.Transport*`를 수정하지 않는다.
- publisher frame과 subscriber receive buffer는 connection별로 `PinnedBlockMemoryPool`에서 한 번 대여해 재사용한다. per-message `new byte[]`, `Buffer.BlockCopy`, `Encoding.GetBytes` 결과 배열을 만들지 않는다.
- data와 control은 topic, publisher socket, subscriber socket, pacing task를 분리한다. 논리 구독자 N명은 `2 + 2N` TCP client connection을 사용한다.
- data payload는 rate가 높아져도 10,240B를 유지한다. control은 2,560B x 100 Hz로 고정한다.
- options는 subscriber 최대 256명과 latency 원본 배열 전체 및 재사용 scratch 하나의 payload 합계 128MiB를 socket/배열 생성 전에 검증한다.
- stream hard gate는 subscriber별 exact count/order/payload, `(sent - 1)` interval actual rate 99% 이상, subscriber별 p99 5,000us 이하, p999 10,000us 이하다.
- stream p50/p99/p999와 first/second-half p99는 subscriber별 계산값 중 최댓값이며 aggregate sample percentile이 아니다.
- global hard gate는 transport drop 0, drain 시 endpoint pending send 합 0, fallback pool rented 0, timeout 0이다.
- latency hard gate는 mixed command에만 적용한다. legacy baseline latency report-only 정책은 유지한다.
- 각 구현 Task는 하나의 cycle과 commit으로 끝내고 사용자 review stop에서 멈춘다. 다음 Task를 자동으로 시작하지 않는다.
- 기존 untracked `.claude/review/*.md`와 `diff/`는 읽기, 수정, stage 대상에서 제외한다.

---

## File Map

### 새 benchmark 파일

- `tests/Hps.Benchmarks/MixedWorkloadOptions.cs`: 고정 profile, checked 계획 수, subscriber/계측 메모리 안전 상한 검증.
- `tests/Hps.Benchmarks/MixedWorkloadStreamResult.cs`: stream별 전달, `N - 1` interval rate, worst-subscriber latency hard gate.
- `tests/Hps.Benchmarks/MixedWorkloadRunResult.cs`: 두 stream과 global diagnostics를 합친 최종 판정 및 console 출력.
- `tests/Hps.Benchmarks/MixedWorkloadReportWriter.cs`: report kind와 schema-version 2로 legacy reader와 분리된 JSON.
- `tests/Hps.Benchmarks/TcpMixedWorkloadScenarioRunner.cs`: data/control 전용 TCP topology, pacing, reusable pinned client buffer, 계측.

### 새 test 파일

- `tests/Hps.Benchmarks.Tests/MixedWorkloadOptionsTests.cs`
- `tests/Hps.Benchmarks.Tests/MixedWorkloadResultTests.cs`
- `tests/Hps.Benchmarks.Tests/MixedWorkloadReportWriterTests.cs`
- `tests/Hps.Benchmarks.Tests/TcpMixedWorkloadScenarioRunnerTests.cs`

### 기존 수정 파일

- `tests/Hps.Benchmarks/BenchmarkCommand.cs`
- `tests/Hps.Benchmarks/BenchmarkCommandLine.cs`
- `tests/Hps.Benchmarks/BenchmarkCommandParser.cs`
- `tests/Hps.Benchmarks/BenchmarkRunIdentity.cs`
- `tests/Hps.Benchmarks/Program.cs`
- `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`
- `tests/Hps.Benchmarks.Tests/BenchmarkRunIdentityTests.cs`
- `tests/Hps.Benchmarks.Tests/BenchmarkProgramProtocolTests.cs`
- `.github/workflows/iouring-benchmark-artifacts.yml`
- `tests/Hps.Benchmarks.Tests/BenchmarkArtifactWorkflowTests.cs`
- root/archive 상태 문서

---

## Review Checkpoint Protocol

Task 2~7 각각에서 다음 순서를 지킨다.

1. 해당 Task의 assertion Red를 확인한다.
2. 그 Red만 통과시키는 최소 Green을 구현한다.
3. focused test와 `Hps.Benchmarks.Tests` 전체를 실행한다.
4. `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, 월별 changelog를 실제 결과로 갱신한다.
5. decision 의미가 바뀐 경우에만 `DECISIONS.md`와 월별 decisions를 수정한다.
6. Task 소유 파일만 commit한다.
7. 사용자 review stop에서 멈춘다.

---

## Task 1: 매 구현 cycle의 기준선과 범위를 확인한다

**Files:**

- Read: `AGENTS.md`
- Read: `AGENT_RULES.md`
- Read: `PLAN.md`
- Read: `CURRENT_PLAN.md`
- Read: `TODOS.md`
- Read: `CHANGELOG_AGENT.md`
- Read: `DECISIONS.md`
- Read: `docs/superpowers/specs/2026-07-18-mixed-tcp-workload-gate-design.md`
- Read: `.claude/review/`의 현재 Task 관련 review 문서

**Interfaces:**

- Consumes: D243 accepted spec와 직전 Task의 review 결과.
- Produces: 한 cycle에서 수정할 파일 allow-list와 재현 가능한 baseline test 결과.

- [x] **Step 1: checkout 소유권을 확인한다**

Run:

```powershell
git status --short --branch
git diff --check
git log -8 --oneline
```

Expected:

- 기존 untracked `.claude/review/*.md`, `diff/` 외에 설명되지 않은 변경이 없어야 한다.
- 현재 Task 파일과 겹치는 사용자 변경이 있으면 덮어쓰지 말고 실제 diff를 읽어 계획을 재평가한다.
- push는 수행하지 않는다.

- [x] **Step 2: benchmark 기준선을 확인한다**

Run:

```powershell
dotnet test tests/Hps.Benchmarks.Tests/Hps.Benchmarks.Tests.csproj -c Release --no-restore -p:NuGetAudit=false -v minimal
```

Expected: 현재 checkout의 benchmark tests 전체 green이며 실행/통과 수가 0보다 커야 한다. 실패하면 신규 Red를 추가하기 전에 baseline failure를 분리한다.

---

## Task 2: Mixed workload options와 실행 전 자원 경계를 고정한다

**Files:**

- Create: `tests/Hps.Benchmarks/MixedWorkloadOptions.cs`
- Create: `tests/Hps.Benchmarks.Tests/MixedWorkloadOptionsTests.cs`
- Modify: Task 종료 시 root/archive 상태 문서

**Interfaces:**

- Consumes: 없음.
- Produces: `MixedWorkloadOptions()`, `MixedWorkloadOptions(int dataRateHz, int durationSeconds, int subscriberCount)`.
- Produces properties: `DataRateHz`, `DurationSeconds`, `SubscriberCount`, `DataMessageCount`, `ControlMessageCount`, `DataDeliveryCount`, `ControlDeliveryCount`, `ClientConnectionCount`, `EstimatedLatencyStorageBytes`.
- Produces constants: payload 10,240/2,560B, default/min data rate 100Hz, control rate 100Hz, default duration 30초, default subscriber 1명, subscriber 상한 256명, latency 저장 상한 128MiB, max frame 16,384B, rate ratio 0.99, p99/p999 5,000/10,000us.

- [x] **Step 1: type 부재 assertion Red를 작성한다**

```csharp
[Fact]
public void Contract_MixedWorkloadOptionsExposesFixedProfileAndDerivedCounts()
{
    Type? type = typeof(BenchmarkCommandParser).Assembly.GetType("Hps.Benchmarks.MixedWorkloadOptions");

    Assert.NotNull(type);
    Assert.NotNull(type!.GetConstructor(Type.EmptyTypes));
    Assert.NotNull(type.GetConstructor(new[] { typeof(int), typeof(int), typeof(int) }));
    Assert.NotNull(type.GetProperty("DataMessageCount"));
    Assert.NotNull(type.GetProperty("ControlMessageCount"));
    Assert.NotNull(type.GetProperty("DataDeliveryCount"));
    Assert.NotNull(type.GetProperty("ControlDeliveryCount"));
    Assert.NotNull(type.GetProperty("ClientConnectionCount"));
    Assert.NotNull(type.GetProperty("EstimatedLatencyStorageBytes"));
}
```

Run:

```powershell
dotnet test tests/Hps.Benchmarks.Tests/Hps.Benchmarks.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~Contract_MixedWorkloadOptionsExposesFixedProfileAndDerivedCounts" -v minimal
```

Expected: `Assert.NotNull(type)` assertion failure 1개. compile failure면 test가 production type을 직접 참조한 것이므로 reflection 경계를 복구한다.

- [x] **Step 2: 최소 type shell로 shape를 Green으로 만든다**

`MixedWorkloadOptions`는 다음 정확한 상수와 생성자 계약을 가진다.

```csharp
internal sealed class MixedWorkloadOptions
{
    public const int DataPayloadBytes = 10240;
    public const int ControlPayloadBytes = 2560;
    public const int DefaultDataRateHz = 100;
    public const int MinimumDataRateHz = 100;
    public const int ControlRateHz = 100;
    public const int DefaultDurationSeconds = 30;
    public const int DefaultSubscriberCount = 1;
    public const int MaximumSubscriberCount = 256;
    public const long MaximumLatencyStorageBytes = 128L * 1024L * 1024L;
    public const int MaxFramePayloadBytes = 16384;
    public const double MinimumRateRatio = 0.99;
    public const double P99LatencyBudgetMicroseconds = 5000.0;
    public const double P999LatencyBudgetMicroseconds = 10000.0;

    public MixedWorkloadOptions()
        : this(DefaultDataRateHz, DefaultDurationSeconds, DefaultSubscriberCount)
    {
    }

    public MixedWorkloadOptions(int dataRateHz, int durationSeconds, int subscriberCount)
    {
        DataRateHz = dataRateHz;
        DurationSeconds = durationSeconds;
        SubscriberCount = subscriberCount;
    }

    public int DataRateHz { get; }
    public int DurationSeconds { get; }
    public int SubscriberCount { get; }
    public int DataMessageCount { get { return 0; } }
    public int ControlMessageCount { get { return 0; } }
    public int DataDeliveryCount { get { return 0; } }
    public int ControlDeliveryCount { get { return 0; } }
    public int ClientConnectionCount { get { return 0; } }
    public long EstimatedLatencyStorageBytes { get { return 0; } }
}
```

Run the focused test again. Expected: 1/1 pass.

- [x] **Step 3: 기본값, 사용자 입력과 overflow behavior Red를 추가한다**

```csharp
[Fact]
public void Constructor_WhenDefaultProfileIsUsed_CalculatesMessageAndDeliveryCounts()
{
    MixedWorkloadOptions options = new MixedWorkloadOptions();

    Assert.Equal(100, options.DataRateHz);
    Assert.Equal(100, MixedWorkloadOptions.ControlRateHz);
    Assert.Equal(30, options.DurationSeconds);
    Assert.Equal(1, options.SubscriberCount);
    Assert.Equal(3000, options.DataMessageCount);
    Assert.Equal(3000, options.ControlMessageCount);
    Assert.Equal(3000, options.DataDeliveryCount);
    Assert.Equal(3000, options.ControlDeliveryCount);
    Assert.Equal(4, options.ClientConnectionCount);
    Assert.Equal(72000L, options.EstimatedLatencyStorageBytes);
}

[Fact]
public void Constructor_WhenRateDurationAndFanoutAreProvided_CalculatesIndependentCounts()
{
    MixedWorkloadOptions options = new MixedWorkloadOptions(250, 4, 3);

    Assert.Equal(1000, options.DataMessageCount);
    Assert.Equal(400, options.ControlMessageCount);
    Assert.Equal(3000, options.DataDeliveryCount);
    Assert.Equal(1200, options.ControlDeliveryCount);
    Assert.Equal(8, options.ClientConnectionCount);
    Assert.Equal(41600L, options.EstimatedLatencyStorageBytes);
}

[Theory]
[InlineData(99, 1, 1)]
[InlineData(100, 0, 1)]
[InlineData(100, 1, 0)]
public void Constructor_WhenInputIsBelowMinimum_ThrowsArgumentOutOfRange(
    int dataRateHz,
    int durationSeconds,
    int subscriberCount)
{
    Assert.Throws<ArgumentOutOfRangeException>(delegate()
    {
        new MixedWorkloadOptions(dataRateHz, durationSeconds, subscriberCount);
    });
}

[Fact]
public void Constructor_WhenPlannedCountOverflows_ThrowsArgumentOutOfRange()
{
    Assert.Throws<ArgumentOutOfRangeException>(delegate()
    {
        new MixedWorkloadOptions(int.MaxValue, 2, 1);
    });
}

[Fact]
public void Constructor_WhenSubscriberCountExceedsHarnessLimit_ThrowsArgumentOutOfRange()
{
    Assert.Throws<ArgumentOutOfRangeException>(delegate()
    {
        new MixedWorkloadOptions(100, 1, 257);
    });
}

[Fact]
public void Constructor_WhenLatencyStorageExceedsHarnessLimit_ThrowsArgumentOutOfRange()
{
    Assert.Throws<ArgumentOutOfRangeException>(delegate()
    {
        new MixedWorkloadOptions(100, 1800, 47);
    });
}

[Fact]
public void Constructor_WhenThirtyMinuteSingleSubscriberSoakIsUsed_RemainsWithinHarnessLimit()
{
    MixedWorkloadOptions options = new MixedWorkloadOptions(100, 1800, 1);

    Assert.Equal(4320000L, options.EstimatedLatencyStorageBytes);
    Assert.True(options.EstimatedLatencyStorageBytes <= MixedWorkloadOptions.MaximumLatencyStorageBytes);
}
```

Expected: count/resource assertions and exception assertions fail against the shell, not compile.

- [x] **Step 4: validation과 checked count를 최소 Green으로 구현한다**

생성자는 아래 순서로 검증하고 모든 계획 수와 자원 추정값을 constructor에서 64비트로 한 번 계산해 readonly property에 저장한다. constructor는 배열과 socket을 만들지 않는다.

```csharp
if (dataRateHz < MinimumDataRateHz)
    throw new ArgumentOutOfRangeException(nameof(dataRateHz));
if (durationSeconds < 1)
    throw new ArgumentOutOfRangeException(nameof(durationSeconds));
if (subscriberCount < 1)
    throw new ArgumentOutOfRangeException(nameof(subscriberCount));
if (subscriberCount > MaximumSubscriberCount)
    throw new ArgumentOutOfRangeException(nameof(subscriberCount));

try
{
    long dataMessageCount = checked((long)dataRateHz * durationSeconds);
    long controlMessageCount = checked((long)ControlRateHz * durationSeconds);
    long dataDeliveryCount = checked(dataMessageCount * subscriberCount);
    long controlDeliveryCount = checked(controlMessageCount * subscriberCount);
    long scratchSampleCount = Math.Max(dataMessageCount, controlMessageCount);
    long latencyStorageBytes = checked(
        checked(dataDeliveryCount + controlDeliveryCount + scratchSampleCount) * sizeof(long));

    if (dataMessageCount > int.MaxValue
        || controlMessageCount > int.MaxValue
        || dataDeliveryCount > int.MaxValue
        || controlDeliveryCount > int.MaxValue)
        throw new ArgumentOutOfRangeException(nameof(durationSeconds));
    if (latencyStorageBytes > MaximumLatencyStorageBytes)
        throw new ArgumentOutOfRangeException(nameof(durationSeconds));

    DataMessageCount = (int)dataMessageCount;
    ControlMessageCount = (int)controlMessageCount;
    DataDeliveryCount = (int)dataDeliveryCount;
    ControlDeliveryCount = (int)controlDeliveryCount;
    ClientConnectionCount = checked(2 + (subscriberCount * 2));
    EstimatedLatencyStorageBytes = latencyStorageBytes;
}
catch (OverflowException)
{
    throw new ArgumentOutOfRangeException(
        nameof(durationSeconds),
        "mixed workload 계획 수 또는 자원 추정값이 지원 범위를 초과합니다.");
}
```

`EstimatedLatencyStorageBytes`는 모든 subscriber의 data/control 원본 `long[]`과 subscriber percentile 계산에 순차 재사용할 최대 stream 길이 scratch `long[]` 하나의 payload byte 합이다. 배열 object header는 subscriber 256명 상한으로 별도 제한한다. `ControlRateHz`는 static constant와 이름이 충돌하므로 instance property를 만들지 않는다.

- [x] **Step 5: focused와 benchmark 전체를 Green으로 확인하고 commit한다**

Run:

```powershell
dotnet test tests/Hps.Benchmarks.Tests/Hps.Benchmarks.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~MixedWorkloadOptionsTests" -v minimal
dotnet test tests/Hps.Benchmarks.Tests/Hps.Benchmarks.Tests.csproj -c Release --no-restore -p:NuGetAudit=false -v minimal
git diff --check
```

Commit after state-doc update:

```powershell
git add -- tests/Hps.Benchmarks/MixedWorkloadOptions.cs tests/Hps.Benchmarks.Tests/MixedWorkloadOptionsTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md docs/agent-state/changelog/2026-07.md
git diff --cached --check
git commit -m "feat(benchmark): define mixed workload options"
```

Review stop: options/math/resource preflight만 보고하고 Task 3을 시작하지 않는다.

---

## Task 3: Stream/global hard gate와 typed mixed report를 만든다

**Files:**

- Create: `tests/Hps.Benchmarks/MixedWorkloadStreamResult.cs`
- Create: `tests/Hps.Benchmarks/MixedWorkloadRunResult.cs`
- Create: `tests/Hps.Benchmarks/MixedWorkloadReportWriter.cs`
- Create: `tests/Hps.Benchmarks.Tests/MixedWorkloadResultTests.cs`
- Create: `tests/Hps.Benchmarks.Tests/MixedWorkloadReportWriterTests.cs`
- Modify: `tests/Hps.Benchmarks/BenchmarkRunIdentity.cs`
- Modify: `tests/Hps.Benchmarks.Tests/BenchmarkRunIdentityTests.cs`
- Modify: Task 종료 시 root/archive 상태 문서

**Interfaces:**

- Consumes: `MixedWorkloadOptions` 상수와 계획 수.
- Produces: `MixedWorkloadStreamResult`의 `DeliveryPassed`, `RatePassed`, `LatencyBudgetPassed`, `Passed`, `ActualRateHz`, `DeliveryFailedSubscriberCount`, `LatencyFailedSubscriberCount`, `WorstSubscriberP50LatencyMicroseconds`, `WorstSubscriberP99LatencyMicroseconds`, `WorstSubscriberP999LatencyMicroseconds`, `WorstSubscriberP99LatencyGrowthRatio`.
- Produces: `MixedWorkloadRunResult`의 `Scenario`, `DurationSeconds`, `SubscriberCount`, `ClientConnectionCount`, `EstimatedLatencyStorageBytes`, `Data`, `Control`, `DroppedPendingSendCount`, `TcpPendingSendQueueHighWatermark`, `EndPendingSendCount`, `FallbackPoolRentedAfterStop`, `TimeoutCount`, `Identity`, `Passed`, `Print(TextWriter)`.
- Produces: `MixedWorkloadReportWriter.Write(string path, MixedWorkloadRunResult result)`.
- Produces: `BenchmarkRunIdentity.CaptureForMixedTcpBackend(TcpLoopbackTransportBackend transportBackend)`.

- [x] **Step 1: result type 부재 assertion Red를 작성한다**

두 type을 reflection으로 찾고 다음 property를 고정한다.

```csharp
[Fact]
public void Contract_MixedWorkloadResultsExposeStreamAndGlobalGates()
{
    Assembly assembly = typeof(BenchmarkCommandParser).Assembly;
    Type? streamType = assembly.GetType("Hps.Benchmarks.MixedWorkloadStreamResult");
    Type? runType = assembly.GetType("Hps.Benchmarks.MixedWorkloadRunResult");

    Assert.NotNull(streamType);
    Assert.NotNull(runType);
    Assert.NotNull(streamType!.GetProperty("DeliveryPassed"));
    Assert.NotNull(streamType.GetProperty("RatePassed"));
    Assert.NotNull(streamType.GetProperty("LatencyBudgetPassed"));
    Assert.NotNull(streamType.GetProperty("DeliveryFailedSubscriberCount"));
    Assert.NotNull(streamType.GetProperty("LatencyFailedSubscriberCount"));
    Assert.NotNull(streamType.GetProperty("WorstSubscriberP99LatencyMicroseconds"));
    Assert.NotNull(streamType.GetProperty("WorstSubscriberP999LatencyMicroseconds"));
    Assert.NotNull(streamType.GetProperty("Passed"));
    Assert.NotNull(runType!.GetProperty("ClientConnectionCount"));
    Assert.NotNull(runType.GetProperty("EstimatedLatencyStorageBytes"));
    Assert.NotNull(runType.GetProperty("Passed"));
}
```

Expected: 최초 `Assert.NotNull(streamType)` assertion failure.

- [x] **Step 2: exact constructor와 property shape를 추가한다**

`MixedWorkloadStreamResult` constructor parameter는 다음 순서를 고정한다.

```csharp
public MixedWorkloadStreamResult(
    string name,
    string topic,
    int payloadBytes,
    int targetRateHz,
    int targetDurationSeconds,
    int plannedMessageCount,
    int sentMessageCount,
    int subscriberCount,
    int plannedDeliveryCount,
    int receivedDeliveryCount,
    int minimumReceivedPerSubscriber,
    int maximumReceivedPerSubscriber,
    int deliveryFailedSubscriberCount,
    int latencyFailedSubscriberCount,
    int sequenceErrorCount,
    int payloadErrorCount,
    double worstSubscriberP50LatencyMicroseconds,
    double worstSubscriberP99LatencyMicroseconds,
    double worstSubscriberP999LatencyMicroseconds,
    double worstSubscriberFirstHalfP99LatencyMicroseconds,
    double worstSubscriberSecondHalfP99LatencyMicroseconds,
    double worstSubscriberP99LatencyGrowthRatio,
    long publisherElapsedTicks)
```

`MixedWorkloadRunResult` constructor parameter는 다음 순서를 고정한다.

```csharp
public MixedWorkloadRunResult(
    string scenario,
    int durationSeconds,
    int subscriberCount,
    int clientConnectionCount,
    long estimatedLatencyStorageBytes,
    MixedWorkloadStreamResult data,
    MixedWorkloadStreamResult control,
    long droppedPendingSendCount,
    int tcpPendingSendQueueHighWatermark,
    int endPendingSendCount,
    int fallbackPoolRentedAfterStop,
    int timeoutCount,
    BenchmarkRunIdentity identity)
```

모든 입력 count는 음수를 거부하고 string/result/identity null을 거부한다. 첫 Green에서는 property 저장과 `Passed => false`만 구현해 shape test를 통과시킨다.

- [x] **Step 3: stream/global 판정 behavior Red를 추가한다**

test helper `CreatePassingStream`은 data 기준으로 100 planned/sent, subscriber 2명, 200 deliveries, min/max 100, delivery/latency failed subscriber 0, error 0, worst-subscriber p99 4,000us, p999 9,000us를 만든다. 첫 completion부터 마지막 completion까지 elapsed는 0.99초여서 actual rate는 100Hz다.

다음 test를 각각 둔다.

- passing stream은 `DeliveryPassed`, `RatePassed`, `LatencyBudgetPassed`, `Passed`가 모두 true다.
- sent가 planned보다 1 작으면 delivery false다.
- 한 subscriber의 count가 99라 `minimumReceivedPerSubscriber == 99`, `deliveryFailedSubscriberCount == 1`이면 delivery false다.
- sequence error 또는 payload error가 1이면 delivery false다.
- 100개 completion의 first-to-last elapsed가 1초면 99개 interval이므로 actual rate 99.0Hz와 rate pass다.
- 100개 completion의 first-to-last elapsed가 0.99초면 actual rate 100.0Hz다.
- actual rate가 target의 98.9%면 rate false다.
- worst-subscriber p99가 5,000.1us, p999가 10,000.1us 또는 `latencyFailedSubscriberCount == 1`이면 latency false다.
- data/control이 통과해도 drop, end pending, pool rented, timeout 중 하나가 1이면 run false다.
- 모든 global count가 0이면 run true다.

`ActualRateHz`는 다음처럼 부동소수점 나눗셈을 강제한다. test는 100개/0.99초가 100Hz이고 100개/1초가 99Hz임을 각각 단언한다.

```csharp
public double ActualRateHz
{
    get
    {
        if (SentMessageCount < 2 || PublisherElapsedTicks <= 0)
            return 0;

        return ((double)(SentMessageCount - 1) * Stopwatch.Frequency) / PublisherElapsedTicks;
    }
}
```

Expected: shell의 false 판정 때문에 passing assertions가 실패한다.

- [x] **Step 4: hard gate를 최소 Green으로 구현한다**

```csharp
public bool DeliveryPassed
{
    get
    {
        return SentMessageCount == PlannedMessageCount
            && ReceivedDeliveryCount == PlannedDeliveryCount
            && MinimumReceivedPerSubscriber == PlannedMessageCount
            && MaximumReceivedPerSubscriber == PlannedMessageCount
            && DeliveryFailedSubscriberCount == 0
            && SequenceErrorCount == 0
            && PayloadErrorCount == 0;
    }
}

public bool RatePassed
{
    get { return ActualRateHz >= TargetRateHz * MixedWorkloadOptions.MinimumRateRatio; }
}

public bool LatencyBudgetPassed
{
    get
    {
        return LatencyFailedSubscriberCount == 0
            && WorstSubscriberP99LatencyMicroseconds <= MixedWorkloadOptions.P99LatencyBudgetMicroseconds
            && WorstSubscriberP999LatencyMicroseconds <= MixedWorkloadOptions.P999LatencyBudgetMicroseconds;
    }
}

public bool Passed
{
    get { return DeliveryPassed && RatePassed && LatencyBudgetPassed; }
}
```

run-level `Passed`는 두 stream pass와 네 global zero 조건을 모두 `&&`로 결합한다. HWM과 latency growth는 report-only다.

- [x] **Step 5: writer 부재와 legacy 격리 assertion Red를 추가한다**

writer type은 reflection으로 먼저 실패시킨다. type shell 뒤에는 passing result를 임시 directory에 쓰고 다음 JSON을 단언한다.

```csharp
Assert.Equal("mixed-tcp-workload", root.GetProperty("report-kind").GetString());
Assert.Equal(2, root.GetProperty("schema-version").GetInt32());
Assert.Equal("mixed-load-open-loop", root.GetProperty("result-name").GetString());
Assert.True(root.GetProperty("passed").GetBoolean());
Assert.Equal(2, root.GetProperty("subscriber-count").GetInt32());
Assert.Equal(6, root.GetProperty("client-connection-count").GetInt32());
Assert.True(root.GetProperty("estimated-latency-storage-bytes").GetInt64() > 0);
Assert.Equal(0, root.GetProperty("dropped-pending-send-count").GetInt64());
Assert.Equal(0, root.GetProperty("end-pending-send-count").GetInt32());
Assert.Equal(2, root.GetProperty("streams").GetArrayLength());
Assert.Equal("data", root.GetProperty("streams")[0].GetProperty("name").GetString());
Assert.Equal(0, root.GetProperty("streams")[0].GetProperty("delivery-failed-subscriber-count").GetInt32());
Assert.Equal(0, root.GetProperty("streams")[0].GetProperty("latency-failed-subscriber-count").GetInt32());
Assert.Equal(9000.0, root.GetProperty("streams")[0].GetProperty("worst-subscriber-p999-latency-us").GetDouble());
```

같은 directory를 `BaselineReportReader.ReadDirectory`로 읽었을 때 count 0도 단언한다. `report-kind`가 mixed 문서 종류를 고정하고, version 1을 쓰면 legacy reader가 mixed shape의 누락 key에서 예외를 내므로 Red가 된다.

- [x] **Step 6: `Utf8JsonWriter`로 report kind와 schema-version 2를 Green으로 구현한다**

top-level key 순서는 다음으로 고정한다.

```text
report-kind, schema-version, result-name, passed, scenario,
benchmark-profile, runner-id, runner-kind, transport-backend,
os-description, os-architecture, process-architecture,
framework-description, processor-count,
duration-seconds, subscriber-count, client-connection-count,
estimated-latency-storage-bytes, max-frame-payload-bytes,
dropped-pending-send-count, tcp-pending-send-queue-high-watermark,
end-pending-send-count, fallback-pool-rented-after-stop, timeout-count,
streams
```

각 stream object는 constructor property 전체와 `actual-rate-hz`, 세 부분 gate, `passed`, `publisher-elapsed-ms`를 쓴다. latency JSON key는 `worst-subscriber-p50-latency-us`, `worst-subscriber-p99-latency-us`, `worst-subscriber-p999-latency-us`, `worst-subscriber-first-half-p99-latency-us`, `worst-subscriber-second-half-p99-latency-us`, `worst-subscriber-p99-latency-growth-ratio`로 고정한다. round 자릿수는 rate/latency 1자리, growth 2자리다.

- [x] **Step 7: backend별 mixed identity를 assertion Red와 Green으로 고정한다**

`BenchmarkRunIdentityTests`에서 `CaptureForMixedTcpBackend`를 reflection으로 먼저 찾아 부재 assertion Red를 확인한다. Green은 다음 profile 상수와 기존 backend name/environment capture를 사용한다.

```csharp
public const string MixedSaeaBenchmarkProfile = "tcp-mixed-load-saea-v1";
public const string MixedRioBenchmarkProfile = "tcp-mixed-load-rio-v1";
public const string MixedIoUringBenchmarkProfile = "tcp-mixed-load-iouring-v1";
```

SAEA/RIO/io_uring theory는 각각 mixed profile과 기존 `SaeaTransport`/`RioTransport`/`IoUringTransport` 이름을 단언한다. 이 method는 Task 4 runner가 별도 임시 identity 없이 바로 사용한다.

- [x] **Step 8: focused와 benchmark 전체를 Green으로 확인하고 commit한다**

Run:

```powershell
dotnet test tests/Hps.Benchmarks.Tests/Hps.Benchmarks.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~MixedWorkloadResultTests|FullyQualifiedName~MixedWorkloadReportWriterTests|FullyQualifiedName~CaptureForMixedTcpBackend" -v minimal
dotnet test tests/Hps.Benchmarks.Tests/Hps.Benchmarks.Tests.csproj -c Release --no-restore -p:NuGetAudit=false -v minimal
git diff --check
```

Commit after state-doc update:

```powershell
git add -- tests/Hps.Benchmarks/MixedWorkloadStreamResult.cs tests/Hps.Benchmarks/MixedWorkloadRunResult.cs tests/Hps.Benchmarks/MixedWorkloadReportWriter.cs tests/Hps.Benchmarks/BenchmarkRunIdentity.cs tests/Hps.Benchmarks.Tests/MixedWorkloadResultTests.cs tests/Hps.Benchmarks.Tests/MixedWorkloadReportWriterTests.cs tests/Hps.Benchmarks.Tests/BenchmarkRunIdentityTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md docs/agent-state/changelog/2026-07.md
git commit -m "feat(benchmark): add mixed workload result report"
```

Review stop: synthetic gate와 report 격리만 보고하고 runner를 시작하지 않는다.

---

## Task 4: 단일 논리 구독자 mixed TCP runner를 구현한다

**Files:**

- Create: `tests/Hps.Benchmarks/TcpMixedWorkloadScenarioRunner.cs`
- Create: `tests/Hps.Benchmarks.Tests/TcpMixedWorkloadScenarioRunnerTests.cs`
- Modify: Task 종료 시 root/archive 상태 문서

**Interfaces:**

- Consumes: `MixedWorkloadOptions`, 두 result type, `BenchmarkRunIdentity.CaptureForMixedTcpBackend`, `BrokerServer`, `ITransportDiagnostics`, `ITransportEndpointDiagnostics`.
- Produces: `public static Task<MixedWorkloadRunResult> RunAsync(MixedWorkloadOptions options, TcpLoopbackTransportBackend transportBackend = TcpLoopbackTransportBackend.Saea)`.
- Produces internal test seam: `CalculateSubscriberLatency(long[] latencyTicks, int count, long[] scratch)`와 `AggregateSubscriberLatencies(SubscriberLatencySummary[] summaries)`.
- Temporary boundary: 이 Task에서는 `SubscriberCount != 1`을 `NotSupportedException`으로 거부한다. CLI는 아직 노출하지 않는다.

- [ ] **Step 1: runner type/method 부재 assertion Red를 작성한다**

```csharp
[Fact]
public void Contract_TcpMixedWorkloadScenarioRunnerExposesRunAsync()
{
    Type? type = typeof(BenchmarkCommandParser).Assembly.GetType(
        "Hps.Benchmarks.TcpMixedWorkloadScenarioRunner");

    Assert.NotNull(type);
    MethodInfo? method = type!.GetMethod("RunAsync", BindingFlags.Static | BindingFlags.Public);
    Assert.NotNull(method);
    Assert.Equal(typeof(Task<MixedWorkloadRunResult>), method!.ReturnType);
}
```

Expected: type null assertion failure.

- [ ] **Step 2: runner shell과 SAEA 1초 integration Red를 만든다**

shell은 `RunAsync`에서 `Task.FromException<MixedWorkloadRunResult>(new NotImplementedException())`를 반환해 shape test를 Green으로 만든다. 이어 다음 integration test가 `NotImplementedException`으로 실패하는 것을 확인한다.

```csharp
[Fact]
public async Task RunAsync_WhenOneSubscriberUsesSaea_DeliversBothStreamsWithoutDropOrLeak()
{
    MixedWorkloadOptions options = new MixedWorkloadOptions(100, 1, 1);

    MixedWorkloadRunResult result = await TcpMixedWorkloadScenarioRunner.RunAsync(
        options,
        TcpLoopbackTransportBackend.Saea);

    Assert.Equal(100, result.Data.SentMessageCount);
    Assert.Equal(100, result.Data.ReceivedDeliveryCount);
    Assert.Equal(100, result.Control.SentMessageCount);
    Assert.Equal(100, result.Control.ReceivedDeliveryCount);
    Assert.Equal(0, result.Data.SequenceErrorCount);
    Assert.Equal(0, result.Control.SequenceErrorCount);
    Assert.Equal(0, result.Data.PayloadErrorCount);
    Assert.Equal(0, result.Control.PayloadErrorCount);
    Assert.Equal(0, result.Data.DeliveryFailedSubscriberCount);
    Assert.Equal(0, result.Control.DeliveryFailedSubscriberCount);
    Assert.Equal(0, result.Data.LatencyFailedSubscriberCount);
    Assert.Equal(0, result.Control.LatencyFailedSubscriberCount);
    Assert.Equal(0, result.DroppedPendingSendCount);
    Assert.Equal(0, result.EndPendingSendCount);
    Assert.Equal(0, result.FallbackPoolRentedAfterStop);
    Assert.Equal(0, result.TimeoutCount);
}
```

integration test는 scheduler noise 때문에 `result.Passed`나 latency budget을 단언하지 않는다. latency gate 계산은 Task 3의 deterministic test가 담당하고 실제 threshold는 Task 8 explicit run에서 판정한다.

- [ ] **Step 3: topology와 buffer ownership을 최소 Green으로 구현한다**

runner의 고정 wire 값은 다음이다.

```csharp
private const string DataTopic = "data";
private const string ControlTopic = "control";
private const byte DataMarker = 0x44;
private const byte ControlMarker = 0x43;
private const int TimestampOffset = 0;
private const int SequenceOffset = 8;
private const int MarkerOffset = 12;
private const int PatternOffset = 13;
private const int SetupTimeoutSeconds = 5;
private const int DrainTimeoutSeconds = 10;
```

setup 순서는 고정한다.

1. server fallback과 benchmark client I/O가 함께 쓰는 16,384B `PinnedBlockMemoryPool` 하나를 만든다.
2. 선택 backend transport와 `BrokerServer(transport, pool, 16384)`를 만든다.
3. TCP loopback server를 시작한다.
4. data subscriber, control subscriber, data publisher, control publisher socket을 각각 만든다.
5. subscriber buffer 두 개와 publisher frame buffer 두 개를 같은 pool에서 대여한다.
6. options가 사전 검증한 data/control latency 원본 배열과 최대 stream 계획 수 길이의 scratch 배열 하나를 만든다.
7. subscriber별 reusable buffer에 length-prefixed `SUBSCRIBE <topic>`을 직접 쓰고 전송한다.
8. `WaitForSubscriberCountAsync`로 data/control 각각 1명을 확인한다.
9. 두 receive task와 두 publisher task를 시작하고 공통 `TaskCompletionSource<long>` start tick을 release한다.
10. 네 task를 완료한 뒤 endpoint pending 합이 0이 될 때까지 최대 10초 drain한다.
11. transport diagnostics와 endpoint snapshot을 캡처한다.
12. socket을 닫고 server를 Stop한 뒤 client block을 정확히 한 번 Return한다.
13. pool의 `RentedCount == 0`을 확인하고 result를 만든다.

`CreateTransport`는 기존 TCP/UDP runner의 backend/capability 조건을 그대로 복제한다. 이번 Task에서 기존 runner 두 파일을 리팩터링하거나 공용 factory를 추가하지 않는다. 세 번째 사용이 생겼지만 backend 선택 code는 짧고 안정적이며, 기존 baseline 파일을 건드리는 것보다 D243 범위가 작다.

- [ ] **Step 4: per-message allocation 없는 frame send/receive를 구현한다**

private method signature를 다음으로 고정한다.

```csharp
private static int PreparePublisherFrame(
    byte[] frame,
    string topic,
    int payloadLength,
    byte marker);

private static void UpdatePublisherPayload(
    byte[] frame,
    int payloadOffset,
    int payloadLength,
    byte marker,
    int sequence,
    long timestamp);

private static async ValueTask SendAllAsync(
    Socket socket,
    byte[] buffer,
    int length,
    CancellationToken cancellationToken);

private static async ValueTask ReceiveExactAsync(
    Socket socket,
    byte[] buffer,
    int offset,
    int length,
    CancellationToken cancellationToken);
```

- `PreparePublisherFrame`은 첫 4B에 command payload 길이를 big-endian으로 쓰고 뒤에 `PUBLISH <topic> ` ASCII bytes를 destination block에 직접 쓴다.
- `UpdatePublisherPayload`는 timestamp, sequence, marker와 `(sequence + payloadIndex) & 0xFF` pattern을 payload 영역에 쓴다.
- `SendAllAsync`는 `Socket.SendAsync(ReadOnlyMemory<byte>, SocketFlags.None, token)`의 partial send를 loop한다.
- `ReceiveExactAsync`는 `Socket.ReceiveAsync(Memory<byte>, SocketFlags.None, token)`의 partial receive를 loop하며 0이면 connection closed failure로 처리한다.
- receive loop는 4B header를 같은 buffer 앞에 읽고 length 검증 후 payload를 같은 block 앞에서 다시 읽는다.
- `ArraySegment<byte>` task overload, per-frame timeout task, per-message byte array는 사용하지 않는다.

- [ ] **Step 5: 공통 시작과 독립 absolute pacing을 구현한다**

두 publisher는 같은 `startTickTask`를 기다린다. 각 publisher의 target tick은 다음 식을 사용한다.

```csharp
long targetTick = startTick + ((long)messageIndex * Stopwatch.Frequency / rateHz);
```

remaining time이 2ms 이상이면 `Task.Delay(remainingMilliseconds - 1, token)`, 그 이하면 `Task.Yield()`로 target까지 접근한다. publisher는 첫 `SendAllAsync` 완료 직후 tick과 마지막 `SendAllAsync` 완료 직후 tick을 기록한다. result는 두 completion 사이 `sent - 1`개 간격으로 actual rate를 계산한다. data와 control pacing state를 공유하지 않는다.

- [ ] **Step 6: subscriber 검증과 percentile 집계를 구현한다**

subscriber state는 계획 수 길이의 `long[] LatencyTicks`, `Received`, `SequenceErrors`, `PayloadErrors`, `TimedOut`만 가진다. runner는 두 stream에서 순차 재사용하는 최대 계획 수 길이의 `long[] percentileScratch` 하나를 별도로 가진다.

같은 파일의 `internal readonly struct SubscriberLatencySummary`는 `P50`, `P99`, `P999`, `FirstHalfP99`, `SecondHalfP99`, `P99GrowthRatio`, `LatencyFailedSubscriberCount`를 저장한다. `CalculateSubscriberLatency`는 subscriber 한 명의 summary를 만들고, `AggregateSubscriberLatencies`는 각 latency 값과 각 subscriber가 계산한 growth ratio의 최댓값, failed count 합을 가진 summary를 반환한다. summaries 입력이 비면 모든 값이 0인 summary를 반환한다.

- frame length가 stream payload 길이와 다르면 payload error를 증가시키고 안전하게 종료한다.
- embedded sequence가 현재 receive index와 다르면 sequence error를 증가시킨다.
- marker와 pattern이 다르면 payload error를 증가시킨다.
- timestamp가 0보다 크면 `Stopwatch.GetTimestamp() - embeddedTimestamp`를 저장한다.
- p50/p99/p999는 subscriber의 유효 latency를 `percentileScratch`에 복사해 sort한 뒤 `ceil(count * percentile) - 1` index를 사용한다.
- first/second-half p99도 sequence 기준 앞/뒤 절반을 같은 scratch에 순차 복사해 계산한다. 별도 aggregate/half 배열을 만들지 않는다.

Task 4에서는 subscriber state가 stream당 하나이므로 min/max는 해당 `Received`다. delivery failed count는 count/order/payload 중 하나라도 실패하면 1이고, latency failed count는 해당 subscriber의 p99 또는 p999가 예산을 넘으면 1이다. stream latency 값은 그 subscriber의 값이다.

- [ ] **Step 7: timeout과 cleanup 결과를 반환 가능한 failure로 만든다**

run 전체에 `duration + DrainTimeoutSeconds + SetupTimeoutSeconds` 단일 `CancellationTokenSource.CancelAfter`를 둔다. setup/drain/receive가 timeout이면 `TimeoutCount`를 증가시키고 partial count로 failed result를 만든다. 예상하지 못한 programming/configuration exception은 숨기지 않고 throw한다.

`finally`는 socket dispose, idempotent `server.StopAsync`, client buffer Return을 수행한다. 주입 pool leak가 남으면 result에 실제 count를 기록하며 임의 retry나 GC를 호출하지 않는다.

- [ ] **Step 8: focused와 benchmark 전체를 Green으로 확인하고 commit한다**

Run:

```powershell
dotnet test tests/Hps.Benchmarks.Tests/Hps.Benchmarks.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~TcpMixedWorkloadScenarioRunnerTests" -v minimal
dotnet test tests/Hps.Benchmarks.Tests/Hps.Benchmarks.Tests.csproj -c Release --no-restore -p:NuGetAudit=false -v minimal
git diff --check
```

Commit after state-doc update:

```powershell
git add -- tests/Hps.Benchmarks/TcpMixedWorkloadScenarioRunner.cs tests/Hps.Benchmarks.Tests/TcpMixedWorkloadScenarioRunnerTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md docs/agent-state/changelog/2026-07.md
git commit -m "feat(benchmark): run single subscriber mixed workload"
```

Review stop: SAEA 1초 exact delivery/drop/pending/leak 결과만 보고하고 fan-out을 시작하지 않는다.

---

## Task 5: 같은 runner를 N명 fan-out으로 확장한다

**Files:**

- Modify: `tests/Hps.Benchmarks/TcpMixedWorkloadScenarioRunner.cs`
- Modify: `tests/Hps.Benchmarks.Tests/TcpMixedWorkloadScenarioRunnerTests.cs`
- Modify: Task 종료 시 root/archive 상태 문서

**Interfaces:**

- Consumes: Task 4의 단일 subscriber runner.
- Produces: `SubscriberCount >= 1` 전체 지원. public/internal signature는 바꾸지 않는다.

- [ ] **Step 1: subscriber 2명 exact fan-out assertion Red를 추가한다**

```csharp
[Fact]
public async Task RunAsync_WhenTwoSubscribersUseSaea_DeliversEveryStreamToEverySubscriber()
{
    MixedWorkloadOptions options = new MixedWorkloadOptions(100, 1, 2);

    MixedWorkloadRunResult result = await TcpMixedWorkloadScenarioRunner.RunAsync(
        options,
        TcpLoopbackTransportBackend.Saea);

    Assert.Equal(200, result.Data.PlannedDeliveryCount);
    Assert.Equal(200, result.Data.ReceivedDeliveryCount);
    Assert.Equal(100, result.Data.MinimumReceivedPerSubscriber);
    Assert.Equal(100, result.Data.MaximumReceivedPerSubscriber);
    Assert.Equal(0, result.Data.DeliveryFailedSubscriberCount);
    Assert.Equal(0, result.Data.LatencyFailedSubscriberCount);
    Assert.Equal(200, result.Control.PlannedDeliveryCount);
    Assert.Equal(200, result.Control.ReceivedDeliveryCount);
    Assert.Equal(0, result.Control.DeliveryFailedSubscriberCount);
    Assert.Equal(0, result.Control.LatencyFailedSubscriberCount);
    Assert.Equal(0, result.DroppedPendingSendCount);
    Assert.Equal(0, result.EndPendingSendCount);
    Assert.Equal(0, result.FallbackPoolRentedAfterStop);
    Assert.Equal(0, result.TimeoutCount);
}
```

Expected: Task 4의 `NotSupportedException` 때문에 fail.

- [ ] **Step 2: fixed guard를 제거하고 subscriber collection을 만든다**

stream별 `Socket[]`, `byte[][]`, `SubscriberState[]`, `Task[]`를 `SubscriberCount` 길이로 한 번 만든다. 각 logical index에서 data/control socket과 buffer를 하나씩 생성한다. `WaitForSubscriberCountAsync(topic, SubscriberCount, 5초)`를 두 topic 모두에 호출한 뒤 receive task를 시작한다.

hot path에서 LINQ, closure capture, growable collection을 사용하지 않는다. task 생성 loop에서는 index를 local 변수로 복사해 각 state/socket을 정확히 연결한다.

- [ ] **Step 3: stream aggregate를 subscriber별 exact/worst-latency gate로 만든다**

- received delivery count는 모든 subscriber `Received` 합이다.
- min/max는 subscriber state의 실제 `Received` 최소/최대다.
- delivery failed subscriber count는 count 불일치, sequence error 또는 payload error가 있는 state 수다.
- 각 subscriber의 p50/p99/p999와 first/second-half p99를 scratch 배열 하나로 순차 계산한다.
- latency failed subscriber count는 p99 5,000us 또는 p999 10,000us를 넘긴 state 수다.
- sequence/payload error는 subscriber 전체 합이다.
- stream p50/p99/p999와 first/second-half p99는 각 subscriber 계산값의 최댓값이다. growth ratio도 subscriber별 ratio의 최댓값이며 서로 다른 subscriber의 half 최댓값으로 다시 나누지 않는다. 모든 subscriber sample을 합친 aggregate 배열은 만들지 않는다.

aggregate 합만 일치해도 한 subscriber가 누락되면 `MinimumReceivedPerSubscriber`와 `DeliveryFailedSubscriberCount`가 run을 실패시켜야 한다. 한 subscriber만 latency 예산을 위반해도 worst-subscriber p99/p999와 `LatencyFailedSubscriberCount`가 stream을 실패시켜야 한다.

deterministic aggregation Red는 정상 `SubscriberLatencySummary`와 p99 6,000us/failed count 1인 summary를 `AggregateSubscriberLatencies`에 넣었을 때 p99가 6,000us, latency failed subscriber count가 1이 되는지를 확인한다. 이 test는 실제 scheduler를 사용하지 않는다.

- [ ] **Step 4: focused와 benchmark 전체를 Green으로 확인하고 commit한다**

Run:

```powershell
dotnet test tests/Hps.Benchmarks.Tests/Hps.Benchmarks.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~TcpMixedWorkloadScenarioRunnerTests" -v minimal
dotnet test tests/Hps.Benchmarks.Tests/Hps.Benchmarks.Tests.csproj -c Release --no-restore -p:NuGetAudit=false -v minimal
git diff --check
```

Commit after state-doc update:

```powershell
git add -- tests/Hps.Benchmarks/TcpMixedWorkloadScenarioRunner.cs tests/Hps.Benchmarks.Tests/TcpMixedWorkloadScenarioRunnerTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md docs/agent-state/changelog/2026-07.md
git commit -m "feat(benchmark): add mixed workload fanout"
```

Review stop: subscriber 1/2 focused 결과를 보고하고 CLI 노출을 시작하지 않는다.

---

## Task 6: Mixed command, CLI validation과 Program을 연결한다

**Files:**

- Modify: `tests/Hps.Benchmarks/BenchmarkCommand.cs`
- Modify: `tests/Hps.Benchmarks/BenchmarkCommandLine.cs`
- Modify: `tests/Hps.Benchmarks/BenchmarkCommandParser.cs`
- Modify: `tests/Hps.Benchmarks/Program.cs`
- Modify: `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`
- Modify: `tests/Hps.Benchmarks.Tests/BenchmarkProgramProtocolTests.cs`
- Modify: Task 종료 시 root/archive 상태 문서

**Interfaces:**

- Consumes: fully tested runner/result/writer.
- Produces command: `BenchmarkCommand.MixedLoadOpenLoop`.
- Produces command-line properties: `MixedDataRateHz`, `MixedDurationSeconds`, `MixedSubscriberCount`.

- [ ] **Step 1: CLI contract assertion Red를 추가한다**

parser tests는 다음을 직접 고정한다.

```csharp
[Fact]
public void TryParse_WhenMixedLoadUsesAllOptions_ReturnsMixedCommandSettings()
{
    BenchmarkCommandLine commandLine;
    string? errorMessage;

    bool parsed = BenchmarkCommandParser.TryParse(
        new[]
        {
            "--mixed-load-open-loop",
            "--backend", "rio",
            "--data-rate-hz", "250",
            "--duration-seconds", "60",
            "--subscribers", "4",
            "--report", "out/mixed.json"
        },
        out commandLine,
        out errorMessage);

    Assert.True(parsed);
    Assert.Null(errorMessage);
    Assert.Equal("MixedLoadOpenLoop", commandLine.Command.ToString());
    Assert.Equal(TcpLoopbackTransportBackend.Rio, commandLine.TransportBackend);
    Assert.Equal(250, commandLine.MixedDataRateHz);
    Assert.Equal(60, commandLine.MixedDurationSeconds);
    Assert.Equal(4, commandLine.MixedSubscriberCount);
    Assert.Equal("out/mixed.json", commandLine.ReportPath);
}
```

추가 theory:

- 옵션 없는 command는 100Hz, 30초, 1명을 사용한다.
- `--data-rate-hz 99`, `--duration-seconds 0`, `--subscribers 0`은 각각 usage error다.
- `--subscribers 257`은 socket을 만들기 전에 usage error다.
- `--data-rate-hz 100 --duration-seconds 1800 --subscribers 47`은 128MiB latency 저장 상한을 넘어 usage error다.
- `--data-rate-hz 100 --duration-seconds 1800 --subscribers 1`은 soak profile로 허용된다.
- count multiplication overflow 입력은 usage error다.
- `--protocol tcp`와 `--protocol udp` 모두 mixed 전용 메시지로 거부한다.
- legacy `--load-open-loop --protocol udp`와 baseline parser test는 그대로 통과한다.

Expected: command/property 부재 때문에 reflection 또는 command string assertion Red. compile failure를 피하려면 최초 test는 신규 enum/property를 reflection으로 확인한 뒤 direct behavior test를 추가한다.

- [ ] **Step 2: command-line 모델과 mixed parser를 최소 Green으로 구현한다**

기존 `BenchmarkCommandLine` 두 constructor의 마지막 optional parameter로 다음을 추가한다.

```csharp
int mixedDataRateHz = MixedWorkloadOptions.DefaultDataRateHz,
int mixedDurationSeconds = MixedWorkloadOptions.DefaultDurationSeconds,
int mixedSubscriberCount = MixedWorkloadOptions.DefaultSubscriberCount
```

기존 call site는 optional default로 유지한다. mixed parser는 `--report`, `--backend`, `--data-rate-hz`, `--duration-seconds`, `--subscribers`만 두 칸씩 읽는다. `--protocol`은 값 존재 여부와 무관하게 `MessageMixedProtocolNotAllowed`로 거부한다.

정수 parse 후 `new MixedWorkloadOptions(dataRate, duration, subscribers)`를 호출해 minimum, subscriber 256명, checked count와 128MiB latency 저장 상한을 검증하고, `ArgumentOutOfRangeException`은 안정적인 usage message로 변환한다. 이 시점에는 runner, socket, latency 배열을 만들지 않는다.

`MessageReportOnlyWithRuns`에는 `--mixed-load-open-loop`도 실행 명령으로 포함되도록 문구를 갱신한다.

- [ ] **Step 3: Program과 help를 연결한다**

switch에 다음 case를 추가한다.

```csharp
case BenchmarkCommand.MixedLoadOpenLoop:
    return CompleteMixedRun(
        TcpMixedWorkloadScenarioRunner.RunAsync(
            new MixedWorkloadOptions(
                commandLine.MixedDataRateHz,
                commandLine.MixedDurationSeconds,
                commandLine.MixedSubscriberCount),
            commandLine.TransportBackend).GetAwaiter().GetResult(),
        commandLine.ReportPath);
```

`CompleteMixedRun`은 `result.Print`, optional `MixedWorkloadReportWriter.Write`, `result.Passed` 기반 exit 0/1을 사용하고 report write exception은 기존과 같은 exit 2다.

help line:

```text
Hps.Benchmarks --mixed-load-open-loop [--backend <saea|rio|iouring>] [--data-rate-hz <100+>] [--duration-seconds <1+>] [--subscribers <1..256>] [--report <path>]
```

Program test는 help에 이 line과 옵션이 있는지만 확인한다. unit suite에서 실제 1초 CLI run을 중복 실행하지 않는다. runner integration은 Task 4/5가 담당한다.

- [ ] **Step 4: focused, benchmark 전체와 legacy parser 회귀를 Green으로 확인한다**

Run:

```powershell
dotnet test tests/Hps.Benchmarks.Tests/Hps.Benchmarks.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~BenchmarkCommandParserTests|FullyQualifiedName~BenchmarkProgramProtocolTests" -v minimal
dotnet test tests/Hps.Benchmarks.Tests/Hps.Benchmarks.Tests.csproj -c Release --no-restore -p:NuGetAudit=false -v minimal
dotnet build tests/Hps.Benchmarks/Hps.Benchmarks.csproj -c Release --no-restore -p:NuGetAudit=false -v minimal
git diff --check
```

- [ ] **Step 5: 1초 CLI smoke로 report와 exit code를 확인하고 commit한다**

Run:

```powershell
$report = "artifacts/benchmarks/mixed/local-cli-smoke.json"
dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj -c Release --no-build --no-restore -- --mixed-load-open-loop --backend saea --duration-seconds 1 --subscribers 1 --report $report
$exit = $LASTEXITCODE
$json = Get-Content -Raw $report | ConvertFrom-Json
$json | Select-Object 'report-kind','schema-version','result-name',passed,'subscriber-count','client-connection-count','estimated-latency-storage-bytes','dropped-pending-send-count','end-pending-send-count','fallback-pool-rented-after-stop','timeout-count'
if ($exit -ne 0) { throw "mixed CLI smoke failed: $exit" }
```

Expected: exit 0, report-kind `mixed-tcp-workload`, schema-version 2, result-name `mixed-load-open-loop`, global zero counters, streams 2개. latency가 threshold를 넘으면 commit하지 않고 raw report를 보존해 Task 4 계측/runner를 재검토한다.

Commit after state-doc update:

```powershell
git add -- tests/Hps.Benchmarks/BenchmarkCommand.cs tests/Hps.Benchmarks/BenchmarkCommandLine.cs tests/Hps.Benchmarks/BenchmarkCommandParser.cs tests/Hps.Benchmarks/Program.cs tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs tests/Hps.Benchmarks.Tests/BenchmarkProgramProtocolTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs/agent-state/changelog/2026-07.md docs/agent-state/decisions/2026-07.md
git commit -m "feat(benchmark): expose mixed workload command"
```

Review stop: CLI/report smoke까지 보고하고 workflow를 시작하지 않는다.

---

## Task 7: Linux io_uring workflow에 독립 mixed artifact를 추가한다

**Files:**

- Modify: `.github/workflows/iouring-benchmark-artifacts.yml`
- Modify: `tests/Hps.Benchmarks.Tests/BenchmarkArtifactWorkflowTests.cs`
- Modify: Task 종료 시 root/archive 상태 문서

**Interfaces:**

- Consumes: `--mixed-load-open-loop --backend iouring`.
- Produces artifact path: `mixed/<yyyy-mm-dd>/session-01/mixed-01.json`부터 `mixed-03.json`.
- Produces environment: `BENCH_MIXED_ROOT`, `BENCH_MIXED_DATE_ROOT`, `BENCH_MIXED_SESSION_DIR`.

- [ ] **Step 1: workflow contract assertion Red를 추가한다**

```csharp
[Fact]
public void IoUringBenchmarkWorkflow_WhenMixedGateRuns_WritesThreeIndependentMixedReports()
{
    string workflow = ReadIoUringBenchmarkArtifactWorkflow();

    Assert.Contains("mixed_root=\"${runner_root}/mixed\"", workflow);
    Assert.Contains("mixed_session_dir=\"${mixed_date_root}/session-01\"", workflow);
    Assert.Contains("BENCH_MIXED_SESSION_DIR=$mixed_session_dir", workflow);
    Assert.Contains("--mixed-load-open-loop --backend iouring", workflow);
    Assert.Contains("--duration-seconds 30 --subscribers 1", workflow);
    Assert.Contains("mixed-${run}.json", workflow);
    Assert.Contains("IOURING_MIXED_EXIT", workflow);
}
```

Expected: 첫 path assertion failure.

- [ ] **Step 2: artifact path와 3회 hard gate step을 Green으로 추가한다**

prepare step에 `mixed_root`, `mixed_date_root`, `mixed_session_dir`을 추가하고 directory를 만든다.

run step은 `run in 01 02 03` loop를 사용한다. 각 command는 다음과 같다.

```bash
dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj --no-build --no-restore -- \
  --mixed-load-open-loop \
  --backend iouring \
  --data-rate-hz 100 \
  --duration-seconds 30 \
  --subscribers 1 \
  --report "$BENCH_MIXED_SESSION_DIR/mixed-${run}.json"
```

한 run이 실패해도 나머지 report를 수집하고 `IOURING_MIXED_EXIT=1`을 기록한다. final gate exit code 배열에 mixed exit를 포함한다. root summary에는 mixed profile, report count 3, mixed exit를 별도 표시한다.

기존 TCP/UDP baseline summary/history/envelope 입력 root에는 mixed directory를 넣지 않는다. summary 마지막 문구는 “legacy baseline latency는 report-only이고 mixed latency는 command hard gate”로 구분한다.

- [ ] **Step 3: focused와 benchmark 전체를 Green으로 확인하고 commit한다**

Run:

```powershell
dotnet test tests/Hps.Benchmarks.Tests/Hps.Benchmarks.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~BenchmarkArtifactWorkflowTests" -v minimal
dotnet test tests/Hps.Benchmarks.Tests/Hps.Benchmarks.Tests.csproj -c Release --no-restore -p:NuGetAudit=false -v minimal
git diff --check
```

Commit after state-doc update:

```powershell
git add -- .github/workflows/iouring-benchmark-artifacts.yml tests/Hps.Benchmarks.Tests/BenchmarkArtifactWorkflowTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs/agent-state/changelog/2026-07.md docs/agent-state/decisions/2026-07.md
git commit -m "ci(benchmark): collect iouring mixed workload"
```

Review stop: workflow source 계약만 보고한다. push와 remote workflow 실행은 사용자 승인 뒤 별도 cycle이다.

---

## Task 8: 전체 회귀와 backend별 성능 수락 evidence를 수집한다

**Files:**

- Generate ignored artifacts under: `artifacts/benchmarks/mixed/`
- Modify: 실제 결과를 기록할 root/archive 상태 문서
- Do not modify: production source unless a raw failure artifact proves the defect and a new design/review unit is opened.

**Interfaces:**

- Consumes: fully exposed mixed command and workflow.
- Produces: Windows SAEA/RIO 3회, preferred backend 1,800초 soak, pushed SHA Linux io_uring 3회 raw evidence.

- [ ] **Step 1: solution 전체를 검증한다**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx -c Release --no-restore -p:NuGetAudit=false -v minimal
dotnet test HighPerformanceSocket.slnx -c Release --no-build --no-restore -p:NuGetAudit=false -v minimal
```

Expected: build 오류 0, 새 경고 0, solution tests 전체 green, discovered/executed count가 0보다 큼.

- [ ] **Step 2: Windows SAEA 기본 profile을 3회 실행한다**

```powershell
$root = "artifacts/benchmarks/mixed/saea-$(Get-Date -Format yyyyMMdd-HHmmss)"
New-Item -ItemType Directory -Force $root | Out-Null
1..3 | ForEach-Object {
    $path = Join-Path $root ("mixed-{0:D2}.json" -f $_)
    dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj -c Release --no-build --no-restore -- --mixed-load-open-loop --backend saea --data-rate-hz 100 --duration-seconds 30 --subscribers 1 --report $path
    if ($LASTEXITCODE -ne 0) { throw "SAEA mixed run $_ failed" }
}
```

각 JSON에서 두 stream `sent/planned`, `received/planned-delivery`, min/max, delivery/latency failed subscriber, sequence/payload error, `N - 1` interval actual rate, worst-subscriber p99/p999와 global drop/HWM/pending/pool/timeout을 기록한다. 세 run 모두 `passed=true`여야 한다.

- [ ] **Step 3: Windows RIO 기본 profile을 3회 실행한다**

RIO capability가 Available인 Windows host에서 Step 2의 backend만 `rio`로 바꾼다. unavailable이면 성공으로 간주하지 않고 환경 blocker로 상태 문서에 기록한다. 세 raw report 모두 hard pass여야 한다.

- [ ] **Step 4: 배포 우선 backend 1,800초 soak를 실행한다**

```powershell
dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj -c Release --no-build --no-restore -- --mixed-load-open-loop --backend saea --data-rate-hz 100 --duration-seconds 1800 --subscribers 1 --report artifacts/benchmarks/mixed/saea-soak-1800s.json
```

실제 배포 우선 backend가 RIO/io_uring으로 확정되면 backend 값만 해당 값으로 바꾼다. soak도 같은 hard gate를 사용한다.

- [ ] **Step 5: push된 동일 SHA에서 Linux io_uring workflow를 실행한다**

사용자가 push한 뒤 `.github/workflows/iouring-benchmark-artifacts.yml`을 `workflow_dispatch`로 실행한다. 다음을 직접 확인한다.

- workflow checkout SHA가 검토한 local commit SHA와 같다.
- `IOURING_MIXED_EXIT=0`이다.
- mixed raw report 3개가 존재하고 모두 report-kind `mixed-tcp-workload`, schema-version 2, passed true다.
- 기존 TCP/UDP baseline, summary, history, envelope exit도 회귀 없이 0이다.
- mixed report가 TCP/UDP baseline summary source count에 섞이지 않는다.

- [ ] **Step 6: actual 운영 fan-out/rate 입력이 있으면 별도 run으로 검증한다**

실제 최대 data rate와 논리 subscriber 수가 환경 변수로 제공되면 다음 command를 추가로 실행한다.

```powershell
if ([string]::IsNullOrWhiteSpace($env:HPS_OPERATIONAL_DATA_RATE_HZ) -or
    [string]::IsNullOrWhiteSpace($env:HPS_OPERATIONAL_SUBSCRIBERS)) {
    throw "HPS_OPERATIONAL_DATA_RATE_HZ와 HPS_OPERATIONAL_SUBSCRIBERS가 모두 필요합니다."
}

$operationalDataRateHz = [int]$env:HPS_OPERATIONAL_DATA_RATE_HZ
$operationalSubscriberCount = [int]$env:HPS_OPERATIONAL_SUBSCRIBERS
dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj -c Release --no-build --no-restore -- --mixed-load-open-loop --backend saea --data-rate-hz $operationalDataRateHz --duration-seconds 30 --subscribers $operationalSubscriberCount --report artifacts/benchmarks/mixed/operational-fanout.json
```

두 환경 변수가 제공되지 않으면 command를 실행하지 않고 기본 100Hz/N=1만 수락했다고 명시하며 더 높은 rate나 다중 subscriber production capacity를 주장하지 않는다.

- [ ] **Step 7: evidence 결과를 commit하고 review stop에서 멈춘다**

ignored raw artifact는 commit하지 않는다. 상태 문서에는 command, SHA, backend, run 수, 실제 핵심 수치와 artifact 위치를 기록한다.

```powershell
git add -- CURRENT_PLAN.md TODOS.md PLAN.md CHANGELOG_AGENT.md DECISIONS.md docs/agent-state/changelog/2026-07.md docs/agent-state/decisions/2026-07.md docs/superpowers/specs/2026-07-18-mixed-tcp-workload-gate-design.md docs/superpowers/plans/2026-07-18-mixed-tcp-workload-gate.md
git diff --cached --check
git commit -m "docs(benchmark): record mixed workload gate evidence"
```

Review stop: 운영 입력 미확정 항목과 remote/soak 미실행 항목을 완료로 쓰지 않는다.

---

## Failure Branches

- options Red가 compile failure면 신규 type을 test에서 직접 참조한 순서 문제다. reflection shape Red부터 복구한다.
- options가 허용한 입력에서 allocation/OOM이 발생하면 `EstimatedLatencyStorageBytes` 식이나 subscriber 256명 경계에 누락된 자원이 있는 것이다. 상한을 높이거나 OOM을 retry하지 말고 실제 할당을 식에 포함하는 assertion Red와 새 review 단위를 연다.
- mixed report를 같은 directory에서 legacy reader가 읽거나 예외를 내면 `report-kind: mixed-tcp-workload`와 `schema-version: 2` 경계를 먼저 확인한다. baseline reader에 mixed 특별 분기를 추가하지 않는다.
- 단일 subscriber runner가 timeout이면 timeout을 늘리기 전에 subscription readiness, publisher sent, subscriber receive, endpoint pending 순서로 raw state를 확인한다.
- sent가 계획과 맞고 received가 부족하며 drop이 증가하면 subscriber send queue/backpressure를 조사한다. queue capacity나 retry를 즉시 바꾸지 않는다.
- drop 0인데 end pending이 남으면 drain 또는 transport pump 진행을 조사한다. pending 0 gate를 제거하지 않는다.
- payload/sequence error가 있으면 shared publisher frame mutation, TCP frame length, topic별 socket/state 연결을 먼저 확인한다.
- 한 subscriber만 latency를 위반했는데 stream이 통과하면 aggregate percentile을 사용한 회귀다. subscriber별 summary 최댓값과 latency failed count assertion부터 확인한다.
- 1초 rate 경계에서 99% gate가 잘못 판정되면 `sent`가 아니라 `sent - 1` interval을 사용했는지 확인한다.
- correctness는 통과하지만 p99/p999만 실패하면 publisher pacing, process GC, OS scheduling, backend completion을 구분해 raw evidence를 남긴다. legacy latency 정책이나 mixed threshold를 자동 완화하지 않는다.
- SAEA는 통과하고 RIO/io_uring만 실패하면 Broker/Protocol을 바꾸지 않고 해당 backend 범위로 조사한다.
- fallback pool이 0이 아니면 GC를 강제하거나 counter를 숨기지 않는다. outstanding owner와 cleanup 순서를 찾는 별도 assertion Red를 만든다.
- integration test가 scheduler latency로 flaky하면 latency assertion을 추가하지 않는다. deterministic result unit test와 explicit performance command의 역할 분리를 유지한다.
- 구현 중 production project 변경 필요성이 생기면 현재 Task를 중단하고 raw failure, 영향 계층, 최소 수정과 새 TDD gate를 별도 설계/review 단위로 작성한다.

---

## Plan Self-Review

- Spec coverage: options, CLI, protocol 거부, checked count, subscriber 256명/latency 128MiB preflight, 두 stream topology, reusable pinned client buffer, shared start, independent pacing, `N - 1` interval rate, per-subscriber exact/worst-latency gate, drop/HWM/pending/leak/timeout, report kind/schema 격리, SAEA/RIO/io_uring/soak/fan-out evidence가 Task 2~8에 각각 연결되어 있다.
- Scope: production Broker/Protocol/Transport, UDP, legacy baseline aggregate, generic workload engine은 수정 대상에 없다.
- Type consistency: `MixedWorkloadOptions`, `MixedWorkloadStreamResult`, `MixedWorkloadRunResult`, writer와 runner signature는 앞 Task에서 정의된 이름을 뒤 Task가 그대로 사용한다.
- Reviewability: options, result/report, subscriber 1, fan-out, CLI, workflow, evidence를 서로 다른 commit/review stop으로 분리했다.
- Remaining operational inputs: 최대 data rate, 최대 logical subscriber 수, 제어 데이터 ACK/retry/durable 의미, 실제 network latency SLO는 D243 기본 구현 밖에 남는다.
