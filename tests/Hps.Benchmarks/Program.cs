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
                    return CompleteRun(TcpLoopbackScenarioRunner.RunSmokeAsync().GetAwaiter().GetResult(), commandLine.ReportPath);

                case BenchmarkCommand.Load:
                    return CompleteRun(TcpLoopbackScenarioRunner.RunLoadAsync().GetAwaiter().GetResult(), commandLine.ReportPath);

                case BenchmarkCommand.LoadOpenLoop:
                    return CompleteRun(TcpLoopbackScenarioRunner.RunOpenLoopAsync().GetAwaiter().GetResult(), commandLine.ReportPath);

                case BenchmarkCommand.BaselineSuite:
                    return CompleteBaselineSuite(commandLine.BaselineOutputDirectory!, commandLine.BaselineRunCount);

                case BenchmarkCommand.SummarizeBaseline:
                    return CompleteBaselineSummary(commandLine.SummaryInputDirectory!, commandLine.SummaryOutputPath!);

                case BenchmarkCommand.Help:
                    PrintUsage(Console.Out);
                    return SuccessExitCode;

                default:
                    return UsageErrorExitCode;
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

        private static int CompleteBaselineSuite(string outputDirectory, int runCount)
        {
            BaselineSuiteRunner runner = new BaselineSuiteRunner(
                kind =>
                {
                    if (kind == BaselineRunKind.Load)
                        return TcpLoopbackScenarioRunner.RunLoadAsync();

                    return TcpLoopbackScenarioRunner.RunOpenLoopAsync();
                },
                TcpLoopbackReportWriter.Write);

            bool passed = runner.RunAsync(outputDirectory, runCount, Console.Out).GetAwaiter().GetResult();
            return passed ? SuccessExitCode : FailedRunExitCode;
        }

        private static int CompleteBaselineSummary(string inputDirectory, string summaryPath)
        {
            try
            {
                System.Collections.Generic.IReadOnlyList<BaselineReport> reports = BaselineReportReader.ReadDirectory(inputDirectory);
                BaselineSummary summary = BaselineSummaryGenerator.Generate(inputDirectory, reports);
                BaselineSummaryWriter.Write(summaryPath, summary);
                Console.Out.WriteLine("baseline-summary: {0}", summaryPath);
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

        private static void PrintUsage(TextWriter writer)
        {
            writer.WriteLine(MessageUsage);
            writer.WriteLine("  Hps.Benchmarks --target");
            writer.WriteLine("  Hps.Benchmarks --smoke [--report <path>]");
            writer.WriteLine("  Hps.Benchmarks --load [--report <path>]");
            writer.WriteLine("  Hps.Benchmarks --load-open-loop [--report <path>]");
            writer.WriteLine("  Hps.Benchmarks --baseline-suite <output-dir> [--runs <count>]");
            writer.WriteLine("  Hps.Benchmarks --summarize-baseline <input-dir> --summary <output-json>");
            writer.WriteLine("  Hps.Benchmarks [BenchmarkDotNet arguments]");
        }

    }
}
