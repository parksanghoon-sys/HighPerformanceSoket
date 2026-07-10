# Subscription Readiness Seam Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 네 cross-module `_subscriptions` reflection/polling을 단일 `BrokerServer.WaitForSubscriberCountAsync` 계약으로 교체하고 Benchmark의 불필요한 Broker 참조를 제거한다.

**Architecture:** `BrokerServer`가 기존 thread-safe `SubscriptionTable.CountSubscribers`를 cold-path에서 polling한다. wire protocol, TCP/UDP handler, publish hot path에는 변경을 넣지 않으며 Dashboard/Benchmark/Server count wait가 같은 public method를 사용한다.

**Tech Stack:** .NET 9, C# 8.0, xUnit 2.9.3, `Task`, `CancellationToken`, `Stopwatch`.

## Global Constraints

- TFM은 `net9.0`, 언어 버전은 C# 8.0이다.
- production code보다 컴파일되는 assertion Red가 먼저다.
- 새 public type, event, snapshot, project, NuGet dependency, `InternalsVisibleTo`를 추가하지 않는다.
- wire SUBSCRIBE ACK와 UDP reliability semantics를 변경하지 않는다.
- 한 구현 커밋으로 완료하고 기존 미추적 `.claude/review` 파일은 stage하지 않는다.

---

### Task 1: Readiness API와 모든 cross-module caller 이관

**Files:**
- Create: `docs/superpowers/plans/2026-07-10-subscription-readiness-seam.md`
- Modify: `src/Hps.Server/BrokerServer.cs`
- Modify: `tests/Hps.Server.Tests/BrokerServerTests.cs`
- Modify: `samples/Hps.Sample.Dashboard/Services/TcpSmokeTestService.cs`
- Modify: `samples/Hps.Sample.Dashboard/Services/UdpSmokeTestService.cs`
- Modify: `tests/Hps.Benchmarks/TcpLoopbackScenarioRunner.cs`
- Modify: `tests/Hps.Benchmarks/UdpLoopbackScenarioRunner.cs`
- Modify: `tests/Hps.Benchmarks/Hps.Benchmarks.csproj`
- Modify: root state files and 2026-07 changelog/decision archives

**Interfaces:**
- Consumes: `SubscriptionTable.CountSubscribers(string)`의 thread-safe aggregate count.
- Produces: `Task BrokerServer.WaitForSubscriberCountAsync(string topic, int minimumCount, TimeSpan timeout, CancellationToken cancellationToken = default)`.

- [x] **Step 1: public method shape Red를 작성한다**

`BrokerServerTests`에 public method가 아직 없음을 assertion failure로 확인하는 reflection test를 추가한다.

```csharp
// in-process readiness 소비자는 BrokerServer private field를 반사하지 않고 하나의 public wait 계약을 사용해야 한다.
[Fact]
public void BrokerServerContract_WhenInspected_ExposesSubscriberCountWaitApi()
{
    MethodInfo? method = typeof(BrokerServer).GetMethod(
        "WaitForSubscriberCountAsync",
        BindingFlags.Instance | BindingFlags.Public,
        null,
        new Type[] { typeof(string), typeof(int), typeof(TimeSpan), typeof(CancellationToken) },
        null);

    Assert.NotNull(method);
    Assert.Equal(typeof(Task), method!.ReturnType);
}
```

Run:

```powershell
dotnet test tests\Hps.Server.Tests\Hps.Server.Tests.csproj --filter "FullyQualifiedName~BrokerServerContract_WhenInspected_ExposesSubscriberCountWaitApi" -v minimal
```

Expected: `Assert.NotNull() Failure`.

- [x] **Step 2: shape만 만족하는 skeleton을 추가한다**

`BrokerServer` public endpoint properties 아래에 XML doc과 임시 skeleton을 추가한다.

```csharp
public Task WaitForSubscriberCountAsync(
    string topic,
    int minimumCount,
    TimeSpan timeout,
    CancellationToken cancellationToken = default)
{
    return Task.CompletedTask;
}
```

Step 1 test가 Green인지 확인한다.

