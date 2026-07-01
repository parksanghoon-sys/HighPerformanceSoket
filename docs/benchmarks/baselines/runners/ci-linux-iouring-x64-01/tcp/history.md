# Baseline History

- source root: `docs\benchmarks\baselines\runners\ci-linux-iouring-x64-01\tcp`
- session count: 1
- hard gate: PASS
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

| 날짜 | session | summary | human report | raw reports | hard passed | warnings | load p99 max us | open-loop p99 max us | send queue HWM max |
| --- | --- | --- | --- | ---: | --- | ---: | ---: | ---: | ---: |
| 2026-07-01 | session-01 | `2026-07-01/session-01/summary.json` | `2026-07-01/session-01/summary.md` | 6 | true | 6 | 4298.8 | 5588.6 | 1 |

## warning 이 있는 session

- `2026-07-01` `session-01`: 6
