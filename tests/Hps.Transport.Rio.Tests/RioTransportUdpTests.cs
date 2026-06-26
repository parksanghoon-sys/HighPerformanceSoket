using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Hps.Buffers;
using Xunit;

namespace Hps.Transport.Rio.Tests
{
    public sealed class RioTransportUdpTests
    {
        // RIO UDP skeleton 의 첫 계약은 BindUdpAsync 가 실제 bind 된 IUdpEndpoint 를 반환하는 것이다.
        // receive/send loop 는 후속 task 이지만, endpoint owner 와 close 경계가 먼저 있어야 한다.
        [Fact]
        public async Task BindUdpAsync_WhenRioDatagramAvailable_ReturnsEndpointWithLocalEndPoint()
        {
            if (!IsRioDatagramAvailable())
                return;

            IUdpEndpoint? endpoint = null;
            using (RioTransport transport = new RioTransport())
            {
                await transport.StartAsync();

                try
                {
                    endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));

                    Assert.NotNull(endpoint);
                    IPEndPoint localEndPoint = Assert.IsType<IPEndPoint>(endpoint.LocalEndPoint);
                    Assert.Equal(IPAddress.Loopback, localEndPoint.Address);
                    Assert.NotEqual(0, localEndPoint.Port);
                }
                finally
                {
                    endpoint?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // UDP RIO completion wait 를 polling 이 아니라 IOCP notification 으로 바꾸려면 endpoint 가 receive/send signal 을 소유해야 한다.
        // 이 테스트는 native wait path 를 바꾸기 전에 endpoint resource shape 가 TCP RIO와 같은 notification 기반으로 열렸는지 먼저 고정한다.
        [Fact]
        public async Task BindUdpAsync_WhenRioDatagramAvailable_CreatesUdpCompletionSignals()
        {
            if (!IsRioDatagramAvailable())
                return;

            IUdpEndpoint? endpoint = null;
            using (RioTransport transport = new RioTransport())
            {
                await transport.StartAsync();

                try
                {
                    endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    RioUdpEndpoint rioEndpoint = Assert.IsType<RioUdpEndpoint>(endpoint);

                    PropertyInfo? receiveSignalProperty = typeof(RioUdpEndpoint).GetProperty(
                        "ReceiveSignal",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    PropertyInfo? sendSignalProperty = typeof(RioUdpEndpoint).GetProperty(
                        "SendSignal",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                    Assert.NotNull(receiveSignalProperty);
                    Assert.NotNull(sendSignalProperty);
                    Assert.NotNull(receiveSignalProperty!.GetValue(rioEndpoint));
                    Assert.NotNull(sendSignalProperty!.GetValue(rioEndpoint));
                }
                finally
                {
                    endpoint?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // UDP wait path 가 TCP RIO처럼 notification arm helper 를 가져야 hot path 에서 Task.Delay fallback 을 제거할 수 있다.
        // 기존 bounded yield/delay polling 구현에는 이 helper shape 가 없었기 때문에 회귀 시 다시 잡힌다.
        [Fact]
        public void RioUdpEndpoint_WhenNotificationWaitIsExpected_ExposesArmNotificationHelper()
        {
            MethodInfo? method = typeof(RioUdpEndpoint).GetMethod(
                "ArmNotification",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(method);
        }

        // RIO UDP receive loop 는 SAEA UDP 와 같은 public handler 계약을 지켜야 한다.
        // raw UDP client 가 보낸 datagram 이 owned RefCountedBuffer 로 전달되고,
        // remote endpoint 는 RIOReceiveEx 가 채운 SOCKADDR_INET buffer 에서 복원되어야 한다.
        [Fact]
        public async Task UdpReceive_WhenRawClientSendsDatagram_DeliversOwnedRefCountedBuffer()
        {
            if (!IsRioDatagramAvailable())
                return;

            using (RioTransport transport = new RioTransport())
            {
                CapturingDatagramHandler datagramHandler = new CapturingDatagramHandler();
                transport.SetDatagramHandler(datagramHandler);
                await transport.StartAsync();

                IUdpEndpoint? endpoint = null;
                Socket? client = null;

                try
                {
                    endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(endpoint.LocalEndPoint);
                    Assert.NotEqual(0, boundEndPoint.Port);

                    client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    client.Bind(new IPEndPoint(IPAddress.Loopback, 0));

                    byte[] payload = new byte[] { 21, 22, 23, 24 };
                    int sent = await client.SendToAsync(new ArraySegment<byte>(payload), SocketFlags.None, boundEndPoint);
                    Assert.Equal(payload.Length, sent);

                    ReceivedDatagram received = await WaitForReceivedDatagramAsync(datagramHandler);

                    Assert.Same(endpoint, received.Endpoint);
                    Assert.Equal(payload, received.Payload);
                    Assert.Equal(client.LocalEndPoint, received.RemoteEndPoint);
                }
                finally
                {
                    client?.Dispose();
                    endpoint?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // RIO UDP send loop 는 SAEA UDP 와 같은 TrySendTo 계약을 따라야 한다.
        // handler 가 받은 datagram 에 ref 를 추가해 같은 endpoint/remote 로 enqueue 하면 raw UDP client 가 동일 payload 를 받아야 한다.
        [Fact]
        public async Task UdpEcho_WhenDatagramHandlerQueuesResponse_ClientReceivesSamePayload()
        {
            if (!IsRioDatagramAvailable())
                return;

            using (RioTransport transport = new RioTransport())
            {
                EchoingDatagramHandler datagramHandler = new EchoingDatagramHandler(transport);
                transport.SetDatagramHandler(datagramHandler);
                await transport.StartAsync();

                IUdpEndpoint? endpoint = null;
                Socket? client = null;

                try
                {
                    endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(endpoint.LocalEndPoint);

                    client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    client.Bind(new IPEndPoint(IPAddress.Loopback, 0));

                    byte[] payload = new byte[] { 51, 52, 53, 54 };
                    int sent = await client.SendToAsync(new ArraySegment<byte>(payload), SocketFlags.None, boundEndPoint);
                    Assert.Equal(payload.Length, sent);

                    ReceivedSocketDatagram echoed = await ReceiveUdpDatagramAsync(client, payload.Length);

                    Assert.Equal(payload, echoed.Payload);
                    Assert.Equal(boundEndPoint, echoed.RemoteEndPoint);
                }
                finally
                {
                    client?.Dispose();
                    endpoint?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // RIO UDP diagnostics 는 SAEA UDP와 같은 endpoint snapshot capability 를 제공해야 한다.
        // bind 된 UDP endpoint 가 open 상태와 초기 send queue 관측값 0으로 보이면 Server/benchmark 쪽이 backend 차이를 몰라도 된다.
        [Fact]
        public async Task GetEndpointSnapshots_WhenUdpEndpointIsOpen_ReturnsUdpSnapshot()
        {
            if (!IsRioDatagramAvailable())
                return;

            using (RioTransport transport = new RioTransport())
            {
                await transport.StartAsync();
                IUdpEndpoint? endpoint = null;

                try
                {
                    endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    ITransportEndpointDiagnostics diagnostics = Assert.IsAssignableFrom<ITransportEndpointDiagnostics>(transport);

                    EndpointSnapshot[] snapshots = diagnostics.GetEndpointSnapshots();
                    EndpointSnapshot snapshot = Assert.Single(snapshots);

                    Assert.Equal(EndpointTransportKind.Udp, snapshot.TransportKind);
                    Assert.Equal(EndpointState.Open, snapshot.State);
                    Assert.Equal(0, snapshot.PendingSendCount);
                    Assert.Equal(0, snapshot.PendingSendQueueHighWatermark);
                    Assert.Equal(0, snapshot.DroppedPendingSendCount);
                    Assert.True(snapshot.Id.Value > 0);
                }
                finally
                {
                    endpoint?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // RIO UDP handler 예외 정책 테스트: SAEA와 같이 handler 가 datagram 소유권을 받은 뒤 예외를 던져도
        // background receive loop 를 fault 상태로 방치하지 않고 endpoint close notification 으로 수렴해야 한다.
        [Fact]
        public async Task UdpReceive_WhenHandlerThrowsAfterTakingOwnership_ClosesEndpointAndNotifiesHandler()
        {
            if (!IsRioDatagramAvailable())
                return;

            using (RioTransport transport = new RioTransport())
            {
                ThrowingAfterReleaseDatagramHandler datagramHandler = new ThrowingAfterReleaseDatagramHandler();
                transport.SetDatagramHandler(datagramHandler);
                await transport.StartAsync();

                IUdpEndpoint? endpoint = null;
                Socket? sender = null;

                try
                {
                    endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(endpoint.LocalEndPoint);

                    sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    byte[] payload = new byte[] { 71, 72, 73 };
                    int sent = await sender.SendToAsync(new ArraySegment<byte>(payload), SocketFlags.None, boundEndPoint);
                    Assert.Equal(payload.Length, sent);

                    IUdpEndpoint closedEndpoint = await WaitForClosedUdpEndpointAsync(datagramHandler.ClosedTask);

                    Assert.Same(endpoint, closedEndpoint);
                    await WaitForRioEndpointClosedAsync(Assert.IsType<RioUdpEndpoint>(endpoint));
                    Assert.Equal(1, datagramHandler.ClosedCallCount);
                }
                finally
                {
                    sender?.Dispose();
                    endpoint?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // RIO UDP bounded receive window 테스트: 첫 handler 가 막혀 있는 동안 다음 ReceiveEx 들을 미리 post 해야 한다.
        // blocked 중 들어온 두 번째 datagram 은 추가 송신 없이 unblock 뒤 곧바로 handler 로 전달되어야 한다.
        [Fact]
        public async Task UdpReceive_WhenHandlerIsBlocked_PrePostsOneAdditionalReceive()
        {
            if (!IsRioDatagramAvailable())
                return;

            using (RioTransport transport = new RioTransport())
            {
                BlockingFirstDatagramHandler datagramHandler = new BlockingFirstDatagramHandler();
                transport.SetDatagramHandler(datagramHandler);
                await transport.StartAsync();

                IUdpEndpoint? endpoint = null;
                Socket? sender = null;

                try
                {
                    endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    RioUdpEndpoint rioEndpoint = Assert.IsType<RioUdpEndpoint>(endpoint);
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(endpoint.LocalEndPoint);

                    sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                    byte[] firstPayload = new byte[] { 81 };
                    int firstSent = await sender.SendToAsync(new ArraySegment<byte>(firstPayload), SocketFlags.None, boundEndPoint);
                    Assert.Equal(firstPayload.Length, firstSent);

                    await WaitForSignalAsync(datagramHandler.FirstReceivedTask);
                    Assert.Equal(1, datagramHandler.ReceivedCount);
                    byte[] secondPayload = new byte[] { 82 };
                    int secondSent = await sender.SendToAsync(new ArraySegment<byte>(secondPayload), SocketFlags.None, boundEndPoint);
                    Assert.Equal(secondPayload.Length, secondSent);

                    await WaitForRentedCountAsync(rioEndpoint.ReceivePool, 3);
                    Assert.Equal(1, datagramHandler.ReceivedCount);

                    datagramHandler.AllowFirstDatagramToComplete();
                    await WaitForSignalAsync(datagramHandler.SecondReceivedTask);

                    Assert.Equal(2, datagramHandler.ReceivedCount);

                    endpoint.Close();
                    endpoint = null;
                    await WaitForRentedCountAsync(rioEndpoint.ReceivePool, 0);
                }
                finally
                {
                    datagramHandler.AllowFirstDatagramToComplete();
                    sender?.Dispose();
                    endpoint?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // RIO UDP bounded receive window 는 첫 handler 가 막힌 동안 두 개의 추가 datagram 을 outstanding receive 로 받아야 한다.
        // 기존 one-deep 구현은 blocked handler 중 추가 한 개까지만 안정적으로 보존하므로 depth 2 전환의 Red 근거가 된다.
        [Fact]
        public async Task UdpReceive_WhenHandlerIsBlocked_PreservesTwoQueuedDatagramsWithBoundedWindow()
        {
            if (!IsRioDatagramAvailable())
                return;

            using (RioTransport transport = new RioTransport())
            {
                BlockingFirstDatagramHandler datagramHandler = new BlockingFirstDatagramHandler();
                transport.SetDatagramHandler(datagramHandler);
                await transport.StartAsync();

                IUdpEndpoint? endpoint = null;
                Socket? sender = null;

                try
                {
                    endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    RioUdpEndpoint rioEndpoint = Assert.IsType<RioUdpEndpoint>(endpoint);
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(endpoint.LocalEndPoint);

                    sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                    int firstSent = await sender.SendToAsync(new ArraySegment<byte>(new byte[] { 101 }), SocketFlags.None, boundEndPoint);
                    Assert.Equal(1, firstSent);

                    await WaitForSignalAsync(datagramHandler.FirstReceivedTask);
                    Assert.Equal(1, datagramHandler.ReceivedCount);

                    int secondSent = await sender.SendToAsync(new ArraySegment<byte>(new byte[] { 102 }), SocketFlags.None, boundEndPoint);
                    Assert.Equal(1, secondSent);
                    int thirdSent = await sender.SendToAsync(new ArraySegment<byte>(new byte[] { 103 }), SocketFlags.None, boundEndPoint);
                    Assert.Equal(1, thirdSent);

                    await WaitForRentedCountAsync(rioEndpoint.ReceivePool, 3);
                    Assert.Equal(1, datagramHandler.ReceivedCount);

                    datagramHandler.AllowFirstDatagramToComplete();
                    await WaitForReceivedCountAsync(datagramHandler, 3);

                    Assert.Equal(3, datagramHandler.ReceivedCount);

                    endpoint.Close();
                    endpoint = null;
                    await WaitForRentedCountAsync(rioEndpoint.ReceivePool, 0);
                }
                finally
                {
                    datagramHandler.AllowFirstDatagramToComplete();
                    sender?.Dispose();
                    endpoint?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // RIO UDP close-drain 테스트: bounded receive window 상태에서는 handler 가 보유한 current datagram 과
        // provider 에 post 된 receive buffers 가 동시에 존재할 수 있다. Close 는 receive CQ를 즉시 닫지 말고
        // receive loop owner 가 모든 resource 를 정리하게 해야 한다.
        [Fact]
        public async Task UdpReceive_WhenEndpointClosesWithPrePostedReceive_ReleasesOutstandingReceive()
        {
            if (!IsRioDatagramAvailable())
                return;

            using (RioTransport transport = new RioTransport())
            {
                BlockingFirstDatagramHandler datagramHandler = new BlockingFirstDatagramHandler();
                transport.SetDatagramHandler(datagramHandler);
                await transport.StartAsync();

                IUdpEndpoint? endpoint = null;
                Socket? sender = null;

                try
                {
                    endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    RioUdpEndpoint rioEndpoint = Assert.IsType<RioUdpEndpoint>(endpoint);
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(endpoint.LocalEndPoint);

                    sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    int sent = await sender.SendToAsync(new ArraySegment<byte>(new byte[] { 91 }), SocketFlags.None, boundEndPoint);
                    Assert.Equal(1, sent);

                    await WaitForSignalAsync(datagramHandler.FirstReceivedTask);
                    await WaitForRentedCountAsync(rioEndpoint.ReceivePool, 3);

                    endpoint.Close();
                    endpoint = null;
                    datagramHandler.AllowFirstDatagramToComplete();

                    await WaitForRentedCountAsync(rioEndpoint.ReceivePool, 0);
                }
                finally
                {
                    datagramHandler.AllowFirstDatagramToComplete();
                    sender?.Dispose();
                    endpoint?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // RIO UDP handler 예외 테스트: handler 호출 전에 이미 next receive 가 post 되었으므로,
        // handler 예외로 endpoint close 로 수렴할 때 next operation 도 같은 receive-loop cleanup 경로에서 정리되어야 한다.
        [Fact]
        public async Task UdpReceive_WhenHandlerThrowsWithPrePostedReceive_ReleasesOutstandingReceiveAndNotifiesOnce()
        {
            if (!IsRioDatagramAvailable())
                return;

            using (RioTransport transport = new RioTransport())
            {
                ThrowingAfterReleaseDatagramHandler datagramHandler = new ThrowingAfterReleaseDatagramHandler();
                transport.SetDatagramHandler(datagramHandler);
                await transport.StartAsync();

                IUdpEndpoint? endpoint = null;
                Socket? sender = null;

                try
                {
                    endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    RioUdpEndpoint rioEndpoint = Assert.IsType<RioUdpEndpoint>(endpoint);
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(endpoint.LocalEndPoint);

                    sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    int sent = await sender.SendToAsync(new ArraySegment<byte>(new byte[] { 92 }), SocketFlags.None, boundEndPoint);
                    Assert.Equal(1, sent);

                    IUdpEndpoint closedEndpoint = await WaitForClosedUdpEndpointAsync(datagramHandler.ClosedTask);

                    Assert.Same(endpoint, closedEndpoint);
                    await WaitForRioEndpointClosedAsync(rioEndpoint);
                    await WaitForRentedCountAsync(rioEndpoint.ReceivePool, 0);
                    Assert.Equal(1, datagramHandler.ClosedCallCount);
                }
                finally
                {
                    sender?.Dispose();
                    endpoint?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // RIO UDP close-drain 테스트: send pump 가 가져가기 전 endpoint 가 닫히면
        // pending queue 가 소유한 datagram ref 를 endpoint close 경로에서 모두 반환해야 한다.
        [Fact]
        public void UdpSendTo_WhenEndpointClosesBeforePumpSends_DrainsQueuedDatagramRef()
        {
            using (RioTransport transport = new RioTransport())
            {
                RioUdpEndpoint? endpoint;
                if (!TryCreateDetachedUdpEndpoint(transport, out endpoint))
                    return;

                RioUdpEndpoint rioEndpoint = endpoint!;
                PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
                RefCountedBuffer buffer = pool.RentCounted();
                bool publisherRefReleased = false;

                try
                {
                    byte[] payload = new byte[] { 91, 92, 93 };
                    payload.CopyTo(buffer.Span);
                    buffer.SetLength(payload.Length);
                    buffer.AddRef();

                    TransportSendBuffer sendBuffer = new TransportSendBuffer(buffer, 0, payload.Length);
                    Assert.True(transport.TrySendTo(rioEndpoint, new IPEndPoint(IPAddress.Loopback, 9), sendBuffer));
                    Assert.Equal(1, rioEndpoint.CreateSnapshot().PendingSendCount);

                    buffer.Release();
                    publisherRefReleased = true;
                    Assert.Equal(1, pool.RentedCount);

                    rioEndpoint.Close();

                    Assert.Equal(0, rioEndpoint.CreateSnapshot().PendingSendCount);
                    Assert.Equal(0, pool.RentedCount);
                }
                finally
                {
                    if (!publisherRefReleased)
                        buffer.Release();

                    rioEndpoint.Close();
                }
            }
        }

        // RIO UDP drop-oldest ownership 테스트: endpoint pending queue 가 capacity 를 넘으면
        // 가장 오래된 Transport 소유 ref 를 정확히 한 번 Release 하고, 남은 항목과 high-watermark 를 SAEA와 같은 의미로 보존해야 한다.
        [Fact]
        public void UdpSendTo_WhenPendingQueueExceedsCapacity_DropsOldestAndKeepsDiagnostics()
        {
            const int Capacity = 16;
            const int SendCount = Capacity + 2;

            using (RioTransport transport = new RioTransport())
            {
                RioUdpEndpoint? endpoint;
                if (!TryCreateDetachedUdpEndpoint(transport, out endpoint))
                    return;

                RioUdpEndpoint rioEndpoint = endpoint!;
                PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
                RefCountedBuffer[] buffers = RentNumberedUdpBuffers(pool, SendCount);
                bool publisherRefsReleased = false;

                try
                {
                    EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 9);
                    for (int index = 0; index < SendCount; index++)
                    {
                        buffers[index].AddRef();
                        TransportSendBuffer sendBuffer = new TransportSendBuffer(buffers[index], 0, buffers[index].Length);

                        Assert.True(transport.TrySendTo(rioEndpoint, remoteEndPoint, sendBuffer));
                    }

                    EndpointSnapshot fullSnapshot = rioEndpoint.CreateSnapshot();
                    Assert.Equal(Capacity, fullSnapshot.PendingSendCount);
                    Assert.Equal(Capacity, fullSnapshot.PendingSendQueueHighWatermark);
                    Assert.Equal(2, fullSnapshot.DroppedPendingSendCount);

                    ReleasePublisherRefs(buffers);
                    publisherRefsReleased = true;

                    Assert.Equal(Capacity, pool.RentedCount);

                    for (int index = 2; index < SendCount; index++)
                    {
                        RioUdpEndpoint.UdpSendRequest sendRequest;
                        Assert.True(rioEndpoint.TryBeginSend(out sendRequest));

                        try
                        {
                            Assert.Same(buffers[index], sendRequest.SendBuffer.Buffer);
                        }
                        finally
                        {
                            sendRequest.SendBuffer.Buffer.Release();
                        }
                    }

                    Assert.False(rioEndpoint.TryBeginSend(out _));
                    Assert.Equal(0, rioEndpoint.CreateSnapshot().PendingSendCount);
                    Assert.Equal(0, pool.RentedCount);
                }
                finally
                {
                    if (!publisherRefsReleased)
                        ReleasePublisherRefs(buffers);

                    rioEndpoint.Close();
                }
            }
        }

        private static bool IsRioDatagramAvailable()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
            {
                return false;
            }

            RioNative? native;
            return RioNative.TryLoadFunctionTable(out native) &&
                native != null &&
                native.SupportsDatagramOperations;
        }

        private static bool TryCreateDetachedUdpEndpoint(RioTransport transport, out RioUdpEndpoint? endpoint)
        {
            endpoint = null;

            if (!IsRioDatagramAvailable())
                return false;

            RioNative? native;
            if (!RioNative.TryLoadFunctionTable(out native) || native == null || !native.SupportsDatagramOperations)
                return false;

            Socket? socket = RioNative.CreateUdpSocket();
            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                endpoint = new RioUdpEndpoint(transport, socket, native, RioCompletionPort.CreateForTests());
                socket = null;
                return true;
            }
            finally
            {
                socket?.Dispose();
            }
        }

        private static async Task<ReceivedDatagram> WaitForReceivedDatagramAsync(CapturingDatagramHandler datagramHandler)
        {
            Task<ReceivedDatagram> receivedTask = datagramHandler.ReceivedTask;
            Task<IUdpEndpoint> closedTask = datagramHandler.ClosedTask;
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            Task completedTask = await Task.WhenAny(receivedTask, closedTask, timeoutTask);

            Assert.Same(receivedTask, completedTask);
            return await receivedTask;
        }

        private static async Task<IUdpEndpoint> WaitForClosedUdpEndpointAsync(Task<IUdpEndpoint> closedTask)
        {
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            Task completedTask = await Task.WhenAny(closedTask, timeoutTask);

            Assert.Same(closedTask, completedTask);
            return await closedTask;
        }

        private static async Task WaitForSignalAsync(Task signalTask)
        {
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            Task completedTask = await Task.WhenAny(signalTask, timeoutTask);

            Assert.Same(signalTask, completedTask);
            await signalTask;
        }

        private static async Task WaitForRioEndpointClosedAsync(RioUdpEndpoint endpoint)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);

            while (DateTime.UtcNow < deadline)
            {
                if (endpoint.IsClosed)
                    return;

                await Task.Delay(10);
            }

            Assert.True(endpoint.IsClosed);
        }

        private static async Task WaitForRentedCountAsync(PinnedBlockMemoryPool pool, int expected)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);

            while (DateTime.UtcNow < deadline)
            {
                if (pool.RentedCount == expected)
                    return;

                await Task.Delay(10);
            }

            Assert.Equal(expected, pool.RentedCount);
        }

        private static async Task WaitForReceivedCountAsync(BlockingFirstDatagramHandler handler, int expected)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);

            while (DateTime.UtcNow < deadline)
            {
                if (handler.ReceivedCount >= expected)
                    return;

                await Task.Delay(10);
            }

            Assert.Equal(expected, handler.ReceivedCount);
        }

        private static RefCountedBuffer[] RentNumberedUdpBuffers(PinnedBlockMemoryPool pool, int count)
        {
            RefCountedBuffer[] buffers = new RefCountedBuffer[count];

            for (int index = 0; index < count; index++)
            {
                RefCountedBuffer buffer = pool.RentCounted();
                buffer.Span[0] = (byte)(index + 1);
                buffer.SetLength(1);
                buffers[index] = buffer;
            }

            return buffers;
        }

        private static void ReleasePublisherRefs(RefCountedBuffer[] buffers)
        {
            for (int index = 0; index < buffers.Length; index++)
            {
                buffers[index].Release();
            }
        }

        private static async Task<ReceivedSocketDatagram> ReceiveUdpDatagramAsync(Socket socket, int maxLength)
        {
            byte[] receiveBuffer = new byte[maxLength];
            Task<SocketReceiveFromResult> receiveTask = socket.ReceiveFromAsync(
                new ArraySegment<byte>(receiveBuffer),
                SocketFlags.None,
                new IPEndPoint(IPAddress.Any, 0));
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            Task completedTask = await Task.WhenAny(receiveTask, timeoutTask);

            Assert.Same(receiveTask, completedTask);
            SocketReceiveFromResult result = await receiveTask;
            byte[] payload = new byte[result.ReceivedBytes];
            Buffer.BlockCopy(receiveBuffer, 0, payload, 0, payload.Length);
            return new ReceivedSocketDatagram(result.RemoteEndPoint, payload);
        }

        // Broker UDP fan-out 은 PUBLISH command header 를 건너뛴 payload slice 를 그대로 TrySendTo 에 넘긴다.
        // RIO SendEx 가 buffer id 기준 offset 을 무시하면 raw echo 는 통과해도 broker publish payload 가 subscriber 에 도착하지 않는다.
        [Fact]
        public async Task UdpEcho_WhenHandlerQueuesResponseSlice_ClientReceivesOnlySlice()
        {
            if (!IsRioDatagramAvailable())
                return;

            using (RioTransport transport = new RioTransport())
            {
                SlicingEchoingDatagramHandler datagramHandler = new SlicingEchoingDatagramHandler(transport, offset: 2, length: 3);
                transport.SetDatagramHandler(datagramHandler);
                await transport.StartAsync();

                IUdpEndpoint? endpoint = null;
                Socket? client = null;

                try
                {
                    endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(endpoint.LocalEndPoint);

                    client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    client.Bind(new IPEndPoint(IPAddress.Loopback, 0));

                    byte[] payload = new byte[] { 61, 62, 63, 64, 65, 66 };
                    int sent = await client.SendToAsync(new ArraySegment<byte>(payload), SocketFlags.None, boundEndPoint);
                    Assert.Equal(payload.Length, sent);

                    ReceivedSocketDatagram echoed = await ReceiveUdpDatagramAsync(client, 3);

                    Assert.Equal(new byte[] { 63, 64, 65 }, echoed.Payload);
                    Assert.Equal(boundEndPoint, echoed.RemoteEndPoint);
                }
                finally
                {
                    client?.Dispose();
                    endpoint?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // UDP broker 는 SUBSCRIBE remote 와 PUBLISH remote 가 서로 달라도 subscriber remote 로 fan-out 해야 한다.
        // 같은 remote echo 만 검증하면 RIO send path 가 이전 remote target 을 안정적으로 사용할 수 있는지 놓칠 수 있다.
        [Fact]
        public async Task UdpSendTo_WhenSecondRemoteTriggersSendToFirstRemote_FirstRemoteReceivesSlice()
        {
            if (!IsRioDatagramAvailable())
                return;

            using (RioTransport transport = new RioTransport())
            {
                TwoRemoteFanoutDatagramHandler datagramHandler = new TwoRemoteFanoutDatagramHandler(transport, offset: 1, length: 3);
                transport.SetDatagramHandler(datagramHandler);
                await transport.StartAsync();

                IUdpEndpoint? endpoint = null;
                Socket? firstClient = null;
                Socket? secondClient = null;

                try
                {
                    endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(endpoint.LocalEndPoint);

                    firstClient = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    firstClient.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    secondClient = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    secondClient.Bind(new IPEndPoint(IPAddress.Loopback, 0));

                    byte[] firstPayload = new byte[] { 70 };
                    int firstSent = await firstClient.SendToAsync(new ArraySegment<byte>(firstPayload), SocketFlags.None, boundEndPoint);
                    Assert.Equal(firstPayload.Length, firstSent);
                    await datagramHandler.FirstRemoteCapturedTask;

                    byte[] secondPayload = new byte[] { 80, 81, 82, 83, 84 };
                    int secondSent = await secondClient.SendToAsync(new ArraySegment<byte>(secondPayload), SocketFlags.None, boundEndPoint);
                    Assert.Equal(secondPayload.Length, secondSent);

                    ReceivedSocketDatagram fanout = await ReceiveUdpDatagramAsync(firstClient, 3);

                    Assert.Equal(new byte[] { 81, 82, 83 }, fanout.Payload);
                    Assert.Equal(boundEndPoint, fanout.RemoteEndPoint);
                }
                finally
                {
                    firstClient?.Dispose();
                    secondClient?.Dispose();
                    endpoint?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // UDP publish command 는 4096B payload 앞에 command envelope 가 붙으므로 실제 datagram 은 4096B를 초과한다.
        // RIO UDP receive block 이 이 크기를 담지 못하면 broker benchmark 에서 endpoint 가 닫히고 fan-out 이 사라진다.
        [Fact]
        public async Task UdpReceive_WhenDatagramExceedsPayloadSizeButFitsBaselineEnvelope_DeliversFullDatagram()
        {
            if (!IsRioDatagramAvailable())
                return;

            using (RioTransport transport = new RioTransport())
            {
                CapturingDatagramHandler datagramHandler = new CapturingDatagramHandler();
                transport.SetDatagramHandler(datagramHandler);
                await transport.StartAsync();

                IUdpEndpoint? endpoint = null;
                Socket? client = null;

                try
                {
                    endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(endpoint.LocalEndPoint);

                    client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    client.Bind(new IPEndPoint(IPAddress.Loopback, 0));

                    byte[] payload = new byte[4224];
                    for (int index = 0; index < payload.Length; index++)
                        payload[index] = (byte)(index & 0xFF);

                    int sent = await client.SendToAsync(new ArraySegment<byte>(payload), SocketFlags.None, boundEndPoint);
                    Assert.Equal(payload.Length, sent);

                    ReceivedDatagram received = await WaitForReceivedDatagramAsync(datagramHandler);

                    Assert.Equal(payload, received.Payload);
                    Assert.Equal(client.LocalEndPoint, received.RemoteEndPoint);
                }
                finally
                {
                    client?.Dispose();
                    endpoint?.Close();
                    await transport.StopAsync();
                }
            }
        }

        private sealed class CapturingDatagramHandler : ITransportDatagramHandler
        {
            private readonly TaskCompletionSource<ReceivedDatagram> _received;
            private readonly TaskCompletionSource<IUdpEndpoint> _closed;

            internal CapturingDatagramHandler()
            {
                _received = new TaskCompletionSource<ReceivedDatagram>(TaskCreationOptions.RunContinuationsAsynchronously);
                _closed = new TaskCompletionSource<IUdpEndpoint>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            internal Task<ReceivedDatagram> ReceivedTask => _received.Task;

            internal Task<IUdpEndpoint> ClosedTask => _closed.Task;

            public void OnDatagramReceived(IUdpEndpoint endpoint, EndPoint remoteEndPoint, RefCountedBuffer datagram)
            {
                // handler 가 받은 최초 참조의 소유권을 테스트가 소비한다.
                // payload 를 복사해 assertion 재료로 남긴 뒤 즉시 Release 하여 pool leak 여부를 receive loop 쪽에서 드러낸다.
                byte[] payload = datagram.Span.Slice(0, datagram.Length).ToArray();
                datagram.Release();
                _received.TrySetResult(new ReceivedDatagram(endpoint, remoteEndPoint, payload));
            }

            public void OnDatagramEndpointClosed(IUdpEndpoint endpoint)
            {
                _closed.TrySetResult(endpoint);
            }
        }

        private sealed class EchoingDatagramHandler : ITransportDatagramHandler
        {
            private readonly RioTransport _transport;

            internal EchoingDatagramHandler(RioTransport transport)
            {
                _transport = transport;
            }

            public void OnDatagramReceived(IUdpEndpoint endpoint, EndPoint remoteEndPoint, RefCountedBuffer datagram)
            {
                // TrySendTo 가 true 를 반환하면 transport queue 가 추가 ref 를 소유한다.
                // handler 는 자신이 받은 guard ref 만 Release 하고, enqueue 실패 시 추가 ref 까지 함께 되돌린다.
                datagram.AddRef();
                TransportSendBuffer sendBuffer = new TransportSendBuffer(datagram, 0, datagram.Length);

                if (_transport.TrySendTo(endpoint, remoteEndPoint, sendBuffer))
                {
                    datagram.Release();
                    return;
                }

                datagram.Release();
                datagram.Release();
            }

            public void OnDatagramEndpointClosed(IUdpEndpoint endpoint)
            {
            }
        }

        private sealed class SlicingEchoingDatagramHandler : ITransportDatagramHandler
        {
            private readonly RioTransport _transport;
            private readonly int _offset;
            private readonly int _length;

            internal SlicingEchoingDatagramHandler(RioTransport transport, int offset, int length)
            {
                _transport = transport;
                _offset = offset;
                _length = length;
            }

            public void OnDatagramReceived(IUdpEndpoint endpoint, EndPoint remoteEndPoint, RefCountedBuffer datagram)
            {
                // TransportSendBuffer 는 ownership 이 아니라 slice metadata 만 바꾸므로,
                // enqueue 전 AddRef/실패 시 Release 규칙은 full echo handler 와 동일하다.
                datagram.AddRef();
                TransportSendBuffer sendBuffer = new TransportSendBuffer(datagram, _offset, _length);

                if (_transport.TrySendTo(endpoint, remoteEndPoint, sendBuffer))
                {
                    datagram.Release();
                    return;
                }

                datagram.Release();
                datagram.Release();
            }

            public void OnDatagramEndpointClosed(IUdpEndpoint endpoint)
            {
            }
        }

        private sealed class TwoRemoteFanoutDatagramHandler : ITransportDatagramHandler
        {
            private readonly RioTransport _transport;
            private readonly int _offset;
            private readonly int _length;
            private readonly object _gate;
            private readonly TaskCompletionSource<bool> _firstRemoteCaptured;
            private EndPoint? _firstRemote;

            internal TwoRemoteFanoutDatagramHandler(RioTransport transport, int offset, int length)
            {
                _transport = transport;
                _offset = offset;
                _length = length;
                _gate = new object();
                _firstRemoteCaptured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            internal Task FirstRemoteCapturedTask => _firstRemoteCaptured.Task;

            public void OnDatagramReceived(IUdpEndpoint endpoint, EndPoint remoteEndPoint, RefCountedBuffer datagram)
            {
                EndPoint? firstRemote;
                lock (_gate)
                {
                    if (_firstRemote == null)
                    {
                        _firstRemote = remoteEndPoint;
                        _firstRemoteCaptured.TrySetResult(true);
                        datagram.Release();
                        return;
                    }

                    firstRemote = _firstRemote;
                }

                // 두 번째 remote 의 datagram payload slice 를 첫 번째 remote 로 보내 broker fan-out 형태를 재현한다.
                datagram.AddRef();
                TransportSendBuffer sendBuffer = new TransportSendBuffer(datagram, _offset, _length);

                if (_transport.TrySendTo(endpoint, firstRemote, sendBuffer))
                {
                    datagram.Release();
                    return;
                }

                datagram.Release();
                datagram.Release();
            }

            public void OnDatagramEndpointClosed(IUdpEndpoint endpoint)
            {
            }
        }

        private sealed class ThrowingAfterReleaseDatagramHandler : ITransportDatagramHandler
        {
            private readonly TaskCompletionSource<IUdpEndpoint> _closed;
            private int _closedCallCount;

            internal ThrowingAfterReleaseDatagramHandler()
            {
                _closed = new TaskCompletionSource<IUdpEndpoint>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            internal Task<IUdpEndpoint> ClosedTask => _closed.Task;

            internal int ClosedCallCount => Volatile.Read(ref _closedCallCount);

            public void OnDatagramReceived(IUdpEndpoint endpoint, EndPoint remoteEndPoint, RefCountedBuffer datagram)
            {
                datagram.Release();
                throw new DatagramHandlerFailureException();
            }

            public void OnDatagramEndpointClosed(IUdpEndpoint endpoint)
            {
                Interlocked.Increment(ref _closedCallCount);
                _closed.TrySetResult(endpoint);
            }
        }

        private sealed class DatagramHandlerFailureException : Exception
        {
        }

        private sealed class BlockingFirstDatagramHandler : ITransportDatagramHandler
        {
            private readonly ManualResetEventSlim _allowFirstDatagramToComplete;
            private readonly TaskCompletionSource<bool> _firstReceived;
            private readonly TaskCompletionSource<bool> _secondReceived;
            private int _receivedCount;

            internal BlockingFirstDatagramHandler()
            {
                _allowFirstDatagramToComplete = new ManualResetEventSlim(false);
                _firstReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _secondReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            internal Task FirstReceivedTask => _firstReceived.Task;

            internal Task SecondReceivedTask => _secondReceived.Task;

            internal int ReceivedCount => Volatile.Read(ref _receivedCount);

            internal void AllowFirstDatagramToComplete()
            {
                _allowFirstDatagramToComplete.Set();
            }

            public void OnDatagramReceived(IUdpEndpoint endpoint, EndPoint remoteEndPoint, RefCountedBuffer datagram)
            {
                int receivedCount = Interlocked.Increment(ref _receivedCount);

                if (receivedCount == 1)
                {
                    _firstReceived.TrySetResult(true);

                    try
                    {
                        if (!_allowFirstDatagramToComplete.Wait(TimeSpan.FromSeconds(5)))
                            throw new TimeoutException("첫 RIO UDP datagram handler 대기 해제가 시간 안에 수행되지 않았다.");
                    }
                    finally
                    {
                        datagram.Release();
                    }

                    return;
                }

                datagram.Release();

                if (receivedCount == 2)
                    _secondReceived.TrySetResult(true);
            }

            public void OnDatagramEndpointClosed(IUdpEndpoint endpoint)
            {
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

        private sealed class ReceivedSocketDatagram
        {
            internal ReceivedSocketDatagram(EndPoint remoteEndPoint, byte[] payload)
            {
                RemoteEndPoint = remoteEndPoint;
                Payload = payload;
            }

            internal EndPoint RemoteEndPoint { get; }

            internal byte[] Payload { get; }
        }
    }
}
