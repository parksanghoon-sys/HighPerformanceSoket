# RIO UDP Repeat Stability Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** RIO UDP의 내부 receive window를 2에서 4로 검증해 4096B x 100 Hz load/open-loop 3회 delivery hard gate를 모두 통과시키고, 실패하면 변경을 수락하지 않은 채 원인 추적 단계로 되돌린다.

**Architecture:** 기존 `RioUdpReceiveSlot[]`, request-context completion mapping, 단일 receive loop와 close/drain owner를 그대로 사용한다. production 변경은 우선 `RioUdpEndpoint.ReceiveWindowSize` 상수 한 줄이며, depth 4 burst/close Red와 반복 benchmark가 generic owner의 재사용 가능성과 실제 효과를 함께 판정한다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, Windows Registered I/O(RIO), PowerShell, 기존 `Hps.Benchmarks` baseline CLI.

## Global Constraints

- TFM은 `net9.0`, 언어는 C# 8.0 문법만 사용한다.
- RIO가 실제로 available인 Windows runner에서만 이 계획을 실행한다. unavailable 상태에서 early-return한 test는 Red/Green 증거가 아니다.
- public API, 설정, 새 abstraction, 새 NuGet 의존성을 추가하지 않는다.
- handler dispatch는 계속 단일 receive loop에서 직렬 실행한다.
- receive payload registration reuse, IPv6, UDP reliability, default transport 승격은 범위 밖이다.
- raw benchmark artifact는 고유 임시 디렉터리에만 두고 repository baseline으로 채택하지 않는다.
- latency/HWM warning은 report-only다. delivery/drop/payload/pool hard gate와 섞지 않는다.
- 반복 gate 전에는 구현 커밋을 만들지 않는다. 성공 또는 실패 문서화까지 하나의 reviewable unit으로 끝낸다.
- 기존 `.claude/review` 미추적 파일은 읽기만 가능하며 stage/commit하지 않는다.
- push와 원격 io_uring gate는 수행하지 않는다.

---

## File Map

- Modify: `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`
  - depth 4 burst/close Red, 중복 pre-post test 제거, cleanup regression 유지.
- Modify: `src/Hps.Transport.Rio/RioUdpEndpoint.cs`
  - internal fixed receive window 2에서 4로 변경.
- Verify only: `src/Hps.Transport.Rio/RioTransport.cs`
  - slot 배열과 request-context mapping이 depth 상수를 generic하게 소비하는지 대조하며 근거 없이 수정하지 않는다.
- Modify: `docs/superpowers/specs/2026-07-11-rio-udp-repeat-stability-hardening-design.md`
  - 구현 결과 또는 hypothesis rejection 결과 기록.
- Modify: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`
  - review stop과 D240 현재 결과 갱신.
- Modify: `docs/agent-state/changelog/2026-07.md`, `docs/agent-state/decisions/2026-07.md`
  - Red/Green/gate 상세 근거 보존.

## Current Baseline

- `RioUdpEndpoint.ReceiveWindowSize == 2`.
- `RioTransportUdpTests`: 18/18 통과.
- `Hps.Transport.Rio.Tests`: 57/57 통과.
- RIO UDP smoke: sent/received 8/8, drop/payload error/pool rented 0.
- 반복 RIO UDP open-loop: received 2996/2997/2999로 3/3 hard fail.
- 같은 환경의 SAEA UDP open-loop: received 3000/3000, hard pass.

---

### Task 1: Depth 4 TDD, 반복 gate, 결과 커밋

**Files:**
- Modify: `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs:332-535`
- Modify: `src/Hps.Transport.Rio/RioUdpEndpoint.cs:19-25`
- Verify only: `src/Hps.Transport.Rio/RioTransport.cs:389-512`
- Modify on final result: `docs/superpowers/specs/2026-07-11-rio-udp-repeat-stability-hardening-design.md`
- Modify on final result: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`
- Modify on final result: `docs/agent-state/changelog/2026-07.md`, `docs/agent-state/decisions/2026-07.md`

**Interfaces:**
- Consumes: `RioUdpEndpoint.ReceiveWindowSize`, `RioUdpReceiveSlot[]`, `BlockingFirstDatagramHandler`, `WaitForRentedCountAsync`, `WaitForReceivedCountAsync`.
- Produces: internal fixed receive depth 4와 반복 gate로 수락되거나 기각된 D240 evidence. public signature는 만들지 않는다.

