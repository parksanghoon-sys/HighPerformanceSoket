using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Hps.Benchmarks
{
    internal static class BaselineReportReader
    {
        public static IReadOnlyList<BaselineReport> ReadDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("baseline summary input directory 는 비어 있을 수 없습니다.", nameof(directory));

            string fullDirectory = Path.GetFullPath(directory);
            string[] files = Directory.GetFiles(fullDirectory, "*.json", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            List<BaselineReport> reports = new List<BaselineReport>();
            for (int i = 0; i < files.Length; i++)
            {
                BaselineReport? report = TryReadReport(files[i]);
                if (report != null)
                    reports.Add(report);
            }

            return reports;
        }

        // summary.json 같은 다른 artifact 는 `schema-version` 또는 `result-name`을 갖지 않는다.
        // run report 최소 식별 key 가 없으면 조용히 건너뛰어 summary command 반복 실행을 안전하게 만든다.
        private static BaselineReport? TryReadReport(string path)
        {
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement root = document.RootElement;
                if (!root.TryGetProperty("schema-version", out JsonElement schemaVersion))
                    return null;

                if (schemaVersion.GetInt32() != 1)
                    return null;

                if (!root.TryGetProperty("result-name", out JsonElement resultNameElement))
                    return null;

                BenchmarkRunIdentity identity = ReadIdentity(root);

                return new BaselineReport(
                    path.Replace('\\', '/'),
                    resultNameElement.GetString()!,
                    GetString(root, "scenario"),
                    GetInt(root, "planned-message-count"),
                    GetInt(root, "sent"),
                    GetInt(root, "received"),
                    GetInt64(root, "dropped"),
                    GetInt(root, "payload-errors"),
                    GetInt(root, "pool-rented"),
                    GetDouble(root, "actual-rate-hz"),
                    GetDouble(root, "p50-latency-us"),
                    GetDouble(root, "p99-latency-us"),
                    GetDouble(root, "p99-latency-growth-ratio"),
                    GetInt(root, "tcp-pending-send-queue-high-watermark"),
                    GetInt(root, "udp-pending-send-queue-high-watermark"),
                    identity);
            }
        }

        private static BenchmarkRunIdentity ReadIdentity(JsonElement root)
        {
            JsonElement benchmarkProfile;
            if (!root.TryGetProperty("benchmark-profile", out benchmarkProfile))
                return BenchmarkRunIdentity.Unknown;

            return new BenchmarkRunIdentity(
                benchmarkProfile.GetString()!,
                GetOptionalString(root, "runner-id"),
                GetOptionalString(root, "runner-kind"),
                GetOptionalString(root, "transport-backend"),
                GetOptionalString(root, "os-description"),
                GetOptionalString(root, "os-architecture"),
                GetOptionalString(root, "process-architecture"),
                GetOptionalString(root, "framework-description"),
                GetOptionalInt(root, "processor-count"));
        }

        private static string GetOptionalString(JsonElement root, string name)
        {
            JsonElement value;
            if (!root.TryGetProperty(name, out value))
                return "unknown";

            string? text = value.GetString();
            if (string.IsNullOrWhiteSpace(text))
                return "unknown";

            return text;
        }

        private static int GetOptionalInt(JsonElement root, string name)
        {
            JsonElement value;
            if (!root.TryGetProperty(name, out value) || value.ValueKind != JsonValueKind.Number)
                return 0;

            return value.GetInt32();
        }

        private static string GetString(JsonElement root, string name)
        {
            return root.GetProperty(name).GetString()!;
        }

        private static int GetInt(JsonElement root, string name)
        {
            return root.GetProperty(name).GetInt32();
        }

        private static long GetInt64(JsonElement root, string name)
        {
            return root.GetProperty(name).GetInt64();
        }

        private static double GetDouble(JsonElement root, string name)
        {
            JsonElement value = root.GetProperty(name);
            if (value.ValueKind == JsonValueKind.Number)
                return value.GetDouble();

            return double.Parse(value.GetString()!, CultureInfo.InvariantCulture);
        }
    }
}
