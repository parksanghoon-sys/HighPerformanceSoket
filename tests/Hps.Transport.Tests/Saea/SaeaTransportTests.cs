using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hps.Buffers;
using Hps.Transport;
using Xunit;

namespace Hps.Transport.Tests
{
    public sealed class SaeaTransportTests
    {
        // TCP loopback 기준선 테스트: SAEA 백엔드의 첫 책임은 실제 payload 송수신 전에
        // listener 를 열고 outbound connect 와 inbound accept 로 양쪽 IConnection 을 만들 수 있는지 증명하는 것이다.
        [Fact]
        public async Task ListenConnectAccept_WhenLoopbackTcp_CreatesInboundAndOutboundConnections()
        {
            using (SaeaTransport transport = new SaeaTransport())
            {
                await transport.StartAsync();

                IConnectionListener? listener = null;
                IConnection? outbound = null;
                IConnection? inbound = null;

                try
                {
                    using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    {
                        listener = await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0), timeout.Token);
                        IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(listener.LocalEndPoint);
                        Assert.NotEqual(0, boundEndPoint.Port);

                        ValueTask<IConnection> accept = listener.AcceptAsync(timeout.Token);
                        outbound = await transport.ConnectTcpAsync(boundEndPoint, timeout.Token);
                        inbound = await accept;

                        Assert.NotNull(outbound);
                        Assert.NotNull(inbound);
                        Assert.NotSame(outbound, inbound);
                    }
                }
                finally
                {
                    outbound?.Close();
                    inbound?.Close();
                    listener?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // SAEA도 registration과 pump task 생성을 분리하면 direct transport Stop 뒤 receive/send pump가 늦게 시작될 수 있다.
        // pump 시작 delegate가 transport lock 안에서 막힌 동안 Stop이 완료되지 않아야 close 뒤 pool 재대여 경계를 제거할 수 있다.
        [Fact]
        public async Task RegisterConnection_WhenPumpStartupIsBlocked_SerializesStopUntilStartupCompletes()
        {
            using (SaeaTransport transport = new SaeaTransport())
            using (ManualResetEventSlim pumpStartupEntered = new ManualResetEventSlim(false))
            using (ManualResetEventSlim releasePumpStartup = new ManualResetEventSlim(false))
            {
                await transport.StartAsync();
                TransportConnection connection = new TransportConnection();
                MethodInfo? registerConnection = FindRegisterConnectionWithPumpStartup();
                Assert.NotNull(registerConnection);

                Task registrationTask = Task.Run(delegate()
                {
                    Action startPumps = delegate()
                    {
                        pumpStartupEntered.Set();
                        if (!releasePumpStartup.Wait(TimeSpan.FromSeconds(5)))
                            throw new TimeoutException("SAEA pump 시작 차단을 제한 시간 안에 해제하지 못했습니다.");
                    };

                    registerConnection!.Invoke(transport, new object[] { connection, startPumps });
                });

                try
                {
                    Assert.True(
                        pumpStartupEntered.Wait(TimeSpan.FromSeconds(5)),
                        "SAEA registration이 pump 시작 경계에 진입하지 못했습니다.");

                    Task stopTask = Task.Run(async delegate()
                    {
                        await transport.StopAsync();
                    });

                    await Task.Delay(TimeSpan.FromMilliseconds(100));
                    Assert.False(stopTask.IsCompleted);

                    releasePumpStartup.Set();
                    await registrationTask.WaitAsync(TimeSpan.FromSeconds(5));
                    await stopTask.WaitAsync(TimeSpan.FromSeconds(5));

                    Assert.True(connection.IsClosed);
                    Assert.Empty(GetEndpointSnapshots(transport));
                }
                finally
                {
                    releasePumpStartup.Set();
                    connection.Close();
                }
            }
        }

        // endpoint snapshot collection 테스트: Interface Server 운영 표면은 Transport 가 현재 보유한 TCP/UDP endpoint 를
        // connection 객체나 socket 참조 없이 값 snapshot 으로 읽을 수 있어야 한다. 닫힌 endpoint 는 tracking 목록에서 빠져야 한다.
        [Fact]
        public async Task GetEndpointSnapshots_WhenTcpAndUdpEndpointsAreOpen_ReturnsActiveEndpointSnapshots()
        {
            using (SaeaTransport transport = new SaeaTransport())
            {
                await transport.StartAsync();

                IConnectionListener? listener = null;
                IConnection? outbound = null;
                IConnection? inbound = null;
                IUdpEndpoint? udpEndpoint = null;

                try
                {
                    using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    {
                        listener = await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0), timeout.Token);
                        IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(listener.LocalEndPoint);

                        ValueTask<IConnection> accept = listener.AcceptAsync(timeout.Token);
                        outbound = await transport.ConnectTcpAsync(boundEndPoint, timeout.Token);
                        inbound = await accept;
                        udpEndpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0), timeout.Token);

                        EndpointSnapshot[] snapshots = GetEndpointSnapshots(transport);

                        Assert.Equal(3, snapshots.Length);
                        Assert.Equal(2, CountSnapshots(snapshots, EndpointTransportKind.Tcp));
                        Assert.Equal(1, CountSnapshots(snapshots, EndpointTransportKind.Udp));
                        AssertUniquePositiveEndpointIds(snapshots);

                        for (int index = 0; index < snapshots.Length; index++)
                        {
                            Assert.Equal(EndpointState.Open, snapshots[index].State);
                            Assert.Equal(0, snapshots[index].PendingSendCount);
                            Assert.Equal(0, snapshots[index].PendingSendQueueHighWatermark);
                            Assert.Equal(0, snapshots[index].DroppedPendingSendCount);
                        }

                        outbound.Close();
                        inbound.Close();
                        udpEndpoint.Close();

                        Assert.Empty(GetEndpointSnapshots(transport));
                    }
                }
                finally
                {
                    udpEndpoint?.Close();
                    outbound?.Close();
                    inbound?.Close();
                    listener?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // TCP recv pump 기준선 테스트: Transport 는 아직 프레이밍을 하지 않고 raw byte stream 조각만
        // borrowed receive buffer 로 전달해야 한다. 이 테스트는 실제 socket 에서 들어온 바이트가 handler 까지 도달하는지 고정한다.
        [Fact]
        public async Task ReceivePump_WhenRawClientSendsBytes_DeliversBorrowedChunkToHandler()
        {
            using (SaeaTransport transport = new SaeaTransport())
            {
                CapturingReceiveHandler receiveHandler = new CapturingReceiveHandler();
                transport.SetReceiveHandler(receiveHandler);
                await transport.StartAsync();

                IConnectionListener? listener = null;
                IConnection? inbound = null;
                Socket? client = null;

                try
                {
                    listener = await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(listener.LocalEndPoint);

                    client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    client.NoDelay = true;

                    ValueTask<IConnection> accept = listener.AcceptAsync();
                    await client.ConnectAsync(boundEndPoint);
                    inbound = await accept;

                    byte[] payload = new byte[] { 10, 20, 30, 40 };
                    int sent = await client.SendAsync(new ArraySegment<byte>(payload), SocketFlags.None);
                    Assert.Equal(payload.Length, sent);

                    ReceivedPayload received = await WaitForReceivedPayloadAsync(receiveHandler.ReceivedTask);

                    Assert.Same(inbound, received.Connection);
                    Assert.Equal(payload, received.Payload);
                }
                finally
                {
                    client?.Dispose();
                    inbound?.Close();
                    listener?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // TCP handler 예외 정책 테스트: receive handler 내부 버그가 발생해도 background receive loop 가
        // fault 상태로 숨어 죽지 않고 connection close 알림으로 수렴해야 Broker 구독 cleanup 이 실행된다.
        [Fact]
        public async Task ReceivePump_WhenHandlerThrows_ClosesConnectionAndNotifiesHandler()
        {
            using (SaeaTransport transport = new SaeaTransport())
            {
                ThrowingReceiveHandler receiveHandler = new ThrowingReceiveHandler();
                transport.SetReceiveHandler(receiveHandler);
                await transport.StartAsync();

                IConnectionListener? listener = null;
                IConnection? inbound = null;
                Socket? client = null;

                try
                {
                    listener = await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(listener.LocalEndPoint);

                    client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    client.NoDelay = true;

                    ValueTask<IConnection> accept = listener.AcceptAsync();
                    await client.ConnectAsync(boundEndPoint);
                    inbound = await accept;

                    byte[] payload = new byte[] { 41, 42, 43 };
                    int sent = await client.SendAsync(new ArraySegment<byte>(payload), SocketFlags.None);
                    Assert.Equal(payload.Length, sent);

                    IConnection closedConnection = await WaitForClosedConnectionAsync(receiveHandler.ClosedTask);

                    Assert.Same(inbound, closedConnection);
                    Assert.Equal(1, receiveHandler.ClosedCallCount);
                    Assert.Equal(0, GetTrackedConnectionCount(transport));
                }
                finally
                {
                    client?.Dispose();
                    inbound?.Close();
                    listener?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // TCP echo 통합 테스트: recv handler 가 받은 borrowed chunk 를 자신의 RefCountedBuffer 로 복사한 뒤
        // 같은 connection 에 TrySend 하면 receive pump 와 send pump 가 함께 동작해 실제 socket 왕복이 완성되어야 한다.
        [Fact]
        public async Task TcpEcho_WhenReceiveHandlerQueuesResponse_ClientReceivesSamePayload()
        {
            PinnedBlockMemoryPool echoPool = new PinnedBlockMemoryPool(32);

            using (SaeaTransport transport = new SaeaTransport())
            {
                EchoingReceiveHandler receiveHandler = new EchoingReceiveHandler(transport, echoPool);
                transport.SetReceiveHandler(receiveHandler);
                await transport.StartAsync();

                IConnectionListener? listener = null;
                IConnection? inbound = null;
                Socket? client = null;

                try
                {
                    listener = await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(listener.LocalEndPoint);

                    client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    client.NoDelay = true;

                    ValueTask<IConnection> accept = listener.AcceptAsync();
                    await client.ConnectAsync(boundEndPoint);
                    inbound = await accept;

                    byte[] payload = new byte[] { 61, 62, 63, 64, 65 };
                    int sent = await client.SendAsync(new ArraySegment<byte>(payload), SocketFlags.None);
                    Assert.Equal(payload.Length, sent);

                    byte[] echoed = await ReceiveExactAsync(client, payload.Length);

                    Assert.Equal(payload, echoed);
                    await WaitForRentedCountAsync(echoPool, 0);
                }
                finally
                {
                    client?.Dispose();
                    inbound?.Close();
                    listener?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // TCP 동시 연결 echo 기준선 테스트: Phase 2 완료 조건은 단일 연결 echo 뿐 아니라 여러 연결의
        // receive/send pump 가 서로의 pending queue, socket, RefCountedBuffer 반환 경계를 침범하지 않는지도 포함한다.
        [Fact]
        public async Task TcpEcho_WhenMultipleClientsSendConcurrently_EchoesEachPayloadAndReturnsBuffers()
        {
            const int ConnectionCount = 8;
            PinnedBlockMemoryPool echoPool = new PinnedBlockMemoryPool(64);

            using (SaeaTransport transport = new SaeaTransport())
            {
                EchoingReceiveHandler receiveHandler = new EchoingReceiveHandler(transport, echoPool);
                transport.SetReceiveHandler(receiveHandler);
                await transport.StartAsync();

                IConnectionListener? listener = null;
                IConnection?[] inboundConnections = new IConnection?[ConnectionCount];
                Socket?[] clients = new Socket?[ConnectionCount];

                try
                {
                    listener = await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(listener.LocalEndPoint);

                    for (int index = 0; index < ConnectionCount; index++)
                    {
                        Socket client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        client.NoDelay = true;
                        clients[index] = client;

                        ValueTask<IConnection> accept = listener.AcceptAsync();
                        await client.ConnectAsync(boundEndPoint);
                        inboundConnections[index] = await accept;
                    }

                    Assert.Equal(ConnectionCount, GetTrackedConnectionCount(transport));

                    Task[] echoTasks = new Task[ConnectionCount];
                    for (int index = 0; index < ConnectionCount; index++)
                    {
                        Socket client = clients[index]!;
                        byte[] payload = CreateConcurrentEchoPayload(index);
                        echoTasks[index] = SendAndReceiveEchoAsync(client, payload);
                    }

                    await WaitForAllTasksAsync(echoTasks);
                    await WaitForRentedCountAsync(echoPool, 0);

                    for (int index = 0; index < inboundConnections.Length; index++)
                    {
                        inboundConnections[index]?.Close();
                    }

                    Assert.Equal(0, GetTrackedConnectionCount(transport));
                }
                finally
                {
                    for (int index = 0; index < clients.Length; index++)
                    {
                        clients[index]?.Dispose();
                    }

                    for (int index = 0; index < inboundConnections.Length; index++)
                    {
                        inboundConnections[index]?.Close();
                    }

                    listener?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // 연결 수명 회귀 테스트: accepted connection 이 Close 된 뒤 transport 내부 추적 목록에 남으면
        // 단명 연결 churn 환경에서 TransportConnection 과 dispose 된 Socket 참조가 transport 수명 내내 누적된다.
        [Fact]
        public async Task Close_WhenAcceptedConnectionIsClosed_RemovesTransportTrackingReference()
        {
            using (SaeaTransport transport = new SaeaTransport())
            {
                await transport.StartAsync();

                IConnectionListener? listener = null;
                IConnection? inbound = null;
                Socket? client = null;

                try
                {
                    listener = await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(listener.LocalEndPoint);

                    client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    ValueTask<IConnection> accept = listener.AcceptAsync();

                    await client.ConnectAsync(boundEndPoint);
                    inbound = await accept;

                    Assert.Equal(1, GetTrackedConnectionCount(transport));

                    inbound.Close();

                    Assert.Equal(0, GetTrackedConnectionCount(transport));
                }
                finally
                {
                    client?.Dispose();
                    inbound?.Close();
                    listener?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // TCP send pump 기준선 테스트: TrySend 가 accepted connection 의 pending 큐에 넣은 payload 는
        // 실제 socket 으로 전송되고, completion 뒤 Transport 가 소유한 RefCountedBuffer ref 를 반환해야 한다.
        [Fact]
        public async Task SendPump_WhenTrySendAcceptedConnection_SendsRequestedPayloadAndReleasesRef()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            RefCountedBuffer buffer = pool.RentCounted();

            using (SaeaTransport transport = new SaeaTransport())
            {
                await transport.StartAsync();

                IConnectionListener? listener = null;
                IConnection? inbound = null;
                Socket? client = null;

                try
                {
                    listener = await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(listener.LocalEndPoint);

                    client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    client.NoDelay = true;

                    ValueTask<IConnection> accept = listener.AcceptAsync();
                    await client.ConnectAsync(boundEndPoint);
                    inbound = await accept;

                    byte[] expected = new byte[] { 2, 3, 4, 5 };
                    buffer.Span[0] = 99;
                    expected.CopyTo(buffer.Span.Slice(2));
                    buffer.Span[6] = 100;
                    buffer.SetLength(7);
                    buffer.AddRef();

                    TransportSendBuffer sendBuffer = new TransportSendBuffer(buffer, 2, expected.Length);
                    Assert.True(transport.TrySend(inbound, sendBuffer));

                    buffer.Release();

                    byte[] received = await ReceiveExactAsync(client, expected.Length);
                    Assert.Equal(expected, received);

                    await WaitForRentedCountAsync(pool, 0);
                }
                finally
                {
                    client?.Dispose();
                    inbound?.Close();
                    listener?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // UDP receive 기준선 테스트: UDP 는 TCP stream 조각이 아니라 datagram 하나가 메시지 하나다.
        // Transport 는 datagram 을 RefCountedBuffer 로 직접 받아 handler 에 소유권을 넘기고, handler 가 Release 해야 한다.
        [Fact]
        public async Task UdpReceive_WhenSocketSendsDatagram_DeliversOwnedRefCountedBuffer()
        {
            using (SaeaTransport transport = new SaeaTransport())
            {
                CapturingDatagramHandler datagramHandler = new CapturingDatagramHandler();
                transport.SetDatagramHandler(datagramHandler);
                await transport.StartAsync();

                IUdpEndpoint? udpEndpoint = null;
                Socket? client = null;

                try
                {
                    udpEndpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(udpEndpoint.LocalEndPoint);
                    Assert.NotEqual(0, boundEndPoint.Port);

                    client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    client.Bind(new IPEndPoint(IPAddress.Loopback, 0));

                    byte[] payload = new byte[] { 11, 12, 13, 14 };
                    int sent = await client.SendToAsync(new ArraySegment<byte>(payload), SocketFlags.None, boundEndPoint);
                    Assert.Equal(payload.Length, sent);

                    ReceivedDatagram received = await WaitForReceivedDatagramAsync(datagramHandler.ReceivedTask);

                    Assert.Same(udpEndpoint, received.Endpoint);
                    Assert.Equal(payload, received.Payload);
                    Assert.Equal(client.LocalEndPoint, received.RemoteEndPoint);
                }
                finally
                {
                    client?.Dispose();
                    udpEndpoint?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // UDP echo 통합 테스트: UDP handler 가 받은 owned RefCountedBuffer 를 같은 endpoint 의 TrySendTo 로
        // 되돌려 보내면 receive loop, datagram 소유권, UDP send pump 가 함께 동작해 client socket 이 동일 payload 를 받아야 한다.
        [Fact]
        public async Task UdpEcho_WhenDatagramHandlerQueuesResponse_ClientReceivesSamePayload()
        {
            using (SaeaTransport transport = new SaeaTransport())
            {
                EchoingDatagramHandler datagramHandler = new EchoingDatagramHandler(transport);
                transport.SetDatagramHandler(datagramHandler);
                await transport.StartAsync();

                IUdpEndpoint? udpEndpoint = null;
                Socket? client = null;

                try
                {
                    udpEndpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(udpEndpoint.LocalEndPoint);

                    client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    client.Bind(new IPEndPoint(IPAddress.Loopback, 0));

                    byte[] payload = new byte[] { 71, 72, 73, 74 };
                    int sent = await client.SendToAsync(new ArraySegment<byte>(payload), SocketFlags.None, boundEndPoint);
                    Assert.Equal(payload.Length, sent);

                    ReceivedSocketDatagram echoed = await ReceiveUdpDatagramAsync(client, payload.Length);

                    Assert.Equal(payload, echoed.Payload);
                    Assert.Equal(boundEndPoint, echoed.RemoteEndPoint);
                }
                finally
                {
                    client?.Dispose();
                    udpEndpoint?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // UDP handler 예외 정책 테스트: handler 가 소유권을 받은 datagram 을 Release 한 뒤 예외를 던져도
        // receive loop 가 task fault 로 숨어 죽지 말고 endpoint close 알림과 정상 종료로 수렴해야 한다.
        [Fact]
        public async Task UdpReceive_WhenHandlerThrowsAfterTakingOwnership_ClosesEndpointAndNotifiesHandler()
        {
            using (SaeaTransport transport = new SaeaTransport())
            {
                ThrowingAfterReleaseDatagramHandler datagramHandler = new ThrowingAfterReleaseDatagramHandler();
                transport.SetDatagramHandler(datagramHandler);

                Socket? receiveSocket = null;
                SaeaUdpEndpoint? udpEndpoint = null;
                Socket? sender = null;

                try
                {
                    receiveSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    receiveSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    udpEndpoint = new SaeaUdpEndpoint(transport, receiveSocket);
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(udpEndpoint.LocalEndPoint);

                    Task receiveLoop = InvokeUdpReceiveLoop(transport, udpEndpoint);

                    sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    byte[] payload = new byte[] { 31, 32, 33 };
                    int sent = await sender.SendToAsync(new ArraySegment<byte>(payload), SocketFlags.None, boundEndPoint);
                    Assert.Equal(payload.Length, sent);

                    IUdpEndpoint closedEndpoint = await WaitForClosedUdpEndpointAsync(datagramHandler.ClosedTask);
                    await WaitForAllTasksAsync(new Task[] { receiveLoop });

                    Assert.Same(udpEndpoint, closedEndpoint);
                    Assert.True(udpEndpoint.IsClosed);
                    Assert.Equal(1, datagramHandler.ClosedCallCount);
                }
                finally
                {
                    sender?.Dispose();
                    udpEndpoint?.Close();
                    receiveSocket?.Dispose();
                }
            }
        }

        // UDP receive backpressure 경계 테스트: SAEA 기준선은 handler 를 동기적으로 호출하므로,
        // handler 가 첫 datagram 을 처리 중이면 receive loop 는 다음 RentCounted 로 넘어가면 안 된다.
        // 이 불변식이 깨지면 느린 fan-out 에서 Transport 내부 prefetch 로 pool 대여 수가 누적될 수 있다.
        [Fact]
        public async Task UdpReceive_WhenHandlerIsBlocked_DoesNotPrefetchAdditionalDatagrams()
        {
            using (SaeaTransport transport = new SaeaTransport())
            {
                BlockingFirstDatagramHandler datagramHandler = new BlockingFirstDatagramHandler();
                transport.SetDatagramHandler(datagramHandler);
                await transport.StartAsync();

                IUdpEndpoint? udpEndpoint = null;
                Socket? sender = null;

                try
                {
                    udpEndpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(udpEndpoint.LocalEndPoint);
                    PinnedBlockMemoryPool receivePool = GetReceivePool(transport);

                    sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                    byte[] firstPayload = new byte[] { 61 };
                    int firstSent = await sender.SendToAsync(new ArraySegment<byte>(firstPayload), SocketFlags.None, boundEndPoint);
                    Assert.Equal(firstPayload.Length, firstSent);

                    await WaitForSignalAsync(datagramHandler.FirstReceivedTask);
                    Assert.Equal(1, datagramHandler.ReceivedCount);
                    Assert.Equal(1, receivePool.RentedCount);

                    byte[] secondPayload = new byte[] { 62 };
                    int secondSent = await sender.SendToAsync(new ArraySegment<byte>(secondPayload), SocketFlags.None, boundEndPoint);
                    Assert.Equal(secondPayload.Length, secondSent);

                    await Task.Delay(TimeSpan.FromMilliseconds(150));

                    Assert.Equal(1, datagramHandler.ReceivedCount);
                    Assert.Equal(1, receivePool.RentedCount);

                    datagramHandler.AllowFirstDatagramToComplete();

                    await WaitForSignalAsync(datagramHandler.SecondReceivedTask);
                    Assert.Equal(2, datagramHandler.ReceivedCount);
                    Assert.Equal(1, receivePool.RentedCount);

                    udpEndpoint.Close();
                    udpEndpoint = null;
                    await WaitForRentedCountAsync(receivePool, 0);
                }
                finally
                {
                    datagramHandler.AllowFirstDatagramToComplete();
                    sender?.Dispose();
                    udpEndpoint?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // UDP send 기준선 테스트: TrySendTo 가 true 를 반환하면 Transport 가 datagram ref 를 소유하고
        // 실제 UDP socket send 이후 완료 경로에서 Release 해야 한다. 전송 범위는 TransportSendBuffer 의 offset/length 로 제한한다.
        [Fact]
        public async Task UdpSendTo_WhenTrySendToBoundEndpoint_SendsRequestedDatagramAndReleasesRef()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            RefCountedBuffer buffer = pool.RentCounted();

            using (SaeaTransport transport = new SaeaTransport())
            {
                await transport.StartAsync();

                IUdpEndpoint? udpEndpoint = null;
                Socket? receiver = null;

                try
                {
                    udpEndpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(udpEndpoint.LocalEndPoint);

                    receiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    receiver.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    IPEndPoint receiverEndPoint = Assert.IsType<IPEndPoint>(receiver.LocalEndPoint);

                    byte[] expected = new byte[] { 21, 22, 23 };
                    buffer.Span[0] = 55;
                    expected.CopyTo(buffer.Span.Slice(4));
                    buffer.Span[7] = 56;
                    buffer.SetLength(8);
                    buffer.AddRef();

                    TransportSendBuffer sendBuffer = new TransportSendBuffer(buffer, 4, expected.Length);
                    Assert.True(transport.TrySendTo(udpEndpoint, receiverEndPoint, sendBuffer));

                    buffer.Release();

                    ReceivedSocketDatagram received = await ReceiveUdpDatagramAsync(receiver, expected.Length);
                    Assert.Equal(expected, received.Payload);
                    Assert.Equal(boundEndPoint, received.RemoteEndPoint);

                    await WaitForRentedCountAsync(pool, 0);
                }
                finally
                {
                    receiver?.Dispose();
                    udpEndpoint?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // UDP send 직렬화 회귀 테스트: TrySendTo 는 datagram 마다 독립 작업을 만들지 않고
        // endpoint 단위 pending queue 에 소유권을 넣어야 한다. pump 가 보내기 전에 endpoint 가 닫히면 queued ref 를 drain 해야 누수가 없다.
        [Fact]
        public void UdpSendTo_WhenEndpointClosesBeforePumpSends_DrainsQueuedDatagramRef()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            RefCountedBuffer buffer = pool.RentCounted();
            SaeaUdpEndpoint? udpEndpoint = null;

            using (SaeaTransport transport = new SaeaTransport())
            {
                Socket? socket = null;

                try
                {
                    socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    udpEndpoint = new SaeaUdpEndpoint(transport, socket);
                    socket = null;

                    byte[] payload = new byte[] { 41, 42, 43 };
                    payload.CopyTo(buffer.Span);
                    buffer.SetLength(payload.Length);
                    buffer.AddRef();

                    TransportSendBuffer sendBuffer = new TransportSendBuffer(buffer, 0, payload.Length);
                    Assert.True(transport.TrySendTo(udpEndpoint, new IPEndPoint(IPAddress.Loopback, 9), sendBuffer));
                    Assert.Equal(1, udpEndpoint.PendingSendCount);

                    buffer.Release();
                    Assert.Equal(1, pool.RentedCount);

                    udpEndpoint.Close();

                    Assert.Equal(0, udpEndpoint.PendingSendCount);
                    Assert.Equal(0, pool.RentedCount);
                }
                finally
                {
                    udpEndpoint?.Close();
                    socket?.Dispose();
                }
            }
        }

        // UDP pending queue 백프레셔 테스트: endpoint pump 가 아직 datagram 을 가져가지 못한 상태에서
        // capacity 를 초과하면 가장 오래된 Transport 소유 ref 를 drop 하고 새 datagram 을 수락해야 한다.
        [Fact]
        public void UdpSendTo_WhenPendingQueueExceedsCapacity_DropsOldestAndReleasesEvictedRef()
        {
            const int Capacity = 16;
            const int SendCount = Capacity + 1;

            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            RefCountedBuffer[] buffers = RentNumberedUdpBuffers(pool, SendCount);
            bool publisherRefsReleased = false;
            SaeaUdpEndpoint? udpEndpoint = null;

            using (SaeaTransport transport = new SaeaTransport())
            {
                Socket? socket = null;

                try
                {
                    socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    udpEndpoint = new SaeaUdpEndpoint(transport, socket);
                    socket = null;

                    EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 9);
                    for (int index = 0; index < SendCount; index++)
                    {
                        buffers[index].AddRef();
                        TransportSendBuffer sendBuffer = new TransportSendBuffer(buffers[index], 0, buffers[index].Length);

                        Assert.True(transport.TrySendTo(udpEndpoint, remoteEndPoint, sendBuffer));
                    }

                    Assert.Equal(Capacity, udpEndpoint.PendingSendCount);

                    ReleasePublisherRefs(buffers);
                    publisherRefsReleased = true;

                    Assert.Equal(Capacity, pool.RentedCount);

                    for (int index = 1; index < SendCount; index++)
                    {
                        SaeaUdpEndpoint.UdpSendRequest sendRequest;
                        Assert.True(udpEndpoint.TryBeginSend(out sendRequest));

                        try
                        {
                            Assert.Same(buffers[index], sendRequest.SendBuffer.Buffer);
                        }
                        finally
                        {
                            sendRequest.SendBuffer.Buffer.Release();
                        }
                    }

                    Assert.False(udpEndpoint.TryBeginSend(out _));
                    Assert.Equal(0, udpEndpoint.PendingSendCount);
                    Assert.Equal(0, pool.RentedCount);
                }
                finally
                {
                    if (!publisherRefsReleased)
                        ReleasePublisherRefs(buffers);

                    udpEndpoint?.Close();
                    socket?.Dispose();
                }
            }
        }

        // UDP close-drain 회귀 테스트: overflow 에서 이미 evict 된 ref 는 close 가 다시 볼 수 없어야 한다.
        // publisher guard ref 를 해제한 뒤에는 queue 에 남은 16개만 endpoint close 경로에서 반환되어야 한다.
        [Fact]
        public void UdpSendTo_WhenPendingQueueAlreadyEvictedOldest_CloseDrainsOnlyRemainingPendingRefs()
        {
            const int Capacity = 16;
            const int SendCount = Capacity + 1;

            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            RefCountedBuffer[] buffers = RentNumberedUdpBuffers(pool, SendCount);
            bool publisherRefsReleased = false;
            SaeaUdpEndpoint? udpEndpoint = null;

            using (SaeaTransport transport = new SaeaTransport())
            {
                Socket? socket = null;

                try
                {
                    socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    udpEndpoint = new SaeaUdpEndpoint(transport, socket);
                    socket = null;

                    EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 9);
                    for (int index = 0; index < SendCount; index++)
                    {
                        buffers[index].AddRef();
                        TransportSendBuffer sendBuffer = new TransportSendBuffer(buffers[index], 0, buffers[index].Length);

                        Assert.True(transport.TrySendTo(udpEndpoint, remoteEndPoint, sendBuffer));
                    }

                    Assert.Equal(Capacity, udpEndpoint.PendingSendCount);

                    ReleasePublisherRefs(buffers);
                    publisherRefsReleased = true;

                    Assert.Equal(Capacity, pool.RentedCount);

                    udpEndpoint.Close();

                    Assert.Equal(0, udpEndpoint.PendingSendCount);
                    Assert.Equal(0, pool.RentedCount);
                }
                finally
                {
                    if (!publisherRefsReleased)
                        ReleasePublisherRefs(buffers);

                    udpEndpoint?.Close();
                    socket?.Dispose();
                }
            }
        }

        // UDP drop 관측성 테스트: UDP send queue 도 drop-oldest 로 datagram 을 버릴 수 있으므로
        // endpoint 단위 counter 가 증가해야 느린 remote 또는 막힌 socket 을 테스트와 운영 진단에서 식별할 수 있다.
        [Fact]
        public void UdpSendTo_WhenPendingQueueDropsOldest_IncrementsDroppedPendingSendCount()
        {
            const int Capacity = 16;
            const int SendCount = Capacity + 2;

            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            RefCountedBuffer[] buffers = RentNumberedUdpBuffers(pool, SendCount);
            bool publisherRefsReleased = false;
            SaeaUdpEndpoint? udpEndpoint = null;

            using (SaeaTransport transport = new SaeaTransport())
            {
                Socket? socket = null;

                try
                {
                    socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    udpEndpoint = new SaeaUdpEndpoint(transport, socket);
                    socket = null;

                    EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 9);
                    for (int index = 0; index < SendCount; index++)
                    {
                        buffers[index].AddRef();
                        TransportSendBuffer sendBuffer = new TransportSendBuffer(buffers[index], 0, buffers[index].Length);

                        Assert.True(transport.TrySendTo(udpEndpoint, remoteEndPoint, sendBuffer));
                    }

                    Assert.Equal(2, udpEndpoint.DroppedPendingSendCount);

                    ReleasePublisherRefs(buffers);
                    publisherRefsReleased = true;

                    udpEndpoint.Close();

                    Assert.Equal(0, pool.RentedCount);
                }
                finally
                {
                    if (!publisherRefsReleased)
                        ReleasePublisherRefs(buffers);

                    udpEndpoint?.Close();
                    socket?.Dispose();
                }
            }
        }

        // UDP send backlog 관측성 테스트. endpoint pending queue 가 drop 전까지 차오른 깊이를
        // Transport 수명 snapshot 에 남겨 막힌 remote 로 인한 send-side 정체를 설명할 수 있어야 한다.
        [Fact]
        public void UdpSendTo_WhenPendingQueueGrows_UpdatesTransportPendingSendQueueHighWatermark()
        {
            const int SendCount = 5;

            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            RefCountedBuffer[] buffers = RentNumberedUdpBuffers(pool, SendCount);
            bool publisherRefsReleased = false;
            SaeaUdpEndpoint? udpEndpoint = null;

            using (SaeaTransport transport = new SaeaTransport())
            {
                ITransportDiagnostics diagnostics = transport;
                Socket? socket = null;

                try
                {
                    socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    udpEndpoint = new SaeaUdpEndpoint(transport, socket);
                    socket = null;

                    EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 9);
                    for (int index = 0; index < buffers.Length; index++)
                    {
                        buffers[index].AddRef();
                        TransportSendBuffer sendBuffer = new TransportSendBuffer(buffers[index], 0, buffers[index].Length);

                        Assert.True(transport.TrySendTo(udpEndpoint, remoteEndPoint, sendBuffer));
                    }

                    TransportDiagnosticsSnapshot snapshot = diagnostics.GetDiagnosticsSnapshot();

                    Assert.Equal(0, snapshot.TcpPendingSendQueueHighWatermark);
                    Assert.Equal(SendCount, snapshot.UdpPendingSendQueueHighWatermark);
                    Assert.Equal(0, snapshot.DroppedPendingSendCount);

                    ReleasePublisherRefs(buffers);
                    publisherRefsReleased = true;

                    udpEndpoint.Close();

                    TransportDiagnosticsSnapshot afterCloseSnapshot = diagnostics.GetDiagnosticsSnapshot();
                    Assert.Equal(SendCount, afterCloseSnapshot.UdpPendingSendQueueHighWatermark);
                    Assert.Equal(0, pool.RentedCount);
                }
                finally
                {
                    if (!publisherRefsReleased)
                        ReleasePublisherRefs(buffers);

                    udpEndpoint?.Close();
                    socket?.Dispose();
                }
            }
        }

        // UDP public 진단 누적 테스트: endpoint 내부 counter 만으로는 endpoint close 뒤 drop 이 운영 표면에서 사라진다.
        // Transport diagnostics snapshot 은 막힌 remote 로 인해 버려진 datagram 수를 endpoint 수명과 무관하게 보존해야 한다.
        [Fact]
        public void UdpSendTo_WhenPendingQueueDropsOldest_IncrementsTransportDiagnosticsSnapshot()
        {
            const int Capacity = 16;
            const int SendCount = Capacity + 2;

            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            RefCountedBuffer[] buffers = RentNumberedUdpBuffers(pool, SendCount);
            bool publisherRefsReleased = false;
            SaeaUdpEndpoint? udpEndpoint = null;

            using (SaeaTransport transport = new SaeaTransport())
            {
                ITransportDiagnostics diagnostics = transport;
                Socket? socket = null;

                try
                {
                    socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    udpEndpoint = new SaeaUdpEndpoint(transport, socket);
                    socket = null;

                    EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 9);
                    for (int index = 0; index < SendCount; index++)
                    {
                        buffers[index].AddRef();
                        TransportSendBuffer sendBuffer = new TransportSendBuffer(buffers[index], 0, buffers[index].Length);

                        Assert.True(transport.TrySendTo(udpEndpoint, remoteEndPoint, sendBuffer));
                    }

                    TransportDiagnosticsSnapshot snapshot = diagnostics.GetDiagnosticsSnapshot();

                    Assert.Equal(0, snapshot.TcpDroppedPendingSendCount);
                    Assert.Equal(2, snapshot.UdpDroppedPendingSendCount);
                    Assert.Equal(2, snapshot.DroppedPendingSendCount);

                    ReleasePublisherRefs(buffers);
                    publisherRefsReleased = true;

                    udpEndpoint.Close();

                    TransportDiagnosticsSnapshot afterCloseSnapshot = diagnostics.GetDiagnosticsSnapshot();
                    Assert.Equal(2, afterCloseSnapshot.DroppedPendingSendCount);
                    Assert.Equal(0, pool.RentedCount);
                }
                finally
                {
                    if (!publisherRefsReleased)
                        ReleasePublisherRefs(buffers);

                    udpEndpoint?.Close();
                    socket?.Dispose();
                }
            }
        }

        private static async Task<ReceivedPayload> WaitForReceivedPayloadAsync(Task<ReceivedPayload> receivedTask)
        {
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            Task completedTask = await Task.WhenAny(receivedTask, timeoutTask);

            Assert.Same(receivedTask, completedTask);
            return await receivedTask;
        }

        private static async Task<byte[]> ReceiveExactAsync(Socket socket, int length)
        {
            Task<byte[]> receiveTask = ReceiveExactCoreAsync(socket, length);
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            Task completedTask = await Task.WhenAny(receiveTask, timeoutTask);

            Assert.Same(receiveTask, completedTask);
            return await receiveTask;
        }

        private static async Task SendAndReceiveEchoAsync(Socket socket, byte[] payload)
        {
            int sent = await socket.SendAsync(new ArraySegment<byte>(payload), SocketFlags.None);
            Assert.Equal(payload.Length, sent);

            byte[] echoed = await ReceiveExactAsync(socket, payload.Length);
            Assert.Equal(payload, echoed);
        }

        private static async Task WaitForAllTasksAsync(Task[] tasks)
        {
            Task allTasks = Task.WhenAll(tasks);
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            Task completedTask = await Task.WhenAny(allTasks, timeoutTask);

            Assert.Same(allTasks, completedTask);
            await allTasks;
        }

        private static async Task WaitForSignalAsync(Task signalTask)
        {
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            Task completedTask = await Task.WhenAny(signalTask, timeoutTask);

            Assert.Same(signalTask, completedTask);
            await signalTask;
        }

        private static byte[] CreateConcurrentEchoPayload(int index)
        {
            // 연결별 payload 를 다르게 만들어 echo 가 다른 connection 의 응답과 섞이는 회귀를 눈에 띄게 한다.
            return new byte[]
            {
                (byte)(80 + index),
                (byte)(90 + index),
                (byte)(100 + index),
                (byte)(110 + index)
            };
        }

        private static async Task<ReceivedDatagram> WaitForReceivedDatagramAsync(Task<ReceivedDatagram> receivedTask)
        {
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            Task completedTask = await Task.WhenAny(receivedTask, timeoutTask);

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

        private static async Task<IConnection> WaitForClosedConnectionAsync(Task<IConnection> closedTask)
        {
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            Task completedTask = await Task.WhenAny(closedTask, timeoutTask);

            Assert.Same(closedTask, completedTask);
            return await closedTask;
        }

        private static async Task<ReceivedSocketDatagram> ReceiveUdpDatagramAsync(Socket socket, int maxLength)
        {
            Task<ReceivedSocketDatagram> receiveTask = ReceiveUdpDatagramCoreAsync(socket, maxLength);
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            Task completedTask = await Task.WhenAny(receiveTask, timeoutTask);

            Assert.Same(receiveTask, completedTask);
            return await receiveTask;
        }

        private static async Task<ReceivedSocketDatagram> ReceiveUdpDatagramCoreAsync(Socket socket, int maxLength)
        {
            byte[] receiveBuffer = new byte[maxLength];
            EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            SocketReceiveFromResult result = await socket.ReceiveFromAsync(new ArraySegment<byte>(receiveBuffer), SocketFlags.None, remoteEndPoint);
            byte[] payload = new byte[result.ReceivedBytes];
            Array.Copy(receiveBuffer, payload, payload.Length);

            return new ReceivedSocketDatagram(result.RemoteEndPoint, payload);
        }

        private static Task InvokeUdpReceiveLoop(SaeaTransport transport, SaeaUdpEndpoint udpEndpoint)
        {
            MethodInfo? method = typeof(SaeaTransport).GetMethod("UdpReceiveLoopAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            object? result = method!.Invoke(transport, new object[] { udpEndpoint });
            return Assert.IsAssignableFrom<Task>(result);
        }

        private static MethodInfo? FindRegisterConnectionWithPumpStartup()
        {
            MethodInfo[] methods = typeof(SaeaTransport).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                if (!string.Equals(method.Name, "RegisterConnection", StringComparison.Ordinal))
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 2 && parameters[1].ParameterType == typeof(Action))
                    return method;
            }

            return null;
        }

        private static EndpointSnapshot[] GetEndpointSnapshots(SaeaTransport transport)
        {
            Type? endpointDiagnosticsType = Type.GetType("Hps.Transport.ITransportEndpointDiagnostics, Hps.Transport");
            Assert.NotNull(endpointDiagnosticsType);
            Assert.True(endpointDiagnosticsType!.IsAssignableFrom(typeof(SaeaTransport)));

            MethodInfo? method = endpointDiagnosticsType.GetMethod("GetEndpointSnapshots", Type.EmptyTypes);
            Assert.NotNull(method);

            object? result = method!.Invoke(transport, null);
            return Assert.IsType<EndpointSnapshot[]>(result);
        }

        private static int CountSnapshots(EndpointSnapshot[] snapshots, EndpointTransportKind transportKind)
        {
            int count = 0;

            for (int index = 0; index < snapshots.Length; index++)
            {
                if (snapshots[index].TransportKind == transportKind)
                    count++;
            }

            return count;
        }

        private static void AssertUniquePositiveEndpointIds(EndpointSnapshot[] snapshots)
        {
            for (int index = 0; index < snapshots.Length; index++)
            {
                Assert.True(snapshots[index].Id.Value > 0);

                for (int compareIndex = index + 1; compareIndex < snapshots.Length; compareIndex++)
                {
                    Assert.NotEqual(snapshots[index].Id, snapshots[compareIndex].Id);
                }
            }
        }

        private static async Task<byte[]> ReceiveExactCoreAsync(Socket socket, int length)
        {
            byte[] received = new byte[length];
            int offset = 0;

            while (offset < length)
            {
                int count = await socket.ReceiveAsync(new ArraySegment<byte>(received, offset, length - offset), SocketFlags.None);
                if (count == 0)
                    throw new InvalidOperationException("송신 pump 검증 중 원격 연결이 먼저 닫혔다.");

                offset += count;
            }

            return received;
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

        private static int GetTrackedConnectionCount(SaeaTransport transport)
        {
            // 이 테스트는 public contract 가 아니라 transport 내부 수명 추적 누수를 보호한다.
            // 별도 진단 API를 만들지 않기 위해 private 목록만 white-box 로 읽고, 생산 코드 경계는 건드리지 않는다.
            FieldInfo? field = typeof(SaeaTransport).GetField("_connections", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);

            ICollection? connections = Assert.IsAssignableFrom<ICollection>(field!.GetValue(transport));
            return connections.Count;
        }

        private static PinnedBlockMemoryPool GetReceivePool(SaeaTransport transport)
        {
            // UDP receive prefetch 여부는 public API가 아니라 SAEA 기준선 내부 pool 대여 경계다.
            // 별도 진단 API를 추가하지 않고 현재 단위의 회귀 테스트에서만 private pool 을 읽는다.
            FieldInfo? field = typeof(SaeaTransport).GetField("_receivePool", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);

            return Assert.IsType<PinnedBlockMemoryPool>(field!.GetValue(transport));
        }

        private sealed class CapturingReceiveHandler : ITransportReceiveHandler
        {
            private readonly TaskCompletionSource<ReceivedPayload> _received;

            internal CapturingReceiveHandler()
            {
                _received = new TaskCompletionSource<ReceivedPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            internal Task<ReceivedPayload> ReceivedTask => _received.Task;

            public void OnReceived(IConnection connection, TransportReceiveBuffer receiveBuffer)
            {
                // TransportReceiveBuffer 는 콜백 동안만 유효하므로 테스트도 즉시 복사해 이후 단언에 사용한다.
                _received.TrySetResult(new ReceivedPayload(connection, receiveBuffer.Span.ToArray()));
            }

            public void OnConnectionClosed(IConnection connection)
            {
            }
        }

        private sealed class ThrowingReceiveHandler : ITransportReceiveHandler
        {
            private readonly TaskCompletionSource<IConnection> _closed;
            private int _closedCallCount;

            internal ThrowingReceiveHandler()
            {
                _closed = new TaskCompletionSource<IConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            internal Task<IConnection> ClosedTask => _closed.Task;

            internal int ClosedCallCount => Volatile.Read(ref _closedCallCount);

            public void OnReceived(IConnection connection, TransportReceiveBuffer receiveBuffer)
            {
                throw new ReceiveHandlerFailureException();
            }

            public void OnConnectionClosed(IConnection connection)
            {
                Interlocked.Increment(ref _closedCallCount);
                _closed.TrySetResult(connection);
            }
        }

        private sealed class ReceiveHandlerFailureException : Exception
        {
        }

        private sealed class EchoingReceiveHandler : ITransportReceiveHandler
        {
            private readonly SaeaTransport _transport;
            private readonly PinnedBlockMemoryPool _pool;

            internal EchoingReceiveHandler(SaeaTransport transport, PinnedBlockMemoryPool pool)
            {
                _transport = transport;
                _pool = pool;
            }

            public void OnReceived(IConnection connection, TransportReceiveBuffer receiveBuffer)
            {
                // 받은 buffer 는 콜백 동안만 유효하므로, echo 응답은 테스트 전용 counted buffer 로 즉시 복사한다.
                // TrySend 성공 시 Transport 가 추가 ref 하나를 소유하고, 이 handler 는 publish 가드 ref 만 해제한다.
                RefCountedBuffer echo = _pool.RentCounted();
                receiveBuffer.Span.CopyTo(echo.Span);
                echo.SetLength(receiveBuffer.Length);
                echo.AddRef();

                TransportSendBuffer sendBuffer = new TransportSendBuffer(echo, 0, receiveBuffer.Length);
                if (_transport.TrySend(connection, sendBuffer))
                {
                    echo.Release();
                    return;
                }

                echo.Release();
                echo.Release();
            }

            public void OnConnectionClosed(IConnection connection)
            {
            }
        }

        private sealed class CapturingDatagramHandler : ITransportDatagramHandler
        {
            private readonly TaskCompletionSource<ReceivedDatagram> _received;

            internal CapturingDatagramHandler()
            {
                _received = new TaskCompletionSource<ReceivedDatagram>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            internal Task<ReceivedDatagram> ReceivedTask => _received.Task;

            public void OnDatagramReceived(IUdpEndpoint endpoint, EndPoint remoteEndPoint, RefCountedBuffer datagram)
            {
                // UDP datagram 은 handler 가 소유권을 받은 RefCountedBuffer 이므로, 테스트도 payload 를 복사한 뒤 즉시 Release 한다.
                byte[] payload = datagram.Span.Slice(0, datagram.Length).ToArray();
                datagram.Release();
                _received.TrySetResult(new ReceivedDatagram(endpoint, remoteEndPoint, payload));
            }

            public void OnDatagramEndpointClosed(IUdpEndpoint endpoint)
            {
            }
        }

        private sealed class EchoingDatagramHandler : ITransportDatagramHandler
        {
            private readonly SaeaTransport _transport;

            internal EchoingDatagramHandler(SaeaTransport transport)
            {
                _transport = transport;
            }

            public void OnDatagramReceived(IUdpEndpoint endpoint, EndPoint remoteEndPoint, RefCountedBuffer datagram)
            {
                // handler 는 datagram 의 guard ref 를 소유한다. echo send 를 수락시키려면 Transport 몫 ref 를
                // 먼저 AddRef 한 뒤 TrySendTo 에 넘기고, handler guard ref 는 즉시 Release 해야 한다.
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
                            throw new TimeoutException("첫 UDP datagram handler 대기 해제가 시간 안에 수행되지 않았다.");
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

        private sealed class ReceivedPayload
        {
            internal ReceivedPayload(IConnection connection, byte[] payload)
            {
                Connection = connection;
                Payload = payload;
            }

            internal IConnection Connection { get; }

            internal byte[] Payload { get; }
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
