# io_uring Benchmark Backend Selector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 기존 benchmark CLI 에 `--backend iouring`을 추가해 TCP/UDP loopback raw report 를 `IoUringTransport`로 생성할 수 있게 한다.

**Architecture:** 기존 SAEA/RIO backend selector 흐름을 확장한다. parser, identity, scenario transport factory, help text 만 수정하고 benchmark schema 는 바꾸지 않는다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, 기존 `Hps.Benchmarks` CLI, `Hps.Transport.IoUring`.

## Global Constraints

- TFM은 `net9.0`, LangVersion은 C# 8.0이다.
- 모든 문서와 주석은 한국어로 작성한다.
- 테스트에는 무엇을 검증하는지 한국어 주석을 남긴다.
- production 변경은 Red-Green-Refactor 순서를 따른다.
- `TransportFactory.CreateDefault()`는 변경하지 않는다.
- fixed registration, zero-copy, IPv6 direct io_uring UDP, latency hard gate 는 이번 범위에서 제외한다.

---

### Task 1: Parser And Identity Contract

**Files:**
- Modify: `tests/Hps.Benchmarks/TcpLoopbackTransportBackend.cs`
- Modify: `tests/Hps.Benchmarks/BenchmarkCommandParser.cs`
- Modify: `tests/Hps.Benchmarks/BenchmarkRunIdentity.cs`
- Modify: `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`
- Modify: `tests/Hps.Benchmarks.Tests/BenchmarkRunIdentityTests.cs`

**Interfaces:**
- Produces: `TcpLoopbackTransportBackend.IoUring`
- Produces: `BenchmarkRunIdentity.IoUringBenchmarkProfile`
- Produces: `BenchmarkRunIdentity.UdpIoUringBenchmarkProfile`
- Produces: `BenchmarkRunIdentity.IoUringTransportBackend`

- [ ] **Step 1: parser Red test**

Add a test that parses `--load --backend iouring --report out/iouring-load.json` and expects backend `IoUring`.

- [ ] **Step 2: identity Red test**

Add a test that invokes `BenchmarkRunIdentity.CaptureForBackendAndProtocol(IoUring, Udp)` and expects
`udp-loopback-iouring-v1` / `IoUringTransport`.

- [ ] **Step 3: run focused tests and confirm failure**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter "FullyQualifiedName~BenchmarkCommandParserTests|FullyQualifiedName~BenchmarkRunIdentityTests" -v minimal
```

Expected: assertion failure or enum parse failure for `IoUring`.

- [ ] **Step 4: implement parser and identity**

Add `IoUring` enum value, accept `iouring` in `TryParseTransportBackend`, update backend error/help strings, and add identity constants.

- [ ] **Step 5: run focused tests**

Run the same focused command. Expected: parser/identity tests pass.

### Task 2: Scenario Runner Wiring

**Files:**
- Modify: `tests/Hps.Benchmarks/Hps.Benchmarks.csproj`
- Modify: `tests/Hps.Benchmarks/TcpLoopbackScenarioRunner.cs`
- Modify: `tests/Hps.Benchmarks/UdpLoopbackScenarioRunner.cs`
- Modify: `tests/Hps.Benchmarks/Program.cs`
- Modify: `tests/Hps.Benchmarks.Tests/BenchmarkProgramProtocolTests.cs`
- Modify: `tests/Hps.Benchmarks.Tests/UdpLoopbackScenarioRunnerTests.cs`

**Interfaces:**
- Consumes: `TcpLoopbackTransportBackend.IoUring`
- Produces: TCP scenario base `tcp-loopback-iouring-baseline`
- Produces: UDP scenario base `udp-loopback-iouring-baseline`

- [ ] **Step 1: scenario Red tests**

Add tests that assert io_uring scenario names and help text contain `saea|rio|iouring`.
Do not require Linux native execution in local Windows tests.

- [ ] **Step 2: run focused tests and confirm failure**

Run:

```powershell
dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --filter "FullyQualifiedName~BenchmarkProgramProtocolTests|FullyQualifiedName~UdpLoopbackScenarioRunnerTests" -v minimal
```

Expected: missing project reference/type or scenario/help assertion failure.

- [ ] **Step 3: implement scenario wiring**

Add `Hps.Transport.IoUring` project reference, import `IoUringTransport`/`IoUringCapabilityProbe`, create io_uring transport only on Linux available hosts, and update scenario names/help text.

- [ ] **Step 4: run focused tests**

Run the same focused command. Expected: tests pass.

### Task 3: Verification And State Docs

**Files:**
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/changelog/2026-06.md`
- Modify: `docs/agent-state/decisions/2026-06.md`

**Interfaces:**
- Consumes: Task 1 and Task 2 completed implementation.
- Produces: D146 implementation state and next artifact review point.

- [ ] **Step 1: full verification**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore -v minimal
dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal
git diff --check
```

Expected: build warning 0/error 0, all tests pass, diff check clean.

- [ ] **Step 2: update state docs**

Record the new CLI/backend selector and the remaining Linux benchmark artifact follow-up.

- [ ] **Step 3: commit**

Stage only touched implementation/tests/docs and commit:

```powershell
git commit -m "feat: add iouring benchmark backend selector"
```
