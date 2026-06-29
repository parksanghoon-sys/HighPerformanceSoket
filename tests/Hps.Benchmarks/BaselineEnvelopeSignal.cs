namespace Hps.Benchmarks
{
    internal sealed class BaselineEnvelopeSignal
    {
        public BaselineEnvelopeSignal(
            string kind,
            string metric,
            string direction,
            double reference,
            double limit,
            double candidate)
        {
            Kind = kind;
            Metric = metric;
            Direction = direction;
            Reference = reference;
            Limit = limit;
            Candidate = candidate;
        }

        public string Kind { get; }

        public string Metric { get; }

        public string Direction { get; }

        public double Reference { get; }

        public double Limit { get; }

        public double Candidate { get; }
    }
}
