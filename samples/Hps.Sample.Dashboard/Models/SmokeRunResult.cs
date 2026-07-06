namespace Hps.Sample.Dashboard.Models
{
    public sealed class SmokeRunResult
    {
        public SmokeRunResult(
            string protocol,
            bool succeeded,
            int sent,
            int received,
            long dropped,
            int payloadErrors,
            int poolRented,
            string message)
        {
            Protocol = protocol;
            Succeeded = succeeded;
            Sent = sent;
            Received = received;
            Dropped = dropped;
            PayloadErrors = payloadErrors;
            PoolRented = poolRented;
            Message = message;
        }

        public string Protocol { get; }

        public bool Succeeded { get; }

        public int Sent { get; }

        public int Received { get; }

        public long Dropped { get; }

        public int PayloadErrors { get; }

        public int PoolRented { get; }

        public string Message { get; }
    }
}
