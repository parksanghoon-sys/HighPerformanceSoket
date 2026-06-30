using System;
using System.Net;
using System.Net.Sockets;
using Hps.Buffers;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringUdpEndpointShapeTests
    {
        private const int PendingSendCapacity = 16;

        // UDP endpoint 는 socket 뿐 아니라 msghdr/iovec/sockaddr pin 수명도 completion 까지 보장해야 한다.
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
            using (IoUringTransport transport = new IoUringTransport())
            {
                IoUringCompletionLoop? loop = null;
                IoUringUdpEndpoint? endpoint = null;

                try
                {
                    endpoint = CreateDetachedEndpoint(transport, out loop);

                    PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
                    RefCountedBuffer buffer = pool.RentCounted();
                    buffer.SetLength(1);
                    buffer.AddRef();

                    bool accepted = endpoint.TryAcceptSend(
                        new IPEndPoint(IPAddress.Loopback, 9),
                        new TransportSendBuffer(buffer, 0, 1));
                    Assert.True(accepted);
                    buffer.Release();

                    endpoint.Close();

                    Assert.Equal(0, pool.RentedCount);
                }
                finally
                {
                    endpoint?.Dispose();
                    loop?.Dispose();
                }
            }
        }

        // UDP public send path 소유권 테스트: TrySendTo 가 true 를 반환하면 호출자는 자기 publish ref 만 내려놓고,
        // queued transport ref 는 endpoint close/drain 이 반환해야 한다. 이 경계를 깨면 fan-out drop/close 경합에서 누수나 이중 반환이 생긴다.
        [Fact]
        public void UdpSendTo_WhenAccepted_TransportOwnsQueuedRefUntilEndpointCloses()
        {
            using (IoUringTransport transport = new IoUringTransport())
            {
                IoUringCompletionLoop? loop = null;
                IoUringUdpEndpoint? endpoint = null;
                RefCountedBuffer? buffer = null;
                bool publisherRefReleased = false;

                try
                {
                    endpoint = CreateDetachedEndpoint(transport, out loop);
                    PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
                    buffer = pool.RentCounted();
                    buffer.SetLength(1);
                    buffer.AddRef();

                    bool accepted = transport.TrySendTo(
                        endpoint,
                        new IPEndPoint(IPAddress.Loopback, 9),
                        new TransportSendBuffer(buffer, 0, 1));

                    if (!accepted)
                        buffer.Release();

                    Assert.True(accepted);

                    buffer.Release();
                    publisherRefReleased = true;

                    EndpointSnapshot snapshot = endpoint.CreateSnapshot();
                    Assert.Equal(1, snapshot.PendingSendCount);
                    Assert.Equal(1, snapshot.PendingSendQueueHighWatermark);
                    Assert.Equal(0, snapshot.DroppedPendingSendCount);
                    Assert.Equal(1, pool.RentedCount);

                    endpoint.Close();

                    Assert.Equal(0, pool.RentedCount);
                }
                finally
                {
                    if (!publisherRefReleased)
                        buffer?.Release();

                    endpoint?.Dispose();
                    loop?.Dispose();
                }
            }
        }

        // UDP send reject 소유권 테스트: 닫힌 endpoint 로 보낸 datagram 은 transport 가 ref 를 가져가지 않는다.
        // 호출자가 실패 반환 뒤 추가 ref 를 Release 해도 안전해야 closed endpoint 로 인한 누수와 이중 반환을 동시에 막을 수 있다.
        [Fact]
        public void UdpSendTo_WhenEndpointClosed_ReturnsFalseAndLeavesCallerOwnedRef()
        {
            using (IoUringTransport transport = new IoUringTransport())
            {
                IoUringCompletionLoop? loop = null;
                IoUringUdpEndpoint? endpoint = null;

                try
                {
                    endpoint = CreateDetachedEndpoint(transport, out loop);
                    endpoint.Close();

                    PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
                    RefCountedBuffer buffer = pool.RentCounted();
                    buffer.SetLength(1);
                    buffer.AddRef();

                    bool accepted = transport.TrySendTo(
                        endpoint,
                        new IPEndPoint(IPAddress.Loopback, 9),
                        new TransportSendBuffer(buffer, 0, 1));

                    Assert.False(accepted);
                    buffer.Release();
                    buffer.Release();
                    Assert.Equal(0, pool.RentedCount);
                }
                finally
                {
                    endpoint?.Dispose();
                    loop?.Dispose();
                }
            }
        }

        // UDP IPv4-only 정책 테스트: D140 의 v1 범위는 IPv4 one-deep recvmsg/sendmsg 이므로 IPv6 remote 는 false 로 거절한다.
        // 거절 경로는 queue 에 들어가지 않으므로 caller 가 추가 ref 를 직접 Release 해야 하고 transport 는 ref 를 건드리지 않아야 한다.
        [Fact]
        public void UdpSendTo_WhenRemoteIsIpv6_ReturnsFalseAndLeavesCallerOwnedRef()
        {
            using (IoUringTransport transport = new IoUringTransport())
            {
                IoUringCompletionLoop? loop = null;
                IoUringUdpEndpoint? endpoint = null;

                try
                {
                    endpoint = CreateDetachedEndpoint(transport, out loop);

                    PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
                    RefCountedBuffer buffer = pool.RentCounted();
                    buffer.SetLength(1);
                    buffer.AddRef();

                    bool accepted = transport.TrySendTo(
                        endpoint,
                        new IPEndPoint(IPAddress.IPv6Loopback, 9),
                        new TransportSendBuffer(buffer, 0, 1));

                    Assert.False(accepted);
                    buffer.Release();
                    buffer.Release();
                    Assert.Equal(0, pool.RentedCount);
                    Assert.Equal(0, endpoint.CreateSnapshot().PendingSendCount);
                }
                finally
                {
                    endpoint?.Dispose();
                    loop?.Dispose();
                }
            }
        }

        // drop-oldest 는 가장 오래된 pending datagram ref 를 즉시 반환해야 한다.
        // endpoint 와 transport diagnostics 모두 drop count/high-watermark 를 보존해야 운영에서 느린 consumer 를 식별할 수 있다.
        [Fact]
        public void UdpEndpoint_WhenPendingQueueExceedsCapacity_DropsOldestAndKeepsDiagnostics()
        {
            using (IoUringTransport transport = new IoUringTransport())
            {
                ITransportDiagnostics diagnostics = transport;
                IoUringCompletionLoop? loop = null;
                IoUringUdpEndpoint? endpoint = null;

                try
                {
                    endpoint = CreateDetachedEndpoint(transport, out loop);

                    PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
                    IPEndPoint remote = new IPEndPoint(IPAddress.Loopback, 9);

                    for (int index = 0; index < PendingSendCapacity + 2; index++)
                    {
                        RefCountedBuffer buffer = pool.RentCounted();
                        buffer.SetLength(1);
                        buffer.AddRef();

                        bool accepted = endpoint.TryAcceptSend(remote, new TransportSendBuffer(buffer, 0, 1));
                        Assert.True(accepted);
                        buffer.Release();
                    }

                    EndpointSnapshot endpointSnapshot = endpoint.CreateSnapshot();
                    Assert.Equal(PendingSendCapacity, endpointSnapshot.PendingSendCount);
                    Assert.Equal(PendingSendCapacity, endpointSnapshot.PendingSendQueueHighWatermark);
                    Assert.Equal(2, endpointSnapshot.DroppedPendingSendCount);

                    TransportDiagnosticsSnapshot transportSnapshot = diagnostics.GetDiagnosticsSnapshot();
                    Assert.Equal(PendingSendCapacity, transportSnapshot.UdpPendingSendQueueHighWatermark);
                    Assert.Equal(2, transportSnapshot.UdpDroppedPendingSendCount);
                    Assert.Equal(2, transportSnapshot.DroppedPendingSendCount);
                    Assert.Equal(PendingSendCapacity, pool.RentedCount);

                    endpoint.Close();

                    EndpointSnapshot closedSnapshot = endpoint.CreateSnapshot();
                    Assert.Equal(0, closedSnapshot.PendingSendCount);
                    Assert.Equal(PendingSendCapacity, closedSnapshot.PendingSendQueueHighWatermark);
                    Assert.Equal(2, closedSnapshot.DroppedPendingSendCount);
                    Assert.Equal(0, pool.RentedCount);
                }
                finally
                {
                    endpoint?.Dispose();
                    loop?.Dispose();
                }
            }
        }

        private static IoUringUdpEndpoint CreateDetachedEndpoint(
            IoUringTransport transport,
            out IoUringCompletionLoop loop)
        {
            IoUringOperationRegistry registry = new IoUringOperationRegistry();
            loop = IoUringCompletionLoop.CreateForTests(registry);
            Socket? socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                IoUringUdpEndpoint endpoint = new IoUringUdpEndpoint(transport, socket, registry, loop);
                socket = null;
                return endpoint;
            }
            finally
            {
                socket?.Dispose();
            }
        }
    }
}