- [x] **Step 3: timeout과 cancellation behavior Red를 작성한다**

```csharp
// subscriber가 목표 수에 도달하지 않으면 무한 대기하지 않고 지정 timeout으로 종료해야 한다.
[Fact]
public async Task WaitForSubscriberCountAsync_WhenMinimumIsNotReached_ThrowsTimeout()
{
    FakeTransport transport = new FakeTransport();
    PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
    using (BrokerServer server = new BrokerServer(transport, pool, 64))
    {
        await Assert.ThrowsAsync<TimeoutException>(
            delegate { return server.WaitForSubscriberCountAsync("alpha", 1, TimeSpan.FromMilliseconds(25)); });
    }
}

// host shutdown은 readiness polling을 즉시 취소할 수 있어야 timeout까지 불필요하게 기다리지 않는다.
[Fact]
public async Task WaitForSubscriberCountAsync_WhenCancellationIsRequested_ThrowsCancellation()
{
    FakeTransport transport = new FakeTransport();
    PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
    using (BrokerServer server = new BrokerServer(transport, pool, 64))
    using (CancellationTokenSource cancellation = new CancellationTokenSource())
    {
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            delegate
            {
                return server.WaitForSubscriberCountAsync(
                    "alpha",
                    1,
                    TimeSpan.FromSeconds(5),
                    cancellation.Token);
            });
    }
}

// 잘못된 minimum/timeout은 polling을 시작하지 않고 호출자 계약 오류로 즉시 드러나야 한다.
[Fact]
public async Task WaitForSubscriberCountAsync_WhenMinimumIsNegative_Throws()
{
    using (BrokerServer server = new BrokerServer(new FakeTransport(), new PinnedBlockMemoryPool(64), 64))
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            delegate { return server.WaitForSubscriberCountAsync("alpha", -1, TimeSpan.FromSeconds(5)); });
    }
}

[Fact]
public async Task WaitForSubscriberCountAsync_WhenTimeoutIsNotPositive_Throws()
{
    using (BrokerServer server = new BrokerServer(new FakeTransport(), new PinnedBlockMemoryPool(64), 64))
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            delegate { return server.WaitForSubscriberCountAsync("alpha", 1, TimeSpan.Zero); });
    }
}

// topic validation은 wrapper에서 완화하지 않고 기존 SubscriptionTable 계약과 같은 예외를 유지해야 한다.
[Fact]
public async Task WaitForSubscriberCountAsync_WhenTopicIsNull_Throws()
{
    using (BrokerServer server = new BrokerServer(new FakeTransport(), new PinnedBlockMemoryPool(64), 64))
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            delegate { return server.WaitForSubscriberCountAsync(null!, 1, TimeSpan.FromSeconds(5)); });
    }
}

[Fact]
public async Task WaitForSubscriberCountAsync_WhenTopicIsEmpty_Throws()
{
    using (BrokerServer server = new BrokerServer(new FakeTransport(), new PinnedBlockMemoryPool(64), 64))
    {
        await Assert.ThrowsAsync<ArgumentException>(
            delegate { return server.WaitForSubscriberCountAsync(string.Empty, 1, TimeSpan.FromSeconds(5)); });
    }
}
```

Run the behavior tests and confirm they fail because the skeleton completes successfully or skips validation.

- [x] **Step 4: 최소 polling implementation으로 Green을 만든다**

`BrokerServer.cs`에 `using System.Diagnostics;`와 10ms poll interval을 추가하고 skeleton을 다음 구현으로 교체한다.

