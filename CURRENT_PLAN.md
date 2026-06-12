# CURRENT_PLAN.md — 현재 실행 지점

## 최종 목표
고성능 소켓 기반 pub/sub 메시지 브로커를 구현한다. 우선 사용 목표는 **4096 bytes 메시지를 100 Hz로 지연 누적 없이 처리**하는 것이다.

현재 해석:
- 단일 메시지 크기: 4096 bytes.
- 단일 스트림 기준 빈도: 100 Hz, 즉 약 409.6 KB/s payload.
- “딜레이 없이”는 현재 정량 latency SLO가 아니므로, 우선은 지속 부하에서 큐 적체가 누적되지 않고 p99 지연이 안정적으로 유지되는 상태로 해석한다.
- Phase 4 벤치마크 단계에서 p50/p99 지연, 처리량, 큐 길이, 누수 여부를 측정 가능한 기준으로 확정한다.

## 현재 Phase
Phase 3 — Protocol 프레이밍/코덱, Broker 라우팅, Server/Sample 흐름.

## 확인된 현재 상태
- Phase 0 스캐폴딩은 존재한다.
  - `HighPerformanceSocket.slnx`
  - `Directory.Build.props`
  - `src/Hps.Buffers`
  - `tests/Hps.Buffers.Tests`
- `src/Hps.Buffers/BipBuffer.cs`의 검토 must-fix M1·M2는 이번 사이클에서 반영됐다.
- **구현 전 설계 결정은 모두 종결됨**(DECISIONS D005~D012). `.claude/review/`에 검토 6건:
  - `phase1-bipbuffer.md` — BipBuffer must-fix **2건(M1, M2)**. (D005)
  - `phase1-refcounted-pool.md` — RefCountedBuffer/Pool 설계 **승인**(AddRef 순서 계약). (D006)
  - `phase2-transport-bipbuffer.md` — 송신 다중생산자(D1)·버퍼 소유권(D2)·백프레셔(D3). (D007)
  - `phase3-broker-routing.md` — 빈 토픽 eager-cleanup 경합(R1) 금지. (D008)
  - `phase3-publish-ownership.md` — recv→팬아웃 핸드오프(TCP 1회 복사 / UDP 직접 recv). (D009)
  - `phase3-framing-and-close.md` — TCP 프레임 조립(D010)·종료 release 계약(D011)·drop-oldest evict release(D012).
- `tests/Hps.Buffers.Tests/BipBufferTests.cs`가 추가됐고 M1/M2 회귀 테스트, deterministic edge 테스트,
  seeded fuzz 테스트가 discover된다.
- `BipBuffer` 내부의 `Volatile.Read/Write` 호출은 committed count, producer/consumer cursor, watermark snapshot/publish helper로 정리됐다.
- `BipBuffer`와 `RefCountedBuffer`의 private helper에는 snapshot/publish 의미와 수명/소유권 경계 주석이 보강됐다.
- `src/Hps.Buffers/PinnedBlockMemoryPool.cs`가 추가됐고 최소 API 테스트가 discover된다.
- `PinnedBlockMemoryPool` 멀티스레드 대여/반환 스트레스 테스트가 추가됐다.
- `PinnedBlockMemoryPoolTests`는 reflection 기반 `PoolApi` 래퍼 없이 public API를 직접 호출하도록 정리됐다.
- `src/Hps.Buffers/RefCountedBuffer.cs`가 추가됐고 최소 참조계수/반환 계약 테스트가 discover된다.
- `PinnedBlockMemoryPool.RentCounted()`가 추가되어 counted buffer 가 마지막 `Release()`에서 풀로 돌아간다.
- `RefCountedBuffer` 내부의 `Volatile.Read/Write` 호출은 의도 기반 helper로 감싸져 수명/길이 상태 관측 의미가 드러나도록 정리됐다.
- `RefCountedBuffer` 동시 Release/팬아웃 스트레스 테스트가 추가되어 구독자 수 가변 fan-out과 다수 buffer in-flight 반환을 검증한다.
- `src/Hps.Transport`와 `tests/Hps.Transport.Tests` 프로젝트가 추가됐다.
- `src/Hps.Transport` 폴더 구조가 `Abstractions/`, `Runtime/`, `Saea/`로 분리됐다.
  namespace 는 그대로 `Hps.Transport`를 유지해 public API 와 기존 using 은 바꾸지 않는다.
- `tests/Hps.Transport.Tests` 폴더 구조가 `Contracts/`, `Runtime/`, `Saea/`로 분리됐다.
  테스트 파일도 production 책임 축과 같은 방향으로 배치한다.
