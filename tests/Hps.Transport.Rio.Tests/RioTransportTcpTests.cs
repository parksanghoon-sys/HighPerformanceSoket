using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
    }
}
