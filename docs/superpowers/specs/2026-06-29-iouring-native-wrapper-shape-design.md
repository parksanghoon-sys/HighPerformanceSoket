# Linux io_uring native wrapper shape 설계

## 배경

Phase 6 첫 boundary 는 D133에 따라 `Hps.Transport.IoUring` project, capability probe,
`IoUringTransport` lifecycle shell, non-Linux unsupported boundary 까지만 완료했다.
다음 단계는 실제 TCP/UDP pump 로 바로 가지 않고, Linux native syscall 과 mmap 자원 수명을 안전하게 감싸는
internal wrapper shape 를 먼저 확정하는 것이다.

현재 검증 환경은 Windows 이므로 Linux integration 을 당장 green gate 로 삼을 수 없다. 따라서 이번 설계의 목표는
Windows 에서도 컴파일/계약 테스트가 가능한 managed boundary 를 만들고, Linux 에서는 작고 명시적인 probe 로
`io_uring_setup` 가능 여부를 확인할 수 있는 구조를 잡는 것이다.

## 목표

- `ITransport` public 계약을 넓히지 않는다.
- `IoUringTransport`가 raw syscall, mmap pointer, fd close 순서를 직접 알지 않게 한다.
- `io_uring_setup` fd, SQ/CQ ring mmap, SQE mmap, fixed buffer registration 수명을 각각 명확한 owner 로 분리한다.
- non-Linux 또는 syscall unavailable 환경에서는 예외 escape 대신 `IoUringCapabilityStatus.Unavailable` 또는
  명시적 `NotSupportedException`으로 수렴한다.
- TCP/UDP pump, zero-copy send, Linux loopback integration 은 후속 구현 단위로 분리한다.

## 비목표

- 이번 단위에서 `IORING_OP_SEND`, `IORING_OP_RECV`, `SENDMSG`, `RECVMSG`, `SEND_ZC`를 구현하지 않는다.
- 이번 단위에서 `TransportFactory.CreateDefault()` 또는 host selector 를 변경하지 않는다.
- 이번 단위에서 외부 `liburing` NuGet/native dependency 를 추가하지 않는다.
- 이번 단위에서 Linux benchmark, default backend promotion, RIO/SAEA 공통 selector package 를 열지 않는다.

## 확인한 기존 패턴

- RIO는 `RioNative`가 function table, socket 생성, buffer registration, CQ/RQ operation 을 숨긴다.
- `RioTransport`는 public `ITransport` lifecycle 과 connection/endpoint owner 를 잡고,
  raw native handle 조작은 `RioNative`, `RioCompletionPort`, `RioPayloadRegistrationCache` 같은 내부 타입에 위임한다.
- RIO capability probe 는 OS guard 후 native loader 를 시도하고, unavailable 을 process failure 로 만들지 않는다.
- io_uring도 같은 계층 규칙을 따른다. 단, RIO function table 과 달리 io_uring은 syscall fd 와 mmap 영역이 핵심 자원이므로
  fd owner 와 mmap owner 를 명시적으로 나눠야 한다.

## 대안

### 대안 A — `IoUringTransport`에서 P/Invoke를 직접 호출

장점은 파일 수가 적고 처음 구현 속도가 빠르다는 점이다.
하지만 transport lifecycle, native ABI, mmap pointer, fixed buffer registration, TCP/UDP pump 가 한 파일에 섞인다.
이 구조는 close/drain 버그를 만들기 쉽고, RIO에서 이미 피했던 coupling 을 반복한다.

판정: 채택하지 않는다.

### 대안 B — internal native adapter + ring owner + registration owner 분리

`IoUringNative`는 syscall/mmap/munmap/close 호출과 ABI struct 만 가진다.
`IoUringQueue`는 setup fd 와 SQ/CQ/SQE mmap 수명을 소유한다.
`IoUringRegisteredBufferSet`은 fixed buffer registration 과 deregistration 수명을 소유한다.
`IoUringTransport`는 이 owner 들을 Start/Stop 또는 connection/endpoint resource 에 붙일 뿐 raw pointer 를 알지 않는다.

