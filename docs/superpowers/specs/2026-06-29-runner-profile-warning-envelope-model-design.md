# Runner/Profile Warning Envelope Model 설계

- 날짜: 2026-06-29
- 상태: Accepted
- 관련 결정: D080, D082, D090, D096, D123, D124, D125
- 관련 파일:
  - `tests/Hps.Benchmarks/BaselineSummaryGenerator.cs`
  - `tests/Hps.Benchmarks/BaselineSummaryWriter.cs`
  - `tests/Hps.Benchmarks/BaselineHistoryWriter.cs`
  - `tests/Hps.Benchmarks/BaselineHistoryReader.cs`
  - `docs/benchmarks/baselines/index.md`

## 배경

D124에서 `local-win-x64-01`의 3-date-root/9-session envelope 를 runner-local reference 로 채택했다.
하지만 현재 `BaselineSummaryGenerator`의 warning threshold 는 전역 상수다.

현재 전역 warning threshold:

| 항목 | 값 |
| --- | ---: |
| load p99 latency | 1386.2 us |
| open-loop p99 latency | 1508.3 us |
| p99 growth ratio | 2.0 |
| actual rate low | 95 Hz |
| load TCP HWM | 4 |
| open-loop TCP HWM | 8 |

이 값을 `local-win-x64-01`의 SAEA TCP loopback 기준으로 낮추면 같은 기준이 CI runner, RIO, UDP benchmark 에도
적용된다. runner/profile/workload 가 다른 측정값을 같은 전역 threshold 로 판단하면 false signal 이 늘고,
반대로 어떤 runner 에서는 느슨한 기준이 된다.

## 목표

이번 설계의 목표는 baseline artifact 로 축적한 reference envelope 를
benchmark profile, runner id/kind, backend, workload case 별로 적용하는 모델을 정하는 것이다.

구체적으로는 다음 질문을 닫는다.

1. 기존 `warning-count`를 유지할지, 새 비교 신호를 둘지.
2. reference envelope 를 어떤 artifact 에서 계산할지.
3. candidate summary/history 를 어떤 key 로 reference 와 매칭할지.
4. 초과 신호를 어떤 schema 로 저장하고 exit code 에 반영할지.
5. 구현은 어느 command/API 경계로 시작할지.

## 현재 artifact 제약

`summary.json`은 kind별 상세 aggregate 를 가진다.

- p50 min/max/median
- p99 min/max/median
- p99 growth ratio min/max
- actual rate min/max
- TCP HWM min/max
- dropped/payload error/pool rented aggregate
- comparison key

`history.json`은 여러 session 을 묶지만, top-level 에 모든 kind metric 을 다시 쓰지 않는다.
대신 각 session 의 `summary-path`를 가진다.

따라서 full envelope 는 `history.json` 자체만으로 계산하지 않고,
reference history 가 가리키는 각 `summary.json`을 다시 읽어 계산해야 한다.

## 선택지

### 선택지 A: 기존 `warning-count` 전역 상수를 runner-local 값으로 낮춘다

채택하지 않는다.

장점은 구현이 가장 작다는 점이다. 하지만 `BaselineSummaryGenerator`는 runner/profile/workload scoped 입력을 받지 않는다.
전역 상수를 낮추면 local SAEA TCP loopback 기준이 CI/RIO/UDP에도 적용된다.
이는 D090/D096의 CI artifact-only 정책과도 충돌한다.

### 선택지 B: `--summarize-baseline`에 reference history 옵션을 붙인다

지금은 채택하지 않는다.

summary 생성과 envelope 비교를 한 command 에 합칠 수 있지만, summary artifact 의 의미가 흔들린다.
기존 summary 는 source report 를 읽어 hard gate, soft warning, comparison compatibility 를 계산하는 산출물이다.
여기에 reference history 를 섞으면 같은 input directory 도 옵션에 따라 다른 warning/count 를 만들 수 있다.
기존 artifact 재생성 및 역사 비교가 더 복잡해진다.

### 선택지 C: 별도 envelope comparison command/artifact 를 추가한다

채택한다.

새 command 는 reference history 와 candidate summary/history 를 읽어 별도 `envelope` artifact 를 생성한다.
기존 `summary.json`, `history.json`, `warning-count`, `hard-passed`, exit code 정책은 유지한다.
Envelope signal 은 D080 comparison signal 과 같은 계열의 non-failing 보조 artifact 로 시작한다.