- `TransportSendBuffer`가 `RefCountedBuffer + offset + length` 기반 송신 요청 범위를 표현한다.
  raw `Memory<byte>`를 public send 계약에 노출하지 않는다.
- `ITransport.TrySend(IConnection, TransportSendBuffer)`는 send 수락 성공 시 Transport가 버퍼 참조 1개를 소유하고,
  실패 시 호출자가 Release 해야 한다는 소유권 경계를 XML doc으로 명시한다.
- `IConnection`은 연결 핸들/수명 계약에 집중하도록 `Close()`/`Dispose()`만 노출한다. 송신 큐나 펌프 같은
  Transport 내부 구현 세부사항은 `IConnection` public API 에 노출하지 않는다.
- `TransportBase`가 `ITransport.TrySend`의 공통 소유권 판정을 구현한다. 이 구현은 내부 `TransportConnection`이 만든
  연결만 받으며, open 연결이면 pending 송신 큐에 넣고 closed 연결이면 false 를 반환한다.
- `TransportBase.TrySend`는 pending 큐에 넣기 전에 `TransportSendBuffer`가 실제 live `RefCountedBuffer`를 가리키는지 확인한다.
  `TransportSendBuffer`는 struct 이므로 `default` 값이 public API로 들어와 close drain 시점에 늦게 실패하지 않게 수락 경계에서 차단한다.
- `TransportConnection.Close()`는 pending 송신 항목을 drain 하며 각 `RefCountedBuffer`를 Release 한다.
  이미 begin 된 in-flight 항목은 `TransportConnection.InFlightSend` handle 이 완료/취소/unwind 경로에서 책임진다.
- `TransportConnection.TryBeginInFlightSend(out InFlightSend?)`가 추가되어 송신 펌프가 pending 항목을 raw 값으로 가져가지 않고
  dispose 가능한 in-flight handle 로 받는다. handle 은 `Complete()`와 `Dispose()` 모두에서 Transport 소유 ref 를 정확히 한 번 반환한다.
- `ITransport.StartAsync`/`StopAsync`는 기본 `CancellationToken` 인자를 허용한다.
- `ITransport.ListenTcpAsync(EndPoint, CancellationToken)`와 `ConnectTcpAsync(EndPoint, CancellationToken)`가 추가되어
  TCP 연결 획득의 public 진입점이 확정됐다.
- `IConnectionListener`가 추가되어 listener 의 바인딩 endpoint, `AcceptAsync`, `Close`/`Dispose` 수명 경계를 표현한다.
  UDP datagram 계약은 accept 개념이 없으므로 별도 단위로 남겼다.
- `SaeaTransport`의 TCP listen/connect/accept 최소 기준선이 추가됐다. loopback 에서 listener 를 열고,
  outbound connect 와 inbound accept 로 양쪽 `IConnection`을 얻는 테스트가 discover된다.
- `SaeaConnectionListener`는 socket 세부 타입을 public API 로 노출하지 않고 `IConnectionListener` 뒤에 숨긴다.
- `TransportConnection.Close()`는 실제 socket 같은 backend 자원을 함께 dispose 할 수 있게 됐다.
- `ITransport.SetReceiveHandler(ITransportReceiveHandler)`가 추가되어 Transport recv pump 가 상위 계층으로
  TCP byte stream 조각과 연결 종료를 전달할 public 계약이 생겼다.
- `TransportReceiveBuffer`는 `readonly ref struct` borrowed view 로 추가됐다. `ReadOnlySpan<byte>`와 `Length`만 노출하므로
  수신 ring/pinned block 소유권이 콜백 밖으로 저장되지 않는다.
- `TransportBase`는 receive handler 등록을 공통으로 처리하고, 후속 recv pump 가 사용할 snapshot helper 를 제공한다.
- `SaeaTransport`의 TCP recv pump 최소 기준선이 추가됐다. accepted TCP socket 이 받은 raw byte chunk 를
  pinned receive block 에 읽고, `ITransportReceiveHandler.OnReceived`에 borrowed `TransportReceiveBuffer`로 전달한다.
- recv pump 는 아직 프레이밍을 하지 않는다. D010의 TCP frame 조립과 D009의 payload `RefCountedBuffer` 복사는 Phase 3 범위다.
- 사용자 리뷰에서 발견된 `SaeaTransport` connection tracking 누수를 수정했다. accepted/outbound connection 이 `Close()`되면
  `TransportConnection` close callback 으로 transport 의 `_connections` 추적 목록에서 즉시 제거된다.
