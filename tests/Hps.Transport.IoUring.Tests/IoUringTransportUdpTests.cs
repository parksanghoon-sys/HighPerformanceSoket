using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using Hps.Buffers;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringTransportUdpTests
    {
        // UDP endpoint resource 만 있어서는 public BindUdpAsync 경로가 열리지 않는다.
        // transport 가 endpoint 등록, receive loop 시작, close notify 를 직접 소유해야 stop/close 수명이 TCP와 대칭이 된다.
        [Fact]
        public void UdpTransportShape_WhenInspected_ExposesBindReceivePumpMembers()
        {
            Type transportType = typeof(IoUringTransport);

            Assert.NotNull(transportType.GetField("_udpEndpoints", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(transportType.GetMethod("RegisterUdpEndpoint", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(transportType.GetMethod("UnregisterUdpEndpoint", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(transportType.GetMethod("StartUdpReceiveLoop", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(transportType.GetMethod("UdpReceiveLoopAsync", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(transportType.GetMethod("NotifyUdpEndpointClosed", BindingFlags.Instance | BindingFlags.NonPublic));
        }

        // UDP send path 는 public TrySendTo override 와 endpoint 단일 send pump 로만 열려야 한다.
        // shape test 로 먼저 고정해, caller 가 성공 반환 뒤 맡긴 ref 를 background send completion 이 반환하는 경계를 강제한다.
        [Fact]
        public void UdpSendTransportShape_WhenInspected_ExposesSendPumpMembers()
        {
            Type transportType = typeof(IoUringTransport);

            Assert.NotNull(transportType.GetMethod(
                "TrySendTo",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));
            Assert.NotNull(transportType.GetMethod("StartUdpSendLoop", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(transportType.GetMethod("UdpSendLoopAsync", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(transportType.GetMethod("SendUdpDatagramAsync", BindingFlags.Instance | BindingFlags.NonPublic));
        }

        // Linux available host 에서 UDP recvmsg pump 가 datagram 을 RefCountedBuffer ownership 으로 handler 에 넘기는지 검증한다.
        // Windows와 unavailable Linux에서는 native syscall 경로를 검증할 수 없으므로 capability gate 로 개발 환경을 보존한다.
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

                IUdpEndpoint? endpoint = null;
                Socket client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                try
                {
                    endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    byte[] payload = new byte[] { 1, 2, 3 };
                    await client.SendToAsync(new ArraySegment<byte>(payload), SocketFlags.None, endpoint.LocalEndPoint);

                    ReceivedDatagram received = await handler.ReceiveAsync();

                    Assert.Same(endpoint, received.Endpoint);
                    Assert.Equal(payload, received.Payload);
                }
                finally
                {
                    client.Dispose();
                    endpoint?.Close();
                    await transport.StopAsync();
                }
            }
        }

        // Linux available host 에서 handler 가 받은 datagram 을 TrySendTo 로 다시 queue 하면 sendmsg completion 뒤 client 가 payload 를 받아야 한다.
        // unavailable 환경에서는 syscall 경로를 early-return 하며, 소유권 경계는 endpoint queue tests 가 로컬에서 별도 검증한다.
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

                IUdpEndpoint? endpoint = null;
                Socket client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                try
                {
                    endpoint = await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    client.Bind(new IPEndPoint(IPAddress.Loopback, 0));

                    byte[] payload = new byte[] { 4, 5, 6 };
                    await client.SendToAsync(new ArraySegment<byte>(payload), SocketFlags.None, endpoint.LocalEndPoint);

                    byte[] received = await ReceiveUdpDatagramAsync(client, payload.Length);

                    Assert.Equal(payload, received);
                }
                finally
                {
                    client.Dispose();
                    endpoint?.Close();
                    await transport.StopAsync();
                }
            }
        }

        private static async Task<byte[]> ReceiveUdpDatagramAsync(Socket socket, int maxLength)
        {
            byte[] buffer = new byte[maxLength];
            Task<SocketReceiveFromResult> receiveTask = socket.ReceiveFromAsync(
                new ArraySegment<byte>(buffer),
                SocketFlags.None,
                new IPEndPoint(IPAddress.Any, 0));

            Task completed = await Task.WhenAny(receiveTask, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
            if (completed != receiveTask)
                throw new TimeoutException("io_uring UDP send pump 가 제한 시간 안에 payload 를 전달하지 못했습니다.");

            SocketReceiveFromResult result = await receiveTask.ConfigureAwait(false);
            byte[] payload = new byte[result.ReceivedBytes];
            Buffer.BlockCopy(buffer, 0, payload, 0, payload.Length);
            return payload;
        }

        private sealed class CapturingDatagramHandler : ITransportDatagramHandler
        {
            private readonly TaskCompletionSource<ReceivedDatagram> _received;

            internal CapturingDatagramHandler()
            {
                _received = new TaskCompletionSource<ReceivedDatagram>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

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
                    throw new TimeoutException("io_uring UDP receive pump 가 제한 시간 안에 datagram 을 전달하지 못했습니다.");

                return await _received.Task.ConfigureAwait(false);
            }
        }

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
