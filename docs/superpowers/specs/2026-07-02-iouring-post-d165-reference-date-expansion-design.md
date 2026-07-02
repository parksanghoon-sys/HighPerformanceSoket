# D165 이후 io_uring reference date 확장 설계

- 날짜: 2026-07-02
- 상태: Accepted
- 관련 결정: D153, D154, D158, D164, D165, D166
- 관련 artifact: GitHub Actions run `28566385562`

## 목적

D165 원격 artifact gate 는 D164로 확장한 TCP 3-session, UDP 6-session reference history 를 실제 workflow envelope step 에 연결했고,
TCP/UDP 모두 signal-count 0을 기록했다.
이번 설계는 이 결과를 바로 fixed registration, zero-copy send, default promotion, latency hard gate 로 확장할지,
아니면 passing artifact 를 두 번째 date root reference 로 수동 채택해 protocol reference 를 더 안정화할지 결정한다.

## 현재 상태

- `ci-linux-iouring-x64-01/tcp` repository reference 는 `2026-07-01/session-01..03` 상태다.
- `ci-linux-iouring-x64-01/udp` repository reference 는 `2026-07-01/session-01..06` 상태다.
- D165 run `28566385562`:
  - workflow success
  - TCP/UDP raw report count 각각 6
  - TCP/UDP hard-passed true
  - TCP/UDP drop, payload error, pool rented 모두 0
  - TCP envelope reference-summary-count 3, signal-count 0
  - UDP envelope reference-summary-count 6, signal-count 0

## 후보 평가

### 후보 A: fixed registration 또는 zero-copy send 구현

지금 열지 않는다.

D165 artifact 는 최적화가 필요하다는 failure evidence 가 아니라, 확장 reference 기준에서도 signal 0인 passing evidence 다.
fixed registration 과 zero-copy send 는 buffer registration lifetime, in-flight deregister 금지, fallback, completion ownership 을 건드리는 큰 변경이다.
현재 drop/payload-error/pool leak 또는 반복 signal 이 없으므로 이 변경을 바로 여는 근거가 부족하다.

### 후보 B: default promotion 또는 latency hard gate 승격

지금 열지 않는다.

`io_uring`은 여전히 Linux opt-in backend 다. 현재 reference 는 protocol별 provisional baseline 이며,
단일 CI runner의 짧은 artifact chain 을 근거로 default backend 승격이나 latency hard gate 를 적용하면 false gate 위험이 크다.
D125 이후 정책도 hard gate 보다는 runner/profile/protocol scoped envelope artifact 를 먼저 축적하는 방향이다.

### 후보 C: D165 raw report 를 두 번째 date root reference 로 수동 채택

채택한다.

D165는 D164 reference 를 실제 원격 workflow 에서 사용했고 signal 0을 기록한 passing artifact 다.
이를 `2026-07-02/session-01`로 수동 채택하면 같은 runner/protocol reference 가 단일 날짜에만 묶인 상태를 벗어나고,
future envelope comparison 이 날짜 단위 변동에 덜 민감해진다.
이는 자동 채택이 아니라 D165 검토 결과에 따른 명시적 수동 채택이다.

## 결정

D166: D165 raw report 를 `ci-linux-iouring-x64-01` protocol별 두 번째 date root reference 로 수동 채택한다.

- TCP run `28566385562` -> `tcp/2026-07-02/session-01`
- UDP run `28566385562` -> `udp/2026-07-02/session-01`

각 target session 에는 raw report 6개만 복사하고, `summary.json`/`summary.md`는 repository 경로 기준으로 재생성한다.
date root history, protocol root history, baseline index 를 함께 갱신한다.

## 검증 계획

- 각 신규 session raw report count 가 6개인지 확인한다.
- TCP protocol root history 가 session-count 4, hard-passed true, comparison-compatible true 인지 확인한다.
- UDP protocol root history 가 session-count 7, hard-passed true, comparison-compatible true 인지 확인한다.
- 최신 session 기준 TCP/UDP envelope smoke 가 signal-count 0인지 확인한다.
- repository baseline artifact 에 local absolute path 가 들어가지 않는지 확인한다.
- `Hps.Benchmarks.Tests`와 `git diff --check`를 실행한다.

## 범위 밖

- 자동 repository baseline 채택
- latency hard gate 또는 warning-as-failure
- fixed registration 또는 zero-copy send
- `TransportFactory.CreateDefault()` promotion
- TCP/UDP JSON schema rename