- [ ] **Step 1: tracked 작업 트리와 RIO availability를 확인한다**

Run:

```powershell
git status --short --branch
$env:NUGET_PACKAGES = 'C:\Users\ADMIN\.nuget\packages'
dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -c Release --no-build --no-restore -- --smoke --protocol udp --backend rio
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter FullyQualifiedName~RioTransportUdpTests -v minimal
```

Expected:

- tracked 변경이 없어야 한다. 기존 `.claude/review/*.md` 미추적 파일은 그대로 허용한다.
- smoke는 `smoke-result: pass`, sent/received 8/8, drop/payload error/pool rented 0이다.
- focused baseline은 18/18 통과한다.
- RIO unavailable, smoke fail, tracked 변경 발견 중 하나라도 있으면 구현을 시작하지 않고 상태를 재조정한다.

- [ ] **Step 2: depth 4 burst Red test로 기존 depth 2 한계를 고정한다**

Replace `UdpReceive_WhenHandlerIsBlocked_PreservesTwoQueuedDatagramsWithBoundedWindow` with:

```csharp
// RIO UDP depth 4 window 는 첫 handler 가 막힌 동안 네 개의 추가 datagram 을 outstanding receive 로 받아야 한다.
// current handler-owned datagram 1개와 posted slot 4개가 동시에 존재하므로 pool peak는 5다.
[Fact]
public async Task UdpReceive_WhenHandlerIsBlocked_PreservesFourQueuedDatagramsWithBoundedWindow()
{
    if (!IsRioDatagramAvailable())
        return;

    using (RioTransport transport = new RioTransport())
    {
        BlockingFirstDatagramHandler datagramHandler = new BlockingFirstDatagramHandler();
        transport.SetDatagramHandler(datagramHandler);
        await transport.StartAsync();

        IUdpEndpoint? endpoint = null;
        Socket? sender = null;

        try
        {
            endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
            RioUdpEndpoint rioEndpoint = Assert.IsType<RioUdpEndpoint>(endpoint);
            IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(endpoint.LocalEndPoint);

            sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            int firstSent = await sender.SendToAsync(new ArraySegment<byte>(new byte[] { 101 }), SocketFlags.None, boundEndPoint);
            Assert.Equal(1, firstSent);

            await WaitForSignalAsync(datagramHandler.FirstReceivedTask);
            Assert.Equal(1, datagramHandler.ReceivedCount);

            int secondSent = await sender.SendToAsync(new ArraySegment<byte>(new byte[] { 102 }), SocketFlags.None, boundEndPoint);
            Assert.Equal(1, secondSent);
            int thirdSent = await sender.SendToAsync(new ArraySegment<byte>(new byte[] { 103 }), SocketFlags.None, boundEndPoint);
            Assert.Equal(1, thirdSent);
            int fourthSent = await sender.SendToAsync(new ArraySegment<byte>(new byte[] { 104 }), SocketFlags.None, boundEndPoint);
            Assert.Equal(1, fourthSent);
            int fifthSent = await sender.SendToAsync(new ArraySegment<byte>(new byte[] { 105 }), SocketFlags.None, boundEndPoint);
            Assert.Equal(1, fifthSent);

            await WaitForRentedCountAsync(rioEndpoint.ReceivePool, 5);
            Assert.Equal(1, datagramHandler.ReceivedCount);

            datagramHandler.AllowFirstDatagramToComplete();
            await WaitForReceivedCountAsync(datagramHandler, 5);

            Assert.Equal(5, datagramHandler.ReceivedCount);

            endpoint.Close();
            endpoint = null;
            await WaitForRentedCountAsync(rioEndpoint.ReceivePool, 0);
        }
        finally
        {
            datagramHandler.AllowFirstDatagramToComplete();
            sender?.Dispose();
            endpoint?.Close();
            await transport.StopAsync();
        }
    }
}
```

Do not remove `UdpReceive_WhenHandlerIsBlocked_PrePostsOneAdditionalReceive` yet. Red가 실제로 확인되기 전에는 test refactor를 섞지 않는다.

- [ ] **Step 3: close owner Red를 depth 4 peak로 강화한다**

Replace `UdpReceive_WhenEndpointClosesWithPrePostedReceive_ReleasesOutstandingReceive` with:

