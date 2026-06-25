# Phase 4 explicit runner 3-session 이후 다음 후보 재평가

- 날짜: 2026-06-25
- 상태: Accepted
- 관련 결정: D082, D083, D084, D085
- 관련 artifact:
  - `docs/benchmarks/baselines/index.md`
  - `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/history.json`
  - `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/history.md`
- 관련 검토:
  - `.claude/review/2026-06-18-repeat-baseline-policy-review.md`
  - `.claude/review/2026-06-24-latency-envelope-and-gate-deferral-design-review.md`

## 목적

`local-win-x64-01/2026-06-24` explicit runner baseline 이 3-session reference 를 갖췄다.
이번 문서는 그 다음 Phase 4 작업을 고른다. 핵심 질문은 다음 두 가지다.

1. 같은 runner 의 date root 를 더 쌓을 것인가.
2. 지금 CI/warning-as-failure 설계로 넘어갈 것인가.

## 확인된 사실

- `local-win-x64-01/2026-06-24/history.json`은 `session-count=3`, `hard-passed=true`,
  `warning-count=0`, `comparison-compatible=true`, unknown runner 0, mismatch 0 이다.
- explicit runner reference envelope 는 load p99 max 870.7 us, open-loop p99 max 1051.5 us 다.
- dropped total, payload error total, pool rented max 는 모두 0 이다.
- D082는 warning-as-failure 또는 CI latency gate 승격 전에 명시 runner id 와 여러 날짜 root 의 compatible baseline 을 요구한다.
- 현재 explicit runner date root 는 `2026-06-24` 하나뿐이다.
- 기존 top-level `2026-06-24` baseline 은 `runner-id=local-unspecified`라 gate 승격 표본 count 에 산입하지 않는다.

## 후보

### 후보 A: 같은 runner 의 다음 date root 를 수집한다

권장한다. 현재 부족한 것은 같은 runner 의 날짜 다양성이다. 다음 date root 를 만들면 D082의 gate 승격 조건에 필요한
근거가 하나 늘어난다. 첫 단위는 `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/session-01/`
수집으로 제한한다.

장점:
- 현재 artifact schema 와 history command 를 그대로 재사용한다.
- CI/warning-as-failure 설계 전 필요한 실제 근거를 늘린다.
- 실패하더라도 영향 범위가 특정 session artifact 와 root 상태 문서로 제한된다.

단점:
- benchmark 실행 시간이 든다.
- date root 하나가 추가되어도 아직 gate 승격 조건은 완전히 충족하지 않는다.

### 후보 B: CI/warning-as-failure 설계를 바로 시작한다

아직 이르다. explicit runner date root 가 하나뿐이라 gate 입력 표본이 부족하다. 지금 설계하면 임계값과 적용 범위를
다시 바꿀 가능성이 높다.

### 후보 C: RIO/io_uring backend 로 넘어간다

아직 이르다. Phase 4의 SAEA 기준선과 runner/date root 기준이 더 단단해진 뒤 backend 비교로 넘어가야 한다.
지금 시작하면 성능 비교 기준과 backend 구현 검증이 동시에 흔들린다.

## 결정

다음 단일 작업 단위는 **`local-win-x64-01/2026-06-25/session-01` explicit runner baseline 수집**으로 둔다.

이 결정은 CI/warning-as-failure 를 포기한다는 뜻이 아니다. 같은 runner 의 date root 를 더 쌓아 gate 승격 판단의
근거를 확보한 뒤, warning-as-failure 와 CI workflow 설계를 다시 평가한다.

## 다음 작업 단위

1. `HPS_BENCHMARK_RUNNER_ID=local-win-x64-01`, `HPS_BENCHMARK_RUNNER_KIND=local`로
   `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/session-01/`에 `--baseline-suite --runs 3`을 실행한다.
2. 같은 session directory 에 `summary.json`과 `summary.md`를 생성한다.
3. `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/history.json`과 `history.md`를 생성한다.
4. `docs/benchmarks/baselines/index.md`에 runner/date history row 와 session row 를 추가한다.
5. `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`를 갱신한다.

## 범위 밖

- CI workflow 작성
- warning-as-failure 또는 latency hard gate 구현
- RIO/io_uring backend 착수
- 기존 `2026-06-18`, top-level `2026-06-24`, `local-win-x64-01/2026-06-24` artifact 수정
- `BaselineHistoryReader` recursive runner discovery 확장

## 검증 계획

- `--baseline-suite`: raw report 6개와 baseline-suite-result pass 확인
- `--summarize-baseline`: source-report-count 6, hard-passed true, warning-count 확인
- `--summarize-baseline-history`: session-count 1, hard-passed true, warning-count 확인
- artifact local absolute path 검색
- `Hps.Benchmarks.Tests`
- `git diff --check`
- solution build/test
