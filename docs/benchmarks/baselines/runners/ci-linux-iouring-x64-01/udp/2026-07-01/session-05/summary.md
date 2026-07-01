# Baseline Summary

- 입력 directory: `docs\benchmarks\baselines\runners\ci-linux-iouring-x64-01\udp\2026-07-01\session-05`
- source report count: 6
- hard gate: PASS
- hard failure count: 0
- warning count: 2

## Comparison

- compatible: true
- unknown-runner-count: 0
- mismatch-count: 0
- benchmark-profile: udp-loopback-iouring-v1
- runner-id: ci-linux-iouring-x64-01
- runner-kind: ci
- transport-backend: IoUringTransport
- os-architecture: X64
- process-architecture: X64
- framework-description: .NET 9.0.17

| result | scenario | payload bytes | target rate hz | target duration seconds |
| --- | --- | ---: | ---: | ---: |
| load | udp-loopback-iouring-baseline | 4096 | 100 | 30 |
| open-loop | udp-loopback-iouring-baseline-open-loop | 4096 | 100 | 30 |

- mismatch: 없음

## 종류별 요약

| kind | runs | p50 median us | p99 median us | p99 max us | send queue HWM max | dropped total | pool rented max |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| load | 3 | 710.6 | 1495 | 1896.2 | 0 | 0 | 0 |
| open-loop | 3 | 1171.8 | 1275.6 | 1282.4 | 0 | 0 | 0 |

## Warnings

| code | kind | metric | value | threshold | source |
| --- | --- | --- | ---: | ---: | --- |
| load-p99-latency-high | load | p99-latency-us | 1896.2 | 1386.2 | `load-02.json` |
| load-p99-latency-high | load | p99-latency-us | 1495 | 1386.2 | `load-03.json` |
