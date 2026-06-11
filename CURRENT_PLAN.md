# CURRENT_PLAN.md — 현재 실행 지점

## 최종 목표
고성능 소켓 기반 pub/sub 메시지 브로커를 구현한다. 우선 사용 목표는 **4096 bytes 메시지를 100 Hz로 지연 누적 없이 처리**하는 것이다.

현재 해석:
- 단일 메시지 크기: 4096 bytes.
- 단일 스트림 기준 빈도: 100 Hz, 즉 약 409.6 KB/s payload.
- “딜레이 없이”는 현재 정량 latency SLO가 아니므로, 우선은 지속 부하에서 큐 적체가 누적되지 않고 p99 지연이 안정적으로 유지되는 상태로 해석한다.
- Phase 4 벤치마크 단계에서 p50/p99 지연, 처리량, 큐 길이, 누수 여부를 측정 가능한 기준으로 확정한다.

## 현재 Phase
Phase 2 — Transport 추상화 `src/Hps.Transport/` 초기 계약.

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
- 재확인: `dotnet test HighPerformanceSocket.slnx`는 테스트 34개를 실행했고 모두 통과했다.
- 재확인: `dotnet build HighPerformanceSocket.slnx`는 경고 0개, 오류 0개로 통과했다.
- D013 기준으로 이번 기능 단위 완료 후 다음 구현은 사용자 리뷰 뒤 진행한다.

## 다음 단일 작업 단위
사용자 리뷰 대기.

리뷰 후 계속 진행 지시가 있으면 다음 단일 작업 단위는 TCP payload I/O에 들어가기 전,
`SaeaTransport`의 TCP send pump 가 `ITransport.TrySend`로 enqueue 된 `TransportSendBuffer`를 실제 socket 으로 보내고
in-flight handle 을 완료해 ref 를 반환하는 최소 loopback 기준선이다. 첫 테스트는 raw socket client 가 accepted
connection 으로 전송된 작은 payload 를 받는 흐름으로 제한한다.
프레이밍, UDP, RIO/io_uring, backpressure 정책은 그 다음 단위로 둔다.

## 이번 단위의 검증 경로
- Red: `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~Close_WhenAcceptedConnectionIsClosed_RemovesTransportTrackingReference"`
  → `Close()` 이후 transport 내부 추적 connection 수가 1로 남아 단언 실패 1개.
- Green: `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~Close_WhenAcceptedConnectionIsClosed_RemovesTransportTrackingReference"`
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj`
- `dotnet test HighPerformanceSocket.slnx`
- `dotnet build HighPerformanceSocket.slnx`
- `git diff --check`
- 테스트 출력에서 `Hps.Buffers.Tests` 18개와 `Hps.Transport.Tests` 16개가 discover되고 실행됐는지 확인한다.
- 결과: focused 통과 1, 실패 0, 건너뜀 0. Transport 전체 통과 16. 전체 통과 34, 실패 0, 건너뜀 0. 빌드 경고 0, 오류 0.

## 이번 작업에서 건드리지 않은 범위
- 명시적인 SocketAsyncEventArgs 기반 payload send/recv 펌프
- `SaeaTransport`가 `ITransport.TrySend` pending queue 를 실제 socket send 로 drain 하는 구현
- UDP datagram bind/receive/send 계약
- drop-oldest backpressure evict release
- Protocol/Broker/Server

위 범위는 사용자 리뷰 후 다음 단일 작업 단위에서 필요 범위만 다시 확인하고 진행한다.
