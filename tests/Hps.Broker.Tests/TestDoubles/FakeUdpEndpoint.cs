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

        internal int CloseCallCount { get; private set; }

        public EndPoint LocalEndPoint { get; }

        public void Close()
        {
            CloseCallCount++;
        }

        public void Dispose()
        {
        }
    }
}
