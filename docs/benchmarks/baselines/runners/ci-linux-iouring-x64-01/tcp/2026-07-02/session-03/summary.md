# Baseline Summary

- 입력 directory: `docs\benchmarks\baselines\runners\ci-linux-iouring-x64-01\tcp\2026-07-02\session-03`
- source report count: 6
- hard gate: PASS
- hard failure count: 0
- warning count: 6

## Comparison

- compatible: true
- unknown-runner-count: 0
- mismatch-count: 0
- benchmark-profile: tcp-loopback-iouring-v1
- runner-id: ci-linux-iouring-x64-01
- runner-kind: ci
- transport-backend: IoUringTransport
- os-architecture: X64
- process-architecture: X64
- framework-description: .NET 9.0.17

| result | scenario | payload bytes | target rate hz | target duration seconds |
| --- | --- | ---: | ---: | ---: |
| load | tcp-loopback-iouring-baseline | 4096 | 100 | 30 |
| open-loop | tcp-loopback-iouring-baseline-open-loop | 4096 | 100 | 30 |

- mismatch: 없음

## 종류별 요약

| kind | runs | p50 median us | p99 median us | p99 max us | send queue HWM max | dropped total | pool rented max |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| load | 3 | 3046.9 | 4974.3 | 5061.1 | 1 | 0 | 0 |
| open-loop | 3 | 2453.6 | 4587 | 4589.9 | 1 | 0 | 0 |

## Warnings

| code | kind | metric | value | threshold | source |
| --- | --- | --- | ---: | ---: | --- |
| load-p99-latency-high | load | p99-latency-us | 4974.3 | 1386.2 | `load-01.json` |
| load-p99-latency-high | load | p99-latency-us | 4095.7 | 1386.2 | `load-02.json` |
| load-p99-latency-high | load | p99-latency-us | 5061.1 | 1386.2 | `load-03.json` |
| open-loop-p99-latency-high | open-loop | p99-latency-us | 3511.5 | 1508.3 | `open-loop-01.json` |
| open-loop-p99-latency-high | open-loop | p99-latency-us | 4587 | 1508.3 | `open-loop-02.json` |
| open-loop-p99-latency-high | open-loop | p99-latency-us | 4589.9 | 1508.3 | `open-loop-03.json` |
