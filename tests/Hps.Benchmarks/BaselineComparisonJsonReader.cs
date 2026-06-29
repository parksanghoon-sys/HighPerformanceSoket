using System.Collections.Generic;
using System.Text.Json;

namespace Hps.Benchmarks
{
    internal static class BaselineComparisonJsonReader
    {
        // summary/history/envelope reader 가 같은 comparison JSON 계약을 해석하게 하는 단일 진입점이다.
        // legacyMissingCode 를 호출자가 넘기게 해 summary 누락과 history 누락을 같은 구조로 기록하되 원인 code 는 구분한다.
        public static BaselineComparisonResult Read(
            JsonElement root,
            string? defaultSession,
            string? defaultSummaryPath,
            string legacyMissingCode)
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
                            legacyMissingCode,
                            "comparison-compatible",
                            "present",
                            "missing",
                            null,
                            defaultSession,
                            defaultSummaryPath)
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
                    mismatches.Add(ReadComparisonMismatch(mismatchElement, defaultSession, defaultSummaryPath));
            }

            return new BaselineComparisonResult(
                compatibleElement.GetBoolean(),
                key,
                unknownRunnerCount,
                mismatches);
        }

        private static BaselineComparisonKey ReadComparisonKey(JsonElement key)
        {
            // comparison key 는 runner/environment 만으로 끝나지 않고 result-name 별 workload case 까지 포함한다.
            // case 배열을 그대로 보존해야 load/open-loop scenario 차이를 같은 key 안에서 비교할 수 있다.
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

        private static BaselineComparisonMismatch ReadComparisonMismatch(
            JsonElement mismatch,
            string? defaultSession,
            string? defaultSummaryPath)
        {
            return new BaselineComparisonMismatch(
                mismatch.GetProperty("code").GetString()!,
                mismatch.GetProperty("field").GetString()!,
                mismatch.GetProperty("expected").GetString()!,
                mismatch.GetProperty("actual").GetString()!,
                GetOptionalString(mismatch, "source-path"),
                GetOptionalString(mismatch, "session") ?? defaultSession,
                GetOptionalString(mismatch, "summary-path") ?? defaultSummaryPath);
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
