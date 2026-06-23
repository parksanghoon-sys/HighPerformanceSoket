# Baseline history report command 설계

- 날짜: 2026-06-23
- 상태: Draft for review
- 관련 결정: D069, D070, D071
- 관련 코드: `tests/Hps.Benchmarks`, `tests/Hps.Benchmarks.Tests`
- 관련 artifact: `docs/benchmarks/baselines/index.md`, `docs/benchmarks/baselines/2026-06-18/**/summary.json`

## 목적

현재 Phase 4는 per-run raw JSON, per-session `summary.json`, 사람이 읽는 `summary.md`, 수동 `docs/benchmarks/baselines/index.md`까지 갖고 있다.
다음으로 필요한 것은 CI workflow 가 아니라, 여러 session summary 를 한 번에 읽어 추세 검토용 history artifact 를 만드는 provider-independent command 다.

이 설계의 목표는 다음 구현 단위를 작게 고정하는 것이다.

- 기존 `--summarize-baseline` output 을 재사용한다.
- warning 은 계속 soft signal 로 둔다.
- hard failure 는 기존 summary 의 delivery/drop/leak 결과만 aggregate 한다.
- GitHub Actions, self-hosted runner, warning-as-failure, latency hard gate 는 이번 범위에서 제외한다.

## 현재 확인된 사실

- `--baseline-suite <output-dir> [--runs <count>]`는 per-run raw JSON 을 session directory 에 남긴다.
- `--summarize-baseline <input-dir> --summary <output-json> [--summary-md <output-md>]`는 session 하나를 요약한다.
- summary JSON schema 는 `summary-version`, `source-report-count`, `hard-passed`, `warning-count`, `warnings`, `by-kind`를 가진다.
- 현재 수동 index 는 세 session 의 summary path, hard/warning 상태, p99/HWM 대표값을 한 표로 모은다.
- D071은 history index 를 먼저 두고, warning-as-failure 는 같은 runner baseline 이 더 쌓인 뒤 별도 결정한다고 정리했다.

## 검토한 접근

### A. `docs/benchmarks/baselines/index.md`를 계속 수동 갱신

가장 작지만 새 session 이 생길 때마다 사람이 summary JSON 값을 읽어 표를 고쳐야 한다.
이미 summary JSON schema 가 있으므로, 이 방식은 재발견 비용과 전사 오류를 남긴다.

### B. `--summarize-baseline-history` command 추가

권장 방향이다. baseline root 아래의 session summary JSON 들을 읽어 `history.json`과 선택적 `history.md`를 만든다.
기존 raw JSON과 per-session summary 는 그대로 두고, history 는 파생 artifact 로만 취급한다.

장점:

- CI provider 와 독립적이다.
- current `index.md` 표를 code-generated artifact 로 재현할 수 있다.
- warning-as-failure 없이도 여러 session 의 hard/warning 상태를 한 번에 확인할 수 있다.
- 기존 `BenchmarkCommandParser`, `BaselineSummaryWriter`, `BaselineSummaryMarkdownWriter` 패턴을 그대로 따른다.

단점:

- runner identity/environment metadata 가 아직 없으므로, latency regression 자동 판정은 여전히 할 수 없다.
- root directory 가 `session-01` 역할을 겸하는 2026-06-18 예외를 reader 규칙에 명시해야 한다.

### C. CI workflow와 warning-as-failure를 바로 구현

지금은 보류한다. 같은 runner identity, 날짜가 다른 session, 환경 메타데이터가 충분하지 않아 false negative 위험이 크다.

## 결정

다음 구현 후보는 B를 따른다.

새 CLI command 이름은 다음 형태로 둔다.

```text
Hps.Benchmarks --summarize-baseline-history <baseline-root> --history <output-json> [--history-md <output-md>]
```

명명 이유:

- `--summarize-baseline`은 session 하나를 요약한다.
- `--summarize-baseline-history`는 여러 session summary 를 다시 요약한다.
- `--history`는 machine-readable JSON output 이고, `--history-md`는 사람이 보는 보조 artifact 다.

## History input 규칙

입력 root 는 `docs/benchmarks/baselines` 또는 특정 날짜 directory 둘 다 허용한다.

reader 는 아래 summary 를 대상으로 삼는다.

- `<YYYY-MM-DD>/summary.json`: 과거 구현 흐름 때문에 root 가 `session-01(root)` 역할을 하는 경우.
- `<YYYY-MM-DD>/session-NN/summary.json`: 권장 session directory 구조.

무제한 recursive scan 은 하지 않는다. 이유는 같은 directory 안에 생성된 `history.json`, 임시 비교 artifact, 다른 날짜 하위 복사본이 섞일 수 있기 때문이다.

초기 구현의 session identity 규칙:

- 날짜: summary path 상위의 `YYYY-MM-DD` directory 이름.
- session: `session-NN` directory 이름. 날짜 root 의 `summary.json`은 `session-01(root)`로 표시한다.
- summary path: 입력 root 기준 상대 경로를 `/` separator 로 저장한다.
- human report path: 같은 directory 의 `summary.md`가 있으면 상대 경로로 저장하고, 없으면 null 로 둔다.

