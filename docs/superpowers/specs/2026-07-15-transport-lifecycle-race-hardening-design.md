# Transport lifecycle 경합 hardening 설계

- 날짜: 2026-07-15
- 상태: 구현 완료 - 사용자 review stop
- 관련 결정: D011, D013, D241
- 목표: `BrokerServer`와 native transport에서 시작과 종료가 겹쳐도 종료 뒤 새 listener, connection, UDP endpoint 또는 pump가 살아남지 않게 한다.
- 구현 계획: `docs/superpowers/plans/2026-07-15-transport-lifecycle-race-hardening.md`

## 1. 문제와 확인된 범위

현재 `BrokerServer.StartTcpAsync`와 `StartUdpAsync`는 `_gate` 안에서 시작 flag를 먼저 설정한 뒤,
transport 시작과 listener/endpoint 생성을 비동기로 수행한다. 생성이 끝난 뒤 다시 `_gate`를 잡아 실제 resource를 게시한다.

이 사이에 `StopAsync`가 실행되면 다음 순서가 가능하다.

1. Start가 `_tcpStarted` 또는 `_udpStarted`를 `true`로 설정한다.
2. Start가 `ListenTcpAsync` 또는 `BindUdpAsync` 완료를 기다린다.
3. Stop이 아직 게시되지 않은 resource를 `null`로 관측하고 transport를 중지한다.
4. Start가 완료되어 이미 닫혔거나 transport 추적에서 빠진 resource를 server field에 게시한다.

RIO와 io_uring도 public method 시작 시점에만 `EnsureRunning()`을 호출한다. socket/native resource 생성 뒤의
`RegisterListener`, `RegisterConnection`, `RegisterUdpEndpoint`는 `_stopped`를 다시 확인하지 않고 목록에 추가한다.
따라서 Stop이 목록 snapshot을 뜬 뒤 새 resource가 등록되면 그 resource는 종료 snapshot에 포함되지 않는다.

SAEA의 `Register*`는 `_gate` 안에서 `EnsureRunningLocked()`를 호출하므로 같은 등록 경합을 이미 막고 있다.
이번 변경은 이 working pattern을 native backend와 server orchestration에 적용한다.

## 2. 대안 비교

### A. Server lifecycle 직렬화 + native 등록 시 종료 재검사 - 채택

- `BrokerServer`의 `StartTcpAsync`, `StartUdpAsync`, `StopAsync` 전체를 하나의 async lifecycle gate로 직렬화한다.
- `Dispose`는 `_disposed` 표식을 먼저 원자적으로 게시한 뒤 같은 직렬화된 Stop 경로를 사용한다.
- RIO/io_uring의 모든 `Register*`는 자신의 `_gate` 안에서 `_stopped`를 확인한 뒤에만 목록에 추가한다.
- RIO completion port의 snapshot/null 전환도 `_gate` 안으로 옮겨 생성과 종료를 직렬화한다.
- public API, backend 선택 정책, 데이터 hot path는 바꾸지 않는다.

server 사용자와 직접 `ITransport` 사용자를 모두 보호하면서 기존 책임 경계를 유지하는 가장 작은 완결된 변경이다.

### B. Server lifecycle만 직렬화 - 제외

`BrokerServer` 경로는 안전해지지만 직접 RIO/io_uring을 사용하는 sample, benchmark, library consumer는
`ListenTcpAsync`/`BindUdpAsync`/`ConnectTcpAsync`와 `StopAsync` 경합에 계속 노출된다.

### C. Native registration guard만 추가 - 제외

native resource는 종료 뒤 등록되지 않지만 `BrokerServer`는 SAEA에서도 Start 완료 resource를 Stop 뒤 다시 field에 게시할 수 있다.
또한 concurrent TCP/UDP start 중 한쪽이 transport start 완료 전에 다른 쪽 bind를 시작할 수 있다.

### D. lifecycle generation과 취소 전파 - 보류

