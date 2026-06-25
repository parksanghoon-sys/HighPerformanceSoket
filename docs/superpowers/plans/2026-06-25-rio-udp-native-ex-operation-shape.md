# RIO UDP Task 1 native Ex operation shape 구현 계획

## Goal

D109의 첫 구현 단위로 `RioNative`에 UDP datagram operation 에 필요한 `RIOReceiveEx`/`RIOSendEx` wrapper shape 를 추가한다.
이번 단위는 UDP endpoint 를 만들지 않고 native capability 와 marshalling 경계만 닫는다.

## Boundaries

포함:

- `RioNative` internal datagram capability property.
- `RIOReceiveEx`/`RIOSendEx` delegate binding.
- nullable `RIO_BUF` pointer marshalling helper.
- capability/argument validation tests.

제외:

- `RioTransport.BindUdpAsync(...)` 구현.
- `RioUdpEndpoint`.
- UDP loopback receive/send.
- sockaddr encode/decode helper.
- `TransportFactory.CreateDefault()` 변경.

## Current code facts

- `RioExtensionFunctionTable`에는 `ReceiveEx`/`SendEx` pointer field 가 이미 있다.
- `HasRequiredPointers()`는 TCP pump 필수 pointer 만 확인하고 Ex pointer 는 제외한다.
- `RioNative` constructor 는 TCP `_receive`/`_send` delegate 만 바인딩한다.
- 기존 TCP operation wrapper 는 `RioBufferSegment[]`를 pinned array 로 넘긴다.
- `RioCapabilityProbeTests.ReceiveSendOperations_WhenMissing_AreDetectedBeforePump`가 TCP wrapper argument validation 을 이미 검증한다.

## Design decision for this task

`RioCapabilityProbe.GetStatus()`는 그대로 둔다.
TCP RIO availability 와 UDP datagram operation availability 를 분리하기 위해 `RioNative`에 internal property 를 추가한다.

예상 shape:

```csharp
internal bool SupportsDatagramOperations
{
    get { return _functionTable.ReceiveEx != IntPtr.Zero && _functionTable.SendEx != IntPtr.Zero; }
}
```

wrapper 예상 shape:

```csharp
internal bool ReceiveEx(
    IntPtr requestQueue,
    RioBufferSegment? data,
    RioBufferSegment? localAddress,
    RioBufferSegment? remoteAddress,
    IntPtr requestContext)

internal bool SendEx(
    IntPtr requestQueue,
    RioBufferSegment? data,
    RioBufferSegment? remoteAddress,
    IntPtr requestContext)
```

초기 wrapper 는 control context, pFlags, RIO flags 를 모두 null/0 으로 고정한다.
batching 과 ancillary data 는 UDP endpoint parity 이후 별도 단위다.

## Task 1. Red tests

파일: `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`

추가 테스트 1:

```csharp
[Fact]
public void TryLoadFunctionTable_WhenRioAvailable_ExposesDatagramOperationCapability()
```

의도:

- Windows + RIO available 에서 `RioNative`가 datagram operation capability 를 노출하는지 확인한다.
- reflection 으로 `SupportsDatagramOperations` property 를 찾는다.
- 현재 구현에는 property 가 없으므로 `Assert.NotNull(property)`로 실패한다.
- 실패는 compile failure 가 아니라 assertion failure 여야 한다.

추가 테스트 2:

```csharp
[Fact]
public void ReceiveSendExOperations_WhenRequestQueueIsNull_ThrowArgumentException()
```

의도:

- Green 이후 direct internal API 로 전환한다.
- `SupportsDatagramOperations`가 false 면 return 한다.
- `ReceiveEx(IntPtr.Zero, data: null, localAddress: null, remoteAddress: null, requestContext: IntPtr.Zero)`와
  `SendEx(IntPtr.Zero, data: null, remoteAddress: null, requestContext: IntPtr.Zero)`가 `ArgumentException`을 던져야 한다.
