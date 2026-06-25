# Windows RIO backend boundary 설계

## 상태

Accepted.

## 목적

Phase 5의 목표는 기존 `ITransport` 계약 뒤에 Windows Registered I/O(RIO) backend 를 추가하고,
Phase 2/3 통합 테스트와 Phase 4 benchmark 로 SAEA 기준선 대비 동작과 성능을 비교할 수 있게 만드는 것이다.

이번 설계는 RIO P/Invoke 구현을 바로 시작하지 않고, 먼저 책임 경계와 수명 규칙을 확정한다.
RIO는 request queue, completion queue, registered buffer 수명과 스레드 접근 규칙이 SAEA보다 명시적이므로,
경계를 닫지 않고 구현을 시작하면 buffer release, socket close, queue notification 경합에서 결함이 생기기 쉽다.

## 현재 코드 기준

- `TransportFactory.CreateDefault()`는 항상 `SaeaTransport`를 반환한다.
- `src/Hps.Transport.Rio/` 프로젝트는 아직 없다.
- 상위 계층은 `ITransport`, `IConnection`, `IUdpEndpoint`만 알고 backend concrete type 은 모른다.
- `TransportBase`는 send ownership, drop counter, endpoint high-watermark diagnostics 를 공통으로 제공한다.
- `TransportConnection`은 pending send queue 와 in-flight send ref release 계약을 이미 갖고 있다.
- SAEA 기준선은 raw socket send/receive loop 로 TCP/UDP broker path 를 검증한다.

## Microsoft RIO 문서에서 반영할 제약

Microsoft Learn 기준으로 RIO request queue 는 기존 Winsock socket 에 completion queue 를 연결해 만들며,
`RIOSend`/`RIOReceive`를 사용하려면 먼저 `RIOCreateRequestQueue`가 필요하다.

`RIORegisterBuffer`는 버퍼를 등록하고, 등록된 동안 가상 메모리 page 가 physical memory 에 lock 된다.
따라서 등록 단위가 너무 작거나 너무 많으면 physical memory footprint 와 등록 overhead 가 커진다.
등록 해제는 outstanding send/receive request 가 없는 상태에서만 안전하게 다룬다.

RIO completion/request queue 접근은 성능상 동기화 primitive 로 보호되지 않는다.
같은 queue 또는 같은 socket request queue 를 여러 thread 가 접근하면 외부 직렬화가 필요하다.

completion notification 은 `RIOCreateCompletionQueue`의 notification 설정과 `RIONotify`로 관리한다.
IOCP completion 을 쓰면 `GetQueuedCompletionStatus` 계열로 notification 을 받고,
이후 `RIODequeueCompletion`으로 완료 결과를 drain 한다.
`RIODequeueCompletion`으로 completion 을 꺼낸 뒤에야 시스템이 해당 request 의 buffer/registration association 과 quota charge 를 해제한다.

## 접근 대안

### 선택안 A - TCP-only RIO backend skeleton 먼저

처음에는 TCP listen/connect/accept/send/receive 만 RIO backend 로 붙이고 UDP는 SAEA fallback 또는 unsupported 로 둔다.

장점:
- Phase 2/3 TCP broker 통합 테스트를 가장 먼저 재사용할 수 있다.
- RIO request/completion queue, registered receive block, send completion release 규칙을 작은 범위에서 검증한다.
- UDP `RIOSendEx`/`RIOReceiveEx` endpoint/remote handling 을 나중으로 미뤄 초기 위험을 낮춘다.

단점:
- Phase 5 첫 구현만으로는 UDP RIO path 를 검증하지 못한다.
- `TransportFactory`가 TCP 가능 여부와 UDP 가능 여부를 함께 판단해야 하는 경우 설계가 복잡해질 수 있다.

### 선택안 B - TCP/UDP 동시 RIO backend

TCP와 UDP를 한 번에 RIO backend 로 설계하고 구현한다.

장점:
- 최종 Interface Server 목표와 가장 직접적으로 맞는다.
- backend 선택 정책이 단순해 보인다.

