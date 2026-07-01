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
- D125 기준으로 runner/profile scoped 판단은 기존 `warning-count`가 아니라 별도 envelope comparison artifact 로 분리한다.
  이 신호도 초기에는 report-only 이며 process failure 나 CI failure 로 쓰지 않는다.
- D127 기준으로 CI benchmark workflow 는 repository reference history 가 있을 때 업로드 artifact date root 에
  `envelope.json`과 `envelope.md`를 함께 생성한다. 이 artifact 의 mismatch/signal 도 report-only 이며
  `warning-count`나 workflow failure 로 합산하지 않는다.
- D151 기준으로 Linux `io_uring` benchmark workflow 는 TCP/UDP protocol root 를 분리하므로,
  reference history 도 `docs/benchmarks/baselines/runners/<runner-id>/tcp/history.json`,
  `.../<runner-id>/udp/history.json`처럼 protocol별로 둔다.
  아직 `ci-linux-iouring-x64-01` repository reference 는 없으며, reference 가 없으면 workflow envelope step 은 skip 한다.
- D153 기준으로 첫 `ci-linux-iouring-x64-01` TCP/UDP reference 는 provisional reference 로 수동 채택할 수 있다.
  초기 `io_uring` reference 의 warning-count > 0은 D070 전역 soft threshold signal 로 기록하되,
  채택 차단 조건이나 hard gate 승격 근거로 사용하지 않는다.
- CI benchmark 는 D090 기준으로 artifact-only 단계에서 시작한다.
  CI의 매 실행 artifact 는 `artifacts/benchmarks/runners/<ci-runner-id>/...` 같은 CI artifact 영역에 두고,
  이 index 에는 사람이 repository baseline 으로 채택한 결과만 추가한다.

## Runner Groups

명시적 runner baseline 은 D084 기준으로 `docs/benchmarks/baselines/runners/<runner-id>/YYYY-MM-DD/session-NN/`
구조를 사용한다.

| runner id | runner kind | profile | transport backend | latest date root | 비고 |
| --- | --- | --- | --- | --- | --- |
| ci-linux-iouring-x64-01/tcp | ci | tcp-loopback-iouring-v1 | IoUringTransport | [2026-07-01](runners/ci-linux-iouring-x64-01/tcp/2026-07-01/history.json) | Linux io_uring TCP provisional reference, runner/protocol root [history.json](runners/ci-linux-iouring-x64-01/tcp/history.json) |
| ci-linux-iouring-x64-01/udp | ci | udp-loopback-iouring-v1 | IoUringTransport | [2026-07-01](runners/ci-linux-iouring-x64-01/udp/2026-07-01/history.json) | Linux io_uring UDP provisional reference, runner/protocol root [history.json](runners/ci-linux-iouring-x64-01/udp/history.json) |
| ci-windows-x64-01 | ci | tcp-loopback-saea-v1 | SaeaTransport | [2026-06-29](runners/ci-windows-x64-01/2026-06-29/history.json) | CI push-triggered artifacts adopted manually, runner root [history.json](runners/ci-windows-x64-01/history.json) |
| local-win-x64-01 | local | tcp-loopback-saea-v1 | SaeaTransport | [2026-06-29](runners/local-win-x64-01/2026-06-29/history.json) | explicit runner 3-date-root reference 완료, runner root [history.json](runners/local-win-x64-01/history.json) |

## Runner Date-level History

