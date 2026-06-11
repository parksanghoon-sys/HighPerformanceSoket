using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hps.Transport;
using Xunit;

namespace Hps.Transport.Tests
{
    public sealed class SaeaTransportTests
    {
        // TCP loopback 기준선 테스트: SAEA 백엔드의 첫 책임은 실제 payload 송수신 전에
        // listener 를 열고 outbound connect 와 inbound accept 로 양쪽 IConnection 을 만들 수 있는지 증명하는 것이다.
        [Fact]
        public async Task ListenConnectAccept_WhenLoopbackTcp_CreatesInboundAndOutboundConnections()
        {
            using (SaeaTransport transport = new SaeaTransport())
            {
                await transport.StartAsync();

                IConnectionListener? listener = null;
                IConnection? outbound = null;
                IConnection? inbound = null;

                try
                {
                    using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    {
                        listener = await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0), timeout.Token);
                        IPEndPoint boundEndPoint = Assert.IsType<IPEndPoint>(listener.LocalEndPoint);
                        Assert.NotEqual(0, boundEndPoint.Port);

                        ValueTask<IConnection> accept = listener.AcceptAsync(timeout.Token);
                        outbound = await transport.ConnectTcpAsync(boundEndPoint, timeout.Token);
                        inbound = await accept;

                        Assert.NotNull(outbound);
                        Assert.NotNull(inbound);
                        Assert.NotSame(outbound, inbound);
                    }
                }
                finally
                {
                    outbound?.Close();
                    inbound?.Close();
                    listener?.Close();
                    await transport.StopAsync();
                }
            }
        }
    }
}