- `TransportConnection.Close()`는 pending drain 과 closed 표시만 connection lock 안에서 처리하고, unregister callback 과 socket dispose 는
  lock 밖에서 수행한다. dispose 가 지연되어도 같은 connection 의 `TrySend`/관측자가 불필요하게 대기하지 않는다.
- `SaeaTransport`의 TCP send pump 최소 기준선이 추가됐다. `ITransport.TrySend`가 accepted/outbound connection 에 enqueue 한
  `TransportSendBuffer`를 connection별 단일 send loop 가 socket 으로 전송하고, completion 뒤 `InFlightSend.Complete()`로
  Transport 소유 ref 를 반환한다.
- 첫 send pump 는 프레이밍 없이 `TransportSendBuffer.Offset/Length` 범위만 전송한다. 송신 버퍼는 `RefCountedBuffer.Memory`에서
  `ArraySegment<byte>`를 얻어 socket 으로 넘기며, 중간 payload 복사는 추가하지 않는다.
- TCP echo loopback 통합 테스트가 추가됐다. receive handler 가 borrowed chunk 를 자신의 `RefCountedBuffer`로 복사한 뒤
  같은 connection 에 `TrySend`하면 실제 client socket 이 동일 payload 를 다시 받는다.
- echo 통합 테스트는 기존 recv pump 와 send pump 구현만으로 통과했으므로 production code 변경은 없었다.
- TCP 동시 연결 echo 통합 테스트가 추가됐다. loopback listener 에 8개 client 를 연결하고 각 connection 이
  동시에 서로 다른 payload 를 echo 받아, connection별 receive/send pump 와 RefCountedBuffer 반환 경계가 독립적으로 유지되는지 검증한다.
- 동시 echo 테스트는 기존 TCP receive pump, send pump, connection tracking 구현만으로 통과했으므로 production code 변경은 없었다.
- `TransportConnection`은 pending 큐가 빈 상태에서 새 항목이 들어오거나 close 로 pump 를 깨워야 할 때만 send signal 을 보낸다.
  pending drain 과 in-flight release 계약(D016, D017)은 유지된다.
- `TransportConnection` pending send queue 에 기본 capacity 16과 drop-oldest evict-release 정책이 추가됐다.
  queue 가 가득 찬 상태에서 새 send 를 수락하면 가장 오래된 pending 항목의 Transport 소유 ref 를 Release 하고,
  새 항목을 enqueue 한다. close 는 남은 pending 항목만 drain 하므로 evict 된 항목을 다시 Release 하지 않는다.
- UDP datagram public 계약이 추가됐다. UDP 는 TCP accept 모델과 분리된 `IUdpEndpoint` 수명 핸들을 사용하고,
  `ITransport.BindUdpAsync`/`TrySendTo`/`SetDatagramHandler` 로 bind, send, receive 경계를 노출한다.
- UDP receive 는 D009를 반영해 `RefCountedBuffer`를 직접 대여하고, datagram handler 가 해당 ref 소유권을 받아 직접 Release 한다.
  이 기준선은 Protocol/Broker fan-out 이전의 Transport datagram 경계만 검증한다.
- `SaeaTransport`의 UDP loopback 기준선이 추가됐다. 외부 UDP socket 이 보낸 datagram 은 handler 로 전달되고,
  `TrySendTo`로 보낸 `TransportSendBuffer`는 원격 UDP socket 에 도착한 뒤 Transport 소유 ref 가 반환된다.
- UDP echo loopback 통합 테스트가 추가됐다. datagram handler 가 받은 owned `RefCountedBuffer`에 send ref 를 추가한 뒤
  같은 endpoint 의 `TrySendTo`로 되돌려 보내면 client socket 이 동일 payload 를 받는다.
- UDP echo 통합 테스트는 기존 UDP receive loop 와 endpoint send pump 구현만으로 통과했으므로 production code 변경은 없었다.
- `.claude/review/phase2-udp-datagram.md` 검토는 UDP 기준선을 승인했고, S1 소유권 이전 순서 개선과 S2 UDP send pump/배압 후속 항목을 남겼다.
- S1은 반영됐다. UDP receive loop 는 handler 호출 전에 local `datagram` 참조를 끊어, handler 가 소유권을 받은 뒤 예외를 던져도
  loop catch 가 같은 `RefCountedBuffer`를 다시 Release 하지 않는다.