## 결정

D125로 다음 정책을 채택한다.

1. 기존 `BaselineSummaryGenerator` warning threshold 는 그대로 둔다.
2. 기존 `warning-count`는 전역 coarse warning 으로 유지한다.
3. runner/profile scoped 판단은 별도 `envelope comparison` artifact 로 분리한다.
4. envelope comparison 은 초기에는 process failure, CI failure, warning-as-failure 로 승격하지 않는다.
5. reference envelope 는 `history.json`과 그 history 가 참조하는 session `summary.json`들에서 계산한다.
6. candidate 는 `summary.json` 또는 `history.json`을 허용하되, history 입력이면 그 history 가 참조하는 summary 들을 다시 읽는다.

## 비교 key

Envelope comparison 은 아래 key 가 모두 같을 때만 metric 비교를 수행한다.

- `benchmark-profile`
- `runner-id`
- `runner-kind`
- `transport-backend`
- `os-description`
- `os-architecture`
- `process-architecture`
- `framework-description`
- `cases[]`
  - `result-name`
  - `scenario`
  - `payload-bytes`
  - `target-rate-hz`
  - `target-duration-seconds`

이 key 는 기존 D080 `comparison-key`를 재사용한다.
reference/candidate 중 하나라도 `comparison-compatible=false`이거나 key 가 없으면 metric 비교를 하지 않고
`envelope-compatible=false`와 mismatch signal 만 남긴다.

이 정책은 runner-local 비교를 명확하게 만든다.
예를 들어 `local-win-x64-01` reference 는 `ci-windows-x64-01`, RIO, UDP artifact 와 직접 비교하지 않는다.

## Envelope 계산 규칙

Reference history 가 가리키는 compatible session summary 들만 envelope 계산에 포함한다.

- `hard-passed=false`인 reference summary 는 제외한다.
- `comparison-compatible=false`인 reference summary 는 제외한다.
- 포함 가능한 summary 가 없으면 envelope artifact 는 `envelope-compatible=false`로 남긴다.

Metric 방향:

| metric | 방향 | reference aggregate |
| --- | --- | --- |
| p50 max us | 낮을수록 좋음 | max |
| p99 max us | 낮을수록 좋음 | max |
| p99 median max us | 낮을수록 좋음 | max |
| p99 growth ratio max | 낮을수록 좋음 | max |
| actual rate min hz | 높을수록 좋음 | min |
| TCP HWM max | 낮을수록 좋음 | max |
| dropped total | 0 유지 | sum |
| payload error total | 0 유지 | sum |
| pool rented max | 0 유지 | max |

초기 signal limit 은 reference aggregate 에 완충 폭을 붙인 값이다.
이 값은 hard SLO 가 아니라 local runner regression review 를 돕는 signal limit 이다.

| metric | limit |
| --- | --- |
| p50/p99/p99 median | `max(reference * 1.20, reference + 100us)` |
| p99 growth ratio | `reference + 0.25` |
| actual rate | `max(95Hz, reference - 1Hz)` |
| TCP HWM | `reference + 2` |
| dropped total | `0` |
| payload error total | `0` |
| pool rented max | `0` |

이 완충 폭은 첫 구현의 기본값이다. runner 별 evidence 가 더 쌓이면 별도 결정으로 조정한다.

## 새 command shape

첫 구현 command 는 기존 summary/history 생성을 변경하지 않는 독립 command 로 둔다.

```text
Hps.Benchmarks --compare-baseline-envelope <candidate-json> --reference-history <reference-history-json> --envelope <output-json> [--envelope-md <output-md>]
```

- `<candidate-json>`은 `summary-version: 1` summary 또는 `history-version: 1` history 다.
- `<reference-history-json>`은 `history-version: 1` history 다.
- history 안의 relative `summary-path`는 history 파일이 있는 directory 를 기준으로 해석한다.
- Markdown 은 JSON과 같은 model 에서 파생되는 human-readable 보조 artifact 다.
- command 는 artifact 생성 성공 시 exit code 0을 반환한다.
- envelope signal 이 있어도 exit code 를 바꾸지 않는다.
- JSON parse 실패, schema mismatch, file write 실패, usage error 는 exit code 2를 반환한다.

