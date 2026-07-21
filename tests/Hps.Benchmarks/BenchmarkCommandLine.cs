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
            LoopbackProtocol loopbackProtocol = LoopbackProtocol.Tcp,
            int mixedDataRateHz = MixedWorkloadOptions.DefaultDataRateHz,
            int mixedDurationSeconds = MixedWorkloadOptions.DefaultDurationSeconds,
            int mixedSubscriberCount = MixedWorkloadOptions.DefaultSubscriberCount)
            : this(
                command,
                reportPath,
                baselineOutputDirectory,
                baselineRunCount,
                summaryInputDirectory,
                summaryOutputPath,
                summaryMarkdownOutputPath,
                historyInputRoot,
                historyOutputPath,
                historyMarkdownOutputPath,
                null,
                null,
                null,
                null,
                transportBackend,
                loopbackProtocol,
                mixedDataRateHz,
                mixedDurationSeconds,
                mixedSubscriberCount)
        {
        }

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
            string? envelopeCandidatePath,
            string? envelopeReferenceHistoryPath,
            string? envelopeOutputPath,
            string? envelopeMarkdownOutputPath,
            TcpLoopbackTransportBackend transportBackend = TcpLoopbackTransportBackend.Saea,
            LoopbackProtocol loopbackProtocol = LoopbackProtocol.Tcp,
            int mixedDataRateHz = MixedWorkloadOptions.DefaultDataRateHz,
            int mixedDurationSeconds = MixedWorkloadOptions.DefaultDurationSeconds,
            int mixedSubscriberCount = MixedWorkloadOptions.DefaultSubscriberCount)
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
            EnvelopeCandidatePath = envelopeCandidatePath;
            EnvelopeReferenceHistoryPath = envelopeReferenceHistoryPath;
            EnvelopeOutputPath = envelopeOutputPath;
            EnvelopeMarkdownOutputPath = envelopeMarkdownOutputPath;
            TransportBackend = transportBackend;
            LoopbackProtocol = loopbackProtocol;
            MixedDataRateHz = mixedDataRateHz;
            MixedDurationSeconds = mixedDurationSeconds;
            MixedSubscriberCount = mixedSubscriberCount;
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

        public string? EnvelopeCandidatePath { get; }

        public string? EnvelopeReferenceHistoryPath { get; }

        public string? EnvelopeOutputPath { get; }

        public string? EnvelopeMarkdownOutputPath { get; }

        public TcpLoopbackTransportBackend TransportBackend { get; }

        public LoopbackProtocol LoopbackProtocol { get; }

        public int MixedDataRateHz { get; }

        public int MixedDurationSeconds { get; }

        public int MixedSubscriberCount { get; }
    }
}
