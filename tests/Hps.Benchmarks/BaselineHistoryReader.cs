using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hps.Benchmarks
{
    internal static class BaselineHistoryReader
    {
        private static readonly Regex DateDirectoryPattern = new Regex(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.CultureInvariant);
        private static readonly Regex SessionDirectoryPattern = new Regex(@"^session-\d{2}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static IReadOnlyList<BaselineHistorySession> ReadSessions(string sourceRoot)
        {
            if (string.IsNullOrWhiteSpace(sourceRoot))
                throw new ArgumentException("baseline history input root 는 비어 있을 수 없습니다.", nameof(sourceRoot));

            string fullRoot = Path.GetFullPath(sourceRoot);
            List<BaselineHistorySession> sessions = new List<BaselineHistorySession>();

            if (IsDateDirectory(fullRoot))
                AddDateRoot(fullRoot, fullRoot, sessions);
            else
                AddDateChildren(fullRoot, sessions);

            if (sessions.Count == 0)
                throw new InvalidOperationException("baseline history summary.json 을 찾지 못했습니다.");

            return sessions;
        }

        private static void AddDateChildren(string parentRoot, List<BaselineHistorySession> sessions)
        {
            string[] directories = Directory.GetDirectories(parentRoot);
            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < directories.Length; i++)
            {
                if (IsDateDirectory(directories[i]))
                    AddDateRoot(parentRoot, directories[i], sessions);
            }
        }

        private static void AddDateRoot(string inputRoot, string dateRoot, List<BaselineHistorySession> sessions)
        {
            string rootSummary = Path.Combine(dateRoot, "summary.json");
            if (File.Exists(rootSummary))
                sessions.Add(ReadSummary(inputRoot, dateRoot, "session-01(root)", rootSummary));

            string[] directories = Directory.GetDirectories(dateRoot);
            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < directories.Length; i++)
            {
                string sessionName = Path.GetFileName(directories[i]);
                if (!SessionDirectoryPattern.IsMatch(sessionName))
                    continue;

                string summaryPath = Path.Combine(directories[i], "summary.json");
                if (File.Exists(summaryPath))
                    sessions.Add(ReadSummary(inputRoot, dateRoot, sessionName, summaryPath));
            }
        }

        private static bool IsDateDirectory(string path)
        {
            return DateDirectoryPattern.IsMatch(Path.GetFileName(path));
        }

        private static BaselineHistorySession ReadSummary(string inputRoot, string dateRoot, string session, string summaryPath)
        {
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(summaryPath)))
            {
                JsonElement root = document.RootElement;
                int summaryVersion = root.GetProperty("summary-version").GetInt32();
                if (summaryVersion != 1)
                    throw new InvalidOperationException("지원하지 않는 baseline summary version 입니다.");

                string? humanReportPath = null;
                string? summaryDirectory = Path.GetDirectoryName(summaryPath);
                if (!string.IsNullOrEmpty(summaryDirectory))
                {
                    string markdownPath = Path.Combine(summaryDirectory, "summary.md");
                    if (File.Exists(markdownPath))
                        humanReportPath = ToRelativePath(inputRoot, markdownPath);
                }

                double? loadP99 = GetKindDouble(root, "load", "p99-max-us");
                double? openLoopP99 = GetKindDouble(root, "open-loop", "p99-max-us");
                int loadHwm = GetKindInt(root, "load", "tcp-hwm-max");
                int openLoopHwm = GetKindInt(root, "open-loop", "tcp-hwm-max");
                string relativeSummaryPath = ToRelativePath(inputRoot, summaryPath);
                BaselineComparisonResult comparison = ReadComparison(root, session, relativeSummaryPath);

                return new BaselineHistorySession(
                    Path.GetFileName(dateRoot),
                    session,
                    relativeSummaryPath,
                    humanReportPath,
                    root.GetProperty("source-report-count").GetInt32(),
                    root.GetProperty("hard-passed").GetBoolean(),
                    root.GetProperty("hard-failure-count").GetInt32(),
                    root.GetProperty("warning-count").GetInt32(),
                    loadP99,
                    openLoopP99,
                    Math.Max(loadHwm, openLoopHwm),
                    comparison);
            }
        }

        private static BaselineComparisonResult ReadComparison(JsonElement root, string session, string summaryPath)
        {
            JsonElement compatibleElement;
            if (!root.TryGetProperty("comparison-compatible", out compatibleElement))
            {
                return new BaselineComparisonResult(
                    false,
                    null,
                    0,
                    new[]
                    {
                        new BaselineComparisonMismatch(
                            "legacy-summary-without-comparison",
                            "comparison-compatible",
                            "present",
                            "missing",
                            null,
                            session,
                            summaryPath)
                    });
            }

            BaselineComparisonKey? key = null;
            JsonElement keyElement;
            if (root.TryGetProperty("comparison-key", out keyElement) && keyElement.ValueKind != JsonValueKind.Null)
                key = ReadComparisonKey(keyElement);

            int unknownRunnerCount = 0;
            JsonElement unknownRunnerElement;
            if (root.TryGetProperty("unknown-runner-count", out unknownRunnerElement))
                unknownRunnerCount = unknownRunnerElement.GetInt32();

            List<BaselineComparisonMismatch> mismatches = new List<BaselineComparisonMismatch>();
            JsonElement mismatchesElement;
            if (root.TryGetProperty("comparison-mismatches", out mismatchesElement) && mismatchesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement mismatchElement in mismatchesElement.EnumerateArray())
                    mismatches.Add(ReadComparisonMismatch(mismatchElement, session, summaryPath));
            }

            return new BaselineComparisonResult(
                compatibleElement.GetBoolean(),
                key,
                unknownRunnerCount,
                mismatches);
        }

        private static BaselineComparisonKey ReadComparisonKey(JsonElement key)
        {
            List<BaselineComparisonCase> cases = new List<BaselineComparisonCase>();
            JsonElement casesElement;
            if (key.TryGetProperty("cases", out casesElement) && casesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement runCase in casesElement.EnumerateArray())
                {
                    cases.Add(new BaselineComparisonCase(
                        runCase.GetProperty("result-name").GetString()!,
                        runCase.GetProperty("scenario").GetString()!,
                        runCase.GetProperty("payload-bytes").GetInt32(),
                        runCase.GetProperty("target-rate-hz").GetDouble(),
                        runCase.GetProperty("target-duration-seconds").GetInt32()));
                }
            }

            return new BaselineComparisonKey(
                key.GetProperty("benchmark-profile").GetString()!,
                key.GetProperty("runner-id").GetString()!,
                key.GetProperty("runner-kind").GetString()!,
                key.GetProperty("transport-backend").GetString()!,
                key.GetProperty("os-description").GetString()!,
                key.GetProperty("os-architecture").GetString()!,
                key.GetProperty("process-architecture").GetString()!,
                key.GetProperty("framework-description").GetString()!,
                cases);
        }

        private static BaselineComparisonMismatch ReadComparisonMismatch(JsonElement mismatch, string session, string summaryPath)
        {
            return new BaselineComparisonMismatch(
                mismatch.GetProperty("code").GetString()!,
                mismatch.GetProperty("field").GetString()!,
                mismatch.GetProperty("expected").GetString()!,
                mismatch.GetProperty("actual").GetString()!,
                GetOptionalString(mismatch, "source-path"),
                GetOptionalString(mismatch, "session") ?? session,
                GetOptionalString(mismatch, "summary-path") ?? summaryPath);
        }

        private static string? GetOptionalString(JsonElement element, string propertyName)
        {
            JsonElement value;
            if (!element.TryGetProperty(propertyName, out value) || value.ValueKind == JsonValueKind.Null)
                return null;

            return value.GetString();
        }

        private static double? GetKindDouble(JsonElement root, string kindName, string propertyName)
        {
            JsonElement byKind;
            JsonElement kind;
            JsonElement value;
            if (!root.TryGetProperty("by-kind", out byKind) || !byKind.TryGetProperty(kindName, out kind) || kind.ValueKind == JsonValueKind.Null)
                return null;

            if (!kind.TryGetProperty(propertyName, out value))
                return null;

            if (value.ValueKind == JsonValueKind.Number)
                return value.GetDouble();

            return double.Parse(value.GetString()!, CultureInfo.InvariantCulture);
        }

        private static int GetKindInt(JsonElement root, string kindName, string propertyName)
        {
            JsonElement byKind;
            JsonElement kind;
            JsonElement value;
            if (!root.TryGetProperty("by-kind", out byKind) || !byKind.TryGetProperty(kindName, out kind) || kind.ValueKind == JsonValueKind.Null)
                return 0;

            if (!kind.TryGetProperty(propertyName, out value))
                return 0;

            return value.GetInt32();
        }

        private static string ToRelativePath(string root, string path)
        {
            Uri rootUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(root)));
            Uri pathUri = new Uri(Path.GetFullPath(path));
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('\\', '/');
        }

        private static string AppendDirectorySeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                return path;

            return path + Path.DirectorySeparatorChar;
        }
    }
}
