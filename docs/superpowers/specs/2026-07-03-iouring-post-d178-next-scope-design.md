# D179 io_uring post-D178 next scope design

## 상태

Accepted.

## 배경

D178에서 D177 fixed buffer registration evidence test 가 원격 `iouring-linux-contract.yml`
run `28631346969`에서 통과했다.

- workflow conclusion: success
- root `summary.md`: test exit code 0
- TRX counters: total 58, executed 58, passed 58, failed 0, notExecuted 0
- `Register_WhenLinuxCapabilityAvailable_RegistersAndUnregistersMultipleBuffers`: outcome Passed
- test output: `io_uring capability status: Available`, `registered fixed buffer count: 2`

이 결과는 `IoUringRegisteredBufferSet`이 Linux runner 에서 native fixed buffer register/unregister 를 수행할 수 있음을
보여준다. 그러나 아직 TCP/UDP pump 가 fixed buffer 를 사용한다는 뜻은 아니다. 현재 TCP send path 는
`IORING_OP_SEND`에 managed byte[] pointer 를 넣고, UDP send path 는 `IORING_OP_SENDMSG`와 pinned message buffer 를 쓴다.

liburing/man7 기준으로 fixed-buffer I/O는 등록된 buffer index 와 해당 등록 영역 안의 address/length 를 SQE에 함께 넣어야 한다.
`read_fixed`/`write_fixed`는 등록 buffer 를 요구하고, `send_zc_fixed`는 추가 notification CQE 로 memory reuse 시점까지
소유권 모델이 달라진다. 따라서 D178 다음 단위는 바로 pump 를 바꾸기보다 fixed-buffer SQE 제출 계약을 분리해서 검증해야 한다.

참고한 upstream 문서:
- `io_uring_register_buffers(3)`: registered buffer 는 kernel mapping/page ref 비용을 I/O마다 반복하지 않게 하는 최적화다.
- `io_uring_prep_read_fixed(3)` / `io_uring_prep_write_fixed(3)`: `buf`와 `nbytes`는 `buf_index`로 등록된 영역 안에 있어야 한다.
- `io_uring_prep_send_zc_fixed(3)`: zero-copy fixed send 는 보통 send CQE와 notification CQE를 분리한다.

## 목표

fixed buffer pump 로 가기 전, 현재 raw SQE 구조가 fixed-buffer operation 을 정확히 표현하고
Linux kernel 이 등록된 buffer index/range 를 실제 I/O에 사용할 수 있는지 작은 contract test 로 검증한다.

## 후보 비교

### 후보 A: TCP send pump 를 fixed buffer 로 바로 변경

장점:
- 실제 TCP send path 최적화에 가장 직접적으로 접근한다.
- benchmark artifact 로 성능 차이를 바로 관측할 수 있다.

단점:
- `TransportConnection.InFlightSend`, length-prefix block, `RefCountedBuffer` fan-out, close drain, 등록 lifetime 이 한 번에 얽힌다.
- 현재 `RefCountedBuffer`는 pool block 단위 소유권과 ref-count release 를 중심으로 설계되어 있고,
  registered buffer index 를 transport resource 가 어떻게 찾고 보존할지 아직 정해지지 않았다.
- 실패하면 SQE field mapping 문제인지, registration lifetime 문제인지, TCP pump ownership 문제인지 분리하기 어렵다.

판단: 아직 열지 않는다.

### 후보 B: zero-copy send 또는 `send_zc_fixed`로 직행

장점:
- 커널 단 복사 최소화 목표에 가장 강하게 맞닿아 있다.
- `IORING_SEND_ZC_REPORT_USAGE`를 쓰면 copied fallback 여부를 관측할 수 있다.

단점:
- zero-copy send 는 일반 send 와 달리 memory reuse 를 notification CQE로 판단해야 한다.
- 현재 completion loop 는 operation context 1개와 일반 CQE 1개 completion 모델에 맞춰져 있다.
- fan-out payload 는 여러 subscriber send queue 가 같은 `RefCountedBuffer`를 공유하므로 notification CQE 전 release 금지 규칙이 추가된다.