- S2는 UDP send 직렬화와 receive backpressure 질문으로 나눴다. send 직렬화는 이번 단위에서 처리하고,
  receive backpressure 질문은 별도 설계 단위가 필요하므로 `TODOS.md`의 Deferred Backlog 로 유지한다.
- S2 중 UDP send 직렬화는 반영됐다. `TrySendTo`는 datagram 마다 `Task.Run`을 만들지 않고 endpoint pending queue 에
  `TransportSendBuffer`를 넣으며, bind 된 endpoint 당 단일 UDP send pump 가 queue 를 drain 한다.
- UDP endpoint 가 닫히면 아직 pump 가 보내지 않은 queued datagram 의 Transport 소유 ref 를 close 경로에서 drain 하므로
  endpoint close 전후 경합에서도 `RefCountedBuffer`가 누수되지 않는다.
- `SaeaUdpEndpoint` pending send queue 에도 기본 capacity 16과 drop-oldest evict-release 정책이 추가됐다.
  queue 가 가득 찬 상태에서 새 datagram 을 수락하면 가장 오래된 pending datagram 의 Transport 소유 ref 를 Release 하고,
  새 datagram 을 enqueue 한다. close 는 남은 pending datagram 만 drain 하므로 evict 된 datagram 을 다시 Release 하지 않는다.
- TCP `TransportConnection`과 UDP `SaeaUdpEndpoint`에 내부 `DroppedPendingSendCount` counter 가 추가됐다.
  drop-oldest eviction 이 발생할 때마다 누적되므로, 느린 소비자나 막힌 UDP remote 때문에 메시지가 버려졌는지
  테스트와 내부 진단에서 확인할 수 있다. public metric/log 표면은 아직 만들지 않았다.
- UDP receive backpressure 정책(Q1)은 fan-out/backpressure 결정과 맞물리므로 계속 Deferred Backlog 로 둔다.
- Phase 2 backend selector 최소 계약이 추가됐다. `TransportFactory.CreateDefault()`는 상위 계층이 concrete backend 를 직접 new 하지 않도록
  `ITransport` 생성 진입점을 제공하며, 현재는 모든 환경에서 `SaeaTransport`로 fallback 한다.
- 실제 OS/capability probe 와 RIO/io_uring 선택 로직은 아직 구현하지 않았다. factory 위치만 먼저 고정해 이후 backend 교체 지점을 만든다.
- 파일 이동 전용 구조 정리 단위로 `Hps.Transport`와 `Hps.Transport.Tests`의 파일을 책임별 하위 폴더로 옮겼다.
  동작과 namespace 는 바꾸지 않았고, SDK-style project 의 recursive compile include 에 맡긴다.
- 재확인: `dotnet test HighPerformanceSocket.slnx`는 테스트 40개를 실행했고 모두 통과했다.
- 재확인: `dotnet build HighPerformanceSocket.slnx`는 경고 0개, 오류 0개로 통과했다.
- Phase 3 첫 단위로 `src/Hps.Protocol`과 `tests/Hps.Protocol.Tests` 프로젝트가 추가됐다.
- `TcpFrameAssembler` 기본 계약이 추가됐다. connection 단위 상태 객체가 TCP 4바이트 big-endian payload length header 를 누적하고,
  payload 를 `RefCountedBuffer`로 복사해 `FrameReady` 때 caller 에 소유권을 넘긴다.
- `TcpFrameAssembler`는 maxPayload 초과를 `PayloadTooLarge`로 반환하고, 조립 중인 partial payload 는 `Dispose()`에서 반환한다.
- `TcpFrameAssembler` edge/fuzz 테스트가 보강됐다. 0 length frame, 한 chunk 의 다중 frame/consumed loop,
  `payloadLength == maxPayloadLength`, 결정적 chunk fragmentation fuzz 를 영구 회귀 테스트로 고정했다.
- `TcpFrameReceiveHandler`가 추가되어 Transport raw TCP receive chunk 를 connection 별 `TcpFrameAssembler`로 조립하고,
  완성 frame 을 `ITcpFrameHandler`로 전달한다.
- `TcpFrameReceiveHandler`는 `PayloadTooLarge`를 받으면 D010 계약대로 connection 을 닫고 상위 close handler 에 알린다.
  Transport close 알림을 받으면 조립 중 partial payload 를 Dispose 해 D011 종료 누수 0 경계를 지킨다.
