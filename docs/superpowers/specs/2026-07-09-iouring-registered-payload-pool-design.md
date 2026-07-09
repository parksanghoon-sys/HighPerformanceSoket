# io_uring Registered Payload Pool 설계

## 목적

D224 원격 Linux contract gate 로 fixed send registry lifetime owner 와 opt-in `WRITE_FIXED` helper shape 는 검증됐다.
하지만 production fan-out payload 는 publish 시점에 동적으로 생기는 `RefCountedBuffer`이므로,
현재 구조에서는 TCP connection resource 생성 시점에 fixed table 에 등록할 payload block 목록을 알 수 없다.

이 문서는 production TCP payload `WRITE_FIXED` 연결 전에 필요한 registered payload block source 를 설계한다.
목표는 send hot path 에서 `RegisterBuffers`/`UnregisterBuffers`를 호출하지 않고, 이미 등록된 block 의 fixed index lookup 만으로
payload 를 전송할 수 있는 구조를 만드는 것이다.

## 현재 구조 요약

- TCP ingress 는 `TcpFrameAssembler`가 `PinnedBlockMemoryPool.RentCounted()`로 frame payload block 을 대여한다.
- UDP ingress 는 `IoUringUdpEndpoint.IoUringUdpReceiveSlot`이 endpoint receive pool 에서 `RefCountedBuffer`를 대여한다.
- Broker fan-out 은 같은 `RefCountedBuffer`에 구독자 수만큼 `AddRef()` 후 `TransportSendBuffer` slice 를 큐에 넣는다.
- `TransportConnection`은 pending/in-flight/drop/close 경로에서 해당 ref 를 정확히 한 번 `Release()`한다.
- `RefCountedBuffer`는 마지막 `Release()`에서 자신의 `_pool.Return(block)`으로만 반환된다.
- `IoUringFixedSendBufferRegistry`는 backing `byte[]` identity 를 fixed buffer index 로 조회할 수 있지만,
  등록 대상 block 목록은 외부에서 미리 제공되어야 한다.

## 설계상 문제

io_uring fixed buffer table 은 queue/resource lifetime 동안 안정적으로 유지되어야 한다.
하지만 현재 `PinnedBlockMemoryPool`은 lazy allocation/cache 구조이고, 어떤 block 이 나중에 publish payload 로 쓰일지
미리 알려주지 않는다.

따라서 단순히 `SendInFlightAsync(...)`에서 현재 payload block 을 보고 fixed table 에 넣는 방식은 제외한다.
그 방식은 send hot path 에 native registration churn 을 만들고, D210의 remote timeout 실패 패턴으로 되돌아간다.

## 검토한 접근

### 접근 A: send마다 현재 payload block 등록

가장 작게 붙일 수 있지만 제외한다.
hot path 에 `RegisterBuffers`/`UnregisterBuffers`가 들어가고, active send 중 queue fixed table 을 교체할 수 있다.
D210에서 이미 유사한 방향이 remote timeout 으로 실패했다.

### 접근 B: connection registry 에 최근 payload block cache 추가

부분 hit 는 가능하지만 제외한다.
payload block 은 message 단위로 바뀌므로 hit rate 가 불명확하고, cache eviction 시점의 native table 교체가 active `WRITE_FIXED`
completion 과 충돌하지 않는다는 별도 protocol 이 필요하다.

### 접근 C: queue-scoped registered payload pool 도입

권장안이다.
io_uring queue/resource lifetime 에 묶인 fixed payload block pool 을 만들고, 첫 범위에서는 TCP assembler 가 이 pool 에서
payload block 을 대여하게 한다. pool 이 소유한 backing arrays 는 queue 시작 시 fixed table 에 등록되고, send hot path 는
해당 array 의 fixed index 만 조회한다. UDP receive path 는 별도 설계 전까지 이 pool 에 연결하지 않는다.

## 권장 아키텍처

### 1. Buffer 반환 owner 경계 일반화

현재 `RefCountedBuffer`는 `PinnedBlockMemoryPool`에 직접 반환된다.
registered pool 이 reusable slot 을 안전하게 회수하려면 마지막 `Release()` 시 “어느 owner 로 block 을 돌려줄지”를 추상화해야 한다.

권장 shape:

```csharp
public interface IRefCountedBufferOwner
{
    int BlockSize { get; }

    void Return(byte[] block);
}
```

