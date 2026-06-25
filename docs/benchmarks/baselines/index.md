# Benchmark Baseline History

이 문서는 반복 baseline session 을 찾기 위한 전역 index 다.
자동 판정의 canonical input 은 각 session 의 raw JSON 과 `summary.json`이며,
이 문서는 사람이 빠르게 경로와 상태를 확인하기 위한 보조 entry point 로만 사용한다.

## 운영 원칙

- session 단위는 `docs/benchmarks/baselines/YYYY-MM-DD/session-NN/`을 기본으로 한다.
- 2026-06-18 root directory 는 초기 구현 흐름 때문에 `session-01` 역할을 겸한다.
- 명시적 `HPS_BENCHMARK_RUNNER_ID`로 생성한 baseline 은
  `docs/benchmarks/baselines/runners/<runner-id>/YYYY-MM-DD/session-NN/` 아래에 둔다.
- top-level `YYYY-MM-DD` roots 는 legacy 또는 `local-unspecified` baseline 으로 보존하고, 명시 runner session 을 섞지 않는다.
- `runner-id`는 path 와 raw report metadata 가 같아야 하며, host name, user name, IP address, 내부 자산 번호를 쓰지 않는다.
- raw JSON 은 원본 측정값으로 보존한다.
- `summary.json`은 자동화와 추세 비교용 machine-readable artifact 다.
- `summary.md`는 리뷰용 human-readable artifact 다.
- `history.json`은 여러 session summary 를 묶은 date-level machine-readable artifact 다.
- `history.md`는 date-level history 를 사람이 빠르게 확인하기 위한 보조 artifact 다.
- `warning-count > 0`은 현재 hard failure 가 아니다. warning-as-failure 와 latency hard gate 는 별도 결정 전까지 보류한다.
- CI benchmark 는 D090 기준으로 artifact-only 단계에서 시작한다.
  CI의 매 실행 artifact 는 `artifacts/benchmarks/runners/<ci-runner-id>/...` 같은 CI artifact 영역에 두고,
  이 index 에는 사람이 repository baseline 으로 채택한 결과만 추가한다.

## Runner Groups

명시적 runner baseline 은 D084 기준으로 `docs/benchmarks/baselines/runners/<runner-id>/YYYY-MM-DD/session-NN/`
구조를 사용한다.

| runner id | runner kind | profile | transport backend | latest date root | 비고 |
| --- | --- | --- | --- | --- | --- |
| ci-windows-x64-01 | ci | tcp-loopback-saea-v1 | SaeaTransport | [2026-06-25](runners/ci-windows-x64-01/2026-06-25/history.json) | CI push-triggered artifact adopted manually, runner root [history.json](runners/ci-windows-x64-01/history.json) |
| local-win-x64-01 | local | tcp-loopback-saea-v1 | SaeaTransport | [2026-06-25](runners/local-win-x64-01/2026-06-25/history.json) | explicit runner 2-date-root reference 완료, runner root [history.json](runners/local-win-x64-01/history.json) |

## Runner Date-level History

| runner id | 날짜 | history | human report | sessions | hard passed | warnings | comparison compatible |
| --- | --- | --- | --- | ---: | --- | ---: | --- |
| ci-windows-x64-01 | 2026-06-25 | [history.json](runners/ci-windows-x64-01/2026-06-25/history.json) | [history.md](runners/ci-windows-x64-01/2026-06-25/history.md) | 1 | true | 0 | true |
| local-win-x64-01 | 2026-06-25 | [history.json](runners/local-win-x64-01/2026-06-25/history.json) | [history.md](runners/local-win-x64-01/2026-06-25/history.md) | 3 | true | 0 | true |
| local-win-x64-01 | 2026-06-24 | [history.json](runners/local-win-x64-01/2026-06-24/history.json) | [history.md](runners/local-win-x64-01/2026-06-24/history.md) | 3 | true | 0 | true |