- `TcpCommandDecoder`가 추가되어 TCP frame payload 를 `SUBSCRIBE <topic>` 또는 `PUBLISH <topic> <payload>` command 로 해석한다.
  topic/payload 는 원본 frame 의 span view 이며, malformed input 은 예외 없이 `TcpCommandDecodeError`로 보고한다.
- `.claude/review/phase3-frame-adapter-command.md`는 6314b7f까지 승인했고 must-fix 는 없었다.
  해당 리뷰의 low 관찰 중 O1/O2는 반영됐다.
- `TcpFrameReceiveHandler`는 `PayloadTooLarge`, handler 실패, Transport close 통지가 겹쳐도 상위 close handler 에
  connection 별 1회만 알린다. close 통지 표식은 weak marker 로 관리해 단명 connection 을 강하게 붙잡지 않는다.
- `ITcpFrameHandler.OnFrame`이 예외를 던지면 frame 을 수락하지 못한 것으로 보고 어댑터가 `RefCountedBuffer`를 Release 한 뒤
  connection 을 닫는다.
- `src/Hps.Broker`와 `tests/Hps.Broker.Tests` 프로젝트가 추가됐다.
- `SubscriptionTable`이 추가되어 topic 별 `IConnection` 구독자 set 을 관리한다. D008에 따라 빈 topic entry 는 즉시 제거하지 않는
  NoCleanup 정책을 사용하며, 동시 subscribe-vs-unsubscribe R1 경합 테스트를 영구 회귀로 고정했다.
- `BrokerPublisher`가 추가되어 `SubscriptionTable` snapshot 을 구독자별 `ITransport.TrySend` 호출로 fan-out 한다.
  구독자마다 같은 `RefCountedBuffer`에 `AddRef`하고, Transport 가 거부한 구독자 ref 는 즉시 `Release`한다.
  publish guard ref 는 caller 가 계속 소유하므로 Publish 반환 뒤 caller 가 직접 `Release`해야 한다.
- `BrokerPublisher`는 같은 `RefCountedBuffer` 안의 offset/length payload range 를 fan-out 할 수 있다.
  TCP command frame 의 `PUBLISH <topic> <payload>` 버퍼에서 payload slice 만 추가 복사 없이 전송하기 위한 선행 조건이다.
- `.claude/review/review-status-2026-06-11.md`가 추가되어 기존 Claude 검토 의견의 현재 조치 여부를 정리했다.
  기존 `.claude/review/*.md` 원문은 당시 스냅샷으로 보존하고, 현재 작업 트리와 어긋나는 오래된 평가는
  review status 문서에서 superseded 로 해석한다.
- `.claude/review/overall-state-2026-06-11.md`는 H1 backpressure, H2 연결 종료 구독 정리, H3 end-to-end 결선을 핵심 미결로 지적했다.
- H2의 Broker 라우팅 테이블 쪽 선행 API로 `SubscriptionTable.UnsubscribeAll(IConnection)`을 추가했다.
  이 API 는 닫힌 connection 을 모든 topic set 에서 제거하고, D008 NoCleanup 정책에 따라 topic entry 자체는 제거하지 않는다.
- `TcpCommand`가 `PayloadOffset`을 노출해 Broker가 command 문법을 다시 계산하지 않고 frame 안의 publish payload slice 를 fan-out 할 수 있게 됐다.
- `BrokerTcpFrameHandler`가 추가되어 `SUBSCRIBE`는 `SubscriptionTable.Subscribe`, `PUBLISH`는 `BrokerPublisher.Publish(topic, frame, offset, length)`,
  `OnConnectionClosed`는 `SubscriptionTable.UnsubscribeAll`로 연결한다.
- Broker command handler 는 수락한 frame guard ref 를 처리 후 항상 `Release`한다. malformed command 는 현재 protocol error 응답이 없으므로
  frame 을 회수하고 connection 을 닫는다.
- `src/Hps.Server`와 `tests/Hps.Server.Tests` 프로젝트가 추가됐다.
- `BrokerServer` 최소 TCP host wiring 이 추가됐다. 주입된 `ITransport`에 `TcpFrameReceiveHandler(BrokerTcpFrameHandler)`를 등록하고,
  `StartAsync`/`ListenTcpAsync` 후 listener accept loop 를 시작한다. `StopAsync`/`Dispose`는 accept loop 를 깨우고 listener 를 닫은 뒤
  Transport 를 중지한다.
- `BrokerServer + SaeaTransport` 실제 TCP command loopback 통합 테스트가 추가됐다. raw TCP subscriber socket 이
  length-prefix `SUBSCRIBE alpha`를 보내고, raw TCP publisher socket 이 `PUBLISH alpha <payload>`를 보내면 subscriber socket 이
  broker fan-out 된 raw payload 를 받는지 검증한다.
