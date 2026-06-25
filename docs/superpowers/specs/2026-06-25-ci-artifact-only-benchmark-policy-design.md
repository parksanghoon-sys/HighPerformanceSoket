# CI artifact-only benchmark 정책 설계

- 날짜: 2026-06-25
- 상태: Accepted
- 관련 결정: D080, D082, D084, D089, D090
- 관련 코드:
  - `tests/Hps.Benchmarks/Program.cs`
  - `tests/Hps.Benchmarks/BenchmarkRunIdentity.cs`
  - `tests/Hps.Benchmarks/BaselineSuiteRunner.cs`
- 관련 artifact:
  - `docs/benchmarks/baselines/index.md`
  - `docs/superpowers/specs/2026-06-25-phase4-gate-promotion-reassessment-design.md`

## 목적

D089는 `local-win-x64-01`의 2-date-root/6-session evidence 만으로는 warning-as-failure 또는 CI latency
hard gate 를 승격하지 않기로 했다. 그래도 CI에서 benchmark artifact 를 남기면 이후 runner 별 비교와
gate 승격 검토가 쉬워진다.

이번 설계는 CI workflow 를 바로 작성하지 않고, CI benchmark 를 어떤 runner identity 와 artifact 정책으로
운영할지 먼저 닫는다.

## 현재 확인된 사실

- 현재 repository 에는 `.github/workflows/`가 없다.
- benchmark CLI 는 다음 command 를 제공한다.
  - `--baseline-suite <output-dir> [--runs <count>]`
  - `--summarize-baseline <input-dir> --summary <output-json> [--summary-md <output-md>]`
  - `--summarize-baseline-history <baseline-root> --history <output-json> [--history-md <output-md>]`
- `Program`의 exit code 는 다음 규칙을 따른다.
  - usage/report write failure: 2
  - `baseline-suite`의 run hard failure: 1
  - `summary`/`history`의 `hard-passed=false`: 1
  - `warning-count > 0`만으로는 exit code 를 바꾸지 않음
- `BenchmarkRunIdentity.CaptureDefault()`는 `HPS_BENCHMARK_RUNNER_ID`와
  `HPS_BENCHMARK_RUNNER_KIND`만 명시 override 로 사용한다.
- host name, user name, IP address 는 privacy 때문에 자동 수집하지 않는다.

## 검토한 선택지

### 선택지 A: CI에서 latency warning 을 실패로 올린다

채택하지 않는다. local runner reference 도 아직 D082의 3-date-root 조건을 채우지 못했다.
CI runner 는 OS scheduling, CPU quota, background load 변동이 더 클 수 있으므로 latency warning 을
바로 실패로 올리면 false failure 가능성이 높다.

### 선택지 B: CI에서 build/test 만 수행하고 benchmark 는 돌리지 않는다

보수적으로 안전하지만 Phase 4 artifact 축적 목적에는 부족하다. CI runner 에서 benchmark artifact 를 남기지 않으면
local runner 와 CI runner 의 차이를 나중에 판단할 근거가 없다.

### 선택지 C: CI benchmark 는 artifact-only 로 수집한다

채택한다. CI에서 benchmark 를 실행하되 실패 조건은 기존 hard gate 로 제한한다.
latency, HWM, warning 은 실패가 아니라 report artifact 로 남긴다.

## 결정

CI benchmark 는 **artifact-only** 단계로 시작한다.

이 정책에서 "artifact-only"는 benchmark 를 실행하지 않는다는 뜻이 아니다.
benchmark raw report, summary, history 를 생성하지만 latency/HWM/warning 을 CI failure 로 승격하지 않는다는 뜻이다.

CI benchmark 의 실패 조건은 다음으로 제한한다.

1. `dotnet build` 실패
2. `dotnet test` 실패
3. benchmark command usage/write failure(exit code 2)
4. `baseline-suite`, `summary`, `history`의 `hard-passed=false`로 인한 exit code 1

다음은 실패 조건이 아니다.

- `warning-count > 0`
- p99 envelope 초과
- p99 growth ratio 증가
- TCP HWM 증가
- local runner envelope 와 CI runner envelope 차이

## CI runner identity

CI runner 는 local runner 와 다른 `runner-id`를 사용한다.

권장 기본값:

- `HPS_BENCHMARK_RUNNER_ID=ci-windows-x64-01`
- `HPS_BENCHMARK_RUNNER_KIND=ci`

Linux CI가 추가되면 별도 runner id 를 사용한다.

- `ci-linux-x64-01`

runner id 는 D084와 같은 privacy-safe stable token 규칙을 따른다.
host name, user name, IP address, 사내 자산 번호를 쓰지 않는다.

## artifact 저장 정책

