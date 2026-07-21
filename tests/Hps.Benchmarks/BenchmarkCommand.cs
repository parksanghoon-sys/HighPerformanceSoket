namespace Hps.Benchmarks
{
    internal enum BenchmarkCommand
    {
        None,
        Target,
        Smoke,
        Load,
        LoadOpenLoop,
        MixedLoadOpenLoop,
        BaselineSuite,
        SummarizeBaseline,
        SummarizeBaselineHistory,
        CompareBaselineEnvelope,
        Help
    }
}
