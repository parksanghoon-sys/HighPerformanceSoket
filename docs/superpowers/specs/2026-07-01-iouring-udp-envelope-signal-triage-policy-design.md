# io_uring UDP envelope signal triage 정책 설계

- 날짜: 2026-07-01
- 상태: Accepted
- 관련 결정: D125, D151, D153, D154, D155, D156
- 관련 workflow: `.github/workflows/iouring-benchmark-artifacts.yml`
- 관련 baseline: `docs/benchmarks/baselines/runners/ci-linux-iouring-x64-01/udp/history.json`

## 목적

D155 reference-present run `28493590950`은 TCP/UDP `envelope.json`/`envelope.md` 생성 경로를 검증했다.
TCP는 signal 0이지만, UDP는 `envelope-signal-count=2`를 기록했다.

이번 설계는 이 UDP signal 을 바로 fixed registration, zero-copy, default promotion 같은 구현으로 연결할지,
아니면 provisional reference 상태를 먼저 안정화할지 정한다.

## 현재 evidence

### D154 provisional reference

reference artifact 는 run `28492234252`에서 채택했다.

- UDP source-report-count: 6
- UDP hard-passed: true
- UDP warning-count: 3
- UDP load p99 max: 1623.8 us
- UDP open-loop p99 max: 1322.0 us
- UDP dropped/payload-error/pool-rented: 0

### D155 candidate

candidate artifact 는 run `28493590950`이다.

- UDP source-report-count: 6
- UDP hard-passed: true
- UDP warning-count: 2
- UDP load p99 max: 2033.4 us
- UDP open-loop p99 max: 1312.4 us
- UDP dropped/payload-error/pool-rented: 0
- UDP envelope-compatible: false
- UDP envelope-signal-count: 2
- signals:
  - load `p99-max-us`: reference 1623.8, limit 1948.56, candidate 2033.4
  - open-loop `p50-median-us`: reference 158.6, limit 258.6, candidate 1156.3

## 해석

D155는 두 가지를 동시에 보여준다.

1. 좋은 신호: reference-present envelope artifact path 는 정상 동작한다.
2. 주의 신호: UDP latency distribution 은 1-session provisional reference 로 안정적으로 설명되지 않는다.

특히 open-loop `p50-median-us` signal 은 reference median 이 158.6 us로 매우 낮고,
candidate median 은 1156.3 us다. 같은 candidate 의 open-loop `p99-max-us`는 reference limit 안에 있다.
즉 이 signal 은 delivery/drop/leak 실패나 queue backlog 가 아니라, 초기 reference session 하나가
중앙값 envelope 를 너무 좁게 만든 상태일 가능성이 높다.

load `p99-max-us` signal 은 candidate 2033.4 us가 reference limit 1948.56 us를 약 84.84 us 초과한다.
이 역시 hard gate 실패가 아니며, D153 기준으로는 report-only triage 대상이다.

## 검토한 선택지

### 선택지 A: fixed registration 또는 zero-copy 구현을 바로 연다

채택하지 않는다.
현재 failure 는 drop/payload/pool leak 이 아니라 latency envelope signal 이고,
reference 는 1-session provisional baseline 이다.
이 상태에서 최적화 구현을 열면 원인이 measurement variance 인지, UDP pump 구조 문제인지,
payload registration 비용인지 구분하지 못한다.

### 선택지 B: envelope threshold 를 즉시 완화한다

채택하지 않는다.
threshold 를 넓히면 D155 signal 은 사라지지만, reference 가 얇다는 문제를 숨긴다.
D125의 목적은 warning-count 와 별도로 runner/profile scoped signal 을 드러내는 것이므로,
초기 신호를 없애기보다 triage policy 로 해석해야 한다.

### 선택지 C: reference-present artifact 를 추가 수집하고 반복성으로 판단한다

채택한다.
D154 reference 와 D155 candidate 는 모두 같은 날짜, 같은 runner, 같은 profile 이지만 표본이 너무 적다.
추가 run 을 모아 signal 이 반복되는지, 특정 metric 만 튀는지, hard gate 는 계속 유지되는지 확인한다.

