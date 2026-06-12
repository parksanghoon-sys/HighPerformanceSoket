# CHANGELOG_AGENT.md

## 2026-06-12 (Codex — TCP pending send queue backpressure)

### 작업 단위
- `.claude/review/overall-state-2026-06-11.md` H1 중 TCP `TransportConnection` pending send queue backpressure 만 처리했다.
- 범위는 TCP connection pending queue capacity 와 drop-oldest evict-release 로 제한했다.
- UDP endpoint pending send queue backpressure, UDP receive backpressure, configurable capacity 는 포함하지 않았다.

### Red
- capacity 17번째 send 후 pending count 가 17로 남아 `Expected: 16, Actual: 17`로 실패하는 것을 확인했다.
- overflow 뒤 publisher guard ref 를 놓고 close 하는 경로도 evict 가 없어 `RentedCount==17`로 남는 실패를 확인했다.

### 구현
- `TransportConnection` pending send queue 기본 capacity 를 16으로 두었다.
- open connection 에서 queue 가 가득 찬 상태로 새 send 를 수락하면 가장 오래된 pending 항목을 dequeue 하고,
  그 Transport 소유 ref 를 `Release`한 뒤 새 항목을 enqueue 한다.
- evict 대상 선택과 queue 제거는 connection lock 으로 직렬화하고, `Release`는 lock 밖에서 수행한다.
- close 는 남아 있는 pending 항목만 drain 하므로 이미 evict 된 항목을 다시 Release 하지 않는다.

### 상태 갱신
- `DECISIONS.md`에 D039로 TCP pending send queue drop-oldest 결정을 기록했다.
- `CURRENT_PLAN.md`를 TCP backpressure 완료 및 UDP endpoint pending send queue 대기 상태로 갱신했다.
- `TODOS.md`에서 H1을 TCP 완료와 UDP endpoint 후속으로 분리했다.

### 검증
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~TransportSendQueueTests"` → Red 실패 2/통과 7 → Green 통과 9.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj` → 통과 28, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Transport.Tests` 통과 28 + `Hps.Server.Tests` 통과 4 +
  `Hps.Buffers.Tests` 통과 18 + `Hps.Protocol.Tests` 통과 24 + `Hps.Broker.Tests` 통과 17, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음. Git의 LF↔CRLF 안내 경고만 출력됨.

## 2026-06-12 (Codex — BrokerServer TCP command loopback 통합 테스트)

### 작업 단위
- `BrokerServer + SaeaTransport` 실제 TCP command loopback 통합 테스트를 추가했다.
- 범위는 subscriber/publisher raw socket 경로에서 length-prefix command 가 Broker fan-out 으로 이어지는지 검증하는 test-only 단위로 제한했다.
- production code, backpressure, samples, 다중 subscriber fan-out 은 포함하지 않았다.

### Red/현상 확인
- 새 통합 테스트는 기존 production 구현으로 첫 실행부터 통과했다.
- 따라서 이번 단위는 누락된 회귀 검증을 고정하는 작업이며, production code 변경은 하지 않았다.

### 테스트
- subscriber socket 이 length-prefix `SUBSCRIBE alpha` frame 을 보낸 뒤 서버 내부 subscription table 에 등록될 때까지 기다린다.
- publisher socket 이 length-prefix `PUBLISH alpha <payload>` frame 을 보내면 subscriber socket 이 payload 원문을 받는지 검증한다.
- publish frame/send ref 가 모두 반환되어 server payload pool 의 `RentedCount==0`으로 돌아오는지 검증한다.

### 상태 갱신
- `CURRENT_PLAN.md`를 실제 TCP command loopback 검증 완료 및 backpressure 대기 상태로 갱신했다.
- `TODOS.md`에서 loopback 검증 항목을 Completed 로 이동하고 다음 후보를 Transport send pending queue backpressure 로 갱신했다.

### 검증
- `dotnet test tests\Hps.Server.Tests\Hps.Server.Tests.csproj --filter "FullyQualifiedName~TcpCommandLoopback"` → 통과 1, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Server.Tests\Hps.Server.Tests.csproj` → 통과 4, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Server.Tests` 통과 4 + `Hps.Transport.Tests` 통과 26 +
  `Hps.Buffers.Tests` 통과 18 + `Hps.Protocol.Tests` 통과 24 + `Hps.Broker.Tests` 통과 17, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음. Git의 LF↔CRLF 안내 경고만 출력됨.

## 2026-06-12 (Codex — Hps.Server 최소 TCP host wiring)

### 작업 단위
- `src/Hps.Server`와 `tests/Hps.Server.Tests` 프로젝트를 추가했다.
- 범위는 `BrokerServer`가 기존 Transport/Protocol/Broker 구성요소를 조립하고 TCP listener accept loop 수명을 관리하는 최소 host wiring 으로 제한했다.
- 실제 socket 경로의 `SUBSCRIBE`/`PUBLISH` end-to-end fan-out, samples, backpressure, protocol error 응답은 포함하지 않았다.

### Red
- `BrokerServer` 타입 부재를 reflection 기반 테스트의 `Assert.NotNull` 실패로 확인했다.
- 계약 surface Green 이후 stub 상태에서 receive handler 등록, Transport start/listen, accept loop 시작, Stop listener/Transport 정리 테스트가 실패했다.

### 구현
- `BrokerServer`가 `SubscriptionTable`, `BrokerPublisher`, `BrokerTcpFrameHandler`, `TcpFrameReceiveHandler`를 내부 조립한다.
- `StartTcpAsync`는 주입된 `ITransport`에 `TcpFrameReceiveHandler`를 등록하고, `StartAsync`/`ListenTcpAsync` 후 accept loop 를 시작한다.
- accept loop 는 accepted connection 을 별도로 저장하지 않는다. connection tracking, receive/send pump, close drain 은 Transport 계약이 계속 책임진다.
- `StopAsync`/`Dispose`는 accept loop 취소, listener close/dispose, Transport stop 순서로 listener 수명을 정리한다.

### 상태 갱신
- `DECISIONS.md`에 D038로 Server host wiring 책임 경계를 기록했다.
- `CURRENT_PLAN.md`를 Server 최소 wiring 완료 및 실제 TCP command loopback 통합 테스트 대기 상태로 갱신했다.
- `TODOS.md` Completed에 이번 단위를 추가하고, 실제 socket command loopback 검증을 `P1_SOON` Deferred Backlog 로 남겼다.

### 검증
- `dotnet test tests\Hps.Server.Tests\Hps.Server.Tests.csproj --filter "FullyQualifiedName~BrokerServerContract"` → Red 실패 1/통과 0 → 계약 Green 통과 1.
- `dotnet test tests\Hps.Server.Tests\Hps.Server.Tests.csproj --filter "FullyQualifiedName~BrokerServerTests"` → 동작 Red 실패 2/통과 1 → Green 통과 3.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Server.Tests` 통과 3 + `Hps.Transport.Tests` 통과 26 +
  `Hps.Buffers.Tests` 통과 18 + `Hps.Protocol.Tests` 통과 24 + `Hps.Broker.Tests` 통과 17, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음. Git의 LF↔CRLF 안내 경고만 출력됨.

## 2026-06-11 (Codex — Broker TCP command handler)

### 작업 단위
- TCP frame payload command 를 Broker subscribe/publish/close cleanup 으로 연결하는 `BrokerTcpFrameHandler`를 추가했다.
- 범위는 command handler 와 payload offset 선행 계약으로 제한했다.
- Server host wiring, samples, backpressure, protocol error 응답 프레임은 포함하지 않았다.

### Red
- `TcpCommand.PayloadOffset` property 부재를 reflection 기반 테스트의 `Assert.NotNull` 실패로 확인했다.
- `PayloadOffset` 기본값 0 상태에서 `PUBLISH alpha <payload>`의 실제 payload 시작 offset 14 단언이 실패했다.
- `BrokerTcpFrameHandler` 타입/생성자/`ITcpFrameHandler` 구현 부재를 reflection 기반 테스트의 `Assert.NotNull` 실패로 확인했다.
- no-op handler 에서 subscribe 등록, publish payload range fan-out, close cleanup, malformed command close/release 테스트가 실패했다.

### 구현
- `TcpCommand`에 `PayloadOffset`을 추가하고, `TcpCommandDecoder`가 `PUBLISH` payload 시작 offset 을 계산해 넘긴다.
- `Hps.Broker`가 `Hps.Protocol`을 참조하도록 하고 `BrokerTcpFrameHandler`를 추가했다.
- `BrokerTcpFrameHandler.OnFrame`은 `SUBSCRIBE`를 `SubscriptionTable.Subscribe`로, `PUBLISH`를
  `BrokerPublisher.Publish(topic, frame, command.PayloadOffset, command.Payload.Length)`로 연결한다.
- `OnConnectionClosed`는 `SubscriptionTable.UnsubscribeAll`을 호출한다.
- malformed command 는 현재 protocol error response 가 없으므로 frame 을 Release 한 뒤 connection 을 닫는 최소 정책으로 정리했다.

### 상태 갱신
- `DECISIONS.md`에 D037로 Broker TCP frame handler 결정을 기록했다.
- `CURRENT_PLAN.md`를 command handler 완료 및 Server wiring 대기 상태로 갱신했다.
- `TODOS.md` Completed에 이번 단위를 추가하고 다음 후보를 `Hps.Server` 최소 host/wiring 으로 갱신했다.

### 검증
- `dotnet test tests\Hps.Protocol.Tests\Hps.Protocol.Tests.csproj --filter "FullyQualifiedName~TcpCommandDecoderTests"` → Red: payload offset 계약 부재 실패 1/통과 9 → 계약 Green 통과 10 → offset 동작 Red 실패 2/통과 8 → Green 통과 10.
- `dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter "FullyQualifiedName~BrokerTcpFrameHandlerTests"` → Red: handler 계약 부재 실패 1 → 계약 Green 통과 1 → 동작 Red 실패 4/통과 1 → Green 통과 5.
- `dotnet test tests\Hps.Protocol.Tests\Hps.Protocol.Tests.csproj` → 통과 24, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj` → 통과 17, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Protocol.Tests` 통과 24 + `Hps.Broker.Tests` 통과 17 +
  `Hps.Transport.Tests` 통과 26 + `Hps.Buffers.Tests` 통과 18, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음. Git의 LF↔CRLF 안내 경고만 출력됨.

## 2026-06-11 (Codex — Broker connection cleanup API)

### 작업 단위
- `.claude/review/overall-state-2026-06-11.md` H2의 연결 종료 구독 정리 누락 중 Broker 라우팅 테이블 API만 처리했다.
- 범위는 `SubscriptionTable.UnsubscribeAll(IConnection)`과 해당 회귀 테스트로 제한했다.
- TCP command handler, `OnConnectionClosed` wiring, Server wiring, backpressure 는 다음 단위로 남겼다.

### Red
- `SubscriptionTable.UnsubscribeAll(IConnection)` 메서드 부재를 reflection 기반 테스트의 `Assert.NotNull` 실패로 확인했다.
- 메서드 시그니처만 있는 no-op stub 에서 여러 topic 에 걸친 같은 connection 제거 수가 0으로 남아 동작 테스트가 실패했다.

### 구현
- `UnsubscribeAll`이 현재 topic entry 전체를 열거하며 각 `TopicSubscriptions`에서 대상 connection 을 제거한다.
- D008 NoCleanup 정책을 유지하기 위해 빈 topic entry 자체는 제거하지 않는다.
- 반환값은 실제 제거된 구독 수로 두어 command handler/운영 진단에서 cleanup 효과를 관측할 수 있게 했다.

### 상태 갱신
- `DECISIONS.md`에 D036으로 connection-wide cleanup API 결정을 기록했다.
- `CURRENT_PLAN.md`를 cleanup API 완료 및 command handler 결선 대기 상태로 갱신했다.
- `TODOS.md` Completed에 이번 단위를 추가했고, overall review H1 send backpressure 를 별도 deferred backlog 로 기록했다.

### 검증
- `dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter "FullyQualifiedName~BrokerRoutingTests"` → Red: 계약 부재 실패 1/통과 4 → 계약 Green 통과 5 → 동작 Red 실패 1/통과 5 → Green 통과 6.
- `dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj` → 통과 12, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Broker.Tests` 통과 12 + `Hps.Buffers.Tests` 통과 18 +
  `Hps.Transport.Tests` 통과 26 + `Hps.Protocol.Tests` 통과 23, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음. Git의 LF↔CRLF 안내 경고만 출력됨.

## 2026-06-11 (Codex — Broker publish payload range)

### 작업 단위
- `BrokerPublisher`가 같은 `RefCountedBuffer` 안의 일부 offset/length 범위만 fan-out 할 수 있게 했다.
- 범위는 command handler 선행 조건인 payload slice 전송 계약으로 제한했다.
- TCP command handler, protocol error 응답, Server wiring 은 포함하지 않았다.

