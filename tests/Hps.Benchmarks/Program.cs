using System;
using System.IO;
using BenchmarkDotNet.Running;

namespace Hps.Benchmarks
{
    internal static class Program
    {
        private const int SuccessExitCode = 0;
        private const int FailedRunExitCode = 1;
        private const int UsageErrorExitCode = 2;
        private const int ReportWriteFailedExitCode = 2;

        private const string MessageUsage = "사용법";

        public static int Main(string[] args)
        {
            BenchmarkCommandLine commandLine;
            string? errorMessage;

            if (!BenchmarkCommandParser.TryParse(args, out commandLine, out errorMessage))
            {
                BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
                return SuccessExitCode;
            }

            if (errorMessage != null)
            {
                Console.Error.WriteLine(errorMessage);
                PrintUsage(Console.Error);
                return UsageErrorExitCode;
            }

            switch (commandLine.Command)
            {
                case BenchmarkCommand.Target:
                    BenchmarkTargets.Print(Console.Out);
                    return SuccessExitCode;

                case BenchmarkCommand.Smoke:
                    if (commandLine.LoopbackProtocol == LoopbackProtocol.Udp)
                        return CompleteRun(UdpLoopbackScenarioRunner.RunSmokeAsync(commandLine.TransportBackend).GetAwaiter().GetResult(), commandLine.ReportPath);

                    return CompleteRun(TcpLoopbackScenarioRunner.RunSmokeAsync(commandLine.TransportBackend).GetAwaiter().GetResult(), commandLine.ReportPath);

                case BenchmarkCommand.Load:
                    if (commandLine.LoopbackProtocol == LoopbackProtocol.Udp)
                        return CompleteRun(UdpLoopbackScenarioRunner.RunLoadAsync(commandLine.TransportBackend).GetAwaiter().GetResult(), commandLine.ReportPath);

                    return CompleteRun(TcpLoopbackScenarioRunner.RunLoadAsync(commandLine.TransportBackend).GetAwaiter().GetResult(), commandLine.ReportPath);

                case BenchmarkCommand.LoadOpenLoop:
                    if (commandLine.LoopbackProtocol == LoopbackProtocol.Udp)
                        return CompleteRun(UdpLoopbackScenarioRunner.RunOpenLoopAsync(commandLine.TransportBackend).GetAwaiter().GetResult(), commandLine.ReportPath);

                    return CompleteRun(TcpLoopbackScenarioRunner.RunOpenLoopAsync(commandLine.TransportBackend).GetAwaiter().GetResult(), commandLine.ReportPath);

                case BenchmarkCommand.BaselineSuite:
                    return CompleteBaselineSuite(commandLine.BaselineOutputDirectory!, commandLine.BaselineRunCount, commandLine.TransportBackend, commandLine.LoopbackProtocol);

                case BenchmarkCommand.SummarizeBaseline:
                    return CompleteBaselineSummary(commandLine.SummaryInputDirectory!, commandLine.SummaryOutputPath!, commandLine.SummaryMarkdownOutputPath);

                case BenchmarkCommand.SummarizeBaselineHistory:
                    return CompleteBaselineHistory(commandLine.HistoryInputRoot!, commandLine.HistoryOutputPath!, commandLine.HistoryMarkdownOutputPath);

                case BenchmarkCommand.CompareBaselineEnvelope:
                    return CompleteBaselineEnvelope(
                        commandLine.EnvelopeCandidatePath!,
                        commandLine.EnvelopeReferenceHistoryPath!,
                        commandLine.EnvelopeOutputPath!,
                        commandLine.EnvelopeMarkdownOutputPath);

                case BenchmarkCommand.Help:
                    PrintUsage(Console.Out);
                    return SuccessExitCode;

                default:
                    return UsageErrorExitCode;
            }
        }

        private static int CompleteBaselineEnvelope(string candidatePath, string referenceHistoryPath, string envelopePath, string? envelopeMarkdownPath)
        {
            try
            {
                BaselineEnvelopeSource reference = BaselineEnvelopeSourceReader.Read(referenceHistoryPath);
                BaselineEnvelopeSource candidate = BaselineEnvelopeSourceReader.Read(candidatePath);
                BaselineEnvelopeComparison comparison = BaselineEnvelopeComparisonGenerator.Generate(reference, candidate);
                BaselineEnvelopeComparisonWriter.Write(envelopePath, comparison);
                if (envelopeMarkdownPath != null)
                    WriteBaselineEnvelopeMarkdown(envelopeMarkdownPath, comparison);

                Console.Out.WriteLine("baseline-envelope: {0}", envelopePath);
                if (envelopeMarkdownPath != null)
                    Console.Out.WriteLine("baseline-envelope-md: {0}", envelopeMarkdownPath);

                Console.Out.WriteLine("envelope-compatible: {0}", comparison.EnvelopeCompatible ? "true" : "false");
                Console.Out.WriteLine("envelope-signal-count: {0}", comparison.SignalCount);
                return SuccessExitCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("baseline-envelope-error: {0}", ex.Message);
                return ReportWriteFailedExitCode;
            }
        }

        private static int CompleteRun(TcpLoopbackRunResult result, string? reportPath)
        {
            result.Print(Console.Out);

            if (reportPath != null)
            {
                try
                {
                    TcpLoopbackReportWriter.Write(reportPath, result);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("report-write-error: {0}", ex.Message);
                    return ReportWriteFailedExitCode;
                }
            }

            return result.Passed ? SuccessExitCode : FailedRunExitCode;
        }