## Date-level History

| 날짜 | history | human report | sessions | hard passed | warnings | comparison compatible |
| --- | --- | --- | ---: | --- | ---: | --- |
| 2026-06-24 | [history.json](2026-06-24/history.json) | [history.md](2026-06-24/history.md) | 3 | true | 0 | true |
| 2026-06-18 | [history.json](2026-06-18/history.json) | [history.md](2026-06-18/history.md) | 3 | true | 0 | false |

## Baseline Sessions

| 날짜 | session | runner/scope | summary | human report | raw reports | hard passed | warnings | load p99 max us | open-loop p99 max us | TCP HWM max |
| --- | --- | --- | --- | --- | ---: | --- | ---: | ---: | ---: | ---: |
| 2026-06-25 | session-01 | ci-windows-x64-01 CI Windows TCP loopback SAEA, adopted from push run 28145025444 | [summary.json](runners/ci-windows-x64-01/2026-06-25/session-01/summary.json) | [summary.md](runners/ci-windows-x64-01/2026-06-25/session-01/summary.md) | 6 | true | 0 | 275.3 | 322.9 | 2 |
| 2026-06-25 | session-01 | local-win-x64-01 explicit runner, local Windows TCP loopback SAEA | [summary.json](runners/local-win-x64-01/2026-06-25/session-01/summary.json) | [summary.md](runners/local-win-x64-01/2026-06-25/session-01/summary.md) | 6 | true | 0 | 921.1 | 1077.4 | 2 |
| 2026-06-25 | session-02 | local-win-x64-01 explicit runner, local Windows TCP loopback SAEA | [summary.json](runners/local-win-x64-01/2026-06-25/session-02/summary.json) | [summary.md](runners/local-win-x64-01/2026-06-25/session-02/summary.md) | 6 | true | 0 | 935.6 | 1013.1 | 2 |
| 2026-06-25 | session-03 | local-win-x64-01 explicit runner, local Windows TCP loopback SAEA | [summary.json](runners/local-win-x64-01/2026-06-25/session-03/summary.json) | [summary.md](runners/local-win-x64-01/2026-06-25/session-03/summary.md) | 6 | true | 0 | 842 | 975.6 | 2 |
| 2026-06-24 | session-01 | local-win-x64-01 explicit runner, local Windows TCP loopback SAEA | [summary.json](runners/local-win-x64-01/2026-06-24/session-01/summary.json) | [summary.md](runners/local-win-x64-01/2026-06-24/session-01/summary.md) | 6 | true | 0 | 870.7 | 844.7 | 2 |
| 2026-06-24 | session-02 | local-win-x64-01 explicit runner, local Windows TCP loopback SAEA | [summary.json](runners/local-win-x64-01/2026-06-24/session-02/summary.json) | [summary.md](runners/local-win-x64-01/2026-06-24/session-02/summary.md) | 6 | true | 0 | 821.4 | 893 | 2 |
| 2026-06-24 | session-03 | local-win-x64-01 explicit runner, local Windows TCP loopback SAEA | [summary.json](runners/local-win-x64-01/2026-06-24/session-03/summary.json) | [summary.md](runners/local-win-x64-01/2026-06-24/session-03/summary.md) | 6 | true | 0 | 806.9 | 1051.5 | 2 |
| 2026-06-24 | session-01 | local Windows TCP loopback SAEA, D079 metadata | [summary.json](2026-06-24/session-01/summary.json) | [summary.md](2026-06-24/session-01/summary.md) | 6 | true | 0 | 876.3 | 948.5 | 2 |
| 2026-06-24 | session-02 | local Windows TCP loopback SAEA, D079 metadata | [summary.json](2026-06-24/session-02/summary.json) | [summary.md](2026-06-24/session-02/summary.md) | 6 | true | 0 | 1020.4 | 1006.5 | 2 |
| 2026-06-24 | session-03 | local Windows TCP loopback SAEA, D079 metadata | [summary.json](2026-06-24/session-03/summary.json) | [summary.md](2026-06-24/session-03/summary.md) | 6 | true | 0 | 930 | 979.4 | 2 |
| 2026-06-18 | session-01(root) | local Windows TCP loopback SAEA | [summary.json](2026-06-18/summary.json) | [summary.md](2026-06-18/summary.md) | 6 | true | 0 | 924.1 | 1005.5 | 2 |
| 2026-06-18 | session-02 | local Windows TCP loopback SAEA | [summary.json](2026-06-18/session-02/summary.json) | [summary.md](2026-06-18/session-02/summary.md) | 6 | true | 0 | 512.1 | 643.3 | 3 |
| 2026-06-18 | session-03 | local Windows TCP loopback SAEA | [summary.json](2026-06-18/session-03/summary.json) | [summary.md](2026-06-18/session-03/summary.md) | 6 | true | 0 | 489.9 | 587.8 | 3 |