### Red
- `Publish(string, RefCountedBuffer, int, int)` overload 부재를 reflection 기반 단언 실패로 확인했다.
- overload no-op stub 에서 payload range fan-out 이 수락 수 0으로 실패하는 것을 확인했다.
- 잘못된 offset/length 가 0-subscriber topic 에서 예외 없이 묻히는 실패를 확인했다.

### 구현
- 기존 `Publish(string, RefCountedBuffer)`는 `offset=0`, `length=payload.Length`로 ranged overload 에 위임한다.
- ranged overload 는 구독자 snapshot 전에 offset/length 를 payload length 기준으로 검증한다.
- 구독자별 send 는 기존 AddRef/TrySend/false-release 계약을 유지하면서 `TransportSendBuffer(payload, offset, length)`를 전달한다.

### 상태 갱신
- `DECISIONS.md`에 D035로 Broker publish payload range 결정을 기록했다.
- `CURRENT_PLAN.md`를 payload range 완료 및 command handler 선행 조건 해소 상태로 갱신했다.
- `TODOS.md` Completed에 이번 단위를 추가했다.

### 검증
- `dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter "FullyQualifiedName~BrokerPublisherTests"` → Red: overload 부재 실패 1/통과 3 → Green 통과 4 → Red: range 동작 실패 2/통과 4 → Green 통과 6.
- `dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj` → 통과 10, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Broker.Tests` 통과 10 + `Hps.Buffers.Tests` 통과 18 +
  `Hps.Transport.Tests` 통과 26 + `Hps.Protocol.Tests` 통과 23, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음. Git의 LF↔CRLF 안내 경고만 출력됨.

## 2026-06-11 (Codex — Claude 검토 조치 현황 문서화)

### 작업 단위
- 사용자가 요청한 `.claude/review/` 검토 의견 확인 결과를 문서로 남겼다.
- 기존 Claude 검토 문서는 삭제하지 않고 보존했다.
- 범위는 review status 문서와 상태 문서 연결로 제한했고, production/test code 는 수정하지 않았다.

### 조치
- `.claude/review/review-status-2026-06-11.md`를 추가했다.
- `REVIEW_2026-06-11.md`가 현재 작업 트리 기준으로는 오래된 스냅샷임을 명시했다.
- must-fix/should-fix/O 항목별 현재 조치 여부와 남은 비차단 항목을 정리했다.
- `.claude/review/README.md`에 현재 조치 현황 문서 링크와 보존 원칙을 추가했다.

### 상태 갱신
- `CURRENT_PLAN.md`에 review status 문서가 추가됐고 기존 검토 원문은 보존한다는 현재 상태를 기록했다.
- `TODOS.md`의 Completed에 이번 문서화 작업을 추가했다.

### 검증
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Broker.Tests` 통과 8 + `Hps.Buffers.Tests` 통과 18 +
  `Hps.Transport.Tests` 통과 26 + `Hps.Protocol.Tests` 통과 23, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음. Git의 LF↔CRLF 안내 경고만 출력됨.

## 2026-06-11 (Codex — Broker publish fan-out)

### 작업 단위
- `SubscriptionTable` 위에 publish fan-out 진입점 `BrokerPublisher`를 추가했다.
- 범위는 구독자 snapshot → 구독자별 `AddRef` → `ITransport.TrySend` → 거부 ref 즉시 반환까지로 제한했다.
- command handler, Server wiring, backpressure/drop-oldest 정책은 포함하지 않았다.

### Red
- `BrokerPublisher` 타입 부재를 reflection 기반 테스트의 `Assert.NotNull` 실패로 확인했다.
- `BrokerPublisher(SubscriptionTable, ITransport)` 생성자와 `Publish(string, RefCountedBuffer)` 계약 부재를 reflection 기반 단언 실패로 확인했다.
- no-op stub 에서 구독자 2명 fan-out 이 수락 수 0으로 실패했고, Transport 거부 구독자 경계도 수락 수 0으로 실패했다.

### 구현
- `BrokerPublisher.Publish`가 `SubscriptionTable.CopySubscribers`로 현재 구독자 snapshot 을 읽는다.
- snapshot 배열은 `ArrayPool<IConnection>`에서 대여하고, 구독자 수가 배열보다 크면 더 큰 배열로 재시도한다.
- 구독자마다 같은 `RefCountedBuffer`에 `AddRef()`한 뒤 `TransportSendBuffer(payload, 0, payload.Length)`로 `ITransport.TrySend`를 호출한다.
- `TrySend`가 false 를 반환하거나 전송 시도 중 예외가 나면 Broker 가 방금 추가한 구독자 ref 를 즉시 `Release()`한다.
- publish guard ref 는 caller 소유로 유지한다. BrokerPublisher 는 Publish 반환 시 caller ref 를 해제하지 않는다.

### 상태 갱신
- `DECISIONS.md`에 D034로 Broker publish fan-out 소유권 결정을 기록했다.
- `CURRENT_PLAN.md`를 publish fan-out 완료 및 사용자 리뷰 대기 상태로 갱신했다.
- `TODOS.md`에 publish fan-out 완료 항목을 추가하고 다음 구현 후보는 리뷰 후 진행하도록 남겼다.

### 검증
- `dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter "FullyQualifiedName~BrokerPublisherTests"` → Red: 타입 부재 실패 1 → 계약 부재 실패 1/통과 1 → 동작 Red 실패 2/통과 2 → Green 통과 4.
- `dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter "FullyQualifiedName~BrokerPublisherTests"` → 최종 통과 4, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj` → 통과 8, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Broker.Tests` 통과 8 + `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 26 + `Hps.Protocol.Tests` 통과 23, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음. Git의 LF↔CRLF 안내 경고만 출력됨.

## 2026-06-11 (Codex — Broker subscription routing table)

### 작업 단위
- Phase 3 Broker 의 첫 단위로 `src/Hps.Broker`와 `tests/Hps.Broker.Tests` 프로젝트를 추가했다.
- 범위는 topic 별 subscription routing table 로 제한했다. publish fan-out, command handler, backpressure 는 포함하지 않았다.

### Red
- Broker 프로젝트/`SubscriptionTable` 타입 부재를 reflection 기반 테스트의 `Assert.NotNull` 실패로 확인했다.
- `Subscribe`/`Unsubscribe`/`IsSubscribed`/`CountSubscribers`/`CopySubscribers` API 부재를 reflection 기반 테스트의 단언 실패로 확인했다.
- 직접 public API 동작 테스트는 no-op stub 에서 구독 추가, 해지, snapshot 복사, D008 R1 동시 subscribe-vs-unsubscribe 경계를 만족하지 못해 실패했다.

### 구현
- `SubscriptionTable`을 추가해 `topic -> connection set` 라우팅을 관리한다.
- D008에 따라 구독자 set 이 비어도 topic entry 를 즉시 제거하지 않는 NoCleanup 정책을 적용했다.
- connection 비교는 reference equality 로 고정했다. 같은 connection handle 만 같은 구독자로 취급한다.
- `CopySubscribers`는 caller 제공 배열에 가능한 만큼 복사하고, 반환값으로 전체 구독자 수를 알려 fan-out caller 가 재시도 크기를 판단할 수 있게 했다.
- 테스트는 Green 후 reflection 계약 테스트를 제거하고 직접 public API 테스트만 남겼다.

### 상태 갱신
- `DECISIONS.md`에 D033으로 Broker subscription routing 결정을 기록했다.
- `TODOS.md`의 기존 Broker routing deferred 항목은 완료로 이동했다.
- `CURRENT_PLAN.md`를 routing table 완료 및 사용자 리뷰 대기 상태로 갱신했다.

### 검증
- `dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj --filter "FullyQualifiedName~BrokerRoutingTests"` → Red: 타입 부재 실패 1 → API 부재 실패 1/통과 1 → 동작 Red 실패 4/통과 2 → Green 통과 6 → 리팩터 후 통과 4.
- `dotnet test tests\Hps.Broker.Tests\Hps.Broker.Tests.csproj` → 통과 4, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Broker.Tests` 통과 4 + `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 26 + `Hps.Protocol.Tests` 통과 23, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음. Git의 LF↔CRLF 안내 경고만 출력됨.

## 2026-06-11 (Codex — TCP frame receive handler hardening)

### 작업 단위
- `.claude/review/phase3-frame-adapter-command.md`의 low 관찰 중 O1/O2만 작은 Protocol hardening 단위로 처리했다.
- O3 랜덤 적대적 fuzz 는 비차단 테스트 보강이므로 이번 커밋에 섞지 않고 `TODOS.md` Deferred Backlog 로 남겼다.

### Red
- `OnConnectionClosed_AfterPayloadTooLarge_NotifiesFrameHandlerOnlyOnce`는 PayloadTooLarge 후 Transport close 알림이 다시 오면
  상위 `OnConnectionClosed`가 2회 호출되어 실패했다.
- `OnReceived_WhenFrameHandlerThrows_ReleasesFrameAndClosesConnection`은 `OnFrame` 예외 후 frame 이 반환되지 않아
  `pool.RentedCount == 1`로 남는 실패를 확인했다.

### 구현
- `TcpFrameReceiveHandler`가 connection 별 close 통지를 한 번만 수행하도록 `ConditionalWeakTable` 기반 weak marker 를 추가했다.
- `ITcpFrameHandler.OnFrame` 계약을 정상 반환 시 소유권 이전으로 명확히 하고, 예외 시 어댑터가 frame 을 `Release()`하도록 했다.
- `OnFrame` 예외는 해당 connection 의 protocol 처리 실패로 보고 assembler 를 제거한 뒤 connection 을 닫고 close 를 1회 통지한다.

### 상태 갱신
- `DECISIONS.md`에 D032로 dispatch 실패와 close 통지 멱등성 결정을 기록했다.
- `CURRENT_PLAN.md`와 `TODOS.md`를 hardening 완료 및 사용자 리뷰 대기 상태로 갱신했다.
- D010 랜덤 적대적 fuzz 는 `P3_NICE` deferred backlog 로 분리했다.

### 검증
- `dotnet test tests\Hps.Protocol.Tests\Hps.Protocol.Tests.csproj --filter "FullyQualifiedName~TcpFrameReceiveHandlerTests"` → Red: 실패 2/통과 5 → Green 통과 7.
- `dotnet test tests\Hps.Protocol.Tests\Hps.Protocol.Tests.csproj --filter "FullyQualifiedName~TcpFrameReceiveHandlerTests"` → 최종 통과 7, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Protocol.Tests\Hps.Protocol.Tests.csproj` → 통과 23, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 26 + `Hps.Protocol.Tests` 통과 23, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음. Git의 LF↔CRLF 안내 경고만 출력됨.

## 2026-06-11 (Codex — TCP command decoder)

### 작업 단위
- Phase 3의 TCP frame payload 를 broker command 로 해석하는 작은 Protocol 단위를 구현했다.
- `SUBSCRIBE <topic>`과 `PUBLISH <topic> <payload>`만 다룬다.
- `TcpCommand`는 topic/payload 를 복사하지 않고 frame 내부 span view 로 노출한다.
- Broker routing, subscription table, Server wiring, protocol error 응답은 포함하지 않았다.

### Red
- `TcpCommandDecoder` 타입 부재를 reflection 기반 테스트의 `Assert.NotNull` 실패로 확인했다.
- `TcpCommand`, `TcpCommandKind`, `TcpCommandDecodeError`, `TryDecode` 계약 부재를 reflection 기반 테스트의 단언 실패로 확인했다.
- 직접 동작 테스트 8개는 스텁 decoder 가 모든 입력을 false 로 반환해 subscribe/publish 성공 경계와 malformed error 경계를 만족하지 못해 실패했다.

### 구현
- `TcpCommandKind`에 `Subscribe`, `Publish` command 종류를 추가했다.
- `TcpCommandDecodeError`로 empty frame, unknown command, missing topic, invalid topic, missing publish payload separator 를 구분했다.
- `TcpCommand`를 `readonly ref struct`로 두어 topic/payload 가 원본 frame span 을 가리키는 수명 경계를 코드로 강제했다.
- `TcpCommandDecoder.TryDecode`는 예외 없이 `false + error`로 malformed input 을 보고한다.
- `PUBLISH` payload 는 topic 뒤 두 번째 공백 이후의 나머지 전체 byte 로 유지해 payload 내부 공백과 임의 byte 를 보존한다.

### 상태 갱신
- `DECISIONS.md`에 D031로 TCP command decode 문법과 span view 소유권 결정을 기록했다.
- `CURRENT_PLAN.md`와 `TODOS.md`를 decoder 완료 및 사용자 리뷰 대기 상태로 갱신했다.

