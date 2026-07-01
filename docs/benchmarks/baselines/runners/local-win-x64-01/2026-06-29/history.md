# Baseline History

- source root: `docs\benchmarks\baselines\runners\local-win-x64-01\2026-06-29`
- session count: 3
- hard gate: PASS
- warning count: 0

## Comparison

- compatible: true
- unknown-runner-count: 0
- mismatch-count: 0
- benchmark-profile: tcp-loopback-saea-v1
- runner-id: local-win-x64-01
- runner-kind: local
- transport-backend: SaeaTransport
- os-architecture: X64
- process-architecture: X64
- framework-description: .NET 9.0.16

| result | scenario | payload bytes | target rate hz | target duration seconds |
| --- | --- | ---: | ---: | ---: |
| load | tcp-loopback-saea-baseline | 4096 | 100 | 30 |
| open-loop | tcp-loopback-saea-baseline-open-loop | 4096 | 100 | 30 |

- mismatch: 없음

| 날짜 | session | summary | human report | raw reports | hard passed | warnings | load p99 max us | open-loop p99 max us | send queue HWM max |
| --- | --- | --- | --- | ---: | --- | ---: | ---: | ---: | ---: |
| 2026-06-29 | session-01 | `session-01/summary.json` | `session-01/summary.md` | 6 | true | 0 | 844.6 | 948.8 | 2 |
| 2026-06-29 | session-02 | `session-02/summary.json` | `session-02/summary.md` | 6 | true | 0 | 856.7 | 878.3 | 2 |
| 2026-06-29 | session-03 | `session-03/summary.json` | `session-03/summary.md` | 6 | true | 0 | 884.6 | 978.9 | 2 |

## warning 이 있는 session

- 없음
