# Latency envelope 재산정과 gate 보류 조건 설계

- 날짜: 2026-06-24
- 상태: Accepted
- 관련 결정: D063, D069, D070, D079, D080, D082
- 관련 artifact:
  - `docs/benchmarks/baselines/2026-06-24/session-01/summary.json`
  - `docs/benchmarks/baselines/2026-06-24/session-02/summary.json`
  - `docs/benchmarks/baselines/2026-06-24/session-03/summary.json`
  - `docs/benchmarks/baselines/2026-06-24/history.json`
  - `docs/benchmarks/baselines/index.md`

## 목적

D070은 3개 baseline session 이 있어도 latency hard gate 를 바로 도입하지 않기로 했다. 이후 D079/D080으로
runner identity 와 comparison signal 이 생겼고, 2026-06-24에는 같은 runner/profile/workload 로 비교 가능한
baseline session 3개가 새로 생성됐다.

이번 설계는 그 3개 compatible baseline 을 근거로 현재 latency envelope 를 다시 산정하되, 이 값을 곧바로
warning-as-failure 또는 CI failure 조건으로 승격할 수 있는지 판단한다.

## 현재 확인된 사실

- 2026-06-24 date root 는 `session-01`, `session-02`, `session-03` 세 session 을 가진다.
- 각 session 은 `load` 3회, `open-loop` 3회 raw report 를 포함한다.
- `history.json` 기준 `session-count=3`, `hard-passed=true`, `failed-session-count=0`, `warning-count=0`이다.
- `comparison-compatible=true`, `unknown-runner-count=0`, `comparison-mismatch-count=0`이다.
- 비교 key 는 `tcp-loopback-saea-v1`, `SaeaTransport`, Windows 10.0.26200, X64, .NET 9.0.16,
  4096 bytes, 100 Hz, 30 seconds 로 일치한다.
- runner id 는 `local-unspecified`다. 같은 로컬 장비 안에서는 비교 가능하지만, 장기 gate 로 쓰기에는
  명시적 runner identity 가 아직 부족하다.

## 2026-06-24 reference envelope

아래 값은 hard threshold 가 아니라 현재 관측된 compatible reference envelope 다. 이후 compatible baseline 이
이 범위를 넘으면 회귀 의심 신호로 기록하되, 별도 결정 전까지 실패로 처리하지 않는다.

| 항목 | load | open-loop | 해석 |
| --- | ---: | ---: | --- |
| session 수 | 3 | 3 | 각 session 은 raw report 3회씩 포함한다. |
| run 수 | 9 | 9 | 3 session x 3 run 이다. |
| p50 max | 257.2 us | 281.7 us | 중앙 지연은 300 us 아래에서 유지됐다. |
| p99 max | 1020.4 us | 1006.5 us | 두 경로 모두 최악 p99가 1 ms 근처까지 흔들린다. |
| p99 median max | 967.5 us | 994.4 us | p99 중앙값도 1 ms 바로 아래까지 접근한다. |
| p99 growth ratio max | 1.23 | 1.06 | load 에서 한 session 의 전후반 p99 차이가 더 크다. |
| actual rate min | 99.8 Hz | 99.9 Hz | 목표 100 Hz 근처를 유지했다. |
| TCP HWM max | 1 | 2 | send queue 적체는 낮다. |
| dropped total | 0 | 0 | drop 없음. |
| payload error total | 0 | 0 | payload 오류 없음. |
| pool rented max | 0 | 0 | 종료 후 pool leak 없음. |

## 검토한 선택지

### 선택지 A: p99 1 ms를 즉시 hard latency gate 로 둔다

권장하지 않는다. 2026-06-24 표본에서 `load p99 max=1020.4 us`, `open-loop p99 max=1006.5 us`가 이미
1 ms를 조금 넘는다. delivery/drop/leak 은 모두 정상인데 1 ms hard gate 를 바로 적용하면 현재 정상 baseline 도
실패로 바뀐다.

### 선택지 B: 현재 수치를 non-failing reference envelope 로 기록한다

