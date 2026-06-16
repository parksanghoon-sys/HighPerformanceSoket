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

        private const string MessageReportOnlyWithRuns = "--report \uC635\uC158\uC740 --smoke, --load, --load-open-loop \uB4A4\uC5D0\uC11C\uB9CC \uC0AC\uC6A9\uD560 \uC218 \uC788\uC2B5\uB2C8\uB2E4.";
        private const string MessageUnknownRunnerArgs = "\uC54C \uC218 \uC5C6\uB294 benchmark runner \uC778\uC790\uC785\uB2C8\uB2E4.";
        private const string MessageReportPathRequired = "--report \uC635\uC158\uC5D0\uB294 \uC800\uC7A5\uD560 \uD30C\uC77C \uACBD\uB85C\uAC00 \uD544\uC694\uD569\uB2C8\uB2E4.";
        private const string MessageReportExecutionOnly = "--report \uC635\uC158\uC740 benchmark \uC2E4\uD589 \uBA85\uB839\uC5D0\uC11C\uB9CC \uC0AC\uC6A9\uD560 \uC218 \uC788\uC2B5\uB2C8\uB2E4.";
        private const string MessageUsage = "\uC0AC\uC6A9\uBC95";

        public static int Main(string[] args)
        {
            BenchmarkCommand command;
            string? reportPath;
            string? errorMessage;

            if (!TryParseKnownCommand(args, out command, out reportPath, out errorMessage))
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

            if (command == BenchmarkCommand.Target)
            {
                BenchmarkTargets.Print(Console.Out);
                return SuccessExitCode;
            }

            if (command == BenchmarkCommand.Smoke)
            {
                TcpLoopbackRunResult result = TcpLoopbackScenarioRunner.RunSmokeAsync().GetAwaiter().GetResult();
                return CompleteRun(result, reportPath);
            }

            if (command == BenchmarkCommand.Load)
            {
                TcpLoopbackRunResult result = TcpLoopbackScenarioRunner.RunLoadAsync().GetAwaiter().GetResult();
                return CompleteRun(result, reportPath);
            }

            if (command == BenchmarkCommand.LoadOpenLoop)
            {
                TcpLoopbackRunResult result = TcpLoopbackScenarioRunner.RunOpenLoopAsync().GetAwaiter().GetResult();
                return CompleteRun(result, reportPath);
            }

            if (command == BenchmarkCommand.Help)
            {
                PrintUsage(Console.Out);
                return SuccessExitCode;
            }

            return UsageErrorExitCode;
        }

        private static bool TryParseKnownCommand(
            string[] args,
            out BenchmarkCommand command,
            out string? reportPath,
            out string? errorMessage)
        {
            command = BenchmarkCommand.None;
            reportPath = null;
            errorMessage = null;

            if (args.Length == 0)
                return false;

            string commandArg = args[0];

            if (string.Equals(commandArg, "--help", StringComparison.OrdinalIgnoreCase))
            {
                command = BenchmarkCommand.Help;
                return ValidateNoReportOption(args, out errorMessage);
            }

            if (string.Equals(commandArg, "--target", StringComparison.OrdinalIgnoreCase))
            {
                command = BenchmarkCommand.Target;
                return ValidateNoReportOption(args, out errorMessage);
            }

            if (string.Equals(commandArg, "--smoke", StringComparison.OrdinalIgnoreCase))
            {
                command = BenchmarkCommand.Smoke;
                return TryParseOptionalReport(args, out reportPath, out errorMessage);
            }

            if (string.Equals(commandArg, "--load", StringComparison.OrdinalIgnoreCase))
            {
                command = BenchmarkCommand.Load;
                return TryParseOptionalReport(args, out reportPath, out errorMessage);
            }

            if (string.Equals(commandArg, "--load-open-loop", StringComparison.OrdinalIgnoreCase))
            {
                command = BenchmarkCommand.LoadOpenLoop;
                return TryParseOptionalReport(args, out reportPath, out errorMessage);
            }

            if (ContainsReportOption(args))
            {
                errorMessage = MessageReportOnlyWithRuns;
                return true;
            }

            return false;
        }

        private static bool TryParseOptionalReport(string[] args, out string? reportPath, out string? errorMessage)
        {
            reportPath = null;
            errorMessage = null;

            if (args.Length == 1)
                return true;

            if (args.Length != 3 || !string.Equals(args[1], "--report", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = MessageUnknownRunnerArgs;
                return true;
            }

            if (string.IsNullOrWhiteSpace(args[2]))
            {
                errorMessage = MessageReportPathRequired;
                return true;
            }

            reportPath = args[2];
            return true;
        }

        private static bool ValidateNoReportOption(string[] args, out string? errorMessage)
        {
            errorMessage = null;

            if (args.Length == 1)
                return true;

            if (ContainsReportOption(args))
                errorMessage = MessageReportExecutionOnly;
            else
                errorMessage = MessageUnknownRunnerArgs;

            return true;
        }

        private static bool ContainsReportOption(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--report", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
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

        private static void PrintUsage(TextWriter writer)
        {
            writer.WriteLine(MessageUsage);
            writer.WriteLine("  Hps.Benchmarks --target");
            writer.WriteLine("  Hps.Benchmarks --smoke [--report <path>]");
            writer.WriteLine("  Hps.Benchmarks --load [--report <path>]");
            writer.WriteLine("  Hps.Benchmarks --load-open-loop [--report <path>]");
            writer.WriteLine("  Hps.Benchmarks [BenchmarkDotNet arguments]");
        }

        private enum BenchmarkCommand
        {
            None,
            Target,
            Smoke,
            Load,
            LoadOpenLoop,
            Help
        }
    }
}
