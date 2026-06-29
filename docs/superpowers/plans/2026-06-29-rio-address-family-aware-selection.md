# RIO Address-Family-Aware Selection Implementation Plan

> **For agentic workers:** 이 계획은 D122를 구현하는 한 흐름이다. 사용자 확인이 필요한 결정은 이미 설계에서 닫혔으므로,
> Red-Green-Refactor 를 지키며 코드와 상태 문서를 이어서 갱신한다.

## Goal

RIO backend 의 현재 IPv4-only 지원 범위를 TCP/UDP public boundary 와 sample broker host selection 에 일관되게 반영한다.
IPv6 listen 주소에서 `--transport auto`는 SAEA로 fallback 하고, explicit `--transport rio`는 조기 실패해야 한다.

## Architecture

`RioNative`는 지금 IPv4 registered socket 만 만든다. 따라서 RIO transport 는 TCP/UDP 모두 IPv4 `IPEndPoint`만 받는 opt-in backend 로 둔다.
host composition 은 `SampleTransportSelector`에서 requested mode, RIO capability status, listen address family 를 함께 보고 concrete transport 를 만든다.

## Global Constraints

- C# 8 문법만 사용한다.
- full IPv6 RIO 구현은 하지 않는다.
- base `TransportFactory.CreateDefault()`는 변경하지 않는다.
- `--transport saea` 기존 동작은 유지한다.
- 테스트 주석은 무엇을 검증하는지 한국어로 남긴다.

## Task 1: RIO TCP IPv6 endpoint guard

**Files**

- Modify: `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`
- Modify: `src/Hps.Transport.Rio/RioTransport.cs`

**Steps**

- [x] Red: `ListenTcpAsync_WhenLocalEndpointIsIpv6_ThrowsExplicitNotSupported` test 를 추가한다.
- [x] Red: `ConnectTcpAsync_WhenRemoteEndpointIsIpv6_ThrowsExplicitNotSupported` test 를 추가한다.
- [x] Green: TCP endpoint address-family helper 를 추가하고 listen/connect 시작부에서 검사한다.
- [x] Verify: focused TCP guard tests 와 `Hps.Transport.Rio.Tests`를 실행한다.

## Task 2: sample broker address-family-aware selector

**Files**

- Modify: `tests/Hps.Sample.BrokerServer.Tests/SampleTransportSelectorTests.cs`
- Modify: `samples/Hps.Sample.BrokerServer/SampleTransportSelector.cs`
- Modify: `samples/Hps.Sample.BrokerServer/Program.cs`

**Steps**

- [x] Red: IPv6 listen + `auto` + RIO available 이 SAEA fallback notice 를 반환하는 selector test 를 추가한다.
- [x] Red: IPv6 listen + explicit `rio` + RIO available 이 runtime failure 를 반환하는 selector test 를 추가한다.
- [x] Green: selector 에 listen `AddressFamily` 입력을 추가하고 Program 에서 parsed address family 를 전달한다.
- [x] Refactor: 기존 selector helper/test call site 를 새 signature 로 정리한다.
- [x] Verify: focused sample broker tests 를 실행한다.

## Task 3: state docs, verification, commit

**Files**

- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/decisions/2026-06.md`
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`

**Steps**

- [x] D122와 실행 결과를 root/archive docs 에 기록한다.
- [x] `dotnet build HighPerformanceSocket.slnx --no-restore`
- [x] `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`
- [x] `git diff --check`
- [ ] `.claude/review/*` untracked 문서는 stage 하지 않고 관련 파일만 커밋한다.