CI benchmark artifact 는 committed baseline 과 섞지 않는다.

권장 임시 artifact root:

```text
artifacts/benchmarks/runners/<ci-runner-id>/<yyyy-mm-dd>/<run-id>/
  load-01.json
  load-02.json
  load-03.json
  open-loop-01.json
  open-loop-02.json
  open-loop-03.json
  summary.json
  summary.md
  history.json
  history.md
```

`docs/benchmarks/baselines/runners/<runner-id>/...`는 사람이 검토하고 repository 에 보존하기로 결정한
baseline 만 들어간다. CI가 매번 생성하는 raw artifact 를 바로 docs 아래에 commit 하지 않는다.

CI artifact 를 장기 baseline 으로 채택하려면 별도 문서/리뷰 단위에서 다음을 확인한 뒤 docs 로 옮긴다.

- runner id 와 raw metadata 일치
- `comparison-compatible=true`, unknown runner 0, mismatch 0
- hard gate pass
- artifact path 에 로컬 절대 경로 없음
- 같은 runner 의 기존 history 와 비교 의미가 있음

## 실행 순서 정책

CI artifact-only 단계에서 권장 실행 순서는 다음과 같다.

1. `dotnet restore HighPerformanceSocket.slnx`
2. `dotnet build HighPerformanceSocket.slnx --no-restore`
3. `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`
4. `HPS_BENCHMARK_RUNNER_ID=<ci-runner-id>`, `HPS_BENCHMARK_RUNNER_KIND=ci`를 설정한다.
5. `dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj --no-build --no-restore -- --baseline-suite <artifact-session-dir> --runs 3`
6. `dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj --no-build --no-restore -- --summarize-baseline <artifact-session-dir> --summary <artifact-session-dir>/summary.json --summary-md <artifact-session-dir>/summary.md`
7. `dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj --no-build --no-restore -- --summarize-baseline-history <artifact-date-root> --history <artifact-date-root>/history.json --history-md <artifact-date-root>/history.md`

CI는 위 command 의 exit code 를 그대로 따른다. 별도 script 에서 warning-count 를 실패로 변환하지 않는다.

## local baseline 과 CI baseline 분리

local runner baseline 과 CI runner artifact 는 같은 latency envelope 로 비교하지 않는다.

- local runner: `local-win-x64-01`
- CI runner: `ci-windows-x64-01`

같은 profile/workload 라도 runner kind 와 실행 환경이 다르면 scheduling noise 와 CPU quota 가 다를 수 있다.
summary/history comparison 은 같은 runner/environment 안의 비교 가능성 신호로 쓰고, cross-runner 성능 비교는
별도 비교 문서가 생길 때까지 수동 해석으로만 다룬다.

## docs index 반영 정책

`docs/benchmarks/baselines/index.md`는 committed baseline 의 entry point 다.
CI의 매 실행 artifact 는 이 index 에 자동 추가하지 않는다.

다만 CI artifact-only 정책 자체는 index 의 운영 원칙에 기록한다.
추후 CI artifact 중 일부를 reference baseline 으로 채택하면 그때 `Runner Groups`와 session row 에 추가한다.

## 다음 구현 후보

이 설계가 승인되면 다음 구현 후보는 둘 중 하나다.

1. **CI artifact-only workflow skeleton**
   - `.github/workflows/benchmark-artifacts.yml` 같은 workflow 를 추가한다.
   - latency warning 을 실패로 올리지 않는다.
   - artifact upload 를 구성한다.
2. **CI command smoke 문서/스크립트**
   - workflow 없이 로컬에서 CI와 같은 command sequence 를 재현하는 문서 또는 script 를 만든다.
   - 실제 CI 도입 전 command 안정성을 먼저 확인한다.

권장 순서는 1번이다. 이미 benchmark CLI가 exit code 정책을 갖고 있으므로 workflow는 그 규약을 그대로 쓰면 된다.

## 범위 밖

- GitHub Actions workflow 작성
- CI artifact upload 구현
- warning-as-failure 구현
- latency hard threshold 구현
- summary/history schema 변경
- cross-runner aggregate command 구현
- CI artifact 를 docs baseline 으로 자동 승격하는 기능

## 검증 계획

이번 단위는 문서와 정책 정렬만 변경한다.

- 현재 benchmark `Program` exit code 규칙을 대조한다.
- `BenchmarkRunIdentity`의 runner id/kind 환경 변수 규칙을 대조한다.
- `.github/workflows`가 아직 없음을 확인한다.
- `docs/benchmarks/baselines/index.md`, `DECISIONS.md`, root 상태 문서를 같은 정책으로 맞춘다.
- 임시 표기와 내부 모순을 검색한다.
- `git diff --check`, solution build/test 로 repository 상태를 확인한다.
