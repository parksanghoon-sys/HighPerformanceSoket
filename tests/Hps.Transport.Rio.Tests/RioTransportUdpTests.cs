using System;
using System.Net;
using System.Net.Sockets;
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

        private static async Task<ReceivedDatagram> WaitForReceivedDatagramAsync(CapturingDatagramHandler datagramHandler)
        {
            Task<ReceivedDatagram> receivedTask = datagramHandler.ReceivedTask;
            Task<IUdpEndpoint> closedTask = datagramHandler.ClosedTask;
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            Task completedTask = await Task.WhenAny(receivedTask, closedTask, timeoutTask);

            Assert.Same(receivedTask, completedTask);
            return await receivedTask;
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
