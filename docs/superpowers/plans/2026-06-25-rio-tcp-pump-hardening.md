# RIO TCP Pump Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** RIO TCP pump 의 send completion 처리와 contract coverage 를 SAEA 기준선에 더 가깝게 보강한다.

**Architecture:** `RioTransport` 내부 send helper 를 byte-count 기반 remaining loop 로 바꾸고, RIO available live loopback tests 로 length-prefixed/larger payload 경로를 고정한다. close-drain full owner 재구조화는 이번 단위에서 확대하지 않고 self-review/deferred 기록으로 남긴다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, Windows RIO, `PinnedBlockMemoryPool`, `TransportSendBuffer`.

---

## File Structure

- Modify: `src/Hps.Transport.Rio/RioTransport.cs`
  - `SendRegisteredArrayAsync(...)`가 partial completion byte count 를 반복 처리한다.
- Modify: `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`
  - larger payload loopback 과 length-prefixed send loopback coverage 를 추가한다.
- Modify: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`
  - hardening 결과와 검증을 기록한다.
- Modify: `docs/superpowers/specs/2026-06-25-rio-tcp-pump-hardening-design.md`
  - 구현 결과가 설계와 달라질 때만 보정한다.

---

### Task 1: RIO send completion byte-count loop

**Files:**
- Modify: `src/Hps.Transport.Rio/RioTransport.cs`
- Modify: `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`
- Modify: root state docs

- [ ] **Step 1: Add contract coverage tests**

Add two RIO-available tests to `RioTransportTcpTests`:

```csharp
        // RIO send path 가 작은 smoke payload 뿐 아니라 receive block 크기에 가까운 payload 도 그대로 전달하는지 확인한다.
        // partial completion 을 강제하지는 못하지만, byte-count loop 보강 후 큰 payload 경로가 회귀하지 않도록 잡는다.
        [Fact]
        public async Task TcpLoopback_WhenRioAvailable_DeliversLargePayload()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
            {
                return;
            }

            byte[] payload = new byte[4096];
            for (int i = 0; i < payload.Length; i++)
                payload[i] = (byte)(i % 251);

            byte[] received = await SendAndReceiveAsync(payload, prependLengthPrefix: false);

            Assert.Equal(payload, received);
        }

        // Broker TCP outbound 는 D065에 따라 length prefix 를 붙여 보낸다.
        // RIO opt-in backend 도 같은 TransportSendBuffer metadata 를 해석해야 상위 Broker 경로를 나중에 재사용할 수 있다.
        [Fact]
        public async Task TcpLoopback_WhenRioAvailable_DeliversLengthPrefixedPayload()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
            {
                return;
            }

            byte[] received = await SendAndReceiveAsync(new byte[] { 5, 6, 7 }, prependLengthPrefix: true);

            Assert.Equal(new byte[] { 0, 0, 0, 3, 5, 6, 7 }, received);
        }
```

Extract the existing loopback body into `SendAndReceiveAsync(byte[] payload, bool prependLengthPrefix)`.

- [ ] **Step 2: Run tests and record result**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore
```

Expected: tests may pass with current implementation because provider likely completes sends fully. Record this as contract coverage, not as a Red for partial completion.

- [ ] **Step 3: Implement byte-count loop**

Change `SendRegisteredArrayAsync(...)` so it registers the array once and posts repeated `RIOSend` calls until all requested bytes are completed.

Rules:

- `remaining` starts at `length`.
- post segment is `new RioBufferSegment(bufferId, currentOffset, remaining)`.
- completion status nonzero closes via `SocketException`.
- transferred 0 closes via `SocketException`.
- transferred greater than remaining closes via `SocketException`.
- otherwise advance `currentOffset` and `remaining`.

- [ ] **Step 4: Verify**

Run:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore
for ($i = 0; $i -lt 10; $i++) { dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-build --no-restore; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build --no-restore
git diff --check
```

- [ ] **Step 5: Record and commit**

Update root state docs and commit:

```powershell
git add src/Hps.Transport.Rio/RioTransport.cs tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs docs/superpowers/specs/2026-06-25-rio-tcp-pump-hardening-design.md docs/superpowers/plans/2026-06-25-rio-tcp-pump-hardening.md docs/agent-state/reviews/2026-06-25-rio-task6-self-review.md CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md
git commit -m "fix: harden rio tcp send completion"
```

## Self-Review

- Spec coverage: send partial completion loop is implemented; close-drain full owner is explicitly deferred until stronger evidence.
- Placeholder scan: no TBD/TODO placeholders.
- Type consistency: plan uses existing `RioTransport`, `TransportSendBuffer`, `RioBufferSegment`, and `RioTransportTcpTests` names.
