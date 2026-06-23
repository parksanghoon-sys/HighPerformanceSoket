# Summary/history comparison signal 설계

- 날짜: 2026-06-23
- 상태: Accepted
- 관련 결정: D063, D069, D070, D071, D078, D079, D080
- 관련 코드: `tests/Hps.Benchmarks`, `tests/Hps.Benchmarks.Tests`
- 관련 artifact:
  - `docs/benchmarks/baselines/**/summary.json`
  - `docs/benchmarks/baselines/**/history.json`

## 목적

D079로 raw benchmark report 에 runner/environment metadata 를 남기고 reader 가 이를 보존하게 됐다.
다음 문제는 여러 raw report 를 summary 로 묶거나 여러 summary 를 history 로 묶을 때, 그 결과들이
정말 같은 비교군인지 machine-readable 하게 판단할 수 없다는 점이다.

이번 설계의 목적은 `summary.json`과 `history.json`에 비교 가능성 신호를 추가하는 것이다.
이 신호는 latency hard gate 나 CI failure 로 바로 쓰지 않는다. 지금 단계에서는 사람이 리뷰하거나
후속 warning-as-failure 정책이 참조할 수 있는 비파괴 관측 field 로만 둔다.

## 현재 확인한 사실

- raw report writer 는 schema v1 top-level 에 `benchmark-profile`, `runner-id`, `runner-kind`,
  `transport-backend`, OS/framework/architecture metadata 를 기록한다.
- raw report writer 는 이미 `payload-bytes`, `target-rate-hz`, `target-duration-seconds`,
  `scenario`, `result-name`도 기록한다.
- `BaselineReportReader`는 identity metadata 를 읽지만, 아직 payload/target 설정은
  `BaselineReport` model 로 올리지 않는다.
- `BaselineSummary`와 `BaselineHistory`는 hard gate, latency warning, HWM aggregate 만 가진다.
- 하나의 summary directory 에는 `load`와 `open-loop` raw report 가 함께 들어갈 수 있고,
  두 kind 의 `scenario` 값은 다르다. 따라서 comparison key 를 단일 `scenario`로 만들면
  정상 summary 가 항상 mismatch 가 된다.

## 결정

summary/history output 에 비교 가능성 field 를 additive 로 추가한다.
`summary-version`과 `history-version`은 기존 `1`을 유지한다. 새 field 는 기존 reader 가 무시할 수 있는
top-level additive field 이며, 기존 hard gate 와 exit code 정책을 바꾸지 않는다.

비교 신호는 기존 `warning-count`에 합산하지 않는다. 기존 warning 은 latency/HWM/actual-rate 같은
성능 관측 warning 이고, comparison mismatch 는 "이 artifact 를 같은 기준선으로 비교하면 안 된다"는
별도 품질 신호다.

## Summary JSON field

`summary.json` top-level 에 다음 field 를 추가한다.

```json
{
  "comparison-compatible": true,
  "comparison-key": {
    "benchmark-profile": "tcp-loopback-saea-v1",
    "runner-id": "local-unspecified",
    "runner-kind": "local",
    "transport-backend": "SaeaTransport",
    "os-description": "Microsoft Windows ...",
    "os-architecture": "X64",
    "process-architecture": "X64",
    "framework-description": ".NET 9.0...",
    "cases": [
      {
        "result-name": "load",
        "scenario": "tcp-loopback-saea-baseline",
        "payload-bytes": 4096,
        "target-rate-hz": 100,
        "target-duration-seconds": 30
      },
      {
        "result-name": "open-loop",
        "scenario": "tcp-loopback-saea-baseline-open-loop",
        "payload-bytes": 4096,
        "target-rate-hz": 100,
        "target-duration-seconds": 30
      }
    ]
  },
  "unknown-runner-count": 0,
  "comparison-mismatch-count": 0,
  "comparison-mismatches": []
}
```

`comparison-compatible`은 다음 조건을 모두 만족할 때만 `true`다.

- source report 가 1개 이상 존재한다.
- 모든 source report 의 runner/environment key 가 `unknown`이 아니고 서로 같다.
- 같은 `result-name` group 안에서 `scenario`, `payload-bytes`, `target-rate-hz`,
  `target-duration-seconds`가 모두 같다.
- `comparison-key.cases`는 source report 에서 관측된 `result-name`별 case 를 canonical order 로 담는다.

`processor-count`는 D079와 같이 hard compatibility key 에 넣지 않는다.
CI/VM 환경에서는 logical CPU 수가 같은 runner 안에서도 바뀔 수 있어, 이 값만으로 summary 를
incompatible 처리하면 false mismatch 가 생길 수 있다. 초기 구현에서는 raw report 에 남은 diagnostic 으로만 둔다.

## Mismatch 표현

`comparison-mismatches`는 machine-readable array 로 둔다. 각 entry 는 다음 field 를 가진다.

```json
{
  "code": "comparison-key-mismatch",
  "field": "runner-id",
  "expected": "local-a",
  "actual": "local-b",
  "source-path": "session-02/load-03.json"
}
```

초기 code 는 다음으로 제한한다.

- `no-source-reports`: summary input 에 raw report 가 없다.
- `unknown-runner`: legacy raw report 처럼 runner/environment metadata 가 없어 key 를 신뢰할 수 없다.
- `comparison-key-mismatch`: 기준 report 와 다른 hard comparison key 가 있다.

