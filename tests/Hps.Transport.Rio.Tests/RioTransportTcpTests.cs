using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
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

        // 같은 RIO connection 에서 receive block 은 connection resource lifetime 동안 한 번만 등록되어야 한다.
        // 첫 payload 처리 뒤 두 번째 receive 를 다시 post 할 때 registration counter 가 늘어나면,
        // receive path 가 아직 per-operation RegisterBuffer/DeregisterBuffer 비용을 내고 있다는 뜻이다.
        [Fact]
        public async Task TcpLoopback_WhenRioAvailable_ReusesReceiveBufferRegistrationAcrossPayloads()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
            {
                return;
            }

            RioBufferRegistrationDiagnostics diagnostics = GetRioBufferRegistrationDiagnostics();
            long registrations = await SendTwoSingleBytePayloadsAsync(prependLengthPrefix: false, expectedReceiveLength: 2, diagnostics);

            Assert.Equal(2, registrations);
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

        // TCP outbound frame 의 4-byte length prefix 는 send pump 전용 scratch buffer 이므로
        // connection resource lifetime 에 한 번 등록해 재사용할 수 있다.
        // 이 테스트는 payload 두 번 전송에서 payload registration 두 번만 남고 prefix registration 이 반복되지 않는지 검증한다.
        [Fact]
        public async Task TcpLoopback_WhenRioAvailable_ReusesLengthPrefixRegistrationAcrossPayloads()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
            {
                return;
            }

            RioBufferRegistrationDiagnostics diagnostics = GetRioBufferRegistrationDiagnostics();
            long registrations = await SendTwoSingleBytePayloadsAsync(prependLengthPrefix: true, expectedReceiveLength: 10, diagnostics);

            Assert.Equal(2, registrations);
        }

        // 같은 backing payload block 을 같은 RIO connection 으로 두 번 보낼 때 payload registration 은 한 번만 일어나야 한다.
        // receive/prefix resource registration 은 connection setup 전에 reset 되므로, reset 이후 증가는 payload cache miss 만 의미한다.
        [Fact]
        public async Task TcpLoopback_WhenRioAvailable_ReusesPayloadRegistrationForSameBackingBlock()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
            {
                return;
            }

            RioBufferRegistrationDiagnostics diagnostics = GetRioBufferRegistrationDiagnostics();
            long registrations = await SendSameBackingPayloadTwiceAsync(diagnostics);

            Assert.Equal(1, registrations);
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

                await handler.WaitUntilClosedAsync(server);
                Assert.Equal(1, handler.GetClosedCallCount(server));

                client.Close();
                server.Close();
                listener.Close();
                await transport.StopAsync();
                Assert.Equal(0, pool.RentedCount);
            }
        }

        // RIO completion pump 가 빈 CQ를 만났을 때 곧바로 timer sleep 으로 내려가면 Windows timer granularity 때문에
        // 작은 loopback 메시지도 15ms 안팎의 wake 지연을 보인다. 이 테스트는 connection setup 시간을 제외한
        // TrySend→OnReceived 구간만 측정해 completion wake 가 timer-scale 지연에 묶이지 않는지 확인한다.
        [Fact]
        public async Task TcpLoopback_WhenRioAvailable_DeliversSmallPayloadWithoutTimerScaleWake()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
            {
                return;
            }

            TimeSpan first = await MeasureSinglePayloadDeliveryAsync();
            TimeSpan second = await MeasureSinglePayloadDeliveryAsync();
            TimeSpan third = await MeasureSinglePayloadDeliveryAsync();

            TimeSpan median = Median(first, second, third);
            Assert.True(
                median < TimeSpan.FromMilliseconds(12),
                "RIO small-payload completion wake 가 timer-scale 지연을 보였습니다. " +
                "samples(ms): " +
                first.TotalMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ", " +
                second.TotalMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ", " +
                third.TotalMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
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

        private static async Task<long> SendTwoSingleBytePayloadsAsync(
            bool prependLengthPrefix,
            int expectedReceiveLength,
            RioBufferRegistrationDiagnostics diagnostics)
        {
            RecordingReceiveHandler handler = new RecordingReceiveHandler(expectedReceiveLength);
            using (RioTransport transport = new RioTransport())
            {
                transport.SetReceiveHandler(handler);
                await transport.StartAsync();

                IConnectionListener listener = await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                IConnection client = await transport.ConnectTcpAsync(listener.LocalEndPoint);
                IConnection server = await listener.AcceptAsync();

                // connection setup 시점의 receive/prefix resource 등록은 이번 검증 범위가 아니다.
                // reset 뒤에는 실제 payload send 와 receive 재등록 여부만 관측한다.
                diagnostics.Reset();

                PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
                RefCountedBuffer first = CreateSingleByteBuffer(pool, 31);
                RefCountedBuffer second = CreateSingleByteBuffer(pool, 32);

                TransportSendBuffer firstSend = new TransportSendBuffer(first, 0, 1);
                TransportSendBuffer secondSend = new TransportSendBuffer(second, 0, 1);
                if (prependLengthPrefix)
                {
                    firstSend = firstSend.WithLengthPrefix();
                    secondSend = secondSend.WithLengthPrefix();
                }

                Assert.True(transport.TrySend(client, firstSend));
                first.Release();

                await handler.WaitUntilAtLeastAsync(prependLengthPrefix ? 5 : 1);
                await Task.Delay(TimeSpan.FromMilliseconds(20)).ConfigureAwait(false);

                Assert.True(transport.TrySend(client, secondSend));
                second.Release();

                await handler.ReceiveAsync();

                client.Close();
                server.Close();
                listener.Close();
                await transport.StopAsync();
                await WaitForPoolDrainedAsync(pool);
                return diagnostics.RegistrationCount;
            }
        }

        private static async Task<long> SendSameBackingPayloadTwiceAsync(RioBufferRegistrationDiagnostics diagnostics)
        {
            RecordingReceiveHandler handler = new RecordingReceiveHandler(expectedLength: 2);
            using (RioTransport transport = new RioTransport())
            {
                transport.SetReceiveHandler(handler);
                await transport.StartAsync();

                IConnectionListener listener = await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                IConnection client = await transport.ConnectTcpAsync(listener.LocalEndPoint);
                IConnection server = await listener.AcceptAsync();

                diagnostics.Reset();

                PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
                RefCountedBuffer buffer = pool.RentCounted();
                buffer.Span[0] = 51;
                buffer.SetLength(1);

                buffer.AddRef();
                Assert.True(transport.TrySend(client, new TransportSendBuffer(buffer, 0, 1)));
                await handler.WaitUntilAtLeastAsync(1);
                await Task.Delay(TimeSpan.FromMilliseconds(20)).ConfigureAwait(false);

                buffer.Span[0] = 52;
                buffer.AddRef();
                Assert.True(transport.TrySend(client, new TransportSendBuffer(buffer, 0, 1)));

                buffer.Release();
                await handler.ReceiveAsync();

                client.Close();
                server.Close();
                listener.Close();
                await transport.StopAsync();
                await WaitForPoolDrainedAsync(pool);
                return diagnostics.RegistrationCount;
            }
        }

        private static RefCountedBuffer CreateSingleByteBuffer(PinnedBlockMemoryPool pool, byte value)
        {
            RefCountedBuffer buffer = pool.RentCounted();
            buffer.Span[0] = value;
            buffer.SetLength(1);
            buffer.AddRef();
            return buffer;
        }

        private static async Task<TimeSpan> MeasureSinglePayloadDeliveryAsync()
        {
            RecordingReceiveHandler handler = new RecordingReceiveHandler(expectedLength: 1);
            using (RioTransport transport = new RioTransport())
            {
                transport.SetReceiveHandler(handler);
                await transport.StartAsync();

                IConnectionListener listener = await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                IConnection client = await transport.ConnectTcpAsync(listener.LocalEndPoint);
                IConnection server = await listener.AcceptAsync();

                PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
                RefCountedBuffer buffer = pool.RentCounted();
                buffer.Span[0] = 73;
                buffer.SetLength(1);
                buffer.AddRef();

                Stopwatch stopwatch = Stopwatch.StartNew();
                Assert.True(transport.TrySend(client, new TransportSendBuffer(buffer, 0, 1)));
                buffer.Release();
                await handler.ReceiveAsync();
                stopwatch.Stop();

                client.Close();
                server.Close();
                listener.Close();
                await transport.StopAsync();
                await WaitForPoolDrainedAsync(pool);
                return stopwatch.Elapsed;
            }
        }

        private static async Task WaitForPoolDrainedAsync(PinnedBlockMemoryPool pool)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(1);
            while (pool.RentedCount != 0 && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(TimeSpan.FromMilliseconds(1)).ConfigureAwait(false);

            Assert.Equal(0, pool.RentedCount);
        }

        private static TimeSpan Median(TimeSpan first, TimeSpan second, TimeSpan third)
        {
            if (first > second)
                Swap(ref first, ref second);
            if (second > third)
                Swap(ref second, ref third);
            if (first > second)
                Swap(ref first, ref second);

            return second;
        }

        private static void Swap(ref TimeSpan left, ref TimeSpan right)
        {
            TimeSpan temporary = left;
            left = right;
            right = temporary;
        }

        private sealed class RecordingReceiveHandler : ITransportReceiveHandler
        {
            private readonly object _gate;
            private readonly byte[] _receivedBytes;
            private readonly TaskCompletionSource<byte[]> _received;
            private readonly TaskCompletionSource<bool>[] _lengthReached;
            private int _receivedLength;

            internal RecordingReceiveHandler(int expectedLength)
            {
                _gate = new object();
                _receivedBytes = new byte[expectedLength];
                _received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                _lengthReached = new TaskCompletionSource<bool>[expectedLength + 1];
                for (int i = 0; i < _lengthReached.Length; i++)
                    _lengthReached[i] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public void OnReceived(IConnection connection, TransportReceiveBuffer receiveBuffer)
            {
                lock (_gate)
                {
                    int copyLength = Math.Min(receiveBuffer.Length, _receivedBytes.Length - _receivedLength);
                    receiveBuffer.Span.Slice(0, copyLength).CopyTo(new Span<byte>(_receivedBytes, _receivedLength, copyLength));
                    _receivedLength += copyLength;

                    for (int i = 0; i <= _receivedLength; i++)
                        _lengthReached[i].TrySetResult(true);

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

            internal async Task WaitUntilAtLeastAsync(int length)
            {
                Task<bool> waitTask;
                lock (_gate)
                {
                    if (_receivedLength >= length)
                        return;

                    waitTask = _lengthReached[length].Task;
                }

                Task completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
                if (!object.ReferenceEquals(completed, waitTask))
                    throw new TimeoutException("RIO TCP loopback receive progress 를 제한 시간 안에 관측하지 못했습니다.");
            }
        }

        private static RioBufferRegistrationDiagnostics GetRioBufferRegistrationDiagnostics()
        {
            Type nativeType = typeof(RioCapabilityProbe).Assembly.GetType("Hps.Transport.RioNative")!;
            PropertyInfo? registrationCount = nativeType.GetProperty(
                "BufferRegistrationCount",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo? reset = nativeType.GetMethod(
                "ResetBufferRegistrationDiagnostics",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(registrationCount);
            Assert.NotNull(reset);
            return new RioBufferRegistrationDiagnostics(registrationCount!, reset!);
        }

        private sealed class RioBufferRegistrationDiagnostics
        {
            private readonly PropertyInfo _registrationCount;
            private readonly MethodInfo _reset;

            internal RioBufferRegistrationDiagnostics(PropertyInfo registrationCount, MethodInfo reset)
            {
                _registrationCount = registrationCount;
                _reset = reset;
            }

            internal long RegistrationCount
            {
                get { return (long)_registrationCount.GetValue(null)!; }
            }

            internal void Reset()
            {
                _reset.Invoke(null, Array.Empty<object>());
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
            private readonly object _gate;
            private readonly List<IConnection> _closedConnections;
            private readonly TaskCompletionSource<IConnection> _closed;
            private readonly Dictionary<IConnection, TaskCompletionSource<bool>> _closedWaiters;

            internal ThrowingReceiveHandler()
            {
                _gate = new object();
                _closedConnections = new List<IConnection>();
                _closed = new TaskCompletionSource<IConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
                _closedWaiters = new Dictionary<IConnection, TaskCompletionSource<bool>>();
            }

            internal Task<IConnection> ClosedTask
            {
                get { return _closed.Task; }
            }

            internal int GetClosedCallCount(IConnection connection)
            {
                lock (_gate)
                {
                    int count = 0;
                    for (int i = 0; i < _closedConnections.Count; i++)
                    {
                        if (object.ReferenceEquals(_closedConnections[i], connection))
                            count++;
                    }

                    return count;
                }
            }

            public void OnReceived(IConnection connection, TransportReceiveBuffer receiveBuffer)
            {
                throw new InvalidOperationException("테스트용 receive handler failure 입니다.");
            }

            public void OnConnectionClosed(IConnection connection)
            {
                TaskCompletionSource<bool>? waiter = null;
                lock (_gate)
                {
                    _closedConnections.Add(connection);
                    _closedWaiters.TryGetValue(connection, out waiter);
                }

                if (waiter != null)
                    waiter.TrySetResult(true);

                _closed.TrySetResult(connection);
            }

            internal async Task WaitUntilClosedAsync(IConnection connection)
            {
                TaskCompletionSource<bool> waiter;
                lock (_gate)
                {
                    for (int i = 0; i < _closedConnections.Count; i++)
                    {
                        if (object.ReferenceEquals(_closedConnections[i], connection))
                            return;
                    }

                    if (!_closedWaiters.TryGetValue(connection, out waiter!))
                    {
                        waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        _closedWaiters.Add(connection, waiter);
                    }
                }

                Task completed = await Task.WhenAny(waiter.Task, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
                if (!object.ReferenceEquals(completed, waiter.Task))
                    throw new TimeoutException("RIO TCP server connection close notification 을 제한 시간 안에 관측하지 못했습니다.");
            }
        }
    }
}
