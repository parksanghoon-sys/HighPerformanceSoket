# D162 이후 io_uring protocol reference 확장 설계

- 날짜: 2026-07-01
- 상태: Accepted
- 관련 결정: D153, D154, D158, D160, D162, D163
- 관련 baseline: `docs/benchmarks/baselines/runners/ci-linux-iouring-x64-01/`

## 목적

D160과 D162 원격 artifact 는 updated reference 기준으로 TCP/UDP envelope signal 0을 확인했다.
이번 설계는 이 artifact 를 바로 최적화 구현 근거로 쓸지, 아니면 protocol별 repository reference 를 더 안정화하는 표본으로
수동 채택할지 결정한다.

## 현재 상태

- TCP protocol reference 는 `2026-07-01/session-01` 1개뿐이다.
- UDP protocol reference 는 D158 이후 `2026-07-01/session-01..04` 4개다.
- D160 run `28495804466`:
  - workflow success
  - TCP/UDP raw report count 각각 6
  - TCP/UDP envelope compatible true, signal-count 0
- D162 run `28497147332`:
  - workflow success
  - TCP/UDP raw report count 각각 6
  - TCP/UDP envelope compatible true, signal-count 0
  - Markdown label 변경도 원격 artifact 에 반영됐다.

## 후보 평가

### 후보 A: fixed registration 또는 zero-copy send 구현

지금 열지 않는다.

D160/D162는 최적화 필요성을 보여주는 failure artifact 가 아니라, 현재 protocol reference envelope 안에서 동작한다는
증거다. drop, payload error, pool leak, hard gate failure 가 없으므로 큰 소유권 변경을 열 근거가 부족하다.

### 후보 B: latency hard gate 또는 warning-as-failure 승격

지금 열지 않는다.

TCP/UDP 모두 warning-count 는 남아 있지만 D070 전역 soft threshold 기준이다.
D125 이후 판단 기준은 runner/profile/protocol scoped envelope 이며, D160/D162는 이 envelope 에서 signal 0이다.
warning-count 만으로 hard gate 를 승격하면 D125/D159 판단과 충돌한다.

### 후보 C: D160/D162 raw report 를 protocol reference session 으로 수동 채택

채택한다.

D160/D162는 모두 hard gate 와 envelope 를 통과했다.
TCP reference 는 1-session 뿐이라 특히 얇고, UDP도 D158 이후 안정화됐지만 추가 passing session 을 더하면 future envelope 가
단일 outlier 에 덜 흔들린다.
이는 자동 baseline 채택이 아니라 D160/D162 검토 결과에 따른 수동 reference 확장이다.

## 결정

D163: D160/D162 raw report 를 `ci-linux-iouring-x64-01` protocol별 provisional reference session 으로 수동 채택한다.

- TCP run `28495804466` -> `tcp/2026-07-01/session-02`
- TCP run `28497147332` -> `tcp/2026-07-01/session-03`
- UDP run `28495804466` -> `udp/2026-07-01/session-05`
- UDP run `28497147332` -> `udp/2026-07-01/session-06`

각 target session 에는 raw report 6개만 복사하고, `summary.json`/`summary.md`는 repository 경로 기준으로 재생성한다.
date root history, protocol root history, baseline index 도 함께 갱신한다.

## 검증 계획

- 각 신규 session raw report count 가 6개인지 확인한다.
- TCP protocol root history 가 session-count 3, hard-passed true, comparison-compatible true 인지 확인한다.
- UDP protocol root history 가 session-count 6, hard-passed true, comparison-compatible true 인지 확인한다.
- updated TCP/UDP reference history 로 각 최신 session summary 를 envelope smoke 비교해 signal-count 0을 확인한다.
- repository baseline artifact 에 local absolute path 가 들어가지 않는지 확인한다.
- `Hps.Benchmarks.Tests`와 `git diff --check`를 실행한다.

## 범위 밖

- 자동 repository baseline 채택
- latency hard gate 또는 warning-as-failure
- fixed registration 또는 zero-copy send
- `TransportFactory.CreateDefault()` promotion
- TCP/UDP JSON schema rename