- wrapper 가 아직 없으면 Red 단계에서는 reflection shape test 만 먼저 둔다. Green 뒤 이 테스트를 direct API 로 추가한다.

Red command:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~DatagramOperation|FullyQualifiedName~ReceiveSendEx"
```

Expected Red:

- 첫 테스트가 `Assert.NotNull(property)`에서 실패한다.

## Task 2. Green implementation

파일: `src/Hps.Transport.Rio/RioNative.cs`

변경:

1. field 추가:

```csharp
private readonly RioPostBufferExDelegate? _receiveEx;
private readonly RioPostBufferExDelegate? _sendEx;
```

2. constructor 에서 pointer 가 non-zero 일 때만 delegate binding:

```csharp
if (functionTable.ReceiveEx != IntPtr.Zero)
    _receiveEx = Marshal.GetDelegateForFunctionPointer<RioPostBufferExDelegate>(functionTable.ReceiveEx);
```

3. property 추가:

```csharp
internal bool SupportsDatagramOperations
{
    get { return _receiveEx != null && _sendEx != null; }
}
```

4. delegate shape 추가:

```csharp
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
private delegate int RioPostBufferExDelegate(
    IntPtr requestQueue,
    IntPtr data,
    uint dataBufferCount,
    IntPtr localAddress,
    IntPtr remoteAddress,
    IntPtr controlContext,
    IntPtr flagsBuffer,
    uint flags,
    IntPtr requestContext);
```

5. wrapper 추가:

```csharp
internal bool ReceiveEx(...)
internal bool SendEx(...)
```

6. helper 추가:

```csharp
private static IntPtr PinOptionalSegment(RioBufferSegment? segment, out GCHandle handle)
```

또는 더 단순하게 각 optional segment 를 1개짜리 `RioBufferSegment[]`로 만들어 pinned pointer 를 넘긴다.
핫패스 최적화는 endpoint 구현 때 재평가하고, 이번 단위는 shape/validation 안전성을 우선한다.

validation:

- request queue null 은 `ArgumentException`.
- operation delegate 가 없으면 `NotSupportedException`.
- `SendEx`는 data 와 remoteAddress 가 둘 다 null 이어도 wrapper shape 상 허용한다.
  실제 endpoint 구현에서 payload/remote endpoint 정책을 더 좁힌다.

Green command:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~DatagramOperation|FullyQualifiedName~ReceiveSendEx"
```

## Task 3. Refactor and verification

Refactor:

- TCP `PostBuffers(...)`와 Ex optional segment pinning helper 의 중복을 과하게 합치지 않는다.
  Ex는 null pointer 조합이 있어 별도 helper 가 더 읽기 쉽다.
- comments 는 Korean 으로 남긴다.
  특히 Ex pointer 가 TCP availability 에 포함되지 않는 이유와 UDP endpoint 이전에 wrapper만 여는 이유를 설명한다.

Verification:

```powershell
dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore
dotnet build HighPerformanceSocket.slnx --no-restore
dotnet test HighPerformanceSocket.slnx --no-build
git diff --check
```

## Commit plan

커밋 1개:

```text
feat: add rio datagram native operation shape
```

포함 파일:

- `src/Hps.Transport.Rio/RioNative.cs`
- `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`
- root state docs

## Risks

- `ReceiveEx`/`SendEx` pointer 가 일부 provider 에서 null 일 수 있다.
  TCP RIO availability 와 UDP operation capability 를 분리해 fallback 판단을 흐리지 않는다.
- optional `RIO_BUF` marshalling 에서 pinned handle release 누락이 생기면 testhost memory pinning 이 남는다.
  helper 는 try/finally 로 handle 을 항상 free 한다.
- live UDP operation 을 이번 단위에 넣으면 scope 가 endpoint owner 로 커진다.
  이번 단위는 pointer/capability/wrapper validation 까지만 닫는다.