```csharp
// RIO UDP close-drain 테스트: handler current 1개와 posted slot 4개가 존재해도
// receive loop owner가 모든 registration과 pooled ref를 정리해야 한다.
[Fact]
public async Task UdpReceive_WhenEndpointClosesWithBoundedReceiveWindow_ReleasesFourOutstandingReceives()
{
    if (!IsRioDatagramAvailable())
        return;

    using (RioTransport transport = new RioTransport())
    {
        BlockingFirstDatagramHandler datagramHandler = new BlockingFirstDatagramHandler();
        transport.SetDatagramHandler(datagramHandler);
        await transport.StartAsync();

        IUdpEndpoint? endpoint = null;
        Socket? sender = null;

        try
        {
            endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
            RioUdpEndpoint rioEndpoint = Assert.IsType<RioUdpEndpoint>(endpoint);
            IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(endpoint.LocalEndPoint);

            sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            int sent = await sender.SendToAsync(new ArraySegment<byte>(new byte[] { 91 }), SocketFlags.None, boundEndPoint);
            Assert.Equal(1, sent);

            await WaitForSignalAsync(datagramHandler.FirstReceivedTask);
            await WaitForRentedCountAsync(rioEndpoint.ReceivePool, 5);

            endpoint.Close();
            endpoint = null;
            datagramHandler.AllowFirstDatagramToComplete();

            await WaitForRentedCountAsync(rioEndpoint.ReceivePool, 0);
        }
        finally
        {
            datagramHandler.AllowFirstDatagramToComplete();
            sender?.Dispose();
            endpoint?.Close();
            await transport.StopAsync();
        }
    }
}
```

- [ ] **Step 4: 두 Red가 올바른 assertion failure인지 확인한다**

Run:

```powershell
$env:NUGET_PACKAGES = 'C:\Users\ADMIN\.nuget\packages'
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter "FullyQualifiedName~UdpReceive_WhenHandlerIsBlocked_PreservesFourQueuedDatagramsWithBoundedWindow|FullyQualifiedName~UdpReceive_WhenEndpointClosesWithBoundedReceiveWindow_ReleasesFourOutstandingReceives" -v minimal
```

Expected:

- 2개 test 모두 실패한다.
- failure는 `WaitForRentedCountAsync`의 expected 5 / actual 3 계열 assertion failure여야 한다.
- compile error, RIO unavailable early return, unrelated failure는 유효한 Red가 아니다.

Run:

```powershell
git diff -- src\Hps.Transport.Rio\RioUdpEndpoint.cs
```

Expected: production diff가 비어 있어야 한다.

- [ ] **Step 5: 최소 Green으로 receive window 상수만 4로 바꾼다**

In `src/Hps.Transport.Rio/RioUdpEndpoint.cs`, replace:

```csharp
internal const int ReceiveWindowSize = 2;
```

with:

```csharp
internal const int ReceiveWindowSize = 4;
```

Do not modify `RioTransport.cs`. `CreateUdpReceiveSlots`와 `CreateRequestQueue`가 이미 같은 상수를 사용한다.

- [ ] **Step 6: 두 focused test가 Green인지 확인한다**

Run the same command as Step 4.

Expected: passed 2, failed 0, close 후 pool rented 0.

- [ ] **Step 7: 중복 test를 제거하고 depth 4 wording을 정리한다**

In `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`:

- Delete the complete `UdpReceive_WhenHandlerIsBlocked_PrePostsOneAdditionalReceive` method and its two-line comment.
- Keep only `UdpReceive_WhenHandlerIsBlocked_PreservesFourQueuedDatagramsWithBoundedWindow` as the blocked-window behavior contract.
- Change the comment above `UdpReceive_WhenHandlerThrowsWithPrePostedReceive_ReleasesOutstandingReceiveAndNotifiesOnce` to:

```csharp
// RIO UDP handler 예외 테스트: handler 호출 전에 receive slot들이 다시 post되므로,
// handler 예외로 endpoint close에 수렴할 때 모든 slot owner도 같은 receive-loop cleanup 경로에서 정리되어야 한다.
```

Do not rename or change the handler-exception test body.

- [ ] **Step 8: focused/full RIO 회귀를 확인한다**

Run:

```powershell
$env:NUGET_PACKAGES = 'C:\Users\ADMIN\.nuget\packages'
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj -c Release --no-restore -p:NuGetAudit=false --filter FullyQualifiedName~RioTransportUdpTests -v minimal
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj -c Release --no-restore -p:NuGetAudit=false -v minimal
```

