# Phase 6 io_uring boundary 다음 후보 설계

- 날짜: 2026-06-29
- 상태: Accepted
- 관련 결정: D090, D095, D119, D122, D125, D128, D131, D132
- 관련 계획: `PLAN.md` Phase 6

## 배경

D131로 D127/D130 원격 CI artifact 검증과 두 번째 CI repository baseline 채택이 완료됐다.
`ci-windows-x64-01` runner root history 는 2-session, hard-passed true, warning-count 0,
comparison-compatible true 상태다.

이 상태에서 다음 후보를 다시 고른다.

현재 중요한 사실은 다음과 같다.

- CI runner evidence 는 2-date-root/2-session 이며, 아직 latency hard gate 또는 warning-as-failure 로 승격하기에는 표본이 부족하다.
- D125/D127의 envelope signal 은 report-only 이고, 이번 CI artifact 도 p99 upper-bound signal 2개를 기록했지만 workflow failure 로 쓰지 않는다.
- RIO backend 는 TCP/UDP IPv4 opt-in 경계, RIO UDP delivery gate, address-family-aware sample selector 까지 갖췄다.
- D119/D122 기준으로 `TransportFactory.CreateDefault()`는 계속 SAEA default 이며, RIO full IPv6/default promotion 은 deferred 상태가 맞다.
- `src/Hps.Transport.IoUring/` project 는 아직 없다.
- `PLAN.md`의 다음 OS backend 단계는 Phase 6 Linux io_uring 이다.

## 후보 평가

### 후보 A: CI latency hard gate 또는 warning-as-failure 승격

지금 열지 않는다.

CI baseline 은 2-session 뿐이고, 이번 D131 artifact 는 기존 1-session reference 대비 p99 envelope signal 2개를 기록했다.
이는 즉시 실패 조건으로 쓰기보다 CI hosted runner 변동성을 더 쌓아야 한다는 신호다.

게다가 D125는 runner/profile scoped 판단을 별도 artifact 로 분리했을 뿐,
그 signal 을 process failure 로 승격하는 정책을 아직 정하지 않았다.

### 후보 B: RIO default promotion 또는 full IPv6 구현

지금 열지 않는다.

RIO는 현재 IPv4-only opt-in backend 로 명시되어 있고, sample host 는 address-family-aware fallback 을 제공한다.
full IPv6는 TCP/UDP sockaddr, dual-mode socket, scope id, benchmark artifact, default promotion 기준이 함께 얽힌다.
현재 v1 목표를 진행하는 데 즉시 blocking 이 아니므로 `P2_LATER`를 유지한다.

### 후보 C: server-level diagnostics public API

지금 열지 않는다.

Transport diagnostics snapshot 은 이미 benchmark/test에서 소비 가능하다.
실제 host/metrics exporter 요구가 없는 상태에서 `BrokerServer` public API를 넓히면 장기 API 부담이 생긴다.
`P3_NICE` deferred 상태가 계속 적절하다.

### 후보 D: Phase 6 Linux io_uring boundary 설계

채택한다.

이 후보는 `PLAN.md`의 다음 backend 축과 직접 맞고, 기존 RIO 경험을 재사용할 수 있다.
단, 현재 실행 환경은 Windows 이므로 Linux 전용 integration 을 바로 검증할 수 없다.
따라서 첫 단위는 full io_uring TCP/UDP pump 가 아니라 다음으로 제한한다.

1. `src/Hps.Transport.IoUring/` project skeleton.
2. OS/capability probe 와 status model.
3. non-Linux 에서 명시적 unsupported boundary.
4. 기본 `TransportFactory.CreateDefault()`가 계속 SAEA임을 고정.
5. native P/Invoke shape 는 후속 구현 계획에서 별도 task 로 분리.

이 방식이면 Windows에서도 compile/test 가능한 contract 를 먼저 세우고,
Linux host가 필요해지는 지점을 명확히 분리할 수 있다.

## 결정

D132로 다음 실행 후보를 **Phase 6 Linux io_uring backend boundary 설계와 첫 구현 계획**으로 둔다.

첫 구현 후보는 `Hps.Transport.IoUring` skeleton/capability probe/unsupported guard 다.
이 단계는 상위 `ITransport` public 계약을 넓히지 않고, default factory 를 바꾸지 않는다.

## 첫 implementation boundary

### 포함

- `Hps.Transport.IoUring` project 추가.
- `IoUringCapabilityStatus`와 `IoUringCapabilityProbe`.
- `IoUringTransport` opt-in root type skeleton.
- non-Linux 환경에서 `UnsupportedOperatingSystem`을 반환하는 probe test.
- default factory 가 SAEA를 유지하는 regression test.
- solution 등록과 상태 문서 갱신.

### 제외

- 실제 `io_uring_setup`/`io_uring_enter` P/Invoke.
- TCP/UDP send/receive pump.
- fixed buffer registration.
- Linux integration test.
- default backend promotion.
- Windows에서 Linux 동작을 mock 으로 성공처럼 보이게 하는 테스트.

## 검증 전략

첫 task 는 Windows에서도 검증 가능해야 한다.

- Red: `IoUringCapabilityProbe` type 부재를 assertion failure 로 확인한다.
- Green: non-Linux 에서 `UnsupportedOperatingSystem`을 반환하고, default factory 가 SAEA를 유지한다.
- 프로젝트 추가 후 solution build/test 를 통과시킨다.

Linux 전용 native behavior 는 후속 task 에서 Linux host 또는 CI matrix 가 준비된 뒤 별도 gate 로 다룬다.

## 범위 밖

- CI gate 승격.
- RIO default promotion.
- RIO full IPv6.
- server diagnostics public API.
- io_uring 성능 벤치마크.

## 검증

- D131 이후 CI baseline 상태와 D090/D095/D125/D128 정책 대조.
- RIO deferred backlog 와 D119/D122 결정 대조.
- `src/Hps.Transport.IoUring/` 부재 확인.
- `PLAN.md` Phase 6 목표와 첫 implementation boundary 정합성 확인.
- `git diff --check`.