Stop이 진행 중인 Start를 선점하도록 generation token과 내부 cancellation을 추가할 수 있다.
하지만 start 취소 결과, transport one-shot 상태, cleanup owner를 새로 정의해야 한다. 현재 목표에는 전체 operation 직렬화가 더 단순하다.

## 3. 선택한 구조

### 3.1 BrokerServer

`BrokerServer`는 control path 전용 `SemaphoreSlim(1, 1)` lifecycle gate를 소유한다.

- `StartTcpAsync`는 gate 획득부터 resource 게시 또는 실패 cleanup까지 독점한다.
- `StartUdpAsync`도 같은 gate를 사용한다.
- `StopAsync`는 gate를 얻은 뒤 현재 resource snapshot, close/dispose, accept loop drain, transport stop까지 마친다.
- `Dispose`는 기존 `_gate` 안에서 `_disposed = true`를 Stop보다 먼저 기록한 뒤 `StopAsync`를 호출한다.
  따라서 Dispose가 먼저 시작되면 후속 Start가 거부되고, Start가 먼저 시작되면 Dispose의 Stop이 기다렸다가 생성 resource를 닫는다.
- 기존 `_gate`는 field snapshot과 짧은 상태 변경을 보호하는 용도로 유지한다.
- publish, receive, fan-out hot path는 lifecycle gate를 사용하지 않는다.
- gate 대기 중 cancellation은 operation 진입 전 취소로 처리하며, gate를 획득한 뒤에는 기존 cancellation 전달 계약을 유지한다.

이 구조에서 Stop은 진행 중인 Start를 앞질러 가지 않는다. Start가 성공하면 Stop이 방금 생성된 resource를 포함해 정상 종료하고,
Start가 실패하면 cleanup을 끝낸 뒤 Stop이 no-op 또는 남아 있는 다른 ingress 종료로 수렴한다.

### 3.2 RIO

RIO는 `_stopped`를 one-shot 종료 권위값으로 유지한다.

- `RegisterListener`, `RegisterConnection`, `RegisterUdpEndpoint`는 `_gate` 안에서 `_stopped`를 확인한다.
- 종료가 시작된 뒤의 등록은 `InvalidOperationException`으로 거부한다.
- `GetOrCreateCompletionPort`도 같은 종료 검사를 사용한다.
- `StopAsync`는 listener/connection/endpoint와 completion port를 한 번의 `_gate` 임계구역에서 snapshot하고 field를 비운다.
- 등록 거부 시 호출자가 아직 소유한 socket/connection/endpoint를 기존 catch/finally 경계에서 정리한다.
- 특히 UDP endpoint constructor가 만든 RQ/CQ/registration은 등록 실패 시 endpoint dispose로 정리한다.

### 3.3 io_uring

io_uring도 `_stopped`를 기준으로 모든 `Register*`를 거부한다.

- `StopCore`가 `_stopped = true`와 resource snapshot을 수행한 뒤에는 새 목록 항목이 생기지 않는다.
- queue, completion loop, registry를 획득했지만 아직 등록하지 못한 resource는 등록 실패 catch/finally에서 닫는다.
- completion loop/queue의 기존 drain 순서와 registered payload owner 순서는 변경하지 않는다.

### 3.4 SAEA

SAEA의 `EnsureRunningLocked()` 등록 계약은 그대로 유지한다. 이번 작업은 회귀 테스트만 공유하고 production 변경은 하지 않는다.

## 4. lifecycle 불변식

1. `BrokerServer`의 start/stop operation은 동시에 두 개 이상 실행되지 않으며 Dispose는 종료 표식을 먼저 게시한다.
2. Stop이 resource snapshot을 시작한 뒤 어느 backend에도 새 tracked resource가 등록되지 않는다.
3. 등록이 거부된 resource는 caller local owner가 정확히 한 번 정리한다.
4. 종료 뒤 `LocalEndPoint`와 `UdpLocalEndPoint`는 `null`이며 listener/endpoint는 닫혀 있다.
5. transport stop은 한 server lifetime에서 실제 시작된 transport에 대해서만 수행한다.
6. receive/send/publish hot path에는 새 lock, semaphore, allocation을 추가하지 않는다.
7. transport의 one-shot stop/restart 의미와 default backend 선택은 바꾸지 않는다.