- `PinnedBlockMemoryPool`은 이 interface 를 구현한다.
- `RefCountedBuffer`는 concrete pool 대신 owner interface 를 보관한다.
- 기존 public 사용자는 계속 `PinnedBlockMemoryPool.RentCounted()`를 사용하므로 source compatibility 를 유지한다.
- `RefCountedBuffer` public API 는 바꾸지 않는다.

이 변경은 `Hps.Buffers` 계층의 구조 변경이므로 첫 구현 task 로 따로 검증한다.

### 2. Buffer source 경계

`TcpFrameAssembler`는 지금 `PinnedBlockMemoryPool.RentCounted()`를 직접 호출한다.
registered pool exhaustion 시 baseline fallback 을 명시적으로 선택하려면 assembler 가 concrete pool 대신 source 경계를 받아야 한다.

권장 shape:

```csharp
public interface IRefCountedBufferSource
{
    int BlockSize { get; }

    RefCountedBuffer RentCounted();
}
```

- `PinnedBlockMemoryPool`은 이 source interface 도 구현한다.
- `IoUringRegisteredPayloadBlockPool`은 `TryRentCounted(out RefCountedBuffer? buffer)` 같은 non-throwing internal API 를 갖는다.
- io_uring TCP path 는 registered pool 과 기존 fallback pool 을 묶는 composite source 를 assembler 에 주입한다.
- composite source 는 registered slot 이 있을 때 registered block 을 반환하고, 없을 때만 명시적 fallback pool 에서 대여한다.
- fallback 은 source 구성 단계에서 드러나므로 registered pool 내부의 hidden allocation fallback 을 만들지 않는다.

### 3. io_uring registered payload pool

새 internal owner 를 `Hps.Transport.IoUring`에 둔다.

권장 shape:

```text
IoUringRegisteredPayloadBlockPool
  - owns N pinned byte[] blocks
  - registers all blocks once through IoUringRegisteredBufferSet
  - implements IRefCountedBufferOwner
  - TryRentCounted(out RefCountedBuffer? buffer) returns a RefCountedBuffer backed by one registered block
  - Return(byte[]) marks the slot reusable
  - TryGetBufferIndex(byte[], out int) maps backing array to fixed index
  - Dispose() unregisters buffers after all owned blocks are no longer rented
```

slot 재사용 정책:

- pool capacity 는 fixed table capacity 와 같다.
- `RentCounted()`는 free slot 이 없으면 false/nullable 또는 explicit failure 를 반환하는 shape 로 시작한다.
- hidden allocation fallback 은 금지한다. fallback 이 필요하면 호출 계층이 기존 `PinnedBlockMemoryPool`을 명시적으로 선택해야 한다.
- 마지막 `Release()`에서 owner `Return(byte[])`가 호출되면 slot 을 다시 free queue 에 넣는다.

### 4. Protocol ingress 연결

TCP는 이미 D009/D010에 따라 receive chunk 에서 payload block 으로 1회 복사한다.
이 복사는 registered payload block 으로 흡수할 수 있다.

권장 변경:

- `TcpFrameAssembler`가 concrete `PinnedBlockMemoryPool` 대신 `IRefCountedBufferSource`를 받게 한다.
- 기존 생성자는 유지하고 내부에서 adapter 를 사용한다.
- io_uring TCP receive handler 구성 시 registered payload source 와 fallback source 를 합성해 주입할 수 있게 한다.

초기 구현에서는 SAEA/RIO 기본 경로를 변경하지 않는다.
io_uring opt-in path 에서만 registered payload pool 을 사용한다.

### 5. UDP ingress 연결

UDP는 현재 receive slot 이 endpoint receive pool 에서 datagram buffer 를 직접 대여해 kernel receive target 으로 쓴다.
이 path 는 원칙적으로 zero-copy receive 에 가깝다.

초기 범위에서는 UDP를 production fixed send 등록 대상에서 제외한다.
이유:

- UDP receive buffers 는 endpoint receive window 와 직접 결합되어 있다.
- fan-out send 완료 전 datagram buffer 를 receive slot 으로 되돌리면 use-after-free 위험이 있다.
- receive slot depth, publish fan-out ref, fixed send table slot 을 한 번에 묶으면 범위가 커진다.

따라서 첫 registered payload pool 구현은 TCP publish payload 에만 적용한다.
UDP는 별도 설계에서 receive slot 과 registered send pool 의 관계를 다룬다.

### 6. Send path 연결

`IoUringFixedSendBufferRegistry`는 registered payload pool 의 `TryGetBufferIndex(byte[])` 또는 equivalent mapping 을 사용한다.
기본 연결 순서는 다음과 같다.

