using System;
using System.Globalization;
using System.IO;

namespace Hps.Benchmarks
{
    internal static class BaselineSummaryMarkdownWriter
    {
        public static void Write(TextWriter writer, BaselineSummary summary)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            if (summary == null)
                throw new ArgumentNullException(nameof(summary));

            writer.WriteLine("# Baseline Summary");
            writer.WriteLine();
            WriteLine(writer, "- 입력 directory: `{0}`", summary.SourceDirectory);
            WriteLine(writer, "- source report count: {0}", summary.SourceReportCount);
            WriteLine(writer, "- hard gate: {0}", summary.HardPassed ? "PASS" : "FAIL");
            WriteLine(writer, "- hard failure count: {0}", summary.HardFailureCount);
            WriteLine(writer, "- warning count: {0}", summary.WarningCount);
            writer.WriteLine();

            WriteComparison(writer, summary.Comparison);
            writer.WriteLine();
            WriteKindSummary(writer, summary);
            writer.WriteLine();
            WriteWarnings(writer, summary);
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

            writer.WriteLine("| code | field | expected | actual | source |");
            writer.WriteLine("| --- | --- | --- | --- | --- |");
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
                "| {0} | {1} | {2} | {3} | `{4}` |",
                EscapeCell(mismatch.Code),
                EscapeCell(mismatch.Field),
                EscapeCell(mismatch.Expected),
                EscapeCell(mismatch.Actual),
                EscapeCode(mismatch.SourcePath ?? "-"));
        }

        // Markdown 은 사람이 리뷰할 companion artifact 이다.
        // 자동화의 canonical 입력은 summary.json 이므로, 여기서는 핵심 추세와 원인 추적용 source path 만 압축해 보여준다.
        private static void WriteKindSummary(TextWriter writer, BaselineSummary summary)
        {
            writer.WriteLine("## 종류별 요약");
            writer.WriteLine();
            writer.WriteLine("| kind | runs | p50 median us | p99 median us | p99 max us | TCP HWM max | dropped total | pool rented max |");
            writer.WriteLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
            WriteKindRow(writer, summary.Load);
            WriteKindRow(writer, summary.OpenLoop);
        }

        private static void WriteKindRow(TextWriter writer, BaselineKindSummary? kind)
        {
            if (kind == null)
                return;

            WriteLine(
                writer,
                "| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} |",
                EscapeCell(kind.Kind),
                kind.RunCount,
                FormatDouble(kind.P50Median),
                FormatDouble(kind.P99Median),
                FormatDouble(kind.P99Max),
                kind.TcpHighWatermarkMax,
                kind.DroppedTotal,
                kind.PoolRentedMax);
        }

        private static void WriteWarnings(TextWriter writer, BaselineSummary summary)
        {
            writer.WriteLine("## Warnings");
            writer.WriteLine();

            if (summary.Warnings.Count == 0)
            {
                writer.WriteLine("- 없음");
                return;
            }

            writer.WriteLine("| code | kind | metric | value | threshold | source |");
            writer.WriteLine("| --- | --- | --- | ---: | ---: | --- |");
            for (int i = 0; i < summary.Warnings.Count; i++)
            {
                BaselineWarning warning = summary.Warnings[i];
                WriteLine(
                    writer,
                    "| {0} | {1} | {2} | {3} | {4} | `{5}` |",
                    EscapeCell(warning.Code),
                    EscapeCell(warning.Kind),
                    EscapeCell(warning.Metric),
                    FormatDouble(warning.Value),
                    FormatDouble(warning.Threshold),
                    EscapeCode(warning.SourcePath));
            }
        }

        private static void WriteLine(TextWriter writer, string format, params object[] args)
        {
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture, format, args));
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
