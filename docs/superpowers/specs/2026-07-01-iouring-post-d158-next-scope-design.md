# D158 이후 io_uring 후속 범위 재평가 설계

- 날짜: 2026-07-01
- 상태: Accepted
- 관련 결정: D146, D151, D153, D158, D159
- 관련 baseline: `docs/benchmarks/baselines/runners/ci-linux-iouring-x64-01/`

## 목적

D158로 UDP provisional reference 를 4-session 으로 안정화했다.
이번 설계는 그 다음 작업을 fixed registration, zero-copy send, UDP pump 구조 변경, latency hard gate 중 어디로 둘지 재평가한다.

## 확인된 사실

- TCP/UDP `io_uring` benchmark artifact 경로는 동작한다.
- TCP/UDP protocol별 repository reference history 가 존재한다.
- UDP protocol root history 는 session-count 4, hard-passed true, comparison-compatible true 다.
- D158 updated UDP reference 로 `session-04` summary 를 envelope smoke 비교하면 `envelope-compatible=true`,
  `envelope-signal-count=0`이다.
- UDP candidate 3개에서 반복된 open-loop p50 signal 은 얇은 1-session reference 문제로 해석됐다.
- 모든 D155~D158 UDP evidence 에서 dropped total, payload-error total, pool-rented max 는 0이다.

## 후보 평가

### 후보 A: fixed payload registration cache 구현

지금 열지 않는다.

`IoUringRegisteredBufferSet` owner boundary 는 이미 존재하지만, TCP/UDP send payload 에 연결하려면
`RefCountedBuffer` 수명, fan-out 중복 등록, in-flight send 완료, close drain, deregistration 순서가 함께 얽힌다.
현재 D158 이후의 반복 signal 은 reference 안정화로 사라졌고, drop/leak/correctness 문제가 없으므로
이 큰 소유권 변경을 바로 여는 근거가 부족하다.

### 후보 B: zero-copy send 구현

지금 열지 않는다.

프로젝트 목표는 최종적으로 불필요한 복사를 줄이는 것이지만, 현재 benchmark 는 4096B x 100Hz에서
delivery/drop/leak hard gate 를 통과한다. zero-copy send 는 kernel 지원 여부와 fallback, payload pinning,
completion ownership 을 새로 설계해야 하므로 report-only latency signal 만으로 착수하기에는 범위가 크다.

### 후보 C: UDP pump 구조 변경

지금 열지 않는다.

D143 receive window 이후 UDP hard gate 는 통과하고, D158 reference 안정화 후 open-loop p50 signal 도 smoke 에서 사라졌다.
현재 evidence 로는 UDP pump 구조 변경이 필요한 failure mode 가 남아 있지 않다.

### 후보 D: latency hard gate 또는 warning-as-failure 승격

지금 열지 않는다.

TCP와 UDP 모두 아직 `io_uring` protocol별 provisional reference 단계다.
warning-count 는 D070 전역 soft threshold 에서 나온 값이며, D125 이후 runner/profile scoped envelope 로 따로 본다.
새 hard gate 를 만들려면 여러 remote run 과 날짜 root 에서 stable envelope 를 먼저 확인해야 한다.

### 후보 E: D158 reference-present 원격 artifact gate

채택한다.

repository reference 를 바꿨으므로, 다음으로 필요한 evidence 는 원격 Linux runner 가 새 UDP reference history 를 읽어
future candidate artifact 의 `envelope.json`/`envelope.md`를 어떻게 생성하는지 확인하는 것이다.
이는 구현 변경 없이 artifact chain 과 reference 안정화 효과를 검증하는 최소 단위다.

## 결정

D159: D158 이후 다음 단위는 fixed registration/zero-copy/default promotion 이 아니라,
updated UDP reference 가 반영된 `iouring-benchmark-artifacts.yml` 원격 artifact gate 로 둔다.

## 검증 조건

원격 artifact 검토 시 다음을 확인한다.

- workflow conclusion success
- TCP/UDP baseline, summary, history, envelope exit code 0
- TCP/UDP raw report count 6 이상
- TCP/UDP hard-passed true
- UDP envelope signal count 가 D158 이전 반복 p50 signal 과 달라졌는지
- drop/payload-error/pool-rented 가 0인지

## 범위 밖

- fixed payload registration cache 구현
- zero-copy send 구현
- UDP pump 구조 변경
- latency hard gate 또는 warning-as-failure
- `TransportFactory.CreateDefault()` promotion
- 자동 repository baseline 채택

