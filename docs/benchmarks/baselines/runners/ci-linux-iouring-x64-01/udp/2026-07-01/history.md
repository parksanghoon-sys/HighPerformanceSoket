# Baseline History

- source root: `docs\benchmarks\baselines\runners\ci-linux-iouring-x64-01\udp\2026-07-01`
- session count: 4
- hard gate: PASS
- warning count: 8

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

| 날짜 | session | summary | human report | raw reports | hard passed | warnings | load p99 max us | open-loop p99 max us | TCP HWM max |
| --- | --- | --- | --- | ---: | --- | ---: | ---: | ---: | ---: |
| 2026-07-01 | session-01 | `session-01/summary.json` | `session-01/summary.md` | 6 | true | 3 | 1623.8 | 1322 | 0 |
| 2026-07-01 | session-02 | `session-02/summary.json` | `session-02/summary.md` | 6 | true | 2 | 2033.4 | 1312.4 | 0 |
| 2026-07-01 | session-03 | `session-03/summary.json` | `session-03/summary.md` | 6 | true | 3 | 1871.2 | 1270.3 | 0 |
| 2026-07-01 | session-04 | `session-04/summary.json` | `session-04/summary.md` | 6 | true | 0 | 1246.9 | 1271.3 | 0 |

## warning 이 있는 session

- `2026-07-01` `session-01`: 3
- `2026-07-01` `session-02`: 2
- `2026-07-01` `session-03`: 3
