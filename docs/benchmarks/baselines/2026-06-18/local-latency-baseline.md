# 2026-06-18 로컬 TCP loopback latency baseline

- 실행 시각: 2026-06-18 10:15~10:22 KST
- OS: Microsoft Windows 10.0.26200
- Architecture: X64
- .NET SDK: 10.0.203
- 빌드: `dotnet build HighPerformanceSocket.slnx --no-restore`
- 대상: `tests/Hps.Benchmarks` TCP loopback SAEA baseline
- 목적: D063에 따라 latency 값을 hard gate 로 고정하기 전, 같은 환경에서 반복 실행한 관측 범위를 남긴다.

## 해석 원칙

이 문서는 현재 개발 PC의 참고 baseline 이다. p50/p99 값은 OS scheduling, 백그라운드 부하, JIT/워밍업 상태에 민감하므로
아래 숫자를 CI hard threshold 로 사용하지 않는다.

현재 자동 pass/fail 의미는 기존과 같다.

- `sent == planned-message-count`
- `sent == received`
- `dropped == 0`
- `payload-errors == 0`
- `pool-rented == 0`

latency, actual rate, queue high-watermark 는 회귀 판단을 돕는 관측값으로만 본다.

## Closed-loop `--load`

| run | report | sent/received | dropped | TCP HWM | actual Hz | p50 us | p99 us | p99 growth |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | `load-01.json` | 3000/3000 | 0 | 1 | 99.8 | 245.2 | 894.3 | 0.95 |
| 2 | `load-02.json` | 3000/3000 | 0 | 1 | 99.9 | 235.7 | 879.7 | 1.05 |
| 3 | `load-03.json` | 3000/3000 | 0 | 1 | 99.9 | 221.6 | 924.1 | 1.09 |

관측 범위:

- p50: 221.6~245.2 us
- p99: 879.7~924.1 us
- TCP pending send queue HWM: 1
- drop/leak/payload error: 0

## Open-loop `--load-open-loop`

| run | report | sent/received | dropped | TCP HWM | actual Hz | p50 us | p99 us | p99 growth |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | `open-loop-01.json` | 3000/3000 | 0 | 2 | 99.9 | 260.4 | 915.9 | 1.00 |
| 2 | `open-loop-02.json` | 3000/3000 | 0 | 2 | 99.9 | 240.7 | 955.4 | 1.15 |
| 3 | `open-loop-03.json` | 3000/3000 | 0 | 2 | 99.9 | 262.0 | 1005.5 | 0.95 |

관측 범위:

- p50: 240.7~262.0 us
- p99: 915.9~1005.5 us
- TCP pending send queue HWM: 2
- drop/leak/payload error: 0

## Session 02 `--baseline-suite --runs 3`

- 실행 시각: 2026-06-18 13:42~13:44 KST
- 명령: `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build -- --baseline-suite docs\benchmarks\baselines\2026-06-18\session-02 --runs 3`
- 결과 디렉터리: `docs/benchmarks/baselines/2026-06-18/session-02/`
- suite 결과: pass

### Closed-loop

| run | report | sent/received | dropped | TCP HWM | actual Hz | p50 us | p99 us | p99 growth |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | `session-02/load-01.json` | 3000/3000 | 0 | 1 | 99.8 | 226.9 | 509.3 | 1.16 |
| 2 | `session-02/load-02.json` | 3000/3000 | 0 | 1 | 100.0 | 230.7 | 512.1 | 0.93 |
| 3 | `session-02/load-03.json` | 3000/3000 | 0 | 1 | 99.9 | 256.7 | 481.6 | 0.99 |

관측 범위:

- p50: 226.9~256.7 us
- p99: 481.6~512.1 us
- TCP pending send queue HWM: 1
- drop/leak/payload error: 0

### Open-loop

| run | report | sent/received | dropped | TCP HWM | actual Hz | p50 us | p99 us | p99 growth |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | `session-02/open-loop-01.json` | 3000/3000 | 0 | 2 | 100.0 | 229.0 | 597.3 | 0.96 |
| 2 | `session-02/open-loop-02.json` | 3000/3000 | 0 | 2 | 100.0 | 247.5 | 564.9 | 1.05 |
| 3 | `session-02/open-loop-03.json` | 3000/3000 | 0 | 3 | 100.0 | 274.3 | 643.3 | 0.65 |

