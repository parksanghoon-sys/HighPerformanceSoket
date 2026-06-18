# 반복 baseline 분포 기반 latency 정책 설계

- 날짜: 2026-06-18
- 상태: Accepted
- 관련 결정: D063, D069, D070
- 관련 artifact:
  - `docs/benchmarks/baselines/2026-06-18/local-latency-baseline.md`
  - `docs/benchmarks/baselines/2026-06-18/load-*.json`
  - `docs/benchmarks/baselines/2026-06-18/open-loop-*.json`
  - `docs/benchmarks/baselines/2026-06-18/session-02/*.json`
  - `docs/benchmarks/baselines/2026-06-18/session-03/*.json`

## 목적

D069에서 요구한 같은 장비 기준 최소 3개 baseline session 이 확보됐다. 이제 다음 작업은 baseline 을 더 쌓는 것이 아니라,
세 session 의 분포를 근거로 latency 값을 어떻게 자동화에 연결할지 정하는 것이다.

이번 설계의 목표는 다음 두 가지다.

- delivery/drop/leak hard gate 는 현재처럼 즉시 실패 조건으로 유지한다.
- p50/p99 latency 와 TCP high-watermark 는 곧바로 hard failure 로 올리지 않고, 먼저 summary artifact 와 soft warning 으로 관측한다.

## 확인된 데이터

세 session 은 모두 같은 개발 PC에서 같은 날짜에 실행됐다. 각 session 은 closed-loop `--load` 3회와 open-loop
`--load-open-loop` 3회를 포함한다.

| session | kind | p50 range us | p99 range us | TCP HWM |
| --- | --- | ---: | ---: | ---: |
| session-01 | load | 221.6~245.2 | 879.7~924.1 | 1 |
| session-01 | open-loop | 240.7~262.0 | 915.9~1005.5 | 2 |
| session-02 | load | 226.9~256.7 | 481.6~512.1 | 1 |
| session-02 | open-loop | 229.0~274.3 | 564.9~643.3 | 2~3 |
| session-03 | load | 223.9~243.5 | 471.0~489.9 | 1 |
| session-03 | open-loop | 241.4~262.1 | 502.6~587.8 | 2~3 |

전체 run 기준:

- closed-loop 9회: delivery/drop/leak 실패 0, p99 471.0~924.1us, TCP HWM 1.
- open-loop 9회: delivery/drop/leak 실패 0, p99 502.6~1005.5us, TCP HWM 2~3.
- actual rate 는 99.8~100.0Hz 범위로 목표 100Hz에 근접했다.
- p99 growth ratio 는 closed-loop 0.93~1.16, open-loop 0.65~1.15였다.

해석:

- 같은 날짜와 같은 장비에서도 session-01 p99가 session-02/03보다 높다.
- p50은 session 간 차이가 작지만 p99는 큰 편차를 보인다.
- 모든 run 이 delivery/drop/leak hard gate 를 통과했으므로 기능적 실패는 없다.
- TCP HWM 은 capacity 16에 멀리 못 미친다. open-loop 가 closed-loop 보다 높지만 3 이하로 유지됐다.

## 검토한 선택지

### 선택지 A: 지금 p99 hard threshold 를 도입

장점은 자동화가 명확하다는 점이다. 예를 들어 open-loop p99가 1005.5us를 넘으면 실패로 보는 식이다.

하지만 현재 데이터는 같은 장비 같은 날짜에서도 p99가 크게 달라졌다. session-01을 기준으로 hard threshold 를 잡으면
너무 느슨해질 수 있고, session-02/03을 기준으로 잡으면 false negative 위험이 크다. 따라서 지금 hard failure 로 승격하지 않는다.

### 선택지 B: latency 값을 계속 수동 문서로만 관리

가장 작지만 다음 작업자가 다시 JSON을 직접 읽어야 한다. 이미 `--baseline-suite`로 raw JSON을 자동 수집할 수 있으므로,
수동 문서만 유지하면 D069 이후의 판단 비용이 줄지 않는다.

### 선택지 C: summary artifact 와 soft warning 을 먼저 도입

권장 방향이다. 기존 per-run JSON을 보존하고, 별도 summary 단계가 전체 session 분포와 warning 후보를 산출한다.
hard gate 는 `TcpLoopbackRunResult.Passed`의 기존 조건으로 유지하고, latency/HWM 이상 징후는 warning 으로만 기록한다.

