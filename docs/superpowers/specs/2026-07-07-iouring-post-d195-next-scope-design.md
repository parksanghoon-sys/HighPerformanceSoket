# D196 io_uring post-D195 next scope design

## 상태

Accepted.

## 배경

D195에서 D181 fixed-write SQE helper/native completion evidence 가 원격
`iouring-linux-contract.yml` gate 를 통과했다.

- workflow run: `28834265348`
- head SHA: `848ce55341945a83d61023d7e54add5906fd7590`
- test counters: total/executed/passed 61, failed 0
- 핵심 test: `WriteFixed_WhenLinuxCapabilityAvailable_WritesRegisteredBufferSliceToPipe`
- capability: `Available`
- completion result: `2`
- 검증 payload: registered buffer `{10,20,30,40}`의 offset 1 length 2를 pipe 로 쓰고 `{20,30}`을 읽었다.

이 결과는 `IORING_OP_WRITE_FIXED`의 opcode, SQE field mapping, registered buffer range,
kernel completion 계약이 Linux runner 에서 동작한다는 증거다. 그러나 production TCP/UDP pump 가
fixed buffer 를 사용해도 된다는 증거는 아직 아니다.

현재 production `io_uring` send 경로는 다음처럼 유지되어 있다.

- TCP send pump: `IoUringTransport.SendInFlightAsync`가 `TrySubmitSend`를 사용한다.
- UDP send pump: `TrySubmitSendMessage`와 `IoUringUdpMessageBuffer`를 사용한다.
- fixed buffer registration owner: `IoUringRegisteredBufferSet`은 존재하지만 production pump lifetime 에 연결되지 않았다.
- `TransportConnection.InFlightSend`와 `RefCountedBuffer` fan-out 소유권은 기존 shared send queue 계약으로 유지된다.

D195 이후 바로 pump 를 바꾸면 fixed-write contract, socket fd 지원 여부, `RefCountedBuffer` registration lifetime,
close drain, fallback 정책이 한 번에 섞인다. 다음 단위는 이 중 가장 먼저 확인해야 하는 불확실성 하나만 줄여야 한다.

## 목표

`WRITE_FIXED`가 pipe fd 뿐 아니라 stream socket fd 에 대해서도 registered buffer slice 를 정상 전송하는지
Linux native contract evidence 로 고정한다.

이 단위의 목적은 production TCP pump 변경이 아니라, pump 변경 전에 "socket fd 에 fixed write 를 제출해도 되는가"를
분리해서 검증하는 것이다.

## 후보 비교

### 후보 A: TCP send pump 를 바로 `TrySubmitWriteFixed`로 변경

장점:

- 실제 TCP send path 최적화에 가장 직접적으로 접근한다.
- benchmark artifact 로 성능 차이를 바로 관측할 수 있다.

단점:

- TCP length prefix, payload buffer, fan-out `RefCountedBuffer`, in-flight release, close drain, fallback 정책을 동시에 건드린다.
- 실패 시 socket fd 지원 문제인지, registration lifetime 문제인지, pump ownership 문제인지 분리하기 어렵다.
- 현재 `TransportConnection` shared queue 계약은 buffer registration index 를 알지 못한다.

판단: 아직 이르다. D196 범위에서 제외한다.

### 후보 B: zero-copy send 또는 `send_zc_fixed`로 이동

장점:

- 커널 단 복사까지 줄인다는 장기 목표에 가장 가깝다.

단점:

- zero-copy send 는 일반 completion CQE와 별도 notification CQE로 memory reuse 시점을 판단해야 한다.
- 현재 completion loop 와 operation context 는 "operation 1개 = completion 1개" 모델에 가깝다.
- fan-out payload 공유와 notification 완료 전 release 금지 규칙이 추가로 필요하다.

판단: fixed-write socket contract 와 lifetime model 이후 별도 설계로 다룬다.

### 후보 C: socket fd fixed-write native contract evidence

장점:

- D195 pipe evidence 이후 가장 작은 자연스러운 다음 증거다.
- production pump 를 바꾸지 않고 `WRITE_FIXED`와 socket fd 조합만 검증한다.
- 실패 원인을 kernel/socket/fixed-write contract 쪽으로 좁힐 수 있다.
- Linux contract workflow 에서 기존 `Hps.Transport.IoUring.Tests` 범위로 검증 가능하다.

단점:

- 성능 개선은 아직 없다.
- `RefCountedBuffer` registration lifetime 이나 TCP pump close drain 은 여전히 후속 문제로 남는다.
- `socketpair(AF_UNIX, SOCK_STREAM)`은 stream socket contract 를 검증하지만 실제 IPv4 TCP loopback 과 완전히 같지는 않다.

판단: 다음 구현 단위로 채택한다.

## 결정

D196 다음 구현 단위는 **io_uring fixed-write socket fd contract evidence** 로 둔다.

구현 범위:

- `tests/Hps.Transport.IoUring.Tests`에 Linux capability gated native test 를 추가한다.
- test-only helper 로 `socketpair(AF_UNIX, SOCK_STREAM)`를 만든다.
- 한쪽 fd 에 `TrySubmitWriteFixed`를 제출하고 반대쪽 fd 에서 정확한 payload 를 읽는다.
- registered buffer 예시는 D195와 같은 `{10,20,30,40}`, offset 1 length 2, expected `{20,30}`을 재사용한다.
- completion result 는 `2`를 기대한다.
- helper 는 test assembly 내부에만 둔다. production transport API, `ITransport`, `TransportConnection`, `RefCountedBuffer` public surface 는 변경하지 않는다.
- `iouring-linux-contract.yml`은 이미 project-scoped test workflow 이므로 workflow 변경은 하지 않는다.

`socketpair`를 우선 쓰는 이유:

- Linux fd 기반 stream socket semantics 를 작고 결정적으로 검증할 수 있다.
- TCP loopback listener/connect 보다 test setup 이 작고 flaky 요소가 적다.
- production pump 통합 전 "socket fd 에 `WRITE_FIXED`가 적용되는가"라는 핵심 질문에는 충분히 가깝다.

## 제외 범위

- TCP send pump 를 fixed-write 로 변경
- UDP send pump 를 fixed-buffer operation 으로 변경
- `RefCountedBuffer` 또는 `PinnedBlockMemoryPool`에 registration index surface 추가
- production fixed-buffer lease/cache owner 추가
- `send_zc`, `send_zc_fixed`, notification CQE 처리
- fixed receive buffer ring, provided buffer ring, buffer bundle send
- `TransportFactory.CreateDefault()` promotion
- latency hard gate 또는 warning-as-failure

## 검증 계획

로컬:

- Red: 새 socket fixed-write evidence test 가 helper/test 부재로 assertion failure 를 낸다.
- Green: focused test 통과. Windows/local unavailable 환경은 capability guard 로 early-return 한다.
- `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal`
- `dotnet test HighPerformanceSocket.slnx -v minimal`
- `git diff --check`

원격:

- 사용자 push 이후 `iouring-linux-contract.yml`을 수동 실행한다.
- artifact summary/TRX 에서 socket fixed-write evidence test 가 capability `Available` 상태로 Passed 인지 확인한다.
- stdout 에 socket fixed-write completion result 2를 남긴다.
- 이 remote gate 전까지는 D196을 production pump fixed-buffer 연결 근거로 쓰지 않는다.

## 다음 상태 문서 반영

- `DECISIONS.md`: D196 active decision 으로 추가한다.
- `TODOS.md`: D195 후보 재평가를 완료로 이동하고, D196 구현을 Current TODO 로 둔다.
- `CURRENT_PLAN.md`: D196 결정과 다음 실행 지점을 기록한다.
- `CHANGELOG_AGENT.md`: D196 설계 결과와 검증 한계를 기록한다.