## History output JSON 초안

`history.json`은 summary JSON을 대체하지 않는다. 여러 summary 를 찾기 위한 aggregate artifact 다.

필드:

- `history-version`: 1
- `source-root`
- `session-count`
- `hard-passed`: 모든 session summary 의 `hard-passed`가 true 일 때 true
- `hard-failure-count`
- `warning-count`
- `sessions`

각 session entry:

- `date`
- `session`
- `summary-path`
- `human-report-path`
- `source-report-count`
- `hard-passed`
- `warning-count`
- `load-p99-max-us`
- `open-loop-p99-max-us`
- `tcp-hwm-max`

`tcp-hwm-max`는 `by-kind.load.tcp-hwm-max`와 `by-kind.open-loop.tcp-hwm-max` 중 큰 값이다.
load 또는 open-loop summary 가 없으면 해당 p99 값은 0으로 둔다. 현재 schema 는 양쪽 runner 를 모두 기대하지만,
부분 artifact 를 읽을 때도 history command 가 crash 하지 않고 결함을 history 값으로 드러내기 위함이다.

## Markdown output 초안

`history.md`는 현재 `docs/benchmarks/baselines/index.md`와 같은 정보를 표로 제공한다.

필수 내용:

- source root
- session count
- hard passed
- warning count
- session table
- warning 이 있는 session list

초기 구현은 `index.md`를 자동 갱신하지 않는다. command 사용자가 원하면 `--history-md docs/benchmarks/baselines/index.generated.md`처럼 별도 파일을 만들고,
검토 후 사람이 `index.md` 정책을 바꿀 수 있다.

## Exit code 정책

- 모든 summary 의 `hard-passed == true`: exit code 0.
- 하나라도 `hard-passed == false`: exit code 1.
- input path 없음, summary 없음, JSON read/write 오류, usage error: exit code 2.
- `warning-count > 0`: exit code 에 영향을 주지 않는다.

이 정책은 기존 summary command 의 hard gate 의미와 맞춘다. warning-as-failure 는 별도 옵션으로도 아직 추가하지 않는다.

## 구현 단위 분해

### Task 1: Parser contract

- `BenchmarkCommand.HistoryBaseline` 또는 `SummarizeBaselineHistory` 값을 추가한다.
- `BenchmarkCommandLine`에 `HistoryInputRoot`, `HistoryOutputPath`, `HistoryMarkdownOutputPath`를 추가한다.
- `BenchmarkCommandParserTests`에 command parsing, missing `--history`, invalid `--history-md`, `--report` 혼용 거부 테스트를 추가한다.
- `Program.PrintUsage`만 갱신하고 실제 실행은 아직 연결하지 않는다.

### Task 2: History domain + reader

- `BaselineHistorySession`, `BaselineHistory` 값을 추가한다.
- `BaselineSummaryReader` 또는 `BaselineHistoryReader`를 추가해 summary JSON schema v1을 읽는다.
- fake directory 기반 테스트로 날짜 root summary 와 `session-NN/summary.json` discovery 를 검증한다.

### Task 3: History generator + writer

- summary entries 를 history aggregate 로 변환한다.
- JSON writer 와 Markdown writer 를 분리한다.
- tests 는 hard-passed aggregation, warning-count 합산, p99/HWM 대표값, generated Markdown table 을 검증한다.

### Task 4: Program wiring + smoke

- `Program`에 `--summarize-baseline-history` 실행 경로를 연결한다.
- 현재 `docs/benchmarks/baselines` 또는 `docs/benchmarks/baselines/2026-06-18`로 CLI smoke 를 수행한다.
- smoke artifact 는 commit 하지 않는다.

각 Task 는 별도 커밋으로 나눈다. 다음 실제 구현은 Task 1만 먼저 진행한다.

## 범위 밖

- CI provider workflow 작성
- warning-as-failure 옵션
- latency hard threshold
- baseline 간 regression 판정
- runner identity/environment metadata schema
- 기존 `index.md` 자동 덮어쓰기
- per-run raw JSON schema 변경

## 검증 계획

설계 단위 검증:

- 기존 benchmark CLI/parser/summary 타입과 충돌하지 않는지 source 대조.
- `docs/benchmarks/baselines/index.md`와 D071 정책이 같은 방향인지 확인.
- placeholder, TODO, 미정 항목, 범위 누수가 없는지 self-review.
- `git diff --check`.
- 문서 변경이지만 현재 repo 관례에 맞춰 solution build/test 를 실행한다.

## Self-review

- Placeholder scan: 미정 placeholder 없음.
- Internal consistency: session 하나 summary 와 history aggregate 역할을 분리했다.
- Scope check: CI workflow, warning-as-failure, regression 판정은 제외했다.
- Ambiguity check: root summary 예외, session discovery depth, exit code, output key 를 명시했다.