        private static int CompleteBaselineSuite(string outputDirectory, int runCount, TcpLoopbackTransportBackend transportBackend, LoopbackProtocol loopbackProtocol)
        {
            BaselineSuiteRunner runner = new BaselineSuiteRunner(
                kind =>
                {
                    if (kind == BaselineRunKind.Load)
                    {
                        if (loopbackProtocol == LoopbackProtocol.Udp)
                            return UdpLoopbackScenarioRunner.RunLoadAsync(transportBackend);

                        return TcpLoopbackScenarioRunner.RunLoadAsync(transportBackend);
                    }

                    if (loopbackProtocol == LoopbackProtocol.Udp)
                        return UdpLoopbackScenarioRunner.RunOpenLoopAsync(transportBackend);

                    return TcpLoopbackScenarioRunner.RunOpenLoopAsync(transportBackend);
                },
                TcpLoopbackReportWriter.Write);

            bool passed = runner.RunAsync(outputDirectory, runCount, Console.Out).GetAwaiter().GetResult();
            return passed ? SuccessExitCode : FailedRunExitCode;
        }

        private static int CompleteBaselineSummary(string inputDirectory, string summaryPath, string? summaryMarkdownPath)
        {
            try
            {
                System.Collections.Generic.IReadOnlyList<BaselineReport> reports = BaselineReportReader.ReadDirectory(inputDirectory);
                BaselineSummary summary = BaselineSummaryGenerator.Generate(inputDirectory, reports);
                BaselineSummaryWriter.Write(summaryPath, summary);
                if (summaryMarkdownPath != null)
                    WriteBaselineSummaryMarkdown(summaryMarkdownPath, summary);

                Console.Out.WriteLine("baseline-summary: {0}", summaryPath);
                if (summaryMarkdownPath != null)
                    Console.Out.WriteLine("baseline-summary-md: {0}", summaryMarkdownPath);

                Console.Out.WriteLine("source-report-count: {0}", summary.SourceReportCount);
                Console.Out.WriteLine("hard-passed: {0}", summary.HardPassed ? "true" : "false");
                Console.Out.WriteLine("warning-count: {0}", summary.WarningCount);
                return summary.HardPassed ? SuccessExitCode : FailedRunExitCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("baseline-summary-error: {0}", ex.Message);
                return ReportWriteFailedExitCode;
            }
        }

        private static int CompleteBaselineHistory(string inputRoot, string historyPath, string? historyMarkdownPath)
        {
            try
            {
                System.Collections.Generic.IReadOnlyList<BaselineHistorySession> sessions = BaselineHistoryReader.ReadSessions(inputRoot);
                BaselineHistory history = BaselineHistoryGenerator.Generate(inputRoot, sessions);
                BaselineHistoryWriter.Write(historyPath, history);
                if (historyMarkdownPath != null)
                    WriteBaselineHistoryMarkdown(historyMarkdownPath, history);

                Console.Out.WriteLine("baseline-history: {0}", historyPath);
                if (historyMarkdownPath != null)
                    Console.Out.WriteLine("baseline-history-md: {0}", historyMarkdownPath);

                Console.Out.WriteLine("session-count: {0}", history.SessionCount);
                Console.Out.WriteLine("hard-passed: {0}", history.HardPassed ? "true" : "false");
                Console.Out.WriteLine("warning-count: {0}", history.WarningCount);
                return history.HardPassed ? SuccessExitCode : FailedRunExitCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("baseline-history-error: {0}", ex.Message);
                return ReportWriteFailedExitCode;
            }
        }

        private static void WriteBaselineEnvelopeMarkdown(string path, BaselineEnvelopeComparison comparison)
        {
            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (StreamWriter writer = new StreamWriter(fullPath, false))
            {
                BaselineEnvelopeComparisonMarkdownWriter.Write(writer, comparison);
            }
        }

        private static void WriteBaselineHistoryMarkdown(string path, BaselineHistory history)
        {
            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (StreamWriter writer = new StreamWriter(fullPath, false))
            {
                BaselineHistoryMarkdownWriter.Write(writer, history);
            }
        }

        private static void WriteBaselineSummaryMarkdown(string path, BaselineSummary summary)
        {
            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (StreamWriter writer = new StreamWriter(fullPath, false))
            {
                BaselineSummaryMarkdownWriter.Write(writer, summary);
            }
        }

        private static void PrintUsage(TextWriter writer)
        {
            writer.WriteLine(MessageUsage);
            writer.WriteLine("  Hps.Benchmarks --target");
            writer.WriteLine("  Hps.Benchmarks --smoke [--protocol <tcp|udp>] [--backend <saea|rio>] [--report <path>]");
            writer.WriteLine("  Hps.Benchmarks --load [--protocol <tcp|udp>] [--backend <saea|rio>] [--report <path>]");
            writer.WriteLine("  Hps.Benchmarks --load-open-loop [--protocol <tcp|udp>] [--backend <saea|rio>] [--report <path>]");
            writer.WriteLine("  Hps.Benchmarks --baseline-suite <output-dir> [--runs <count>] [--protocol <tcp|udp>] [--backend <saea|rio>]");
            writer.WriteLine("  Hps.Benchmarks --summarize-baseline <input-dir> --summary <output-json> [--summary-md <output-md>]");
            writer.WriteLine("  Hps.Benchmarks --summarize-baseline-history <baseline-root> --history <output-json> [--history-md <output-md>]");
            writer.WriteLine("  Hps.Benchmarks --compare-baseline-envelope <candidate-json> --reference-history <reference-history-json> --envelope <output-json> [--envelope-md <output-md>]");
            writer.WriteLine("  Hps.Benchmarks [BenchmarkDotNet arguments]");
        }

    }
}
