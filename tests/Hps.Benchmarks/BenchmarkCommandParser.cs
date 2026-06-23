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
            if (args.Length == 4 && string.Equals(args[2], "--runs", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(args[3], out runCount) || runCount <= 0)
                    errorMessage = MessageBaselineRunsInvalid;
            }
            else if (args.Length != 2)
            {
                errorMessage = MessageUnknownRunnerArgs;
            }

            commandLine = new BenchmarkCommandLine(BenchmarkCommand.BaselineSuite, null, args[1], runCount, null, null, null, null, null, null);
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
            ParseOptionalReport(args, out reportPath, out errorMessage);
            commandLine = new BenchmarkCommandLine(command, reportPath, null, 0, null, null, null, null, null, null);
            return true;
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
                errorMessage = MessageReportPathRequired;
            else
                reportPath = args[2];
        }

        private static string? ValidateNoReportOption(string[] args)
        {
            if (args.Length == 1)
                return null;

            if (ContainsReportOption(args))
                return MessageReportExecutionOnly;

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
    }
}
