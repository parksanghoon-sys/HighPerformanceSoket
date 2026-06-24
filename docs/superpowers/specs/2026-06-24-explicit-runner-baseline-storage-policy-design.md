# Explicit runner baseline 저장 구조와 수집 정책 설계

- 날짜: 2026-06-24
- 상태: Accepted
- 관련 결정: D079, D080, D082, D083, D084
- 관련 코드:
  - `tests/Hps.Benchmarks/BenchmarkRunIdentity.cs`
  - `tests/Hps.Benchmarks/BaselineHistoryReader.cs`
- 관련 artifact:
  - `docs/benchmarks/baselines/index.md`
  - `docs/benchmarks/baselines/2026-06-24/`
  - `docs/superpowers/specs/2026-06-24-phase4-next-candidate-reassessment.md`

## 목적

D082는 2026-06-24 `local-unspecified` baseline 3개를 reference latency envelope 로만 쓰고,
warning-as-failure 와 CI latency failure 는 보류하기로 했다. D083은 명시적 runner id baseline 을 기존
`2026-06-24/session-04`처럼 섞지 말고, 저장 구조 정책을 먼저 정하라고 했다.

이번 설계는 명시적 `HPS_BENCHMARK_RUNNER_ID`로 생성한 baseline 을 어디에 저장하고, 어떤 root 를 history 입력으로
쓸지 확정한다. 코드 변경이나 새 측정 artifact 생성은 이번 범위가 아니다.

## 현재 제약

- `BenchmarkRunIdentity.CaptureDefault()`는 `HPS_BENCHMARK_RUNNER_ID`와 `HPS_BENCHMARK_RUNNER_KIND`가 없으면
  `runner-id=local-unspecified`, `runner-kind=local`을 기록한다.
- `BaselineHistoryReader.ReadSessions(...)`는 입력 root 가 날짜 directory 이면 그 date root 의 `summary.json`과
  `session-NN/summary.json`을 읽는다.
- 입력 root 가 날짜 directory 가 아니면 바로 아래의 `YYYY-MM-DD` directory 만 찾고, 각 date root 안의 session 을 읽는다.
- 현재 `docs/benchmarks/baselines/2026-06-24/`는 이미 `local-unspecified` session 3개로 comparison-compatible 하다.
- 같은 date root 에 명시적 runner id session 을 추가하면 history comparison 이 의도적 mismatch 를 기록한다.

## 검토한 저장 구조

### 선택지 A: 기존 top-level date root 에 계속 session 을 추가

사용하지 않는다. `docs/benchmarks/baselines/2026-06-24/session-04/`에 명시적 runner id 결과를 추가하면
기존 `local-unspecified` session 과 섞인다. 이 경우 `comparison-compatible=false`는 정확한 신호지만,
그 date root 가 reference envelope 과 explicit runner baseline 중 무엇을 의미하는지 애매해진다.

### 선택지 B: date root 아래 runner group 을 둔다

예: `docs/benchmarks/baselines/2026-06-24/runners/<runner-id>/session-01/`

현재 reader 는 date root 아래에서 `session-NN`만 읽고 `runners/` directory 는 무시한다. 따라서 이 구조를 쓰려면
history reader 확장이 먼저 필요하다. 또한 date root 안에 서로 다른 runner group 이 섞여 index 와 history output 의
소유 경계가 흐려진다.

### 선택지 C: runner group 아래 date root 를 둔다

예: `docs/benchmarks/baselines/runners/<runner-id>/2026-06-24/session-01/`

채택한다. 이 구조는 현재 reader 의 parent-root discovery 규칙과 맞는다. runner root 를 입력하면 바로 아래의
`YYYY-MM-DD` date roots 를 읽을 수 있고, date root 를 입력하면 해당 날짜의 session 만 읽을 수 있다.
기존 top-level `YYYY-MM-DD` roots 는 그대로 legacy/local-unspecified baseline 으로 남길 수 있다.

## 결정

명시적 runner id baseline 은 아래 구조로 저장한다.

```text
docs/benchmarks/baselines/
  YYYY-MM-DD/                              # legacy 또는 local-unspecified date root
  runners/
    <runner-id>/
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
        history.json                      # 해당 runner/date 의 date-level history
        history.md
      history.json                        # 선택: 해당 runner 의 multi-date history
      history.md
```

이 구조에서 `docs/benchmarks/baselines/runners/<runner-id>/`는 runner root 다.
현재 `BaselineHistoryReader`는 이 root 를 parent root 로 해석하고, 바로 아래 `YYYY-MM-DD` directories 를 읽는다.
따라서 첫 explicit runner 수집은 reader 코드 변경 없이 진행할 수 있다.

## runner id naming guide

`HPS_BENCHMARK_RUNNER_ID`는 비교 가능성을 가르는 장기 식별자다. privacy 를 위해 host name, user name, IP address,
회사 내부 장비명은 자동 수집하지 않고 직접 넣지도 않는다.

권장 형식은 lowercase ASCII, 숫자, hyphen 만 쓰는 stable token 이다.

권장 예:

- `local-win-x64-01`
- `ci-windows-x64-01`
- `lab-linux-x64-01`

