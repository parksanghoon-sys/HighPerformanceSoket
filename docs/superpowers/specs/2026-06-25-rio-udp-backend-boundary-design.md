# RIO UDP backend boundary 설계

## 상태

Accepted.

## 배경

D108에서 RIO default promotion 은 보류됐다.
가장 큰 이유는 현재 `RioTransport`가 TCP-only opt-in backend 이며, `ITransport.BindUdpAsync(...)` parity 가 없기 때문이다.
Interface Server 목표는 TCP/UDP endpoint 로 외부 데이터를 받아 topic subscriber 에 발행하는 것이므로,
RIO가 기본 backend 후보가 되려면 UDP datagram path 도 `ITransport` 계약 뒤에서 동작해야 한다.

이번 설계는 RIO UDP를 바로 구현하지 않고, native operation shape, endpoint owner, buffer lifetime,
backpressure/diagnostics parity 를 먼저 확정한다.

## 현재 기준선

### SAEA UDP

`SaeaUdpEndpoint`는 bind socket 단위 owner 다.

- endpoint-local pending send queue 를 갖는다.
- capacity 는 16이고 drop-oldest 정책을 사용한다.
- evicted datagram 의 `RefCountedBuffer`는 queue lock 밖에서 `Release`한다.
- close 시 pending send 를 모두 drain 하고 endpoint 를 transport tracking list 에서 제거한다.
- receive loop 는 handler 호출이 반환될 때까지 다음 receive 를 prefetch 하지 않는다.
- handler 예외가 발생하면 endpoint 를 닫고 `OnDatagramEndpointClosed`를 호출한다.
- diagnostics snapshot 은 endpoint id, UDP kind, state, pending count, high-watermark, dropped count 를 기록한다.

### 현재 RIO

`RioTransport`는 TCP에 대해서만 다음을 갖는다.

- TCP listen/connect/accept.
- `RIOReceive`/`RIOSend` wrapper.
- shared IOCP completion pump + CQ signal.
- connection resource lifetime receive/prefix registration.
- connection-local payload registration cache.

`RioNative.RioExtensionFunctionTable`에는 `ReceiveEx`/`SendEx` pointer field 가 있지만,
현재 availability 판정과 wrapper 에서는 TCP `Receive`/`Send`만 필수로 본다.

## Microsoft RIO UDP 제약

Microsoft Learn 기준으로 `RIOSendEx`는 connected RIO TCP socket 또는 bound RIO UDP socket 에 network data 를 보낼 수 있고,
UDP remote address 는 registered buffer slice 로 전달한다.

`RIOReceiveEx`는 connected/bound UDP socket 에서 data 를 받고, completion 때 local/remote address 를 registered buffer slice 에 쓸 수 있다.

이번 설계에서 반영할 제약:

- data buffer, local address buffer, remote address buffer 는 모두 `RIO_BUF` 형태의 registered buffer slice 다.
- send/receive operation 이 completion 되기 전에는 해당 buffer id 와 backing memory 가 유효해야 한다.
- receive data buffer 는 completion 전까지 읽거나 재사용하면 안 된다.
- datagram socket 에서는 `RIO_MSG_WAITALL`을 사용하지 않는다.
- `RIO_MSG_DEFER`/`RIO_MSG_COMMIT_ONLY` batching 은 v1 UDP parity 범위에서 제외한다.

## 결정

RIO UDP는 SAEA UDP와 같은 public `IUdpEndpoint`/`ITransportDatagramHandler` 계약을 구현한다.
구현은 TCP `RioConnectionResource`를 억지로 재사용하지 않고, UDP 전용 `RioUdpEndpoint` resource owner 를 둔다.

이유:

- UDP는 connection 이 없고 remote endpoint 가 datagram 마다 달라질 수 있다.
- receive path 는 payload buffer 외에 remote address registered buffer 를 completion까지 보존해야 한다.
- send path 는 payload buffer와 remote address buffer 의 수명을 함께 관리해야 한다.
- TCP connection resource 에 끼워 넣으면 close/drain/diagnostics 경계가 흐려진다.

## 설계

### 1. Native wrapper

`RioNative`에 `ReceiveEx`/`SendEx` delegate 와 wrapper 를 추가한다.

필수 wrapper:

- `internal bool ReceiveEx(IntPtr requestQueue, RioBufferSegment? data, RioBufferSegment? localAddress, RioBufferSegment? remoteAddress, IntPtr requestContext)`
- `internal bool SendEx(IntPtr requestQueue, RioBufferSegment? data, RioBufferSegment? remoteAddress, IntPtr requestContext)`

초기 구현에서는 control context, flags buffer, RIO flags 를 지원하지 않는다.
wrapper 내부에서는 nullable segment 를 native `PRIO_BUF` 또는 null 로 변환한다.

availability:

- RIO TCP availability 는 기존 필수 pointer 집합을 유지한다.
- RIO UDP availability 는 `ReceiveEx`와 `SendEx` pointer 가 추가로 필요하다.
- 따라서 `RioCapabilityProbe.GetStatus()` 자체를 UDP까지 포함한 status 로 넓히지 않고,
  internal `RioNative.SupportsDatagramOperations` 같은 세부 capability 를 둔다.

### 2. `RioUdpEndpoint` owner

새 internal endpoint owner 를 둔다.

예상 책임:

- bound UDP registered socket.
- UDP request queue/completion queues.
- receive payload block registration.
- receive local/remote address blocks registration.
- endpoint-local pending send queue/drop-oldest/high-watermark/drop count.
- close 시 pending send drain, outstanding receive/send completion 정리, registered buffer deregister, socket dispose, transport unregister.

SAEA와 마찬가지로 public `IUdpEndpoint`에는 backend 세부 타입을 노출하지 않는다.