### 검증
- `dotnet test tests\Hps.Protocol.Tests\Hps.Protocol.Tests.csproj --filter "FullyQualifiedName~TcpCommandDecoderTests"` → Red: 실패 1 → Green 통과 1 → Red: 실패 1/통과 1 → Green 통과 2 → Red: 실패 8/통과 3 → 최종 통과 9.
- `dotnet test tests\Hps.Protocol.Tests\Hps.Protocol.Tests.csproj` → 통과 21, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 26 + `Hps.Protocol.Tests` 통과 21, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음.

## 2026-06-11 (Codex — TCP receive frame 어댑터)

### 작업 단위
- Phase 3의 assembler ↔ Transport receive loop 연결을 작은 Protocol 단위로 구현했다.
- `TcpFrameReceiveHandler`가 `ITransportReceiveHandler`를 구현해 raw TCP chunk 를 connection 별 `TcpFrameAssembler`로 조립한다.
- 완성 frame 은 `ITcpFrameHandler.OnFrame`으로 소유권을 넘긴다. handler 는 받은 `RefCountedBuffer`를 Release 해야 한다.
- `PayloadTooLarge`는 D010 계약대로 connection 을 닫고 상위 handler 에 close 를 알린다.
- command codec, Broker, Server wiring 은 포함하지 않았다.

### Red
- `TcpFrameReceiveHandler` 타입 부재를 reflection 기반 테스트의 `Assert.NotNull` 실패로 확인했다.
- `ITcpFrameHandler`/constructor/`ITransportReceiveHandler` 계약 부재를 reflection 기반 테스트의 `Assert.NotNull` 실패로 확인했다.
- 동작 테스트 3개는 빈 adapter 구현에서 frame 전달 0건, partial payload 대여 0건, close 호출 0건으로 실패했다.

### 구현
- `src/Hps.Protocol`이 `Hps.Transport` public abstraction 을 참조하도록 project reference 를 추가했다.
- `ITcpFrameHandler`를 추가해 완성 frame 과 connection close 알림을 Protocol 상위 계층으로 전달한다.
- `TcpFrameReceiveHandler`가 connection 별 assembler dictionary 를 관리하고, `TransportReceiveBuffer`의 `Span`을 consumed loop 로 처리한다.
- `OnConnectionClosed`는 partial payload 를 가진 assembler 를 Dispose 해 풀 누수를 막는다.
- `PayloadTooLarge`를 받으면 assembler 를 제거하고 connection 을 닫은 뒤 상위 close callback 을 호출한다.

### 상태 갱신
- `DECISIONS.md`에 D030으로 TCP raw receive → frame callback 어댑터 결정을 기록했다.
- `CURRENT_PLAN.md`와 `TODOS.md`를 사용자 리뷰 대기 상태로 갱신했다.

### 검증
- `dotnet test tests\Hps.Protocol.Tests\Hps.Protocol.Tests.csproj --filter "FullyQualifiedName~TcpFrameReceiveHandlerTests"` → Red: 실패 1 → Green 통과 2 → Red: 실패 3/통과 2 → 최종 통과 5.
- `dotnet test tests\Hps.Protocol.Tests\Hps.Protocol.Tests.csproj` → 통과 12, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 26 + `Hps.Protocol.Tests` 통과 12, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음. Git의 LF↔CRLF 안내 경고만 출력됨.

## 2026-06-11 (Codex — TCP 프레임 조립기 edge/fuzz 테스트 보강)

### 작업 단위
- `.claude/review/phase3-frame-assembler.md`의 should-add 항목 중 `TcpFrameAssembler` 단위 테스트 보강만 처리했다.
- 범위는 `tests/Hps.Protocol.Tests/TcpFrameAssemblerTests.cs`의 테스트 추가와 상태 문서 갱신으로 제한했다.
- production code 는 변경하지 않았다.

### 테스트
- 0 length frame 이 `FrameReady`로 완성되고 caller 가 Release 할 소유권 있는 빈 `RefCountedBuffer`를 받는지 검증했다.
- 하나의 TCP chunk 에 여러 frame 이 붙었을 때 첫 호출이 첫 frame 길이까지만 `consumed`로 보고하고,
  caller 가 remaining slice 로 재호출해 다음 frame 을 읽는 계약을 검증했다.
- `payloadLength == maxPayloadLength`가 성공하는지 검증해 최대 payload 경계의 오프바이원 회귀를 막았다.
- 결정적 fuzz 테스트로 24개 frame 을 1/2/7/3/11/5/1/13/4바이트 패턴 chunk 로 쪼개고,
  consumed 기반 caller loop 가 참조 payload 목록과 같은 순서·내용으로 복원하는지 확인했다.

### 결과
- 추가 테스트는 기존 `TcpFrameAssembler` 구현으로 즉시 통과했다.
- 따라서 이번 단위는 D010 회귀 테스트 고정이며 구현 변경은 없었다.

### 검증
- `dotnet test tests\Hps.Protocol.Tests\Hps.Protocol.Tests.csproj --filter "FullyQualifiedName~TcpFrameAssemblerTests"` → 통과 7, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Protocol.Tests\Hps.Protocol.Tests.csproj` → 통과 7, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 26 + `Hps.Protocol.Tests` 통과 7, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음. Git의 LF↔CRLF 안내 경고만 출력됨.

## 2026-06-11 (Codex — TCP 프레임 조립기 기본 계약)

### 작업 단위
- Phase 3 첫 Protocol 단위로 `Hps.Protocol`과 `Hps.Protocol.Tests` 프로젝트를 추가했다.
- TCP 4바이트 big-endian length-prefix frame 을 connection 단위로 조립하는 `TcpFrameAssembler` 기본 계약을 구현했다.
- 범위는 fragmented header/payload 조립, maxPayload 초과 거부, partial payload dispose 반환으로 제한했다.

### Red
- `TcpFrameAssembler` 타입 부재를 reflection 기반 테스트의 `Assert.NotNull` 실패로 확인했다.
- `TryReadFrame` API 부재를 reflection 기반 테스트의 `Assert.NotNull` 실패로 확인했다.
- 직접 public API 테스트 3개는 스텁 구현에서 `NeedMoreData`만 반환하거나 partial buffer 를 대여하지 않아 단언 실패했다.

### 구현
- `TcpFrameReadStatus`를 추가했다: `NeedMoreData`, `FrameReady`, `PayloadTooLarge`.
- `TcpFrameAssembler`는 header 4바이트를 누적하고, payload length 가 허용 범위이면 `RefCountedBuffer`를 대여해 payload 를 누적 복사한다.
- frame 완성 시 `SetLength` 후 caller 에 buffer 소유권을 넘기고, 내부 상태를 다음 frame 을 받을 수 있게 초기화한다.
- 조립 중인 payload 가 있으면 `Dispose()`에서 정확히 한 번 `Release()`한다.

### 상태 갱신
- `DECISIONS.md`에 D029로 TCP 프레임 조립 API와 소유권 경계를 기록했다.
- `CURRENT_PLAN.md`를 Phase 3 진입 상태로 갱신했다.
- `TODOS.md`에 완료 항목과 후속 frame edge/fuzz coverage backlog 를 기록했다.

### 검증
- `dotnet test tests\Hps.Protocol.Tests\Hps.Protocol.Tests.csproj --filter "FullyQualifiedName~TcpFrameAssemblerTests"` → Red: 실패 3, 통과 1. Green: 통과 4.
- 리팩터 후 `dotnet test tests\Hps.Protocol.Tests\Hps.Protocol.Tests.csproj` → 통과 3, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 26 + `Hps.Protocol.Tests` 통과 3, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음. Git의 LF↔CRLF 안내 경고만 출력됨.

## 2026-06-11 (Codex — TCP 동시 연결 echo 통합 테스트)

### 작업 단위
- Phase 2 테스트 기준의 동시 연결 안정성을 TCP echo loopback 통합 테스트로 보강했다.
- 범위는 `SaeaTransportTests`의 테스트 추가와 상태 문서 갱신으로 제한했다. production code 는 변경하지 않았다.

### 테스트
- `TcpEcho_WhenMultipleClientsSendConcurrently_EchoesEachPayloadAndReturnsBuffers`를 추가했다.
- 테스트는 loopback listener 에 8개 raw TCP client 를 연결하고, 각 accepted `IConnection`의 receive/send pump 가 동시에 echo 왕복을 처리하는지 검증한다.
- client 별 payload 를 다르게 만들어 connection 간 응답 섞임을 단언으로 드러내고, echo buffer pool 이 `RentedCount==0`으로 돌아오는지 확인한다.
- 모든 inbound connection 을 닫은 뒤 transport 내부 추적 수가 0으로 돌아와 단명 connection churn 의 누수 회귀도 함께 방어한다.

### 결과
- focused 실행에서 기존 TCP receive pump + send pump + connection tracking 구현만으로 동시 echo 왕복이 통과했다.
- 따라서 이번 단위는 production 변경 없이 통합 회귀 테스트만 추가했다.

### 검증
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~TcpEcho_WhenMultipleClientsSendConcurrently_EchoesEachPayloadAndReturnsBuffers"` → 통과 1, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj` → 통과 26, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 26, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음. Git의 LF↔CRLF 안내 경고만 출력됨.

## 2026-06-11 (Codex — UDP echo loopback 통합 테스트)

### 작업 단위
- Phase 2 완료 기준의 UDP loopback echo 왕복을 작은 test-only 단위로 고정했다.
- 범위는 `SaeaTransportTests`의 통합 테스트와 상태 문서 갱신으로 제한했다. production code 는 변경하지 않았다.

### 테스트
- `UdpEcho_WhenDatagramHandlerQueuesResponse_ClientReceivesSamePayload`를 추가했다.
- 테스트 handler 는 UDP receive 로 받은 owned `RefCountedBuffer`에 Transport 송신 ref 를 먼저 추가하고,
  같은 `IUdpEndpoint`의 `TrySendTo`로 원격 endpoint 에 되돌려 보낸다.
- `TrySendTo` 성공 뒤 handler guard ref 를 Release 하고, send pump completion 이 남은 ref 를 반환하는 경계를 실제 socket 왕복으로 검증한다.

### 결과
- focused 실행에서 기존 UDP receive loop + endpoint send pump 구현만으로 echo 왕복이 통과했다.
- 따라서 이번 단위는 production 변경 없이 통합 회귀 테스트만 추가했다.

### 검증
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~UdpEcho_WhenDatagramHandlerQueuesResponse_ClientReceivesSamePayload"` → 통과 1, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj` → 통과 25, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 25, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음. Git의 LF↔CRLF 안내 경고만 출력됨.

## 2026-06-11 (Codex — TCP echo loopback 통합 테스트)

### 작업 단위
- Phase 2 완료 기준의 TCP loopback echo 왕복을 작은 test-only 단위로 고정했다.
- 범위는 `SaeaTransportTests`의 통합 테스트와 상태 문서 갱신으로 제한했다. production code 는 변경하지 않았다.

### 테스트
- `TcpEcho_WhenReceiveHandlerQueuesResponse_ClientReceivesSamePayload`를 추가했다.
- 테스트 handler 는 `TransportReceiveBuffer`가 콜백 동안만 유효하다는 계약을 지키기 위해 payload 를 테스트 전용
  `RefCountedBuffer`로 즉시 복사한다.
- handler 는 echo buffer 에 publish 가드 ref 와 Transport 송신 ref 를 분리해 적용하고,
  `TrySend` 성공 뒤 publish 가드 ref 를 Release 한다. 송신 completion 뒤 `RentedCount==0`으로 돌아오는지 확인한다.

### 결과
- focused 실행에서 기존 recv pump + send pump 구현만으로 echo 왕복이 통과했다.
- 따라서 이번 단위는 production 변경 없이 통합 회귀 테스트만 추가했다.

### 검증
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~TcpEcho_WhenReceiveHandlerQueuesResponse_ClientReceivesSamePayload"` → 통과 1, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj` → 통과 24, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 24, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음. Git의 LF→CRLF 안내 경고만 출력됨.

## 2026-06-11 (Codex — UDP endpoint send 직렬화)

### 작업 단위
- `.claude/review/phase2-udp-datagram.md`의 S2 중 UDP send 의 datagram별 `Task.Run` 생성을 제거했다.
- 범위는 endpoint별 pending send queue 와 단일 send pump, close 전 queued datagram drain 으로 제한했다.
  UDP receive backpressure 정책(Q1)은 fan-out/backpressure 결정과 맞물리므로 별도 backlog 로 유지했다.

### Red
- `SaeaTransportTests`에 `UdpSendTo_WhenEndpointClosesBeforePumpSends_DrainsQueuedDatagramRef`를 추가했다.
- 구현 전에는 `SaeaUdpEndpoint.PendingSendCount`가 없어 `Assert.NotNull` 단언 실패가 발생했다.

### 구현
- `SaeaUdpEndpoint`에 pending send queue, send signal, `PendingSendCount`, `TryAcceptSend`, `TryBeginSend`를 추가했다.
- `SaeaTransport.TrySendTo`는 live buffer 검증 후 endpoint queue 에 송신 요청을 수락시키며, datagram마다 별도 `Task.Run`을 만들지 않는다.
- `BindUdpAsync`는 endpoint별 단일 UDP send loop 를 시작한다. loop 는 queued datagram 을 순차적으로 `SendToAsync`로 보내고,
  기존 `SendUdpDatagramAsync` finally 경로에서 Transport 소유 ref 를 정확히 한 번 Release 한다.
- `SaeaUdpEndpoint.Close()`는 아직 pump 가 가져가지 않은 queued datagram 을 drain 하고 각 `RefCountedBuffer`를 Release 한다.

### 테스트
- Red 단계에서는 pending queue 관측 지점 부재를 reflection 으로 확인했다.
- Green 후에는 테스트를 internal API 직접 검증으로 리팩터링해 endpoint queue 상태와 close drain 결과를 명확히 단언했다.
- 기존 UDP send loopback 테스트와 함께 실행해 실제 datagram 전송, offset/length 범위, completion Release 계약이 유지되는지 확인했다.

### 상태 갱신
- `CURRENT_PLAN.md`를 UDP send 직렬화 반영 상태와 다음 리뷰 대기 지점으로 갱신했다.
- `TODOS.md`에서 combined UDP send/receive backlog 를 receive backpressure 항목으로 축소하고, send 직렬화 완료 항목을 추가했다.
- `DECISIONS.md`에 UDP send 는 endpoint별 pending queue 와 단일 pump 로 처리한다는 D028을 기록했다.

### 검증
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~UdpSendTo_WhenEndpointClosesBeforePumpSends_DrainsQueuedDatagramRef"` → Red: `PendingSendCount` 부재 실패 1, Green: 통과 1, 실패 0, 건너뜀 0.
- Green 후 테스트 리팩터링: UDP focused 테스트 2개 통과, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj` → 통과 23, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 23, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음. Git의 LF→CRLF 안내 경고만 출력됨.

## 2026-06-11 (Codex — Hps.Transport 폴더 구조 분리)

### 작업 단위
- 보기 어려워진 `src/Hps.Transport` flat 파일 배치를 책임별 하위 폴더로 분리했다.
- 동작, namespace, public API 는 바꾸지 않는 파일 이동 전용 refactor 로 제한했다.

### 구조
- `src/Hps.Transport/Abstractions`: `ITransport`, `IConnection`, listener/handler/endpoint 계약, `TransportSendBuffer`, `TransportReceiveBuffer`.
- `src/Hps.Transport/Runtime`: `TransportBase`, `TransportConnection`, `TransportFactory`.
- `src/Hps.Transport/Saea`: `SaeaTransport`, `SaeaConnectionListener`, `SaeaUdpEndpoint`.
- `tests/Hps.Transport.Tests/Contracts`: public 계약 테스트.
- `tests/Hps.Transport.Tests/Runtime`: 공통 queue/ownership runtime 테스트.
- `tests/Hps.Transport.Tests/Saea`: SAEA loopback/backend 기준선 테스트.

### 상태 갱신
- `AGENTS.md`의 프로젝트 레이아웃에 `Hps.Transport` 하위 폴더 책임을 추가했다.
- `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`를 새 구조 기준으로 갱신했다.
- `DECISIONS.md`에는 D027로 이후 파일 추가 위치 규칙을 남겼다.

### 검증
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj` → 통과 22, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 22, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음. Git의 LF→CRLF 안내 경고만 출력됨.