피해야 할 예:

- 실제 PC hostname
- 사용자 계정명
- 사내 자산 번호
- IP address 또는 MAC address
- 공백, 한글, slash, backslash 가 포함된 값

runner directory name 은 `runner-id`와 같게 둔다. 즉 `HPS_BENCHMARK_RUNNER_ID=local-win-x64-01`로 실행한 baseline 은
`docs/benchmarks/baselines/runners/local-win-x64-01/...` 아래에 둔다. 이 규칙은 사람이 path 만 봐도 어떤 runner 의
baseline 인지 확인하기 위한 것이다.

`HPS_BENCHMARK_RUNNER_KIND`는 당분간 `local` 또는 `ci`만 쓴다. 더 세분화된 kind 가 필요해지면 별도 결정으로 추가한다.

## 수집 절차

첫 explicit runner baseline 은 다음 절차로 만든다.

1. `HPS_BENCHMARK_RUNNER_ID`를 privacy-safe stable token 으로 설정한다.
2. 필요하면 `HPS_BENCHMARK_RUNNER_KIND`를 `local` 또는 `ci`로 설정한다.
3. `docs/benchmarks/baselines/runners/<runner-id>/<YYYY-MM-DD>/session-01/`에 `--baseline-suite` 결과를 저장한다.
4. 같은 session directory 에 `summary.json`과 `summary.md`를 생성한다.
5. date root 에 `history.json`과 `history.md`를 생성한다.
6. 같은 runner 에 날짜 root 가 2개 이상 생기면 runner root 에 multi-date `history.json`/`history.md`를 선택적으로 생성한다.
7. `docs/benchmarks/baselines/index.md`에 runner group, date-level history, session row 를 추가한다.

첫 수집 단위는 `session-01` 하나만 만든다. D082의 warning-as-failure 승격 조건은 "서로 다른 날짜 root 3개 이상,
date root 당 compatible session 3개 이상"이므로, 첫 수집은 gate 승격이 아니라 저장 구조 검증과 artifact 경로 검증이 목적이다.

## history 입력 규칙

- 특정 runner/date 만 집계할 때:
  - 입력: `docs/benchmarks/baselines/runners/<runner-id>/<YYYY-MM-DD>`
  - 출력: 같은 date root 의 `history.json`, `history.md`
- 특정 runner 의 여러 날짜를 집계할 때:
  - 입력: `docs/benchmarks/baselines/runners/<runner-id>`
  - 출력: runner root 의 `history.json`, `history.md`
- 기존 top-level local-unspecified baseline 을 집계할 때:
  - 입력: `docs/benchmarks/baselines/<YYYY-MM-DD>` 또는 `docs/benchmarks/baselines`
  - 출력: 기존 경로의 `history.json`, `history.md`

현재 reader 는 `runners/` 아래를 자동으로 재귀 탐색하지 않는다. 따라서 `docs/benchmarks/baselines`를 입력으로
runner group 까지 모두 읽는 기능은 이번 설계에 포함하지 않는다. 전체 runner 를 한 번에 모으는 cross-runner index 나
aggregate command 가 필요해지면 별도 설계로 다룬다.

## index 운영 정책

`docs/benchmarks/baselines/index.md`는 canonical data source 가 아니라 사람이 경로와 상태를 찾는 entry point 다.
새 explicit runner baseline 이 생기면 다음을 추가한다.

- runner group 목록: runner id, runner kind, profile, transport backend, 최신 date root, 비고
- runner/date history row: `runners/<runner-id>/<YYYY-MM-DD>/history.json`
- session row: `runners/<runner-id>/<YYYY-MM-DD>/session-NN/summary.json`
- 해석 메모: 첫 explicit runner baseline 은 gate 승격 표본이 아니라 저장 구조 검증 표본이라는 점

기존 `2026-06-24` top-level row 는 이동하지 않는다. 이 row 는 `local-unspecified` reference envelope 근거로 유지한다.

## 범위 밖

- explicit runner baseline 실제 수집
- `BaselineHistoryReader`의 recursive runner discovery 구현
- 전체 runner 를 한 번에 비교하는 cross-runner aggregate
- CI workflow 작성
- warning-as-failure 또는 latency hard gate 구현
- 기존 top-level baseline artifact 이동 또는 이름 변경
- `BenchmarkRunIdentity` capture field 추가

## 다음 작업 단위

다음 단위는 첫 explicit runner baseline 을 새 구조에 수집하는 것이다.
권장 runner id 는 `local-win-x64-01`이다. 수집 전 현재 날짜 기준으로 runner root 와 date root 를 정하고,
이번 설계의 path 규칙을 그대로 적용한다.

## 검증 결과

이번 단위는 문서 정책 변경만 수행한다.

- `BaselineHistoryReader` directory 규칙과 채택한 runner-root 구조가 충돌하지 않는지 대조했다.
- D079/D080/D082/D083과 새 D084 문구가 서로 모순되지 않는지 확인했다.
- `docs/benchmarks/baselines/index.md` 운영 원칙이 새 구조를 설명하는지 확인했다.
- 신규 설계/결정/index 문서 임시 표기 검색 결과 없음.
- `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과를 확인했다.
