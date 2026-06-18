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

## 결론

현재 로컬 기준선에서는 closed-loop 와 open-loop 모두 4096B x 100Hz x 30초 목표를 delivery/drop/leak 관점에서 통과했다.
open-loop 에서 TCP HWM 이 closed-loop 보다 높지만 2에 머물렀고, drop 은 발생하지 않았다.

hard latency SLO 는 아직 정하지 않는다. 최소한 같은 장비에서 날짜를 달리한 반복 측정이나 CI 전용 baseline 이 쌓인 뒤,
절대 p99 threshold 대신 baseline 대비 상대 회귀율과 soft warning/hard failure 경계를 별도 결정으로 다루는 편이 안전하다.
