using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Hps.Buffers;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringTransportTcpTests
    {
        // TCP listener/resource 경계가 없으면 Listen/Accept wiring을 IoUringTransport 하나에 직접 욱여넣게 된다.
        // receive/send pump를 붙이기 전에 owner 타입부터 assertion failure로 고정한다.
        [Fact]
        public void TcpResourceTypes_WhenInspected_Exist()
        {
            Assert.NotNull(Type.GetType("Hps.Transport.IoUringConnectionListener, Hps.Transport.IoUring"));
            Assert.NotNull(Type.GetType("Hps.Transport.IoUringTcpConnectionResource, Hps.Transport.IoUring"));
        }

        // resource skeleton은 실제 SQE를 submit하지 않아도 socket, pinned block, operation context 수명을 함께 책임져야 한다.
        // Dispose에서 block 반환과 registry unregister가 누락되면 후속 pump 구현 전에 누수 경계가 생긴다.
        [Fact]
        public void TcpConnectionResource_WhenDisposed_ReturnsBlocksAndUnregistersContexts()
        {
            Type resourceType = RequiredType("Hps.Transport.IoUringTcpConnectionResource, Hps.Transport.IoUring");
            IoUringOperationRegistry registry = new IoUringOperationRegistry();
            IoUringCompletionLoop loop = IoUringCompletionLoop.CreateForTests(registry);
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IDisposable? resource = null;

            try
            {
                resource = (IDisposable)CreateInstance(resourceType, socket, registry, loop);

                PinnedBlockMemoryPool receivePool = (PinnedBlockMemoryPool)ReadProperty(resource, "ReceivePool");
                PinnedBlockMemoryPool lengthPrefixPool = (PinnedBlockMemoryPool)ReadProperty(resource, "LengthPrefixPool");
                IoUringOperationContext receiveContext = (IoUringOperationContext)ReadProperty(resource, "ReceiveContext");
                IoUringOperationContext sendContext = (IoUringOperationContext)ReadProperty(resource, "SendContext");

                resource.Dispose();
                resource = null;

                IoUringOperationContext? ignored;
                Assert.Equal(0, receivePool.RentedCount);
                Assert.Equal(0, lengthPrefixPool.RentedCount);
                Assert.False(registry.TryResolve(receiveContext.Token, out ignored));
                Assert.False(registry.TryResolve(sendContext.Token, out ignored));
            }
            finally
            {
                resource?.Dispose();
                socket.Dispose();
                loop.Dispose();
            }
        }

        // explicit io_uring backend은 지원되지 않는 OS에서 socket bind/connect로 내려가기 전에 실패해야 한다.
        // 이 경계가 유지되어야 selector가 SAEA fallback을 안전하게 선택할 수 있다.
        [Fact]
        public async Task ListenTcpAsync_WhenNotLinux_ThrowsNotSupportedException()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            using (IoUringTransport transport = new IoUringTransport())
            {
                await transport.StartAsync();

                await Assert.ThrowsAsync<NotSupportedException>(async delegate()
                {
                    await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                });
            }
        }

        // Phase 6 구현 중에도 기본 backend는 계속 SAEA다.
        // io_uring은 Linux 전용 opt-in backend로 남겨 default behavior drift를 막는다.
        [Fact]
        public void CreateDefault_DuringTcpBoundaryWork_ReturnsSaeaTransport()
        {
            using (ITransport transport = TransportFactory.CreateDefault())
            {
                Assert.IsType<SaeaTransport>(transport);
            }
        }

        private static Type RequiredType(string name)
        {
            Type? type = Type.GetType(name);
            Assert.NotNull(type);
            return type!;
        }

        private static object CreateInstance(Type type, params object[] arguments)
        {
            object? instance = Activator.CreateInstance(
                type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                arguments,
                null);
            Assert.NotNull(instance);
            return instance!;
        }

        private static object ReadProperty(object target, string propertyName)
        {
            PropertyInfo? property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(property);

            object? result = property!.GetValue(target);
            Assert.NotNull(result);
            return result!;
        }
    }
}