## 2026-06-11 (Codex — TransportFactory SAEA fallback 기준선)

### 작업 단위
- Phase 2 backend selector 최소 계약으로 `TransportFactory.CreateDefault()`를 추가했다.
- 범위는 상위 계층이 concrete backend 를 직접 선택하지 않도록 `ITransport` 생성 진입점을 만드는 데 한정했다.
  실제 OS/capability probe, RIO/io_uring backend 선택, backend 옵션은 포함하지 않았다.

### Red
- `TransportContractTests`에 기본 Transport factory 계약 테스트를 추가했다.
- 구현 전에는 `TransportFactory` 타입이 없어 `Assert.NotNull` 단언 실패가 발생했다.

### 구현
- `TransportFactory` 정적 클래스를 추가했다.
- `CreateDefault()`는 현재 모든 환경에서 크로스플랫폼 기준선인 `SaeaTransport`를 `ITransport`로 반환한다.
- XML doc에는 현재 fallback 성격과 이후 backend probe 확장 위치임을 명시했다.

### 테스트
- Red 단계에서는 타입 부재를 확인하기 위해 reflection 으로 factory 존재를 검사했다.
- Green 이후에는 테스트를 직접 `TransportFactory.CreateDefault()` 호출로 리팩터링해 public API 사용 형태를 고정했다.
- 반환값이 `ITransport`로 사용 가능하고 현재 fallback 이 `SaeaTransport`인지 검증했다.

### 상태 갱신
- `CURRENT_PLAN.md`를 backend selector 최소 계약 완료와 테스트 40개 기준으로 갱신했다.
- `TODOS.md`에서 backend selector 항목을 Completed 로 옮기고, 다음 후보로 UDP endpoint send 직렬화/backpressure 항목을 유지했다.
- `DECISIONS.md`에 기본 Transport 생성 진입점을 D026으로 기록했다.

### 검증
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~TransportFactory_CreateDefault_ReturnsSaeaFallbackAsITransport"` → Red: `TransportFactory` 타입 부재 실패 1, Green: 통과 1, 실패 0, 건너뜀 0.
- Green 후 테스트 리팩터링: 같은 focused 테스트 통과 1, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj` → 통과 22, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 22, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음. Git의 LF→CRLF 안내 경고만 출력됨.

## 2026-06-11 (Codex — UDP datagram receive 소유권 이전 순서 수정)

### 작업 단위
- `.claude/review/phase2-udp-datagram.md`의 S1 should-fix 를 반영해 UDP receive loop 의 datagram 소유권 이전 시점을 명확히 했다.
- 범위는 handler 호출 전 local ref 차단과 회귀 테스트로 제한했다. UDP send pump/배압 정책(S2/Q1)은 별도 backlog 로 이월했다.

### Red
- `SaeaTransportTests`에 handler 가 받은 `RefCountedBuffer`를 Release 한 뒤 예외를 던지는 회귀 테스트를 추가했다.
- 구현 전에는 receive loop catch 가 같은 datagram 을 다시 Release 하면서 원래 handler 예외가 `InvalidOperationException`으로 덮였다.

### 구현
- `UdpReceiveLoopAsync`에서 `SetLength` 후 `ownedDatagram`으로 소유권을 옮기고, handler dispatch 전에 local `datagram`을 null 로 끊었다.
- 이 순서로 handler 호출 시점부터 Release 책임이 `ITransportDatagramHandler` 계약으로 넘어가며, handler 예외 경로에서 loop catch 가 이미 이전된 ref 를 다시 만지지 않는다.

### 테스트
- public receive API 는 background loop 예외를 노출하지 않으므로, 이번 회귀 테스트는 private receive loop 를 reflection 으로 직접 실행한다.
- handler 가 Release 후 던진 고유 예외가 double-release 예외로 덮이지 않는지 검증한다.
- 테스트 주석에는 white-box 테스트가 필요한 이유와 보호하는 소유권 경계를 한국어로 남겼다.

### 상태 갱신
- `CURRENT_PLAN.md`를 S1 반영 상태와 테스트 39개 기준으로 갱신했다.
- `TODOS.md`에 S1 완료 항목을 추가하고, S2/Q1을 UDP endpoint send 직렬화와 receive backpressure 정책 backlog 로 남겼다.
- `DECISIONS.md`에 UDP datagram handler 호출 시점의 소유권 이전 결정을 D025로 기록했다.

### 검증
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~UdpReceive_WhenHandlerThrowsAfterTakingOwnership_DoesNotReleaseDatagramAgain"` → Red: `InvalidOperationException`으로 예외가 덮여 실패 1, Green: 통과 1, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj` → 통과 21, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 21, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음. Git의 LF→CRLF 안내 경고만 출력됨.

## 2026-06-11 (Codex — UDP datagram 계약과 SAEA 기준선)

### 작업 단위
- UDP datagram public 계약을 TCP connection/listener 모델과 분리하고, `SaeaTransport`의 UDP loopback bind/send/receive 기준선을 구현했다.
- 범위는 `IUdpEndpoint`, datagram handler, UDP bind/send/receive 와 ref 반환 검증으로 제한했다.
  UDP 신뢰성/순서 보장/혼잡 제어, backend selector, RIO/io_uring 구현은 포함하지 않았다.

### Red
- `TransportContractTests`에 UDP endpoint/datagram handler 계약 테스트를 추가했다.
  구현 전에는 `IUdpEndpoint` 타입이 없어 `Assert.NotNull` 단언 실패가 발생했다.
- `SaeaTransportTests`에 UDP receive loopback 테스트를 추가했다.
  구현 전에는 `BindUdpAsync`가 `NotImplementedException`을 던졌다.
- `SaeaTransportTests`에 UDP send loopback 테스트를 추가했다.
  구현 전에는 `TrySendTo`가 `NotImplementedException`을 던졌다.

### 구현
- `IUdpEndpoint`를 추가해 UDP local endpoint 의 수명과 `LocalEndPoint`를 표현했다.
- `ITransportDatagramHandler`를 추가해 UDP receive 가 owned `RefCountedBuffer`를 handler 로 넘기도록 했다.
- `ITransport`에 `SetDatagramHandler`, `BindUdpAsync`, `TrySendTo`를 추가했다.
- `TransportBase`는 datagram handler 등록과 snapshot 을 공통 처리하고, 기존 테스트용 subclass 가 깨지지 않도록 UDP 메서드는 기본 `NotImplementedException` 구현을 제공한다.
- `SaeaUdpEndpoint`를 추가해 UDP socket 과 transport unregister 경계를 캡슐화했다.
- `SaeaTransport`는 UDP endpoint 를 bind 하고 receive loop 에서 pinned counted buffer 를 직접 대여해 datagram handler 로 전달한다.
- `TrySendTo`는 `TransportSendBuffer.Offset/Length` 범위만 UDP socket 으로 전송하고, true 반환 뒤 Transport 소유 ref 를 completion/unwind 경로에서 반환한다.

### 테스트
- UDP public 계약이 TCP accept 모델과 섞이지 않고, raw `Memory<byte>`/`ReadOnlyMemory<byte>`를 public datagram 계약에 노출하지 않는지 검증했다.
- 외부 UDP socket 이 보낸 datagram 이 handler 로 전달되고, handler 가 받은 `RefCountedBuffer`를 직접 Release 하는지 검증했다.
- `TrySendTo`가 offset/length 범위만 원격 UDP socket 에 보내며, publish guard ref 해제 뒤 Transport 소유 ref 가 반환되어 `RentedCount==0`으로 돌아오는지 검증했다.
- 테스트에는 각 테스트가 보호하는 UDP datagram 소유권과 1 datagram = 1 message 의도를 한국어 주석으로 남겼다.

### 상태 갱신
- `CURRENT_PLAN.md`를 UDP datagram 기준선과 테스트 38개 통과 상태로 갱신했다.
- `TODOS.md`에서 UDP datagram 항목을 Completed 로 옮기고, 다음 후보를 Phase 2 backend selector 최소 계약으로 남겼다.
- `DECISIONS.md`에 UDP datagram 과 `RefCountedBuffer` handler 소유권 결정을 D024로 기록했다.

### 검증
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~Transport_Contract_ExposesUdpDatagramModelWithoutTcpConnection"` → Red: `IUdpEndpoint` 타입 부재 실패 1, Green: 통과 1, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~UdpReceive_WhenSocketSendsDatagram_DeliversOwnedRefCountedBuffer"` → Red: `BindUdpAsync` `NotImplementedException` 실패 1, Green: 통과 1, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~UdpSendTo_WhenTrySendToBoundEndpoint_SendsRequestedDatagramAndReleasesRef"` → Red: `TrySendTo` `NotImplementedException` 실패 1, Green: 통과 1, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj` → 통과 20, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 20, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → whitespace 오류 없음. Git의 LF→CRLF 안내 경고만 출력됨.

## 2026-06-11 (Codex — SaeaTransport TCP send pump 기준선)

### 작업 단위
- `SaeaTransport`가 `ITransport.TrySend`로 enqueue 된 `TransportSendBuffer`를 실제 TCP socket 으로 전송하는 최소 기준선을 구현했다.
- 범위는 raw TCP payload send 와 in-flight ref 반환으로 제한했다. 프레이밍, UDP, backpressure, 명시적 SocketAsyncEventArgs 최적화는 포함하지 않았다.