장점은 책임 경계가 RIO와 일관되고, Windows에서도 non-Linux guard/shape tests 를 작성할 수 있다는 점이다.
단점은 파일 수가 늘고 첫 구현에서 native pump 가 아직 나오지 않는다는 점이다.

판정: 채택한다.

### 대안 C — `liburing` wrapper dependency 도입

native ABI drift 를 직접 관리하지 않아도 되는 장점이 있다.
하지만 새 외부 의존성은 AGENTS 규칙상 사전 합의가 필요하고, 배포/라이선스/런타임 native artifact 관리 비용이 생긴다.
또한 이 프로젝트의 목적은 backend boundary 자체를 검증하는 것이므로 첫 native wrapper 에서는 과한 선택이다.

판정: 채택하지 않는다.

## 결정

D134로 다음을 채택한다.

1. io_uring native 경계는 `IoUringNative`, `IoUringQueue`, `IoUringRegisteredBufferSet`으로 나눈다.
2. `IoUringNative`는 internal static adapter 이며 public API 로 노출하지 않는다.
3. `IoUringQueue`는 `SafeHandle` 기반 fd owner 와 mmap region owner 를 포함하는 disposable root 다.
4. `IoUringRegisteredBufferSet`은 ring fd 에 묶인 fixed buffer registration owner 다.
5. 첫 구현은 capability probe 가 실제 Linux 에서 작은 ring setup/close 를 시도할 수 있는 수준까지만 다룬다.
6. TCP/UDP pump 는 queue owner 가 green 된 뒤 별도 설계/구현으로 진행한다.

## 제안 파일 구조

```text
src/Hps.Transport.IoUring/
  IoUringNative.cs                  syscall, mmap, munmap, close, ABI struct
  IoUringSafeHandle.cs              io_uring fd close owner
  IoUringMemoryMap.cs               mmap/munmap owner
  IoUringQueue.cs                   setup 결과 fd + SQ/CQ/SQE mmap owner
  IoUringRegisteredBufferSet.cs     fixed buffer register/deregister owner
  IoUringCapabilityProbe.cs         기존 probe 를 native setup probe 로 확장

tests/Hps.Transport.IoUring.Tests/
  IoUringNativeShapeTests.cs        non-Linux guard, internal type/method shape
  IoUringQueueTests.cs              non-Linux unsupported, Linux setup/Dispose smoke
  IoUringRegisteredBufferSetTests.cs fixed buffer registration owner boundary
```

## 책임 경계

### `IoUringNative`

- OS/architecture guard 를 포함한다.
- `io_uring_setup`, `io_uring_enter`, `io_uring_register` syscall wrapper 를 제공한다.
- `mmap`, `munmap`, `close` wrapper 를 제공한다.
- native 실패는 `errno`를 포함한 managed result 또는 `SocketException`/`IOException`으로 변환한다.
- pointer lifetime 을 소유하지 않는다. raw syscall 호출만 수행한다.

### `IoUringSafeHandle`

- fd close 를 정확히 한 번 수행한다.
- `SafeHandle` 기반으로 finalizer fallback 을 제공하되, 정상 경로에서는 owner `Dispose`에서 닫힌다.
- fd 값이 음수이면 invalid handle 로 본다.

### `IoUringMemoryMap`

- `mmap` pointer 와 length 를 소유한다.
- `Dispose`에서 `munmap`을 정확히 한 번 호출한다.
- pointer 를 외부에 줄 때는 `IntPtr` 또는 internal span factory 로만 제한한다.

### `IoUringQueue`

