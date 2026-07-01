# io_uring protocol별 reference baseline 수동 채택 정책 설계

- 날짜: 2026-07-01
- 상태: Accepted
- 관련 결정: D090, D095, D125, D147, D151, D152, D153
- 관련 workflow: `.github/workflows/iouring-benchmark-artifacts.yml`
- 관련 index: `docs/benchmarks/baselines/index.md`

## 목적

D151로 Linux `io_uring` benchmark workflow 에 protocol별 envelope comparison step 을 추가했고,
D152 원격 run `28492234252`에서 reference history 없음 skip 경로가 정상임을 확인했다.

하지만 repository 에 아직 `ci-linux-iouring-x64-01/tcp/history.json`과
`ci-linux-iouring-x64-01/udp/history.json`이 없어서, envelope comparison 은 실제 기준값을 계산하지 못한다.
이번 설계는 원격 artifact 를 언제, 어디에, 어떤 검증 뒤 repository reference baseline 으로 수동 채택할지 정한다.

## 현재 evidence

원격 run `28492234252`의 artifact 는 다음 값을 가진다.

```text
iouring-benchmark-artifacts-2026-07-01-github-28492234252-1/
  tcp/2026-07-01/session-01/
  tcp/history.json
  tcp/history.md
  udp/2026-07-01/session-01/
  udp/history.json
  udp/history.md
  summary.md
  dotnet-info.txt
```

- TCP source-report-count: 6
- TCP hard-passed: true
- TCP warning-count: 6
- TCP load p99 max: 4298.8 us
- TCP open-loop p99 max: 5588.6 us
- TCP dropped/payload-error/pool-rented: 0
- TCP HWM max: 1
- UDP source-report-count: 6
- UDP hard-passed: true
- UDP warning-count: 3
- UDP load p99 max: 1623.8 us
- UDP open-loop p99 max: 1322.0 us
- UDP dropped/payload-error/pool-rented: 0
- UDP HWM max: 0
- envelope output: reference history 없음으로 skip, exit 0

## 기존 D095와 차이

D095의 Windows CI baseline 채택 정책은 `warning-count=0`을 필수 조건으로 둔다.
이는 이미 SAEA/CI reference 를 깨끗한 기준값으로 보존하려는 정책이다.

Linux `io_uring`은 상황이 다르다.

1. 아직 protocol별 repository reference history 가 없다.
2. 현재 warning 은 runner/profile/protocol envelope 기준이 아니라 D070 전역 soft threshold 에서 나온 값이다.
3. reference 가 없으면 D151 envelope comparison 이 계속 skip 되어, 향후 run 의 상대적 악화 여부도 볼 수 없다.
4. 이번 채택은 latency hard gate 나 warning-as-failure 가 아니라 report-only 기준값을 만드는 것이다.

따라서 D095를 그대로 복사하지 않고, `io_uring` 초기 protocol reference 에 한해 warning-count 를 채택 차단 조건에서 제외한다.
대신 index 와 결정 문서에 provisional reference 라고 명시하고, gate 승격 근거로 사용하지 않는다.

## 저장 구조

`io_uring` benchmark 는 같은 runner id 아래에서 TCP와 UDP를 protocol root 로 분리한다.
TCP와 UDP는 profile, scenario, transport path 가 달라 같은 runner root history 에 섞으면 안 된다.

```text
docs/benchmarks/baselines/runners/
  ci-linux-iouring-x64-01/
    tcp/
      2026-07-01/
        session-01/
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
      history.json
      history.md
    udp/
      2026-07-01/
        session-01/
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
      history.json
      history.md
```

각 protocol root 는 `BaselineHistoryReader`의 parent-root discovery 규칙과 맞는다.
따라서 `tcp/` 또는 `udp/`를 history input 으로 쓰면 바로 아래 날짜 root 를 읽을 수 있다.

## 채택 전 필수 조건

`io_uring` protocol별 artifact 를 repository reference baseline 으로 채택하려면 다음을 모두 만족해야 한다.

1. source run 은 `iouring-benchmark-artifacts.yml`의 성공한 `workflow_dispatch` run 이어야 한다.
2. artifact 이름은 `iouring-benchmark-artifacts-<yyyy-mm-dd>-github-<run-id>-<attempt>` 형식이어야 한다.
3. artifact 내부는 `tcp/<yyyy-mm-dd>/session-01`과 `udp/<yyyy-mm-dd>/session-01`을 모두 가져야 한다.
4. 각 protocol session 은 raw report 6개를 가져야 한다.
   - load 3회
   - open-loop 3회
   - payload 4096 bytes
   - target rate 100 Hz
5. raw report metadata 는 artifact runner id 와 일치해야 한다.
   - `runner-id=ci-linux-iouring-x64-01`
   - `runner-kind=ci`
   - `transport-backend=IoUringTransport`
   - TCP profile/scenario 는 TCP loopback io_uring 계열이어야 한다.
   - UDP profile/scenario 는 UDP loopback io_uring 계열이어야 한다.
6. 각 protocol `summary.json`은 다음 값을 가져야 한다.
   - `source-report-count=6`
   - `hard-passed=true`
   - `comparison-compatible=true`
   - `unknown-runner-count=0`
   - dropped total 0
   - payload-error total 0
   - pool-rented max 0
7. 각 protocol `history.json`은 다음 값을 가져야 한다.
   - `session-count=1`
   - `hard-passed=true`
   - `comparison-compatible=true`
