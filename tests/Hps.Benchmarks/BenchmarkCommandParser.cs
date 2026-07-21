using System;

namespace Hps.Benchmarks
{
    internal static class BenchmarkCommandParser
    {
        public const int DefaultBaselineRunCount = 3;

        public const string MessageReportOnlyWithRuns = "--report 옵션은 --smoke, --load, --load-open-loop, --mixed-load-open-loop 뒤에서만 사용할 수 있습니다.";
        public const string MessageUnknownRunnerArgs = "알 수 없는 benchmark runner 인자입니다.";
        public const string MessageReportPathRequired = "--report 옵션에는 저장할 파일 경로가 필요합니다.";
        public const string MessageReportExecutionOnly = "--report 옵션은 benchmark 실행 명령에서만 사용할 수 있습니다.";
        public const string MessageBackendPathRequired = "--backend 옵션에는 saea, rio 또는 iouring 값이 필요합니다.";
        public const string MessageBackendInvalid = "--backend 옵션은 saea, rio 또는 iouring 값만 사용할 수 있습니다.";
        public const string MessageBackendExecutionOnly = "--backend 옵션은 benchmark 실행 명령에서만 사용할 수 있습니다.";
        public const string MessageProtocolPathRequired = "--protocol 옵션에는 tcp 또는 udp 값이 필요합니다.";
        public const string MessageProtocolInvalid = "--protocol 옵션은 tcp 또는 udp 값만 사용할 수 있습니다.";
        public const string MessageProtocolExecutionOnly = "--protocol 옵션은 benchmark 실행 명령에서만 사용할 수 있습니다.";
        public const string MessageMixedProtocolNotAllowed = "--protocol 옵션은 --mixed-load-open-loop과 함께 사용할 수 없습니다.";
        public const string MessageMixedDataRateInvalid = "--data-rate-hz 옵션에는 100 이상의 정수가 필요합니다.";
        public const string MessageMixedDurationInvalid = "--duration-seconds 옵션에는 1 이상의 정수가 필요합니다.";
        public const string MessageMixedSubscriberInvalid = "--subscribers 옵션에는 1 이상 256 이하의 정수가 필요합니다.";
        public const string MessageMixedOptionsInvalid = "mixed workload 입력이 count 또는 latency 저장소 허용 범위를 벗어났습니다.";
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
        public const string MessageEnvelopeCandidateRequired = "--compare-baseline-envelope 옵션에는 candidate summary/history JSON 경로가 필요합니다.";
        public const string MessageEnvelopeReferenceHistoryRequired = "--reference-history 옵션에는 reference history JSON 파일 경로가 필요합니다.";
        public const string MessageEnvelopeOutputRequired = "--envelope 옵션에는 저장할 envelope JSON 파일 경로가 필요합니다.";
        public const string MessageEnvelopeMarkdownOutputRequired = "--envelope-md 옵션에는 저장할 envelope Markdown 파일 경로가 필요합니다.";
        public const string MessageEnvelopeExecutionOptionNotAllowed = "--report, --backend, --protocol 옵션은 --compare-baseline-envelope 와 함께 사용할 수 없습니다.";

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

            if (string.Equals(commandArg, "--compare-baseline-envelope", StringComparison.OrdinalIgnoreCase))
                return ParseCompareBaselineEnvelope(args, out commandLine, out errorMessage);

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

            if (string.Equals(commandArg, "--mixed-load-open-loop", StringComparison.OrdinalIgnoreCase))
                return ParseMixedRunner(args, out commandLine, out errorMessage);

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

        private static bool ParseCompareBaselineEnvelope(
            string[] args,
            out BenchmarkCommandLine commandLine,
            out string? errorMessage)
        {
            string? candidatePath = args.Length >= 2 ? args[1] : null;
            commandLine = CreateEnvelopeCommand(candidatePath, null, null, null);
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                errorMessage = MessageEnvelopeCandidateRequired;
                return true;
            }

            if (ContainsReportOption(args) || ContainsBackendOption(args) || ContainsProtocolOption(args))
            {
                errorMessage = MessageEnvelopeExecutionOptionNotAllowed;
                return true;
            }

