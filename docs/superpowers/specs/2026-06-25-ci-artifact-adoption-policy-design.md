# CI artifact adoption policy 설계

- 날짜: 2026-06-25
- 상태: Accepted
- 관련 결정: D090, D091, D093, D094, D095
- 관련 workflow: `.github/workflows/benchmark-artifacts.yml`
- 관련 index: `docs/benchmarks/baselines/index.md`

## 목적

CI가 자동 생성한 benchmark artifact 를 어떤 조건에서 repository baseline 으로 채택할지 정한다.

D090/D093에 따라 CI artifact 는 baseline 에 자동 반영하지 않는다.
하지만 `workflow_dispatch`와 `push` trigger 검증으로 CI runner evidence 가 쌓이기 시작했으므로,
사람 또는 에이전트가 검토 후 `docs/benchmarks/baselines/runners/<runner-id>/...` 아래로 채택하는 절차가 필요하다.

## 현재 artifact 구조

CI workflow 는 artifact upload 이름에 GitHub run identity 를 넣는다.

```text
benchmark-artifacts-ci-windows-x64-01-YYYY-MM-DD-github-<run-id>-<attempt>
```

artifact 내부는 history command 와 호환되도록 date root 구조를 유지한다.

```text
history.json
history.md
session-01/
  load-01.json
  load-02.json
  load-03.json
  open-loop-01.json
  open-loop-02.json
  open-loop-03.json
  summary.json
  summary.md
```

repository baseline 은 D084에 따라 다음 구조를 사용한다.

```text
docs/benchmarks/baselines/runners/<runner-id>/YYYY-MM-DD/session-NN/
```

따라서 artifact directory 를 통째로 그대로 커밋하지 않는다.
채택 시에는 검증된 raw report 를 repository baseline 의 다음 `session-NN`으로 복사하고,
summary/history/index 는 repository 경로 기준으로 다시 생성한다.

## 채택 전 필수 조건

CI artifact 를 repository baseline 으로 채택하려면 다음을 모두 만족해야 한다.

1. source run 은 `Benchmark Artifacts` workflow 의 성공한 run 이어야 한다.
2. source run event 는 `workflow_dispatch` 또는 D094가 허용한 `push` event 이어야 한다.
3. artifact 이름은 `benchmark-artifacts-<runner-id>-<yyyy-mm-dd>-github-<run-id>-<attempt>` 형식을 따라야 한다.
4. raw report 의 runner metadata 는 artifact runner id 와 일치해야 한다.
   - `runner-id=ci-windows-x64-01`
   - `runner-kind=ci`
   - `benchmark-profile=tcp-loopback-saea-v1`
   - `transport-backend=SaeaTransport`
5. `summary.json`은 다음 값을 가져야 한다.
   - `source-report-count=6`
   - `hard-passed=true`
   - `comparison-compatible=true`
   - `unknown-runner-count=0`
   - `warning-count=0`
6. `history.json`은 다음 값을 가져야 한다.
   - `session-count=1`
   - `hard-passed=true`
   - `comparison-compatible=true`
   - `warning-count=0`
7. raw report 와 summary/history 안에 로컬 절대 경로가 없어야 한다.
8. artifact 가 기존 repository baseline 과 비교 가능한 workload set 을 가져야 한다.
   - load 3회
   - open-loop 3회
   - payload 4096 bytes
   - target rate 100 Hz

`warning-count > 0` artifact 는 repository reference baseline 으로 채택하지 않는다.
그런 artifact 는 CI evidence 로는 남길 수 있지만, future comparison 기준점으로 삼기에는 noise 가능성이 크다.

## 채택 절차

1. GitHub run id 를 정한다.
2. artifact 를 임시 directory 로 다운로드한다.

```powershell
gh run download <run-id> --dir <temp-dir>
```

3. artifact 이름, 파일 수, raw report 6개, `summary.json`, `history.json`을 확인한다.
4. `summary.json`과 `history.json`의 hard gate, warning, comparison 값을 확인한다.
5. raw report metadata 와 artifact runner id 를 대조한다.
6. target directory 를 정한다.

