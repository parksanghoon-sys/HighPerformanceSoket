# Baseline Summary

- 입력 directory: `docs\benchmarks\baselines\2026-06-18\session-03`
- source report count: 6
- hard gate: PASS
- hard failure count: 0
- warning count: 0

## Comparison

- compatible: false
- unknown-runner-count: 6
- mismatch-count: 6
- comparison-key: 없음

| code | field | expected | actual | source |
| --- | --- | --- | --- | --- |
| unknown-runner | runner-identity | known | unknown | `load-01.json` |
| unknown-runner | runner-identity | known | unknown | `load-02.json` |
| unknown-runner | runner-identity | known | unknown | `load-03.json` |
| unknown-runner | runner-identity | known | unknown | `open-loop-01.json` |
| unknown-runner | runner-identity | known | unknown | `open-loop-02.json` |
| unknown-runner | runner-identity | known | unknown | `open-loop-03.json` |

## 종류별 요약

| kind | runs | p50 median us | p99 median us | p99 max us | TCP HWM max | dropped total | pool rented max |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| load | 3 | 240 | 473.6 | 489.9 | 1 | 0 | 0 |
| open-loop | 3 | 260.6 | 556.2 | 587.8 | 3 | 0 | 0 |

## Warnings

- 없음
