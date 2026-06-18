# Baseline report history 와 warning 승격 정책 설계

- 날짜: 2026-06-18
- 상태: Accepted
- 관련 결정: D063, D069, D070, D071
- 관련 artifact:
  - `docs/benchmarks/baselines/2026-06-18/summary.json`
  - `docs/benchmarks/baselines/2026-06-18/summary.md`
  - `docs/benchmarks/baselines/2026-06-18/session-02/summary.json`
  - `docs/benchmarks/baselines/2026-06-18/session-02/summary.md`
  - `docs/benchmarks/baselines/2026-06-18/session-03/summary.json`
  - `docs/benchmarks/baselines/2026-06-18/session-03/summary.md`

## 목적

D070 이후 baseline summary JSON 과 Markdown artifact 는 존재한다. 하지만 이 artifact 를 운영할 때 어떤 이력을
보존하고, soft warning 을 언제 실패 조건으로 승격할지는 아직 확정되지 않았다.

이번 설계는 CI provider workflow 를 바로 만들지 않고, 다음 구현자가 같은 기준으로 report history 와 warning 정책을
확장할 수 있도록 provider-independent 한 규칙을 먼저 정한다.

## 현재 확인된 사실

- `--baseline-suite`는 closed-loop/load 와 open-loop run 결과를 raw JSON 으로 남긴다.
- `--summarize-baseline <input-dir> --summary <output-json> [--summary-md <output-md>]`는 같은 directory 의 raw JSON 을
  읽어 summary JSON 과 사람이 읽는 Markdown 을 생성한다.
- summary JSON 은 자동화 입력이며, summary Markdown 은 리뷰용 보조 artifact 다.
- 현재 hard gate 는 delivery/drop/leak 기준이다. latency, p99 growth, high-watermark 는 soft warning 으로만 기록한다.
- 2026-06-18 기준 root, `session-02`, `session-03` baseline 에 summary JSON/Markdown 이 모두 존재한다.

## 검토한 선택지

### 선택지 A: 지금 CI workflow 와 warning-as-failure 를 함께 구현

자동화 범위가 넓어지지만, runner 종류와 artifact 보존 위치를 아직 모른다. 같은 코드라도 GitHub Actions, self-hosted
runner, 로컬 nightly 에서 baseline 변동성이 다르므로 지금 실패 정책까지 고정하면 false negative 위험이 크다.

### 선택지 B: report history 규칙과 soft warning 정책만 먼저 고정

권장 방향이다. 현재 존재하는 raw JSON, summary JSON, summary Markdown 을 그대로 활용하고, 실패 승격 조건은 더 많은
동일 runner baseline 이 쌓인 뒤 결정한다. 다음 구현도 CI provider 에 묶이지 않은 작은 단위로 나눌 수 있다.

### 선택지 C: latency threshold 를 hard gate 로 바로 승격

D063/D069/D070과 충돌한다. 현재 수치는 단일 개발 장비의 반복 baseline 이며, p99 값은 session 간 변동이 크다. delivery,
drop, leak 이 이미 hard gate 이므로 latency hard gate 는 아직 보류한다.

## 결정 초안

v1 report history 는 **baseline session directory** 를 이력 단위로 본다.

- canonical source 는 per-run raw JSON 이다.
- `summary.json`은 자동화와 추세 비교를 위한 파생 artifact 다.
- `summary.md`는 사람 리뷰를 빠르게 하기 위한 파생 artifact 다.
- summary artifact 는 generator 개선으로 재생성될 수 있지만, raw JSON 은 가능한 변경하지 않는다.
- CI provider workflow 는 이번 범위에서 제외한다.

warning 정책은 계속 soft warning 으로 유지한다.

- `warning-count > 0`은 기본적으로 process failure 가 아니다.
- `hard-passed == false`는 기존 delivery/drop/leak 조건에 따라 실패로 본다.
- warning-as-failure 는 동일 runner 에서 충분한 반복 baseline 과 실행 환경 메타데이터가 쌓인 뒤 별도 결정한다.
- latency hard gate 는 아직 도입하지 않는다.

## Report history 규칙

### Directory 규칙

반복 baseline 은 아래 구조를 권장한다.

```text
docs/benchmarks/baselines/
  YYYY-MM-DD/
    session-01/
      load-01.json
      load-02.json
      load-03.json
      open-loop-01.json
      open-loop-02.json
      open-loop-03.json
      summary.json
      summary.md
    session-02/
      ...
```

현재 2026-06-18 baseline root directory 는 과거 구현 흐름 때문에 `session-01` 역할을 겸한다. 이후 새 session 은
명시적인 `session-NN` directory 를 사용해 root 와 session 의미가 섞이지 않게 한다.

### Artifact 역할

- raw JSON: 재현 가능한 원본 측정값이다. 비교와 재요약의 기준이다.
- `summary.json`: machine-readable summary 다. CI, local script, dashboard 가 읽는 대상이다.
- `summary.md`: human-readable review artifact 다. 자동 실패 판정의 기준으로 쓰지 않는다.

### 이력 비교 전제

서로 다른 장비, OS 상태, runner 종류, 전원 정책, 백그라운드 부하가 다른 baseline 은 latency 수치 비교 기준으로 바로
섞지 않는다. 비교 자동화를 하려면 먼저 runner identity 또는 environment metadata 를 raw report 나 summary 에 포함하는
별도 설계가 필요하다.

## Warning 승격 정책

초기 warning 은 review signal 로만 다룬다.

- p99, p50, p99 growth ratio, TCP/UDP high-watermark, actual rate warning 은 summary 에 기록한다.
- warning 이 생기면 해당 raw JSON 과 session context 를 함께 본다.
- warning 이 반복되더라도 이번 정책만으로는 실패 승격하지 않는다.

warning-as-failure 를 검토하려면 최소한 아래 조건이 먼저 필요하다.

- 같은 runner identity 에서 날짜가 다른 baseline session 이 3개 이상 있다.
- 각 session 은 closed-loop 3회와 open-loop 3회를 포함한다.
- 모든 session 의 delivery/drop/leak hard gate 는 통과한다.
- warning threshold 가 transient scheduling noise 와 실제 regression 을 구분할 만큼 안정적인지 검토한다.
- warning-as-failure 를 켜는 명시적 command option 또는 CI setting 이 별도 설계되어 있다.

## 다음 구현 후보

이 설계가 승인되면 다음 구현은 CI workflow 가 아니라 작은 provider-independent 단위로 시작한다.

1. `docs/benchmarks/baselines/index.md` 같은 수동/반자동 history index 를 추가해 현재 session 들의 summary 경로와
   hard/warning 상태를 한곳에 모은다.
2. 또는 `--summarize-baseline`과 별개로 여러 summary JSON 을 읽는 history report command 를 설계한다.

두 후보 중 첫 단위는 code risk 가 낮은 history index 가 더 적합하다. command 구현은 index 형식과 실제 소비 방식이
확정된 뒤 진행한다.

## 범위 밖

- CI provider 별 workflow 작성
- warning-as-failure command option 구현
- latency hard threshold 확정
- baseline 간 자동 regression 판정
- summary schema 변경
- runner/environment metadata schema 추가

## 검증 계획

이번 단위는 설계 문서와 상태 문서만 변경한다.

- 현재 baseline summary artifact 존재 여부를 확인한다.
- 문서 안에 미완성 표기, 내부 모순, scope 누수가 없는지 self-review 한다.
- `git diff --check`로 whitespace 오류를 확인한다.
- 코드 변경은 없지만 repository 상태 확인 차원에서 solution build/test 를 실행한다.
