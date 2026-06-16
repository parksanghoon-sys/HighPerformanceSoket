using System.Net;
using Hps.Transport;

namespace Hps.Broker.Tests
{
    internal sealed class FakeUdpEndpoint : IUdpEndpoint
    {
        internal FakeUdpEndpoint(EndPoint localEndPoint)
        {
            LocalEndPoint = localEndPoint;
        }

        public EndPoint LocalEndPoint { get; }

        public void Close()
        {
        }

        public void Dispose()
        {
        }
    }
}
