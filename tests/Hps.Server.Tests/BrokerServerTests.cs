using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hps.Broker;
using Hps.Buffers;
using Hps.Protocol;
using Hps.Server;
using Hps.Transport;
using Xunit;

namespace Hps.Server.Tests
{
    public sealed class BrokerServerTests
    {
        // 서버 host 첫 계약 테스트: Phase 3의 Protocol/Broker 구현은 이미 존재하므로,
        // 서버 계층은 새 protocol 을 만들지 않고 Transport 에 TCP frame receive handler 를 꽂는 명시적 진입점을 제공해야 한다.
        [Fact]
        public void BrokerServerContract_WhenInspected_ExposesMinimalTcpHostWiringApi()
        {
            Type? serverType = Type.GetType("Hps.Server.BrokerServer, Hps.Server", throwOnError: false);

            Assert.NotNull(serverType);
            Assert.Contains(serverType!.GetConstructors(), HasExpectedConstructor);
            Assert.NotNull(serverType.GetProperty("LocalEndPoint", BindingFlags.Instance | BindingFlags.Public));
            Assert.Equal(typeof(ValueTask), GetPublicMethod(serverType, "StartTcpAsync").ReturnType);
            Assert.Equal(typeof(ValueTask), GetPublicMethod(serverType, "StopAsync").ReturnType);
            Assert.True(typeof(IDisposable).IsAssignableFrom(serverType));
        }

        // 서버 wiring 테스트: Server 계층은 Protocol/Broker 를 우회한 별도 흐름을 만들지 않고,
        // Transport 에 TcpFrameReceiveHandler 를 등록한 뒤 listen 과 accept 대기를 시작해야 실제 TCP command 가 들어올 수 있다.
        [Fact]
        public async Task StartTcpAsync_WhenCalled_RegistersFrameReceiveHandlerStartsTransportAndBeginsAccept()
        {
            FakeTransport transport = new FakeTransport();
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            using (BrokerServer server = new BrokerServer(transport, pool, 64))
            {
                await server.StartTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));

