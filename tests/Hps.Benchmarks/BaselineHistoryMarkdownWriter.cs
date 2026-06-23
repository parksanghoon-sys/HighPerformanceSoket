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
