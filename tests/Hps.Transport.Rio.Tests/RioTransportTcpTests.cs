using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Hps.Buffers;
using Xunit;

namespace Hps.Transport.Rio.Tests
{
    public sealed class RioTransportTcpTests
    {
        // RIO TCP wiring은 Windows/RIO available 환경에서만 실제 loopback으로 검증한다.
        // unavailable 환경에서는 opt-in backend가 capability failure를 명시해야 fallback 판단이 가능하다.
        [Fact]
        public async Task ListenTcpAsync_WhenRioUnavailable_ThrowsNotSupportedException()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                RioCapabilityProbe.GetStatus() == RioCapabilityStatus.Available)
            {
                return;
            }

            using (RioTransport transport = new RioTransport())
            {
                await transport.StartAsync();

                NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(async delegate()
                {
                    await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                });

                Assert.Contains("RIO function table", exception.Message, StringComparison.Ordinal);
            }
        }

        // RIO transport 는 native socket 경계만이 아니라 ITransport 계약에서도 receive/send loopback 을 만족해야 한다.
        // 이 테스트는 RIO available 환경에서 TrySend payload 가 peer connection 의 receive handler 로 도착하는지 검증한다.
        [Fact]
        public async Task TcpLoopback_WhenRioAvailable_DeliversPayload()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
            {
                return;
            }

            byte[] received = await SendAndReceiveAsync(new byte[] { 11, 22 }, prependLengthPrefix: false, expectedReceiveLength: 2);

            Assert.Equal(new byte[] { 11, 22 }, received);
        }

        // RIO send path 가 작은 smoke payload 뿐 아니라 receive block 크기에 가까운 payload 도 그대로 전달하는지 확인한다.
        // partial completion 을 강제하지는 못하지만, byte-count loop 보강 후 큰 payload 경로가 회귀하지 않도록 잡는다.
        [Fact]
        public async Task TcpLoopback_WhenRioAvailable_DeliversLargePayload()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
            {
                return;
            }

            byte[] payload = new byte[4096];
            for (int i = 0; i < payload.Length; i++)
                payload[i] = (byte)(i % 251);

            byte[] received = await SendAndReceiveAsync(payload, prependLengthPrefix: false, expectedReceiveLength: payload.Length);

            Assert.Equal(payload, received);
        }

        // Broker TCP outbound 는 D065에 따라 length prefix 를 붙여 보낸다.
        // RIO opt-in backend 도 같은 TransportSendBuffer metadata 를 해석해야 상위 Broker 경로를 나중에 재사용할 수 있다.
        [Fact]
        public async Task TcpLoopback_WhenRioAvailable_DeliversLengthPrefixedPayload()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
            {
                return;
            }

            byte[] received = await SendAndReceiveAsync(new byte[] { 5, 6, 7 }, prependLengthPrefix: true, expectedReceiveLength: 7);

            Assert.Equal(new byte[] { 0, 0, 0, 3, 5, 6, 7 }, received);
        }

        // RIO close 경로는 receive pump 가 이미 RIOReceive 를 post 한 상태에서 socket/CQ 정리와 경합할 수 있다.
        // 이 테스트는 짧은 connect/accept/close churn 을 반복해 testhost crash 없이 모든 resource close 가 끝나는지 검증한다.
        [Fact]
        public async Task TcpLoopback_WhenRioAvailable_RepeatedCloseAfterAcceptDoesNotCrash()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
            {
                return;
            }

            for (int i = 0; i < 25; i++)
            {
                using (RioTransport transport = new RioTransport())
                {
                    transport.SetReceiveHandler(new RecordingReceiveHandler(expectedLength: 1));
                    await transport.StartAsync();

                    IConnectionListener listener = await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                    IConnection client = await transport.ConnectTcpAsync(listener.LocalEndPoint);
                    IConnection server = await listener.AcceptAsync();

                    client.Close();
                    server.Close();
                    listener.Close();
                    await transport.StopAsync();
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        // Receive handler 예외는 상위 broker cleanup 을 끊지 않도록 connection close 알림으로 수렴해야 한다.
        // SAEA와 같은 handler-failure 계약을 RIO receive pump 에도 고정해 backend 별 비대칭을 막는다.
        [Fact]
        public async Task ReceivePump_WhenRioAvailable_HandlerThrowsClosesConnectionAndNotifiesHandler()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
            {
                return;
            }

            ThrowingReceiveHandler handler = new ThrowingReceiveHandler();
            using (RioTransport transport = new RioTransport())
            {
                transport.SetReceiveHandler(handler);
                await transport.StartAsync();

                IConnectionListener listener = await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                IConnection client = await transport.ConnectTcpAsync(listener.LocalEndPoint);
                IConnection server = await listener.AcceptAsync();

                PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
                RefCountedBuffer buffer = pool.RentCounted();
                buffer.Span[0] = 41;
                buffer.SetLength(1);
                buffer.AddRef();

                Assert.True(transport.TrySend(client, new TransportSendBuffer(buffer, 0, 1)));
                buffer.Release();

                IConnection closedConnection = await WaitForClosedConnectionAsync(handler.ClosedTask);

                Assert.Same(server, closedConnection);
                Assert.Equal(1, handler.ClosedCallCount);

                client.Close();
                server.Close();
                listener.Close();
                await transport.StopAsync();
                Assert.Equal(0, pool.RentedCount);
            }
        }

        private static async Task<byte[]> SendAndReceiveAsync(byte[] payload, bool prependLengthPrefix, int expectedReceiveLength)
        {
            RecordingReceiveHandler handler = new RecordingReceiveHandler(expectedReceiveLength);
            using (RioTransport transport = new RioTransport())
            {
                transport.SetReceiveHandler(handler);
                await transport.StartAsync();

                IConnectionListener listener = await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                IConnection client = await transport.ConnectTcpAsync(listener.LocalEndPoint);
                IConnection server = await listener.AcceptAsync();

                PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(Math.Max(16, payload.Length));
                RefCountedBuffer buffer = pool.RentCounted();
                payload.CopyTo(buffer.Span);
                buffer.SetLength(payload.Length);
                buffer.AddRef();

                TransportSendBuffer sendBuffer = new TransportSendBuffer(buffer, 0, payload.Length);
                if (prependLengthPrefix)
                    sendBuffer = sendBuffer.WithLengthPrefix();

                Assert.True(transport.TrySend(client, sendBuffer));
                buffer.Release();

                byte[] received = await handler.ReceiveAsync();

                client.Close();
                server.Close();
                listener.Close();
                await transport.StopAsync();
                Assert.Equal(0, pool.RentedCount);
                return received;
            }
        }

        private sealed class RecordingReceiveHandler : ITransportReceiveHandler
        {
            private readonly object _gate;
            private readonly byte[] _receivedBytes;
            private readonly TaskCompletionSource<byte[]> _received;
            private int _receivedLength;

            internal RecordingReceiveHandler(int expectedLength)
            {
                _gate = new object();
                _receivedBytes = new byte[expectedLength];
                _received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public void OnReceived(IConnection connection, TransportReceiveBuffer receiveBuffer)
            {
                lock (_gate)
                {
                    int copyLength = Math.Min(receiveBuffer.Length, _receivedBytes.Length - _receivedLength);
                    receiveBuffer.Span.Slice(0, copyLength).CopyTo(new Span<byte>(_receivedBytes, _receivedLength, copyLength));
                    _receivedLength += copyLength;

                    if (_receivedLength == _receivedBytes.Length)
                        _received.TrySetResult((byte[])_receivedBytes.Clone());
                }
            }

            public void OnConnectionClosed(IConnection connection)
            {
            }

            internal async Task<byte[]> ReceiveAsync()
            {
                Task completed = await Task.WhenAny(_received.Task, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
                if (!object.ReferenceEquals(completed, _received.Task))
                    throw new TimeoutException("RIO TCP loopback receive completion을 제한 시간 안에 관측하지 못했습니다.");

                return await _received.Task.ConfigureAwait(false);
            }
        }

        private static async Task<IConnection> WaitForClosedConnectionAsync(Task<IConnection> closedTask)
        {
            Task completed = await Task.WhenAny(closedTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            if (!object.ReferenceEquals(completed, closedTask))
                throw new TimeoutException("RIO TCP handler 예외 후 close notification 을 제한 시간 안에 관측하지 못했습니다.");

            return await closedTask.ConfigureAwait(false);
        }

        private sealed class ThrowingReceiveHandler : ITransportReceiveHandler
        {
            private readonly TaskCompletionSource<IConnection> _closed;
            private int _closedCallCount;

            internal ThrowingReceiveHandler()
            {
                _closed = new TaskCompletionSource<IConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            internal Task<IConnection> ClosedTask
            {
                get { return _closed.Task; }
            }

            internal int ClosedCallCount
            {
                get { return Volatile.Read(ref _closedCallCount); }
            }

            public void OnReceived(IConnection connection, TransportReceiveBuffer receiveBuffer)
            {
                throw new InvalidOperationException("테스트용 receive handler failure 입니다.");
            }

            public void OnConnectionClosed(IConnection connection)
            {
                Interlocked.Increment(ref _closedCallCount);
                _closed.TrySetResult(connection);
            }
        }
    }
}