### Red
- `SaeaTransportTests`에 accepted connection 으로 `TrySend`한 payload 가 raw socket client 로 도착하고,
  send completion 뒤 `RefCountedBuffer`가 풀로 반환되는지 검증하는 테스트를 추가했다.
- 구현 전에는 send pump 가 없어 client receive 가 5초 안에 완료되지 않아 timeout 단언 실패가 발생했다.

### 구현
- `TransportConnection`에 pending send signal 을 추가했다. 빈 큐에서 첫 항목이 enqueue 되거나 close 로 펌프를 깨워야 할 때만 signal 을 보낸다.
- `SaeaTransport`가 connection 생성 시 send loop 를 시작하게 했다.
- send loop 는 `TryBeginInFlightSend`로 in-flight handle 을 얻고, `TransportSendBuffer.Offset/Length` 범위만 socket 으로 전송한다.
- send completion 은 `InFlightSend.Complete()`를 호출하고, socket error/close/unwind 경로는 `Dispose()`가 Transport 소유 ref 를 반환한다.

### 테스트
- payload 앞뒤에 sentry byte 를 둔 뒤 `TransportSendBuffer`의 offset/length 범위만 client 가 받는지 검증했다.
- publish guard ref 를 먼저 `Release()`한 뒤에도 send completion 전에는 풀로 돌아가지 않고, completion 뒤 `RentedCount==0`이 되는지 검증했다.
- 테스트에는 TCP send pump 가 보호해야 하는 socket write 와 ref 반환 의도를 한국어 주석으로 남겼다.

### 상태 갱신
- `CURRENT_PLAN.md`를 TCP send pump 기준선과 테스트 35개 통과 상태로 갱신했다.
- `TODOS.md`에서 TCP send pump 항목을 Completed 로 옮기고, 다음 후보를 UDP datagram public 계약/SAEA 기준선으로 남겼다.
- `DECISIONS.md`에 TCP send pump 의 raw Socket baseline 과 in-flight handle 재사용 결정을 D023으로 기록했다.

### 검증
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~SendPump_WhenTrySendAcceptedConnection_SendsRequestedPayloadAndReleasesRef"` → Red: timeout 단언 실패 1, Green: 통과 1, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj` → 통과 17, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 17, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → 문제 없음.

## 2026-06-11 (Codex — SaeaTransport connection unregister 누수 수정)

### 작업 단위
- 사용자 리뷰에서 지적된 `SaeaTransport._connections` 등록 해제 누락을 수정했다.
- 범위는 닫힌 connection 의 transport 추적 참조 제거와 close 시 socket dispose lock 분리로 제한했다.
  TCP send pump, 프레이밍, UDP, backpressure 는 포함하지 않았다.

### Red
- `SaeaTransportTests`에 accepted connection 을 `Close()`한 뒤 transport 내부 tracking count 가 0으로 돌아오는지 검증하는 회귀 테스트를 추가했다.
- 구현 전에는 `_connections`에 닫힌 connection 이 남아 `Expected: 0`, `Actual: 1` 단언 실패가 발생했다.

### 구현
- `TransportConnection`에 close callback 을 추가해 첫 `Close()` 성공 경로에서만 transport 에 등록 해제를 알린다.
- `SaeaTransport`는 connection 생성 시 `UnregisterConnection` callback 을 넘기고, 개별 connection close 시 `_connections`에서 제거한다.
- `TransportConnection.Close()`는 pending drain 과 closed 표시를 lock 안에서 끝낸 뒤,
  unregister callback 과 backend socket dispose 를 lock 밖에서 수행하도록 정리했다.

### 테스트
- raw socket client 로 listener 에 연결해 transport 가 추적하는 accepted connection 하나를 만든다.
- accepted `IConnection.Close()` 이후 transport 내부 추적 목록 count 가 0으로 감소하는지 검증했다.
- 테스트에는 목적 주석을 남겨 단명 connection churn 에서 닫힌 socket 참조가 누적되는 회귀를 막는다는 의도를 명시했다.

### 상태 갱신
- `CURRENT_PLAN.md`를 connection tracking 누수 해소와 테스트 34개 통과 상태로 갱신했다.
- `TODOS.md`에 이번 누수 수정 단위를 Completed 로 기록하고, 다음 구현 단위는 계속 TCP send pump 기준선으로 남겼다.
- `DECISIONS.md`에 닫힌 SAEA connection unregister 와 dispose lock 분리를 D022로 기록했다.

### 검증
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~Close_WhenAcceptedConnectionIsClosed_RemovesTransportTrackingReference"` → Red: 실패 1(`Expected: 0`, `Actual: 1`), Green: 통과 1, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj` → 통과 16, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 16, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → 문제 없음.

## 2026-06-11 (Codex — SaeaTransport TCP recv pump 기준선)

### 작업 단위
- `SaeaTransport`가 실제 TCP socket 에서 받은 raw byte stream 조각을 receive handler 로 전달하는 최소 기준선을 구현했다.
- 프레이밍, `RefCountedBuffer` payload 조립, 송신 펌프, UDP, 명시적 SocketAsyncEventArgs 최적화는 넣지 않고 recv chunk 전달만 별도 단위로 처리했다.

### Red
- `SaeaTransportTests`에 raw socket client 가 loopback listener 로 작은 byte 배열을 보내고,
  accepted `IConnection`의 receive handler 가 해당 bytes 를 관측하는 테스트를 추가했다.
- 구현 전에는 handler 가 호출되지 않아 timeout 단언 실패가 발생했다.

### 구현
- `SaeaTransport`가 connection 생성 시 receive loop 를 시작하게 했다.
- receive loop 는 `PinnedBlockMemoryPool`에서 receive block 을 대여하고, socket recv 결과를 `TransportReceiveBuffer` borrowed view 로 동기 dispatch 한다.
- `TransportReceiveBuffer`는 async method 안에 보관하지 않고, 별도 동기 helper 에서만 생성해 ref struct 제약을 유지했다.
- remote close 또는 socket error 는 `ITransportReceiveHandler.OnConnectionClosed`를 호출하고 `IConnection.Close()` 경로로 정리한다.

### 테스트
- raw socket client 가 `{10,20,30,40}` payload 를 보내면 handler 가 같은 bytes 를 받는지 검증했다.
- handler 가 받은 `IConnection`이 listener 에서 accept 한 inbound connection 과 같은 instance 인지 검증했다.
- 테스트 helper 는 borrowed buffer 를 콜백 안에서 즉시 복사해, 콜백 이후 span 수명에 의존하지 않게 했다.

### 상태 갱신
- `CURRENT_PLAN.md`를 TCP recv pump 기준선과 테스트 33개 통과 상태로 갱신했다.
- `TODOS.md`에서 이번 recv pump 기준선을 Completed로 옮기고, 다음 리뷰 단위를 TCP send pump 기준선으로 남겼다.
- `DECISIONS.md`에 receive pump 의 pinned block 사용과 raw chunk 전달 범위를 D021로 기록했다.

### 검증
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~ReceivePump_WhenRawClientSendsBytes_DeliversBorrowedChunkToHandler"` → Red: timeout 단언 실패 1, Green: 통과 1, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj` → 통과 15, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 15, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → 문제 없음.

## 2026-06-11 (Codex — Transport receive delivery 계약)

### 작업 단위
- TCP payload I/O 구현 전에 Transport 가 상위 계층으로 recv byte stream 을 전달하는 public 계약을 확정했다.
- 실제 socket recv pump, 프레이밍, `RefCountedBuffer` payload 조립은 넣지 않고 borrowed receive boundary 만 별도 단위로 처리했다.

### Red
- `TransportContractTests`에 receive handler 와 borrowed receive buffer 계약 테스트를 추가했다.
- 구현 전에는 `ITransportReceiveHandler`/`TransportReceiveBuffer` 타입이 없어 `Assert.NotNull`이 실패했다.

### 구현
- `ITransport.SetReceiveHandler(ITransportReceiveHandler)`를 추가했다.
- `ITransportReceiveHandler`를 추가해 `OnReceived(IConnection, TransportReceiveBuffer)`와 `OnConnectionClosed(IConnection)`를 정의했다.
- `TransportReceiveBuffer`를 `readonly ref struct`로 추가해 콜백 동안만 유효한 `ReadOnlySpan<byte>` view 와 `Length`만 노출하게 했다.
- `TransportBase`에 receive handler 저장과 recv pump 용 snapshot helper 를 추가했다.

### 테스트
- receive delivery 계약이 `IConnection` public API 가 아니라 `ITransport` handler 경계에 있는지 검증했다.
- `TransportReceiveBuffer`가 byref-like 타입이며 `Span`/`Length`를 제공하는지 검증했다.
- handler/transport/receive buffer 계약이 raw `Memory<byte>`/`ReadOnlyMemory<byte>` parameter/property 를 노출하지 않는지 검증했다.

### 상태 갱신
- `CURRENT_PLAN.md`를 receive delivery 계약 확정 상태와 테스트 32개 통과 상태로 갱신했다.
- `TODOS.md`에서 receive delivery 계약을 Completed로 옮기고, 다음 리뷰 단위를 `SaeaTransport` TCP recv pump 기준선으로 남겼다.
- `DECISIONS.md`에 borrowed receive delivery 경계를 D020으로 기록했다.

### 검증
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~Transport_Contract_ExposesBorrowedReceiveDeliveryBoundary"` → Red: 실패 1(`ITransportReceiveHandler`/`TransportReceiveBuffer` 없음), Green: 통과 1, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj` → 통과 14, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 14, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → 문제 없음.

## 2026-06-11 (Codex — SaeaTransport TCP loopback 연결 기준선)

### 작업 단위
- `SaeaTransport`가 실제 loopback TCP listener/connect/accept 를 수행해 양쪽 `IConnection`을 만들 수 있는 최소 기준선을 구현했다.
- payload send/recv, SocketAsyncEventArgs 버퍼 운용, UDP, backpressure 는 넣지 않고 연결 수명만 별도 단위로 처리했다.

### Red
- `SaeaTransportTests`에 localhost loopback 에서 listener 를 열고 outbound connect 와 inbound accept 를 수행하는 테스트를 추가했다.
- 구현 전에는 `SaeaTransport` 타입이 없어 reflection 기반 `Assert.NotNull`이 실패했다.

### 구현
- `SaeaTransport`를 추가해 `StartAsync`, `ListenTcpAsync`, `ConnectTcpAsync`, `StopAsync`, `Dispose`를 구현했다.
- `SaeaConnectionListener`를 추가해 listen socket 을 `IConnectionListener` 뒤에 숨기고, accepted socket 을 `TransportConnection`으로 등록하게 했다.
- `TransportConnection`에 backend resource dispose 경계를 추가해, 실제 socket 을 감싼 연결도 `Close()`에서 함께 닫히게 했다.
- listener 와 connection 은 Transport 내부 목록으로 추적하고, `StopAsync`/`Dispose`에서 idempotent close 경로를 재사용한다.

### 테스트
- 포트 0으로 listener 를 열고 `LocalEndPoint`의 실제 포트가 채워지는지 검증했다.
- listener 의 `AcceptAsync`와 transport 의 `ConnectTcpAsync`를 loopback 으로 연결해 inbound/outbound `IConnection`이 각각 생성되는지 검증했다.
- Green 후 테스트는 reflection 에서 직접 `SaeaTransport` public 타입 호출 방식으로 리팩터링했다.

### 상태 갱신
- `CURRENT_PLAN.md`를 SAEA TCP loopback 기준선과 테스트 31개 통과 상태로 갱신했다.
- `TODOS.md`에서 이번 loopback 기준선을 Completed로 옮기고, 다음 리뷰 단위를 Transport receive delivery 계약 확정으로 남겼다.
- `DECISIONS.md`에 TCP socket 수명과 이번 SAEA 기준선 범위를 D019로 기록했다.

### 검증
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~ListenConnectAccept_WhenLoopbackTcp_CreatesInboundAndOutboundConnections"` → Red: 실패 1(`SaeaTransport` 없음), Green: 통과 1, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj` → 통과 13, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 13, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → 문제 없음.

## 2026-06-11 (Codex — Transport TCP listen/connect/accept public 계약)

### 작업 단위
- Phase 2 SAEA 기준선에 들어가기 전에 상위 계층이 TCP 연결을 어떻게 얻는지 public 계약으로 고정했다.
- 실제 SAEA socket I/O, UDP datagram 계약, send/recv payload 처리는 넣지 않고 연결 획득 모델만 별도 단위로 처리했다.

### Red
- `TransportContractTests`에 TCP listen/connect/accept 계약 테스트를 추가했다.
- 구현 전에는 `IConnectionListener` 타입이 없어 `Assert.NotNull`이 실패했다.