| runner id | 날짜 | history | human report | sessions | hard passed | warnings | comparison compatible |
| --- | --- | --- | --- | ---: | --- | ---: | --- |
| ci-linux-iouring-x64-01/tcp | 2026-07-01 | [history.json](runners/ci-linux-iouring-x64-01/tcp/2026-07-01/history.json) | [history.md](runners/ci-linux-iouring-x64-01/tcp/2026-07-01/history.md) | 1 | true | 6 | true |
| ci-linux-iouring-x64-01/udp | 2026-07-01 | [history.json](runners/ci-linux-iouring-x64-01/udp/2026-07-01/history.json) | [history.md](runners/ci-linux-iouring-x64-01/udp/2026-07-01/history.md) | 1 | true | 3 | true |
| ci-windows-x64-01 | 2026-06-29 | [history.json](runners/ci-windows-x64-01/2026-06-29/history.json) | [history.md](runners/ci-windows-x64-01/2026-06-29/history.md) | 1 | true | 0 | true |
| ci-windows-x64-01 | 2026-06-25 | [history.json](runners/ci-windows-x64-01/2026-06-25/history.json) | [history.md](runners/ci-windows-x64-01/2026-06-25/history.md) | 1 | true | 0 | true |
| local-win-x64-01 | 2026-06-29 | [history.json](runners/local-win-x64-01/2026-06-29/history.json) | [history.md](runners/local-win-x64-01/2026-06-29/history.md) | 3 | true | 0 | true |
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
| 2026-07-01 | session-01 | ci-linux-iouring-x64-01 Linux TCP loopback io_uring, provisional reference adopted from run 28492234252 | [summary.json](runners/ci-linux-iouring-x64-01/tcp/2026-07-01/session-01/summary.json) | [summary.md](runners/ci-linux-iouring-x64-01/tcp/2026-07-01/session-01/summary.md) | 6 | true | 6 | 4298.8 | 5588.6 | 1 |
| 2026-07-01 | session-01 | ci-linux-iouring-x64-01 Linux UDP loopback io_uring, provisional reference adopted from run 28492234252 | [summary.json](runners/ci-linux-iouring-x64-01/udp/2026-07-01/session-01/summary.json) | [summary.md](runners/ci-linux-iouring-x64-01/udp/2026-07-01/session-01/summary.md) | 6 | true | 3 | 1623.8 | 1322.0 | 0 |
| 2026-06-29 | session-01 | ci-windows-x64-01 CI Windows TCP loopback SAEA, adopted from push run 28350456434 | [summary.json](runners/ci-windows-x64-01/2026-06-29/session-01/summary.json) | [summary.md](runners/ci-windows-x64-01/2026-06-29/session-01/summary.md) | 6 | true | 0 | 401 | 520.7 | 2 |
| 2026-06-25 | session-01 | ci-windows-x64-01 CI Windows TCP loopback SAEA, adopted from push run 28145025444 | [summary.json](runners/ci-windows-x64-01/2026-06-25/session-01/summary.json) | [summary.md](runners/ci-windows-x64-01/2026-06-25/session-01/summary.md) | 6 | true | 0 | 275.3 | 322.9 | 2 |
| 2026-06-29 | session-01 | local-win-x64-01 explicit runner, local Windows TCP loopback SAEA | [summary.json](runners/local-win-x64-01/2026-06-29/session-01/summary.json) | [summary.md](runners/local-win-x64-01/2026-06-29/session-01/summary.md) | 6 | true | 0 | 844.6 | 948.8 | 2 |
| 2026-06-29 | session-02 | local-win-x64-01 explicit runner, local Windows TCP loopback SAEA | [summary.json](runners/local-win-x64-01/2026-06-29/session-02/summary.json) | [summary.md](runners/local-win-x64-01/2026-06-29/session-02/summary.md) | 6 | true | 0 | 856.7 | 878.3 | 2 |
| 2026-06-29 | session-03 | local-win-x64-01 explicit runner, local Windows TCP loopback SAEA | [summary.json](runners/local-win-x64-01/2026-06-29/session-03/summary.json) | [summary.md](runners/local-win-x64-01/2026-06-29/session-03/summary.md) | 6 | true | 0 | 884.6 | 978.9 | 2 |
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

이 표는 D095 수동 채택 절차로 repository baseline 구조에 들어온 CI runner reference 다.
CI hosted runner evidence 이므로 local runner envelope 와 직접 비교하지 않고, 같은 CI runner 의 후속 session 을
리뷰하는 기준으로만 사용한다.

| 항목 | load | open-loop |
| --- | ---: | ---: |
| compatible sessions | 2 | 2 |
| raw runs | 6 | 6 |
| p50 max us | 156.2 | 161.4 |
| p99 max us | 401 | 520.7 |
| p99 median max us | 293.9 | 279.2 |
| p99 growth ratio max | 1.09 | 1.46 |
| actual rate min hz | 99.8 | 100 |
| TCP HWM max | 1 | 2 |
| dropped total | 0 | 0 |
| payload error total | 0 | 0 |
| pool rented max | 0 | 0 |

이 CI envelope 는 D131 기준으로 2-date-root/2-session reference signal 이다. 값은 artifact chain, D127 envelope
upload, D095 수동 채택 절차가 동작함을 보여주지만, CI runner 표본은 아직 latency hard gate 또는
warning-as-failure 조건으로 승격하지 않는다.

## ci-linux-iouring-x64-01 io_uring Protocol Reference

이 표는 D153 기준으로 수동 채택한 Linux `io_uring` protocol별 provisional reference 다.
TCP와 UDP는 protocol root 를 분리하며, 같은 runner id 라도 같은 history 에 섞지 않는다.
warning-count 는 D070 전역 soft threshold signal 이므로 기록하되, 초기 reference 채택 차단 조건이나
latency hard gate 로 사용하지 않는다.