### 3. Receive path

초기 v1은 SAEA와 같은 no-prefetch semantics 를 유지한다.

순서:

1. receive payload block 을 pool 에서 대여하고 registered buffer id 를 확보한다.
2. remote address block 과 필요 시 local address block 을 endpoint resource lifetime 또는 receive operation lifetime 으로 등록한다.
3. `RIOReceiveEx`를 post 한다.
4. completion 을 기다린다.
5. completion byte count 로 payload `RefCountedBuffer.Length`를 설정한다.
6. remote address buffer 를 `EndPoint`로 decode 한다.
7. handler 에 ownership 을 넘긴다.
8. handler 반환 뒤 다음 receive 를 post 한다.

handler 예외 정책은 SAEA와 맞춘다.
handler 가 datagram ownership 을 받은 뒤 예외를 던지면 Transport 는 endpoint 를 닫고 `OnDatagramEndpointClosed`를 호출한다.
datagram release 책임은 기존 계약대로 handler 쪽에 남는다.

### 4. Send path

public send entry 는 기존 `ITransport.TrySendTo(IUdpEndpoint, EndPoint, TransportSendBuffer)`를 그대로 사용한다.

`RioUdpEndpoint.TryAcceptSend(...)`는 SAEA와 같은 queue/drop-oldest 규칙을 쓴다.
send pump 는 dequeue 한 항목에 대해 다음을 수행한다.

1. payload `TransportSendBuffer`의 backing `byte[]` 등록 id 를 얻는다.
   초기 구현은 connection-local payload cache 를 바로 재사용하지 않고 UDP endpoint-local payload registration cache 로 둔다.
2. remote endpoint 를 endpoint-local scratch address block 에 encode 한다.
3. remote address block 이 completion 전까지 재사용되지 않도록 send outstanding 1개를 유지한다.
4. `RIOSendEx`를 post 한다.
5. completion 뒤 `TransportSendBuffer` transport-owned ref 를 release 한다.

초기 v1은 `MaxOutstandingSend = 1`로 둔다.
이렇게 해야 remote address scratch block 하나를 재사용할 수 있고, SAEA의 endpoint 단일 pump model 과도 맞는다.

### 5. Diagnostics

RIO UDP endpoint snapshot 은 SAEA UDP와 같은 값 semantics 를 제공한다.

- `EndpointTransportKind.Udp`
- open/closed state
- pending send count
- pending send queue high-watermark
- dropped pending send count

Transport-level diagnostics 에서는 TCP/UDP drop/high-watermark aggregate 가 기존 `TransportBase` 기록 경로를 재사용해야 한다.

## 구현 순서

### Task 1. Native Ex operation shape

- Red: `RioNative`가 RIO available 환경에서 `ReceiveEx`/`SendEx` datagram function pointers 를 expose 해야 한다는 capability test.
- Green: delegate, wrapper, `SupportsDatagramOperations`를 추가한다.
- 검증: RIO capability tests.

### Task 2. UDP endpoint owner skeleton

- Red: `BindUdpAsync_WhenRioAvailable_ReturnsEndpointWithLocalEndPoint`.
- Green: registered UDP socket bind, endpoint tracking, close/unregister, unsupported 환경 명시 실패.
- 검증: focused RIO UDP tests.

### Task 3. UDP receive loop

- Red: raw UDP client datagram 이 RIO endpoint handler 로 전달되는 loopback test.
- Green: `RIOReceiveEx` post/completion/decode/dispatch.
- 검증: receive loopback, handler exception close notify, pool leak 0.

### Task 4. UDP send loop

- Red: handler 또는 test가 `TrySendTo`로 queue 한 datagram 이 raw UDP client 로 도착하는 loopback test.
- Green: endpoint pending queue, `RIOSendEx`, release path.
- 검증: send loopback, close drains pending, drop-oldest ownership tests.

### Task 5. diagnostics parity

- Red: RIO UDP endpoint pending/high-watermark/drop snapshot tests.
- Green: SAEA와 같은 diagnostics update path.
- 검증: transport diagnostics contract tests.

## 범위 밖

- UDP 신뢰성/순서보장/혼잡제어.
- `RIO_MSG_DEFER` batching.
- multiple outstanding UDP receives.
- transport-wide shared payload registration cache.
- `TransportFactory.CreateDefault()` 변경.
- composite TCP RIO + UDP SAEA backend.

## 위험과 대응

- remote address buffer lifetime: send/receive completion 전 재사용 금지. 초기 outstanding 1개와 endpoint scratch block 으로 단순화한다.
- datagram handler exception: SAEA와 같은 close notify 정책을 유지한다.
- endpoint close 중 outstanding operation: socket/CQ close 뒤 completion wait 가 불안정하면 TCP에서 다뤘던 close/churn hardening 패턴을 재사용한다.
- address family: IPv4/IPv6 `SOCKADDR_INET` encode/decode helper 를 별도 internal helper 로 분리한다.

## 검증 계획

- native wrapper tests: RIO available 조건부 pointer/wrapper validation.
- RIO UDP endpoint focused tests: bind, receive, send, handler exception, close drain, drop-oldest, diagnostics.
- 기존 SAEA UDP tests 와 의미 대조.
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --no-restore`.
- `dotnet build HighPerformanceSocket.slnx --no-restore`.
- `dotnet test HighPerformanceSocket.slnx --no-build`.
- `git diff --check`.

## 참고

- Microsoft Learn `LPFN_RIOSENDEX`: https://learn.microsoft.com/en-us/windows/win32/api/mswsock/nc-mswsock-lpfn_riosendex
- Microsoft Learn `LPFN_RIORECEIVEEX`: https://learn.microsoft.com/en-us/windows/win32/api/mswsock/nc-mswsock-lpfn_rioreceiveex
