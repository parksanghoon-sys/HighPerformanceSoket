# Phase 4 gate 승격 후보 재평가 설계

- 날짜: 2026-06-25
- 상태: Accepted
- 관련 결정: D063, D070, D080, D082, D084, D088, D089
- 관련 artifact:
  - `docs/benchmarks/baselines/index.md`
  - `docs/benchmarks/baselines/runners/local-win-x64-01/history.json`
  - `docs/benchmarks/baselines/runners/local-win-x64-01/history.md`
  - `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/history.json`
  - `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/history.json`
- 관련 검토:
  - `.claude/review/2026-06-18-repeat-baseline-policy-review.md`
  - `.claude/review/2026-06-24-latency-envelope-and-gate-deferral-design-review.md`

## 목적

`local-win-x64-01` runner 는 이제 두 개의 date root 를 가진다.

- `2026-06-24`: compatible session 3개
- `2026-06-25`: compatible session 3개

각 session 은 `load` 3회와 `open-loop` 3회 raw report 를 포함하고, runner root history 는
`session-count=6`, `hard-passed=true`, `warning-count=0`, `comparison-compatible=true`다.

이번 설계는 이 evidence 로 D082의 warning-as-failure 또는 CI latency gate 를 승격할 수 있는지 재평가하고,
다음 Phase 4 단일 작업 단위를 정한다.

## 현재 확인된 사실

- runner id 는 명시적이고 privacy-safe 한 `local-win-x64-01`이다.
- runner kind 는 `local`, benchmark profile 은 `tcp-loopback-saea-v1`, backend 는 `SaeaTransport`다.
- 두 date root 모두 `comparison-compatible=true`, `unknown-runner-count=0`, `comparison-mismatch-count=0`이다.
- runner root 전체 6개 session 에서 delivery/drop/leak hard gate 는 모두 통과했다.
- dropped total, payload error total, pool rented max 는 모두 0이다.
- explicit runner reference envelope 는 다음과 같다.

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

## D082 조건 대조

D082는 warning-as-failure 승격 전제 조건을 다음처럼 잡았다.

| 조건 | 현재 상태 | 판단 |
| --- | --- | --- |
| 명시적 runner id | `local-win-x64-01` | 충족 |
| 같은 runner/profile/workload 의 서로 다른 날짜 root 3개 이상 | `2026-06-24`, `2026-06-25` 2개 | 미충족 |
| 각 날짜 root 당 compatible session 3개 이상 | 두 date root 모두 3개 | 충족 |
| 각 session 이 load 3회/open-loop 3회 포함 | 모두 충족 | 충족 |
| delivery/drop/leak hard gate 통과 | 모두 통과 | 충족 |
| comparison-compatible true, unknown/mismatch 0 | 모두 충족 | 충족 |
| warning threshold 가 scheduling noise 와 regression 을 구분한다는 별도 검토 | 아직 없음 | 미충족 |

따라서 현재 evidence 는 **gate 승격 검토를 시작할 수 있는 수준**까지는 왔지만,
**warning-as-failure 또는 latency hard gate 를 켤 수준은 아니다.**

## 검토한 선택지

### 선택지 A: 지금 warning-as-failure 를 켠다

채택하지 않는다. date root 가 아직 2개라 D082의 날짜 다양성 조건을 충족하지 못한다.
또한 p99 max 가 open-loop 에서 1077.4 us 까지 관측되어 1 ms 같은 단순 threshold 는 여전히 현 baseline 을
정상 실행에서도 실패시킬 수 있다.

### 선택지 B: 지금 CI latency hard gate 를 작성한다

채택하지 않는다. CI runner identity, artifact 보존 위치, local baseline 과 CI baseline 의 분리 정책이 아직 없다.
CI에서 latency hard failure 를 켜면 로컬 runner evidence 와 CI runner noise 를 섞어 판단할 위험이 있다.

### 선택지 C: CI는 artifact-only 로 설계하고 latency 는 report-only 로 둔다

부분적으로 권장한다. CI가 필요하다면 첫 단계는 build/test 와 기존 delivery/drop/leak hard gate 만 실패 조건으로 삼고,
latency/HWM/warning 은 artifact 로 남기는 것이 안전하다. 다만 이번 단위는 CI workflow 구현이 아니라 gate 승격 재평가이므로,
CI artifact-only 설계는 다음 후보 중 하나로 둔다.