## 결정

v1에서는 p50/p99 latency hard failure 를 아직 추가하지 않는다.

다음 구현 단위는 CI workflow 나 hard threshold 가 아니라, baseline summary artifact 와 soft warning 산출이다.
이 구현은 기존 per-run JSON을 입력으로 읽고, 원본 JSON을 대체하지 않는다.

hard failure 는 계속 다음 조건으로만 판단한다.

- `sent == planned-message-count`
- `sent == received`
- `dropped == 0`
- `payload-errors == 0`
- `pool-rented == 0`

soft warning 은 exit code 를 실패로 바꾸지 않는다. warning 은 summary JSON과 콘솔 출력 또는 Markdown report 에 기록한다.

## Soft warning 초안

초기 warning 기준은 너무 공격적으로 잡지 않는다. 현재 로컬 baseline envelope 를 기준으로 다음 항목을 warning 후보로 본다.

| metric | baseline envelope | warning 후보 |
| --- | ---: | ---: |
| closed-loop p99 max | 924.1us | 1.5배 초과, 즉 1386.2us 초과 |
| open-loop p99 max | 1005.5us | 1.5배 초과, 즉 1508.3us 초과 |
| p99 growth ratio max | 1.16 | 2.0 초과 |
| closed-loop TCP HWM | 1 | 4 이상 |
| open-loop TCP HWM | 3 | 8 이상 |
| actual rate | 99.8~100.0Hz | 95Hz 미만 |

이 기준은 hard gate 가 아니다. 목적은 "바로 실패"가 아니라 review 대상 artifact 를 만드는 것이다.
특히 p99는 OS scheduling 과 JIT/워밍업 영향을 크게 받으므로, warning 이 발생해도 raw JSON과 session context 를 같이 확인한다.

drop 이 1 이상이면 soft warning 이 아니라 기존 hard gate 실패다. TCP HWM 이 16에 도달하면 drop 이 아직 0이어도 queue capacity 포화 신호이므로
warning 으로 기록한다.

## Summary artifact 정책

다음 구현은 per-run JSON을 입력으로 받아 summary 를 생성한다. summary 는 추가 artifact 이며, 기존 schema v1 report 를 바꾸지 않는다.

권장 산출물:

- `summary.json`: 자동화와 CI artifact 소비용.
- 선택적 `summary.md`: 사람이 빠르게 보는 리뷰용. 첫 구현에서는 JSON만 만들어도 된다.

초기 `summary.json` 필드:

- `summary-version`: 1
- `source-directory`
- `source-report-count`
- `hard-passed`
- `hard-failure-count`
- `warning-count`
- `warnings`
- `by-kind.load`
- `by-kind.open-loop`

`by-kind`에는 최소 다음 값을 넣는다.

- run count
- p50 min/max
- p99 min/max
- p99 growth ratio min/max
- actual rate min/max
- TCP HWM min/max
- dropped total
- payload error total
- pool rented max

## 다음 구현 단위

다음 단위는 작게 유지한다.

1. `tests/Hps.Benchmarks`에 per-run JSON directory 를 읽는 summary generator 를 추가한다.
2. xUnit 테스트는 fake JSON file set 으로 hard pass, hard fail, soft warning 을 검증한다.
3. CLI 는 별도 command 로 시작한다. 권장 형태는 `--summarize-baseline <input-dir> --summary <output-json>`이다.
4. 첫 구현은 summary JSON만 만든다. Markdown report 와 CI workflow 는 다음 단위로 분리한다.

## 범위 밖

- CI provider workflow 작성.
- Markdown report generator 구현.
- latency warning 을 exit code 실패로 바꾸는 정책.
- RIO/io_uring backend 성능 기준.
- 다른 장비나 다른 날짜 baseline 과의 비교.
- per-topic 또는 per-endpoint QoS 정책.

## 검증 계획

이번 단위는 설계 문서와 상태 문서만 변경한다.

- raw JSON 18개를 파싱해 표와 수치가 맞는지 확인한다.
- `DECISIONS.md`에 D070을 추가해 hard gate 보류와 summary/soft warning 우선순위를 기록한다.
- `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`를 다음 실행 지점에 맞춘다.
- `git diff --check`로 whitespace 오류를 검증한다.