- `IoUringNative.Setup(entries)` 결과 fd 와 params 를 받아 SQ ring, CQ ring, SQE 영역을 mmap 한다.
- setup 중간 실패 시 이미 열린 fd/mmap 을 역순으로 정리한다.
- 첫 구현에서는 submit/complete pump 를 제공하지 않고, setup/Dispose/probe 경계만 제공한다.
- 후속 pump 구현이 필요한 SQ tail/head, CQ head/tail, SQE pointer 계산은 이 타입 안쪽에 둔다.

### `IoUringRegisteredBufferSet`

- fixed buffer registration 은 ring fd 단위 owner 로 둔다.
- source buffer 는 `PinnedBlockMemoryPool`에서 온 고정 배열이어야 한다.
- registration owner 는 byte[] pool ownership 을 갖지 않고, kernel registration 수명만 소유한다.
- Dispose 는 `IORING_UNREGISTER_BUFFERS` 또는 equivalent deregistration 을 수행한다.

## Capability probe 정책

`IoUringCapabilityProbe.GetStatus()`는 다음 순서로 동작한다.

1. non-Linux: `UnsupportedOperatingSystem`.
2. unsupported architecture: `Unavailable`.
3. Linux: 작은 entries 값으로 `IoUringQueue.TryCreateForProbe(...)`를 호출한다.
4. setup 성공: queue 를 즉시 dispose 하고 `Available`.
5. `ENOSYS`, `EINVAL`, `EPERM`, `EACCES`, `ENOMEM`, seccomp 차단 등 setup 실패: `Unavailable`.

probe 는 process failure 를 만들지 않는다. 실제 backend 선택자는 explicit io_uring 선택 시 unavailable 을 명시 오류로 보여주고,
auto/fallback 정책은 후속 host/composition 설계에서 판단한다.

## 오류 처리

- argument validation 실패는 `ArgumentException` 계열로 즉시 드러낸다.
- OS/architecture 미지원은 `NotSupportedException` 또는 capability `UnsupportedOperatingSystem`으로 수렴한다.
- native syscall 실패는 probe 에서는 `Unavailable`로 흡수하고, explicit queue 생성 API 에서는 errno 를 포함한 예외로 드러낸다.
- setup 중간 실패는 열린 자원을 역순 dispose 한다.
- Dispose 는 idempotent 해야 한다.

## 테스트 전략

- Windows/non-Linux 에서는 reflection 기반 shape test 와 unsupported boundary test 를 수행한다.
- Linux 에서는 `IoUringCapabilityProbe.GetStatus()`가 exception 없이 `Available` 또는 `Unavailable`을 반환해야 한다.
- Linux 에서 `Available`이면 `IoUringQueue.Create(entries)`가 fd/mmap owner 를 만들고 Dispose 로 닫을 수 있어야 한다.
- fixed buffer registration test 는 capability `Available`일 때만 실행한다.
- 모든 새 테스트에는 무엇을 검증하는지 한국어 주석을 남긴다.

## 후속 구현 순서

1. `IoUringNative` ABI shape 와 non-Linux guard.
2. `IoUringQueue` setup/Dispose owner.
3. `IoUringCapabilityProbe`를 real setup probe 로 확장.
4. `IoUringRegisteredBufferSet` fixed buffer registration owner.
5. 이후 TCP-first pump 설계로 이동한다.

## 범위 밖으로 남길 항목

- SQE submit/complete pump.
- TCP accept/connect/listen 구현.
- UDP datagram pump.
- `SEND_ZC` 또는 `MSG_ZEROCOPY`.
- host selector 또는 default backend promotion.
- Linux benchmark artifact.

## Self-review

- Placeholder scan: 미완성 표식이나 임시 작성 문구는 없다.
- Consistency: D133의 skeleton 이후 native wrapper 분리 기준과 일치한다.
- Scope: syscall wrapper shape 와 owner boundary 만 다루고 TCP/UDP pump 로 확장하지 않는다.
- Ambiguity: unavailable/error 정책, owner 책임, 테스트 환경별 기대값을 명시했다.
