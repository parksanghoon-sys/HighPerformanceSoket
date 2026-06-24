using System;
using System.Collections.Generic;
using System.Globalization;

namespace Hps.Benchmarks
{
    internal static class BaselineHistoryGenerator
    {
        public static BaselineHistory Generate(string sourceRoot, IReadOnlyList<BaselineHistorySession> sessions)
        {
            if (sourceRoot == null)
                throw new ArgumentNullException(nameof(sourceRoot));

            if (sessions == null)
                throw new ArgumentNullException(nameof(sessions));

            bool hardPassed = true;
            int failedSessionCount = 0;
            int warningCount = 0;
            for (int i = 0; i < sessions.Count; i++)
            {
                if (!sessions[i].HardPassed)
                {
                    hardPassed = false;
                    failedSessionCount++;
                }

                warningCount += sessions[i].WarningCount;
            }

            BaselineComparisonResult comparison = GenerateComparison(sessions);
            return new BaselineHistory(sourceRoot, sessions, hardPassed, failedSessionCount, warningCount, comparison);
        }

        private static BaselineComparisonResult GenerateComparison(IReadOnlyList<BaselineHistorySession> sessions)
        {
            if (sessions.Count == 0)
            {
                return new BaselineComparisonResult(
                    false,
                    null,
                    0,
                    new[]
                    {
                        new BaselineComparisonMismatch(
                            "no-source-reports",
                            "sessions",
                            ">0",
                            "0",
                            null,
                            null,
                            null)
                    });
            }

            BaselineComparisonKey? baselineKey = null;
            int unknownRunnerCount = 0;
            List<BaselineComparisonMismatch> mismatches = new List<BaselineComparisonMismatch>();

            for (int i = 0; i < sessions.Count; i++)
            {
                BaselineHistorySession session = sessions[i];
                unknownRunnerCount += session.Comparison.UnknownRunnerCount;

                if (!session.Comparison.Compatible)
                {
                    AddSessionMismatches(mismatches, session);
                    continue;
                }

                if (session.Comparison.Key == null)
                {
                    mismatches.Add(CreateHistoryMismatch(
                        session,
                        "comparison-key",
                        "present",
                        "missing"));
                    continue;
                }

                if (baselineKey == null)
                {
                    baselineKey = session.Comparison.Key;
                    continue;
                }

                AddKeyMismatches(mismatches, baselineKey, session.Comparison.Key, session);
            }

            return new BaselineComparisonResult(mismatches.Count == 0 && baselineKey != null, baselineKey, unknownRunnerCount, mismatches);
        }

        private static void AddSessionMismatches(List<BaselineComparisonMismatch> mismatches, BaselineHistorySession session)
        {
            if (session.Comparison.Mismatches.Count == 0)
            {
                mismatches.Add(CreateHistoryMismatch(session, "comparison-compatible", "true", "false"));
                return;
            }

            for (int i = 0; i < session.Comparison.Mismatches.Count; i++)
            {
                BaselineComparisonMismatch mismatch = session.Comparison.Mismatches[i];
                mismatches.Add(new BaselineComparisonMismatch(
                    mismatch.Code,
                    mismatch.Field,
                    mismatch.Expected,
                    mismatch.Actual,
                    mismatch.SourcePath,
                    mismatch.Session ?? session.Session,
                    mismatch.SummaryPath ?? session.SummaryPath));
            }
        }

        private static void AddKeyMismatches(
            List<BaselineComparisonMismatch> mismatches,
            BaselineComparisonKey expected,
            BaselineComparisonKey actual,
            BaselineHistorySession session)
        {
            AddStringMismatch(mismatches, session, "benchmark-profile", expected.BenchmarkProfile, actual.BenchmarkProfile);
            AddStringMismatch(mismatches, session, "runner-id", expected.RunnerId, actual.RunnerId);
            AddStringMismatch(mismatches, session, "runner-kind", expected.RunnerKind, actual.RunnerKind);
            AddStringMismatch(mismatches, session, "transport-backend", expected.TransportBackend, actual.TransportBackend);
            AddStringMismatch(mismatches, session, "os-description", expected.OsDescription, actual.OsDescription);
            AddStringMismatch(mismatches, session, "os-architecture", expected.OsArchitecture, actual.OsArchitecture);
            AddStringMismatch(mismatches, session, "process-architecture", expected.ProcessArchitecture, actual.ProcessArchitecture);
            AddStringMismatch(mismatches, session, "framework-description", expected.FrameworkDescription, actual.FrameworkDescription);
            AddCaseMismatches(mismatches, expected, actual, session);
        }