### 구현
- `ITransport.ListenTcpAsync(EndPoint, CancellationToken)`를 추가해 TCP listener 생성 진입점을 명시했다.
- `ITransport.ConnectTcpAsync(EndPoint, CancellationToken)`를 추가해 outbound TCP 연결 생성 진입점을 명시했다.
- `IConnectionListener`를 추가해 listener 의 `LocalEndPoint`, `AcceptAsync`, `Close`/`Dispose` 계약을 분리했다.
- `TransportBase`에 TCP listen/connect 추상 멤버를 추가해 concrete transport 가 같은 계약을 구현하도록 했다.

### 테스트
- public contract 가 TCP listener, connect, accept 를 `IConnection`/`IConnectionListener` 중심으로 노출하는지 검증했다.
- listener 계약에 `LocalEndPoint`, `AcceptAsync`, `Close`, `IDisposable`이 있는지 검증했다.
- 기존 raw `Memory<byte>`/`ReadOnlyMemory<byte>` parameter 금지 검사를 listener 계약까지 확장했다.

### 상태 갱신
- `CURRENT_PLAN.md`를 TCP 연결 계약 확정 상태와 테스트 30개 통과 상태로 갱신했다.
- `TODOS.md`에서 public 연결 모델 항목을 Completed로 이동하고, 다음 리뷰 단위를 SAEA TCP loopback 기준선으로 남겼다.
- `DECISIONS.md`에 TCP 연결 획득 계약을 D018로 기록했다.

### 검증
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~Transport_Contract_ExposesTcpListenConnectAcceptModel"` → Red: 실패 1(`IConnectionListener` 없음), Green: 통과 1, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj` → 통과 12, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 12, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `git diff --check` → 문제 없음.

## 2026-06-11 (Codex — Transport in-flight handle abandon-leak 방어)

### 작업 단위
- `REVIEW_2026-06-11.md`의 위험 #1을 반영해, 송신 펌프가 dequeue 이후 close/unwind 로 completion 없이 빠져나가는 abandon-leak 경로를 막았다.
- 실제 socket send, SAEA 백엔드, listen/connect/accept 모델은 넣지 않고 in-flight 소유권 API만 별도 단위로 처리했다.

### Red
- `TransportSendQueueTests`에 pump abandon 시나리오를 보호하는 테스트를 추가했다.
- 구현 전에는 `TryBeginInFlightSend` 메서드가 없어 reflection 기반 `Assert.NotNull`이 실패했다.

### 구현
- `TransportConnection.TryDequeueSend(out TransportSendBuffer)` raw dequeue API를 제거했다.
- `TransportConnection.TryBeginInFlightSend(out InFlightSend?)`를 추가해 송신 펌프가 pending 항목을 dispose 가능한 handle 로 받게 했다.
- `InFlightSend.Complete()`와 `Dispose()`는 같은 release 경로를 타며, `Interlocked.Exchange`로 실제 Release 를 한 번만 수행한다.
- `Close()`는 여전히 pending 만 drain 한다. 이미 begin 된 in-flight ref 는 펌프 handle 의 completion/unwind 경로가 책임진다.

### 테스트
- close 이후 completion 없이 펌프가 unwind 되는 abandon 시나리오에서 `Dispose()`가 Transport 소유 ref 를 반환하는지 검증했다.
- 정상 completion 후 finally/dispose 가 다시 지나가도 이중 Release 가 발생하지 않는지 검증했다.
- 기존 close/in-flight 경계 테스트를 raw buffer release 에서 handle `Dispose()` 경로로 바꿨다.

### 상태 갱신
- `CURRENT_PLAN.md`를 in-flight handle 구현 상태와 테스트 29개 통과 상태로 갱신했다.
- `TODOS.md`에 이번 abandon-leak 방어 작업을 Completed로 기록하고, 다음 리뷰 단위는 Phase 2 연결 모델 확정으로 유지했다.
- `DECISIONS.md`의 D017을 raw `CompleteInFlightSend` 계약에서 handle 기반 completion/unwind 계약으로 갱신했다.

### 검증
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~InFlightSend_WhenPumpAbandonsAfterClose_DisposePathReleasesTransportOwnedRef"` → 통과 1, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~InFlightSend_WhenPumpCompletesDequeuedSend_CompleteReleasesTransportOwnedRef"` → 통과 1, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~TransportSendQueueTests"` → 통과 7, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj` → 통과 11, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 11, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

## 2026-06-10 (Codex — Transport in-flight completion release)

### 작업 단위
- 송신 펌프가 pending 큐에서 dequeue 한 in-flight 항목을 완료/취소/unwind 시 반환하는 최소 release 경로를 구현했다.
- 실제 socket send, SAEA 백엔드, listen/connect/accept 모델은 넣지 않고 completion ownership 경계만 별도 단위로 처리했다.

### Red
- `TransportSendQueueTests`에 `CompleteInFlightSend` 메서드 존재와 release 동작을 요구하는 테스트를 먼저 추가했다.
- 구현 전에는 reflection 기반 `Assert.NotNull`이 실패해 completion release 경로가 아직 없음을 확인했다.

### 구현
- `TransportConnection.CompleteInFlightSend(TransportSendBuffer)`를 추가했다.
- 이 메서드는 이미 `TryDequeueSend`로 pending 큐에서 빠져나온 항목의 Transport 소유 ref 를 `Release`한다.
- pending 큐 상태를 변경하지 않으므로 `_gate` lock 을 잡지 않는다. close 는 pending 만 drain 하고 in-flight 는 펌프 완료 경로가 책임진다는 D016 경계를 유지한다.

### 테스트
- close 이후에도 이미 dequeue 된 in-flight 항목은 close 가 반환하지 않고, `CompleteInFlightSend`가 반환하는지 검증했다.
- close 없이 정상 completion 만으로도 Transport 소유 ref 가 반환되어 `RentedCount==0`으로 돌아오는지 검증했다.
- Red 확인 뒤 테스트는 reflection 을 제거하고 internal API 직접 호출 방식으로 리팩터링했다.

### 상태 갱신
- `CURRENT_PLAN.md`를 in-flight completion release 구현 상태와 테스트 28개 통과 상태로 갱신했다.
- `TODOS.md`에서 이번 in-flight completion release 작업을 Completed로 이동하고, 다음 리뷰 단위를 Phase 2 연결 모델 확정으로 남겼다.
- `DECISIONS.md`에 completion callback 이 사용할 단일 in-flight release 경로를 D017로 기록했다.

### 검증
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~CompleteInFlightSend_WhenPumpCompletesDequeuedSend_ReleasesTransportOwnedRef"` → 통과 1, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~TransportSendQueueTests"` → 통과 6, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj` → 통과 10, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 10, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

## 2026-06-10 (Codex — Transport 송신 큐 close/drain release)

### 작업 단위
- `ITransport.TrySend`가 수락한 pending 송신 항목을 close 시 누수 없이 release 하는 최소 큐 골격을 구현했다.
- 실제 소켓 I/O, SAEA 백엔드, completion callback 은 넣지 않고 pending queue 와 close/drain 소유권만 별도 단위로 처리했다.

### Red
- `TransportSendQueueTests`에 `TransportBase` 타입 존재 테스트를 먼저 추가해 타입 부재 단언 실패를 확인했다.
- `default(TransportSendBuffer)`가 `TrySend`에서 예외 없이 pending 큐에 들어갈 수 있음을 `Assert.Throws` 실패로 확인했다.

### 구현
- `TransportBase`를 추가해 `ITransport.TrySend(IConnection, TransportSendBuffer)`의 공통 소유권 판정을 구현했다.
- `TransportConnection`을 내부 연결 상태로 추가하고 pending 송신 큐, close reject, close drain 을 구현했다.
- `Close()`는 closed 표시와 pending drain 을 같은 lock 안에서 처리해 close 이후 새 send 가 drain 을 빠져나가지 못하게 했다.
- 송신 펌프가 `TryDequeueSend`로 가져간 in-flight 항목은 close 가 release 하지 않도록 pending 과 분리했다.
- `TransportBase.TrySend`는 pending 큐에 넣기 전에 `TransportSendBuffer`가 live `RefCountedBuffer`를 가리키는지 확인한다.
  생성자를 거치지 않은 default 요청이나 이미 반환된 버퍼를 close drain 시점까지 지연시키지 않기 위한 방어다.
- 테스트 접근을 위해 `InternalsVisibleTo("Hps.Transport.Tests")`를 추가했다.
- `ITransport.StartAsync`/`StopAsync`는 기존 작업 중 반영된 기본 `CancellationToken` 인자를 유지했다.

### 테스트
- open 연결에서 `TrySend` 성공 후 publish 가드 ref 를 Release 해도 close 전까지 pool 이 반환되지 않고, close drain 에서 반환되는지 검증했다.
- closed 연결에서 `TrySend`가 false 를 반환하면 Transport 가 ref 소유권을 가져가지 않아 호출자가 Release 해야 함을 검증했다.
- `default(TransportSendBuffer)`가 pending 큐에 들어가지 않고 수락 경계에서 즉시 거부되는지 검증했다.
- `Close()`를 두 번 호출해도 pending 항목이 한 번만 Release 되는지 검증했다.
- 송신 펌프가 dequeue 한 in-flight 항목은 close 가 Release 하지 않고 펌프 완료 경로가 Release 해야 함을 검증했다.

### 상태 갱신
- `CURRENT_PLAN.md`를 Transport pending queue/close drain 구현 상태와 테스트 27개 통과 상태로 갱신했다.
- `TODOS.md`에서 이번 close/drain 작업을 Completed로 이동하고, 다음 리뷰 단위를 in-flight send completion Release 경로로 남겼다.
- `DECISIONS.md`에 pending 과 in-flight release 책임 분리를 D016으로 기록했다.

### 검증
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~TransportSendQueueTests"` → 통과 5, 실패 0, 건너뜀 0.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj` → 통과 9, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 9, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

## 2026-06-10 (Codex — Transport 버퍼 소유권 계약)

### 작업 단위
- Phase 2 진입을 위해 `Hps.Transport` public 계약의 첫 단위를 추가했다.
- 실제 소켓 I/O, SAEA 백엔드, 송신 큐/펌프 구현은 넣지 않고, raw `Memory<byte>` 대신 `RefCountedBuffer` 핸들을 받는 송신 소유권 경계만 고정했다.

### Red
- `tests/Hps.Transport.Tests`를 추가하고 reflection 기반 테스트로 `Hps.Transport.TransportSendBuffer` 타입 부재를 단언 실패로 확인했다.

### 구현
- `src/Hps.Transport` 프로젝트를 추가하고 `Hps.Buffers`를 참조하도록 했다.
- `TransportSendBuffer`를 `RefCountedBuffer + offset + length` 값 타입으로 추가했다.
- `TransportSendBuffer`는 `RefCountedBuffer.Length` 기준 payload 범위를 벗어난 offset/length 를 거부한다.
- 사용자 리뷰를 반영해 송신 시도와 소유권 판정을 `IConnection`이 아니라 `ITransport.TrySend(IConnection, TransportSendBuffer)`에 둔다.
- `IConnection`은 연결 핸들/수명 계약에 집중하도록 `Close()`/`Dispose()`만 노출한다.
- `ITransport`는 lifecycle 계약과 `TrySend` 계약만 우선 추가했고 listen/connect/accept 모델은 다음 구현 단위로 남겼다.

### 테스트
- `TransportSendBuffer`가 `RefCountedBuffer`와 payload range 를 그대로 노출하는지 검증했다.
- payload 범위 밖 송신 요청이 거부되는지 검증했다.
- 이미 풀에 반환된 `RefCountedBuffer`로 송신 요청을 만들면 거부되는지 검증했다.
- `ITransport.TrySend(IConnection, TransportSendBuffer)`가 존재하고 bool 을 반환하는지 검증했다.
- `IConnection` public 계약에 `TransportSendBuffer` parameter 가 없고, Transport public 계약에 raw `Memory<byte>`/
  `ReadOnlyMemory<byte>` parameter 가 없는지 검증했다.

### 상태 갱신
- `CURRENT_PLAN.md`를 Phase 2 초기 계약 상태와 테스트 22개 통과 상태로 갱신했다.
- `TODOS.md`에서 이번 계약 작업을 Completed로 이동하고, 다음 리뷰 단위를 `ITransport.TrySend` 송신 큐 close/drain release 계약 구현으로 남겼다.
- `DECISIONS.md`에 Transport 송신 계약의 소유권 경계를 D015로 기록했다.

