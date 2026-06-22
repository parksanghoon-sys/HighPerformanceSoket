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

        // UDP host wiring 계약 테스트: BrokerUdpDatagramHandler 는 이미 Broker 계층에 존재하므로,
        // 서버 계층은 Transport 에 datagram handler 를 등록하고 UDP endpoint 를 bind 하는 별도 진입점을 제공해야 한다.
        [Fact]
        public void BrokerServerContract_WhenInspected_ExposesMinimalUdpHostWiringApi()
        {
            Type? serverType = Type.GetType("Hps.Server.BrokerServer, Hps.Server", throwOnError: false);

            Assert.NotNull(serverType);
            Assert.NotNull(serverType!.GetProperty("UdpLocalEndPoint", BindingFlags.Instance | BindingFlags.Public));
            Assert.Equal(typeof(ValueTask), GetPublicMethod(serverType, "StartUdpAsync").ReturnType);
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

        // UDP wiring 테스트: Server 는 Transport 에 BrokerUdpDatagramHandler 를 등록하고 UDP endpoint 를 bind 해야
        // 실제 UDP datagram self-command 가 Broker routing/fan-out 경로로 들어올 수 있다.
        [Fact]
        public async Task StartUdpAsync_WhenCalled_RegistersDatagramHandlerStartsTransportAndBindsEndpoint()
        {
            FakeTransport transport = new FakeTransport();
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            using (BrokerServer server = new BrokerServer(transport, pool, 64))
            {
                await server.StartUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));

                Assert.Equal(1, transport.SetDatagramHandlerCallCount);
                Assert.IsType<BrokerUdpDatagramHandler>(transport.DatagramHandler);
                Assert.Equal(1, transport.StartCallCount);
                Assert.Equal(1, transport.BindUdpCallCount);
                Assert.NotNull(transport.UdpEndpoint);
                Assert.Same(transport.UdpEndpoint!.LocalEndPoint, server.UdpLocalEndPoint);
            }
        }

        // TCP와 UDP는 같은 Interface Server 인스턴스의 서로 다른 ingress 이다.
        // 이미 TCP listener 가 떠 있어도 UDP bind 를 추가할 수 있어야 하며, Transport.StartAsync 는 backend 수명마다 한 번만 호출되어야 한다.
        [Fact]
        public async Task StartTcpAsyncThenStartUdpAsync_WhenCalled_StartsTransportOnceAndKeepsBothEndpoints()
        {
            FakeTransport transport = new FakeTransport();
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            using (BrokerServer server = new BrokerServer(transport, pool, 64))
            {
                await server.StartTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                await transport.Listener!.WaitForAcceptCallAsync();

                await server.StartUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));

                Assert.Equal(1, transport.StartCallCount);
                Assert.NotNull(server.LocalEndPoint);
                Assert.NotNull(server.UdpLocalEndPoint);
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

        // UDP stop 수명 테스트: bind 된 UDP endpoint 도 StopAsync 에서 닫고 dispose 해야 한다.
        // 그렇지 않으면 같은 port 재시작이 실패하거나 Transport 의 endpoint close notification cleanup 이 실행될 기회가 사라진다.
        [Fact]
        public async Task StopAsync_WhenUdpStarted_ClosesUdpEndpointAndStopsTransport()
        {
            FakeTransport transport = new FakeTransport();
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            using (BrokerServer server = new BrokerServer(transport, pool, 64))
            {
                await server.StartUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));

                Assert.NotNull(transport.UdpEndpoint);

                await server.StopAsync();

                Assert.Equal(1, transport.UdpEndpoint!.CloseCallCount);
                Assert.Equal(1, transport.UdpEndpoint.DisposeCallCount);
                Assert.Equal(1, transport.StopCallCount);
                Assert.Null(server.UdpLocalEndPoint);
            }
        }

        // UDP lease timer wiring 테스트는 enabled options 일 때 Server host 가 bind 성공 뒤 sweep timer 를 만들고,
        // timer fire 시 BrokerUdpDatagramHandler 의 sweep entry point 를 통해 stale remote subscription 을 제거하는지 검증한다.
        [Fact]
        public async Task StartUdpAsync_WhenUdpLeaseSweepEnabled_CreatesTimerAndSweepsExpiredRemote()
        {
            FakeTransport transport = new FakeTransport();
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            ManualTimeProvider timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-22T00:00:00Z"));
            BrokerServerOptions options = BrokerServerOptions.CreateWithUdpLeaseSweep(
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(5),
                timeProvider);
            using (BrokerServer server = new BrokerServer(transport, pool, 64, options))
            {
                await server.StartUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));

                Assert.Single(timeProvider.Timers);
                Assert.Equal(TimeSpan.FromSeconds(5), timeProvider.Timers[0].DueTime);
                Assert.Equal(TimeSpan.FromSeconds(5), timeProvider.Timers[0].Period);

                EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 20000);
                transport.DatagramHandler!.OnDatagramReceived(
                    transport.UdpEndpoint!,
                    remoteEndPoint,
                    RentDatagram(pool, "SUBSCRIBE alpha"));
                Assert.True(ReadSubscriptionTable(server).IsSubscribed("alpha", BrokerSubscriber.ForUdp(transport.UdpEndpoint!, remoteEndPoint)));

                timeProvider.Advance(TimeSpan.FromSeconds(31));
                timeProvider.Timers[0].Fire();

                Assert.False(ReadSubscriptionTable(server).IsSubscribed("alpha", BrokerSubscriber.ForUdp(transport.UdpEndpoint!, remoteEndPoint)));
                Assert.Equal(0, pool.RentedCount);
            }
        }

        // UDP lease timer stop 테스트는 Server host 가 소유한 timer 를 StopAsync 에서 endpoint/transport 종료 전에 dispose 하는지 검증한다.
        // timer 를 남겨두면 stop 이후에도 routing table cleanup callback 이 들어와 host 수명 경계를 흐릴 수 있다.
        [Fact]
        public async Task StopAsync_WhenUdpLeaseSweepEnabled_DisposesSweepTimer()
        {
            FakeTransport transport = new FakeTransport();
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            ManualTimeProvider timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-22T00:00:00Z"));
            BrokerServerOptions options = BrokerServerOptions.CreateWithUdpLeaseSweep(
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(5),
                timeProvider);
            using (BrokerServer server = new BrokerServer(transport, pool, 64, options))
            {
                await server.StartUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                ManualTimer timer = Assert.Single(timeProvider.Timers);

                await server.StopAsync();

                Assert.Equal(1, timer.DisposeCallCount);
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

                    byte[] receivedPayload = await ReceiveFrameAsync(subscriber);
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

        // 실제 TCP 다중 subscriber fan-out 테스트: BrokerPublisher 단위 테스트만으로는 Server/Transport/Protocol 결선 뒤에도
        // 같은 topic 의 모든 raw socket subscriber 가 payload 를 받는지 알 수 없다. 이 테스트는 공유 RefCountedBuffer fan-out 이
        // subscriber 2명에게 각각 도착하고, 송신 완료 뒤 server pool 이 0으로 돌아오는 end-to-end 경계를 고정한다.
        [Fact]
        public async Task TcpCommandLoopback_WhenTwoSubscribersShareTopic_FansOutPayloadToBothSockets()
        {
            const string Topic = "alpha";
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(128);
            byte[] expectedPayload = new byte[] { 101, 102, 103, 104, 105, 106, 107 };

            using (SaeaTransport transport = new SaeaTransport())
            using (BrokerServer server = new BrokerServer(transport, pool, 128))
            {
                Socket? subscriberOne = null;
                Socket? subscriberTwo = null;
                Socket? publisher = null;

                try
                {
                    await server.StartTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(server.LocalEndPoint);

                    subscriberOne = CreateConnectedTcpClient(boundEndPoint);
                    subscriberTwo = CreateConnectedTcpClient(boundEndPoint);
                    publisher = CreateConnectedTcpClient(boundEndPoint);

                    await SendFrameAsync(subscriberOne, Encoding.ASCII.GetBytes("SUBSCRIBE " + Topic));
                    await SendFrameAsync(subscriberTwo, Encoding.ASCII.GetBytes("SUBSCRIBE " + Topic));
                    await WaitForSubscriberCountAsync(server, Topic, 2);

                    await SendFrameAsync(publisher, CreatePublishCommand(Topic, expectedPayload));

                    Task<byte[]> firstReceiveTask = ReceiveFrameAsync(subscriberOne);
                    Task<byte[]> secondReceiveTask = ReceiveFrameAsync(subscriberTwo);

                    Assert.Equal(expectedPayload, await firstReceiveTask);
                    Assert.Equal(expectedPayload, await secondReceiveTask);

                    await WaitForRentedCountAsync(pool, 0);
                }
                finally
                {
                    subscriberOne?.Dispose();
                    subscriberTwo?.Dispose();
                    publisher?.Dispose();
                    await server.StopAsync();
                }
            }
        }

        // TCP stream 은 message boundary 를 보존하지 않으므로 broker->subscriber outbound 도 길이 prefix 로 메시지 경계를 실어야 한다.
        // 첫 payload 의 앞 4바이트를 작은 길이처럼 구성해, raw payload outbound 구현에서는 frame reader 가 payload 일부만 읽고 실패하도록 고정한다.
        [Fact]
        public async Task TcpCommandLoopback_WhenPublisherSendsVariableLengthMessages_SubscriberReceivesLengthPrefixedFrames()
        {
            const string Topic = "alpha";
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(128);
            byte[] firstPayload = new byte[] { 0, 0, 0, 3, 170, 187, 204 };
            byte[] secondPayload = new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 };

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

                    await SendFrameAsync(publisher, CreatePublishCommand(Topic, firstPayload));
                    await SendFrameAsync(publisher, CreatePublishCommand(Topic, secondPayload));

                    Assert.Equal(firstPayload, await ReceiveFrameAsync(subscriber));
                    Assert.Equal(secondPayload, await ReceiveFrameAsync(subscriber));

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

        // drop-oldest 실제 fire 경로 검증: 기존 closed/open-loop 부하는 subscriber 가 계속 읽기 때문에
        // TCP pending queue capacity 16에 도달하지 못했다. 이 테스트는 subscriber 를 의도적으로 읽지 않게 정체시켜
        // OS send buffer 포화 뒤 Transport pending queue drop 과 high-watermark 포화를 end-to-end 로 고정한다.
        [Fact]
        public async Task TcpCommandLoopback_WhenSubscriberDoesNotRead_DropsOldestAndReportsTransportDiagnostics()
        {
            const string Topic = "alpha";
            const int PayloadLength = 32768;
            const int MaxPublishCount = 2048;
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(65536);
            byte[] payload = new byte[PayloadLength];

            using (SaeaTransport transport = new SaeaTransport())
            using (BrokerServer server = new BrokerServer(transport, pool, 65536))
            {
                Socket? stalledSubscriber = null;
                Socket? publisher = null;

                try
                {
                    await server.StartTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(server.LocalEndPoint);

                    stalledSubscriber = CreateConnectedTcpClient(boundEndPoint);
                    stalledSubscriber.ReceiveBufferSize = 1024;
                    publisher = CreateConnectedTcpClient(boundEndPoint);

                    await SendFrameAsync(stalledSubscriber, Encoding.ASCII.GetBytes("SUBSCRIBE " + Topic));
                    await WaitForSubscriberCountAsync(server, Topic, 1);

                    TransportDiagnosticsSnapshot diagnostics = await PublishUntilTcpDropAsync(
                        publisher,
                        (ITransportDiagnostics)transport,
                        Topic,
                        payload,
                        MaxPublishCount);

                    Assert.True(diagnostics.TcpDroppedPendingSendCount > 0);
                    Assert.Equal(16, diagnostics.TcpPendingSendQueueHighWatermark);
                    Assert.Equal(0, diagnostics.UdpDroppedPendingSendCount);
                    Assert.Equal(0, diagnostics.UdpPendingSendQueueHighWatermark);
                }
                finally
                {
                    publisher?.Dispose();
                    stalledSubscriber?.Dispose();
                    await server.StopAsync();
                    await WaitForRentedCountAsync(pool, 0);
                }
            }
        }

        // 실제 UDP command loopback 테스트는 host wiring, SAEA UDP receive/send pump, Broker datagram handler 를 함께 묶는다.
        // UDP protocol 에는 subscribe ack 가 아직 없으므로 publish 전 white-box 구독 카운트로 race 를 제거하고,
        // fan-out 결과는 command prefix 가 아닌 원본 payload 만 datagram 으로 돌아오는지 실제 socket 으로 검증한다.
        [Fact]
        public async Task UdpCommandLoopback_WhenSubscriberAndPublisherUseDatagramCommands_FansOutPayload()
        {
            const string Topic = "alpha";
            PinnedBlockMemoryPool serverPool = new PinnedBlockMemoryPool(128);
            byte[] expectedPayload = new byte[] { 201, 202, 203, 204, 205 };

            using (SaeaTransport transport = new SaeaTransport())
            using (BrokerServer server = new BrokerServer(transport, serverPool, 128))
            {
                Socket? subscriber = null;
                Socket? publisher = null;

                try
                {
                    await server.StartUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    IPEndPoint serverEndPoint = Assert.IsType<IPEndPoint>(server.UdpLocalEndPoint);

                    subscriber = CreateBoundUdpSocket();
                    publisher = CreateBoundUdpSocket();

                    await SendUdpDatagramAsync(subscriber, serverEndPoint, Encoding.ASCII.GetBytes("SUBSCRIBE " + Topic));
                    await WaitForSubscriberCountAsync(server, Topic, 1);

                    await SendUdpDatagramAsync(publisher, serverEndPoint, CreatePublishCommand(Topic, expectedPayload));

                    ReceivedUdpDatagram received = await ReceiveUdpDatagramAsync(subscriber, 256);
                    Assert.Equal(expectedPayload, received.Payload);
                    Assert.Equal(serverEndPoint, received.RemoteEndPoint);
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

        private static Socket CreateBoundUdpSocket()
        {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
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

        private static async Task<TransportDiagnosticsSnapshot> PublishUntilTcpDropAsync(
            Socket publisher,
            ITransportDiagnostics diagnosticsReader,
            string topic,
            byte[] payload,
            int maxPublishCount)
        {
            for (int index = 0; index < maxPublishCount; index++)
            {
                WritePublishSequence(payload, index);
                await SendFrameWithTimeoutAsync(
                    publisher,
                    CreatePublishCommand(topic, payload),
                    TimeSpan.FromSeconds(5));

                TransportDiagnosticsSnapshot snapshot = diagnosticsReader.GetDiagnosticsSnapshot();
                if (snapshot.TcpDroppedPendingSendCount > 0)
                    return snapshot;
            }

            return diagnosticsReader.GetDiagnosticsSnapshot();
        }

        private static void WritePublishSequence(byte[] payload, int sequence)
        {
            payload[0] = (byte)((sequence >> 24) & 0xFF);
            payload[1] = (byte)((sequence >> 16) & 0xFF);
            payload[2] = (byte)((sequence >> 8) & 0xFF);
            payload[3] = (byte)(sequence & 0xFF);
        }

        private static async Task<byte[]> ReceiveFrameAsync(Socket socket)
        {
            byte[] header = await ReceiveExactAsync(socket, 4);
            int payloadLength =
                (header[0] << 24)
                | (header[1] << 16)
                | (header[2] << 8)
                | header[3];

            Assert.InRange(payloadLength, 0, 1024);
            return await ReceiveExactAsync(socket, payloadLength);
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

        private static async Task SendFrameWithTimeoutAsync(Socket socket, byte[] payload, TimeSpan timeout)
        {
            Task sendTask = SendFrameAsync(socket, payload);
            Task timeoutTask = Task.Delay(timeout);
            Task completedTask = await Task.WhenAny(sendTask, timeoutTask);

            Assert.Same(sendTask, completedTask);
            await sendTask;
        }

        private static async Task SendUdpDatagramAsync(Socket socket, EndPoint remoteEndPoint, byte[] payload)
        {
            int sent = await socket.SendToAsync(new ArraySegment<byte>(payload), SocketFlags.None, remoteEndPoint);
            Assert.Equal(payload.Length, sent);
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

        private static async Task<ReceivedUdpDatagram> ReceiveUdpDatagramAsync(Socket socket, int maxLength)
        {
            Task<ReceivedUdpDatagram> receiveTask = ReceiveUdpDatagramCoreAsync(socket, maxLength);
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            Task completedTask = await Task.WhenAny(receiveTask, timeoutTask);

            Assert.Same(receiveTask, completedTask);
            return await receiveTask;
        }

        private static async Task<ReceivedUdpDatagram> ReceiveUdpDatagramCoreAsync(Socket socket, int maxLength)
        {
            byte[] receiveBuffer = new byte[maxLength];
            EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            SocketReceiveFromResult result = await socket.ReceiveFromAsync(
                new ArraySegment<byte>(receiveBuffer),
                SocketFlags.None,
                remoteEndPoint);
            byte[] payload = new byte[result.ReceivedBytes];

            Buffer.BlockCopy(receiveBuffer, 0, payload, 0, payload.Length);
            return new ReceivedUdpDatagram(result.RemoteEndPoint, payload);
        }

        private static RefCountedBuffer RentDatagram(PinnedBlockMemoryPool pool, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            RefCountedBuffer datagram = pool.RentCounted();
            bytes.CopyTo(datagram.Span);
            datagram.SetLength(bytes.Length);
            return datagram;
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

        private sealed class ReceivedUdpDatagram
        {
            internal ReceivedUdpDatagram(EndPoint remoteEndPoint, byte[] payload)
            {
                RemoteEndPoint = remoteEndPoint;
                Payload = payload;
            }

            internal EndPoint RemoteEndPoint { get; }

            internal byte[] Payload { get; }
        }

        private sealed class ManualTimeProvider : TimeProvider
        {
            private readonly System.Collections.Generic.List<ManualTimer> _timers;
            private DateTimeOffset _utcNow;

            internal ManualTimeProvider(DateTimeOffset utcNow)
            {
                _utcNow = utcNow;
                _timers = new System.Collections.Generic.List<ManualTimer>();
            }

            internal System.Collections.Generic.IReadOnlyList<ManualTimer> Timers
            {
                get { return _timers; }
            }

            public override DateTimeOffset GetUtcNow()
            {
                return _utcNow;
            }

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                ManualTimer timer = new ManualTimer(callback, state, dueTime, period);
                _timers.Add(timer);
                return timer;
            }

            internal void Advance(TimeSpan delta)
            {
                _utcNow = _utcNow.Add(delta);
            }
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly TimerCallback _callback;
            private readonly object? _state;

            internal ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                _callback = callback;
                _state = state;
                DueTime = dueTime;
                Period = period;
            }

            internal TimeSpan DueTime { get; private set; }

            internal TimeSpan Period { get; private set; }

            internal int DisposeCallCount { get; private set; }

            internal bool Disposed { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (Disposed)
                    return false;

                DueTime = dueTime;
                Period = period;
                return true;
            }

            public void Dispose()
            {
                DisposeCallCount++;
                Disposed = true;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return default;
            }

            internal void Fire()
            {
                if (!Disposed)
                    _callback(_state);
            }
        }

        private sealed class FakeTransport : ITransport
        {
            internal int SetReceiveHandlerCallCount { get; private set; }

            internal int StartCallCount { get; private set; }

            internal int ListenTcpCallCount { get; private set; }

            internal int SetDatagramHandlerCallCount { get; private set; }

            internal int BindUdpCallCount { get; private set; }

            internal int StopCallCount { get; private set; }

            internal ITransportReceiveHandler? ReceiveHandler { get; private set; }

            internal ITransportDatagramHandler? DatagramHandler { get; private set; }

            internal FakeConnectionListener? Listener { get; private set; }

            internal FakeUdpEndpoint? UdpEndpoint { get; private set; }

            public void SetReceiveHandler(ITransportReceiveHandler receiveHandler)
            {
                SetReceiveHandlerCallCount++;
                ReceiveHandler = receiveHandler;
            }

            public void SetDatagramHandler(ITransportDatagramHandler datagramHandler)
            {
                SetDatagramHandlerCallCount++;
                DatagramHandler = datagramHandler;
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
                BindUdpCallCount++;
                UdpEndpoint = new FakeUdpEndpoint(new IPEndPoint(IPAddress.Loopback, 54322));
                return new ValueTask<IUdpEndpoint>(UdpEndpoint);
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

        private sealed class FakeUdpEndpoint : IUdpEndpoint
        {
            internal FakeUdpEndpoint(EndPoint localEndPoint)
            {
                LocalEndPoint = localEndPoint;
            }

            internal int CloseCallCount { get; private set; }

            internal int DisposeCallCount { get; private set; }

            public EndPoint LocalEndPoint { get; }

            public void Close()
            {
                CloseCallCount++;
            }

            public void Dispose()
            {
                DisposeCallCount++;
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
