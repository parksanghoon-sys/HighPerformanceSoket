# CI artifact trigger policy 설계

- 날짜: 2026-06-25
- 상태: Accepted
- 관련 결정: D090, D091, D092, D093, D094
- 관련 workflow: `.github/workflows/benchmark-artifacts.yml`

## 목적

`Benchmark Artifacts` workflow 를 언제 자동 실행할지 정한다.

D093에서 manual run 2회만으로는 gate/trigger 를 바로 승격하지 않기로 했지만,
계속 `workflow_dispatch`만 유지하면 code change 이후 CI runner evidence 를 쌓기 어렵다.
따라서 자동 trigger 를 최소 범위로 추가하되, PR noise 와 docs-only 변경 비용은 피한다.

## 확인된 기준

- workflow 는 이미 두 번의 manual run 에서 성공했다.
- D090에 따라 latency/HWM/warning 은 report-only 이며, failure 조건은 build/test, command failure,
  delivery/drop/leak hard gate 실패로 제한한다.
- D091에 따라 GitHub run identity 는 artifact name 에만 두고, upload 내부 구조는 date/session layout 을 유지한다.
- D092에 따라 action runtime 은 Node 24 계열로 갱신되어 Node deprecation annotation 이 제거됐다.
- D093에 따라 CI artifact 는 docs baseline 에 자동 채택하지 않고 evidence 로만 둔다.

## 선택지

### 선택지 A: `workflow_dispatch`만 유지

채택하지 않는다.

가장 조용하지만, merge 이후 code change 에 대한 CI runner artifact 가 자동으로 남지 않는다.
Phase 4의 목적이 반복 가능한 benchmark evidence 를 쌓는 것이므로 장기적으로 부족하다.

### 선택지 B: 모든 `push`와 모든 `pull_request`에서 실행

채택하지 않는다.

artifact workflow 는 약 4분 실행되고 artifact 10개를 업로드한다.
모든 PR과 모든 push 에서 실행하면 리뷰 전 변경, fork/PR 보안 경계, docs-only 변경까지 artifact noise 가 커진다.

### 선택지 C: `push` to `master`만 실행

부분 채택한다.

merge 된 상태에 대해서만 artifact 를 남기므로 PR 중간 noise 를 피할 수 있다.
다만 docs-only 변경에도 실행되면 비용이 불필요하므로 path filter 를 함께 둔다.

### 선택지 D: `push` to `master` + code/benchmark/build path filter

채택한다.

운영 부담과 evidence 수집의 균형이 가장 좋다.
다음 경로 변경에만 자동 실행한다.

- `.github/workflows/benchmark-artifacts.yml`
- `Directory.Build.props`
- `HighPerformanceSocket.slnx`
- `src/**`
- `tests/**`
- `samples/**`

docs-only 변경, review 문서, 상태 문서 변경은 자동 benchmark 를 실행하지 않는다.
수동 확인이 필요하면 `workflow_dispatch`를 계속 사용한다.

### 선택지 E: schedule trigger

채택하지 않는다.

날짜별 CI evidence 를 자동으로 쌓는 장점은 있지만, 코드 변경 없는 반복 실행 비용이 생긴다.
우선 merge-based evidence 를 쌓은 뒤 필요하면 별도 단위로 검토한다.

## 결정

`Benchmark Artifacts` workflow 는 다음 trigger 를 가진다.

1. `workflow_dispatch`
2. `push` to `master`, 단 code/benchmark/build 관련 path 변경에 한정

`pull_request`와 `schedule`은 이번 범위에서 추가하지 않는다.

## 영향

- 코드/벤치/빌드/샘플 변경이 `master`에 push 되면 CI benchmark artifact 가 자동으로 생성된다.
- docs-only 변경은 benchmark artifact 를 만들지 않는다.
- PR 단계에서는 benchmark artifact 를 만들지 않는다.
- failure policy 는 D090 그대로 유지한다.
- artifact 는 여전히 docs baseline 에 자동 채택하지 않는다.

## 검증 계획

- workflow YAML에 `workflow_dispatch`와 `push.branches: master`가 모두 있는지 확인한다.
- `push.paths`가 code/benchmark/build 관련 경로만 포함하는지 확인한다.
- workflow 안에 `pull_request`, `schedule`, warning-as-failure, latency failure logic 이 없는지 확인한다.
- `git diff --check`로 YAML/문서 whitespace 를 확인한다.