## 2026-06-24 Reference Latency Envelope

이 표는 D082 기준의 non-failing reference envelope 다. 자동 실패 조건이 아니라 후속 baseline 리뷰 기준으로만 사용한다.
집계 방식은 각 session `summary.json`의 `by-kind` aggregate 를 세 session 간 다시 집계하는 방식이며,
latency, growth, HWM 은 max, actual rate 는 min 을 사용한다.

| 항목 | load | open-loop |
| --- | ---: | ---: |
| compatible sessions | 3 | 3 |
| raw runs | 9 | 9 |
| p50 max us | 257.2 | 281.7 |
| p99 max us | 1020.4 | 1006.5 |
| p99 median max us | 967.5 | 994.4 |
| p99 growth ratio max | 1.23 | 1.06 |
| actual rate min hz | 99.8 | 99.9 |
| TCP HWM max | 1 | 2 |
| dropped total | 0 | 0 |
| payload error total | 0 | 0 |
| pool rented max | 0 | 0 |

## ci-windows-x64-01 CI Runner Reference Latency Envelope

???쒕뒗 D095 ?섎룞 梨꾪깮 ?덉감濡?repository baseline 援ъ“???ㅼ뼱??泥?CI runner reference ??
CI hosted runner evidence ?대?濡?local runner envelope ? 吏곸젒 鍮꾧탳?섏? ?딄퀬, 媛숈? CI runner ?????꾩냽 session ??媛깆떊 湲곗??쇰줈留??ъ슜?쒕떎.

| ??ぉ | load | open-loop |
| --- | ---: | ---: |
| compatible sessions | 1 | 1 |
| raw runs | 3 | 3 |
| p50 max us | 149 | 158.3 |
| p99 max us | 275.3 | 322.9 |
| p99 median max us | 262.5 | 263.3 |
| p99 growth ratio max | 1.09 | 1.46 |
| actual rate min hz | 99.9 | 100 |
| TCP HWM max | 1 | 2 |
| dropped total | 0 | 0 |
| payload error total | 0 | 0 |
| pool rented max | 0 | 0 |

## local-win-x64-01 Explicit Runner Reference Latency Envelope

이 표는 D084 저장 구조 아래에서 수집한 explicit runner reference 다.
2026-06-25 session-03 추가로 같은 runner 의 두 date root 가 각각 3-session reference 를 갖췄다.
다만 이 표는 아직 D082 기준의 warning-as-failure 또는 CI latency gate 로 자동 승격하지 않고,
다음 단위에서 gate 후보를 재평가하기 위한 입력으로만 사용한다.

| 항목 | load | open-loop |
| --- | ---: | ---: |
| compatible sessions | 6 | 6 |
| raw runs | 18 | 18 |
| p50 max us | 268.1 | 322.6 |
| p99 max us | 935.6 | 1077.4 |
| p99 median max us | 903.9 | 1048.9 |
| p99 growth ratio max | 1.2 | 1.18 |
| actual rate min hz | 99.1 | 99.9 |
| TCP HWM max | 1 | 2 |
| dropped total | 0 | 0 |
| payload error total | 0 | 0 |
| pool rented max | 0 | 0 |

