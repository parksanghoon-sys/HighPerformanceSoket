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
    }
}
