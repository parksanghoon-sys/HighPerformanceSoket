# io_uring UDP Pump Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `Hps.Transport.IoUring`에 UDP bind/receive/send pump 를 추가해 opt-in Linux io_uring backend 가 UDP datagram 경로를 갖게 한다.

**Architecture:** v1은 IPv4 one-deep `recvmsg`/`sendmsg` pump 로 제한한다. `IoUringTransport`의 shared queue/completion loop 를 재사용하고, `IoUringUdpEndpoint`가 UDP socket, pinned message header/iovec/sockaddr scratch, receive/send operation context, pending send queue 를 소유한다. fixed registration cache, zero-copy send, receive window 확장, default promotion 은 후속으로 둔다.

**Tech Stack:** .NET 9.0, C# 8.0, xUnit, Linux `io_uring_enter`, `IORING_OP_RECVMSG`, `IORING_OP_SENDMSG`, `PinnedBlockMemoryPool`, `RefCountedBuffer`.

## Global Constraints

- TFM: `net9.0`, LangVersion: `8.0`.
- 코드 식별자를 제외한 문서와 주석은 한국어로 작성한다.
- public API 변경 없이 기존 `ITransport`, `IUdpEndpoint`, `ITransportDatagramHandler` 계약을 재사용한다.
- 모든 새 테스트에는 무엇을 검증하는지 한국어 주석을 작성한다.
- production 구현은 실패 테스트가 먼저 있어야 한다.
- UDP v1은 IPv4 `IPEndPoint`만 직접 지원하고 IPv6 direct io_uring UDP 는 후속 범위로 둔다.
- fixed payload registration cache, zero-copy send, default backend promotion 은 이번 계획에 포함하지 않는다.

---

## File Structure

- Modify: `src/Hps.Transport.IoUring/IoUringNative.cs`
  - `IORING_OP_RECVMSG`, `IORING_OP_SENDMSG`, Linux `msghdr` shape 를 추가한다.
- Modify: `src/Hps.Transport.IoUring/IoUringQueue.cs`
  - message header pointer 를 SQE address 로 받는 `TrySubmitReceiveMessage`, `TrySubmitSendMessage`를 추가한다.
- Modify: `src/Hps.Transport.IoUring/IoUringOperationKind.cs`
  - UDP receive/send operation kind 를 TCP와 구분한다.
- Create: `src/Hps.Transport.IoUring/IoUringSockaddr.cs`
  - IPv4 sockaddr encode/decode helper 를 제공한다.
- Create: `src/Hps.Transport.IoUring/IoUringUdpMessageBuffer.cs`
  - pinned `msghdr`/`iovec`/sockaddr scratch lifetime 을 operation completion 까지 유지한다.
- Create: `src/Hps.Transport.IoUring/IoUringUdpEndpoint.cs`
  - UDP endpoint lifecycle, receive pool, pending send queue, diagnostics snapshot 을 소유한다.
- Modify: `src/Hps.Transport.IoUring/IoUringTransport.cs`
  - UDP bind, receive loop, send loop, endpoint registration, close notification 을 연결한다.
- Create/Modify tests under `tests/Hps.Transport.IoUring.Tests/`
  - native shape, endpoint shape, Linux-gated receive/send loopback, ownership cleanup 을 검증한다.
- Modify state docs:
  - `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`,
    `docs/agent-state/changelog/2026-06.md`, `docs/agent-state/decisions/2026-06.md`.

---

### Task 1: Native UDP Message Shape

