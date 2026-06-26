using System;

namespace Hps.Benchmarks
{
    internal static class BenchmarkCommandParser
    {
        public const int DefaultBaselineRunCount = 3;

        public const string MessageReportOnlyWithRuns = "--report 옵션은 --smoke, --load, --load-open-loop 뒤에서만 사용할 수 있습니다.";
        public const string MessageUnknownRunnerArgs = "알 수 없는 benchmark runner 인자입니다.";
        public const string MessageReportPathRequired = "--report 옵션에는 저장할 파일 경로가 필요합니다.";
        public const string MessageReportExecutionOnly = "--report 옵션은 benchmark 실행 명령에서만 사용할 수 있습니다.";
        public const string MessageBackendPathRequired = "--backend 옵션에는 saea 또는 rio 값이 필요합니다.";
        public const string MessageBackendInvalid = "--backend 옵션은 saea 또는 rio 값만 사용할 수 있습니다.";
        public const string MessageBackendExecutionOnly = "--backend 옵션은 benchmark 실행 명령에서만 사용할 수 있습니다.";
        public const string MessageProtocolPathRequired = "--protocol 옵션에는 tcp 또는 udp 값이 필요합니다.";
        public const string MessageProtocolInvalid = "--protocol 옵션은 tcp 또는 udp 값만 사용할 수 있습니다.";
        public const string MessageProtocolExecutionOnly = "--protocol 옵션은 benchmark 실행 명령에서만 사용할 수 있습니다.";
        public const string MessageBaselineOutputRequired = "--baseline-suite 옵션에는 report directory 경로가 필요합니다.";
        public const string MessageBaselineRunsInvalid = "--runs 옵션에는 1 이상의 정수가 필요합니다.";
        public const string MessageBaselineReportNotAllowed = "--report 옵션은 --baseline-suite 와 함께 사용할 수 없습니다.";
        public const string MessageSummaryInputRequired = "--summarize-baseline 옵션에는 입력 directory 경로가 필요합니다.";
        public const string MessageSummaryOutputRequired = "--summary 옵션에는 저장할 summary JSON 파일 경로가 필요합니다.";
        public const string MessageSummaryMarkdownOutputRequired = "--summary-md 옵션에는 저장할 summary Markdown 파일 경로가 필요합니다.";
        public const string MessageSummaryReportNotAllowed = "--report 옵션은 --summarize-baseline 과 함께 사용할 수 없습니다.";
        public const string MessageHistoryInputRequired = "--summarize-baseline-history 옵션에는 입력 baseline root 경로가 필요합니다.";
        public const string MessageHistoryOutputRequired = "--history 옵션에는 저장할 history JSON 파일 경로가 필요합니다.";
        public const string MessageHistoryMarkdownOutputRequired = "--history-md 옵션에는 저장할 history Markdown 파일 경로가 필요합니다.";
        public const string MessageHistoryReportNotAllowed = "--report 옵션은 --summarize-baseline-history 와 함께 사용할 수 없습니다.";