판단: fixed-buffer SQE 계약과 notification CQE 모델을 각각 분리한 뒤 별도 설계로 열어야 한다.

### 후보 C: fixed-buffer SQE 제출 계약 probe 를 먼저 추가

장점:
- D178 register/unregister evidence 다음에 가장 작은 자연스러운 증거다.
- production TCP/UDP pump 를 바꾸지 않고 `IoUringQueue`가 fixed-buffer SQE field 를 정확히 채우는지 확인할 수 있다.
- Linux contract workflow 로 실제 kernel completion 까지 확인할 수 있다.
- 실패 원인을 SQE opcode/field/register range/feature support 쪽으로 좁힐 수 있다.

단점:
- 성능 개선은 아직 없다.
- TCP/UDP pump integration 은 후속 설계가 필요하다.

판단: 다음 구현 단위로 채택한다.

## 결정

D179 다음 구현 단위는 **io_uring fixed-buffer SQE submission contract evidence** 로 둔다.

구현 범위:
- `IoUringNative`에 fixed-buffer operation opcode 를 추가한다.
  - 우선순위는 `IORING_OP_WRITE_FIXED`다. TCP/UDP pump 변경 없이 등록 buffer 가 실제 I/O source 로 쓰이는지 검증하기 쉽기 때문이다.
- `IoUringQueue`에 fixed-buffer submission helper 를 추가한다.
  - 예: `TrySubmitWriteFixed(int fileDescriptor, byte[] buffer, int offset, int length, int bufferIndex, ulong token)`
  - helper 는 `Address`, `Length`, `BufferIndex`, `UserData`를 명확히 채운다.
  - `offset/length`는 managed buffer 범위를 검증하고, `bufferIndex`는 음수를 거부한다.
- 테스트는 두 층으로 둔다.
  - shape test: SQE struct 에 `BufferIndex` field 가 있고 fixed-write opcode/helper 가 존재하는지 확인한다.
  - Linux/capability gated native test: 작은 io_uring queue 와 registered buffer 1개를 만들고,
    fixed write submission 이 completion success 를 반환하는지 확인한다.
- native test 의 I/O target 은 pump 와 분리된 작은 fd 로 둔다.
  - 우선 anonymous pipe 또는 socket pair 같은 local fd 를 사용한다.
  - 이 fd helper 는 test-only native helper 로 제한하고 production transport API 에 노출하지 않는다.
- `iouring-linux-contract.yml`은 이미 `Hps.Transport.IoUring.Tests` 전체를 실행하므로 workflow 변경은 하지 않는다.

## 제외 범위

- TCP/UDP send/receive pump 를 fixed-buffer operation 으로 변경
- `RefCountedBuffer` 또는 `PinnedBlockMemoryPool`에 registration index surface 추가
- `send_zc`, `send_zc_fixed`, notification CQE 처리
- fixed receive buffer ring, provided buffer ring, buffer bundle send
- default backend promotion
- latency hard gate 또는 warning-as-failure

## 검증 계획

로컬:
- Red: shape test 가 fixed-write opcode/helper 부재로 assertion failure 를 낸다.
- Green: `IoUringSubmissionShapeTests` 또는 신규 focused tests 통과.
- `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj -v minimal`
- `dotnet test HighPerformanceSocket.slnx -v minimal`
- `git diff --check`

원격:
- 사용자 push 이후 `iouring-linux-contract.yml`을 수동 실행한다.
- artifact summary 와 TRX 에서 fixed write submission evidence test 가 capability available 상태에서 실행·통과했는지 확인한다.
- expected: test exit code 0, 기존 TCP/UDP io_uring tests green, fixed write completion result 가 expected byte count 와 일치.

## 다음 상태 문서 반영

- `DECISIONS.md`: D179 active decision 으로 추가한다.
- `TODOS.md`: Current TODO 를 fixed-buffer SQE submission contract evidence 구현으로 바꾼다.
- `CURRENT_PLAN.md`: D179 결정과 다음 실행 지점을 기록한다.