### 검증
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --filter "FullyQualifiedName~TransportContractTests"` → 통과 4, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → `Hps.Buffers.Tests` 통과 18 + `Hps.Transport.Tests` 통과 4, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

## 2026-06-10 (Codex — RefCountedBuffer 동시 Release 스트레스 테스트)

### 작업 단위
- D013에 따라 `RefCountedBuffer` 동시 Release/팬아웃 스트레스 테스트만 별도 리뷰 단위로 보강했다.
- production code 수정 없이 기존 참조계수 구현이 동시 반환 계약을 만족하는지 테스트로 확인했다.

### 테스트
- 구독자 수 0, 1, 2, 4, 8, 32명 fan-out에서 publish 가드 ref와 구독자별 ref가 동시에 `Release()`되는 상황을 반복 검증했다.
- 64개 buffer가 동시에 in-flight 상태일 때 각 buffer의 여러 ref가 경쟁적으로 `Release()`되어도 종료 후 `RentedCount==0`으로 돌아오는지 검증했다.
- 새 테스트와 helper에는 무엇을 보호하는지 설명하는 한국어 주석을 남겼다.

### 상태 갱신
- `CURRENT_PLAN.md`에 테스트 18개 통과와 빌드 경고 0개 상태를 반영했다.
- `TODOS.md`에서 이번 스트레스 테스트 보강을 Completed로 이동하고, 다음 리뷰 단위를 Phase 2 `ITransport`/버퍼 소유권 계약 구체화로 남겼다.

### 검증
- `dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~RefCountedBufferTests"` → 통과 7, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → 통과 18, 실패 0, 건너뜀 0.
- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.

## 2026-06-10 (Codex — private helper 주석 보강)

### 작업 단위
- 사용자 검토 의견에 따라 `BipBuffer`와 `RefCountedBuffer`의 private helper 주석을 보강했다.
- 기능 변경 없이 helper가 감싼 volatile snapshot/publish 의미와 소유권/수명 경계를 설명하는 주석만 추가했다.

### 수정
- `BipBuffer` helper에 committed count, consumer cursor snapshot, producer cursor snapshot, watermark snapshot,
  producer/consumer cursor publish 의도를 설명했다.
- `RefCountedBuffer` helper에 payload length publish, ref count snapshot, live block snapshot, returned flag 의미를 설명했다.

### 상태 갱신
- `CURRENT_PLAN.md`에 private helper 주석 보강 상태와 검증 결과를 반영했다.
- `TODOS.md`에 이번 주석 보강을 Completed로 기록했고, 다음 리뷰 단위는 `RefCountedBuffer` 동시 Release/fan-out 스트레스 테스트로 유지했다.

### 검증
- `dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~BipBufferTests|FullyQualifiedName~RefCountedBufferTests"` → 통과 11, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → 통과 16, 실패 0, 건너뜀 0.

## 2026-06-10 (Codex — BipBuffer Volatile helper 리팩터링)

### 작업 단위
- 사용자 검토 의견에 따라 `BipBuffer` 내부 public 메서드 본문에 직접 보이던 `Volatile.Read/Write` 호출을 helper로 감쌌다.
- 기능 변경 없이 SPSC cursor/count 상태 관측 의미를 더 읽기 쉽게 만드는 리팩터링 단위로만 진행했다.

### 수정
- `ReadCommittedCountSnapshot`, `IsCommittedCountZero`, `ReadConsumerCursorSnapshot`, `ReadProducerCursorSnapshot`,
  `ReadWatermarkSnapshot`, `PublishProducerCursor`, `PublishConsumerCursor` helper를 추가했다.
- `Interlocked.Add(ref _count, ...)`는 생산자/소비자 간 commit/consume count 변경의 핵심이라 그대로 명시적으로 남겼다.
- `Volatile.Read/Write` 호출은 helper 영역으로 모았다.

### 상태 갱신
- `CURRENT_PLAN.md`에 BipBuffer helper 리팩터링 상태와 검증 결과를 반영했다.
- `TODOS.md`에 이번 리팩터링을 Completed로 기록했고, 다음 리뷰 단위는 `RefCountedBuffer` 동시 Release/fan-out 스트레스 테스트로 유지했다.

### 검증
- 리팩터링 전 `dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~BipBufferTests"` → 통과 6, 실패 0, 건너뜀 0.
- 리팩터링 후 `dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~BipBufferTests"` → 통과 6, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → 통과 16, 실패 0, 건너뜀 0.

## 2026-06-10 (Codex — RefCountedBuffer Volatile helper 리팩터링)

### 작업 단위
- 사용자 검토 의견에 따라 `RefCountedBuffer` 내부의 `Volatile.Read/Write` 호출을 읽기 쉬운 helper로 감쌌다.
- 기능 변경 없이 코드 읽기성을 개선하는 리팩터링 단위로만 진행했다.

### 수정
- `ReadPublishedLength`, `PublishLength`, `ReadRefCountSnapshot`, `ReadBlockSnapshot`, `IsReturned` helper를 추가했다.
- public API와 참조계수 알고리즘은 변경하지 않았다.
- `Interlocked.CompareExchange`/`Exchange`는 참조계수와 정확히-1회 반환 알고리즘의 핵심이므로 `AddRef`/`Release`/반환 경로에 명시적으로 남겼다.

### 상태 갱신
- `CURRENT_PLAN.md`에 helper 리팩터링 상태와 검증 결과를 반영했다.
- `TODOS.md`에 이번 리팩터링을 Completed로 기록했고, 다음 리뷰 단위는 `RefCountedBuffer` 동시 Release/fan-out 스트레스 테스트로 유지했다.

### 검증
- 리팩터링 전 `dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~RefCountedBufferTests"` → 통과 5, 실패 0, 건너뜀 0.
- 리팩터링 후 `dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~RefCountedBufferTests"` → 통과 5, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → 통과 16, 실패 0, 건너뜀 0.

## 2026-06-10 (Codex — RefCountedBuffer 최소 참조계수/반환 계약)

### 작업 단위
- D013에 따라 `RefCountedBuffer`의 최소 참조계수/반환 계약만 별도 리뷰 단위로 진행했다.
- 고동시성 fan-out/release 해머 테스트는 다음 보강 단위로 분리했다.

### Red
- `RefCountedBufferTests`를 먼저 추가했다.
- 컴파일 실패가 아니라 단언 실패가 되도록 임시 reflection helper를 사용했다.
- `PinnedBlockMemoryPool.RentCounted 메서드가 존재해야 한다.` 실패 5개로 Red를 확인했다.

### 구현
- `src/Hps.Buffers/RefCountedBuffer.cs`를 추가했다.
- `PinnedBlockMemoryPool.RentCounted()`를 추가해 기존 `Rent()`/`Return(byte[])` 경로를 재사용하도록 했다.
- `RefCountedBuffer`는 생성 ref=1로 시작하고, `AddRef()`/`Release()`를 Interlocked 기반으로 처리한다.
- 마지막 `Release()`가 0에 도달하면 내부 블록을 풀에 정확히 1회 반환한다.
- 이미 반환된 버퍼의 과다 `Release()`와 반환 후 `AddRef()` 부활을 계약 위반으로 거부한다.
- `Memory`/`Span`은 전체 블록을 노출하고, `Length`/`SetLength(int)`는 유효 payload 길이를 별도로 관리한다.

### 테스트
- counted buffer 대여 시 `RentedCount` 증가, `Memory`/`Span` 전체 블록 노출, `Length` 갱신, 마지막 `Release()` 반환.
- 균형 잡힌 `AddRef()`/`Release()`에서 마지막 Release 전에는 반환되지 않고 마지막 Release 에서만 반환.
- 과다 `Release()` 예외 및 풀 카운트 보존.
- 반환 후 `AddRef()` 부활 거부.
- `SetLength` 음수/용량 초과 거부 및 기존 길이 보존.
- Green 후 테스트를 직접 public API 호출 방식으로 리팩터링해 reflection helper를 제거했다.

### 상태 갱신
- `CURRENT_PLAN.md`에 RefCountedBuffer 최소 계약 구현과 테스트 16개 통과 상태를 반영했다.
- `TODOS.md`에서 최소 계약 구현을 Completed로 옮기고, 동시 Release/fan-out 스트레스 테스트를 다음 `P1_SOON` 항목으로 분리했다.

### 검증
- `dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~RefCountedBufferTests"` → 통과 5, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → 통과 16, 실패 0, 건너뜀 0.

## 2026-06-10 (Codex — PinnedBlockMemoryPool 테스트 직접 API 리팩터링)

### 작업 단위
- `PinnedBlockMemoryPoolTests`에서 production 타입을 reflection으로 호출하던 `PoolApi` nested class를 제거했다.
- `PinnedBlockMemoryPool`은 이미 public API가 존재하므로, 현재 테스트는 실제 호출 경로를 직접 검증하는 방식이 더 적합하다.
- production code 수정은 없었다.

### 수정
- `PoolApi.Create(...)` 호출을 `new PinnedBlockMemoryPool(...)`로 바꿨다.
- reflection 전용 `using System.Reflection`, `using System.Runtime.ExceptionServices`를 제거했다.
- `PoolApi` nested class를 삭제해 테스트가 타입/메서드 존재 여부가 아니라 실제 API 계약을 바로 검증하게 했다.

### 상태 갱신
- `CURRENT_PLAN.md`에 Pool 테스트가 직접 public API를 사용하도록 정리됐음을 반영했다.
- `TODOS.md`에 이번 리팩터링을 Completed로 기록했고, 다음 리뷰 단위는 `RefCountedBuffer` 최소 참조계수/반환 계약으로 유지했다.

### 검증
- `dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~PinnedBlockMemoryPoolTests"` → 통과 5, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → 통과 11, 실패 0, 건너뜀 0.

## 2026-06-10 (Codex — PinnedBlockMemoryPool 멀티스레드 스트레스 테스트)

### 작업 단위
- D013에 따라 `PinnedBlockMemoryPool` 멀티스레드 대여/반환 스트레스 테스트만 별도 리뷰 단위로 진행했다.
- production code 수정 없이 테스트 보강만 수행했다.

### 테스트
- 8개 worker를 동시에 시작해 각 10,000회 `Rent()`/`Return(byte[])`을 반복한다.
- 각 worker는 대여한 블록 길이가 `BlockSize`와 같은지 확인하고, 예외가 발생하면 테스트 스레드로 전달한다.
- 모든 worker 종료 후 `RentedCount==0`을 검증해 누수와 카운트 경합을 확인한다.

### 상태 갱신
- `CURRENT_PLAN.md`를 사용자 리뷰 대기 상태로 갱신했다.
- `TODOS.md`에서 Pool 멀티스레드 스트레스 테스트를 Completed로 옮기고,
  다음 리뷰 단위는 `RefCountedBuffer` 최소 참조계수/반환 계약으로 유지했다.

### 검증
- `dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~RentAndReturn_WhenCalledFromMultipleThreads_FinishesWithNoLeaks"` → 통과 1, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → 통과 11, 실패 0, 건너뜀 0.

## 2026-06-10 (Codex — PinnedBlockMemoryPool 최소 API)

### 작업 단위
- D013에 따라 `PinnedBlockMemoryPool` 최소 API와 단일스레드 계약 테스트만 별도 리뷰 단위로 진행했다.
- `RefCountedBuffer`와 Pool 멀티스레드 스트레스 테스트는 이번 단위에서 제외했다.

### Red
- `PinnedBlockMemoryPoolTests`를 먼저 추가했다.
- 타입이 아직 없어서 `Hps.Buffers.PinnedBlockMemoryPool, Hps.Buffers 타입이 존재해야 한다.` 단언 실패로 Red를 확인했다.

### 구현
- `src/Hps.Buffers/PinnedBlockMemoryPool.cs`를 추가했다.
- API: `PinnedBlockMemoryPool(int blockSize)`, `BlockSize`, `RentedCount`, `Rent()`, `Return(byte[])`.
- 새 블록은 `GC.AllocateUninitializedArray<byte>(BlockSize, pinned: true)`로 생성한다.
- 반납 블록 크기가 `BlockSize`와 다르면 `ArgumentException`으로 거부한다.
- `RentedCount`가 음수가 되지 않도록 Return 시 대여 카운트 가드를 둔다.

### 테스트
- block size와 `RentedCount` 추적.
- 반납 블록 재사용.
- 잘못된 크기 배열 반환 거부 및 count 보존.
- 0 이하 block size 거부.

### 상태 갱신
- `CURRENT_PLAN.md`를 사용자 리뷰 대기 상태로 갱신했다.
- `TODOS.md`에서 Pool 최소 API를 Completed로 옮기고, 멀티스레드 대여/반환 스트레스 테스트를 다음 `P1_SOON` 항목으로 분리했다.

### 검증
- `dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~PinnedBlockMemoryPoolTests"` → 통과 4, 실패 0, 건너뜀 0.
- `dotnet test HighPerformanceSocket.slnx` → 통과 10, 실패 0, 건너뜀 0.

## 2026-06-10 (Codex — 테스트 의도 주석 규칙 반영)

### 작업 단위
- 사용자 지시에 따라 테스트에도 무엇을 검증하는지 주석으로 남기는 규칙을 `AGENT_RULES.md`에 추가했다.
- 장기 결정으로 DECISIONS D014를 추가했다.
- `BipBufferTests.cs`의 각 테스트에 보호하는 불변식, 회귀 사례, 경계 조건, 동시성 가정을 설명하는 주석을 추가했다.