단점:
- RIO queue, buffer registration, remote endpoint, datagram ownership 경합을 동시에 다뤄야 한다.
- 실패 원인 분리가 어렵고, 첫 구현 단위가 너무 커진다.

### 선택안 C - capability probe 와 native wrapper 만 먼저

처음에는 `TransportFactory` 선택 없이 RIO function table load/probe 와 wrapper 테스트만 만든다.

장점:
- P/Invoke signature 와 OS capability 확인을 가장 작은 단위로 검증한다.
- Windows가 아닌 환경에서 skip/fallback 정책을 먼저 고정할 수 있다.

단점:
- `ITransport` 동작 가치가 바로 나오지 않는다.
- wrapper만 오래 남으면 실제 backend 구현과 괴리될 수 있다.

## 결정

선택안 A를 기본으로 하되, 구현 계획은 C의 probe/wrapper 를 첫 task 로 분리한다.

즉, Phase 5는 다음 순서로 진행한다.

1. RIO project skeleton 과 Windows-only capability probe/native function table wrapper.
2. RIO buffer registration owner.
3. TCP request/completion queue owner.
4. TCP connect/listen/accept wiring.
5. TCP receive/send pump 와 `TransportConnection` ownership 연결.
6. 기존 TCP transport/server loopback tests 재사용.
7. SAEA vs RIO Phase 4 benchmark 비교.

UDP RIO는 TCP backend 가 안정화된 뒤 별도 설계/구현 단위로 둔다.
`ITransport.BindUdpAsync`와 `TrySendTo`는 RIO backend 첫 TCP 단위에서 구현하지 않는다.
상위 server 가 TCP와 UDP를 동시에 필요로 하는 경우, v1 RIO backend 선택은 TCP-only host 또는 명시 opt-in 경로로 제한하고,
기본 `TransportFactory.CreateDefault()`는 모든 필수 기능이 안정화될 때까지 SAEA를 유지한다.

## 책임 경계

### `Hps.Transport.Rio`

새 project 는 `Hps.Transport`와 `Hps.Buffers`에만 의존한다.
Broker/Protocol/Server 를 참조하지 않는다.

예상 internal 구성:

- `RioTransport`: `TransportBase`를 상속하는 backend root.
- `RioNative`: `WSAIoctl(SIO_GET_MULTIPLE_EXTENSION_FUNCTION_POINTER)`와 RIO function table P/Invoke.
- `RioCapabilityProbe`: 현재 OS/socket provider 에서 RIO function table 을 얻을 수 있는지 검사.
- `RioRegisteredBufferPool`: `PinnedBlockMemoryPool` block 을 RIO buffer id 로 등록하고, outstanding request 가 0일 때만 해제한다.
- `RioCompletionQueue`: IOCP notification, `RIONotify`, `RIODequeueCompletion` drain 을 소유한다.
- `RioRequestQueue`: socket 별 `RIO_RQ`와 outstanding receive/send count 를 소유한다.
- `RioConnection`: RIO socket/resource 와 `TransportConnection`을 연결하는 adapter.

### `Hps.Transport`

기존 abstraction 은 유지한다.
RIO 때문에 public `ITransport`/`IConnection`을 넓히지 않는다.

`TransportFactory`는 후속 구현에서 다음 순서로 확장한다.

1. 기본값은 계속 SAEA.
2. 명시 opt-in factory 또는 internal test factory 로 RIO를 먼저 노출.
3. TCP/UDP feature parity 와 테스트가 갖춰진 뒤 OS capability 기반 default 선택을 검토.

## Buffer registration 정책

RIO backend 는 `PinnedBlockMemoryPool`을 계속 사용한다.
다만 RIO는 registered buffer id 가 필요하므로, pool block 을 빌릴 때마다 등록하지 않고 backend 시작 시 또는 pool expansion 시 등록하는 owner 를 둔다.

초기 구현은 고정 개수 block 을 등록하는 단순 모델로 시작한다.
동적 pool expansion, registration cache eviction, block compaction 은 Phase 7 튜닝 전까지 보류한다.

