# Benchmark Baseline History

이 문서는 반복 baseline session 을 찾기 위한 전역 index 다.
자동 판정의 canonical input 은 각 session 의 raw JSON 과 `summary.json`이며,
이 문서는 사람이 빠르게 경로와 상태를 확인하기 위한 보조 entry point 로만 사용한다.

## 운영 원칙

- session 단위는 `docs/benchmarks/baselines/YYYY-MM-DD/session-NN/`을 기본으로 한다.
- 2026-06-18 root directory 는 초기 구현 흐름 때문에 `session-01` 역할을 겸한다.
- raw JSON 은 원본 측정값으로 보존한다.
- `summary.json`은 자동화와 추세 비교용 machine-readable artifact 다.
- `summary.md`는 리뷰용 human-readable artifact 다.
- `warning-count > 0`은 현재 hard failure 가 아니다. warning-as-failure 와 latency hard gate 는 별도 결정 전까지 보류한다.

## Baseline Sessions

| 날짜 | session | runner/scope | summary | human report | raw reports | hard passed | warnings | load p99 max us | open-loop p99 max us | TCP HWM max |
| --- | --- | --- | --- | --- | ---: | --- | ---: | ---: | ---: | ---: |
| 2026-06-18 | session-01(root) | local Windows TCP loopback SAEA | [summary.json](2026-06-18/summary.json) | [summary.md](2026-06-18/summary.md) | 6 | true | 0 | 924.1 | 1005.5 | 2 |
| 2026-06-18 | session-02 | local Windows TCP loopback SAEA | [summary.json](2026-06-18/session-02/summary.json) | [summary.md](2026-06-18/session-02/summary.md) | 6 | true | 0 | 512.1 | 643.3 | 3 |
| 2026-06-18 | session-03 | local Windows TCP loopback SAEA | [summary.json](2026-06-18/session-03/summary.json) | [summary.md](2026-06-18/session-03/summary.md) | 6 | true | 0 | 489.9 | 587.8 | 3 |

## 해석 메모

- 세 session 모두 delivery/drop/leak hard gate 를 통과했다.
- 세 session 모두 warning 이 없다.
- session-01 은 같은 날짜의 초기 baseline 이며, 이후 session 보다 p99 가 높게 관측됐다.
- 현재 수치는 hard latency SLO 가 아니라 Phase 4 추세 관측값이다.
- 서로 다른 장비나 CI runner 의 session 은 runner identity/environment metadata 설계 전까지 같은 latency envelope 으로 직접 비교하지 않는다.

## 다음 갱신 규칙

새 baseline session 을 추가할 때는 다음 순서로 갱신한다.

1. raw JSON 6개 이상을 session directory 에 보존한다.
2. `--summarize-baseline <session-dir> --summary <session-dir>/summary.json --summary-md <session-dir>/summary.md`로 summary artifact 를 생성한다.
3. 이 index 에 session row 를 한 줄 추가한다.
4. hard failure 또는 warning 이 있으면 `해석 메모`에 원인과 후속 판단을 짧게 남긴다.
