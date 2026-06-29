# Phase 4 D127 이후 다음 실행 후보 재평가

- 날짜: 2026-06-29
- 상태: Accepted
- 관련 결정: D090, D095, D096, D123, D124, D125, D126, D127, D128
- 입력 리뷰: `.claude/review/2026-06-29-next-scope-decision-review.md`

## 배경

`.claude/review/2026-06-29-next-scope-decision-review.md`는 D122 직후의 범위 판단 문서다.
그 리뷰는 `local-win-x64-01` date root 가 2개라는 전제를 사용했고,
Phase 4 baseline gate evidence 계속 수집 또는 D090 CI artifact-only workflow skeleton 을 다음 후보로 권장했다.

현재 상태는 그 전제보다 앞서 있다.

- D123: `local-win-x64-01`은 3-date-root/9-session reference evidence 를 확보했다.
- D124: 그 evidence 는 runner-local 수동 리뷰 기준으로 채택됐고, gate 승격은 보류됐다.
- D125: runner/profile scoped 판단은 별도 envelope comparison artifact 로 분리됐다.
- D126: repository 기본 SDK 선택이 .NET 9 계열로 고정됐다.
- D127: CI artifact-only workflow 가 reference history 존재 시 `envelope.json`/`envelope.md`를 업로드하도록 연결됐다.

따라서 기존 리뷰의 "3번째 date root 수집"과 "CI artifact-only workflow skeleton"은 이미 완료된 방향이다.

## 후보 평가

### 후보 A: RIO full IPv6/default promotion

지금 열지 않는다.
TODO의 `P2_LATER` 상태와 D119/D121/D122 판단이 여전히 유효하다.
현재 RIO는 IPv4-only opt-in backend 로 운영 가능한 fallback 이 있고,
full IPv6는 native sockaddr, dual-mode socket, TCP/UDP contract matrix, benchmark artifact 가 얽힌 큰 범위다.

### 후보 B: server-level diagnostics public API

지금 열지 않는다.
TODO의 `P3_NICE` 상태와 D068 판단이 여전히 유효하다.
현재 diagnostics 소비자는 transport snapshot/test/benchmark 중심이며,
실제 운영 host 또는 metrics exporter 요구가 없는 상태에서 `BrokerServer` public surface 를 먼저 고정하면
불필요한 API가 될 가능성이 높다.

### 후보 C: warning-as-failure 또는 latency hard gate

지금 승격하지 않는다.
D125 envelope comparison 은 report-only artifact 로 막 구현된 상태이고,
D127 workflow 연결도 아직 원격 CI run 에서 검증되지 않았다.
또한 CI runner repository baseline 은 아직 1-session reference 이므로 hard gate 로 쓰기에는 표본이 부족하다.

### 후보 D: D127 push-triggered CI artifact 검증

채택한다.
workflow 변경은 로컬 정적 테스트와 CLI smoke 로 검증했지만,
실제 GitHub Actions 환경에서 upload artifact 에 `envelope.json`과 `envelope.md`가 포함되는지는 push-triggered run 으로만 확인할 수 있다.
이 검증은 D127을 완료 상태로 운영하기 위한 가장 가까운 evidence 이며,
다음 큰 구현 방향을 넓히기 전에 CI artifact chain 을 먼저 확인하는 것이 안전하다.

## 결정

D128로 다음 실행 후보를 **D127 workflow 의 원격 push-triggered CI run 검증**으로 둔다.

검증 목표:

1. `Benchmark Artifacts` workflow 가 push to `master`에서 실행된다.
2. artifact name 이 D091 규칙대로 GitHub run identity 를 포함한다.
3. artifact date root 안에 raw report 6개, `summary.json`, `summary.md`, `history.json`, `history.md`,
   `envelope.json`, `envelope.md`가 포함된다.
4. `summary.json`과 `history.json`은 기존 hard gate 를 통과한다.
5. `envelope.json`은 report-only 로 해석하며, mismatch/signal 이 있어도 CI failure 로 승격하지 않는다.

## 범위 밖

- CI artifact 자동 repository baseline 채택
- warning-as-failure 구현
- latency hard gate 구현
- RIO full IPv6/default promotion
- server-level diagnostics public API

## 검증

- 최신 review 문서의 전제가 D123~D127 현재 상태와 맞는지 대조한다.
- TODO deferred backlog priority 가 여전히 타당한지 확인한다.
- `git diff --check`, solution build/test 로 문서 변경이 repository 상태를 깨지 않는지 확인한다.
