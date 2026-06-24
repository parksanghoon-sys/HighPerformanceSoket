# Phase 4 다음 실행 후보 재평가

- 날짜: 2026-06-24
- 상태: Accepted
- 관련 결정: D063, D069, D070, D071, D079, D080, D082, D083
- 관련 코드:
  - `tests/Hps.Benchmarks/BaselineHistoryReader.cs`
  - `tests/Hps.Benchmarks/BenchmarkRunIdentity.cs`
- 관련 artifact:
  - `docs/benchmarks/baselines/index.md`
  - `docs/benchmarks/baselines/2026-06-24/history.json`
  - `docs/superpowers/specs/2026-06-24-latency-envelope-and-gate-deferral-design.md`

## 목적

D082로 2026-06-24 compatible baseline 3개는 reference latency envelope 로만 쓰고,
hard latency gate, warning-as-failure, CI latency failure 는 계속 보류하기로 했다.
이 문서는 그 이후 Phase 4에서 바로 실행할 수 있는 다음 단일 작업 단위를 다시 고른다.

## 현재 확인된 사실

- D082 기준 2026-06-24 baseline 은 `runner-id=local-unspecified`라 gate 승격 표본 count 에 산입하지 않는다.
- warning-as-failure 전제 중 첫 번째는 명시적 `HPS_BENCHMARK_RUNNER_ID`로 생성한 baseline 이다.
- `BaselineHistoryReader`는 parent root 아래의 `YYYY-MM-DD` directory 와 date root 아래의 `session-NN` directory 만 읽는다.
- `docs/benchmarks/baselines/2026-06-24/`에는 이미 `local-unspecified` session 3개가 있다.
- 같은 date root 에 명시적 runner id session 을 `session-04`로 추가하면 `history.json`의 comparison key 가 달라져
  `comparison-compatible=false`가 된다.
- 현재 `TODOS.md` deferred backlog 에는 server-level diagnostics model 이 남아 있지만, D068에 따라 실제 host/metrics
  surface 가 생기기 전까지는 `P3_NICE`다.

## 검토한 후보

### 후보 A: 2026-06-24 date root 에 explicit runner baseline 을 바로 추가

권장하지 않는다. 기존 `session-01`~`session-03`은 `local-unspecified`이고 새 session 만 명시 runner id 를 가지면
date-level history 가 intentional mismatch 로 바뀐다. artifact 를 먼저 만들면 나중에 저장 구조를 다시 정리해야 한다.

### 후보 B: explicit runner baseline 저장 구조와 수집 정책을 먼저 설계

권장 방향이다. D082의 다음 전제로 명시 runner baseline 으로 가려면 먼저 directory grouping 과 history reader 입력 범위를
정해야 한다. 특히 runner id 를 date root 위에 둘지, date root 아래 runner group 을 둘지, 기존 date root 를 legacy 로 둘지,
history command 가 어떤 root 를 입력으로 받을지 정해야 한다.

### 후보 C: CI workflow 또는 warning-as-failure 구현

아직 이르다. explicit runner baseline 저장 구조가 없으면 CI artifact 를 어디에 보존할지, local baseline 과 CI baseline 을
어떻게 분리할지, 어떤 history 를 gate 입력으로 볼지 결정할 수 없다. D082의 보류 조건도 아직 충족하지 못했다.

### 후보 D: Phase 5/6 OS capability probe 또는 RIO/io_uring backend 착수

범위가 너무 크다. Phase 4 benchmark artifact 구조가 explicit runner 기준으로 정리되기 전에 backend 별 latency 비교 기준을
만들기 어렵다. 지금 착수하면 검증 단위가 커지고 현재 Phase 4 흐름을 건너뛰게 된다.

### 후보 E: server-level diagnostics model 설계

현재 유일한 deferred backlog 이지만 `P3_NICE`다. D068로 v1 단순 pass-through API 는 추가하지 않기로 했고, 실제 host/metrics
surface 요구가 아직 없다. 지금 올리면 사용처 없이 API를 넓힐 위험이 있다.

## 결정

다음 단일 작업 단위는 **explicit runner baseline 저장 구조와 수집 정책 설계**로 둔다.

이번 결정은 코드 변경이나 artifact 생성이 아니다. 다음 문서 단위에서 아래 질문을 닫은 뒤 명시 runner baseline 수집 또는
history reader 확장을 진행한다.

- 명시 runner baseline directory 를 `docs/benchmarks/baselines/<runner-group>/YYYY-MM-DD/session-NN/`처럼 runner 를 date 위에 둘지,
  다른 구조를 쓸지.
- 기존 `docs/benchmarks/baselines/YYYY-MM-DD/` artifact 를 legacy/local-unspecified 로 그대로 둘지.
- `BaselineHistoryReader`가 runner group root 를 지원해야 하는지, 아니면 runner group 안의 date root 만 입력으로 받을지.
- `docs/benchmarks/baselines/index.md`를 runner group 단위로 어떻게 분리할지.
- `HPS_BENCHMARK_RUNNER_ID` 예시 값이 privacy 를 해치지 않으면서도 장기적으로 stable 해야 하므로 어떤 naming guide 를 둘지.

## 다음 작업 단위

다음 작업은 `docs/superpowers/specs/2026-06-24-explicit-runner-baseline-storage-policy-design.md`를 작성하는 것이다.
이 작업은 문서 전용 batch 로 진행하고, 코드와 새 benchmark artifact 는 만들지 않는다.

## 범위 밖

- explicit runner baseline 실제 수집
- `BaselineHistoryReader` 코드 변경
- CI workflow 작성
- warning-as-failure 또는 latency hard gate 구현
- RIO/io_uring backend 착수
- server-level diagnostics public API 설계

## 검증 결과

- D082, D079, D080, `BaselineHistoryReader`의 현재 directory 가정을 대조했다.
- `.claude/review/review-status-2026-06-18.md`의 남은 비차단 후속과 현재 결정 문서를 대조했다.
- `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`를 같은 다음 진입점으로 맞췄다.
- 신규 설계/결정 문서 임시 표기 검색 결과 매치가 없었다.
- `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과를 확인했다.
