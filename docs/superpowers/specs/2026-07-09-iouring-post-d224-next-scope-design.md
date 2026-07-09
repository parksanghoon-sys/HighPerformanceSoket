# D224 이후 io_uring fixed send 후속 범위 설계

## 목적

D224 원격 Linux contract gate 는 fixed send registry lifetime owner 와 opt-in helper shape 가 Linux에서 깨지지 않는다는
근거를 제공했다. 그러나 이 결과를 곧바로 production TCP payload `WRITE_FIXED` default 연결로 확장하면 안 된다.

이번 문서는 D224 이후 다음 작업 후보를 실제 코드 구조와 대조해 재평가하고, 다음 구현 전에 닫아야 할 설계 gap 을 명확히 한다.

## 확인된 사실

- `IoUringFixedSendBufferRegistry`는 이미 알고 있는 `TransportSendBuffer[]`에서 backing `byte[]`를 골라 fixed buffer table 에 등록한다.
- `IoUringTcpConnectionResource`는 optional registry owner 를 보관할 수 있다.
- `IoUringTransport.SendFixedRegisteredPayloadAsync(...)` helper 는 registry lookup 이 성공할 때 `TrySubmitWriteFixed(...)`를 사용할 수 있다.
- 기본 `SendInFlightAsync(...)`는 아직 `SendArrayAsync(...)`/`TrySubmitSend(...)` baseline path 를 유지한다.
- `BrokerPublisher`는 publish 시점에 `RefCountedBuffer`를 구독자별 `TransportSendBuffer`로 넘긴다.
- `RefCountedBuffer` backing block 은 `PinnedBlockMemoryPool.RentCounted()`가 동적으로 대여한다.
- 현재 `PinnedBlockMemoryPool`은 lazy allocation/cache 구조이며, transport queue 에 등록할 전체 block 목록을 사전에 제공하지 않는다.

## 핵심 gap

현재 registry factory 는 “이미 존재하는 send buffer 목록”을 받아 fixed table 을 만든다.
하지만 production TCP connection resource 는 connection 생성 시점에 앞으로 fan-out 될 payload block 을 알 수 없다.

따라서 다음 코드는 아직 안전하지 않다.

```text
SendInFlightAsync
  -> resource.FixedSendBufferRegistry.TryGetSlot(current dynamic sendBuffer)
  -> SendFixedRegisteredPayloadAsync
```

이 path 는 대부분 registry miss 가 나거나, registry 를 send마다 다시 만들려는 압력을 만든다.
후자는 D210의 실패 패턴인 active send 중 `RegisterBuffers`/`UnregisterBuffers` churn 으로 되돌아간다.

## 선택지

### A. send마다 payload block 을 등록한다

장점:
- 변경 범위가 가장 작다.
- 현재 `TransportSendBuffer`만으로 구현 가능하다.

단점:
- D210에서 이미 remote timeout/cancelled 로 실패한 방향과 같은 계열이다.
- `IoUringRegisteredBufferSet.Dispose()`가 queue 전체 fixed table 을 unregister 하므로 active send 와 충돌할 수 있다.
- hot path 에 native register/unregister 가 들어간다.

판단: 제외한다.

### B. connection-scoped fixed send registry 에 최근 payload block 을 캐시한다

장점:
- helper/registry owner 를 그대로 확장할 수 있다.
- 일부 재사용 payload 에서 fixed-write hit 가 가능하다.

단점:
- publish payload block 은 message 별로 동적으로 바뀌므로 hit rate 가 불명확하다.
- miss 처리와 eviction 이 active `WRITE_FIXED` completion 과 충돌하지 않도록 별도 pin/ref/lifetime protocol 이 필요하다.
- eviction 순간 native fixed table 을 교체하면 D210 계열 risk 가 다시 생긴다.

판단: 지금 바로 구현하지 않는다. cache/eviction protocol 설계가 먼저 필요하다.

### C. queue-scoped registered payload block pool 을 별도로 둔다

장점:
- fixed table lifetime 을 queue/resource lifetime 에 묶을 수 있다.
- send hot path 는 fixed index lookup 만 수행할 수 있다.
- active send 중 native registration table 을 교체하지 않는다.

단점:
- 현재 Broker/Protocol 이 쓰는 `PinnedBlockMemoryPool`과 transport queue-scoped registered pool 의 관계를 새로 설계해야 한다.
- publish payload 를 registered pool block 에 직접 담지 못하면 추가 복사가 생긴다.
- TCP frame assembler, UDP datagram receive, Broker fan-out ownership 경계를 함께 검토해야 한다.

판단: production fixed-write default 연결 전 가장 타당한 방향이지만, 구현 범위가 크므로 별도 설계/계획 단위로 승격한다.

## 권장 방향

D224 이후 바로 production TCP payload `WRITE_FIXED`를 default 로 연결하지 않는다.

다음 단위는 **queue-scoped registered payload block source 설계**로 둔다.
목표는 “어떤 buffer 가 fixed table 에 안정적으로 등록되어 있고, publish payload 가 그 buffer 에 어떻게 들어오며,
fan-out 중 ref/lifetime 이 어떻게 유지되는가”를 명확히 하는 것이다.

특히 다음 질문을 먼저 닫아야 한다.

- registered payload pool 을 transport 계층이 소유할지, broker/protocol ingress 쪽 pool 을 backend-aware 로 바꿀지
- TCP publish 의 기존 1회 복사를 registered block 으로 흡수할 수 있는지
- UDP publish 의 zero-copy 원칙과 registered pool 이 충돌하는지
- fixed table capacity 와 send queue capacity 16의 관계를 어떻게 둘지
- registry miss fallback 을 계속 허용할지, opt-in mode 에서만 hard fail 할지
- remote Linux contract 에서 어떤 end-to-end evidence 를 production 연결 근거로 삼을지

## 다음 작업 제안

다음 작업은 구현이 아니라 설계 문서 작성이다.

- 문서: `docs/superpowers/specs/2026-07-09-iouring-registered-payload-pool-design.md`
- 범위:
  - `PinnedBlockMemoryPool`
  - `RefCountedBuffer`
  - TCP frame assembler publish copy boundary
  - UDP datagram receive ownership
  - `BrokerPublisher`
  - `IoUringFixedSendBufferRegistry`
  - `IoUringTransport.SendInFlightAsync`
- 산출물:
  - registered payload pool ownership model
  - fixed table lifetime model
  - fan-out ref/lifetime model
  - fallback/miss policy
  - TDD implementation slice 후보

## 검토 필요 지점

이 선택은 payload ownership 과 zero-copy 목표에 직접 영향을 준다.
따라서 구현 전에 사용자가 설계 방향을 검토해야 한다.

현재 권장안은 C안, 즉 queue-scoped registered payload block source 를 별도 설계로 승격하는 것이다.
