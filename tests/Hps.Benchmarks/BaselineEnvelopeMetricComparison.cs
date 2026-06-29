namespace Hps.Benchmarks
{
    internal sealed class BaselineEnvelopeMetricComparison
    {
        public BaselineEnvelopeMetricComparison(
            string metric,
            string direction,
            double reference,
            double limit,
            double candidate,
            bool signaled)
        {
            Metric = metric;
            Direction = direction;
            Reference = reference;
            Limit = limit;
            Candidate = candidate;
            Signaled = signaled;
        }

        public string Metric { get; }

        public string Direction { get; }

        public double Reference { get; }

        public double Limit { get; }

        public double Candidate { get; }

        public bool Signaled { get; }
    }
}
