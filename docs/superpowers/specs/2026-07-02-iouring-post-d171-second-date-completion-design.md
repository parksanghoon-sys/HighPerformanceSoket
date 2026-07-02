# D171 이후 io_uring 두 번째 date root 완성 설계

- 날짜: 2026-07-02
- 상태: Accepted
- 관련 결정: D168, D169, D170, D171, D172
- 관련 artifact: GitHub Actions run `28569649366`

## 목적

D171 원격 artifact gate 는 D170으로 확장한 TCP 5-session, UDP 8-session reference history 를 실제 workflow envelope step 에 연결했고,
TCP/UDP 모두 signal-count 0을 기록했다.
이번 설계는 이 결과를 최적화 구현의 근거로 볼지, 아니면 두 번째 date root 를 3-session reference 로 완성하는 표본으로 볼지 결정한다.

## 현재 상태

- TCP repository reference 는 `2026-07-01/session-01..03`, `2026-07-02/session-01..02` 상태다.
- UDP repository reference 는 `2026-07-01/session-01..06`, `2026-07-02/session-01..02` 상태다.
- D171 run `28569649366`:
  - workflow success
  - TCP/UDP raw report count 각각 6
  - TCP/UDP hard-passed true
  - TCP/UDP load/open-loop dropped-total, payload-error-total, pool-rented-max 모두 0
  - TCP envelope reference-summary-count 5, compatible true, signal-count 0
  - UDP envelope reference-summary-count 8, compatible true, signal-count 0

## 후보 평가

### 후보 A: fixed registration 또는 zero-copy send 구현

지금 열지 않는다.

D171은 drop, payload error, pool leak, hard gate failure, envelope signal 을 만들지 않았다.
fixed registration 과 zero-copy send 는 native buffer lifetime 과 in-flight ownership 을 건드리는 큰 변경이므로,
현재 evidence 만으로 바로 여는 것은 과하다.

### 후보 B: latency hard gate 또는 default promotion 승격

지금 열지 않는다.

warning-count 는 계속 report-only soft signal 이고, `io_uring`은 아직 opt-in backend 다.
두 번째 date root 가 2-session까지 안정화됐지만 hard gate/default 승격은 runner/profile scoped threshold 와 promotion policy 가 별도로 필요하다.

### 후보 C: D171 raw report 를 두 번째 date root session-03 으로 수동 채택

채택한다.

D171은 D170 reference 를 원격 workflow 에서 사용했고 signal 0을 기록한 passing artifact 다.
이를 `2026-07-02/session-03`으로 채택하면 두 번째 date root 가 3-session reference 가 되어,
기존 local/CI baseline 흐름에서 반복적으로 사용한 최소 date-root 안정화 기준과 맞아진다.
이는 자동 채택이 아니라 D171 검토 결과에 따른 명시적 수동 채택이다.

## 결정

D172: D171 raw report 를 `ci-linux-iouring-x64-01` protocol별 두 번째 date root 의 `session-03` reference 로 수동 채택한다.

- TCP run `28569649366` -> `tcp/2026-07-02/session-03`
- UDP run `28569649366` -> `udp/2026-07-02/session-03`

각 target session 에는 raw report 6개만 복사하고, `summary.json`/`summary.md`는 repository 경로 기준으로 재생성한다.
date root history, protocol root history, baseline index 를 함께 갱신한다.

## 검증 계획

- 각 신규 session raw report count 가 6개인지 확인한다.
- TCP protocol root history 가 session-count 6, hard-passed true, comparison-compatible true 인지 확인한다.
- UDP protocol root history 가 session-count 9, hard-passed true, comparison-compatible true 인지 확인한다.
- 최신 session 기준 TCP/UDP envelope smoke 가 signal-count 0인지 확인한다.
- repository baseline artifact 에 local absolute path 가 들어가지 않는지 확인한다.
- `Hps.Benchmarks.Tests`와 `git diff --check`를 실행한다.

## 범위 밖

- 자동 repository baseline 채택
- latency hard gate 또는 warning-as-failure
- fixed registration 또는 zero-copy send
- `TransportFactory.CreateDefault()` promotion
