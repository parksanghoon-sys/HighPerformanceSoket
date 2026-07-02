# Baseline History

- source root: `docs\benchmarks\baselines\runners\ci-linux-iouring-x64-01\udp\2026-07-02`
- session count: 2
- hard gate: PASS
- warning count: 4

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

| 날짜 | session | summary | human report | raw reports | hard passed | warnings | load p99 max us | open-loop p99 max us | send queue HWM max |
| --- | --- | --- | --- | ---: | --- | ---: | ---: | ---: | ---: |
| 2026-07-02 | session-01 | `session-01/summary.json` | `session-01/summary.md` | 6 | true | 1 | 1597.6 | 1414.6 | 0 |
| 2026-07-02 | session-02 | `session-02/summary.json` | `session-02/summary.md` | 6 | true | 3 | 1953.5 | 1300.8 | 0 |

## warning 이 있는 session

- `2026-07-02` `session-01`: 1
- `2026-07-02` `session-02`: 3
