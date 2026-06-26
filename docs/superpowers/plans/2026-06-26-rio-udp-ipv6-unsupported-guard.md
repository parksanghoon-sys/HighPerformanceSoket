# RIO UDP IPv6 Unsupported Guard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** RIO UDP v1의 IPv4-only 정책을 public boundary 에서 명확히 적용해 unsupported IPv6가 background send/receive loop 로 흘러가지 않게 한다.

**Architecture:** D121에 따라 full IPv6 구현은 하지 않는다. `RioTransport.BindUdpAsync(...)`는 IPv6 local endpoint 를 explicit `NotSupportedException`으로 거부하고, `RioTransport.TrySendTo(...)`는 IPv6 remote endpoint 를 enqueue 하지 않고 `false`로 반환한다. 기존 SAEA transport 와 base factory 는 변경하지 않는다.

**Tech Stack:** .NET 9, C# 8, xUnit, `Hps.Transport.Rio`, `Hps.Transport.Rio.Tests`.

## Global Constraints

- TFM은 `net9.0`, C# 문법은 LangVersion `8.0`만 사용한다.
- public API surface 는 추가하지 않는다.
- RIO UDP는 opt-in IPv4-only backend 로 유지한다.
- `TrySendTo` 실패 시 caller 가 추가 ref 를 반환하는 기존 ownership 계약을 유지한다.
- 모든 새 테스트는 무엇을 검증하는지 한국어 주석으로 남긴다.

---

## File Structure

- Modify `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`
  - IPv6 local bind explicit unsupported test.
  - IPv6 remote send synchronous reject/no enqueue test.
- Modify `src/Hps.Transport.Rio/RioTransport.cs`
  - RIO UDP endpoint address-family guard helper.
  - `BindUdpAsync(...)` local endpoint guard.
  - `TrySendTo(...)` remote endpoint guard.
- Modify root state docs after validation:
  - `CURRENT_PLAN.md`
  - `TODOS.md`
  - `CHANGELOG_AGENT.md`

---

### Task 1: RIO UDP IPv6 unsupported boundary guard

**Files:**
- Modify: `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`
- Modify: `src/Hps.Transport.Rio/RioTransport.cs`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Interfaces:**
- Consumes:
  - `RioTransport.BindUdpAsync(EndPoint, CancellationToken)`
  - `RioTransport.TrySendTo(IUdpEndpoint, EndPoint, TransportSendBuffer)`
  - `ITransportEndpointDiagnostics.GetEndpointSnapshots()`
- Produces:
  - RIO UDP local bind IPv6 explicit unsupported behavior.
  - RIO UDP remote send IPv6 synchronous `false` reject behavior.

- [ ] **Step 1: Write the failing bind guard test**

Add to `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`:

```csharp
        // D121 정책 테스트: RIO UDP v1은 IPv4-only opt-in backend 이다.
        // IPv6 bind 가 socket layer 의 모호한 오류로 떨어지면 default promotion gate 에서 원인을 구분하기 어렵다.
        [Fact]
        public async Task BindUdpAsync_WhenLocalEndpointIsIpv6_ThrowsExplicitNotSupported()
        {
            if (!IsRioDatagramAvailable())
                return;

            using (RioTransport transport = new RioTransport())
            {
                await transport.StartAsync();

                NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(
                    async delegate
                    {
                        await transport.BindUdpAsync(new IPEndPoint(IPAddress.IPv6Loopback, 0));
                    });

                Assert.Contains("IPv4", exception.Message);
                await transport.StopAsync();
            }
        }
```

- [ ] **Step 2: Write the failing remote send guard test**

Add to `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`:

