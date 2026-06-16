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

        private const string MessageReportOnlyWithRuns = "--report 옵션은 --smoke, --load, --load-open-loop 뒤에서만 사용할 수 있습니다.";
        private const string MessageUnknownRunnerArgs = "알 수 없는 benchmark runner 인자입니다.";
        private const string MessageReportPathRequired = "--report 옵션에는 저장할 파일 경로가 필요합니다.";
        private const string MessageReportExecutionOnly = "--report 옵션은 benchmark 실행 명령에서만 사용할 수 있습니다.";
        private const string MessageUsage = "사용법";

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

            switch (command)
            {
                case BenchmarkCommand.Target:
                    BenchmarkTargets.Print(Console.Out);
                    return SuccessExitCode;

                case BenchmarkCommand.Smoke:
                    return CompleteRun(TcpLoopbackScenarioRunner.RunSmokeAsync().GetAwaiter().GetResult(), reportPath);

                case BenchmarkCommand.Load:
                    return CompleteRun(TcpLoopbackScenarioRunner.RunLoadAsync().GetAwaiter().GetResult(), reportPath);

                case BenchmarkCommand.LoadOpenLoop:
                    return CompleteRun(TcpLoopbackScenarioRunner.RunOpenLoopAsync().GetAwaiter().GetResult(), reportPath);

                case BenchmarkCommand.Help:
                    PrintUsage(Console.Out);
                    return SuccessExitCode;

                default:
                    return UsageErrorExitCode;
            }
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
                ValidateNoReportOption(args, out errorMessage);
                return true;
            }

            if (string.Equals(commandArg, "--target", StringComparison.OrdinalIgnoreCase))
            {
                command = BenchmarkCommand.Target;
                ValidateNoReportOption(args, out errorMessage);
                return true;
            }

            if (string.Equals(commandArg, "--smoke", StringComparison.OrdinalIgnoreCase))
            {
                command = BenchmarkCommand.Smoke;
                ParseOptionalReport(args, out reportPath, out errorMessage);
                return true;
            }

            if (string.Equals(commandArg, "--load", StringComparison.OrdinalIgnoreCase))
            {
                command = BenchmarkCommand.Load;
                ParseOptionalReport(args, out reportPath, out errorMessage);
                return true;
            }

            if (string.Equals(commandArg, "--load-open-loop", StringComparison.OrdinalIgnoreCase))
            {
                command = BenchmarkCommand.LoadOpenLoop;
                ParseOptionalReport(args, out reportPath, out errorMessage);
                return true;
            }

            if (ContainsReportOption(args))
            {
                errorMessage = MessageReportOnlyWithRuns;
                return true;
            }

            return false;
        }

        private static void ParseOptionalReport(string[] args, out string? reportPath, out string? errorMessage)
        {
            reportPath = null;
            errorMessage = null;

            if (args.Length == 1)
                return;

            if (args.Length != 3 || !string.Equals(args[1], "--report", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = MessageUnknownRunnerArgs;
                return;
            }

            if (string.IsNullOrWhiteSpace(args[2]))
            {
                errorMessage = MessageReportPathRequired;
                return;
            }

            reportPath = args[2];
        }

        private static void ValidateNoReportOption(string[] args, out string? errorMessage)
        {
            errorMessage = null;

            if (args.Length == 1)
                return;

            if (ContainsReportOption(args))
                errorMessage = MessageReportExecutionOnly;
            else
                errorMessage = MessageUnknownRunnerArgs;
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