        public static bool TryParse(string[] args, out BenchmarkCommandLine commandLine, out string? errorMessage)
        {
            commandLine = new BenchmarkCommandLine(BenchmarkCommand.None, null, null, 0, null, null, null, null, null, null);
            errorMessage = null;

            if (args.Length == 0)
                return false;

            string commandArg = args[0];

            if (string.Equals(commandArg, "--help", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = ValidateNoReportOption(args);
                commandLine = new BenchmarkCommandLine(BenchmarkCommand.Help, null, null, 0, null, null, null, null, null, null);
                return true;
            }

            if (string.Equals(commandArg, "--target", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = ValidateNoReportOption(args);
                commandLine = new BenchmarkCommandLine(BenchmarkCommand.Target, null, null, 0, null, null, null, null, null, null);
                return true;
            }

            if (string.Equals(commandArg, "--summarize-baseline-history", StringComparison.OrdinalIgnoreCase))
                return ParseSummarizeBaselineHistory(args, out commandLine, out errorMessage);

            if (string.Equals(commandArg, "--summarize-baseline", StringComparison.OrdinalIgnoreCase))
                return ParseSummarizeBaseline(args, out commandLine, out errorMessage);

            if (string.Equals(commandArg, "--baseline-suite", StringComparison.OrdinalIgnoreCase))
                return ParseBaselineSuite(args, out commandLine, out errorMessage);

            if (string.Equals(commandArg, "--smoke", StringComparison.OrdinalIgnoreCase))
                return ParseRunner(args, BenchmarkCommand.Smoke, out commandLine, out errorMessage);

            if (string.Equals(commandArg, "--load", StringComparison.OrdinalIgnoreCase))
                return ParseRunner(args, BenchmarkCommand.Load, out commandLine, out errorMessage);

            if (string.Equals(commandArg, "--load-open-loop", StringComparison.OrdinalIgnoreCase))
                return ParseRunner(args, BenchmarkCommand.LoadOpenLoop, out commandLine, out errorMessage);

            if (ContainsReportOption(args))
            {
                errorMessage = MessageReportOnlyWithRuns;
                return true;
            }

            if (ContainsBackendOption(args))
            {
                errorMessage = MessageBackendExecutionOnly;
                return true;
            }

            if (ContainsProtocolOption(args))
            {
                errorMessage = MessageProtocolExecutionOnly;
                return true;
            }

            return false;
        }

        private static bool ParseBaselineSuite(
            string[] args,
            out BenchmarkCommandLine commandLine,
            out string? errorMessage)
        {
            commandLine = new BenchmarkCommandLine(BenchmarkCommand.BaselineSuite, null, null, DefaultBaselineRunCount, null, null, null, null, null, null);
            errorMessage = null;

            if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
            {
                errorMessage = MessageBaselineOutputRequired;
                return true;
            }

            if (ContainsReportOption(args))
            {
                commandLine = new BenchmarkCommandLine(BenchmarkCommand.BaselineSuite, null, args[1], DefaultBaselineRunCount, null, null, null, null, null, null);
                errorMessage = MessageBaselineReportNotAllowed;
                return true;
            }

            int runCount = DefaultBaselineRunCount;
            TcpLoopbackTransportBackend transportBackend = TcpLoopbackTransportBackend.Saea;
            LoopbackProtocol loopbackProtocol = LoopbackProtocol.Tcp;
            for (int index = 2; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length)
                {
                    errorMessage = MessageUnknownRunnerArgs;
                    break;
                }

                if (string.Equals(args[index], "--runs", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(args[index + 1], out runCount) || runCount <= 0)
                        errorMessage = MessageBaselineRunsInvalid;
                }
                else if (string.Equals(args[index], "--backend", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseTransportBackend(args[index + 1], out transportBackend, out errorMessage))
                        break;
                }
                else if (string.Equals(args[index], "--protocol", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseLoopbackProtocol(args[index + 1], out loopbackProtocol, out errorMessage))
                        break;
                }
                else
                {
                    errorMessage = MessageUnknownRunnerArgs;
                    break;
                }
            }

            commandLine = new BenchmarkCommandLine(BenchmarkCommand.BaselineSuite, null, args[1], runCount, null, null, null, null, null, null, transportBackend, loopbackProtocol);
            return true;
        }

        private static bool ParseSummarizeBaseline(
            string[] args,
            out BenchmarkCommandLine commandLine,
            out string? errorMessage)
        {
            string? inputDirectory = args.Length >= 2 ? args[1] : null;
            commandLine = new BenchmarkCommandLine(BenchmarkCommand.SummarizeBaseline, null, null, 0, inputDirectory, null, null, null, null, null);
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(inputDirectory))
            {
                errorMessage = MessageSummaryInputRequired;
                return true;
            }

            if (ContainsReportOption(args))
            {
                errorMessage = MessageSummaryReportNotAllowed;
                return true;
            }

            if (ContainsBackendOption(args))
            {
                errorMessage = MessageBackendExecutionOnly;
                return true;
            }

            if (ContainsProtocolOption(args))
            {
                errorMessage = MessageProtocolExecutionOnly;
                return true;
            }

            if ((args.Length != 4 && args.Length != 6) || !string.Equals(args[2], "--summary", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = MessageSummaryOutputRequired;
                return true;
            }

            if (string.IsNullOrWhiteSpace(args[3]))
            {
                errorMessage = MessageSummaryOutputRequired;
                return true;
            }

            string? summaryMarkdownOutputPath = null;
            if (args.Length == 6)
            {
                if (!string.Equals(args[4], "--summary-md", StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = MessageSummaryMarkdownOutputRequired;
                    return true;
                }

                if (string.IsNullOrWhiteSpace(args[5]))
                {
                    errorMessage = MessageSummaryMarkdownOutputRequired;
                    return true;
                }

                summaryMarkdownOutputPath = args[5];
            }

            commandLine = new BenchmarkCommandLine(BenchmarkCommand.SummarizeBaseline, null, null, 0, inputDirectory, args[3], summaryMarkdownOutputPath, null, null, null);
            return true;
        }

        private static bool ParseSummarizeBaselineHistory(
            string[] args,
            out BenchmarkCommandLine commandLine,
            out string? errorMessage)
        {
            string? inputRoot = args.Length >= 2 ? args[1] : null;
            commandLine = new BenchmarkCommandLine(
                BenchmarkCommand.SummarizeBaselineHistory,
                null,
                null,
                0,
                null,
                null,
                null,
                inputRoot,
                null,
                null);
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(inputRoot))
            {
                errorMessage = MessageHistoryInputRequired;
                return true;
            }

            if (ContainsReportOption(args))
            {
                errorMessage = MessageHistoryReportNotAllowed;
                return true;
            }

            if (ContainsBackendOption(args))
            {
                errorMessage = MessageBackendExecutionOnly;
                return true;
            }

            if (ContainsProtocolOption(args))
            {
                errorMessage = MessageProtocolExecutionOnly;
                return true;
            }

            if ((args.Length != 4 && args.Length != 6) || !string.Equals(args[2], "--history", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = MessageHistoryOutputRequired;
                return true;
            }

            if (string.IsNullOrWhiteSpace(args[3]))
            {
                errorMessage = MessageHistoryOutputRequired;
                return true;
            }

            string? historyMarkdownOutputPath = null;
            if (args.Length == 6)
            {
                if (!string.Equals(args[4], "--history-md", StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = MessageHistoryMarkdownOutputRequired;
                    return true;
                }

                if (string.IsNullOrWhiteSpace(args[5]))
                {
                    errorMessage = MessageHistoryMarkdownOutputRequired;
                    return true;
                }

                historyMarkdownOutputPath = args[5];
            }

            commandLine = new BenchmarkCommandLine(
                BenchmarkCommand.SummarizeBaselineHistory,
                null,
                null,
                0,
                null,
                null,
                null,
                inputRoot,
                args[3],
                historyMarkdownOutputPath);
            return true;
        }

        private static bool ParseRunner(
            string[] args,
            BenchmarkCommand command,
            out BenchmarkCommandLine commandLine,
            out string? errorMessage)
        {
            string? reportPath;
            TcpLoopbackTransportBackend transportBackend;
            LoopbackProtocol loopbackProtocol;
            ParseRunnerOptions(args, out reportPath, out transportBackend, out loopbackProtocol, out errorMessage);
            commandLine = new BenchmarkCommandLine(command, reportPath, null, 0, null, null, null, null, null, null, transportBackend, loopbackProtocol);
            return true;
        }

        private static void ParseRunnerOptions(
            string[] args,
            out string? reportPath,
            out TcpLoopbackTransportBackend transportBackend,
            out LoopbackProtocol loopbackProtocol,
            out string? errorMessage)
        {
            reportPath = null;
            transportBackend = TcpLoopbackTransportBackend.Saea;
            loopbackProtocol = LoopbackProtocol.Tcp;
            errorMessage = null;

            for (int index = 1; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length)
                {
                    errorMessage = MessageUnknownRunnerArgs;
                    return;
                }

                if (string.Equals(args[index], "--report", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        errorMessage = MessageReportPathRequired;
                        return;
                    }

                    reportPath = args[index + 1];
                }
                else if (string.Equals(args[index], "--backend", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseTransportBackend(args[index + 1], out transportBackend, out errorMessage))
                        return;
                }
                else if (string.Equals(args[index], "--protocol", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseLoopbackProtocol(args[index + 1], out loopbackProtocol, out errorMessage))
                        return;
                }
                else
                {
                    errorMessage = MessageUnknownRunnerArgs;
                    return;
                }
            }
        }

        private static string? ValidateNoReportOption(string[] args)
        {
            if (args.Length == 1)
                return null;

            if (ContainsReportOption(args))
                return MessageReportExecutionOnly;

            if (ContainsBackendOption(args))
                return MessageBackendExecutionOnly;

            if (ContainsProtocolOption(args))
                return MessageProtocolExecutionOnly;

            return MessageUnknownRunnerArgs;
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

        private static bool ContainsBackendOption(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--backend", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool ContainsProtocolOption(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--protocol", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool TryParseTransportBackend(
            string value,
            out TcpLoopbackTransportBackend transportBackend,
            out string? errorMessage)
        {
            transportBackend = TcpLoopbackTransportBackend.Saea;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                errorMessage = MessageBackendPathRequired;
                return false;
            }

            if (string.Equals(value, "saea", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(value, "rio", StringComparison.OrdinalIgnoreCase))
            {
                transportBackend = TcpLoopbackTransportBackend.Rio;
                return true;
            }

            errorMessage = MessageBackendInvalid;
            return false;
        }

        private static bool TryParseLoopbackProtocol(
            string value,
            out LoopbackProtocol loopbackProtocol,
            out string? errorMessage)
        {
            loopbackProtocol = LoopbackProtocol.Tcp;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                errorMessage = MessageProtocolPathRequired;
                return false;
            }

            if (string.Equals(value, "tcp", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(value, "udp", StringComparison.OrdinalIgnoreCase))
            {
                loopbackProtocol = LoopbackProtocol.Udp;
                return true;
            }

            errorMessage = MessageProtocolInvalid;
            return false;
        }
    }
}