## 결정

D156: D155 UDP envelope signal 은 즉시 최적화 구현으로 연결하지 않고, 추가 reference-present artifact 로 반복성을 확인한다.

- D155 run 은 reference-present envelope path 검증으로 수락한다.
- UDP signal 2개는 report-only triage signal 로 기록한다.
- fixed registration, zero-copy send, default backend promotion 은 열지 않는다.
- 다음 단위는 `iouring-benchmark-artifacts.yml`을 추가로 실행해 최소 2개 reference-present candidate 를 더 수집하는 것이다.
- 총 3개 reference-present candidate(D155 포함)를 모은 뒤 다음 기준으로 판단한다.
  - 같은 UDP metric 이 2회 이상 반복 signal 이면 UDP latency triage 설계를 연다.
  - signal 이 서로 다른 metric 에 흩어지거나 사라지면 provisional reference envelope 안정화 정책을 설계한다.
  - hard-passed=false, drop/payload-error/pool-rented 증가가 나오면 성능 triage 가 아니라 correctness/reliability issue 로 우선 처리한다.

## 다음 실행 절차

1. 사용자 push 가 필요 없는 상태인지 `git status --short --branch`로 확인한다.
2. `iouring-benchmark-artifacts.yml`을 `workflow_dispatch`로 실행한다.
3. artifact 를 내려받아 TCP/UDP summary 와 envelope 를 확인한다.
4. D155와 같은 형식으로 signal metric, reference, limit, candidate 를 기록한다.
5. 같은 절차를 한 번 더 반복해 reference-present candidate 를 총 3개로 만든다.
6. 세 candidate 의 UDP signal 을 표로 정리한다.
7. 반복 signal 여부에 따라 다음 설계 단위를 정한다.

## 기록 위치

- 원격 run 결과는 `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`,
  `docs/agent-state/changelog/2026-07.md`, `docs/agent-state/decisions/2026-07.md`에 기록한다.
- 사람이 빠르게 baseline 상태를 볼 수 있도록 `docs/benchmarks/baselines/index.md`의
  `ci-linux-iouring-x64-01 io_uring Protocol Reference` 섹션에 candidate summary 를 추가한다.
- candidate raw report 는 repository baseline 으로 자동 채택하지 않는다.
  D153과 별도 채택 판단 전까지는 GitHub artifact evidence 로만 둔다.

## 범위 밖

- latency hard gate 또는 warning-as-failure
- envelope threshold 변경
- automatic repository baseline adoption
- fixed payload registration cache
- zero-copy send
- `TransportFactory.CreateDefault()` promotion
- UDP pump 구조 변경

## 검증 계획

- D155 UDP signal 값을 원격 artifact 와 대조한다.
- D153 provisional reference 정책과 충돌하지 않는지 확인한다.
- D125 report-only envelope 정책과 충돌하지 않는지 확인한다.
- placeholder scan 으로 미정 항목이 남지 않았는지 확인한다.
- 문서 변경은 `git diff --check`로 검증한다.

## 실행 결과

D156 기준으로 reference-present candidate 2개를 추가 수집했다.

| run id | workflow | TCP envelope signals | UDP envelope signals | UDP repeated metric |
| --- | --- | ---: | ---: | --- |
| 28493590950 | success | 0 | 2 | open-loop `p50-median-us` |
| 28494135787 | success | 0 | 2 | open-loop `p50-median-us` |
| 28494404015 | success | 0 | 1 | open-loop `p50-median-us` |

세 run 모두 UDP hard-passed true, dropped total 0, payload-error total 0, pool-rented max 0이다.
따라서 correctness/reliability failure 는 아니지만, D156 반복성 기준상 UDP open-loop `p50-median-us` signal 은 3/3 반복됐다.

D157 기준으로 다음 단위는 UDP open-loop p50 median 반복 signal triage 설계다.
fixed registration, zero-copy send, latency hard gate 는 아직 열지 않는다.
