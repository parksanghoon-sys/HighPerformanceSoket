# io_uring UDP Receive Window 설계

- 날짜: 2026-06-30
- 상태: 구현 진행 기준 설계
- 관련 결정: D140, D141, D142
- 범위: `Hps.Transport.IoUring` UDP receive pump 의 bounded receive slot window

## 목표

D142로 현재 `master`의 io_uring UDP receive/send pump 는 Linux artifact 에서 native `recvmsg`/`sendmsg` 경로를 통과했다.
다음 병목 후보는 fixed registration 또는 zero-copy send 가 아니라 one-deep UDP receive pump 가 만드는 no-posted-receive 구간이다.

이번 설계의 목표는 public transport 계약을 넓히지 않고, IPv4 UDP endpoint 내부에서 여러 receive operation 을 미리 post 하도록 바꾸는 것이다.
이 변경은 UDP 신뢰성, 순서 보장, 혼잡 제어를 추가하지 않는다. 단지 kernel 에 걸려 있는 receive 요청 수를 늘려 burst 흡수 여지를 키운다.

## 현재 구조

현재 `IoUringUdpEndpoint`는 endpoint 당 receive 자원을 하나만 가진다.

- `ReceivePool`
- `ReceiveMessage`
- `ReceiveContext`
- `UdpReceiveLoopAsync`

`UdpReceiveLoopAsync`는 `RefCountedBuffer`를 하나 빌리고, 단일 `IoUringUdpMessageBuffer`로 `recvmsg`를 submit 한 뒤 completion 을 기다린다.
completion 후 datagram 을 handler 에 넘긴 다음 다음 receive 를 다시 post 한다. handler 가 오래 걸리거나 completion 처리 사이에 지연이 생기면
posted receive 가 없는 구간이 생긴다.

`IoUringOperationContext`는 outstanding operation 하나만 기다릴 수 있게 설계되어 있다. 따라서 receive window 를 넓히려면 같은 context 를 재사용할 수 없고,
slot 마다 별도 context 와 message buffer 가 필요하다.

## 후보 비교

### 후보 A: one-deep 유지, benchmark 만 추가

장점:
- 구현 변경이 없다.
- D140의 현재 artifact 기준을 그대로 유지한다.

단점:
- 이미 D142로 syscall path 는 검증됐으므로, 다음 불확실성을 줄이지 못한다.
- Interface Server 목표인 burst ingress 와 publish fan-out 상황에서 receive-side 흡수력이 낮은 상태로 남는다.

판단: 기각한다.

### 후보 B: fixed registration cache 먼저 적용

장점:
- 등록 비용과 kernel copy 비용을 줄이는 장기 목표와 가깝다.

단점:
- receive buffer 는 handler/fan-out 으로 소유권이 넘어가므로 등록 cache 와 pool return 규칙이 복잡해진다.
- window depth 를 먼저 고정하지 않으면 등록 cache 의 lifetime owner 가 불명확하다.

판단: 후속으로 둔다.

### 후보 C: bounded receive slot window

장점:
- D117/D118에서 RIO UDP가 같은 문제를 bounded receive slot 으로 해결한 선례가 있다.
- io_uring의 `user_data` token mapping 과도 잘 맞는다. slot 마다 context token 을 갖고 completion 으로 slot 을 식별하면 된다.
- public API 를 바꾸지 않고 endpoint 내부 구현만 바꿀 수 있다.

단점:
- receive slot 별 datagram/message/context cleanup 이 필요하다.
- handler dispatch 는 계속 직렬이므로, window 가 handler 병렬 처리를 의미하지 않는다는 점을 명확히 해야 한다.

판단: 채택한다.

## 결정

io_uring UDP receive pump 는 bounded receive slot window 로 확장한다.

- 기본 window depth 는 internal constant `ReceiveWindowSize = 4`로 둔다.
- public API, factory default, backend selection 정책은 바꾸지 않는다.
- IPv4-only direct io_uring UDP 범위는 유지한다.
- handler dispatch 는 현재처럼 단일 receive loop 에서 직렬로 호출한다.
- 각 slot 은 `IoUringOperationContext`, `IoUringUdpMessageBuffer`, in-flight `RefCountedBuffer`를 소유한다.
- startup 시 모든 slot 을 post 한다.
- completion 이 오면 token 으로 slot 을 찾고, endpoint 가 open 이면 같은 slot 을 handler dispatch 전에 다시 post 한다.
- endpoint close 또는 handler 예외 시 모든 slot 의 outstanding datagram 을 정리하고 endpoint close notification 은 한 번만 수렴한다.

## 처리 흐름

```mermaid
sequenceDiagram
    participant Loop as "UdpReceiveLoopAsync"
    participant Slot as "IoUringUdpReceiveSlot"
    participant Ring as "io_uring"
    participant Handler as "ITransportDatagramHandler"

    Loop->>Slot: "slot[0..3].Post()"
    Slot->>Ring: "recvmsg(token)"
    Ring-->>Loop: "CQE token"
    Loop->>Slot: "Complete(result)"
    Loop->>Slot: "Post() again if endpoint open"
    Loop->>Handler: "OnDatagramReceived(datagram)"
```

completion 후 repost 를 handler dispatch 전에 수행한다. 이 순서가 핵심이다. handler 가 첫 datagram 에서 막혀도 나머지 slot 들은 이미 kernel 에 posted 상태로 남는다.

## 소유권 규칙

- slot 이 post 에 성공하면 slot 이 `RefCountedBuffer` ref 하나를 소유한다.
- completion 이 성공하면 slot 은 datagram length 와 remote endpoint 를 확정하고, datagram 소유권을 `ReceivedIoUringUdpDatagram`으로 넘긴다.
- dispatch 직전 transport local 변수의 datagram reference 는 null 처리한다.
- handler 는 받은 datagram 을 반드시 release 해야 한다.
- completion 실패, submit 실패, close, dispose 는 slot 이 들고 있는 in-flight datagram 을 release 한다.
- endpoint close 는 pending send queue 와 receive slots 를 모두 drain 한다.

## 테스트 전략

- shape test:
  - endpoint 가 `ReceiveWindowSize`와 receive slot 배열을 갖는지 확인한다.
  - slot 수만큼 receive context token 이 서로 다른지 확인한다.
- local contract test:
  - detached endpoint close 시 receive slots dispose 가 pool leak 없이 완료되는지 확인한다.
- Linux-gated integration:
  - 첫 handler 를 block 한 상태에서 window 만큼 datagram 을 추가 송신하고, unblock 후 모두 handler 로 전달되는지 확인한다.
  - handler 예외 후 endpoint close notification 이 한 번만 발생하고 pool leak 이 없는지 확인한다.

Windows 개발 환경에서는 Linux-gated test 가 capability unavailable 로 early return 한다. 실제 syscall path 는 다음 `iouring-linux-contract` artifact 로 다시 검토한다.

## 범위 밖

- fixed payload registration cache
- `SEND_ZC` / `MSG_ZEROCOPY`
- IPv6 direct io_uring UDP
- default backend promotion
- public receive depth 설정 API
- CI hard gate 승격
- UDP reliability, ordering, congestion control

## 완료 기준

- focused io_uring UDP endpoint/transport tests 통과
- `Hps.Transport.IoUring.Tests` 전체 통과
- solution build 경고 0 / 오류 0
- solution tests 통과
- `git diff --check` 통과
- D143 결정과 상태 문서 갱신
- 사용자 push 이후 새 `iouring-linux-contract` artifact 검토 항목을 Current TODO 로 남김
