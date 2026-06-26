namespace Hps.Benchmarks
{
    internal sealed class BenchmarkCommandLine
    {
        public BenchmarkCommandLine(
            BenchmarkCommand command,
            string? reportPath,
            string? baselineOutputDirectory,
            int baselineRunCount,
            string? summaryInputDirectory,
            string? summaryOutputPath,
            string? summaryMarkdownOutputPath,
            string? historyInputRoot,
            string? historyOutputPath,
            string? historyMarkdownOutputPath,
            TcpLoopbackTransportBackend transportBackend = TcpLoopbackTransportBackend.Saea,
            LoopbackProtocol loopbackProtocol = LoopbackProtocol.Tcp)
        {
            Command = command;
            ReportPath = reportPath;
            BaselineOutputDirectory = baselineOutputDirectory;
            BaselineRunCount = baselineRunCount;
            SummaryInputDirectory = summaryInputDirectory;
            SummaryOutputPath = summaryOutputPath;
            SummaryMarkdownOutputPath = summaryMarkdownOutputPath;
            HistoryInputRoot = historyInputRoot;
            HistoryOutputPath = historyOutputPath;
            HistoryMarkdownOutputPath = historyMarkdownOutputPath;
            TransportBackend = transportBackend;
            LoopbackProtocol = loopbackProtocol;
        }

        public BenchmarkCommand Command { get; }

        public string? ReportPath { get; }

        public string? BaselineOutputDirectory { get; }

        public int BaselineRunCount { get; }

        public string? SummaryInputDirectory { get; }

        public string? SummaryOutputPath { get; }

        public string? SummaryMarkdownOutputPath { get; }

        public string? HistoryInputRoot { get; }

        public string? HistoryOutputPath { get; }

        public string? HistoryMarkdownOutputPath { get; }

        public TcpLoopbackTransportBackend TransportBackend { get; }

        public LoopbackProtocol LoopbackProtocol { get; }
    }
}
