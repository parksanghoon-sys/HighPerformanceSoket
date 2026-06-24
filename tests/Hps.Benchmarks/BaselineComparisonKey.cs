using System.Collections.Generic;

namespace Hps.Benchmarks
{
    internal sealed class BaselineComparisonKey
    {
        public BaselineComparisonKey(
            string benchmarkProfile,
            string runnerId,
            string runnerKind,
            string transportBackend,
            string osDescription,
            string osArchitecture,
            string processArchitecture,
            string frameworkDescription,
            IReadOnlyList<BaselineComparisonCase> cases)
        {
            BenchmarkProfile = benchmarkProfile;
            RunnerId = runnerId;
            RunnerKind = runnerKind;
            TransportBackend = transportBackend;
            OsDescription = osDescription;
            OsArchitecture = osArchitecture;
            ProcessArchitecture = processArchitecture;
            FrameworkDescription = frameworkDescription;
            Cases = cases;
        }

        public string BenchmarkProfile { get; }

        public string RunnerId { get; }

        public string RunnerKind { get; }

        public string TransportBackend { get; }

        public string OsDescription { get; }

        public string OsArchitecture { get; }

        public string ProcessArchitecture { get; }

        public string FrameworkDescription { get; }

        public IReadOnlyList<BaselineComparisonCase> Cases { get; }
    }
}