            if (args.Length < 4
                || !string.Equals(args[2], "--reference-history", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(args[3]))
            {
                errorMessage = MessageEnvelopeReferenceHistoryRequired;
                return true;
            }

            if ((args.Length != 6 && args.Length != 8)
                || !string.Equals(args[4], "--envelope", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(args[5]))
            {
                errorMessage = MessageEnvelopeOutputRequired;
                commandLine = CreateEnvelopeCommand(candidatePath, args[3], null, null);
                return true;
            }

            string? envelopeMarkdownOutputPath = null;
            if (args.Length == 8)
            {
                if (!string.Equals(args[6], "--envelope-md", StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = MessageEnvelopeMarkdownOutputRequired;
                    commandLine = CreateEnvelopeCommand(candidatePath, args[3], args[5], null);
                    return true;
                }

                if (string.IsNullOrWhiteSpace(args[7]))
                {
                    errorMessage = MessageEnvelopeMarkdownOutputRequired;
                    commandLine = CreateEnvelopeCommand(candidatePath, args[3], args[5], null);
                    return true;
                }

                envelopeMarkdownOutputPath = args[7];
            }

            commandLine = CreateEnvelopeCommand(candidatePath, args[3], args[5], envelopeMarkdownOutputPath);
            return true;
        }

        private static BenchmarkCommandLine CreateEnvelopeCommand(
            string? candidatePath,
            string? referenceHistoryPath,
            string? envelopeOutputPath,
            string? envelopeMarkdownOutputPath)
        {
            return new BenchmarkCommandLine(
                BenchmarkCommand.CompareBaselineEnvelope,
                null,
                null,
                0,
                null,
                null,
                null,
                null,
                null,
                null,
                candidatePath,
                referenceHistoryPath,
                envelopeOutputPath,
                envelopeMarkdownOutputPath);
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

        private static bool ParseMixedRunner(
            string[] args,
            out BenchmarkCommandLine commandLine,
            out string? errorMessage)
        {
            string? reportPath = null;
            TcpLoopbackTransportBackend transportBackend = TcpLoopbackTransportBackend.Saea;
            int dataRateHz = MixedWorkloadOptions.DefaultDataRateHz;
            int durationSeconds = MixedWorkloadOptions.DefaultDurationSeconds;
            int subscriberCount = MixedWorkloadOptions.DefaultSubscriberCount;
            errorMessage = null;

            commandLine = CreateMixedCommandLine(
                reportPath,
                transportBackend,
                dataRateHz,
                durationSeconds,
                subscriberCount);

            // mixed runner는 data/control TCP topology를 자체 고정하므로 protocol 값 유무와 관계없이
            // generic pair parsing보다 먼저 거부한다. trailing --protocol도 unknown args로 흐르지 않는다.
            if (ContainsProtocolOption(args))
            {
                errorMessage = MessageMixedProtocolNotAllowed;
                return true;
            }

            for (int index = 1; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length)
                {
                    errorMessage = MessageUnknownRunnerArgs;
                    break;
                }

                string option = args[index];
                string value = args[index + 1];
                if (string.Equals(option, "--report", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        errorMessage = MessageReportPathRequired;
                        break;
                    }

                    reportPath = value;
                }
                else if (string.Equals(option, "--backend", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseTransportBackend(value, out transportBackend, out errorMessage))
                        break;
                }
                else if (string.Equals(option, "--data-rate-hz", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(value, out dataRateHz))
                    {
                        errorMessage = MessageMixedDataRateInvalid;
                        break;
                    }
                }
                else if (string.Equals(option, "--duration-seconds", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(value, out durationSeconds))
                    {
                        errorMessage = MessageMixedDurationInvalid;
                        break;
                    }
                }
                else if (string.Equals(option, "--subscribers", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(value, out subscriberCount))
                    {
                        errorMessage = MessageMixedSubscriberInvalid;
                        break;
                    }
                }
                else
                {
                    errorMessage = MessageUnknownRunnerArgs;
                    break;
                }
            }

            if (errorMessage == null)
            {
                try
                {
                    // options 생성 자체가 allocation 전 checked count와 128MiB latency 저장소 preflight다.
                    // parser가 같은 계약을 재사용해야 CLI 유효 정수만으로 socket/배열 생성 단계에 진입하지 않는다.
                    new MixedWorkloadOptions(dataRateHz, durationSeconds, subscriberCount);
                }
                catch (ArgumentOutOfRangeException)
                {
                    errorMessage = MessageMixedOptionsInvalid;
                }
            }

            commandLine = CreateMixedCommandLine(
                reportPath,
                transportBackend,
                dataRateHz,
                durationSeconds,
                subscriberCount);
            return true;
        }

        private static BenchmarkCommandLine CreateMixedCommandLine(
            string? reportPath,
            TcpLoopbackTransportBackend transportBackend,
            int dataRateHz,
            int durationSeconds,
            int subscriberCount)
        {
            return new BenchmarkCommandLine(
                BenchmarkCommand.MixedLoadOpenLoop,
                reportPath,
                null,
                0,
                null,
                null,
                null,
                null,
                null,
                null,
                transportBackend,
                LoopbackProtocol.Tcp,
                dataRateHz,
                durationSeconds,
                subscriberCount);
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

            if (string.Equals(value, "iouring", StringComparison.OrdinalIgnoreCase))
            {
                transportBackend = TcpLoopbackTransportBackend.IoUring;
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
