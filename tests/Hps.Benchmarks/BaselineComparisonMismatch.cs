namespace Hps.Benchmarks
{
    internal sealed class BaselineComparisonMismatch
    {
        public BaselineComparisonMismatch(
            string code,
            string field,
            string expected,
            string actual,
            string? sourcePath,
            string? session,
            string? summaryPath)
        {
            Code = code;
            Field = field;
            Expected = expected;
            Actual = actual;
            SourcePath = sourcePath;
            Session = session;
            SummaryPath = summaryPath;
        }

        public string Code { get; }

        public string Field { get; }

        public string Expected { get; }

        public string Actual { get; }

        public string? SourcePath { get; }

        public string? Session { get; }

        public string? SummaryPath { get; }
    }
}