`unknown-runner-count`는 `unknown-runner`에 해당하는 source report 수다. legacy raw report 끼리 모든
metadata 가 `unknown`으로 같더라도 compatible 로 보지 않는다. 모르는 runner 와 모르는 runner 는
비교 가능하다고 증명된 것이 아니기 때문이다.

## History JSON field

`history.json` top-level 에 summary 와 같은 비교 신호를 추가한다.

```json
{
  "comparison-compatible": true,
  "comparison-key": { "...": "summary comparison key 와 같은 shape" },
  "comparison-mismatch-count": 0,
  "comparison-mismatches": []
}
```

history 가 compatible 이려면 다음 조건을 모두 만족해야 한다.

- 모든 session summary 가 `comparison-compatible == true`다.
- 모든 session summary 의 `comparison-key`가 같다.
- legacy summary 처럼 comparison field 가 없는 summary 는 incompatible 로 본다.

history mismatch entry 는 session 단위 출처를 포함한다.

```json
{
  "code": "history-comparison-key-mismatch",
  "session": "session-03",
  "summary-path": "2026-06-18/session-03/summary.json",
  "field": "cases[open-loop].scenario",
  "expected": "tcp-loopback-saea-baseline-open-loop",
  "actual": "tcp-loopback-saea-baseline-open-loop-v2"
}
```

legacy summary 는 다음처럼 표현한다.

```json
{
  "code": "legacy-summary-without-comparison",
  "session": "session-01(root)",
  "summary-path": "2026-06-18/summary.json",
  "field": "comparison-compatible",
  "expected": "present",
  "actual": "missing"
}
```

## Markdown 출력

`summary.md`에는 `## Comparison` section 을 추가한다.

- compatible 여부
- runner id/kind/profile/backend
- OS/framework/architecture
- cases 목록
- mismatch 가 있으면 code, field, source 를 표로 출력

`history.md`에는 session table 앞이나 뒤에 `## Comparison` section 을 추가한다.

- history 전체 compatible 여부
- 기준 comparison key 요약
- session 별 mismatch 표

Markdown 은 리뷰 보조 artifact 이며 canonical 값은 JSON 이다.

## 구현 단위 후보

후속 구현 계획은 다음 4개 작은 단위로 나누는 것이 가장 안전하다.

1. Summary comparison model/generator
   - `BaselineReport`에 `PayloadBytes`, `TargetRateHz`, `TargetDurationSeconds`를 추가하고 reader 가 raw field 를 읽게 한다.
   - `BaselineComparisonKey`, `BaselineComparisonCase`, `BaselineComparisonMismatch` 같은 내부 model 을 추가한다.
   - `BaselineSummaryGenerator`가 source report 목록에서 comparison signal 을 계산한다.
   - tests 는 compatible, runner mismatch, legacy unknown, same summary 안의 `load`/`open-loop` scenario 차이를 검증한다.
2. Summary writer/Markdown writer
   - `BaselineSummaryWriter`가 additive comparison field 를 쓴다.
   - `BaselineSummaryMarkdownWriter`가 사람이 볼 comparison section 을 쓴다.
   - writer tests 는 JSON shape 와 Markdown section 을 검증한다.
3. History reader/model/generator
   - `BaselineHistoryReader`가 summary comparison field 를 optional 로 읽는다.
   - legacy summary 는 incompatible session signal 로 변환한다.
   - `BaselineHistoryGenerator`가 session comparison key 를 aggregate 한다.
4. History writer/Markdown writer/CLI smoke
   - `BaselineHistoryWriter`와 `BaselineHistoryMarkdownWriter`가 comparison field/section 을 출력한다.
   - 기존 `--summarize-baseline-history` exit code 는 hard gate 기준만 유지하는지 확인한다.

각 구현 단위는 Red-Green-Refactor 로 진행하고, 새 테스트에는 무엇을 보호하는지 한국어 주석을 남긴다.

## 범위 밖

- comparison mismatch 를 exit code 실패로 승격하는 정책
- CI workflow 또는 warning-as-failure option
- latency hard threshold 확정
- `processor-count`를 hard compatibility key 로 승격
- generated `docs/benchmarks/baselines/index.md` 자동 갱신
- 기존 baseline artifact 전체 재생성
- RIO/io_uring benchmark profile 추가

## 검증 계획

이번 설계 단위의 검증은 문서 정합성 중심이다.

- D079 raw metadata 정책과 충돌하지 않는지 확인한다.
- current `BaselineReport`, `BaselineSummary*`, `BaselineHistory*` 구조에서 필요한 선행 확장을 빠뜨리지 않았는지 확인한다.
- placeholder 가 남지 않았는지 확인한다.
- `git diff --check`, solution build/test 로 현재 repository 상태가 깨지지 않는지 확인한다.

## Self-review

- summary 안에서 `load`와 `open-loop`이 다른 `scenario`를 갖는 현실을 반영해 `cases` 배열로 설계했다.
- legacy raw report/summary 는 compatible 로 추정하지 않고 별도 mismatch 로 드러낸다.
- comparison mismatch 는 기존 `warning-count`와 exit code 에 영향을 주지 않는다.
- `processor-count`는 raw diagnostic 으로 유지하고 hard key 에서 제외했다.
