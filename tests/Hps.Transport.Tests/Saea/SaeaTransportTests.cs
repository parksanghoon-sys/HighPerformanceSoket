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

        // UDP handler 예외 회귀 테스트: handler 호출 시점에 datagram 소유권은 이미 handler 로 넘어간 상태여야 한다.
        // public receive API 는 background loop 예외를 노출하지 않으므로, 이 테스트만 private loop 를 직접 실행해
        // handler 가 Release 후 예외를 던져도 Transport 가 같은 RefCountedBuffer 를 두 번째로 Release 하지 않는지 고정한다.
        [Fact]
        public async Task UdpReceive_WhenHandlerThrowsAfterTakingOwnership_DoesNotReleaseDatagramAgain()
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

                    Exception exception = await WaitForTaskExceptionAsync(receiveLoop);
                    Assert.IsType<DatagramHandlerFailureException>(exception);
                }
                finally
                {
                    sender?.Dispose();
                    udpEndpoint?.Close();
                    receiveSocket?.Dispose();
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

        private static async Task<ReceivedDatagram> WaitForReceivedDatagramAsync(Task<ReceivedDatagram> receivedTask)
        {
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            Task completedTask = await Task.WhenAny(receivedTask, timeoutTask);

            Assert.Same(receivedTask, completedTask);
            return await receivedTask;
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

        private static async Task<Exception> WaitForTaskExceptionAsync(Task task)
        {
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            Task completedTask = await Task.WhenAny(task, timeoutTask);

            Assert.Same(task, completedTask);
            return await Assert.ThrowsAnyAsync<Exception>(async () => await task);
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

        private static int GetTrackedConnectionCount(SaeaTransport transport)
        {
            // 이 테스트는 public contract 가 아니라 transport 내부 수명 추적 누수를 보호한다.
            // 별도 진단 API를 만들지 않기 위해 private 목록만 white-box 로 읽고, 생산 코드 경계는 건드리지 않는다.
            FieldInfo? field = typeof(SaeaTransport).GetField("_connections", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);

            ICollection? connections = Assert.IsAssignableFrom<ICollection>(field!.GetValue(transport));
            return connections.Count;
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

        private sealed class ThrowingAfterReleaseDatagramHandler : ITransportDatagramHandler
        {
            public void OnDatagramReceived(IUdpEndpoint endpoint, EndPoint remoteEndPoint, RefCountedBuffer datagram)
            {
                datagram.Release();
                throw new DatagramHandlerFailureException();
            }

            public void OnDatagramEndpointClosed(IUdpEndpoint endpoint)
            {
            }
        }

        private sealed class DatagramHandlerFailureException : Exception
        {
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
