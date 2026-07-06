namespace Hps.Sample.Dashboard.Models
{
    public sealed class TransportMetricRow
    {
        public TransportMetricRow(
            string name,
            int pendingSendCount,
            long pendingSendQueueHighWatermark,
            long droppedPendingSendCount)
        {
            Name = name;
            PendingSendCount = pendingSendCount;
            PendingSendQueueHighWatermark = pendingSendQueueHighWatermark;
            DroppedPendingSendCount = droppedPendingSendCount;
        }

        public string Name { get; }

        public int PendingSendCount { get; }

        public long PendingSendQueueHighWatermark { get; }

        public long DroppedPendingSendCount { get; }
    }
}
