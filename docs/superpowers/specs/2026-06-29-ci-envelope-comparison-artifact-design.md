# CI Envelope Comparison Artifact 설계

- 날짜: 2026-06-29
- 상태: Accepted
- 관련 결정: D090, D091, D095, D096, D125, D127
- 관련 workflow: `.github/workflows/benchmark-artifacts.yml`

## 배경

D125로 runner/profile scoped baseline 판단은 기존 `warning-count`가 아니라 별도 envelope comparison artifact 로 분리했다.
CLI command 는 이미 reference `history.json`과 candidate `summary.json` 또는 `history.json`을 읽어
`envelope.json`/`envelope.md`를 만들 수 있다.

현재 CI benchmark workflow 는 raw report, summary, date-level history 를 업로드한다.
하지만 D125 command 결과는 아직 CI artifact 에 포함되지 않으므로, CI run 을 내려받는 사람이
기존 repository reference baseline 과 이번 run 의 관계를 별도 명령으로 다시 계산해야 한다.

## 결정

`Benchmark Artifacts` workflow 에 report-only envelope comparison step 을 추가한다.

1. benchmark session summary 와 date-level history 를 생성한 뒤 envelope comparison 을 시도한다.
2. reference 는 repository baseline 의 `docs/benchmarks/baselines/runners/<runner-id>/history.json`을 사용한다.
3. reference history 가 없으면 step 은 skip 하고 workflow 를 성공 상태로 유지한다.
4. reference history 가 있으면 candidate 는 이번 CI run 의 `<session-dir>/summary.json`을 사용한다.
5. output 은 업로드 대상 date root 의 `envelope.json`과 `envelope.md`로 둔다.
6. `envelope-compatible=false` 또는 `envelope-signal-count > 0`은 process failure 가 아니다.
   D125 command 자체가 artifact 생성 성공 시 exit code 0을 반환하므로 workflow failure policy 는 바꾸지 않는다.

## 범위

포함:

- workflow step 추가
- workflow 정적 회귀 테스트
- D125 spec 의 후속 결정 번호 오해 표현 보정
- state/decision/changelog 문서 갱신

제외:

- warning-as-failure 구현
- latency hard gate 구현
- pull_request/schedule trigger 추가
- CI artifact 자동 repository baseline 채택
- reference envelope 값을 별도 repository artifact 로 materialize 하는 기능

## 실패 정책

다음은 workflow failure 로 유지한다.

- build/test 실패
- baseline-suite/summary/history command 실패
- envelope command 의 usage/schema/write 오류

다음은 workflow failure 가 아니다.

- reference history 없음으로 envelope step skip
- envelope mismatch
- envelope signal count 증가
- latency/HWM warning 증가

## 검증

- workflow YAML 에 reference history 존재 여부를 확인하는 skip 분기가 있다.
- workflow YAML 이 `envelope.json`과 `envelope.md`를 date root 아래에 생성한다.
- workflow YAML 이 `--compare-baseline-envelope`, `--reference-history`, `--envelope`, `--envelope-md`를 사용한다.
- 실제 현재 CI repository baseline 을 reference 로 삼아 envelope command smoke 를 실행한다.
- `dotnet build`, `dotnet test`, `git diff --check`를 통과한다.
