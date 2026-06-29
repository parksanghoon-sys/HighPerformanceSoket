# Baseline Summary

- 입력 directory: `docs\benchmarks\baselines\runners\ci-windows-x64-01\2026-06-29\session-01`
- source report count: 6
- hard gate: PASS
- hard failure count: 0
- warning count: 0

## Comparison

- compatible: true
- unknown-runner-count: 0
- mismatch-count: 0
- benchmark-profile: tcp-loopback-saea-v1
- runner-id: ci-windows-x64-01
- runner-kind: ci
- transport-backend: SaeaTransport
- os-architecture: X64
- process-architecture: X64
- framework-description: .NET 9.0.17

| result | scenario | payload bytes | target rate hz | target duration seconds |
| --- | --- | ---: | ---: | ---: |
| load | tcp-loopback-saea-baseline | 4096 | 100 | 30 |
| open-loop | tcp-loopback-saea-baseline-open-loop | 4096 | 100 | 30 |

- mismatch: 없음

## 종류별 요약

| kind | runs | p50 median us | p99 median us | p99 max us | TCP HWM max | dropped total | pool rented max |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| load | 3 | 151.2 | 293.9 | 401 | 1 | 0 | 0 |
| open-loop | 3 | 160.5 | 279.2 | 520.7 | 2 | 0 | 0 |

## Warnings

- 없음