관측 범위:

- p50: 229.0~274.3 us
- p99: 564.9~643.3 us
- TCP pending send queue HWM: 2~3
- drop/leak/payload error: 0

## Session 03 `--baseline-suite --runs 3`

- 실행 시각: 2026-06-18 13:53~13:55 KST
- 명령: `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-build -- --baseline-suite docs\benchmarks\baselines\2026-06-18\session-03 --runs 3`
- 결과 디렉터리: `docs/benchmarks/baselines/2026-06-18/session-03/`
- suite 결과: pass

### Closed-loop

| run | report | sent/received | dropped | TCP HWM | actual Hz | p50 us | p99 us | p99 growth |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | `session-03/load-01.json` | 3000/3000 | 0 | 1 | 99.8 | 223.9 | 489.9 | 0.95 |
| 2 | `session-03/load-02.json` | 3000/3000 | 0 | 1 | 99.9 | 240.0 | 473.6 | 0.95 |
| 3 | `session-03/load-03.json` | 3000/3000 | 0 | 1 | 99.9 | 243.5 | 471.0 | 1.09 |

관측 범위:

- p50: 223.9~243.5 us
- p99: 471.0~489.9 us
- TCP pending send queue HWM: 1
- drop/leak/payload error: 0

### Open-loop

| run | report | sent/received | dropped | TCP HWM | actual Hz | p50 us | p99 us | p99 growth |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | `session-03/open-loop-01.json` | 3000/3000 | 0 | 2 | 100.0 | 241.4 | 587.8 | 0.99 |
| 2 | `session-03/open-loop-02.json` | 3000/3000 | 0 | 3 | 100.0 | 262.1 | 502.6 | 0.80 |
| 3 | `session-03/open-loop-03.json` | 3000/3000 | 0 | 2 | 100.0 | 260.6 | 556.2 | 0.90 |

관측 범위:

- p50: 241.4~262.1 us
- p99: 502.6~587.8 us
- TCP pending send queue HWM: 2~3
- drop/leak/payload error: 0

## Summary artifacts

`--summarize-baseline <input-dir> --summary <output-json>` command 로 각 baseline directory 의 top-level raw JSON 6개를 요약한
`summary.json`을 함께 남겼다. summary reader 는 top-level `load-*.json`/`open-loop-*.json`만 읽고, 같은 directory 의
`summary.json`은 다시 run report 로 집계하지 않는다.

| scope | summary | source reports | hard passed | warnings | load runs | open-loop runs |
| --- | --- | ---: | --- | ---: | ---: | ---: |
| root | `summary.json` | 6 | true | 0 | 3 | 3 |
| session-02 | `session-02/summary.json` | 6 | true | 0 | 3 | 3 |
| session-03 | `session-03/summary.json` | 6 | true | 0 | 3 | 3 |

summary artifact 는 현재 hard gate 결과와 관측 통계를 자동 소비하기 위한 JSON 기준선이다. Markdown 표는 사람이 빠르게 보는
기록이고, 추후 CI나 report tooling 은 summary JSON을 우선 입력으로 사용한다.

## 결론

현재 로컬 기준선에서는 closed-loop 와 open-loop 모두 4096B x 100Hz x 30초 목표를 delivery/drop/leak 관점에서 통과했다.
open-loop 에서 TCP HWM 이 closed-loop 보다 높지만 2에 머물렀고, drop 은 발생하지 않았다.

Session 02와 Session 03에서도 delivery/drop/leak gate 는 모두 통과했다. p99 값은 최초 session 보다 낮게 관측됐지만,
이 차이는 같은 날짜/같은 장비에서도 session 간 편차가 의미 있게 존재함을 보여준다.
이제 D069에서 요구한 최소 3개 baseline session 은 확보됐으나, 곧바로 hard latency threshold 를 고정하지 않고
먼저 session 간 분산, closed/open-loop 차이, TCP HWM 범위를 정리해 soft warning 과 hard failure 경계를 별도 설계로 판단한다.

hard latency SLO 는 아직 정하지 않는다. 최소한 같은 장비에서 날짜를 달리한 반복 측정이나 CI 전용 baseline 이 쌓인 뒤,
절대 p99 threshold 대신 baseline 대비 상대 회귀율과 soft warning/hard failure 경계를 별도 결정으로 다루는 편이 안전하다.
