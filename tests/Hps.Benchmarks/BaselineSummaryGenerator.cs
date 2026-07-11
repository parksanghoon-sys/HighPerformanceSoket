using System;
using System.Collections.Generic;
using System.Globalization;

namespace Hps.Benchmarks
{
    internal static class BaselineSummaryGenerator
    {
        // D070의 첫 warning threshold 는 session-01 max 기반 임시 envelope 이다.
        // hard SLO 가 아니며, session 이 더 쌓이면 median-anchor 또는 percentile 기준으로 재산정한다.
        private const double LoadP99WarningThreshold = 1386.2;
        private const double OpenLoopP99WarningThreshold = 1508.3;
        private const double P99GrowthRatioWarningThreshold = 2.0;
        private const double ActualRateWarningThreshold = 95.0;
        private const int LoadTcpHighWatermarkWarningThreshold = 4;
        private const int OpenLoopTcpHighWatermarkWarningThreshold = 8;

        public static BaselineSummary Generate(string sourceDirectory, IEnumerable<BaselineReport> reports)
        {
            if (sourceDirectory == null)
                throw new ArgumentNullException(nameof(sourceDirectory));

            if (reports == null)
                throw new ArgumentNullException(nameof(reports));

            List<BaselineReport> allReports = new List<BaselineReport>(reports);
            List<BaselineWarning> warnings = new List<BaselineWarning>();
            int hardFailureCount = 0;

            for (int i = 0; i < allReports.Count; i++)
            {
                BaselineReport report = allReports[i];
                if (!report.HardPassed)
                    hardFailureCount++;

                AddWarnings(report, warnings);
            }

            BaselineKindSummary? load = CreateKindSummary("load", allReports);
            BaselineKindSummary? openLoop = CreateKindSummary("open-loop", allReports);
            BaselineComparisonResult comparison = CreateComparison(allReports);

            return new BaselineSummary(
                sourceDirectory,
                allReports.Count,
                allReports.Count > 0 && hardFailureCount == 0,
                hardFailureCount,
                warnings,
                load,
                openLoop,
                comparison);
        }

        // comparison signal 은 hard gate/warning 과 분리된 artifact 품질 신호다.
        // 여기서 한 번 계산해 둬야 JSON/Markdown/history 단계가 서로 다른 기준으로 재계산하지 않는다.
        private static BaselineComparisonResult CreateComparison(List<BaselineReport> reports)
        {
            List<BaselineComparisonMismatch> mismatches = new List<BaselineComparisonMismatch>();
            if (reports.Count == 0)
            {
                mismatches.Add(new BaselineComparisonMismatch(
                    "no-source-reports",
                    "source-report-count",
                    ">0",
                    "0",
                    null,
                    null,
                    null));

                return new BaselineComparisonResult(false, null, 0, mismatches);
            }

            int unknownRunnerCount = 0;
            BaselineReport? baseReport = null;
            Dictionary<string, BaselineComparisonCase> cases = new Dictionary<string, BaselineComparisonCase>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < reports.Count; i++)
            {
                BaselineReport report = reports[i];
                if (IsUnknownIdentity(report.Identity))
                {
                    unknownRunnerCount++;
                    mismatches.Add(new BaselineComparisonMismatch(
                        "unknown-runner",
                        "runner-identity",
                        "known",
                        "unknown",
                        report.SourcePath,
                        null,
                        null));
                    continue;
                }

                if (baseReport == null)
                    baseReport = report;
                else
                    AddIdentityMismatches(baseReport.Identity, report, mismatches);

                AddOrCompareCase(report, cases, mismatches);
            }

            if (baseReport == null)
                return new BaselineComparisonResult(false, null, unknownRunnerCount, mismatches);

            BaselineComparisonKey key = new BaselineComparisonKey(
                baseReport.Identity.BenchmarkProfile,
                baseReport.Identity.RunnerId,
                baseReport.Identity.RunnerKind,
                baseReport.Identity.TransportBackend,
                baseReport.Identity.OsDescription,
                baseReport.Identity.OsArchitecture,
                baseReport.Identity.ProcessArchitecture,
                baseReport.Identity.FrameworkDescription,
                CreateSortedCases(cases));