권장 방향이다. D080 comparison signal 로 같은 비교군인지는 확인되므로, 현재 envelope 를 후속 검토 기준으로
명시할 수 있다. 다만 표본이 하루치 로컬 실행이고 runner id 가 `local-unspecified`라서, 이 값을 자동 실패
조건으로 쓰지는 않는다.

### 선택지 C: CI workflow 와 warning-as-failure 를 함께 도입한다

아직 이르다. CI runner identity, artifact 보존 위치, 재실행 정책, false failure 허용 범위가 없다. CI를 먼저
붙이더라도 latency warning 은 실패로 올리지 않고, 기존 delivery/drop/leak hard gate 만 실패 조건으로 유지해야 한다.

## 결정

2026-06-24 compatible baseline 3개는 **reference latency envelope** 로 채택한다.

- 이 envelope 는 사람이 회귀를 판단할 때 보는 기준이다.
- `summary.json`, `history.json`, `docs/benchmarks/baselines/index.md`가 canonical evidence 다.
- `p99 max`가 1 ms 근처까지 관측됐으므로 1 ms hard SLO 는 현재 baseline 과 맞지 않는다.
- `warning-count > 0`, p99 envelope 초과, p99 growth 증가, HWM 증가가 발생해도 이번 결정만으로는 process failure 가 아니다.
- delivery/drop/leak hard gate 는 계속 유지한다. `hard-passed=false`는 지금처럼 실패다.

## warning-as-failure 승격 보류 조건

다음 조건이 충족될 때까지 warning-as-failure 는 보류한다.

1. 대상 runner 에 명시적 `HPS_BENCHMARK_RUNNER_ID`가 설정되어 `local-unspecified`가 아닌 runner id 로 raw report 가 생성된다.
2. 같은 runner/profile/workload 에서 서로 다른 날짜 root 3개 이상이 존재한다.
3. 각 날짜 root 는 compatible session 3개 이상을 포함한다.
4. 각 session 은 `load` 3회와 `open-loop` 3회를 포함한다.
5. 모든 session 의 delivery/drop/leak hard gate 가 통과한다.
6. `comparison-compatible=true`, `unknown-runner-count=0`, `comparison-mismatch-count=0`이 유지된다.
7. warning threshold 가 transient scheduling noise 와 실제 regression 을 구분할 수 있다는 검토 문서가 별도로 작성된다.

이 조건은 "언젠가 반드시 warning-as-failure 를 켠다"는 뜻이 아니다. 위 조건이 채워진 뒤에도 false failure 비용,
CI runner 안정성, artifact 보존 정책을 다시 검토해야 한다.

## CI gate 보류 조건

CI workflow 에 latency hard gate 를 넣는 작업은 다음 전제가 갖춰질 때까지 보류한다.

- CI runner identity 가 raw report metadata 로 고정된다.
- CI에서 생성한 raw report, summary, history artifact 를 보존할 위치가 정해진다.
- CI 실패 조건이 delivery/drop/leak hard gate 인지, latency warning 까지 포함하는지 별도 결정된다.
- local baseline 과 CI baseline 을 같은 envelope 로 비교하지 않는 정책이 문서화된다.

CI를 먼저 추가해야 한다면, 초기 CI는 build/test 와 기존 delivery/drop/leak hard gate 만 실패 조건으로 삼고,
latency/HWM/warning 은 artifact 로만 남긴다.

## 범위 밖

- warning-as-failure command option 구현
- CI workflow 작성
- latency hard threshold 구현
- summary/history schema 변경
- benchmark runner identity 추가 변경
- RIO/io_uring backend 성능 gate

## 검증 계획

이번 단위는 문서와 정책 정렬만 변경한다.

- 2026-06-24 `history.json`과 세 session `summary.json`의 수치를 대조한다.
- `docs/benchmarks/baselines/index.md`에 reference envelope 해석을 반영한다.
- `DECISIONS.md`와 decision archive 에 D082를 기록한다.
- `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`를 같은 다음 실행 지점으로 맞춘다.
- 임시 표기와 내부 모순을 검색한다.
- `git diff --check`, solution build/test 로 repository 상태를 확인한다.
