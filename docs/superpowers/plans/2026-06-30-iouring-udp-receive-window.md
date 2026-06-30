# io_uring UDP Receive Window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `Hps.Transport.IoUring` UDP receive pump 를 one-deep 에서 bounded receive slot window 로 확장한다.

**Architecture:** endpoint 내부에 `IoUringUdpReceiveSlot[]`를 두고 slot 마다 receive context, message buffer, in-flight datagram 을 소유한다. receive loop 는 startup 에 모든 slot 을 post 하고, completion token 으로 slot 을 찾아 완료 처리한 뒤 endpoint 가 open 이면 handler dispatch 전에 같은 slot 을 repost 한다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, Linux `IORING_OP_RECVMSG`, `PinnedBlockMemoryPool`, `RefCountedBuffer`.

---

## Global Constraints

- public `ITransport`/`IUdpEndpoint` 계약은 바꾸지 않는다.
- io_uring UDP direct path 는 계속 IPv4 전용이다.
- receive window depth 는 internal constant 로 둔다.
- fixed registration, zero-copy send, default backend promotion 은 구현하지 않는다.
- 모든 새 테스트에는 무엇을 검증하는지 한국어 주석을 둔다.
- production 변경은 실패 테스트를 먼저 확인한 뒤 작성한다.

## File Structure

- Modify: `src/Hps.Transport.IoUring/IoUringUdpEndpoint.cs`
  - `ReceiveWindowSize`, receive slot 생성/정리 owner 를 추가한다.
- Modify: `src/Hps.Transport.IoUring/IoUringTransport.cs`
  - `UdpReceiveLoopAsync`를 slot window 기반으로 바꾼다.
  - receive slot completion task 를 `Task.WhenAny`로 기다리고 완료 slot 을 식별한다.
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringUdpEndpointShapeTests.cs`
  - window shape 와 slot context token 검증을 추가한다.
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringTransportUdpTests.cs`
  - Linux-gated blocked handler burst 검증을 추가한다.
- Modify state docs:
  - `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`,
    `docs/agent-state/changelog/2026-06.md`, `docs/agent-state/decisions/2026-06.md`.

---

### Task 1: Receive Slot Shape

**Files:**
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringUdpEndpointShapeTests.cs`
- Modify: `src/Hps.Transport.IoUring/IoUringUdpEndpoint.cs`

- [x] **Step 1: Write failing shape test**

Add `UdpEndpoint_WhenConstructed_CreatesBoundedReceiveWindowSlots`.

Expected assertions:
- `IoUringUdpEndpoint.ReceiveWindowSize` equals `4`.
- endpoint exposes internal `ReceiveSlots`.
- `ReceiveSlots.Length` equals `ReceiveWindowSize`.
- every slot has a distinct `Context.Token`.

- [x] **Step 2: Run focused test and verify Red**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~UdpEndpoint_WhenConstructed_CreatesBoundedReceiveWindowSlots -v minimal
```

Observed Red: failed with `Assert.NotNull() Failure: Value is null` because `ReceiveWindowSize`/`ReceiveSlots` did not exist.

- [x] **Step 3: Implement minimal endpoint slot shape**

Add:
- `internal const int ReceiveWindowSize = 4`
- `internal IoUringUdpReceiveSlot[] ReceiveSlots`
- slot-local `IoUringOperationContext`
- slot-local `IoUringUdpMessageBuffer`

Keep legacy `ReceiveContext` and `ReceiveMessage` mapped to the first slot for transitional compatibility with existing tests.

- [x] **Step 4: Run focused test and verify Green**

Observed Green: focused receive slot shape test passed.

---

### Task 2: Receive Loop Window

**Files:**
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringTransportUdpTests.cs`
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringUdpEndpointShapeTests.cs`
- Modify: `src/Hps.Transport.IoUring/IoUringUdpEndpoint.cs`
- Modify: `src/Hps.Transport.IoUring/IoUringTransport.cs`

- [x] **Step 1: Write failing local slot pump boundary test**

Add `UdpReceiveSlot_WhenInspected_ExposesPumpStateBoundary`.

Expected assertions:
- receive slot exposes `CompletionTask`
- receive slot exposes `Post`
- receive slot exposes `Complete`
- receive slot exposes `ReleaseInFlightDatagram`

- [x] **Step 2: Run focused test and verify Red**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~UdpReceiveSlot_WhenInspected_ExposesPumpStateBoundary -v minimal
```

Observed Red: failed with `Assert.NotNull() Failure: Value is null` because pump helpers did not exist.

- [x] **Step 3: Write Linux-gated behavior test**

Add `UdpReceive_WhenHandlerIsBlocked_PreservesWindowedDatagrams`.

Behavior:
- early return unless `IoUringCapabilityProbe.GetStatus() == Available`.
- handler blocks on first datagram.
- send first datagram and wait until first handler starts.
- send `ReceiveWindowSize` additional one-byte datagrams.
- wait until endpoint receive pool rented count reaches `ReceiveWindowSize + 1`.
- unblock handler.
- wait until handler received count is `ReceiveWindowSize + 1`.

Windows/local unavailable environments pass by early return. Linux available artifact must execute the native path.

- [x] **Step 4: Implement slot-based receive loop**

Change `UdpReceiveLoopAsync`:
- post all slots before the loop waits.
- wait for any slot completion with `Task.WhenAny`.
- find the completed slot by task identity.
- complete the slot and decode remote endpoint.
- if endpoint is open, repost the same slot before handler dispatch.
- dispatch completed datagram serially.
- release in-flight slot datagrams in receive-loop finally.

- [x] **Step 5: Run focused UDP tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringTransportUdpTests -v minimal
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringUdpEndpointShapeTests -v minimal
```

Observed Green:
- `IoUringTransportUdpTests` 6 tests passed.
- `IoUringUdpEndpointShapeTests` 8 tests passed.

---

### Task 3: State Docs And Verification

**Files:**
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/changelog/2026-06.md`
- Modify: `docs/agent-state/decisions/2026-06.md`

- [x] **Step 1: Add D143**

Record:

```markdown
D143 — io_uring UDP receive pump 는 bounded receive slot window 로 확장한다.
```

- [x] **Step 2: Update TODOs**

Move current reassessment item to Completed and add next Current TODO:

```markdown
- [ ] 사용자 push 이후 `iouring-linux-contract` artifact 로 io_uring UDP bounded receive window 를 검토한다.
```

- [x] **Step 3: Run verification**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx -v minimal
dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal
git diff --check
```

Observed:
- build warning 0 / error 0
- full tests 435 passed
- whitespace check passed

- [x] **Step 4: Commit**

Stage only the files in this plan plus the new spec/plan documents. Do not stage unrelated `.claude/review/*` files.

Suggested commit:

```powershell
git commit -m "feat: add iouring udp receive window"
```

## Self-Review

- Spec coverage: D142 이후 후보 재평가, selected receive window, excluded fixed registration/zero-copy/default promotion 을 모두 반영한다.
- Scope: one feature only, public API unchanged.
- TDD: shape Red, local slot pump boundary Red, Linux-gated behavior test, focused Green 순서로 진행했다.
- Risk: Windows에서는 native behavior Red를 볼 수 없으므로 local shape/cleanup과 remote artifact gate 를 함께 사용한다.