### 선택지 D: 다음 실제 날짜 root 를 추가로 수집한다

권장한다. D082의 미충족 조건 중 가장 분명한 것은 세 번째 date root 부재다.
단, 현재 날짜가 2026-06-25이므로 `2026-06-26` 같은 미래 date root 를 지금 만들면 baseline 의미가 흐려진다.
세 번째 date root 는 실제 다음 측정 날짜에 수집해야 한다.

## 결정

현재 시점에서는 **warning-as-failure 와 CI latency hard gate 를 승격하지 않는다.**

대신 다음 정책을 채택한다.

1. `local-win-x64-01`의 2-date-root/6-session 결과는 explicit runner reference envelope 로 유지한다.
2. D082의 날짜 다양성 조건은 그대로 유지한다. 즉 같은 runner 의 서로 다른 date root 3개 이상,
   각 date root 의 compatible session 3개 이상이 필요하다.
3. 세 번째 date root 는 실제 다음 측정 날짜에 수집한다. 날짜를 인위적으로 앞당기거나 같은 날짜에 다른 이름으로
   date root 를 만들지 않는다.
4. CI 작업을 먼저 진행해야 한다면 latency hard gate 가 아니라 **artifact-only CI benchmark 설계**로 분리한다.
   이 경우 실패 조건은 build/test 와 delivery/drop/leak hard gate 까지만 두고, latency/HWM/warning 은 report-only 로 둔다.
5. warning threshold 는 최소한 세 번째 date root 이후 다시 계산한다. threshold 후보는 max-anchor 단일값보다
   date root 별 분포와 p99 median/growth 를 함께 보는 방식으로 검토한다.

## 다음 작업 후보

### 후보 1: 다음 실제 날짜에 `local-win-x64-01/<YYYY-MM-DD>/session-01` 수집

가장 직접적인 evidence 보강이다. 다만 같은 날짜에 바로 이어서 수행할 수 없다.
실제 다음 날짜에 수행하면 D082 조건을 향해 안전하게 전진한다.

### 후보 2: CI artifact-only benchmark 정책 설계

바로 수행 가능하다. 목적은 latency failure 를 켜는 것이 아니라 CI runner identity, artifact 저장 위치,
local baseline 과 CI baseline 분리, exit code 정책을 먼저 닫는 것이다.
현재 작업 흐름상 다음 실행 가능한 문서 단위로 가장 적절하다.

### 후보 3: warning threshold 후보 산정 문서

가능하지만 아직 date root 가 2개라 threshold 논의가 다시 바뀔 가능성이 높다.
세 번째 date root 이후로 미루는 편이 낫다.

## 권장 다음 단위

다음 단일 작업 단위는 **CI artifact-only benchmark 정책 설계**로 둔다.

이 단위는 CI workflow 를 바로 작성하지 않는다. 먼저 아래 결정을 문서로 닫는다.

- CI runner id naming: 예를 들어 `ci-windows-x64-01`
- CI artifact path: local runner 와 섞지 않고 `docs/benchmarks/baselines/runners/<ci-runner-id>/...` 또는 별도 임시 artifact 로 둘지
- CI exit code 정책: build/test/delivery/drop/leak failure 만 실패로 둘지
- latency/HWM/warning 의 처리: report-only 로 둘지, PR comment/summary 로만 둘지
- local baseline 과 CI baseline 을 비교하지 않는 규칙

## 범위 밖

- warning-as-failure 구현
- latency hard threshold 구현
- CI workflow 작성
- 새 benchmark command 구현
- 세 번째 date root 즉시 생성
- RIO/io_uring backend 성능 gate

## 검증 계획

이번 단위는 문서와 정책 정렬만 변경한다.

- runner root `history.json`과 `docs/benchmarks/baselines/index.md` 수치를 대조한다.
- D082 조건과 현재 evidence 의 충족/미충족 상태를 대조한다.
- `DECISIONS.md`와 decision archive 에 D089를 기록한다.
- `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`를 같은 다음 실행 지점으로 맞춘다.
- 임시 표기와 내부 모순을 검색한다.
- `git diff --check`, solution build/test 로 repository 상태를 확인한다.
