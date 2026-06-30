using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Hps.Buffers;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringUdpEndpointShapeTests
    {
        // UDP endpoint 는 socket 뿐 아니라 msghdr/iovec/sockaddr pin 수명을 completion 까지 보장해야 한다.
        // type shape 를 먼저 고정해 receive/send pump 가 raw pointer lifetime 을 지역 변수에 맡기지 않게 한다.
        [Fact]
        public void UdpResourceTypes_WhenInspected_Exist()
        {
            Assert.NotNull(Type.GetType("Hps.Transport.IoUringUdpEndpoint, Hps.Transport.IoUring"));
            Assert.NotNull(Type.GetType("Hps.Transport.IoUringUdpMessageBuffer, Hps.Transport.IoUring"));
        }

        // close drain 은 UDP send queue 가 소유한 ref 를 정확히 반환해야 한다.
        // pump 구현 전에 endpoint resource 만으로 drop/close ownership 계약을 고정한다.
        [Fact]
        public void UdpEndpoint_WhenClosed_DrainsQueuedSendRefs()
        {
            Type endpointType = RequiredType("Hps.Transport.IoUringUdpEndpoint, Hps.Transport.IoUring");
            using (IoUringTransport transport = new IoUringTransport())
            {
                IoUringOperationRegistry registry = new IoUringOperationRegistry();
                IoUringCompletionLoop loop = IoUringCompletionLoop.CreateForTests(registry);
                Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                IUdpEndpoint? endpoint = null;

                try
                {
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    endpoint = (IUdpEndpoint)Activator.CreateInstance(
                        endpointType,
                        BindingFlags.Instance | BindingFlags.NonPublic,
                        null,
                        new object[] { transport, socket, registry, loop },
                        null)!;

                    PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
                    RefCountedBuffer buffer = pool.RentCounted();
                    buffer.SetLength(1);
                    buffer.AddRef();

                    MethodInfo tryAccept = RequiredMethod(endpointType, "TryAcceptSend");
                    bool accepted = (bool)tryAccept.Invoke(
                        endpoint,
                        new object[] { new IPEndPoint(IPAddress.Loopback, 9), new TransportSendBuffer(buffer, 0, 1) })!;
                    Assert.True(accepted);
                    buffer.Release();

                    endpoint.Close();

                    Assert.Equal(0, pool.RentedCount);
                }
                finally
                {
                    endpoint?.Dispose();
                    socket.Dispose();
                    loop.Dispose();
                }
            }
        }

        // drop-oldest 는 가장 오래된 pending datagram ref 를 즉시 반환해야 한다.
        // 이후 endpoint close 가 남은 16개 ref 를 drain 해 pool count 가 0으로 돌아오는지까지 확인한다.
        [Fact]
        public void UdpEndpoint_WhenPendingQueueExceedsCapacity_DropsOldestAndReleasesEvictedRef()
        {
            Type endpointType = RequiredType("Hps.Transport.IoUringUdpEndpoint, Hps.Transport.IoUring");
            using (IoUringTransport transport = new IoUringTransport())
            {
                IoUringOperationRegistry registry = new IoUringOperationRegistry();
                IoUringCompletionLoop loop = IoUringCompletionLoop.CreateForTests(registry);
                Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                IUdpEndpoint? endpoint = null;

                try
                {
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    endpoint = (IUdpEndpoint)Activator.CreateInstance(
                        endpointType,
                        BindingFlags.Instance | BindingFlags.NonPublic,
                        null,
                        new object[] { transport, socket, registry, loop },
                        null)!;

                    PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
                    IPEndPoint remote = new IPEndPoint(IPAddress.Loopback, 9);
                    MethodInfo tryAccept = RequiredMethod(endpointType, "TryAcceptSend");

                    for (int index = 0; index < 17; index++)
                    {
                        RefCountedBuffer buffer = pool.RentCounted();
                        buffer.SetLength(1);
                        buffer.AddRef();

                        bool accepted = (bool)tryAccept.Invoke(
                            endpoint,
                            new object[] { remote, new TransportSendBuffer(buffer, 0, 1) })!;
                        Assert.True(accepted);
                        buffer.Release();
                    }

                    Assert.Equal(16, pool.RentedCount);

                    endpoint.Close();

                    Assert.Equal(0, pool.RentedCount);
                }
                finally
                {
                    endpoint?.Dispose();
                    socket.Dispose();
                    loop.Dispose();
                }
            }
        }

        private static Type RequiredType(string name)
        {
            Type? type = Type.GetType(name);
            Assert.NotNull(type);
            return type!;
        }

        private static MethodInfo RequiredMethod(Type type, string name)
        {
            MethodInfo? method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return method!;
        }
    }
}