## 5. TDD 검증 설계

### Red 1: TCP Start와 Stop 직렬화

`BrokerServerTests`의 fake transport가 `ListenTcpAsync` 안에서 명시적 signal로 대기하게 한다.

1. `StartTcpAsync`를 시작하고 listen 진입을 확인한다.
2. `StopAsync`를 호출한다.
3. listen release 전에는 Stop task가 완료되지 않아야 한다.
4. listen을 release하고 Start와 Stop을 모두 기다린다.
5. 생성된 listener의 close/dispose와 transport stop이 각각 한 번이어야 한다.
6. server `LocalEndPoint`는 `null`이어야 한다.

현재 구현은 3번에서 Stop이 먼저 완료되므로 assertion Red가 발생해야 한다.

### Red 2: UDP Start와 Stop 직렬화

fake transport의 `BindUdpAsync`를 같은 방식으로 block한다.

- bind release 전 Stop은 완료되지 않는다.
- release 뒤 endpoint close/dispose와 transport stop은 각각 한 번이다.
- `UdpLocalEndPoint`는 `null`이다.

현재 구현은 아직 게시되지 않은 endpoint를 놓치므로 Red가 발생해야 한다.

### Red 3: RIO 종료 후 등록 거부

native availability와 무관하게 `RioTransport.StopAsync` 뒤 private `RegisterConnection`을 reflection으로 호출한다.
안전한 standalone `TransportConnection`을 사용하고, inner exception이 `InvalidOperationException`인지 확인한다.

현재 구현은 connection을 목록에 추가해 예외가 발생하지 않으므로 assertion Red가 된다.

### Red 4: io_uring 종료 후 등록 거부

`IoUringTransport.StopAsync` 뒤 같은 방식으로 `RegisterConnection`을 호출한다.
Linux syscall을 만들지 않는 pure lifecycle test로 두어 Windows 전체 test에서도 실행한다.

현재 구현은 예외 없이 등록하므로 assertion Red가 된다.

### Red 5: Dispose 중 transport Stop 실패 뒤 재시작 거부

fake transport의 Stop이 의도한 예외를 던지게 한 뒤 `BrokerServer.Dispose`를 호출하고 같은 server에서 Start를 다시 시도한다.
Dispose는 terminal operation이므로 Stop cleanup이 예외를 내도 후속 Start가 `ObjectDisposedException`으로 거부되어야 한다.

기존 구현은 `_disposed`를 Stop 뒤에 기록해 예외가 대입을 건너뛰므로 후속 Start가 성공하고 assertion Red가 된다.

### Green

- `BrokerServer` lifecycle method 세 개에 단일 async gate를 적용한다.
- `Dispose`는 `_disposed`를 기존 `_gate` 안에서 먼저 게시하고 직렬화된 Stop을 호출한다.
- RIO/io_uring `Register*`에 공통 locked stopped guard를 적용한다.
- RIO completion port snapshot을 transport lock 안으로 옮긴다.
- RIO UDP 등록 실패 cleanup을 endpoint owner까지 확장한다.
- Red가 요구하지 않는 state enum, generation token, 새 public API는 만들지 않는다.

### Refactor

- backend별 중복된 stopped 검사 메시지는 private locked helper 하나로 모은다.
- server의 기존 start/stop 본문은 가능한 한 유지하고 lifecycle gate wrapper만 명확히 분리한다.
- 리팩터 뒤 focused test를 다시 실행해 green을 유지한다.

## 6. 검증 범위

필수 focused gate:

- 신규 `BrokerServer` lifecycle tests.
- `Hps.Server.Tests` 전체.
- 신규 RIO registration test와 `Hps.Transport.Rio.Tests` 전체.
- 신규 io_uring registration test와 `Hps.Transport.IoUring.Tests` 전체.
- `Hps.Transport.Tests` 전체 SAEA 회귀.
- solution Release build와 전체 tests.

이 변경은 데이터 경로를 바꾸지 않으므로 30초 성능 benchmark는 구현 수락의 필수 gate가 아니다.
다만 build/test 뒤 기존 4096B x 100 Hz Windows SAEA load/open-loop를 TCP/UDP 각각 한 번 실행해
lifecycle 변경이 host 시작/종료와 correctness gate를 깨지 않았는지 확인한다.

## 7. 예상 변경 범위

- `src/Hps.Server/BrokerServer.cs`
- `src/Hps.Transport.Rio/RioTransport.cs`
- `src/Hps.Transport.IoUring/IoUringTransport.cs`
- `tests/Hps.Server.Tests/BrokerServerTests.cs`
- `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`
- `tests/Hps.Transport.IoUring.Tests/IoUringTransportTcpTests.cs`
- 관련 state/decision/changelog 문서

public transport abstraction, Protocol, Broker routing, buffer ownership, benchmark schema는 변경하지 않는다.

## 8. 구현 결과

- 계획된 server/native Red 4개와 Dispose stop-failure Red 1개를 production 변경 전에 예상 assertion failure로 확인했다.
- `BrokerServer` lifecycle gate, Dispose 종료 표식 선게시, RIO/io_uring locked stopped guard를 구현했다.
- RIO completion port snapshot/null 전환과 UDP 등록 실패 endpoint cleanup을 보강했다.
- Server 40/40, RIO 57/57, io_uring 89/89, SAEA Transport 44/44와 solution 525/525가 통과했다.
- Release build는 경고 0/오류 0이다.
- SAEA TCP/UDP 4096B x 100 Hz load/open-loop는 모두 3000/3000이며 drop, payload error, pool rented는 0이다.

## 9. 실패 모드와 대응

### lifecycle gate가 Start 실패 cleanup과 교착

- 탐지: 신규 Start 실패 회귀 또는 Stop task timeout.
- 대응: gate를 operation wrapper가 소유하고 기존 cleanup은 같은 operation 안에서 완료한다. cleanup에서 public Stop을 재호출하지 않는다.

### native 등록 거부 뒤 resource 누수

- 탐지: endpoint/connection close count, pool rented count, native registration diagnostics 회귀.
- 대응: 등록 전까지 resource는 local variable이 소유하고 성공한 뒤에만 transport 목록으로 ownership을 이전한다.

### Stop cancellation로 일부 resource만 정리

- 기존 Stop 계약을 이번 작업에서 변경하지 않는다. cancellation은 진입과 backend 전달에 사용하되,
  이미 snapshot한 managed resource cleanup 순서는 유지한다. 별도 cancellation 정책 변경은 범위 밖이다.

## 10. 범위 밖

- transport restart 지원.
- lifecycle public state enum 또는 event.
- generation 기반 Start 선점 취소.
- connection별 start/stop 병렬화.
- hot-path allocation 제거.
- 현재 HEAD io_uring 성능 workflow 실행.
- `BipBuffer` reservation 검증 수정.
- RIO IPv6와 default backend 승격.

## 11. 구현 handoff 기준

- 다섯 Red는 production 변경 전에 각각 예상 assertion failure로 확인한다.
- server gate와 native stopped guard는 별도 Red/Green 순서로 적용한다.
- 신규 test를 통과시키기 위한 최소 변경 뒤 focused suite, solution suite 순으로 확장한다.
- 예상 파일 밖 변경이 필요하면 구현을 멈추고 설계를 재평가한다.
- 구현, 검증, 상태 문서 갱신은 하나의 reviewable commit으로 끝내고 다음 finding을 자동으로 섞지 않는다.