```text
TCP receive chunk
  -> TcpFrameAssembler copies payload into registered payload block
  -> BrokerPublisher fan-out keeps same RefCountedBuffer
  -> TransportConnection pending/in-flight owns subscriber ref
  -> IoUringTransport.SendInFlightAsync
       length prefix: baseline SendArrayAsync
       payload: Try fixed registered payload helper
       miss: baseline SendArrayAsync fallback
```

초기에는 payload miss fallback 을 유지한다.
이렇게 해야 mixed path 를 안전하게 검증할 수 있고, registered pool exhaustion 이 곧바로 message loss 로 이어지지 않는다.

## Capacity 정책

초기 capacity 는 보수적으로 고정한다.

- registered payload block size: 기존 TCP max payload block size 와 동일
- registered payload slot count: 16 또는 32
- send queue capacity 16과 동일하게 시작하는 것을 권장한다.

근거:

- connection 당 pending send capacity 가 16이다.
- fixed table slot 을 send queue 보다 작게 잡으면 정상 fan-out 중 registry miss 가 과도하게 증가한다.
- 너무 크게 잡으면 queue resource 생성 비용과 kernel fixed table footprint 가 증가한다.

capacity 는 public API 로 노출하지 않고 internal option/test seam 으로 시작한다.

## Failure 정책

- registered pool slot 이 없으면 TCP assembler 는 fallback pool 을 사용할 수 있다.
- fallback pool payload 는 registry miss 가 나므로 `SendArrayAsync` baseline 으로 전송된다.
- fallback 발생은 처음에는 metric/log 로 올리지 않는다. 대신 tests 에서 miss fallback 을 명시적으로 검증한다.
- production fixed-write hard gate 또는 zero-copy 목표를 주장하려면 후속 benchmark artifact 에서 hit/miss count 를 관측해야 한다.

## 테스트 전략

### Task 1: Buffer owner abstraction

- `PinnedBlockMemoryPool`이 기존 `RentCounted()`/`Return()` 동작을 유지하는지 검증한다.
- `RefCountedBuffer.Release()`가 owner interface 로 정확히 한 번 반환하는지 test owner 로 검증한다.

### Task 2: Registered payload pool pure contract

- fixed capacity 만큼 block 을 대여할 수 있다.
- 마지막 release 후 같은 slot 이 다시 대여 가능하다.
- live block 은 release 전까지 재대여되지 않는다.
- backing array 는 stable fixed index 로 조회된다.

### Task 3: Native registration owner

- Linux capability available 환경에서 pool 생성 시 모든 slot 이 `IoUringRegisteredBufferSet.Register(...)`로 등록된다.
- dispose 시 unregister 된다.
- Windows/local unavailable 환경은 capability guard 로 early return 한다.

### Task 4: TCP assembler integration

- `TcpFrameAssembler`가 injected source 에서 payload block 을 대여한다.
- 기존 `PinnedBlockMemoryPool` 생성자와 behavior 는 유지된다.
- registered source 로 완성한 frame 이 broker fan-out 에서 같은 `RefCountedBuffer`로 공유된다.

### Task 5: io_uring TCP send opt-in integration

- registered payload block 으로 들어온 publish payload 는 `SendFixedRegisteredPayloadAsync(...)` path 를 탄다.
- length prefix 는 baseline send 를 유지한다.
- registry miss 는 baseline fallback 으로 전송된다.
- local/Windows tests 는 shape/ownership 을 검증하고, Linux remote contract 가 native path 를 검증한다.

## 제외 범위

- UDP registered send pool 연결
- default backend promotion
- zero-copy 달성 주장
- latency hard gate 승격
- public QoS/backpressure policy 확장
- per-send native registration

## 성공 기준

이 설계가 구현되면 다음을 주장할 수 있다.

- TCP publish payload 를 registered block source 에 담을 수 있다.
- io_uring TCP send hot path 는 hit case 에서 native registration 없이 fixed index lookup 으로 payload `WRITE_FIXED`를 제출할 수 있다.
- miss case 는 baseline send 로 안전하게 fallback 한다.

아직 주장하지 않는 것:

- 모든 payload 가 zero-copy 로 전송된다.
- UDP publish 도 fixed send path 를 탄다.
- 4096B x 100Hz latency 목표가 fixed-write 로 개선됐다.
- io_uring backend 를 default 로 승격할 수 있다.
