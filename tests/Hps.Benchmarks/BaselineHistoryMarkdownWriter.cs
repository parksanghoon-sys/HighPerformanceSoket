using System;
using System.Globalization;
using System.IO;

namespace Hps.Benchmarks
{
    internal static class BaselineHistoryMarkdownWriter
    {
        public static void Write(TextWriter writer, BaselineHistory history)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            if (history == null)
                throw new ArgumentNullException(nameof(history));

            writer.WriteLine("# Baseline History");
            writer.WriteLine();
            WriteLine(writer, "- source root: `{0}`", history.SourceRoot);
            WriteLine(writer, "- session count: {0}", history.SessionCount);
            WriteLine(writer, "- hard gate: {0}", history.HardPassed ? "PASS" : "FAIL");
            WriteLine(writer, "- warning count: {0}", history.WarningCount);
            writer.WriteLine();
            WriteComparison(writer, history.Comparison);
            writer.WriteLine();
            writer.WriteLine("| 날짜 | session | summary | human report | raw reports | hard passed | warnings | load p99 max us | open-loop p99 max us | TCP HWM max |");
            writer.WriteLine("| --- | --- | --- | --- | ---: | --- | ---: | ---: | ---: | ---: |");

            for (int i = 0; i < history.Sessions.Count; i++)
                WriteSessionRow(writer, history.Sessions[i]);

            writer.WriteLine();
            writer.WriteLine("## warning 이 있는 session");
            writer.WriteLine();

            bool wroteWarning = false;
            for (int i = 0; i < history.Sessions.Count; i++)
            {
                if (history.Sessions[i].WarningCount == 0)
                    continue;

                wroteWarning = true;
                WriteLine(writer, "- `{0}` `{1}`: {2}", history.Sessions[i].Date, history.Sessions[i].Session, history.Sessions[i].WarningCount);
            }

            if (!wroteWarning)
                writer.WriteLine("- 없음");
        }

        private static void WriteComparison(TextWriter writer, BaselineComparisonResult comparison)
        {
            writer.WriteLine("## Comparison");
            writer.WriteLine();
            WriteLine(writer, "- compatible: {0}", comparison.Compatible ? "true" : "false");
            WriteLine(writer, "- unknown-runner-count: {0}", comparison.UnknownRunnerCount);
            WriteLine(writer, "- mismatch-count: {0}", comparison.MismatchCount);

            if (comparison.Key == null)
            {
                writer.WriteLine("- comparison-key: 없음");
            }
            else
            {
                WriteLine(writer, "- benchmark-profile: {0}", comparison.Key.BenchmarkProfile);
                WriteLine(writer, "- runner-id: {0}", comparison.Key.RunnerId);
                WriteLine(writer, "- runner-kind: {0}", comparison.Key.RunnerKind);
                WriteLine(writer, "- transport-backend: {0}", comparison.Key.TransportBackend);
                WriteLine(writer, "- os-architecture: {0}", comparison.Key.OsArchitecture);
                WriteLine(writer, "- process-architecture: {0}", comparison.Key.ProcessArchitecture);
                WriteLine(writer, "- framework-description: {0}", comparison.Key.FrameworkDescription);
                writer.WriteLine();
                writer.WriteLine("| result | scenario | payload bytes | target rate hz | target duration seconds |");
                writer.WriteLine("| --- | --- | ---: | ---: | ---: |");
                for (int i = 0; i < comparison.Key.Cases.Count; i++)
                    WriteComparisonCaseRow(writer, comparison.Key.Cases[i]);
            }

            writer.WriteLine();
            if (comparison.MismatchCount == 0)
            {
                writer.WriteLine("- mismatch: 없음");
                return;
            }

            writer.WriteLine("| code | field | expected | actual | session | summary |");
            writer.WriteLine("| --- | --- | --- | --- | --- | --- |");
            for (int i = 0; i < comparison.Mismatches.Count; i++)
                WriteComparisonMismatchRow(writer, comparison.Mismatches[i]);
        }

        private static void WriteComparisonCaseRow(TextWriter writer, BaselineComparisonCase runCase)
        {
            WriteLine(
                writer,
                "| {0} | {1} | {2} | {3} | {4} |",
                EscapeCell(runCase.ResultName),
                EscapeCell(runCase.Scenario),
                runCase.PayloadBytes,
                FormatDouble(runCase.TargetRateHz),
                runCase.TargetDurationSeconds);
        }

        private static void WriteComparisonMismatchRow(TextWriter writer, BaselineComparisonMismatch mismatch)
        {
            WriteLine(
                writer,
                "| {0} | {1} | {2} | {3} | {4} | `{5}` |",
                EscapeCell(mismatch.Code),
                EscapeCell(mismatch.Field),
                EscapeCell(mismatch.Expected),
                EscapeCell(mismatch.Actual),
                EscapeCell(mismatch.Session ?? "-"),
                EscapeCode(mismatch.SummaryPath ?? "-"));
        }

        private static void WriteSessionRow(TextWriter writer, BaselineHistorySession session)
        {
            WriteLine(
                writer,
                "| {0} | {1} | `{2}` | {3} | {4} | {5} | {6} | {7} | {8} | {9} |",
                EscapeCell(session.Date),
                EscapeCell(session.Session),
                EscapeCode(session.SummaryPath),
                session.HumanReportPath == null ? "" : "`" + EscapeCode(session.HumanReportPath) + "`",
                session.SourceReportCount,
                session.HardPassed ? "true" : "false",
                session.WarningCount,
                FormatDouble(session.LoadP99MaxMicroseconds),
                FormatDouble(session.OpenLoopP99MaxMicroseconds),
                session.TcpHighWatermarkMax);
        }

        private static void WriteLine(TextWriter writer, string format, params object[] args)
        {
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture, format, args));
        }

        private static string FormatDouble(double? value)
        {
            if (!value.HasValue)
                return "-";

            return value.Value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string EscapeCell(string value)
        {
            return value.Replace("|", "\\|");
        }

        private static string EscapeCode(string value)
        {
            return value.Replace("`", "\\`");
        }
    }
}