8. raw report 와 summary/history 안에 로컬 절대 경로가 없어야 한다.
9. root `summary.md`는 TCP/UDP baseline, summary, history, envelope exit code 를 모두 0으로 기록해야 한다.
10. reference 없음 skip 때문에 envelope artifact 가 없을 수 있다.
    이것은 첫 reference baseline 채택에서는 정상이다.

초기 `io_uring` reference 에서는 `warning-count > 0`을 채택 차단 조건으로 보지 않는다.
단, 해당 baseline 은 provisional reference 로 표기하고, warning-as-failure 또는 latency hard gate 의 근거로 사용하지 않는다.

## 채택 절차

1. GitHub run id 와 artifact name 을 기록한다.
2. artifact 를 임시 directory 로 다운로드한다.
3. TCP/UDP protocol별 raw report 6개, `summary.json`, `history.json`을 확인한다.
4. metadata, hard gate, comparison compatibility, drop/payload/pool 값을 확인한다.
5. target directory 를 정한다.

```text
docs/benchmarks/baselines/runners/ci-linux-iouring-x64-01/tcp/YYYY-MM-DD/session-NN/
docs/benchmarks/baselines/runners/ci-linux-iouring-x64-01/udp/YYYY-MM-DD/session-NN/
```

첫 채택은 `session-01`을 사용한다.
같은 date root 에 후속 artifact 를 추가할 경우에는 다음 available `session-NN`을 사용한다.

6. artifact 의 protocol session directory 에서 raw report 6개만 target session directory 로 복사한다.
7. repository 경로 기준으로 protocol별 summary 를 다시 생성한다.

```powershell
dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj --no-build --no-restore -- `
  --summarize-baseline <target-session-dir> `
  --summary <target-session-dir>/summary.json `
  --summary-md <target-session-dir>/summary.md
```

8. protocol date root history 를 다시 생성한다.

```powershell
dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj --no-build --no-restore -- `
  --summarize-baseline-history <target-protocol-date-root> `
  --history <target-protocol-date-root>/history.json `
  --history-md <target-protocol-date-root>/history.md
```

9. protocol root history 를 다시 생성한다.

```powershell
dotnet run --project tests/Hps.Benchmarks/Hps.Benchmarks.csproj --no-build --no-restore -- `
  --summarize-baseline-history <target-protocol-root> `
  --history <target-protocol-root>/history.json `
  --history-md <target-protocol-root>/history.md
```

10. `docs/benchmarks/baselines/index.md`에 protocol별 runner group, date-level history, session row, provisional 해석 메모를 추가한다.
11. `--compare-baseline-envelope` smoke 를 실행해 새 reference history 를 읽을 수 있는지 확인한다.
    candidate 는 같은 artifact 의 regenerated `summary.json` 또는 protocol root `history.json`을 사용할 수 있다.
12. `git diff --check`와 현재 변경 범위에 맞는 benchmark focused test 또는 solution build/test 를 실행한다.
13. changelog/TODO/decision 에 source run id, artifact name, provisional warning 상태를 기록한다.

## 채택하지 않는 항목

다음은 repository baseline 에 넣지 않는다.

- GitHub artifact zip 자체
- artifact download root directory 이름
- GitHub run id 를 session directory 이름으로 쓴 경로
- protocol root 없이 TCP/UDP를 같은 runner root 에 섞은 history
- raw report 수가 6개가 아닌 artifact
- hard-passed=false artifact
- comparison-compatible=false artifact
- dropped/payload-error/pool-rented 가 0이 아닌 artifact
- runner metadata 가 unknown 이거나 `ci-linux-iouring-x64-01`와 맞지 않는 artifact
- 로컬 절대 경로가 포함된 artifact

## 현재 후보 판단

| run id | artifact | protocol | hard passed | warnings | comparison compatible | 채택 판단 |
| --- | --- | --- | --- | ---: | --- | --- |
| 28492234252 | `iouring-benchmark-artifacts-2026-07-01-github-28492234252-1` | TCP | true | 6 | true | provisional reference 로 채택 가능 |
| 28492234252 | `iouring-benchmark-artifacts-2026-07-01-github-28492234252-1` | UDP | true | 3 | true | provisional reference 로 채택 가능 |

warning-count 는 전역 soft threshold 기준 signal 이므로 기록하되 채택을 막지 않는다.
대신 이 reference 는 future `io_uring` run 의 상대 비교를 위한 시작점이며, 성능 목표 달성 주장이나 default backend promotion 근거가 아니다.

## 결정

D153: D152 artifact 는 protocol별 `io_uring` repository reference baseline 의 첫 provisional 후보로 수동 채택할 수 있다.

- 자동 채택은 하지 않는다.
- TCP와 UDP는 `ci-linux-iouring-x64-01/tcp`, `ci-linux-iouring-x64-01/udp` 아래에 각각 보관한다.
- raw report 6개만 복사하고, summary/history/index 는 repository 경로 기준으로 재생성한다.
- warning-count > 0은 초기 `io_uring` reference 에 한해 채택 차단 조건이 아니다.
- provisional reference 는 latency hard gate, warning-as-failure, default backend promotion 근거가 아니다.
- 다음 구현 단위는 run `28492234252` artifact 를 위 절차로 수동 채택하고,
  protocol별 envelope command smoke 로 reference history 경로가 실제 동작하는지 확인하는 것이다.

## 검증 계획

- D095와 충돌하는 차이를 명시했는지 확인한다.
- D151 protocol split reference path 와 저장 구조가 일치하는지 확인한다.
- `BaselineHistoryReader` parent-root discovery 규칙과 protocol root 구조가 맞는지 확인한다.
- placeholder scan 으로 미정 항목이 남지 않았는지 확인한다.
- 문서 변경은 `git diff --check`로 검증한다.
