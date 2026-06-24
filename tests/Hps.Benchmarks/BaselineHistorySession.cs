namespace Hps.Benchmarks
{
    internal sealed class BaselineHistorySession
    {
        public BaselineHistorySession(
            string date,
            string session,
            string summaryPath,
            string? humanReportPath,
            int sourceReportCount,
            bool hardPassed,
            int hardFailureCount,
            int warningCount,
            double? loadP99MaxMicroseconds,
            double? openLoopP99MaxMicroseconds,
            int tcpHighWatermarkMax,
            BaselineComparisonResult? comparison = null)
        {
            Date = date;
            Session = session;
            SummaryPath = summaryPath;
            HumanReportPath = humanReportPath;
            SourceReportCount = sourceReportCount;
            HardPassed = hardPassed;
            HardFailureCount = hardFailureCount;
            WarningCount = warningCount;
            LoadP99MaxMicroseconds = loadP99MaxMicroseconds;
            OpenLoopP99MaxMicroseconds = openLoopP99MaxMicroseconds;
            TcpHighWatermarkMax = tcpHighWatermarkMax;
            Comparison = comparison ?? new BaselineComparisonResult(false, null, 0, new BaselineComparisonMismatch[0]);
        }

        public string Date { get; }

        public string Session { get; }

        public string SummaryPath { get; }

        public string? HumanReportPath { get; }

        public int SourceReportCount { get; }

        public bool HardPassed { get; }

        public int HardFailureCount { get; }

        public int WarningCount { get; }

        public double? LoadP99MaxMicroseconds { get; }

        public double? OpenLoopP99MaxMicroseconds { get; }

        public int TcpHighWatermarkMax { get; }

        public BaselineComparisonResult Comparison { get; }
    }
}
