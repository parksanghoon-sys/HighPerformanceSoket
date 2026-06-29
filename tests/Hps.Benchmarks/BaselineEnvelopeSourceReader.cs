using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Hps.Benchmarks
{
    internal static class BaselineEnvelopeSourceReader
    {
        // envelope command 의 입력은 candidate summary/history 와 reference history 를 같은 모델로 수렴시키는 경계다.
        // 여기서 source 종류를 고정해 두면 generator/writer 가 파일 schema 분기를 다시 갖지 않아도 된다.
        public static BaselineEnvelopeSource Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("baseline envelope source path 는 비어 있을 수 없습니다.", nameof(path));

            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
                throw new InvalidOperationException("baseline envelope source file 을 찾지 못했습니다.");

            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(fullPath)))
            {
                JsonElement root = document.RootElement;
                if (root.TryGetProperty("summary-version", out JsonElement summaryVersion))
                    return ReadSummarySource(fullPath, root, summaryVersion);

                if (root.TryGetProperty("history-version", out JsonElement historyVersion))
                    return ReadHistorySource(fullPath, root, historyVersion);

                throw new InvalidOperationException("baseline envelope source 는 summary.json 또는 history.json 이어야 합니다.");
            }
        }

        private static BaselineEnvelopeSource ReadSummarySource(string fullPath, JsonElement root, JsonElement version)
        {
            // summary 는 candidate 로 쓰는 최소 artifact 이므로 summary 자체의 comparison 과 by-kind aggregate 를 그대로 노출한다.
            if (version.GetInt32() != 1)
                throw new InvalidOperationException("지원하지 않는 baseline summary version 입니다.");

            BaselineEnvelopeSummary summary = ReadSummary(fullPath, root, null);
            return new BaselineEnvelopeSource(
                BaselineEnvelopeSourceKind.Summary,
                fullPath,
                new[] { summary },
                summary.Comparison);
        }

        private static BaselineEnvelopeSource ReadHistorySource(string fullPath, JsonElement root, JsonElement version)
        {
            // history.json 은 session 목록과 comparison 가능성만 요약하고 full latency/HWM aggregate 는 갖지 않는다.
            // 따라서 session summary-path 를 history 파일 위치 기준으로 다시 열어 reference envelope 계산 재료를 복원한다.
            if (version.GetInt32() != 1)
                throw new InvalidOperationException("지원하지 않는 baseline history version 입니다.");

            BaselineComparisonResult comparison = BaselineComparisonJsonReader.Read(
                root,
                null,
                null,
                "legacy-history-without-comparison");

            JsonElement sessionsElement;
            if (!root.TryGetProperty("sessions", out sessionsElement) || sessionsElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("baseline history sessions 배열을 찾지 못했습니다.");

            string historyDirectory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
            List<BaselineEnvelopeSummary> summaries = new List<BaselineEnvelopeSummary>();
            foreach (JsonElement sessionElement in sessionsElement.EnumerateArray())
            {
                string? summaryPath = GetOptionalString(sessionElement, "summary-path");
                if (string.IsNullOrWhiteSpace(summaryPath))
                    throw new InvalidOperationException("baseline history session summary-path 가 비어 있습니다.");

                string resolvedSummaryPath = Path.GetFullPath(Path.Combine(historyDirectory, summaryPath));
                if (!File.Exists(resolvedSummaryPath))
                    throw new InvalidOperationException("baseline history session summary-path 파일을 찾지 못했습니다.");

                using (JsonDocument summaryDocument = JsonDocument.Parse(File.ReadAllText(resolvedSummaryPath)))
                {
                    JsonElement summaryRoot = summaryDocument.RootElement;
                    int summaryVersion = summaryRoot.GetProperty("summary-version").GetInt32();
                    if (summaryVersion != 1)
                        throw new InvalidOperationException("지원하지 않는 baseline summary version 입니다.");

                    summaries.Add(ReadSummary(
                        resolvedSummaryPath,
                        summaryRoot,
                        GetOptionalString(sessionElement, "session")));
                }
            }

            return new BaselineEnvelopeSource(
                BaselineEnvelopeSourceKind.History,
                fullPath,
                summaries,
                comparison);
        }

        private static BaselineEnvelopeSummary ReadSummary(string fullPath, JsonElement root, string? defaultSession)
        {
            // summary-level comparison mismatch 의 session/path default 를 여기서 주입해 후속 generator 가 원인 artifact 를 추적할 수 있게 한다.
            BaselineComparisonResult comparison = BaselineComparisonJsonReader.Read(
                root,
                defaultSession,
                fullPath,
                "legacy-summary-without-comparison");

            return new BaselineEnvelopeSummary(
                fullPath,
                root.GetProperty("source-report-count").GetInt32(),
                root.GetProperty("hard-passed").GetBoolean(),
                root.GetProperty("warning-count").GetInt32(),
                ReadKind(root, "load"),
                ReadKind(root, "open-loop"),
                comparison);
        }

        private static BaselineKindSummary? ReadKind(JsonElement root, string kindName)
        {
            // envelope metric 비교는 summary writer 가 만든 by-kind aggregate 를 기준으로 한다.
            // kind 자체가 null/missing 이면 generator 가 reference-kind-missing/candidate-kind-missing 으로 판단하도록 null 을 보존한다.
            JsonElement byKind;
            JsonElement kind;
            if (!root.TryGetProperty("by-kind", out byKind)
                || !byKind.TryGetProperty(kindName, out kind)
                || kind.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return new BaselineKindSummary(
                kindName,
                GetInt32(kind, "run-count"),
                GetDouble(kind, "p50-min-us"),
                GetDouble(kind, "p50-max-us"),
                GetDouble(kind, "p50-median-us"),
                GetDouble(kind, "p99-min-us"),
                GetDouble(kind, "p99-max-us"),
                GetDouble(kind, "p99-median-us"),
                GetDouble(kind, "p99-growth-ratio-min"),
                GetDouble(kind, "p99-growth-ratio-max"),
                GetDouble(kind, "actual-rate-min-hz"),
                GetDouble(kind, "actual-rate-max-hz"),
                GetInt32(kind, "tcp-hwm-min"),
                GetInt32(kind, "tcp-hwm-max"),
                GetInt64(kind, "dropped-total"),
                GetInt32(kind, "payload-error-total"),
                GetInt32(kind, "pool-rented-max"));
        }

        private static double GetDouble(JsonElement element, string propertyName)
        {
            JsonElement value = element.GetProperty(propertyName);
            if (value.ValueKind == JsonValueKind.Number)
                return value.GetDouble();

            return double.Parse(value.GetString()!, CultureInfo.InvariantCulture);
        }

        private static int GetInt32(JsonElement element, string propertyName)
        {
            JsonElement value = element.GetProperty(propertyName);
            if (value.ValueKind == JsonValueKind.Number)
                return value.GetInt32();

            return int.Parse(value.GetString()!, CultureInfo.InvariantCulture);
        }

        private static long GetInt64(JsonElement element, string propertyName)
        {
            JsonElement value = element.GetProperty(propertyName);
            if (value.ValueKind == JsonValueKind.Number)
                return value.GetInt64();

            return long.Parse(value.GetString()!, CultureInfo.InvariantCulture);
        }

        private static string? GetOptionalString(JsonElement element, string propertyName)
        {
            JsonElement value;
            if (!element.TryGetProperty(propertyName, out value) || value.ValueKind == JsonValueKind.Null)
                return null;

            return value.GetString();
        }
    }
}