## Envelope artifact schema v1

JSON top-level:

```json
{
  "envelope-version": 1,
  "reference-history-path": ".../history.json",
  "candidate-path": ".../summary.json",
  "candidate-kind": "summary",
  "reference-summary-count": 9,
  "candidate-summary-count": 1,
  "envelope-compatible": true,
  "envelope-signal-count": 0,
  "reference-key": {},
  "candidate-key": {},
  "envelope-mismatches": [],
  "signals": [],
  "by-kind": {}
}
```

`by-kind` 안의 metric row 는 reference, limit, candidate, direction, signaled 를 함께 쓴다.

```json
{
  "load": {
    "p99-max-us": {
      "direction": "upper",
      "reference": 935.6,
      "limit": 1122.72,
      "candidate": 884.6,
      "signaled": false
    }
  }
}
```

Mismatch 예:

```json
{
  "code": "envelope-key-mismatch",
  "field": "runner-id",
  "expected": "local-win-x64-01",
  "actual": "ci-windows-x64-01"
}
```

Signal 예:

```json
{
  "code": "envelope-upper-bound-exceeded",
  "kind": "open-loop",
  "metric": "p99-max-us",
  "reference": 1077.4,
  "limit": 1292.88,
  "candidate": 1450.0
}
```

`envelope-signal-count`는 `warning-count`와 다르다.
기존 summary/history warning 에 합산하지 않는다.

## Markdown output

Markdown 은 다음 섹션을 가진다.

1. `# Baseline Envelope Comparison`
2. reference/candidate path, source count, compatible 여부, signal count
3. 비교 key 요약
4. kind별 metric table
5. mismatch table
6. signal table

Markdown 은 리뷰용이다. 자동화의 canonical input 은 JSON이다.

## Backward compatibility

- 기존 raw report schema 는 변경하지 않는다.
- 기존 `summary.json`/`history.json` schema version 은 변경하지 않는다.
- 기존 `warning-count`와 `hard-passed` 의미는 변경하지 않는다.
- 기존 `--summarize-baseline`/`--summarize-baseline-history` command output 과 exit code 는 변경하지 않는다.
- envelope artifact 는 새 command 로만 생성한다.

## Failure policy

Envelope comparison 은 report-only 다.

- `envelope-compatible=false`는 비교군이 맞지 않다는 신호이지 process failure 가 아니다.
- `envelope-signal-count > 0`은 regression review signal 이지 process failure 가 아니다.
- CI에서 이 command 를 쓰더라도 초기에는 artifact upload 를 위한 보조 산출물로만 쓴다.
- warning-as-failure 또는 latency hard gate 승격은 별도 D126 이후에만 가능하다.

## 첫 구현 경계

첫 구현은 다음 순서가 적절하다.

1. Parser/model: 새 command 를 인식하고 usage error 를 고정한다.
2. Artifact reader: summary/history JSON을 envelope source 로 읽고, history 의 session summary path 를 해석한다.
3. Envelope model/generator/writer: reference envelope, candidate aggregate, mismatch/signal 계산과 JSON writer 를 추가한다.
4. Markdown writer/Program wiring: CLI smoke 와 사람 리뷰용 Markdown 을 연결한다.
5. State docs: D125 구현 완료와 남은 gate 승격 보류 상태를 기록한다.

## 범위 밖

- `BaselineSummaryGenerator` 전역 threshold 상수 변경
- `warning-count` 의미 변경
- warning-as-failure 구현
- CI latency hard gate 구현
- CI artifact 자동 채택
- reference envelope 값을 별도 repository artifact 로 미리 materialize 하는 기능
- percentile/statistical model 도입

## 검증 계획

설계 단계 검증:

- D124와 충돌하지 않는지 확인한다.
- existing summary/history schema 에 없는 값을 요구하지 않는지 확인한다.
- 새 command 가 기존 command output/exit code 를 바꾸지 않는지 확인한다.
- placeholder 또는 미완성 문구, 상충 문구를 검색한다.

구현 단계 검증:

- parser focused tests
- artifact reader focused tests
- envelope generator focused tests
- JSON/Markdown writer focused tests
- Program CLI smoke
- `dotnet build HighPerformanceSocket.slnx --no-restore`
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`
- `git diff --check`
