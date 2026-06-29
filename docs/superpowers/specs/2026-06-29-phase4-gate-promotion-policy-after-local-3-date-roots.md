# Phase 4 local 3-date-root 이후 gate 승격 정책 설계

- 날짜: 2026-06-29
- 상태: Accepted
- 관련 결정: D063, D070, D080, D082, D089, D090, D096, D123, D124
- 관련 artifact:
  - `docs/benchmarks/baselines/index.md`
  - `docs/benchmarks/baselines/runners/local-win-x64-01/history.json`
  - `docs/benchmarks/baselines/runners/local-win-x64-01/history.md`
  - `docs/benchmarks/baselines/runners/ci-windows-x64-01/history.json`
- 관련 코드:
  - `tests/Hps.Benchmarks/BaselineSummaryGenerator.cs`
  - `.github/workflows/benchmark-artifacts.yml`

## 목적

D123으로 `local-win-x64-01` explicit runner 는 3개 date root, 총 9-session reference 를 확보했다.

- `2026-06-24`: compatible session 3개
- `2026-06-25`: compatible session 3개
- `2026-06-29`: compatible session 3개

D082가 요구한 explicit runner 3-date-root evidence 조건은 충족됐다. 이번 설계는 이 evidence 를 근거로
warning-as-failure, latency envelope 초과 처리, CI latency gate 를 지금 승격할 수 있는지 재평가한다.

## 현재 확인된 사실

### local explicit runner

- runner id 는 `local-win-x64-01`이다.
- runner kind 는 `local`, profile 은 `tcp-loopback-saea-v1`, backend 는 `SaeaTransport`다.
- runner root history 는 `session-count=9`, `hard-passed=true`, `warning-count=0`,
  `comparison-compatible=true`, unknown runner 0, mismatch 0이다.
- 모든 session 은 raw report 6개(load 3회, open-loop 3회)를 포함한다.
- delivery/drop/leak hard gate 는 모두 통과했다.
- dropped total, payload error total, pool rented max 는 모두 0이다.

9-session explicit runner reference envelope:

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

### CI runner

- committed CI repository baseline 은 `ci-windows-x64-01/2026-06-25/session-01` 1개뿐이다.
- CI runner root history 는 `session-count=1`, `hard-passed=true`, `warning-count=0`,
  `comparison-compatible=true`다.
- CI benchmark workflow 는 D090/D094 기준으로 artifact-only 단계다.
- CI failure 조건은 build/test, benchmark command failure, delivery/drop/leak hard gate failure 로 제한된다.
- latency/HWM/warning 은 CI failure 조건이 아니다.

### 현재 warning 구현

`BaselineSummaryGenerator`의 warning threshold 는 runner/profile 별 artifact 를 읽지 않는 전역 상수다.

| metric | 현재 threshold |
| --- | ---: |
| load p99 latency | 1386.2 us |
| open-loop p99 latency | 1508.3 us |
| p99 growth ratio | 2.0 |
| actual rate | 95.0 Hz 미만 |
| load TCP HWM | 4 이상 |
| open-loop TCP HWM | 8 이상 |

이 threshold 는 D070의 초기 report-only soft warning 이며, CLI exit code 나 hard gate 를 바꾸지 않는다.
중요한 점은 이 값이 runner scoped 가 아니라는 것이다. 따라서 local runner envelope 를 근거로 이 전역 상수를
바로 낮추면 CI runner, UDP benchmark, RIO benchmark 같은 다른 비교군에도 같은 기준이 적용된다.

## D082 조건 재대조

| 조건 | 현재 상태 | 판단 |
| --- | --- | --- |
| 명시적 runner id | `local-win-x64-01` | 충족 |
| 같은 runner/profile/workload 의 서로 다른 date root 3개 이상 | `2026-06-24`, `2026-06-25`, `2026-06-29` | 충족 |
| 각 date root 당 compatible session 3개 이상 | 모두 3개 | 충족 |
| 각 session 이 load 3회/open-loop 3회 포함 | 모두 충족 | 충족 |
| delivery/drop/leak hard gate 통과 | 모두 통과 | 충족 |
| comparison-compatible true, unknown/mismatch 0 | 모두 충족 | 충족 |
| warning threshold 가 scheduling noise 와 regression 을 구분한다는 별도 검토 | 이번 문서에서 검토 | 조건 검토 완료 |

local runner 기준으로는 reference envelope 채택 조건이 충족됐다. 다만 이 조건 충족은 곧바로
process failure 승격을 뜻하지 않는다. D082도 false failure 비용, CI runner 안정성, artifact 보존 정책을
별도 검토해야 한다고 남겼다.

## 검토한 선택지

### 선택지 A: warning-count > 0을 전역 process failure 로 승격한다

채택하지 않는다. 현재 warning threshold 는 전역 상수라서 `local-win-x64-01` evidence 만으로 모든 runner/profile 에
적용하기에는 범위가 넓다. 특히 CI runner 는 아직 1-session 뿐이고, RIO/UDP benchmark 는 별도 profile/backend 이다.

### 선택지 B: local 9-session envelope 를 기준으로 전역 warning threshold 를 낮춘다

채택하지 않는다. 새 local envelope 는 load p99 max 935.6 us, open-loop p99 max 1077.4 us 이지만,
이 값을 전역 threshold 로 쓰면 local SAEA TCP loopback 전용 관측값이 CI/RIO/UDP에도 적용된다.
summary generator 에 runner/profile scoped threshold 입력이 없는 상태에서 전역 상수를 낮추는 것은 구조적으로 부정확하다.

### 선택지 C: local 9-session envelope 를 runner-local reference 로 채택하고 report-only 로 유지한다

