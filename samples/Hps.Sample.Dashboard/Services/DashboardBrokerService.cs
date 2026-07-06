using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hps.Buffers;
using Hps.Server;
using Hps.Transport;

namespace Hps.Sample.Dashboard.Services
{
    public sealed class DashboardBrokerService : IDisposable
    {
        private const int MaxFrameBytes = 65536;

        private SaeaTransport? _transport;
        private BrokerServer? _server;

        public EndPoint? TcpLocalEndPoint
        {
            get
            {
                BrokerServer? server = _server;
                return server == null ? null : server.LocalEndPoint;
            }
        }

        public EndPoint? UdpLocalEndPoint
        {
            get
            {
                BrokerServer? server = _server;
                return server == null ? null : server.UdpLocalEndPoint;
            }
        }

        public object? DiagnosticsSource
        {
            get { return _transport; }
        }

        public async ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            if (_server != null)
                return;

            SaeaTransport transport = new SaeaTransport();
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(MaxFrameBytes);
            BrokerServer server = new BrokerServer(transport, pool, MaxFrameBytes);

            try
            {
                await server.StartTcpAsync(new IPEndPoint(IPAddress.Loopback, 0), cancellationToken).ConfigureAwait(false);
                await server.StartUdpAsync(new IPEndPoint(IPAddress.Loopback, 0), cancellationToken).ConfigureAwait(false);

                _transport = transport;
                _server = server;
            }
            catch
            {
                server.Dispose();
                transport.Dispose();
                throw;
            }
        }

        public async ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            BrokerServer? server = _server;
            SaeaTransport? transport = _transport;

            _server = null;
            _transport = null;

            if (server == null)
                return;

            await server.StopAsync(cancellationToken).ConfigureAwait(false);
            server.Dispose();
            if (transport != null)
                transport.Dispose();
        }

        public void Dispose()
        {
            StopAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