        private static void AddCaseMismatches(
            List<BaselineComparisonMismatch> mismatches,
            BaselineComparisonKey expected,
            BaselineComparisonKey actual,
            BaselineHistorySession session)
        {
            Dictionary<string, BaselineComparisonCase> expectedCases = new Dictionary<string, BaselineComparisonCase>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < expected.Cases.Count; i++)
                expectedCases[expected.Cases[i].ResultName] = expected.Cases[i];

            Dictionary<string, BaselineComparisonCase> actualCases = new Dictionary<string, BaselineComparisonCase>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < actual.Cases.Count; i++)
                actualCases[actual.Cases[i].ResultName] = actual.Cases[i];

            foreach (KeyValuePair<string, BaselineComparisonCase> expectedCase in expectedCases)
            {
                if (!actualCases.ContainsKey(expectedCase.Key))
                {
                    mismatches.Add(CreateHistoryMismatch(session, "cases[" + expectedCase.Key + "]", "present", "missing"));
                    continue;
                }

                BaselineComparisonCase actualCase = actualCases[expectedCase.Key];
                AddStringMismatch(mismatches, session, "cases[" + expectedCase.Key + "].scenario", expectedCase.Value.Scenario, actualCase.Scenario);
                AddIntMismatch(mismatches, session, "cases[" + expectedCase.Key + "].payload-bytes", expectedCase.Value.PayloadBytes, actualCase.PayloadBytes);
                AddDoubleMismatch(mismatches, session, "cases[" + expectedCase.Key + "].target-rate-hz", expectedCase.Value.TargetRateHz, actualCase.TargetRateHz);
                AddIntMismatch(mismatches, session, "cases[" + expectedCase.Key + "].target-duration-seconds", expectedCase.Value.TargetDurationSeconds, actualCase.TargetDurationSeconds);
            }

            foreach (KeyValuePair<string, BaselineComparisonCase> actualCase in actualCases)
            {
                if (!expectedCases.ContainsKey(actualCase.Key))
                    mismatches.Add(CreateHistoryMismatch(session, "cases[" + actualCase.Key + "]", "missing", "present"));
            }
        }

        private static void AddStringMismatch(
            List<BaselineComparisonMismatch> mismatches,
            BaselineHistorySession session,
            string field,
            string expected,
            string actual)
        {
            if (string.Equals(expected, actual, StringComparison.Ordinal))
                return;

            mismatches.Add(CreateHistoryMismatch(session, field, expected, actual));
        }

        private static void AddIntMismatch(
            List<BaselineComparisonMismatch> mismatches,
            BaselineHistorySession session,
            string field,
            int expected,
            int actual)
        {
            if (expected == actual)
                return;

            mismatches.Add(CreateHistoryMismatch(
                session,
                field,
                expected.ToString(CultureInfo.InvariantCulture),
                actual.ToString(CultureInfo.InvariantCulture)));
        }

        private static void AddDoubleMismatch(
            List<BaselineComparisonMismatch> mismatches,
            BaselineHistorySession session,
            string field,
            double expected,
            double actual)
        {
            if (expected.Equals(actual))
                return;

            mismatches.Add(CreateHistoryMismatch(
                session,
                field,
                expected.ToString("R", CultureInfo.InvariantCulture),
                actual.ToString("R", CultureInfo.InvariantCulture)));
        }

        private static BaselineComparisonMismatch CreateHistoryMismatch(
            BaselineHistorySession session,
            string field,
            string expected,
            string actual)
        {
            return new BaselineComparisonMismatch(
                "history-comparison-key-mismatch",
                field,
                expected,
                actual,
                null,
                session.Session,
                session.SummaryPath);
        }
    }
}
