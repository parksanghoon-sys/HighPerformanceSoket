using System;
using System.Collections.Generic;

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

            return new BaselineHistory(sourceRoot, sessions, hardPassed, failedSessionCount, warningCount);
        }
    }
}