                Assert.Equal(1, transport.SetReceiveHandlerCallCount);
                Assert.IsType<TcpFrameReceiveHandler>(transport.ReceiveHandler);
                Assert.Equal(1, transport.StartCallCount);
                Assert.Equal(1, transport.ListenTcpCallCount);
                Assert.NotNull(transport.Listener);
                Assert.Same(transport.Listener!.LocalEndPoint, server.LocalEndPoint);
                await transport.Listener.WaitForAcceptCallAsync();
            }
        }

        // 서버 종료 수명 테스트: accept loop 가 listener 에서 막혀 있는 상태로 stop 되면,
        // listener 를 깨워 닫고 Transport.StopAsync 까지 호출해야 다음 테스트/호스트 재시작에서 listen 자원이 남지 않는다.
        [Fact]
        public async Task StopAsync_WhenStarted_ClosesListenerAndStopsTransport()
        {
            FakeTransport transport = new FakeTransport();
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            using (BrokerServer server = new BrokerServer(transport, pool, 64))
            {
                await server.StartTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));

                Assert.NotNull(transport.Listener);
                await transport.Listener!.WaitForAcceptCallAsync();

                await server.StopAsync();

                Assert.Equal(1, transport.Listener.CloseCallCount);
                Assert.Equal(1, transport.Listener.DisposeCallCount);
                Assert.Equal(1, transport.StopCallCount);
                Assert.Null(server.LocalEndPoint);
            }
        }

        // 실제 TCP command loopback 테스트: 서버 wiring 이 단순히 handler 를 등록하는 데 그치지 않고,
        // SaeaTransport 의 accept/receive/send pump 를 통해 SUBSCRIBE 와 PUBLISH command 를 연결해 subscriber 로 payload 를 보내야 한다.
        [Fact]
        public async Task TcpCommandLoopback_WhenSubscriberAndPublisherUseLengthPrefixedCommands_FansOutPayload()
        {
            const string Topic = "alpha";
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(128);
            byte[] expectedPayload = new byte[] { 11, 22, 33, 44, 55, 66 };

            using (SaeaTransport transport = new SaeaTransport())
            using (BrokerServer server = new BrokerServer(transport, pool, 128))
            {
                Socket? subscriber = null;
                Socket? publisher = null;

                try
                {
                    await server.StartTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(server.LocalEndPoint);

                    subscriber = CreateConnectedTcpClient(boundEndPoint);
                    publisher = CreateConnectedTcpClient(boundEndPoint);

                    await SendFrameAsync(subscriber, Encoding.ASCII.GetBytes("SUBSCRIBE " + Topic));
                    await WaitForSubscriberCountAsync(server, Topic, 1);

                    await SendFrameAsync(publisher, CreatePublishCommand(Topic, expectedPayload));

                    byte[] receivedPayload = await ReceiveExactAsync(subscriber, expectedPayload.Length);
                    Assert.Equal(expectedPayload, receivedPayload);

                    await WaitForRentedCountAsync(pool, 0);
                }
                finally
                {
                    subscriber?.Dispose();
                    publisher?.Dispose();
                    await server.StopAsync();
                }
            }
        }

        private static bool HasExpectedConstructor(ConstructorInfo constructor)
        {
            ParameterInfo[] parameters = constructor.GetParameters();

            return parameters.Length == 3
                && parameters[0].ParameterType.FullName == "Hps.Transport.ITransport"
                && parameters[1].ParameterType.FullName == "Hps.Buffers.PinnedBlockMemoryPool"
                && parameters[2].ParameterType == typeof(int);
        }

        private static MethodInfo GetPublicMethod(Type type, string name)
        {
            MethodInfo? method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new Type[] { typeof(EndPoint), typeof(CancellationToken) },
                modifiers: null);

            if (method != null)
                return method;

            method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new Type[] { typeof(CancellationToken) },
                modifiers: null);

            Assert.NotNull(method);
            return method!;
        }

        private static Socket CreateConnectedTcpClient(IPEndPoint remoteEndPoint)
        {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                socket.NoDelay = true;
                socket.Connect(remoteEndPoint);
                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private static byte[] CreatePublishCommand(string topic, byte[] payload)
        {
            byte[] prefix = Encoding.ASCII.GetBytes("PUBLISH " + topic + " ");
            byte[] command = new byte[prefix.Length + payload.Length];

            Buffer.BlockCopy(prefix, 0, command, 0, prefix.Length);
            Buffer.BlockCopy(payload, 0, command, prefix.Length, payload.Length);

            return command;
        }

        private static async Task SendFrameAsync(Socket socket, byte[] payload)
        {
            byte[] frame = new byte[4 + payload.Length];
            frame[0] = (byte)((payload.Length >> 24) & 0xFF);
            frame[1] = (byte)((payload.Length >> 16) & 0xFF);
            frame[2] = (byte)((payload.Length >> 8) & 0xFF);
            frame[3] = (byte)(payload.Length & 0xFF);
            Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);

            int offset = 0;
            while (offset < frame.Length)
            {
                int sent = await socket.SendAsync(new ArraySegment<byte>(frame, offset, frame.Length - offset), SocketFlags.None);
                if (sent == 0)
                    throw new InvalidOperationException("TCP frame 전송 중 socket 이 먼저 닫혔다.");

                offset += sent;
            }
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
            byte[] buffer = new byte[length];
            int offset = 0;

            while (offset < length)
            {
                int received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, offset, length - offset), SocketFlags.None);
                if (received == 0)
                    throw new InvalidOperationException("payload 수신 중 socket 이 먼저 닫혔다.");

                offset += received;
            }

            return buffer;
        }

        private static async Task WaitForSubscriberCountAsync(BrokerServer server, string topic, int expected)
        {
            SubscriptionTable subscriptions = ReadSubscriptionTable(server);
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);

            while (DateTime.UtcNow < deadline)
            {
                if (subscriptions.CountSubscribers(topic) == expected)
                    return;

                await Task.Delay(10);
            }

            Assert.Equal(expected, subscriptions.CountSubscribers(topic));
        }

        private static SubscriptionTable ReadSubscriptionTable(BrokerServer server)
        {
            // SUBSCRIBE command 가 서버 내부 Broker 라우팅까지 도달했는지 기다리는 테스트 전용 white-box 경계다.
            // public protocol 에 ack 가 아직 없으므로, publisher 를 보내기 전에 race 없이 구독 등록 완료를 확인한다.
            FieldInfo? field = typeof(BrokerServer).GetField("_subscriptions", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);

            object? value = field!.GetValue(server);
            return Assert.IsType<SubscriptionTable>(value);
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

        private sealed class FakeTransport : ITransport
        {
            internal int SetReceiveHandlerCallCount { get; private set; }

            internal int StartCallCount { get; private set; }

            internal int ListenTcpCallCount { get; private set; }

            internal int StopCallCount { get; private set; }

            internal ITransportReceiveHandler? ReceiveHandler { get; private set; }

            internal FakeConnectionListener? Listener { get; private set; }

            public void SetReceiveHandler(ITransportReceiveHandler receiveHandler)
            {
                SetReceiveHandlerCallCount++;
                ReceiveHandler = receiveHandler;
            }

            public void SetDatagramHandler(ITransportDatagramHandler datagramHandler)
            {
            }

            public ValueTask<IConnectionListener> ListenTcpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
            {
                ListenTcpCallCount++;
                Listener = new FakeConnectionListener(new IPEndPoint(IPAddress.Loopback, 54321));
                return new ValueTask<IConnectionListener>(Listener);
            }

            public ValueTask<IConnection> ConnectTcpAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public ValueTask<IUdpEndpoint> BindUdpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public bool TrySend(IConnection connection, TransportSendBuffer sendBuffer)
            {
                return false;
            }

            public bool TrySendTo(IUdpEndpoint endpoint, EndPoint remoteEndPoint, TransportSendBuffer sendBuffer)
            {
                return false;
            }

            public ValueTask StartAsync(CancellationToken cancellationToken = default)
            {
                StartCallCount++;
                return default;
            }

            public ValueTask StopAsync(CancellationToken cancellationToken = default)
            {
                StopCallCount++;
                return default;
            }

            public void Dispose()
            {
            }
        }

        private sealed class FakeConnectionListener : IConnectionListener
        {
            private readonly TaskCompletionSource<bool> _acceptCalled;
            private readonly TaskCompletionSource<IConnection> _accepted;

            internal FakeConnectionListener(EndPoint localEndPoint)
            {
                LocalEndPoint = localEndPoint;
                _acceptCalled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _accepted = new TaskCompletionSource<IConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            internal int AcceptCallCount { get; private set; }

            internal int CloseCallCount { get; private set; }

            internal int DisposeCallCount { get; private set; }

            public EndPoint LocalEndPoint { get; }

            public ValueTask<IConnection> AcceptAsync(CancellationToken cancellationToken = default)
            {
                AcceptCallCount++;
                _acceptCalled.TrySetResult(true);
                return new ValueTask<IConnection>(_accepted.Task);
            }

            public void Close()
            {
                CloseCallCount++;
                _accepted.TrySetCanceled();
            }

            public void Dispose()
            {
                DisposeCallCount++;
            }

            internal async Task WaitForAcceptCallAsync()
            {
                Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
                Task completedTask = await Task.WhenAny(_acceptCalled.Task, timeoutTask);

                Assert.Same(_acceptCalled.Task, completedTask);
                Assert.Equal(1, AcceptCallCount);
            }
        }
    }
}