**Files:**
- Modify: `src/Hps.Transport.IoUring/IoUringNative.cs`
- Modify: `src/Hps.Transport.IoUring/IoUringQueue.cs`
- Create: `src/Hps.Transport.IoUring/IoUringSockaddr.cs`
- Modify: `src/Hps.Transport.IoUring/IoUringOperationKind.cs`
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringUdpMessageShapeTests.cs`

**Interfaces:**
- Produces: `IoUringNative.OperationReceiveMessage`, `IoUringNative.OperationSendMessage`
- Produces: `IoUringMessageHeader`
- Produces: `IoUringQueue.TrySubmitReceiveMessage(int fileDescriptor, IntPtr messageHeader, ulong token)`
- Produces: `IoUringQueue.TrySubmitSendMessage(int fileDescriptor, IntPtr messageHeader, ulong token)`
- Produces: `IoUringSockaddr.EncodeIPv4(IPEndPoint endPoint, byte[] block)` and `IoUringSockaddr.DecodeIPv4(byte[] block, int length)`
- Consumes: existing `IoUringQueue.TryAcquireSubmissionEntry`, `IoUringNative.Enter`

- [x] **Step 1: Write the failing test**

Create `tests/Hps.Transport.IoUring.Tests/IoUringUdpMessageShapeTests.cs`.

```csharp
using System;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringUdpMessageShapeTests
    {
        // UDP는 remote endpoint 를 주고받아야 하므로 TCP RECV/SEND opcode 만으로는 부족하다.
        // 이 테스트는 recvmsg/sendmsg SQE shape, Linux msghdr layout, IPv4 sockaddr helper 를 먼저 고정한다.
        [Fact]
        public void NativeSubmissionTypes_WhenInspected_ExposeUdpMessageShape()
        {
            Type nativeType = RequiredType("Hps.Transport.IoUringNative, Hps.Transport.IoUring");
            Type messageHeaderType = RequiredType("Hps.Transport.IoUringMessageHeader, Hps.Transport.IoUring");
            Type queueType = RequiredType("Hps.Transport.IoUringQueue, Hps.Transport.IoUring");
            Type sockaddrType = RequiredType("Hps.Transport.IoUringSockaddr, Hps.Transport.IoUring");

            Assert.NotNull(nativeType.GetField("OperationReceiveMessage", BindingFlags.Static | BindingFlags.NonPublic));
            Assert.NotNull(nativeType.GetField("OperationSendMessage", BindingFlags.Static | BindingFlags.NonPublic));
            Assert.True(Marshal.SizeOf(messageHeaderType) >= 56);
            Assert.NotNull(queueType.GetMethod("TrySubmitReceiveMessage", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(queueType.GetMethod("TrySubmitSendMessage", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(sockaddrType.GetMethod("EncodeIPv4", BindingFlags.Static | BindingFlags.NonPublic));
            Assert.NotNull(sockaddrType.GetMethod("DecodeIPv4", BindingFlags.Static | BindingFlags.NonPublic));
        }

        // native sockaddr_in 은 family 는 host byte order, port/address 는 network byte order 이므로
        // encode/decode roundtrip 으로 byte ordering drift 를 잡는다.
        [Fact]
        public void Sockaddr_WhenIpv4EndpointEncoded_DecodesSameEndpoint()
        {
            Type sockaddrType = RequiredType("Hps.Transport.IoUringSockaddr, Hps.Transport.IoUring");
            MethodInfo encode = RequiredMethod(sockaddrType, "EncodeIPv4");
            MethodInfo decode = RequiredMethod(sockaddrType, "DecodeIPv4");
            byte[] block = new byte[32];
            IPEndPoint expected = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 4567);

            encode.Invoke(null, new object[] { expected, block });
            object actual = decode.Invoke(null, new object[] { block, 16 })!;

            Assert.Equal(expected, Assert.IsType<IPEndPoint>(actual));
        }

        private static Type RequiredType(string name)
        {
            Type? type = Type.GetType(name);
            Assert.NotNull(type);
            return type!;
        }

        private static MethodInfo RequiredMethod(Type type, string name)
        {
            MethodInfo? method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return method!;
        }
    }
}
```

- [x] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringUdpMessageShapeTests -v minimal
```

Expected: failure from missing `IoUringMessageHeader`, opcode fields, queue methods, or sockaddr helper.

- [x] **Step 3: Write minimal implementation**

Add to `IoUringNative.cs`:

```csharp
internal const byte OperationSendMessage = 9;
internal const byte OperationReceiveMessage = 10;

[StructLayout(LayoutKind.Sequential)]
internal struct IoUringMessageHeader
{
    internal IntPtr Name;
    internal uint NameLength;
    private uint _padding0;
    internal IntPtr Iov;
    internal UIntPtr IovLength;
    internal IntPtr Control;
    internal UIntPtr ControlLength;
    internal int Flags;
    private int _padding1;
}
```

Add to `IoUringOperationKind.cs`:

```csharp
UdpReceive = 4,
UdpSend = 5
```

Add to `IoUringQueue.cs`:

```csharp
internal unsafe bool TrySubmitReceiveMessage(int fileDescriptor, IntPtr messageHeader, ulong token)
{
    return TrySubmitMessage(fileDescriptor, messageHeader, token, IoUringNative.OperationReceiveMessage);
}

internal unsafe bool TrySubmitSendMessage(int fileDescriptor, IntPtr messageHeader, ulong token)
{
    return TrySubmitMessage(fileDescriptor, messageHeader, token, IoUringNative.OperationSendMessage);
}

private unsafe bool TrySubmitMessage(int fileDescriptor, IntPtr messageHeader, ulong token, byte opcode)
{
    if (fileDescriptor < 0)
        throw new ArgumentOutOfRangeException(nameof(fileDescriptor), "socket file descriptor 가 유효하지 않습니다.");
    if (messageHeader == IntPtr.Zero)
        throw new ArgumentOutOfRangeException(nameof(messageHeader), "message header pointer 는 0일 수 없습니다.");
    if (token == 0)
        throw new ArgumentOutOfRangeException(nameof(token), "io_uring user_data token 은 0을 사용할 수 없습니다.");

    ThrowIfDisposed();

    lock (_submissionGate)
    {
        IoUringSubmissionQueueEntry* submission = TryAcquireSubmissionEntry();
        if (submission == null)
            return false;

        *submission = default(IoUringSubmissionQueueEntry);
        submission->Opcode = opcode;
        submission->FileDescriptor = fileDescriptor;
        submission->Address = (ulong)messageHeader.ToInt64();
        submission->UserData = token;
        PublishSubmissionEntry(submission);
    }

    IoUringNative.Enter(FileDescriptor, 1, 0, 0);
    return true;
}
```

Create `IoUringSockaddr.cs`:

```csharp
using System;
using System.Net;
using System.Net.Sockets;

namespace Hps.Transport
{
    internal static class IoUringSockaddr
    {
        internal const int Ipv4SockaddrLength = 16;

        internal static void EncodeIPv4(IPEndPoint endPoint, byte[] block)
        {
            if (endPoint == null)
                throw new ArgumentNullException(nameof(endPoint));
            if (block == null)
                throw new ArgumentNullException(nameof(block));
            if (endPoint.AddressFamily != AddressFamily.InterNetwork)
                throw new NotSupportedException("io_uring UDP v1 은 IPv4 endpoint 만 지원합니다.");
            if (block.Length < Ipv4SockaddrLength)
                throw new ArgumentException("IPv4 sockaddr block 은 최소 16바이트여야 합니다.", nameof(block));

            Array.Clear(block, 0, Ipv4SockaddrLength);
            block[0] = 2;
            block[1] = 0;
            block[2] = (byte)((endPoint.Port >> 8) & 0xFF);
            block[3] = (byte)(endPoint.Port & 0xFF);
            byte[] addressBytes = endPoint.Address.GetAddressBytes();
            Buffer.BlockCopy(addressBytes, 0, block, 4, addressBytes.Length);
        }

        internal static IPEndPoint DecodeIPv4(byte[] block, int length)
        {
            if (block == null)
                throw new ArgumentNullException(nameof(block));
            if (length < Ipv4SockaddrLength || block.Length < Ipv4SockaddrLength)
                throw new SocketException((int)SocketError.InvalidArgument);
            if (block[0] != 2 || block[1] != 0)
                throw new NotSupportedException("io_uring UDP v1 은 IPv4 remote endpoint 만 decode 합니다.");

            int port = (block[2] << 8) | block[3];
            byte[] address = new byte[4];
            Buffer.BlockCopy(block, 4, address, 0, address.Length);
            return new IPEndPoint(new IPAddress(address), port);
        }
    }
}
```

- [x] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringUdpMessageShapeTests -v minimal
```

Expected: `IoUringUdpMessageShapeTests` pass.

- [x] **Step 5: Commit**

```powershell
git add src\Hps.Transport.IoUring\IoUringNative.cs src\Hps.Transport.IoUring\IoUringQueue.cs src\Hps.Transport.IoUring\IoUringOperationKind.cs src\Hps.Transport.IoUring\IoUringSockaddr.cs tests\Hps.Transport.IoUring.Tests\IoUringUdpMessageShapeTests.cs
git commit -m "feat: add iouring udp message shape"
```

---

### Task 2: UDP Endpoint Resource And Message Buffer

**Files:**
- Create: `src/Hps.Transport.IoUring/IoUringUdpMessageBuffer.cs`
- Create: `src/Hps.Transport.IoUring/IoUringUdpEndpoint.cs`
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringUdpEndpointShapeTests.cs`

**Interfaces:**
- Consumes: `IoUringMessageHeader`, `IoUringIovec`, `IoUringSockaddr`, `IoUringOperationRegistry`
- Produces: `IoUringUdpMessageBuffer.PrepareReceive(...)`, `PrepareSend(...)`, `MessageHeaderPointer`
- Produces: `IoUringUdpEndpoint.TryAcceptSend(...)`, `TryBeginSend(...)`, `CreateSnapshot()`, `Close()`

- [x] **Step 1: Write the failing test**

Create `tests/Hps.Transport.IoUring.Tests/IoUringUdpEndpointShapeTests.cs`.

```csharp
using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Hps.Buffers;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringUdpEndpointShapeTests
    {
        // UDP endpoint 는 socket 뿐 아니라 msghdr/iovec/sockaddr pin 수명을 completion 까지 보장해야 한다.
        // type shape 를 먼저 고정해 receive/send pump 가 raw pointer lifetime 을 지역 변수에 맡기지 않게 한다.
        [Fact]
        public void UdpResourceTypes_WhenInspected_Exist()
        {
            Assert.NotNull(Type.GetType("Hps.Transport.IoUringUdpEndpoint, Hps.Transport.IoUring"));
            Assert.NotNull(Type.GetType("Hps.Transport.IoUringUdpMessageBuffer, Hps.Transport.IoUring"));
        }

        // close drain 은 UDP send queue 가 소유한 ref 를 정확히 반환해야 한다.
        // pump 구현 전에 endpoint resource 만으로 drop/close ownership 계약을 고정한다.
        [Fact]
        public void UdpEndpoint_WhenClosed_DrainsQueuedSendRefs()
        {
            Type endpointType = RequiredType("Hps.Transport.IoUringUdpEndpoint, Hps.Transport.IoUring");
            using (IoUringTransport transport = new IoUringTransport())
            using (IoUringOperationRegistry registry = new IoUringOperationRegistry())
            using (IoUringCompletionLoop loop = IoUringCompletionLoop.CreateForTests(registry))
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                IUdpEndpoint endpoint = (IUdpEndpoint)Activator.CreateInstance(
                    endpointType,
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new object[] { transport, socket, registry, loop },
                    null)!;

                PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
                RefCountedBuffer buffer = pool.RentCounted();
                buffer.SetLength(1);
                buffer.AddRef();

                MethodInfo tryAccept = RequiredMethod(endpointType, "TryAcceptSend");
                bool accepted = (bool)tryAccept.Invoke(
                    endpoint,
                    new object[] { new IPEndPoint(IPAddress.Loopback, 9), new TransportSendBuffer(buffer, 0, 1) })!;
                Assert.True(accepted);
                buffer.Release();

                endpoint.Close();

                Assert.Equal(0, pool.RentedCount);
            }
        }

        private static Type RequiredType(string name)
        {
            Type? type = Type.GetType(name);
            Assert.NotNull(type);
            return type!;
        }

        private static MethodInfo RequiredMethod(Type type, string name)
        {
            MethodInfo? method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return method!;
        }
    }
}
```

- [x] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringUdpEndpointShapeTests -v minimal
```

Expected: failure because `IoUringUdpEndpoint` and `IoUringUdpMessageBuffer` do not exist.

- [x] **Step 3: Write minimal implementation**

Create `IoUringUdpMessageBuffer.cs` with pinned header/iovec/sockaddr arrays. Use `GCHandle` fields and release them in `Dispose()`.

Create `IoUringUdpEndpoint.cs` by following `SaeaUdpEndpoint` queue semantics:

- capacity 16
- drop-oldest
- `RecordUdpPendingSendDepth`
- `RecordUdpPendingSendDrop`
- close drains pending refs
- `CreateSnapshot()` returns UDP snapshot

Constructor signature:

```csharp
internal IoUringUdpEndpoint(
    IoUringTransport transport,
    Socket socket,
    IoUringOperationRegistry registry,
    IoUringCompletionLoop completionLoop)
```

Required properties:

```csharp
internal int SocketFileDescriptor { get; }
internal IoUringOperationContext ReceiveContext { get; }
internal IoUringOperationContext SendContext { get; }
internal IoUringUdpMessageBuffer ReceiveMessage { get; }
internal IoUringUdpMessageBuffer SendMessage { get; }
internal PinnedBlockMemoryPool ReceivePool { get; }
internal bool IsClosed { get; }
internal bool IsDisposed { get; }
```

- [x] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringUdpEndpointShapeTests -v minimal
```

Expected: endpoint shape tests pass.

- [x] **Step 5: Commit**

```powershell
git add src\Hps.Transport.IoUring\IoUringUdpMessageBuffer.cs src\Hps.Transport.IoUring\IoUringUdpEndpoint.cs tests\Hps.Transport.IoUring.Tests\IoUringUdpEndpointShapeTests.cs
git commit -m "feat: add iouring udp endpoint resource"
```

---

### Task 3: UDP Bind And Receive Pump

**Files:**
- Modify: `src/Hps.Transport.IoUring/IoUringTransport.cs`
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringTransportTests.cs`
- Create: `tests/Hps.Transport.IoUring.Tests/IoUringTransportUdpTests.cs`

**Interfaces:**
- Consumes: `IoUringUdpEndpoint`, `IoUringQueue.TrySubmitReceiveMessage`
- Produces: `IoUringTransport.BindUdpAsync(...)` for IPv4 Linux available host
- Produces: receive loop dispatch to `ITransportDatagramHandler`

- [x] **Step 1: Write the failing Linux-gated receive test**

Create `tests/Hps.Transport.IoUring.Tests/IoUringTransportUdpTests.cs`.

```csharp
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Hps.Buffers;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringTransportUdpTests
    {
        // Linux available host 에서 UDP recvmsg pump 가 datagram 을 RefCountedBuffer ownership 으로 handler 에 넘기는지 검증한다.
        // unavailable 환경에서는 early return 하며, 실제 native 경로 검증은 GitHub Actions contract run 에서 수행된다.
        [Fact]
        public async Task UdpReceive_WhenIoUringAvailable_DeliversOwnedRefCountedBuffer()
        {
            if (IoUringCapabilityProbe.GetStatus() != IoUringCapabilityStatus.Available)
                return;

            CapturingDatagramHandler handler = new CapturingDatagramHandler();
            using (IoUringTransport transport = new IoUringTransport())
            {
                transport.SetDatagramHandler(handler);
                await transport.StartAsync();

                IUdpEndpoint endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                using (Socket client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    byte[] payload = new byte[] { 1, 2, 3 };
                    await client.SendToAsync(new ArraySegment<byte>(payload), SocketFlags.None, endpoint.LocalEndPoint);

                    ReceivedDatagram received = await handler.ReceiveAsync();

                    Assert.Same(endpoint, received.Endpoint);
                    Assert.Equal(payload, received.Payload);
                }

                endpoint.Close();
                await transport.StopAsync();
            }
        }

        private sealed class CapturingDatagramHandler : ITransportDatagramHandler
        {
            private readonly TaskCompletionSource<ReceivedDatagram> _received =
                new TaskCompletionSource<ReceivedDatagram>(TaskCreationOptions.RunContinuationsAsynchronously);

            public void OnDatagramReceived(IUdpEndpoint endpoint, EndPoint remoteEndPoint, RefCountedBuffer datagram)
            {
                try
                {
                    byte[] payload = datagram.Memory.Slice(0, datagram.Length).ToArray();
                    _received.TrySetResult(new ReceivedDatagram(endpoint, remoteEndPoint, payload));
                }
                finally
                {
                    datagram.Release();
                }
            }

            public void OnDatagramEndpointClosed(IUdpEndpoint endpoint)
            {
                _received.TrySetException(new InvalidOperationException("datagram 수신 전에 endpoint 가 닫혔습니다."));
            }

            internal async Task<ReceivedDatagram> ReceiveAsync()
            {
                Task completed = await Task.WhenAny(_received.Task, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
                if (completed != _received.Task)
                    throw new TimeoutException("io_uring UDP receive pump 가 제한 시간 안에 datagram 을 전달하지 않았습니다.");

                return await _received.Task.ConfigureAwait(false);
            }
        }

        private sealed class ReceivedDatagram
        {
            internal ReceivedDatagram(IUdpEndpoint endpoint, EndPoint remoteEndPoint, byte[] payload)
            {
                Endpoint = endpoint;
                RemoteEndPoint = remoteEndPoint;
                Payload = payload;
            }

            internal IUdpEndpoint Endpoint { get; }
            internal EndPoint RemoteEndPoint { get; }
            internal byte[] Payload { get; }
        }
    }
}
```

- [x] **Step 2: Run test to verify it fails on Linux or remains skipped on Windows**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringTransportUdpTests -v minimal
```

Expected on Windows: pass by early return. Expected on Linux available host before implementation: failure from unsupported `BindUdpAsync`.

- [x] **Step 3: Write minimal implementation**

Modify `IoUringTransport.cs`:

- add `_udpEndpoints`
- implement `BindUdpAsync` for IPv4
- override `TrySendTo` with endpoint type validation and IPv4 remote validation returning `false`
- add `RegisterUdpEndpoint`, `UnregisterUdpEndpoint`
- add `StartUdpReceiveLoop`
- add `UdpReceiveLoopAsync`
- add `DispatchDatagramReceived`
- add `NotifyUdpEndpointClosed`
- update `GetEndpointSnapshots()` to include UDP endpoints if the class does not already override it
- update `StopCore()` to close UDP endpoints

Receive loop structure:

```csharp
private async Task UdpReceiveLoopAsync(IoUringUdpEndpoint endpoint)
{
    RefCountedBuffer? datagram = null;

    try
    {
        while (true)
        {
            if (endpoint.IsClosed)
                return;

            datagram = endpoint.ReceivePool.RentCounted();
            ArraySegment<byte> segment = GetRefCountedBlockSegment(datagram, 0, endpoint.ReceivePool.BlockSize);
            IoUringOperationContext context = endpoint.ReceiveContext;
            context.Reset(context.Token, IoUringOperationKind.UdpReceive);
            endpoint.ReceiveMessage.PrepareReceive(segment.Array!, segment.Offset, segment.Count);
            ValueTask<IoUringCompletion> wait = context.WaitAsync();

            if (!endpoint.Queue.TrySubmitReceiveMessage(endpoint.SocketFileDescriptor, endpoint.ReceiveMessage.MessageHeaderPointer, context.Token))
                throw new SocketException((int)SocketError.NoBufferSpaceAvailable);

            IoUringCompletion completion = await wait.ConfigureAwait(false);
            if (completion.Result <= 0 || completion.Result > segment.Count)
                throw new SocketException((int)SocketError.ConnectionReset);

            datagram.SetLength(completion.Result);
            EndPoint remoteEndPoint = endpoint.ReceiveMessage.DecodeRemoteEndPoint();
            RefCountedBuffer owned = datagram;
            datagram = null;
            DispatchDatagramReceived(endpoint, remoteEndPoint, owned);
        }
    }
    catch (ObjectDisposedException)
    {
        datagram?.Release();
    }
    catch
    {
        datagram?.Release();
        NotifyUdpEndpointClosed(endpoint);
    }
}
```

- [x] **Step 4: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringTransportUdpTests -v minimal
```

Expected: Windows early return pass, Linux available host receive test pass.

- [x] **Step 5: Commit**

```powershell
git add src\Hps.Transport.IoUring\IoUringTransport.cs tests\Hps.Transport.IoUring.Tests\IoUringTransportTests.cs tests\Hps.Transport.IoUring.Tests\IoUringTransportUdpTests.cs
git commit -m "feat: add iouring udp receive pump"
```

---

### Task 4: UDP Send Pump And Ownership

**Files:**
- Modify: `src/Hps.Transport.IoUring/IoUringTransport.cs`
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringTransportUdpTests.cs`
- Modify: `tests/Hps.Transport.IoUring.Tests/IoUringUdpEndpointShapeTests.cs`

**Interfaces:**
- Consumes: `IoUringUdpEndpoint.TryBeginSend`
- Produces: `IoUringTransport.TrySendTo(...)`
- Produces: `IoUringQueue.TrySubmitSendMessage(...)` pump path

- [x] **Step 1: Write failing send/echo tests**

Append tests to `IoUringTransportUdpTests.cs`.

```csharp
// Linux available host 에서 TrySendTo 가 queued RefCountedBuffer 를 sendmsg 로 전송하고 completion 후 ref 를 반환하는지 검증한다.
[Fact]
public async Task UdpEcho_WhenIoUringAvailable_QueuesResponseAndClientReceivesPayload()
{
    if (IoUringCapabilityProbe.GetStatus() != IoUringCapabilityStatus.Available)
        return;

    using (IoUringTransport transport = new IoUringTransport())
    {
        EchoingDatagramHandler handler = new EchoingDatagramHandler(transport);
        transport.SetDatagramHandler(handler);
        await transport.StartAsync();

        IUdpEndpoint endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
        using (Socket client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            byte[] payload = new byte[] { 4, 5, 6 };
            await client.SendToAsync(new ArraySegment<byte>(payload), SocketFlags.None, endpoint.LocalEndPoint);

            SocketReceiveFromResult result = await client.ReceiveFromAsync(
                new ArraySegment<byte>(new byte[16]),
                SocketFlags.None,
                new IPEndPoint(IPAddress.Any, 0));

            Assert.Equal(payload.Length, result.ReceivedBytes);
        }

        endpoint.Close();
        await transport.StopAsync();
    }
}
```

Add helper handler:

```csharp
private sealed class EchoingDatagramHandler : ITransportDatagramHandler
{
    private readonly IoUringTransport _transport;

    internal EchoingDatagramHandler(IoUringTransport transport)
    {
        _transport = transport;
    }

    public void OnDatagramReceived(IUdpEndpoint endpoint, EndPoint remoteEndPoint, RefCountedBuffer datagram)
    {
        try
        {
            datagram.AddRef();
            if (!_transport.TrySendTo(endpoint, remoteEndPoint, new TransportSendBuffer(datagram, 0, datagram.Length)))
                datagram.Release();
        }
        finally
        {
            datagram.Release();
        }
    }

    public void OnDatagramEndpointClosed(IUdpEndpoint endpoint)
    {
    }
}
```

- [x] **Step 2: Run tests to verify failure on Linux or early return on Windows**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~UdpEcho_WhenIoUringAvailable -v minimal
```

Expected on Linux available host before implementation: client receive timeout or unsupported send path.

- [x] **Step 3: Write minimal send implementation**

Modify `IoUringTransport.cs`:

- `TrySendTo` accepts only `IoUringUdpEndpoint`.
- unsupported remote IPv6 returns `false`.
- `StartUdpSendLoop(endpoint)` starts a task.
- `UdpSendLoopAsync` drains endpoint pending queue.
- `SendUdpDatagramAsync` prepares send message and waits for completion.
- release send buffer in a `finally` around completion path.

Send helper structure:

```csharp
private async Task SendUdpDatagramAsync(IoUringUdpEndpoint endpoint, EndPoint remoteEndPoint, TransportSendBuffer sendBuffer)
{
    try
    {
        ArraySegment<byte> segment = GetRefCountedBlockSegment(sendBuffer.Buffer, sendBuffer.Offset, sendBuffer.Length);
        if (segment.Array == null)
            throw new InvalidOperationException("io_uring UDP send 는 pinned byte[] 기반 RefCountedBuffer 만 지원합니다.");

        IoUringOperationContext context = endpoint.SendContext;
        context.Reset(context.Token, IoUringOperationKind.UdpSend);
        endpoint.SendMessage.PrepareSend(segment.Array, segment.Offset, segment.Count, (IPEndPoint)remoteEndPoint);
        ValueTask<IoUringCompletion> wait = context.WaitAsync();

        if (!endpoint.Queue.TrySubmitSendMessage(endpoint.SocketFileDescriptor, endpoint.SendMessage.MessageHeaderPointer, context.Token))
            throw new SocketException((int)SocketError.NoBufferSpaceAvailable);

        IoUringCompletion completion = await wait.ConfigureAwait(false);
        if (completion.Result != segment.Count)
            throw new SocketException((int)SocketError.ConnectionReset);
    }
    finally
    {
        sendBuffer.Buffer.Release();
    }
}
```

- [x] **Step 4: Add ownership regression tests**

Add to `IoUringUdpEndpointShapeTests.cs`:

```csharp
// drop-oldest 는 evicted datagram ref 를 즉시 반환하고 endpoint/transport diagnostics 를 증가시켜야 한다.
[Fact]
public void UdpEndpoint_WhenPendingQueueExceedsCapacity_DropsOldestAndReleasesEvictedRef()
{
    Type endpointType = RequiredType("Hps.Transport.IoUringUdpEndpoint, Hps.Transport.IoUring");
    using (IoUringTransport transport = new IoUringTransport())
    using (IoUringOperationRegistry registry = new IoUringOperationRegistry())
    using (IoUringCompletionLoop loop = IoUringCompletionLoop.CreateForTests(registry))
    using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
    {
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        IUdpEndpoint endpoint = (IUdpEndpoint)Activator.CreateInstance(
            endpointType,
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new object[] { transport, socket, registry, loop },
            null)!;

        PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
        IPEndPoint remote = new IPEndPoint(IPAddress.Loopback, 9);
        MethodInfo tryAccept = RequiredMethod(endpointType, "TryAcceptSend");

        for (int i = 0; i < 17; i++)
        {
            RefCountedBuffer buffer = pool.RentCounted();
            buffer.SetLength(1);
            buffer.AddRef();
            Assert.True((bool)tryAccept.Invoke(endpoint, new object[] { remote, new TransportSendBuffer(buffer, 0, 1) })!);
            buffer.Release();
        }

        Assert.Equal(16, pool.RentedCount);
        endpoint.Close();
        Assert.Equal(0, pool.RentedCount);
    }
}
```

- [x] **Step 5: Run focused tests**

Run:

```powershell
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringTransportUdpTests -v minimal
dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --filter FullyQualifiedName~IoUringUdpEndpointShapeTests -v minimal
```

Expected: focused UDP transport and endpoint tests pass.

- [x] **Step 6: Commit**

```powershell
git add src\Hps.Transport.IoUring\IoUringTransport.cs tests\Hps.Transport.IoUring.Tests\IoUringTransportUdpTests.cs tests\Hps.Transport.IoUring.Tests\IoUringUdpEndpointShapeTests.cs
git commit -m "feat: add iouring udp send pump"
```

---

### Task 5: State Docs And Verification

**Files:**
- Modify: `CURRENT_PLAN.md`
- Modify: `TODOS.md`
- Modify: `CHANGELOG_AGENT.md`
- Modify: `DECISIONS.md`
- Modify: `docs/agent-state/changelog/2026-06.md`
- Modify: `docs/agent-state/decisions/2026-06.md`

**Interfaces:**
- Consumes: D140 decision for io_uring UDP v1 boundary
- Produces: implementation completion state docs and next remote Linux UDP artifact review entry

- [ ] **Step 1: Verify D140 is present**

Run:

```powershell
rg -n "D140|IPv4 one-deep|recvmsg|sendmsg" DECISIONS.md docs\agent-state\decisions\2026-06.md
```

Expected: active decision index and archive decision both mention D140.

- [ ] **Step 2: Update current docs**

In `TODOS.md`, move the UDP pump implementation entry to Completed with this content:

```markdown
- [x] Phase 6 io_uring UDP pump 구현 계획 Task 1~4를 TDD로 구현했다.
  - 범위: native UDP message shape, IPv4 sockaddr helper, `IoUringUdpEndpoint`, receive pump, send pump.
  - 결과: `Hps.Transport.IoUring` opt-in backend 가 IPv4 UDP bind/receive/send path 를 갖는다.
  - 검증: focused io_uring UDP tests, `Hps.Transport.IoUring.Tests`, solution build/test, `git diff --check`.
  - 비고: 실제 Linux available host UDP pump 검증은 원격 `iouring-linux-contract` artifact 로 확인한다.
```

Set `TODOS.md` Current TODOs to:

```markdown
- [ ] 원격 `iouring-linux-contract` workflow 실행 결과로 io_uring UDP pump artifact 를 검토한다.
  - 입력: GitHub Actions `iouring-linux-contract` run artifact.
  - 목표: UDP receive/send loopback tests 가 Linux available path 에서 통과했는지 확인한다.
  - 현재 상태: 로컬 Windows 검증은 shape/early-return 중심이며, Linux native UDP syscall path 는 원격 run 으로 확인해야 한다.
  - 제외: artifact 확인 전 receive window 확장, fixed registration cache, zero-copy send, default backend promotion.
```

Append to `CHANGELOG_AGENT.md` and `docs/agent-state/changelog/2026-06.md`:

```markdown
## 2026-06-30 (Codex - io_uring udp pump implementation)

### 작업 단위
- Phase 6 io_uring UDP pump 를 구현했다.

### 변경 내용
- native `recvmsg`/`sendmsg` shape, IPv4 sockaddr helper, UDP endpoint resource, receive/send pump 를 추가했다.
- D140 범위대로 IPv4 one-deep UDP pump 만 구현했고 zero-copy/default promotion 은 제외했다.

### 검증
- focused io_uring UDP tests 통과.
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 통과.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 통과.
- `git diff --check` 통과.
```

- [ ] **Step 3: Run full verification**

Run:

```powershell
dotnet build HighPerformanceSocket.slnx --no-restore -v minimal
dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal
git diff --check
```

Expected:

- build warning 0 / error 0
- full test suite green
- whitespace check passes

- [ ] **Step 4: Commit**

```powershell
git add CURRENT_PLAN.md TODOS.md CHANGELOG_AGENT.md DECISIONS.md docs\agent-state\changelog\2026-06.md docs\agent-state\decisions\2026-06.md
git commit -m "docs: record iouring udp pump boundary"
```

---

## Self-Review

- Spec coverage: native message ABI, endpoint resource, receive pump, send pump, ownership cleanup, diagnostics, excluded zero-copy/default promotion are covered.
- 빈칸 검사: 비워 둔 구현 단계나 미정 항목은 없다.
- Type consistency: task interfaces use `IoUringUdpEndpoint`, `IoUringUdpMessageBuffer`, `IoUringSockaddr`, and existing `IoUringQueue`/`IoUringOperationContext` names consistently.
- Scope: one-deep IPv4 UDP pump only; receive window, IPv6, fixed registration, zero-copy and promotion are explicitly excluded.