| protocol | compatible sessions | raw runs | p50 max us | p99 max us | p99 median max us | p99 growth ratio max | actual rate min hz | HWM max | dropped total | payload error total | pool rented max |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| TCP load | 1 | 3 | 3112.9 | 4298.8 | 4144.5 | 1.03 | 99.8 | 1 | 0 | 0 | 0 |
| TCP open-loop | 1 | 3 | 3450.2 | 5588.6 | 4564.0 | 1.31 | 100 | 1 | 0 | 0 | 0 |
| UDP load | 1 | 3 | 850.5 | 1623.8 | 1495.8 | 1.02 | 99.9 | 0 | 0 | 0 | 0 |
| UDP open-loop | 1 | 3 | 1229.0 | 1322.0 | 1276.1 | 1.02 | 100 | 0 | 0 | 0 | 0 |

채택 후 protocol별 reference history 를 대상으로 envelope command smoke 를 실행했고,
TCP/UDP 모두 `envelope-compatible=true`, `envelope-signal-count=0`을 확인했다.
이는 같은 artifact 를 자신이 만든 첫 reference 와 비교한 smoke 이므로 성능 안정성을 증명하는 값이 아니라,
D151 reference path 가 더 이상 skip 되지 않고 실제 command 로 연결된다는 경로 검증이다.

사용자 push 이후 reference-present run `28493590950`도 검토했다.
artifact 는 TCP/UDP `envelope.json`과 `envelope.md`를 모두 포함했고, root summary 의 envelope exit code 는 둘 다 0이다.
TCP envelope 는 `envelope-compatible=true`, `envelope-signal-count=0`이다.
UDP envelope 는 `envelope-compatible=false`, `envelope-signal-count=2`이며,
load `p99-max-us`와 open-loop `p50-median-us` upper-bound signal 을 기록했다.
이 signal 은 D153 provisional reference 상태의 report-only triage 대상이며,
latency hard gate, warning-as-failure, default backend promotion 근거로 사용하지 않는다.

D156 기준으로 reference-present candidate 를 두 개 더 수집했다.
run `28494135787`은 TCP signal 0, UDP signal 2(load `p99-growth-ratio-max`,
open-loop `p50-median-us`)를 기록했고, run `28494404015`는 TCP signal 0,
UDP signal 1(open-loop `p50-median-us`)을 기록했다.
D155 포함 세 candidate 모두 UDP hard gate 와 drop/payload/pool leak 조건은 통과했지만,
open-loop `p50-median-us` signal 이 3/3 반복됐다.
D157 기준으로 다음 단위는 UDP latency triage 설계이며, candidate raw report 는 자동 repository baseline 으로 채택하지 않는다.

## local-win-x64-01 Explicit Runner Reference Latency Envelope

이 표는 D084 저장 구조 아래에서 수집한 explicit runner reference 다.
2026-06-29 세 session 추가로 같은 runner 의 세 date root 가 각각 3-session reference 를 갖췄다.
이로써 D082가 요구한 explicit runner 3-date-root evidence 조건은 충족됐고,
D124 기준으로 이 표를 `local-win-x64-01`의 runner-local reference envelope 로 채택한다.
다만 이 envelope 는 수동 리뷰 기준이며, warning-as-failure 또는 CI latency gate 로 자동 승격하지 않는다.

| 항목 | load | open-loop |
| --- | ---: | ---: |
| compatible sessions | 9 | 9 |
| raw runs | 27 | 27 |
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
  2026-06-29 session-03 추가 후 runner root history 는 9-session 을 묶고 hard gate 와 comparison compatibility 를 통과한다.
  같은 runner 의 세 date root 가 각각 3-session reference 를 갖춰 D082의 evidence 조건은 충족했고,
  D124로 이 envelope 를 runner-local 수동 리뷰 기준으로 채택했다.
  D125로 runner/profile scoped 판단은 기존 `BaselineSummaryGenerator` warning threshold 를 바꾸지 않고
  별도 envelope comparison artifact 로 분리하기로 했다.
  warning-as-failure/CI latency gate 는 계속 승격하지 않는다.
  D090 기준으로 CI benchmark 는 `ci-windows-x64-01` 같은 별도 runner id 를 쓰고, latency/HWM/warning 은 report-only 로 둔다.
- 2026-06-29 CI push run `28350456434`는 raw 6개, summary/history, envelope artifact 를 모두 업로드했고
  workflow 도 성공했다. 업로드 artifact 의 envelope 는 이전 1-session CI reference 대비 load/open-loop p99 upper-bound
  signal 2개를 기록했지만, D125/D127 기준 report-only 신호라서 D095 채택 차단 조건은 아니다.
  repository baseline 으로 채택한 뒤 runner root history 는 2-session, hard-passed true, warning-count 0,
  comparison-compatible true 상태다.
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
8. 후속 envelope comparison artifact 가 생기면 기존 `warning-count`와 분리해 `envelope-signal-count`만 해석 메모에 남긴다.
