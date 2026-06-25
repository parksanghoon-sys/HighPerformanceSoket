# CI artifact-only manual run 2회 이후 Phase 4 재평가

- 날짜: 2026-06-25
- 상태: Accepted
- 관련 결정: D090, D091, D092, D093
- 관련 workflow: `.github/workflows/benchmark-artifacts.yml`
- 관련 run:
  - `28143728630`
  - `28144480160`

## 목적

`ci-windows-x64-01` artifact-only workflow 를 원격에서 두 번 수동 실행한 뒤,
다음 Phase 4 후보를 정한다.

핵심 판단 대상은 세 가지다.

1. latency warning 을 실패로 승격할지
2. CI artifact 를 docs baseline 으로 채택할지
3. workflow 를 `push`/`pull_request` 자동 trigger 로 넓힐지

## 확인된 사실

- run `28143728630`은 성공했다.
  - artifact: `benchmark-artifacts-ci-windows-x64-01-2026-06-25-github-28143728630-1`
  - summary/history hard-passed: true
  - comparison-compatible: true
  - unknown-runner-count: 0
  - warning-count: 1
  - warning: `open-loop-01.json`의 `p99-growth-ratio-high`
  - 비고: 기존 `actions/*@v4` 계열로 인해 Node.js 20 deprecation annotation 이 있었다.
- run `28144480160`은 D092 action version 갱신 후 성공했다.
  - artifact: `benchmark-artifacts-ci-windows-x64-01-2026-06-25-github-28144480160-1`
  - summary/history hard-passed: true
  - comparison-compatible: true
  - unknown-runner-count: 0
  - warning-count: 0
  - 로그 검색 결과: `deprecation`, `Node.js 20`, `node20`, 이전 `actions/*@v4` 문자열 없음
- 두 artifact 는 모두 같은 날짜 root 의 GitHub-hosted Windows runner evidence 다.
- D090에 따라 CI warning 은 report-only 이며 failure 조건이 아니다.
- D090에 따라 CI artifact 는 docs baseline 에 자동 채택하지 않는다.
- D082의 latency gate 승격 조건은 여전히 충족하지 않는다.

## 선택지

### 선택지 A: latency warning 을 CI failure 로 승격한다

채택하지 않는다.

첫 번째 CI artifact 에서 같은 workload 의 `p99-growth-ratio-high` warning 이 실제로 발생했고,
두 번째 artifact 에서는 사라졌다. 이는 CI hosted runner scheduling noise 가능성을 보여준다.
현재 단계에서 warning 을 failure 로 올리면 false failure 비용이 크다.

### 선택지 B: 두 CI artifact 를 docs baseline 으로 채택한다

채택하지 않는다.

두 artifact 는 원격 runner evidence 로 유용하지만, D090은 CI artifact 와 repository baseline 을 분리한다.
baseline 채택은 별도 리뷰 단위에서 runner metadata, comparison compatibility, hard gate, path hygiene,
기존 runner history 와의 비교 가능성을 확인한 뒤 수행한다.

### 선택지 C: workflow 를 즉시 `push`/`pull_request` trigger 로 넓힌다

채택하지 않는다.

workflow 는 약 4분 정도 실행되고 artifact 를 생성한다. 매 push/PR에서 실행하면
artifact noise 와 CI 비용이 늘어난다. 현재 evidence 는 같은 날짜의 manual run 2개뿐이므로,
자동 trigger 범위를 정하기에는 아직 운영 정책 근거가 부족하다.

### 선택지 D: trigger 정책을 별도 설계한 뒤 제한적으로 넓힌다

채택한다.

다음 단위에서는 자동 trigger 를 바로 구현하지 않고, 어떤 event 에서 benchmark artifact 를 생성할지 먼저 정한다.
후보는 다음과 같다.

- `workflow_dispatch` 유지
- `push` to `master`만 추가
- `pull_request`는 제외하고 merge 후 evidence 만 수집
- `schedule`로 낮은 빈도 수집
- path filter 로 benchmark 관련 변경 때만 실행

## 결정

두 manual run 결과만으로 latency gate, warning-as-failure, docs baseline 자동 채택, push/PR 자동 trigger 를
승격하지 않는다.

다음 Phase 4 단위는 **CI artifact trigger policy 설계**로 둔다.
이 설계에서 자동 실행 event, 비용/노이즈, artifact retention, failure policy, baseline 채택 경계를 먼저 정한다.

## 영향

- `.github/workflows/benchmark-artifacts.yml`은 당장 `workflow_dispatch` 전용으로 유지한다.
- D090 report-only warning 정책을 유지한다.
- run `28143728630`, `28144480160` artifact 는 CI evidence 로만 남긴다.
- docs baseline index 는 자동 갱신하지 않는다.
- 다음 단위에서 trigger 정책이 수락되면 workflow event 만 최소 변경한다.

## 검증

- `gh run watch --exit-status`로 두 번째 manual run 성공을 확인했다.
- `gh run view --log`로 D092 action version 과 Node annotation 제거를 확인했다.
- `gh run download` 후 `summary.json`, `history.json`을 읽어 hard gate, warning, comparison 값을 확인했다.
- D090/D091/D092, `docs/benchmarks/baselines/index.md`, 현재 TODO를 대조했다.