            return new BaselineComparisonResult(mismatches.Count == 0, key, unknownRunnerCount, mismatches);
        }

        private static bool IsUnknownIdentity(BenchmarkRunIdentity identity)
        {
            return IsUnknown(identity.BenchmarkProfile)
                || IsUnknown(identity.RunnerId)
                || IsUnknown(identity.RunnerKind)
                || IsUnknown(identity.TransportBackend)
                || IsUnknown(identity.OsDescription)
                || IsUnknown(identity.OsArchitecture)
                || IsUnknown(identity.ProcessArchitecture)
                || IsUnknown(identity.FrameworkDescription);
        }

        private static bool IsUnknown(string value)
        {
            return string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddIdentityMismatches(
            BenchmarkRunIdentity expected,
            BaselineReport actualReport,
            List<BaselineComparisonMismatch> mismatches)
        {
            BenchmarkRunIdentity actual = actualReport.Identity;
            AddStringMismatch("benchmark-profile", expected.BenchmarkProfile, actual.BenchmarkProfile, actualReport.SourcePath, mismatches);
            AddStringMismatch("runner-id", expected.RunnerId, actual.RunnerId, actualReport.SourcePath, mismatches);
            AddStringMismatch("runner-kind", expected.RunnerKind, actual.RunnerKind, actualReport.SourcePath, mismatches);
            AddStringMismatch("transport-backend", expected.TransportBackend, actual.TransportBackend, actualReport.SourcePath, mismatches);
            AddStringMismatch("os-description", expected.OsDescription, actual.OsDescription, actualReport.SourcePath, mismatches);
            AddStringMismatch("os-architecture", expected.OsArchitecture, actual.OsArchitecture, actualReport.SourcePath, mismatches);
            AddStringMismatch("process-architecture", expected.ProcessArchitecture, actual.ProcessArchitecture, actualReport.SourcePath, mismatches);
            AddStringMismatch("framework-description", expected.FrameworkDescription, actual.FrameworkDescription, actualReport.SourcePath, mismatches);
        }

        private static void AddStringMismatch(
            string field,
            string expected,
            string actual,
            string sourcePath,
            List<BaselineComparisonMismatch> mismatches)
        {
            if (string.Equals(expected, actual, StringComparison.Ordinal))
                return;

            mismatches.Add(new BaselineComparisonMismatch(
                "comparison-key-mismatch",
                field,
                expected,
                actual,
                sourcePath,
                null,
                null));
        }

        private static void AddOrCompareCase(
            BaselineReport report,
            Dictionary<string, BaselineComparisonCase> cases,
            List<BaselineComparisonMismatch> mismatches)
        {
            BaselineComparisonCase observed = CreateCase(report);
            BaselineComparisonCase? expected;
            if (!cases.TryGetValue(report.ResultName, out expected))
            {
                cases.Add(report.ResultName, observed);
                return;
            }

            BaselineComparisonCase existing = expected!;
            string prefix = "cases[" + existing.ResultName + "].";
            AddStringMismatch(prefix + "scenario", existing.Scenario, observed.Scenario, report.SourcePath, mismatches);
            AddIntMismatch(prefix + "payload-bytes", existing.PayloadBytes, observed.PayloadBytes, report.SourcePath, mismatches);
            AddDoubleMismatch(prefix + "target-rate-hz", existing.TargetRateHz, observed.TargetRateHz, report.SourcePath, mismatches);
            AddIntMismatch(prefix + "target-duration-seconds", existing.TargetDurationSeconds, observed.TargetDurationSeconds, report.SourcePath, mismatches);
        }

        private static BaselineComparisonCase CreateCase(BaselineReport report)
        {
            return new BaselineComparisonCase(
                report.ResultName,
                report.Scenario,
                report.PayloadBytes,
                report.TargetRateHz,
                report.TargetDurationSeconds);
        }

        private static void AddIntMismatch(
            string field,
            int expected,
            int actual,
            string sourcePath,
            List<BaselineComparisonMismatch> mismatches)
        {
            if (expected == actual)
                return;

            mismatches.Add(new BaselineComparisonMismatch(
                "comparison-key-mismatch",
                field,
                expected.ToString(CultureInfo.InvariantCulture),
                actual.ToString(CultureInfo.InvariantCulture),
                sourcePath,
                null,
                null));
        }

        private static void AddDoubleMismatch(
            string field,
            double expected,
            double actual,
            string sourcePath,
            List<BaselineComparisonMismatch> mismatches)
        {
            if (expected.Equals(actual))
                return;

            mismatches.Add(new BaselineComparisonMismatch(
                "comparison-key-mismatch",
                field,
                expected.ToString("G17", CultureInfo.InvariantCulture),
                actual.ToString("G17", CultureInfo.InvariantCulture),
                sourcePath,
                null,
                null));
        }

        private static IReadOnlyList<BaselineComparisonCase> CreateSortedCases(Dictionary<string, BaselineComparisonCase> cases)
        {
            List<BaselineComparisonCase> sorted = new List<BaselineComparisonCase>(cases.Values);
            sorted.Sort(
                delegate (BaselineComparisonCase left, BaselineComparisonCase right)
                {
                    return StringComparer.OrdinalIgnoreCase.Compare(left.ResultName, right.ResultName);
                });
            return sorted;
        }

        // kind별 summary 는 사람이 outlier 를 해석할 때 보는 중심경향/범위 정보다.
        // latency 값은 hard gate 로 쓰지 않으므로, min/max 뿐 아니라 median 도 함께 보존한다.
        private static BaselineKindSummary? CreateKindSummary(string kind, List<BaselineReport> reports)
        {
            bool hasAny = false;
            int runCount = 0;
            double p50Min = 0;
            double p50Max = 0;
            double p99Min = 0;
            double p99Max = 0;
            double growthMin = 0;
            double growthMax = 0;
            double rateMin = 0;
            double rateMax = 0;
            int tcpHwmMin = 0;
            int tcpHwmMax = 0;
            long droppedTotal = 0;
            int payloadErrorTotal = 0;
            int poolRentedMax = 0;
            List<double> p50Values = new List<double>();
            List<double> p99Values = new List<double>();

            for (int i = 0; i < reports.Count; i++)
            {
                BaselineReport report = reports[i];
                if (!string.Equals(report.ResultName, kind, StringComparison.OrdinalIgnoreCase))
                    continue;

                int sendQueueHighWatermark = GetSendQueueHighWatermark(report);

                if (!hasAny)
                {
                    p50Min = report.P50LatencyMicroseconds;
                    p50Max = report.P50LatencyMicroseconds;
                    p99Min = report.P99LatencyMicroseconds;
                    p99Max = report.P99LatencyMicroseconds;
                    growthMin = report.P99LatencyGrowthRatio;
                    growthMax = report.P99LatencyGrowthRatio;
                    rateMin = report.ActualRateHz;
                    rateMax = report.ActualRateHz;
                    tcpHwmMin = sendQueueHighWatermark;
                    tcpHwmMax = sendQueueHighWatermark;
                    poolRentedMax = report.PoolRented;
                    hasAny = true;
                }
                else
                {
                    p50Min = Math.Min(p50Min, report.P50LatencyMicroseconds);
                    p50Max = Math.Max(p50Max, report.P50LatencyMicroseconds);
                    p99Min = Math.Min(p99Min, report.P99LatencyMicroseconds);
                    p99Max = Math.Max(p99Max, report.P99LatencyMicroseconds);
                    growthMin = Math.Min(growthMin, report.P99LatencyGrowthRatio);
                    growthMax = Math.Max(growthMax, report.P99LatencyGrowthRatio);
                    rateMin = Math.Min(rateMin, report.ActualRateHz);
                    rateMax = Math.Max(rateMax, report.ActualRateHz);
                    tcpHwmMin = Math.Min(tcpHwmMin, sendQueueHighWatermark);
                    tcpHwmMax = Math.Max(tcpHwmMax, sendQueueHighWatermark);
                    poolRentedMax = Math.Max(poolRentedMax, report.PoolRented);
                }

                runCount++;
                p50Values.Add(report.P50LatencyMicroseconds);
                p99Values.Add(report.P99LatencyMicroseconds);
                droppedTotal += report.Dropped;
                payloadErrorTotal += report.PayloadErrors;
            }

            if (!hasAny)
                return null;

            return new BaselineKindSummary(
                kind,
                runCount,
                p50Min,
                p50Max,
                CalculateMedian(p50Values),
                p99Min,
                p99Max,
                CalculateMedian(p99Values),
                growthMin,
                growthMax,
                rateMin,
                rateMax,
                tcpHwmMin,
                tcpHwmMax,
                droppedTotal,
                payloadErrorTotal,
                poolRentedMax);
        }

        // summary JSON의 tcp-hwm-* 이름은 기존 artifact 호환성을 위해 유지한다.
        // 실제 값은 protocol과 무관하게 활성 send queue의 HWM을 보존하도록 두 진단값 중 큰 값을 사용한다.
        private static int GetSendQueueHighWatermark(BaselineReport report)
        {
            return Math.Max(
                report.TcpPendingSendQueueHighWatermark,
                report.UdpPendingSendQueueHighWatermark);
        }

        // 입력 list 는 summary 생성 중에 새로 만든 임시 collection 이므로 정렬로 직접 바꿔도 caller 상태를 오염시키지 않는다.
        private static double CalculateMedian(List<double> values)
        {
            values.Sort();
            int middle = values.Count / 2;

            if ((values.Count % 2) == 1)
                return values[middle];

            return (values[middle - 1] + values[middle]) / 2.0;
        }

        // warning 은 aggregate 1건이 아니라 run 단위로 만든다.
        // source path 를 남겨야 benchmark artifact 에서 튄 run 을 바로 추적할 수 있다.
        private static void AddWarnings(BaselineReport report, List<BaselineWarning> warnings)
        {
            bool openLoop = string.Equals(report.ResultName, "open-loop", StringComparison.OrdinalIgnoreCase);
            double p99Threshold = openLoop ? OpenLoopP99WarningThreshold : LoadP99WarningThreshold;
            int hwmThreshold = openLoop ? OpenLoopTcpHighWatermarkWarningThreshold : LoadTcpHighWatermarkWarningThreshold;
            int sendQueueHighWatermark = GetSendQueueHighWatermark(report);
            string kind = openLoop ? "open-loop" : "load";

            if (report.P99LatencyMicroseconds > p99Threshold)
            {
                warnings.Add(new BaselineWarning(
                    kind + "-p99-latency-high",
                    kind,
                    "p99-latency-us",
                    report.P99LatencyMicroseconds,
                    p99Threshold,
                    report.SourcePath));
            }

            if (report.P99LatencyGrowthRatio > P99GrowthRatioWarningThreshold)
            {
                warnings.Add(new BaselineWarning(
                    "p99-growth-ratio-high",
                    kind,
                    "p99-latency-growth-ratio",
                    report.P99LatencyGrowthRatio,
                    P99GrowthRatioWarningThreshold,
                    report.SourcePath));
            }

            if (report.ActualRateHz < ActualRateWarningThreshold)
            {
                warnings.Add(new BaselineWarning(
                    "actual-rate-low",
                    kind,
                    "actual-rate-hz",
                    report.ActualRateHz,
                    ActualRateWarningThreshold,
                    report.SourcePath));
            }

            if (sendQueueHighWatermark >= hwmThreshold)
            {
                warnings.Add(new BaselineWarning(
                    kind + "-tcp-hwm-high",
                    kind,
                    "tcp-pending-send-queue-high-watermark",
                    sendQueueHighWatermark,
                    hwmThreshold,
                    report.SourcePath));
            }
        }
    }
}
