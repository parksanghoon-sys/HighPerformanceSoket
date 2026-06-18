using System;
using System.Collections.Generic;

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

            return new BaselineSummary(
                sourceDirectory,
                allReports.Count,
                allReports.Count > 0 && hardFailureCount == 0,
                hardFailureCount,
                warnings,
                load,
                openLoop);
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
                    tcpHwmMin = report.TcpPendingSendQueueHighWatermark;
                    tcpHwmMax = report.TcpPendingSendQueueHighWatermark;
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
                    tcpHwmMin = Math.Min(tcpHwmMin, report.TcpPendingSendQueueHighWatermark);
                    tcpHwmMax = Math.Max(tcpHwmMax, report.TcpPendingSendQueueHighWatermark);
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

            if (report.TcpPendingSendQueueHighWatermark >= hwmThreshold)
            {
                warnings.Add(new BaselineWarning(
                    kind + "-tcp-hwm-high",
                    kind,
                    "tcp-pending-send-queue-high-watermark",
                    report.TcpPendingSendQueueHighWatermark,
                    hwmThreshold,
                    report.SourcePath));
            }
        }
    }
}
