using Hps.Sample.Dashboard.Models;
using Hps.Transport;

namespace Hps.Sample.Dashboard.Services
{
    public sealed class DiagnosticsSnapshotService
    {
        public TransportMetricRow[] CreateRows(object? diagnosticsSource)
        {
            ITransportDiagnostics? diagnostics = diagnosticsSource as ITransportDiagnostics;
            if (diagnostics == null)
                return new TransportMetricRow[0];

            TransportDiagnosticsSnapshot snapshot = diagnostics.GetDiagnosticsSnapshot();

            return new TransportMetricRow[]
            {
                new TransportMetricRow(
                    "TCP",
                    0,
                    snapshot.TcpPendingSendQueueHighWatermark,
                    snapshot.TcpDroppedPendingSendCount),
                new TransportMetricRow(
                    "UDP",
                    0,
                    snapshot.UdpPendingSendQueueHighWatermark,
                    snapshot.UdpDroppedPendingSendCount)
            };
        }
    }
}