## 해석 메모

- 2026-06-18 세 session 과 2026-06-24 세 session 모두 delivery/drop/leak hard gate 를 통과했다.
- 현재 기록된 모든 session 은 warning 이 없다.
- session-01 은 같은 날짜의 초기 baseline 이며, 이후 session 보다 p99 가 높게 관측됐다.
- 현재 수치는 hard latency SLO 가 아니라 Phase 4 추세 관측값이다.
- 2026-06-24 session-01/session-02/session-03 은 D079 runner identity/environment metadata 도입 후 생성한 baseline 이다.
  summary/history comparison 은 `comparison-compatible=true`, unknown runner 0, mismatch 0 이다.
- D082 기준으로 2026-06-24 compatible baseline 3개는 reference latency envelope 로 채택하지만,
  hard latency gate, warning-as-failure, CI latency failure 로 승격하지 않는다.
  현재 p99 max 가 load 1020.4 us, open-loop 1006.5 us 까지 관측되어 1 ms hard SLO 는 현 baseline 과 맞지 않는다.
- 2026-06-24 baseline 은 `runner-id=local-unspecified`이므로 gate 승격 조건의 날짜 root count 에 산입하지 않는다.
  이 표본은 reference envelope 의 근거이며, envelope 초과 여부는 현재 자동 failure 가 아니라 수동 리뷰 메모로만 기록한다.
- `local-win-x64-01` runner group 은 첫 explicit runner 3-session baseline 이며, D084 저장 구조와 history command 경로 검증 표본이다.
  2026-06-25 session-03 추가 후 runner root history 는 6-session 을 묶고 hard gate 와 comparison compatibility 를 통과한다.
  같은 runner 의 두 date root 가 각각 3-session reference 를 갖췄지만, D089 기준으로 아직 D082 warning-as-failure/CI gate 로 승격하지 않는다.
  D090 기준으로 CI benchmark 는 `ci-windows-x64-01` 같은 별도 runner id 를 쓰고, latency/HWM/warning 은 report-only 로 둔다.
- 2026-06-18 raw report 는 D079 runner identity/environment metadata 도입 전 artifact 이므로
  summary/history comparison 은 `unknown-runner` mismatch 로 `comparison-compatible=false`를 기록한다.
  이 값은 hard gate 실패가 아니라 비교 가능성 신호다.
- 서로 다른 장비나 CI runner 의 session 은 runner identity/environment metadata 가 있는 새 raw report 부터 같은 latency envelope 으로 비교한다.

## 다음 갱신 규칙

새 baseline session 을 추가할 때는 다음 순서로 갱신한다.

1. 명시적 runner baseline 이면 `runners/<runner-id>/YYYY-MM-DD/session-NN/` 아래에 session directory 를 만들고,
   top-level `YYYY-MM-DD` root 에 섞지 않는다.
2. raw JSON 6개 이상을 session directory 에 보존한다.
3. `--summarize-baseline <session-dir> --summary <session-dir>/summary.json --summary-md <session-dir>/summary.md`로 summary artifact 를 생성한다.
4. 날짜 root 에 대해 `--summarize-baseline-history <date-root> --history <date-root>/history.json --history-md <date-root>/history.md`를 실행한다.
5. 같은 runner 에 여러 날짜 root 가 생기면 runner root 에 대해 `--summarize-baseline-history <runner-root>`도 실행할 수 있다.
6. 이 index 에 runner group, session row, date-level history row 를 갱신한다.
7. hard failure, warning, comparison mismatch 가 있으면 `해석 메모`에 원인과 후속 판단을 짧게 남긴다.
