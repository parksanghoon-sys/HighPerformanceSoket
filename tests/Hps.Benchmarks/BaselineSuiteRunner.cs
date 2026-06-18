using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace Hps.Benchmarks
{
    internal sealed class BaselineSuiteRunner
    {
        private readonly Func<BaselineRunKind, Task<TcpLoopbackRunResult>> _runAsync;
        private readonly Action<string, TcpLoopbackRunResult> _writeReport;

        public BaselineSuiteRunner(
            Func<BaselineRunKind, Task<TcpLoopbackRunResult>> runAsync,
            Action<string, TcpLoopbackRunResult> writeReport)
        {
            if (runAsync == null)
                throw new ArgumentNullException(nameof(runAsync));

            if (writeReport == null)
                throw new ArgumentNullException(nameof(writeReport));

            _runAsync = runAsync;
            _writeReport = writeReport;
        }

        public Task<bool> RunAsync(string outputDirectory, int runCount, TextWriter writer)
        {
            return RunCoreAsync(outputDirectory, runCount, writer);
        }

        private async Task<bool> RunCoreAsync(string outputDirectory, int runCount, TextWriter writer)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("baseline output directory 는 비어 있을 수 없습니다.", nameof(outputDirectory));

            if (runCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(runCount));

            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            bool allPassed = true;
            int padWidth = Math.Max(2, runCount.ToString(CultureInfo.InvariantCulture).Length);

            for (int index = 1; index <= runCount; index++)
            {
                TcpLoopbackRunResult load = await _runAsync(BaselineRunKind.Load).ConfigureAwait(false);
                allPassed &= load.Passed;
                string loadPath = BuildReportPath(outputDirectory, "load", index, padWidth);
                _writeReport(loadPath, load);
                writer.WriteLine("baseline-report: {0}", loadPath);

                TcpLoopbackRunResult openLoop = await _runAsync(BaselineRunKind.OpenLoop).ConfigureAwait(false);
                allPassed &= openLoop.Passed;
                string openLoopPath = BuildReportPath(outputDirectory, "open-loop", index, padWidth);
                _writeReport(openLoopPath, openLoop);
                writer.WriteLine("baseline-report: {0}", openLoopPath);
            }

            writer.WriteLine("baseline-suite-result: {0}", allPassed ? "pass" : "fail");
            return allPassed;
        }

        private static string BuildReportPath(string outputDirectory, string name, int index, int padWidth)
        {
            string fileName = string.Format(
                CultureInfo.InvariantCulture,
                "{0}-{1}.json",
                name,
                index.ToString("D" + padWidth.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));

            return Path.Combine(outputDirectory, fileName).Replace('\\', '/');
        }
    }
}