Expected:

- `RioTransportUdpTests`: 17/17 통과. 기존 18개에서 중복 test 1개만 제거됐다.
- `Hps.Transport.Rio.Tests`: 56/56 통과. 기존 57개에서 같은 test 1개만 제거됐다.
- failure, skip, pool leak assertion이 없어야 한다.

- [ ] **Step 9: solution Release build/test를 확인한다**

Run:

```powershell
$env:NUGET_PACKAGES = 'C:\Users\ADMIN\.nuget\packages'
dotnet build HighPerformanceSocket.slnx -c Release --no-restore -p:NuGetAudit=false -v minimal
dotnet test HighPerformanceSocket.slnx -c Release --no-build --no-restore -p:NuGetAudit=false -v minimal
```

Expected: build warning 0/error 0, solution tests failed 0. 중복 test 제거로 기존 521개 기준선에서 520개가 예상된다.

- [ ] **Step 10: 고유 임시 경로에서 RIO UDP 3회 gate를 실행한다**

Run sequentially; 다른 benchmark나 test를 병렬 실행하지 않는다.

```powershell
$ErrorActionPreference = 'Stop'
$env:NUGET_PACKAGES = 'C:\Users\ADMIN\.nuget\packages'
$env:HPS_BENCHMARK_RUNNER_ID = 'local-win-x64-01'
$env:HPS_BENCHMARK_RUNNER_KIND = 'local'
$udpRoot = Join-Path $env:TEMP ('hps-rio-udp-depth4-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $udpRoot -Force | Out-Null
Write-Output "UDP_ARTIFACT_ROOT=$udpRoot"

dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -c Release --no-build --no-restore -- --baseline-suite $udpRoot --runs 3 --protocol udp --backend rio
$suiteExit = $LASTEXITCODE

dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -c Release --no-build --no-restore -- --summarize-baseline $udpRoot --summary (Join-Path $udpRoot 'summary.json') --summary-md (Join-Path $udpRoot 'summary.md')
$summaryExit = $LASTEXITCODE

Get-Content -Encoding utf8 (Join-Path $udpRoot 'summary.md')
Write-Output "SUITE_EXIT=$suiteExit"
Write-Output "SUMMARY_EXIT=$summaryExit"
```

Expected accepted result: source report count 6, hard failure count 0, suite/summary exit 0, load/open-loop raw report 각 3개.

- [ ] **Step 11: raw report hard gate를 구조적으로 검증한다**

Use the `$udpRoot` from Step 10:

```powershell
$reports = @(Get-ChildItem -LiteralPath $udpRoot -Filter '*.json' |
    Where-Object { $_.Name -notlike 'summary*' } |
    ForEach-Object { Get-Content -Raw -Encoding utf8 $_.FullName | ConvertFrom-Json })

if ($reports.Count -ne 6) { throw "expected 6 raw reports, actual $($reports.Count)" }

$load = @($reports | Where-Object { $_.'result-name' -eq 'load' })
$openLoop = @($reports | Where-Object { $_.'result-name' -eq 'open-loop' })
if ($load.Count -ne 3 -or $openLoop.Count -ne 3) { throw 'expected three load and three open-loop reports' }

$hardFailures = @($reports | Where-Object {
    -not $_.passed -or
    $_.sent -ne 3000 -or
    $_.received -ne 3000 -or
    $_.dropped -ne 0 -or
    $_.'payload-errors' -ne 0 -or
    $_.'pool-rented' -ne 0
})

$reports | Select-Object @{
        Name = 'kind'; Expression = { $_.'result-name' }
    }, @{
        Name = 'rate'; Expression = { $_.'actual-rate-hz' }
    }, @{
        Name = 'received'; Expression = { $_.received }
    }, @{
        Name = 'p99'; Expression = { $_.'p99-latency-us' }
    }, @{
        Name = 'hwm'; Expression = { $_.'udp-pending-send-queue-high-watermark' }
    } | Format-Table -AutoSize

if ($hardFailures.Count -ne 0) { throw "RIO UDP depth 4 hard failures: $($hardFailures.Count)" }
```

Expected: no exception, load/open-loop 모두 received 3000, hard failure count 0.

- [ ] **Step 12A: gate 성공 시 결과와 D240을 accepted로 갱신한다**

Execute only when Steps 10-11 both pass. Update documents with actual measured rate/p50/p99/HWM ranges; do not invent values.