```csharp
        // D121 send boundary 테스트: IPv6 remote 는 background send pump 로 enqueue 하지 않고 즉시 false 로 거부해야 한다.
        // false 는 caller 가 추가 ref 를 반환하는 기존 TrySendTo ownership 계약과 맞다.
        [Fact]
        public async Task TrySendTo_WhenRemoteEndpointIsIpv6_ReturnsFalseWithoutQueueing()
        {
            if (!IsRioDatagramAvailable())
                return;

            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            RefCountedBuffer buffer = pool.RentCounted();
            buffer.Span[0] = 7;
            buffer.SetLength(1);
            bool accepted = false;

            using (RioTransport transport = new RioTransport())
            {
                await transport.StartAsync();
                IUdpEndpoint? endpoint = null;

                try
                {
                    endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    buffer.AddRef();
                    TransportSendBuffer sendBuffer = new TransportSendBuffer(buffer, 0, buffer.Length);

                    accepted = transport.TrySendTo(endpoint, new IPEndPoint(IPAddress.IPv6Loopback, 9), sendBuffer);

                    Assert.False(accepted);
                    EndpointSnapshot snapshot = Assert.Single(((ITransportEndpointDiagnostics)transport).GetEndpointSnapshots());
                    Assert.Equal(0, snapshot.PendingSendCount);
                    Assert.Equal(0, snapshot.PendingSendQueueHighWatermark);
                    Assert.Equal(0, snapshot.DroppedPendingSendCount);
                }
                finally
                {
                    if (!accepted)
                        buffer.Release();

                    buffer.Release();
                    endpoint?.Close();
                    await transport.StopAsync();
                }
            }

            await WaitForRentedCountAsync(pool, 0);
        }
```

- [ ] **Step 3: Run Red tests**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~BindUdpAsync_WhenLocalEndpointIsIpv6_ThrowsExplicitNotSupported|FullyQualifiedName~TrySendTo_WhenRemoteEndpointIsIpv6_ReturnsFalseWithoutQueueing"
```

Expected when RIO datagram is available:

```text
Bind test fails because the thrown exception is not the explicit IPv4-only NotSupportedException.
Send test fails because TrySendTo returns true for IPv6 remote.
```

Expected when RIO datagram is unavailable:

```text
Tests return early and pass; implementation still compiles.
```

- [ ] **Step 4: Implement minimal guard helper**

Modify `src/Hps.Transport.Rio/RioTransport.cs`:

```csharp
        private static bool IsSupportedUdpEndPoint(EndPoint endPoint)
        {
            IPEndPoint? ipEndPoint = endPoint as IPEndPoint;
            return ipEndPoint != null && ipEndPoint.AddressFamily == AddressFamily.InterNetwork;
        }

        private static void ThrowIfUnsupportedUdpLocalEndPoint(EndPoint endPoint)
        {
            if (!IsSupportedUdpEndPoint(endPoint))
                throw new NotSupportedException("RIO UDP v1은 IPv4 IPEndPoint 만 지원합니다.");
        }
```

In `BindUdpAsync(...)`, after `SupportsDatagramOperations` check and before `CreateUdpSocket()`:

```csharp
            ThrowIfUnsupportedUdpLocalEndPoint(localEndPoint);
```

In `TrySendTo(...)`, before `udpEndpoint.IsClosed` check:

```csharp
            if (!IsSupportedUdpEndPoint(remoteEndPoint))
                return false;
```

- [ ] **Step 5: Run focused Green tests**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~BindUdpAsync_WhenLocalEndpointIsIpv6_ThrowsExplicitNotSupported|FullyQualifiedName~TrySendTo_WhenRemoteEndpointIsIpv6_ReturnsFalseWithoutQueueing"
```

Expected:

```text
Passed! - Failed: 0
```

- [ ] **Step 6: Run broader RIO verification**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
git diff --check
```

Expected:

```text
Hps.Transport.Rio.Tests passes.
Build warning 0, error 0.
Solution tests fail 0.
git diff --check exits 0.
```

- [ ] **Step 7: Update state docs**

Update:

- `CURRENT_PLAN.md`
  - D121 guard implementation completed.
  - Next execution point after guard.
- `TODOS.md`
  - Move guard task to Completed.
  - Re-evaluate remaining backlog.
- `CHANGELOG_AGENT.md`
  - Red/Green evidence and verification commands.

- [ ] **Step 8: Commit**

Run:

```powershell
git add src\Hps.Transport.Rio\RioTransport.cs tests\Hps.Transport.Rio.Tests\RioTransportUdpTests.cs CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "fix: guard rio udp ipv6 unsupported endpoints"
```

## Self-Review

- Spec coverage: D121의 bind guard, send guard, no full IPv6 implementation, no base factory change 가 Task 1에 모두 매핑된다.
- Placeholder scan: 남은 `TBD`/`TODO` placeholder 없음.
- Type consistency: 테스트와 구현 모두 `RioTransport`, `ITransportEndpointDiagnostics`, `EndpointSnapshot`, `WaitForRentedCountAsync` 기존 타입/헬퍼를 사용한다.