```text
docs/benchmarks/baselines/runners/ci-windows-x64-01/YYYY-MM-DD/session-NN/
```

`session-NN`은 해당 date root 의 다음 번호를 사용한다.
artifact 내부 이름이 항상 `session-01`이어도 repository 에서는 다음 available session 번호로 옮긴다.

7. raw report 6개만 target session directory 로 복사한다.
8. repository 경로 기준으로 summary 를 다시 생성한다.

```powershell
dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj --no-build --no-restore -- `
  --summarize-baseline <target-session-dir> `
  --summary <target-session-dir>/summary.json `
  --summary-md <target-session-dir>/summary.md
```

9. date-level history 를 다시 생성한다.

```powershell
dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj --no-build --no-restore -- `
  --summarize-baseline-history <target-date-root> `
  --history <target-date-root>/history.json `
  --history-md <target-date-root>/history.md
```

10. runner root history 를 다시 생성한다.

```powershell
dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj --no-build --no-restore -- `
  --summarize-baseline-history <target-runner-root> `
  --history <target-runner-root>/history.json `
  --history-md <target-runner-root>/history.md
```

11. `docs/benchmarks/baselines/index.md`를 갱신한다.
12. `git diff --check`, benchmark tests 또는 solution build/test 중 현재 변경 범위에 맞는 검증을 실행한다.
13. 채택 commit 에는 artifact source run id 와 artifact name 을 changelog/TODO에 기록한다.

## 채택하지 않는 항목

다음은 repository baseline 에 넣지 않는다.

- GitHub artifact zip 자체
- artifact download root directory 이름
- GitHub run id 를 session directory 이름으로 쓴 경로
- warning-count 가 1 이상인 artifact
- comparison-compatible 이 false 인 artifact
- runner metadata 가 unknown 이거나 artifact runner id 와 맞지 않는 artifact
- raw report 수가 6개가 아닌 artifact
- 로컬 절대 경로가 포함된 artifact

## 현재 후보 판단

현재 확인된 CI artifact 는 다음과 같다.

| run id | event | artifact | hard passed | warnings | comparison compatible | adoption 판단 |
| --- | --- | --- | --- | ---: | --- | --- |
| 28143728630 | workflow_dispatch | `benchmark-artifacts-ci-windows-x64-01-2026-06-25-github-28143728630-1` | true | 1 | true | warning 때문에 채택하지 않음 |
| 28144480160 | workflow_dispatch | `benchmark-artifacts-ci-windows-x64-01-2026-06-25-github-28144480160-1` | true | 0 | true | 채택 가능 후보 |
| 28145025444 | push | `benchmark-artifacts-ci-windows-x64-01-2026-06-25-github-28145025444-1` | true | 0 | true | 채택 가능 후보 |

두 채택 가능 후보가 모두 같은 날짜의 같은 CI runner evidence 이므로,
다음 단위에서는 둘 중 하나를 `ci-windows-x64-01/2026-06-25/session-01`로 채택하는 것이 자연스럽다.
동일 날짜에서 여러 CI artifact 를 모두 채택할지는 별도 판단이 필요하다.

## 결정

CI artifact 는 자동 채택하지 않고, 위 checklist 를 통과한 artifact 만 수동 repository baseline 으로 채택한다.
채택 시에는 raw report 6개만 repository baseline session directory 로 복사하고,
summary/history/index 는 repository 경로 기준으로 재생성한다.

첫 채택 후보는 warning 이 없는 최신 push-triggered run `28145025444`로 둔다.
이 artifact 는 실제 D094 trigger 경로로 생성됐으므로 CI 자동 evidence 의 대표성이 가장 높다.

## 검증 계획

- spec placeholder scan 으로 미정 항목이 없는지 확인한다.
- D090/D093/D094와 모순이 없는지 확인한다.
- `docs/benchmarks/baselines/index.md`의 current update rule 과 모순이 없는지 확인한다.
- `git diff --check`를 실행한다.
