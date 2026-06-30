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