### 검증
- `dotnet test HighPerformanceSocket.slnx` → 통과 6, 실패 0, 건너뜀 0.

## 2026-06-10 (Codex — BipBuffer seeded fuzz 테스트)

### 작업 단위
- D013에 따라 `BipBuffer` seeded fuzz 테스트만 별도 리뷰 단위로 진행했다.
- 테스트는 capacity `2, 3, 4, 8, 17, 64`와 seed 4개 조합에서 20,000회 랜덤 write/read를 실행하고,
  단순 참조 큐와 바이트 순서 및 `Count`를 비교한다.
- 실패 시 최근 operation 로그를 메시지에 포함해 재현 조건을 바로 볼 수 있게 했다.

### Red 및 원인
- Red 확인: `capacity=3, seed=4660, iteration=17`에서 `GetReadSpan()`이 빈 span을 반환했지만
  참조 큐와 `buffer.Count`에는 1바이트가 남아 있었다.
- 추가 확인: 첫 수정 후 `capacity=4, seed=4660, iteration=6`에서도 같은 계열이 재현됐다.
- 원인: 버퍼가 비어 있고 `read == write > 0`인 상태에서 producer가 front로 wrap하면
  `watermark == read`인 0길이 상단 구간을 만들 수 있다. 이 경우 consumer는 아직 `read`를 0으로
  되돌릴 기회가 없어 front 데이터를 관측하지 못한다.

### 수정
- `GetWriteSpan()`에서 버퍼가 비어 있고 cursor가 non-zero 위치에서 만난 경우에는 `minimumSize`보다 작더라도
  tail을 먼저 반환하도록 했다.
- tail/front 비교는 실제 front 여유인 `read - 1` 기준으로 바꿨다.

### 상태 갱신
- `CURRENT_PLAN.md`를 사용자 리뷰 대기 상태로 갱신했다.
- `TODOS.md`에서 fuzz 테스트를 Completed로 옮기고, 다음 리뷰 단위는 `PinnedBlockMemoryPool`로 유지했다.

### 검증
- `dotnet test HighPerformanceSocket.slnx` → 통과 6, 실패 0, 건너뜀 0.

## 2026-06-10 (Codex — BipBuffer deterministic edge 테스트)

### 작업 단위
- D013에 따라 `BipBuffer` deterministic edge 테스트만 별도 리뷰 단위로 진행했다.
- 추가한 테스트:
  - `Capacity - 1` 실사용 용량과 full 상태 검증.
  - partial commit 후 커밋된 prefix만 읽히는지 검증.
  - tail이 `minimumSize`를 만족하지 못할 때 front wrap으로 전환되고 watermark 순서가 보존되는지 검증.
- production code 수정은 없었다.

### 상태 갱신
- `CURRENT_PLAN.md`를 사용자 리뷰 대기 상태로 갱신했다.
- `TODOS.md`에서 deterministic edge 테스트를 Completed로 옮기고, fuzz 테스트는 별도 `P1_SOON` 항목으로 남겼다.

### 검증
- `dotnet test HighPerformanceSocket.slnx` → 통과 5, 실패 0, 건너뜀 0.

## 2026-06-10 (Codex — BipBuffer M1/M2 최소 구현 + 리뷰 게이트 반영)

### 작업 단위 크기 규칙 추가
- 사용자 지시에 따라 구현을 작고 리뷰 가능한 기능 단위로 나누고, 한 단위 완료 후 사용자 리뷰 전에는
  다음 기능으로 자동 진행하지 않는 규칙을 `AGENT_RULES.md`에 추가했다.
- 장기 결정으로 DECISIONS D013을 추가했다.
- 후속 사용자 지시에 따라 각 기능 단위 완료 후 관련 파일만 stage 하여 단일 커밋으로 남기고,
  unrelated 변경은 커밋에 포함하지 않는 규칙을 D013과 `AGENT_RULES.md`에 보강했다.

### BipBuffer must-fix 2건 구현
- M1: capacity 끝까지 commit 후 read가 0으로 돌아온 빈 버퍼가 다시 쓰기 가능해야 함을 Red 테스트로 확인했다.
  `Commit()`에서 `_write == _capacity` 상태를 저장하지 않고 즉시 0으로 wrap하도록 수정했다.
- M2: SPSC 스트레스에서 `GetReadSpan()`이 커밋량보다 긴 span을 노출해 `Consume` 계약을 깨는 Red 테스트를 확인했다.
  반환 길이를 `_count` 기준으로 제한했고, `_count` 값 자체는 보정하지 않았다.
- 소비자는 데이터를 처리한 뒤에만 `Consume()`해야 한다는 SPSC 계약을 XML doc에 명시했다.

### 범위 조정
- 이번 사이클은 M1/M2만 리뷰 단위로 닫는다.
- `PLAN.md`가 요구하는 추가 edge/fuzz 테스트는 `TODOS.md`의 `Deferred Backlog`로 분리했다.
  사용자 리뷰 후 계속 진행 지시가 있으면 별도 Red 테스트 사이클로 처리한다.

### 검증
- `dotnet test HighPerformanceSocket.slnx` → 통과 2, 실패 0, 건너뜀 0.

## 2026-06-10 (마무리 — drop-oldest release + CURRENT_PLAN 최신화, Claude)

### D012 (drop-oldest evict release) 확정 — 실측 검증
- 외부 검토의 남은 minor 항목. drop-oldest는 이미 enqueue된 가장 오래된 항목을 능동 제거하므로 별도
  release 지점. evict한 RefCountedBuffer를 정확히 1회 Release, evict/dequeue/close를 단일 락으로 직렬화.
- 프로토타입 실측: 720만 enqueue(cap=16, 대량 eviction) + 동시 pump + close-drain → 누수 0·이중 반환 0.
- 반영: DECISIONS D012, `AGENTS.md §2-5`, `PLAN.md` Phase 3, `phase3-framing-and-close.md`, TODOS.

### CURRENT_PLAN.md 최신화
- 검토 6건·결정 D005~D012 종결을 반영. 다음 단일 작업은 여전히 Phase 1 BipBuffer M1·M2 3색 TDD.
- 테스트 discover 재확인: 0개(D003 기준 green 아님). 첫 Red 테스트로 해소 예정.

### 검증
- D012 프로토타입 실측 통과. 프로덕션 코드 미변경(Codex 구현 대기). 구현 전 설계 결정은 모두 종결.

## 2026-06-10 (설계 결정 — TCP 프레임 조립 + 종료 release, Claude)

### 외부 검토 Major×2 반영 → D010, D011 확정
- **D010 (TCP 프레임 조립)**: recv BipBuffer는 미파싱 스트림만 담고, 파서 상태머신이 헤더 4B 누적(분할 처리)
  → payload를 RefCountedBuffer로 누적 복사. recv 링이 프레임을 통째로 담을 필요 없음(payload > recv 링 허용),
  maxPayload 상한. **프로토타입 실측**: recv 링 64B < payload 300B, 청크 1~7B, 10만 프레임 무결성·누수 0.
- **D011 (연결 종료 release 계약)**: `Close()/Dispose()`는 송신 큐 pending·in-flight·조립중 RefCountedBuffer를
  모두 Release + 이후 enqueue 원자적 reject. 종료 후 `RentedCount==0`. (느린 소비자 끊기 시 누수 방지)
- 반영: `.claude/review/phase3-framing-and-close.md` 신규, DECISIONS D010·D011, `AGENTS.md §2-7`(프레임 조립)·
  신규 `§2-8`(종료 계약), `PLAN.md` Phase 2(종료 계약)·Phase 3(프레임 조립 + D010/D011 테스트), TODOS.
- 검증: D010 프로토타입 실측 통과. 프로덕션 코드 미변경(Codex 구현 대기).

## 2026-06-10 (설계 결정 — Publish payload 소유권, Claude)

### recv→팬아웃 payload 소유권 핸드오프 확정 (D009)
- 미해결 핵심이던 "파싱한 PUBLISH payload를 어떤 소유권으로 RefCountedBuffer 팬아웃에 넘길지"를 결정.
  - TCP: recv 링은 프레이밍 전용, payload는 RefCountedBuffer로 **1회 복사** 후 recv 즉시 Consume.
  - UDP: datagram을 RefCountedBuffer로 **직접 recv**(zero-copy).
  - 수명: publish 가드 ref → 구독자별 AddRef+enqueue(실패 시 즉시 Release) → publish 마지막 Release.
- 반영: `.claude/review/phase3-publish-ownership.md` 신규, DECISIONS D009, `AGENTS.md §2-1/§2-5` 복사 불변식
  문구 정정("구독자당/불필요한 복사 금지, TCP publish 1회 복사 허용"), `PLAN.md` Phase 3, TODOS RefCountedBuffer 항목.
- `RefCountedBuffer`에 `Span`/`Memory`/`Length`/`SetLength` 필요(복사 대상·송신 뷰)로 명시.

### 테스트 discover 상태 확인
- `tests/Hps.Buffers.Tests`에 테스트 `.cs`가 없어 `dotnet test`가 0개 discover. 회귀 아님(Phase 1 TDD 미착수).
  다음 P0(M1·M2 Red 테스트)가 들어가면 discover 시작. D003대로 0개 상태는 green 불인정.

### 검증
- 설계/문서 작업. 프로덕션 코드 미변경(Codex 구현 대기).

## 2026-06-10 (검토 사이클 — Claude)

### 설계 실측 검증 + 상태 파일 동기화
- 핵심 자료구조/설계를 임시 하니스로 실측 검증하고 결과를 `.claude/review/`에 기록(하니스는 검토 후 삭제).
  - `phase1-bipbuffer.md`: **M1**(단일스레드 deadlock)·**M2**(크로스스레드 over-read, SPSC 200만 바이트에서
    소비자가 미커밋 ~115만 바이트 과독·`Count` 음수) 재현. 두 수정 적용 시 단일·크로스스레드 통과.
    M2 문구를 "반환 길이 clamp(≠ `_count` 값 보정)"로 정확히 명시.
  - `phase1-refcounted-pool.md`: 팬아웃 5만 반복·동시 2만 버퍼에서 정확히-1회 반환·누수 0. 설계 승인.
  - `phase2-transport-bipbuffer.md`: 송신 다중생산자 위험(D1) → MPSC 큐→단일 펌프→SPSC. 버퍼 소유권은 풀 핸들(D2).
  - `phase3-broker-routing.md`: 빈 토픽 eager-cleanup이 동시 구독과 경합해 약 51% 유실(20만 회 실측).
    NoCleanup·set-lock은 0 유실. 영리한 lock-free verify-retry는 여전히 틀림(약 50% 유실).
- 위 결과로 상태 파일을 갱신: BipBuffer must-fix를 1건→2건으로, 신규 결정 DECISIONS D005~D008 추가,
  `CURRENT_PLAN.md`/`TODOS.md`의 미결 질문(버퍼 소유권 등)을 해소.

### 검증
- 검증은 임시 콘솔 하니스(`dotnet run`)로 수행. 프로덕션 코드/테스트는 아직 변경하지 않음(Codex 구현 대기).
- `BipBuffer.cs`는 여전히 초안(수정 전). 다음 P0는 M1·M2를 3색 TDD로 해소하는 것.

### 남은 불확실성
- M1·M2 수정과 회귀 테스트는 아직 코드에 반영되지 않음(P0_NOW).
- 라우팅 토픽 키 누적이 실제 문제되는 규모인지는 미정(필요 시에만 sweep).

## 2026-06-10

### 상태 관리 문서 초기화
- `PLAN.md`와 `AGENTS.md` 기준으로 작업 상태 관리 파일을 추가했다.
- 사용자 목표를 현재 작업 목표에 반영했다.
  - 4096 bytes 메시지.
  - 100 Hz.
  - 지연 누적 없이 처리.
- 현재 실행 지점을 Phase 1의 `BipBuffer` must-fix TDD 작업으로 정리했다.
- `.claude/review/phase1-bipbuffer.md`의 M1 deadlock 지적을 다음 작업의 P0 항목으로 연결했다.
- 현재 테스트 프로젝트에는 실제 테스트 `.cs` 파일이 없으므로 `dotnet test` 성공만으로 완료 판단하지 않도록 기록했다.

### 검증
- 문서 작성 작업이므로 빌드/테스트는 새로 실행하지 않았다.
- 이전 확인 기준으로 `dotnet test HighPerformanceSocket.slnx`는 테스트를 discover하지 못하는 상태였다.

### 남은 불확실성
- “딜레이 없이”의 정량 기준은 아직 확정되지 않았다.
- Phase 4에서 p50/p99 latency, 큐 적체, 동시 연결 수, 팬아웃 배율을 포함한 벤치마크 기준으로 구체화해야 한다.
