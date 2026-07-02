# D168 이후 io_uring reference date 지속 확장 설계

- 날짜: 2026-07-02
- 상태: Accepted
- 관련 결정: D153, D154, D158, D164, D167, D168, D169
- 관련 artifact: GitHub Actions run `28568500822`

## 목적

D168 원격 artifact gate 는 D167로 확장한 TCP 4-session, UDP 7-session reference history 를 실제 workflow envelope step 에 연결했고,
TCP/UDP 모두 signal-count 0을 기록했다.
이번 설계는 이 결과를 바로 fixed registration, zero-copy send, default promotion, latency hard gate 로 확장할지,
아니면 passing artifact 를 두 번째 date root 의 추가 reference session 으로 수동 채택해 reference 안정성을 더 높일지 결정한다.

## 현재 상태

- `ci-linux-iouring-x64-01/tcp` repository reference 는 `2026-07-01/session-01..03`,
  `2026-07-02/session-01` 상태다.
- `ci-linux-iouring-x64-01/udp` repository reference 는 `2026-07-01/session-01..06`,
  `2026-07-02/session-01` 상태다.
- D168 run `28568500822`:
  - workflow success
  - TCP/UDP raw report count 각각 6
  - TCP/UDP hard-passed true
  - TCP/UDP load/open-loop dropped-total, payload-error-total, pool-rented-max 모두 0
  - TCP envelope reference-summary-count 4, compatible true, signal-count 0
  - UDP envelope reference-summary-count 7, compatible true, signal-count 0

## 후보 평가

### 후보 A: fixed registration 또는 zero-copy send 구현

지금 열지 않는다.

D168 artifact 는 최적화 필요성을 보여주는 failure artifact 가 아니라, 두 date root reference 기준에서도 signal 0인 passing evidence 다.
fixed registration 과 zero-copy send 는 fixed buffer lifetime, in-flight deregister 금지, payload ownership, fallback path 를 함께 건드린다.
현재 drop, payload error, pool leak, hard gate failure, 반복 envelope signal 이 없으므로 이 변경을 바로 여는 근거가 부족하다.

### 후보 B: default promotion 또는 latency hard gate 승격

지금 열지 않는다.

`io_uring`은 여전히 Linux opt-in backend 이고, 현재 warning-count 는 D070/D125 기준 report-only latency soft signal 이다.
두 date root reference 가 시작됐지만 두 번째 date root 는 아직 1-session 뿐이다.
따라서 default backend 승격이나 latency hard gate 는 false gate 위험이 크고, 현재 evidence chain 의 목적과 맞지 않는다.

### 후보 C: D168 raw report 를 두 번째 date root session 으로 수동 채택

채택한다.

D168은 D167 reference 를 실제 원격 workflow 에서 사용했고 signal 0을 기록한 passing artifact 다.
이를 `2026-07-02/session-02`로 수동 채택하면 두 번째 date root 가 1-session provisional 상태에서 벗어나고,
future envelope comparison 이 단일 날짜 또는 단일 session outlier 에 덜 민감해진다.
이는 자동 채택이 아니라 D168 검토 결과에 따른 명시적 수동 채택이다.

## 결정

D169: D168 raw report 를 `ci-linux-iouring-x64-01` protocol별 두 번째 date root 의 다음 reference session 으로 수동 채택한다.

- TCP run `28568500822` -> `tcp/2026-07-02/session-02`
- UDP run `28568500822` -> `udp/2026-07-02/session-02`

각 target session 에는 raw report 6개만 복사하고, `summary.json`/`summary.md`는 repository 경로 기준으로 재생성한다.
date root history, protocol root history, baseline index 를 함께 갱신한다.

## 검증 계획

- 각 신규 session raw report count 가 6개인지 확인한다.
- TCP protocol root history 가 session-count 5, hard-passed true, comparison-compatible true 인지 확인한다.
- UDP protocol root history 가 session-count 8, hard-passed true, comparison-compatible true 인지 확인한다.
- 최신 session 기준 TCP/UDP envelope smoke 가 signal-count 0인지 확인한다.
- repository baseline artifact 에 local absolute path 가 들어가지 않는지 확인한다.
- `Hps.Benchmarks.Tests`와 `git diff --check`를 실행한다.

## 범위 밖

- 자동 repository baseline 채택
- latency hard gate 또는 warning-as-failure
- fixed registration 또는 zero-copy send
- `TransportFactory.CreateDefault()` promotion
- TCP/UDP JSON schema rename
