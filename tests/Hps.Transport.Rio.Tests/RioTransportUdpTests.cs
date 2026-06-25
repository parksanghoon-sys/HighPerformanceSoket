using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
    }
}