- 이 통합 테스트는 기존 Server/Transport/Protocol/Broker 구현으로 즉시 통과했으므로 production code 수정은 없었다.
- `BrokerServer + SaeaTransport` 실제 TCP command 경로의 다중 subscriber fan-out 통합 테스트가 추가됐다.
  raw TCP subscriber socket 2개가 같은 topic 을 구독한 뒤 publisher socket 1개가 `PUBLISH`를 보내면 두 subscriber socket 이
  동일 payload 를 받는지 검증한다. 공유 `RefCountedBuffer` fan-out 뒤 server pool 이 `RentedCount==0`으로 돌아오는지도 확인한다.
- 다중 subscriber 통합 테스트도 기존 구현으로 즉시 통과했으므로 production code 수정은 없었다.
- Transport send pending queue backpressure 기준선과 내부 drop counter 는 TCP/UDP 모두 적용됐다. samples,
  public metric/log 표면, 다중 메시지 순서/부하 fan-out 검증은 아직 후속 단위로 남아 있다.
- D013 기준으로 이번 기능 단위 완료 후 다음 구현은 사용자 리뷰 뒤 진행한다.

## 다음 단일 작업 단위
사용자 리뷰 대기.

리뷰 후 계속 진행 지시가 있으면 drop-oldest 내부 counter 를 운영자가 읽을 수 있는 public metric/log 표면으로 끌어올릴지
먼저 작은 설계 단위로 검토한다.
UDP receive backpressure 정책(Q1)은 fan-out/backpressure 결정과 맞물리므로 성급히 구현하지 않고 별도 설계 단위로 둔다.
D010 랜덤 적대적 fuzz 는 비차단 테스트 보강이므로 `TODOS.md` Deferred Backlog 에서 별도 단위로 둔다.

## 이번 단위의 검증 경로
- `BrokerServer + SaeaTransport` loopback 에서 raw TCP subscriber 2개가 같은 topic 을 구독하고,
  publisher 1개가 보낸 payload 를 두 socket 모두 받는지 통합 테스트로 확인한다.
- 이번 단위는 누락된 회귀 검증을 추가하는 test-only 단위다. 새 테스트는 기존 production 구현으로 즉시 통과했으므로 production code 수정은 하지 않는다.
- `dotnet test tests\Hps.Server.Tests\Hps.Server.Tests.csproj --filter "FullyQualifiedName~TcpCommandLoopback_WhenTwoSubscribersShareTopic"`
- `dotnet test tests\Hps.Server.Tests\Hps.Server.Tests.csproj`
- `dotnet test HighPerformanceSocket.slnx`
- `dotnet build HighPerformanceSocket.slnx`
- `git diff --check`
- 테스트 출력에서 `Hps.Server.Tests`, `Hps.Broker.Tests`, `Hps.Buffers.Tests`, `Hps.Transport.Tests`, `Hps.Protocol.Tests`가 모두 discover되고 실행되는지 확인한다.
- 결과: focused `TcpCommandLoopback_WhenTwoSubscribersShareTopic` 통과 1.
  Server 전체 통과 5.
  전체 `dotnet test HighPerformanceSocket.slnx`는 `Hps.Transport.Tests` 통과 32 + `Hps.Server.Tests` 통과 5 +
  `Hps.Buffers.Tests` 통과 18 + `Hps.Protocol.Tests` 통과 24 + `Hps.Broker.Tests` 통과 17, 실패 0, 건너뜀 0.
  빌드 경고 0, 오류 0. `git diff --check`는 whitespace 오류 없이 통과했다. Git의 LF↔CRLF 안내 경고만 출력됐다.

## 이번 작업에서 건드리지 않은 범위
- 명시적인 SocketAsyncEventArgs 기반 payload send/recv 최적화
- 실제 OS/capability probe 와 RIO/io_uring backend 선택 로직
- public drop metric/log 표면
- UDP receive backpressure 정책
- configurable pending send capacity
- 다중 메시지 fan-out 순서/부하 통합 테스트
- `TransportFactory.CreateDefault()`를 직접 사용하는 server factory/convenience API
- samples
- backpressure
- protocol error 응답
- D010 랜덤 적대적 fuzz 대량 회귀 테스트

위 범위는 사용자 리뷰 후 다음 단일 작업 단위에서 필요 범위만 다시 확인하고 진행한다.
