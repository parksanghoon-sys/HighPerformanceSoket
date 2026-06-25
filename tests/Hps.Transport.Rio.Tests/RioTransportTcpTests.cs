using System;
using System.Net;
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

            RecordingReceiveHandler handler = new RecordingReceiveHandler();
            using (RioTransport transport = new RioTransport())
            {
                transport.SetReceiveHandler(handler);
                await transport.StartAsync();

                IConnectionListener listener = await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                IConnection client = await transport.ConnectTcpAsync(listener.LocalEndPoint);
                IConnection server = await listener.AcceptAsync();

                PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
                RefCountedBuffer buffer = pool.RentCounted();
                buffer.Span[0] = 11;
                buffer.Span[1] = 22;
                buffer.SetLength(2);
                buffer.AddRef();

                Assert.True(transport.TrySend(client, new TransportSendBuffer(buffer, 0, 2)));
                buffer.Release();

                byte[] received = await handler.ReceiveAsync();

                Assert.Equal(new byte[] { 11, 22 }, received);
                client.Close();
                server.Close();
                listener.Close();
                await transport.StopAsync();
                Assert.Equal(0, pool.RentedCount);
            }
        }

        private sealed class RecordingReceiveHandler : ITransportReceiveHandler
        {
            private readonly TaskCompletionSource<byte[]> _received;

            internal RecordingReceiveHandler()
            {
                _received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public void OnReceived(IConnection connection, TransportReceiveBuffer receiveBuffer)
            {
                byte[] payload = receiveBuffer.Span.ToArray();
                _received.TrySetResult(payload);
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
    }
}
