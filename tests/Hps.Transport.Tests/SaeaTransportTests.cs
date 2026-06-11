using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
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

        private static async Task<ReceivedPayload> WaitForReceivedPayloadAsync(Task<ReceivedPayload> receivedTask)
        {
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            Task completedTask = await Task.WhenAny(receivedTask, timeoutTask);

            Assert.Same(receivedTask, completedTask);
            return await receivedTask;
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
