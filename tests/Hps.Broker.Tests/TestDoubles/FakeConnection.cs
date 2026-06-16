using Hps.Transport;

namespace Hps.Broker.Tests
{
    internal sealed class FakeConnection : IConnection
    {
        internal int CloseCallCount { get; private set; }

        public void Close()
        {
            CloseCallCount++;
        }

        public void Dispose()
        {
        }
    }
}