등록 해제 순서:

1. transport stop/connection close 요청.
2. 새 receive/send posting 중지.
3. outstanding RIO completion drain.
4. in-flight `TransportSendBuffer` release.
5. RIO buffer deregistration.
6. pinned block pool return/dispose.

이 순서를 어기면 completion dequeue 전에 buffer association 이 살아 있다는 RIO 계약과 충돌할 수 있다.

## Queue/threading 정책

첫 구현은 queue ownership 을 단순하게 유지한다.

- completion queue 는 backend worker 한 곳에서만 drain 한다.
- 같은 request queue 에 대한 `RIOSend`/`RIOReceive` posting 은 connection-local gate 또는 single worker queue 로 직렬화한다.
- `TransportConnection` pending queue 는 기존 MPSC entry point 로 유지하되, RIO posting 은 단일 send pump 에서 수행한다.
- notification 은 IOCP 기반으로 설계하되, 최초 smoke 구현에서 event notification 이 더 작으면 별도 결정으로 바꿀 수 있다.

## 테스트 정책

RIO tests 는 Windows 전용이다.
Windows가 아니거나 RIO function table probe 에 실패하면 RIO-specific tests 는 skip 한다.
SAEA tests 는 계속 모든 환경의 기본 회귀 기준이다.

재사용할 test 범위:

- `tests/Hps.Transport.Tests`의 TCP loopback contract.
- `tests/Hps.Server.Tests`의 TCP broker loopback.
- stable subscriber identity TCP loopback.
- send queue drop/leak ownership tests 중 backend-agnostic 범위.

초기 RIO 전용 Red는 compile failure 가 아니라 assertion failure 여야 한다.
예: `RioCapabilityProbe`가 현재 OS에서 `Unsupported` 또는 `Available` 상태를 명시적으로 반환하고,
`TransportFactory` 기본값은 여전히 `SaeaTransport`임을 확인한다.

## Deferred

- UDP RIO endpoint/datagram path.
- `RIOSendEx`/`RIOReceiveEx` remote address handling.
- `RIO_MSG_DEFER` batching.
- multi completion queue sharding.
- automatic default backend selection.
- dynamic registered buffer pool expansion.
- RIO benchmark gate 승격.

## 검증

이번 설계 문서 자체의 검증은 다음으로 한다.

- 현재 `TransportFactory`, `TransportBase`, `TransportConnection`, `SaeaTransport` 구조와 대조.
- Microsoft Learn RIO 문서의 request queue, completion queue, buffer registration, notification, dequeue 수명 규칙 대조.
- placeholder scan.
- `git diff --check`.
- solution build/test.

## 참고 문서

- Microsoft Learn: `RIOCreateRequestQueue` - https://learn.microsoft.com/en-us/windows/win32/api/mswsock/nc-mswsock-lpfn_riocreaterequestqueue
- Microsoft Learn: `RIORegisterBuffer` - https://learn.microsoft.com/en-us/windows/win32/api/mswsock/nc-mswsock-lpfn_rioregisterbuffer
- Microsoft Learn: `RIOCreateCompletionQueue` - https://learn.microsoft.com/en-us/windows/win32/api/mswsock/nc-mswsock-lpfn_riocreatecompletionqueue
- Microsoft Learn: `RIONotify` - https://learn.microsoft.com/en-us/windows/win32/api/mswsock/nc-mswsock-lpfn_rionotify
- Microsoft Learn: `RIODequeueCompletion` - https://learn.microsoft.com/en-us/windows/win32/api/mswsock/nc-mswsock-lpfn_riodequeuecompletion
- Microsoft Learn: `RIOSend` - https://learn.microsoft.com/en-us/windows/win32/api/mswsock/nc-mswsock-lpfn_riosend
- Microsoft Learn: `RIOReceive` - https://learn.microsoft.com/en-us/windows/win32/api/mswsock/nc-mswsock-lpfn_rioreceive
