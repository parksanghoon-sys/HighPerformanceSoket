# D176 io_uring post-D175 next scope design

## 상태

Accepted.

## 배경

D174는 `io_uring` TCP baseline suite 가 stop 중 exit 134로 abort 하던 문제를
shutdown 이후 stale CQE 처리 경계로 수정했다. D175 run `28627435853`은 이 fix 이후
원격 `iouring-benchmark-artifacts.yml` gate 를 통과했다.

- TCP/UDP baseline, summary, history, envelope exit code: 모두 0
- TCP raw report count: 6
- UDP raw report count: 6
- TCP envelope: reference-summary-count 6, signal-count 0
- UDP envelope: reference-summary-count 9, signal-count 0
- drop, payload error, pool rented max: 모두 0

따라서 현재 상태는 correctness/reliability gate 가 다시 green 이며, 다음 작업은
fixed registration, zero-copy send, default promotion, latency gate, reference 확장 중 무엇을
열어도 되는지 판단하는 것이다.

## 목표

D175 evidence 이후 다음 구현 단위를 하나로 좁힌다. 목표는 새로운 성능 최적화를 무리하게 붙이는 것이 아니라,
이후 최적화가 의존할 native capability 와 소유권 경계를 가장 작은 단위로 검증하는 것이다.

## 후보 비교

### 후보 A: D175 artifact 를 repository reference 로 추가 채택

D175 raw report 를 `ci-linux-iouring-x64-01` protocol reference 에 추가한다.

장점:
- 기존 수동 채택 절차를 재사용한다.
- reference envelope 가 조금 더 두꺼워진다.

단점:
- TCP는 이미 6-session, UDP는 9-session reference 이며 두 date root 가 존재한다.
- 이번 run 은 failure fix 검증 표본이지 latency/outlier triage 표본이 아니다.
- 계속 reference 만 늘리면 fixed registration/zero-copy 판단을 미루는 비용이 커진다.

판단: 지금의 다음 단위로는 우선순위가 낮다.

### 후보 B: fixed buffer 또는 zero-copy send 를 바로 pump 에 연결

TCP/UDP send/receive path 에 `IoUringRegisteredBufferSet`, `SEND_ZC`, 또는 fixed-buffer SQE 를 바로 연결한다.

장점:
- 프로젝트 목표인 kernel side copy/cost 절감에 가장 직접적으로 가까워진다.
- benchmark artifact 로 전후 비교를 만들 수 있다.

단점:
- `RefCountedBuffer` fan-out 소유권, in-flight send, deregister 금지, close drain, fallback 정책이 동시에 얽힌다.
- zero-copy send 는 kernel 지원 여부와 completion 의미가 일반 send 와 다르다.
- fixed receive 는 UDP receive slot repost/handler ownership 과 충돌할 수 있다.
- 현재는 native fixed buffer registration 자체가 원격 Linux artifact 에서 명시적으로 검증되지 않았다.

판단: 아직 바로 열지 않는다.

### 후보 C: fixed buffer registration native contract evidence 를 먼저 고정

이미 존재하는 `IoUringRegisteredBufferSet` owner 를 실제 Linux runner 에서 register/unregister 하는
contract test 로 검증한다. pump 동작은 바꾸지 않는다.

장점:
- 이후 fixed-buffer pump 설계의 선행 조건을 좁은 테스트 단위로 닫는다.
- 실패하더라도 pump 소유권, TCP/UDP protocol, benchmark path 와 분리해 원인을 찾을 수 있다.
- Windows/local 환경에서는 기존처럼 platform guard 를 유지하고, Linux available 환경에서만 native path 를 검증한다.
- `iouring-linux-contract.yml`가 이미 `Hps.Transport.IoUring.Tests` TRX artifact 를 업로드하므로 원격 evidence 경로가 있다.

단점:
- 직접적인 latency 개선은 없다.
- fixed send/receive SQE 연결은 후속 설계가 필요하다.

판단: 다음 구현 단위로 채택한다.

## 결정

D176 이후 다음 구현 단위는 **io_uring fixed buffer registration Linux contract evidence** 로 한다.

구현 범위:
- `IoUringRegisteredBufferSetTests`에 Linux/capability gated native register/unregister test 를 추가한다.
- test 는 `IoUringQueue.CreateForProbe(...)`로 작은 ring 을 만들고, 2개 이상의 byte[] buffer 를
  `IoUringRegisteredBufferSet.Register(...)`로 등록한 뒤 dispose 한다.
- capability unavailable 환경은 실패가 아니라 evidence skip 으로 둔다.
- test output 에 capability status 와 register/unregister 시도 결과를 남긴다.
- production pump 는 변경하지 않는다.
- `iouring-linux-contract.yml`는 이미 해당 test project 를 실행하므로 workflow 구조 변경은 하지 않는다.

## 제외 범위

- TCP/UDP pump 에 fixed buffer SQE 연결
- `IORING_OP_SEND_ZC`, `MSG_ZEROCOPY`, zero-copy send
- fixed file registration
- default backend promotion
- latency hard gate 또는 warning-as-failure
- D175 raw report repository reference 수동 채택

## 검증 계획

로컬:
- Red: Linux-gated test 가 처음에는 필요한 evidence assertion 또는 helper 부재로 실패해야 한다.
- Green: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal`
- 전체 영향 확인: `dotnet test HighPerformanceSocket.slnx -v minimal`
- `git diff --check`

원격:
- 사용자 push 이후 `iouring-linux-contract.yml`을 수동 실행한다.
- artifact summary 와 TRX 에서 `IoUringRegisteredBufferSet` native register/unregister test 가
  capability available 상태에서 실행됐는지 확인한다.
- expected: test exit code 0, 기존 TCP/UDP io_uring tests green, fixed registration evidence test green.

## 다음 상태 문서 반영

- `DECISIONS.md`: D176 active decision 으로 추가한다.
- `TODOS.md`: Current TODO 를 fixed buffer registration Linux contract evidence 구현으로 승격한다.
- `CURRENT_PLAN.md`: D176 결정과 다음 실행 지점을 기록한다.
