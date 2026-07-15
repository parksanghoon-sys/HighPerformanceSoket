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

        // StopCore가 목록 snapshot을 비운 뒤 connection 등록을 허용하면 queue가 닫힌 transport에 미추적 resource가 남는다.
        // Linux syscall과 capability probe 없이 종료 경계를 검증해 Windows 개발 환경에서도 registration race를 고정한다.
        [Fact]
        public async Task RegisterConnection_WhenTransportAlreadyStopped_ThrowsInvalidOperationException()
        {
            using (IoUringTransport transport = new IoUringTransport())
            {
                await transport.StopAsync();
                IConnection connection = CreateStandaloneTransportConnection();

                try
                {
                    MethodInfo? registerConnection = typeof(IoUringTransport).GetMethod(
                        "RegisterConnection",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(registerConnection);

                    TargetInvocationException exception = Assert.Throws<TargetInvocationException>(delegate()
                    {
                        registerConnection!.Invoke(transport, new object[] { connection });
                    });

                    Assert.IsType<InvalidOperationException>(exception.InnerException);
                    Assert.Empty(((ITransportEndpointDiagnostics)transport).GetEndpointSnapshots());
                }
                finally
                {
                    connection.Close();
                }
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

        // Linux에서 capability가 실제 available일 때만 receive pump loopback을 검증한다.
        // Windows와 unavailable Linux에서는 이 테스트가 기본 개발 경로를 깨지 않도록 early return 한다.
        [Fact]
        public async Task TcpLoopback_WhenIoUringAvailable_DeliversReceivedBytes()
        {
            if (IoUringCapabilityProbe.GetStatus() != IoUringCapabilityStatus.Available)
                return;

            RecordingReceiveHandler handler = new RecordingReceiveHandler(expectedLength: 3);
            using (IoUringTransport transport = new IoUringTransport())
            {
                transport.SetReceiveHandler(handler);
                await transport.StartAsync();

                IConnectionListener listener = await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                Socket client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await client.ConnectAsync(listener.LocalEndPoint);
                    IConnection server = await listener.AcceptAsync();

                    byte[] payload = new byte[] { 1, 2, 3 };
                    await client.SendAsync(payload, SocketFlags.None);

                    byte[] received = await handler.ReceiveAsync();

                    Assert.Equal(payload, received);
                    server.Close();
                }
                finally
                {
                    client.Dispose();
                    listener.Close();
                    await transport.StopAsync();
                }
            }
        }

        // Linux에서 capability가 실제 available일 때만 send pump loopback을 검증한다.
        // caller ref와 transport ref가 모두 해제되어 pool count가 0으로 돌아오는지도 같이 확인한다.
        [Fact]
        public async Task TcpLoopback_WhenIoUringAvailable_SendsQueuedPayloadToPeer()
        {
            if (IoUringCapabilityProbe.GetStatus() != IoUringCapabilityStatus.Available)
                return;

            using (IoUringTransport transport = new IoUringTransport())
            {
                await transport.StartAsync();

                IConnectionListener listener = await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                Socket client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
                IRefCountedBufferSource source = transport.CreateTcpPayloadBufferSource(pool);
                try
                {
                    await client.ConnectAsync(listener.LocalEndPoint);
                    IConnection server = await listener.AcceptAsync();

                    byte[] payload = new byte[] { 9, 8, 7 };
                    RefCountedBuffer buffer = source.RentCounted();
                    payload.CopyTo(buffer.Span);
                    buffer.SetLength(payload.Length);
                    buffer.AddRef();

                    Assert.True(transport.TrySend(server, new TransportSendBuffer(buffer, 0, payload.Length)));
                    buffer.Release();

                    byte[] received = await ReceiveExactAsync(client, payload.Length);

                    Assert.Equal(payload, received);
                    Console.WriteLine("registered payload fixed send path: hit");
                    server.Close();
                }
                finally
                {
                    client.Dispose();
                    listener.Close();
                    await transport.StopAsync();
                    Assert.Equal(0, pool.RentedCount);
                }
            }
        }

        private static IConnection CreateStandaloneTransportConnection()
        {
            Type? connectionType = Type.GetType("Hps.Transport.TransportConnection, Hps.Transport");
            Assert.NotNull(connectionType);

            object? instance = Activator.CreateInstance(connectionType!, nonPublic: true);
            return Assert.IsAssignableFrom<IConnection>(instance);
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

        private static async Task<byte[]> ReceiveExactAsync(Socket socket, int length)
        {
            byte[] buffer = new byte[length];
            int received = 0;

            while (received < length)
            {
                Task<int> receiveTask = socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer, received, length - received),
                    SocketFlags.None);
                Task completed = await Task.WhenAny(receiveTask, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
                if (completed != receiveTask)
                    throw new TimeoutException("io_uring send pump가 제한 시간 안에 payload를 전달하지 않았습니다.");

                int count = await receiveTask.ConfigureAwait(false);
                if (count == 0)
                    throw new InvalidOperationException("payload 수신 전에 socket이 닫혔습니다.");

                received += count;
            }

            return buffer;
        }

        private sealed class RecordingReceiveHandler : ITransportReceiveHandler
        {
            private readonly int _expectedLength;
            private readonly TaskCompletionSource<byte[]> _completion;

            internal RecordingReceiveHandler(int expectedLength)
            {
                _expectedLength = expectedLength;
                _completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public void OnReceived(IConnection connection, TransportReceiveBuffer buffer)
            {
                if (buffer.Length < _expectedLength)
                    return;

                byte[] copy = new byte[_expectedLength];
                buffer.Span.Slice(0, _expectedLength).CopyTo(copy);
                _completion.TrySetResult(copy);
            }

            public void OnConnectionClosed(IConnection connection)
            {
                _completion.TrySetException(new InvalidOperationException("receive 완료 전에 connection이 닫혔습니다."));
            }

            internal async Task<byte[]> ReceiveAsync()
            {
                Task completed = await Task.WhenAny(_completion.Task, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
                if (completed != _completion.Task)
                    throw new TimeoutException("io_uring receive pump가 제한 시간 안에 payload를 전달하지 않았습니다.");

                return await _completion.Task.ConfigureAwait(false);
            }
        }
    }
}
