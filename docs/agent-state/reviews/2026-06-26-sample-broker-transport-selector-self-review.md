# 2026-06-26 Sample Broker Transport Selector Self-Review

## Scope

- 검토 대상:
  - `samples/Hps.Sample.BrokerServer/`
  - `tests/Hps.Sample.BrokerServer.Tests/`
  - `docs/superpowers/specs/2026-06-26-host-composition-transport-selection-policy-design.md`
  - `docs/superpowers/plans/2026-06-26-sample-broker-transport-selector.md`
- 핵심 목적: D120에 따라 RIO preferred/default selection 을 base factory 가 아니라 sample host composition 경계에 둔 구현이 설계와 맞는지 확인한다.
- 범위 밖:
  - `TransportFactory.CreateDefault()` 기본 backend 변경
  - `Hps.Server` public options 확장
  - production-grade selector package 분리
  - IPv6 RIO UDP 지원 판단

## Findings

### Finding 1

- Severity: Minor
- Dimension: operability / maintainability
- Evidence:
  - 기존 `Program.cs`는 invalid port 와 invalid max-frame-bytes 에 대해 각각 구체적인 오류 메시지를 출력했다.
  - Task 1 구현 후 parser 는 해당 오류를 `false`만 반환하고 `errorMessage == null`로 남겼다.
- Impact:
  - sample host 사용자가 어떤 positional argument 가 잘못됐는지 바로 알기 어렵다.
  - Program validation 책임을 parser 로 옮긴 구조와도 맞지 않는다.
- Resolution:
  - `SampleBrokerServerCommandParser.MessagePortInvalid`,
    `MessageMaxFrameBytesInvalid`를 추가하고 parser 가 해당 오류 메시지를 반환하도록 보정했다.
  - parser tests 에 invalid port/max-frame 메시지 회귀 테스트를 추가했다.

### Finding 2

- Severity: Minor
- Dimension: correctness / maintainability
- Evidence:
  - `SampleTransportSelector.Select(...)`는 `SampleTransportMode`가 `Saea` 또는 `Rio`가 아니면 사실상 `Auto`처럼 처리했다.
  - public enum 은 외부 호출자가 cast 로 정의되지 않은 값을 넘길 수 있다.
- Impact:
  - 호출자 계약 위반이 RIO fallback 정책으로 조용히 흡수되어 debugging 이 어려워질 수 있다.
- Resolution:
  - selector entry 에 enum range guard 를 추가하고 undefined value 를 `ArgumentOutOfRangeException`으로 드러냈다.
  - selector tests 에 undefined enum 회귀 테스트를 추가했다.

## Coverage Check

- optional `--transport <saea|rio|auto>`: 구현 및 Program usage output 반영 완료.
- default `saea`: parser/model 및 selector tests 로 검증.
- explicit `rio`: available 시 RIO, unavailable 시 runtime failure `1`과 no fallback 검증.
- `auto`: available 시 RIO, unsupported/unavailable 시 SAEA fallback notice 검증.
- host composition boundary: sample project 만 `Hps.Transport.Rio`를 참조하고, `Hps.Server`/base `TransportFactory`는 변경하지 않았다.
- test quality: parser/selector/Program tests 가 sample host 의 user-visible CLI boundary 와 selection policy 를 분리해 검증한다.

## Verification

- Red:
  - invalid port/max-frame parser tests 2개가 `Assert.Equal()` failure 로 실패했다. Actual은 `null`.
  - undefined enum selector test 1개가 `Assert.Throws()` failure 로 실패했다.
- Green:
  - focused parser/selector tests 13개 통과.
  - sample broker server tests 15개 통과.
- Full verification:
  - `dotnet build HighPerformanceSocket.slnx --no-restore`: 경고 0개, 오류 0개.
  - `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`: 349개 통과.
  - `git diff --check`: 통과.

## Deferred Items

- production-grade shared selector package 는 host 가 더 늘거나 io_uring selection 요구가 생길 때 별도 설계로 승격한다.
- RIO UDP IPv6 지원 여부는 default promotion gate 전에 별도 설계 결정으로 다룬다.

## Completion Summary

- D120 구현은 설계와 정합한다.
- Blocker/Major finding 은 없다.
- Minor 2건은 이번 self-review 단위에서 TDD로 보정했다.
- 다음 실행 후보는 RIO UDP IPv6 지원 여부를 default promotion gate 전에 결정하는 설계 작업이다.
