namespace Hps.Benchmarks
{
    internal sealed class BaselineWarning
    {
        public BaselineWarning(string code, string kind, string metric, double value, double threshold, string sourcePath)
        {
            Code = code;
            Kind = kind;
            Metric = metric;
            Value = value;
            Threshold = threshold;
            SourcePath = sourcePath;
        }

        public string Code { get; }

        public string Kind { get; }

        public string Metric { get; }

        public double Value { get; }

        public double Threshold { get; }

        public string SourcePath { get; }
    }
}