- spec: status를 `Implemented - depth 4 repeated gate accepted on 2026-07-13`로 바꾸고 Red actual, Green counts, solution verification, six-run metrics를 추가한다.
- `DECISIONS.md`: D240 summary를 fixed depth 4 accepted after Red and 3-run delivery gate로 갱신한다.
- decision archive: `Implementation Result`에 load/open-loop 3/3 pass와 hard failure 0을 기록한다.
- `CURRENT_PLAN.md`, `TODOS.md`: depth 4 implementation review stop으로 전환한다.
- changelog root/archive: Red, minimal Green, test counts, build result, repeated gate metrics를 기록한다.

- [ ] **Step 12B: gate 실패 시 task-owned code/test를 복원하고 rejection을 기록한다**

Execute instead of 12A when suite, summary, or raw hard gate fails. Step 1 required a clean tracked tree, so restore only the task-owned paths:

```powershell
git restore --source=HEAD -- src/Hps.Transport.Rio/RioUdpEndpoint.cs tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs
git diff -- src/Hps.Transport.Rio/RioUdpEndpoint.cs tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs
```

Expected: code/test diff is empty; `.claude/review` remains untouched.

Then record actual metrics:

- spec status: `Rejected by repeated gate on 2026-07-13`.
- D240: fixed depth 4 hypothesis rejected; next unit is ingress/completion/broker/fan-out/subscriber diagnostics design.
- current plan/TODO/changelog/decision archive: no production fix accepted, actual failure and next diagnostics design only.
- Step 14 uses docs-only commit message `docs(rio): record depth 4 gate rejection`.

- [ ] **Step 13: final diff와 범위를 검증한다**

Run:

```powershell
git diff --check
git status --short
git diff --stat
rg -n "[T]BD|[T]ODO:|[P]LACEHOLDER|구현[ ]예정" docs\superpowers\specs\2026-07-11-rio-udp-repeat-stability-hardening-design.md CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs\agent-state\changelog\2026-07.md docs\agent-state\decisions\2026-07.md
```

Accepted scope: production/test 2개와 listed docs만 변경되고 `RioTransport.cs`, Broker, Protocol, benchmark code/schema/workflow diff는 없다.

Rejected scope: production/test diff는 비어 있고 measured rejection docs만 남는다.

- [ ] **Step 14: 하나의 결과 커밋을 만든다**

Accepted path:

```powershell
git add -- src/Hps.Transport.Rio/RioUdpEndpoint.cs tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs docs/superpowers/specs/2026-07-11-rio-udp-repeat-stability-hardening-design.md CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs/agent-state/changelog/2026-07.md docs/agent-state/decisions/2026-07.md
git diff --cached --check
git commit -m "fix(rio): harden udp receive window stability"
```

Rejected path:

```powershell
git add -- docs/superpowers/specs/2026-07-11-rio-udp-repeat-stability-hardening-design.md CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs/agent-state/changelog/2026-07.md docs/agent-state/decisions/2026-07.md
git diff --cached --check
git commit -m "docs(rio): record depth 4 gate rejection"
```

Do not stage `.claude/review`. Do not push.

- [ ] **Step 15: commit 이후 review stop을 확인한다**

Run:

```powershell
git show --stat --oneline --summary HEAD
git diff --check HEAD^ HEAD
git status --short --branch
```

Expected: accepted면 production/test 2개와 지정 docs만 포함되고, rejected면 docs만 포함된다. 기존 `.claude/review` 미추적 파일만 남으며 사용자 review 전에는 다음 구현이나 push를 시작하지 않는다.

---

## Self-Review Checklist

- [x] spec의 fixed depth 4, no-public-config, serial dispatch 요구가 반영됐다.
- [x] production code보다 두 assertion Red가 먼저다.
- [x] 중복 pre-post test는 Green 뒤에만 제거한다.
- [x] close/drain pool peak 5와 drain 0을 모두 검증한다.
- [x] generic slot owner가 깨지지 않으면 `RioTransport.cs`를 수정하지 않는다.
- [x] solution test와 반복 benchmark가 구현 수락 전에 실행된다.
- [x] gate 실패 시 depth 8로 확대하지 않고 task-owned code/test를 복원한다.
- [x] 성공/실패 양쪽 모두 실제 수치와 D240 상태를 문서화한다.
- [x] 단일 결과 commit, no push, review stop을 지킨다.