채택한다. `local-win-x64-01`의 9-session envelope 는 같은 runner/profile/workload 의 후속 baseline 을
수동 리뷰할 때 기준으로 쓴다. 하지만 이 envelope 초과나 기존 soft warning 발생만으로 build/test/CI 를 실패시키지는 않는다.

### 선택지 D: CI latency hard gate 를 켠다

채택하지 않는다. CI runner 는 repository baseline 이 아직 1-session 뿐이고, D090/D096은 CI를 artifact-only 로 유지한다고 정했다.
local runner 와 CI runner 의 OS/framework 버전도 다르며, CI runner scheduling noise 를 local envelope 로 판단하면 false failure 가능성이 크다.

### 선택지 E: runner/profile scoped warning envelope 모델을 후속 설계로 분리한다

채택한다. 실제 warning threshold 를 더 민감하게 만들려면 threshold 가 benchmark profile, runner id/kind,
transport backend, protocol, workload case 단위로 분리되어야 한다. 이 모델은 summary generator 전역 상수를
바로 바꾸는 것이 아니라, reference envelope artifact 를 입력으로 받거나 runner-specific policy 를 명시하는 별도 설계가 필요하다.

## 결정

`local-win-x64-01` 9-session explicit runner envelope 를 **runner-local reference envelope** 로 채택한다.

이번 결정은 다음을 의미한다.

1. `docs/benchmarks/baselines/index.md`의 `local-win-x64-01 Explicit Runner Reference Latency Envelope`가
   후속 local baseline 수동 리뷰의 기준이다.
2. 후속 `local-win-x64-01` compatible baseline 이 이 envelope 를 넘으면 회귀 의심 신호로 기록한다.
3. envelope 초과는 아직 CLI exit code, CI failure, process failure 가 아니다.
4. 기존 `BaselineSummaryGenerator` soft warning threshold 는 그대로 둔다.
5. `warning-count > 0`은 계속 report-only 이며, CI failure 로 승격하지 않는다.
6. CI latency hard gate 는 계속 보류한다.
7. CI artifact 의 repository baseline 채택 기준은 D095를 따른다. 특히 warning-count > 0 artifact 는
   reference baseline 으로 자동 채택하지 않는다.

## gate 승격 보류 조건

warning-as-failure 또는 latency hard gate 는 아래 조건이 닫히기 전까지 보류한다.

1. warning threshold 가 runner/profile/workload scoped 로 표현된다.
2. CI runner 도 최소 3개 date root, date root 당 compatible session 3개 이상을 확보한다.
3. local runner envelope 와 CI runner envelope 를 직접 비교하지 않는 정책이 유지된다.
4. warning 발생 시 재실행/flake 판정/수동 채택 거부 중 어느 운영 동작을 취할지 정한다.
5. pull_request/schedule trigger 에서 benchmark runtime 비용과 false failure 비용을 감당할 수 있는지 별도 검토한다.

## 다음 작업 후보

### 후보 1: runner/profile scoped warning envelope model 설계

가장 자연스러운 후속이다. 목적은 전역 warning 상수를 직접 낮추지 않고, benchmark profile 과 runner identity 를
기준으로 threshold 를 해석하는 모델을 설계하는 것이다.

다룰 항목:

- reference envelope artifact 위치와 schema
- summary generator 가 threshold input 을 받을지, 별도 comparison command 를 둘지
- CI runner 와 local runner threshold 를 분리하는 규칙
- warning-count 를 유지할지, envelope comparison result 를 별도 field 로 둘지
- 기존 summary/history artifact 와의 backward compatibility

### 후보 2: CI runner date-root evidence 추가 수집/채택 정책

CI gate 를 언젠가 승격하려면 CI runner evidence 가 더 필요하다. 다만 CI artifact 는 D095 checklist 를 통과한 뒤
repository baseline 으로 수동 채택해야 한다. 자동 채택이나 pull_request latency failure 는 아직 범위 밖이다.

### 후보 3: Phase 5/6 backend work 계속

latency gate 승격은 보류하고 backend 최적화 작업으로 돌아갈 수 있다. 이 경우 benchmark gate 는 현재처럼
delivery/drop/leak hard gate 와 report-only latency artifact 로 유지한다.

## 권장 다음 단위

다음 단위는 **runner/profile scoped warning envelope model 설계**로 둔다.

이 단위는 바로 production code 를 바꾸지 않는다. 먼저 전역 threshold 를 대체하거나 보강할 수 있는
schema, command boundary, backward compatibility 를 설계한다. 이 설계가 닫힌 뒤에만
`BaselineSummaryGenerator`의 warning 계산 방식을 바꿀지 판단한다.

## 범위 밖

- warning-as-failure 구현
- latency hard threshold 구현
- CI latency hard gate 구현
- pull_request/schedule benchmark trigger 추가
- CI artifact 자동 채택
- `BaselineSummaryGenerator` threshold 상수 변경
- RIO/UDP benchmark gate 승격

## 검증 계획

이번 단위는 문서와 정책 정렬만 변경한다.

- local runner root `history.json`과 index envelope 수치를 대조한다.
- CI runner root `history.json`이 아직 1-session 인지 확인한다.
- `BaselineSummaryGenerator` warning threshold 가 전역 상수인지 확인한다.
- D082/D089/D090/D096/D123과 이번 결정이 충돌하지 않는지 확인한다.
- `DECISIONS.md`와 decision archive 에 D124를 기록한다.
- `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`를 같은 다음 실행 지점으로 맞춘다.
- 임시 표기와 내부 모순을 검색한다.
- `git diff --check`, solution build/test 로 repository 상태를 확인한다.