```csharp
private const int SubscriberCountPollIntervalMilliseconds = 10;

public Task WaitForSubscriberCountAsync(
    string topic,
    int minimumCount,
    TimeSpan timeout,
    CancellationToken cancellationToken = default)
{
    if (minimumCount < 0)
        throw new ArgumentOutOfRangeException(nameof(minimumCount));
    if (timeout <= TimeSpan.Zero)
        throw new ArgumentOutOfRangeException(nameof(timeout));

    cancellationToken.ThrowIfCancellationRequested();
    if (_subscriptions.CountSubscribers(topic) >= minimumCount)
        return Task.CompletedTask;

    return WaitForSubscriberCountCoreAsync(topic, minimumCount, timeout, cancellationToken);
}

private async Task WaitForSubscriberCountCoreAsync(
    string topic,
    int minimumCount,
    TimeSpan timeout,
    CancellationToken cancellationToken)
{
    long startedAt = Stopwatch.GetTimestamp();
    TimeSpan pollInterval = TimeSpan.FromMilliseconds(SubscriberCountPollIntervalMilliseconds);

    while (true)
    {
        TimeSpan remaining = timeout - Stopwatch.GetElapsedTime(startedAt);
        if (remaining <= TimeSpan.Zero)
            break;

        TimeSpan delay = remaining < pollInterval ? remaining : pollInterval;
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

        if (Stopwatch.GetElapsedTime(startedAt) >= timeout)
            break;
        if (_subscriptions.CountSubscribers(topic) >= minimumCount)
            return;
    }

    throw new TimeoutException("Broker subscriber count가 제한 시간 안에 목표 값에 도달하지 않았다.");
}
```

Run the focused API tests and all `Hps.Server.Tests`.

독립 리뷰에서 마지막 poll이 deadline 뒤 count를 성공으로 수락하는 경계를 발견했다. 1ms timeout 뒤 5ms에
구독을 등록하는 회귀 Red를 추가하고 위와 같이 남은 시간만 지연한 뒤 deadline을 count보다 먼저 판정한다.
대기 중 cancellation과 음수 timeout test, public XML exception 계약도 함께 고정한다.

- [x] **Step 5: 네 cross-module caller를 이관한다**

각 caller의 기존 helper 호출을 다음 형태로 교체한다.

```csharp
await server.WaitForSubscriberCountAsync(
    Topic,
    1,
    TimeSpan.FromSeconds(ReceiveTimeoutSeconds)).ConfigureAwait(false);
```

Benchmark에서는 `Topic` 대신 `BenchmarkTargets.DefaultTopic`을 사용한다. 네 파일에서
`WaitForSubscriberCountAsync`/`ReadSubscriptionTable` private helper와 불필요한 `System.Reflection`,
`Hps.Broker` using을 제거한다. Server tests의 count wait call도 public API로 바꾸되 target identity용
`ReadSubscriptionTable`은 유지한다.

- [x] **Step 6: Benchmark dependency를 줄인다**

`tests/Hps.Benchmarks/Hps.Benchmarks.csproj`에서 다음 reference를 제거한다.

```xml
<ProjectReference Include="..\..\src\Hps.Broker\Hps.Broker.csproj" />
```

- [x] **Step 7: 구조와 focused regression을 검증한다**

```powershell
rg -n "_subscriptions|BindingFlags|SubscriptionTable" samples\Hps.Sample.Dashboard\Services\TcpSmokeTestService.cs samples\Hps.Sample.Dashboard\Services\UdpSmokeTestService.cs tests\Hps.Benchmarks\TcpLoopbackScenarioRunner.cs tests\Hps.Benchmarks\UdpLoopbackScenarioRunner.cs
dotnet test tests\Hps.Server.Tests\Hps.Server.Tests.csproj -v minimal
dotnet test tests\Hps.Sample.Dashboard.Tests\Hps.Sample.Dashboard.Tests.csproj -v minimal
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj -v minimal
```

Expected: `rg` match 0, focused projects failure 0.

- [x] **Step 8: full verification과 상태 문서 갱신을 수행한다**

```powershell
dotnet build HighPerformanceSocket.slnx -v minimal
dotnet test HighPerformanceSocket.slnx --no-build -v minimal
git diff --check
```

`CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`와 2026-07 archives에
Red/Green, 제거된 reflection/dependency, test counts, 다음 review stop을 기록한다.

- [x] **Step 9: 단일 구현 커밋을 만든다**

현재 단위 파일만 stage하고 다음 메시지로 commit한다.

```powershell
git commit -m "refactor(server): centralize subscription readiness wait"
```
