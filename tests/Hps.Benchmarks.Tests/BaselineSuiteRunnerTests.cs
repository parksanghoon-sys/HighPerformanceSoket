using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineSuiteRunnerTests
    {
        // runner 는 raw JSON artifact 를 대체하지 않고 load/open-loop 각각의 per-run 파일을 남긴다.
        // 이 테스트는 파일 이름과 실행 순서가 D069 baseline session 정의와 일치하는지 고정한다.
        [Fact]
        public async Task RunAsync_WhenTwoRunsRequested_WritesLoadAndOpenLoopReports()
        {
            List<BaselineRunKind> kinds = new List<BaselineRunKind>();
            List<string> paths = new List<string>();
            BaselineSuiteRunner runner = new BaselineSuiteRunner(
                kind =>
                {
                    kinds.Add(kind);
                    return Task.FromResult(CreatePassingResult(kind));
                },
                (path, result) => paths.Add(path));

            bool passed = await runner.RunAsync("out/baseline", 2, TextWriter.Null);

            Assert.True(passed);
            Assert.Equal(
                new[] { BaselineRunKind.Load, BaselineRunKind.OpenLoop, BaselineRunKind.Load, BaselineRunKind.OpenLoop },
                kinds.ToArray());
            Assert.Equal(
                new[] { "out/baseline/load-01.json", "out/baseline/open-loop-01.json", "out/baseline/load-02.json", "out/baseline/open-loop-02.json" },
                paths.ToArray());
        }

        // 하나라도 delivery/drop/leak gate 를 통과하지 못하면 suite 전체 exit code 가 실패로 전파되어야 한다.
        // latency 값은 아직 hard gate 가 아니므로 Passed 값만 집계한다.
        [Fact]
        public async Task RunAsync_WhenAnyRunFails_ReturnsFalse()
        {
            int callIndex = 0;
            BaselineSuiteRunner runner = new BaselineSuiteRunner(
                kind =>
                {
                    callIndex++;
                    if (callIndex == 2)
                        return Task.FromResult(CreateFailingResult(kind));

                    return Task.FromResult(CreatePassingResult(kind));
                },
                (path, result) => { });

            bool passed = await runner.RunAsync("out/baseline", 1, TextWriter.Null);

            Assert.False(passed);
        }

        // run count 가 100처럼 두 자리보다 커지면 파일명 index 폭도 run count 자리수와 같아야 한다.
        // baseline session artifact 를 문자열 정렬했을 때 시간 순서가 깨지지 않게 하기 위한 경계다.
        [Fact]
        public async Task RunAsync_WhenRunCountHasMoreThanTwoDigits_UsesRunCountDigitWidth()
        {
            List<string> paths = new List<string>();
            BaselineSuiteRunner runner = new BaselineSuiteRunner(
                kind => Task.FromResult(CreatePassingResult(kind)),
                (path, result) => paths.Add(path));

            bool passed = await runner.RunAsync("out/baseline", 100, TextWriter.Null);

            Assert.True(passed);
            Assert.Equal("out/baseline/load-001.json", paths[0]);
            Assert.Equal("out/baseline/open-loop-001.json", paths[1]);
            Assert.Equal("out/baseline/load-100.json", paths[198]);
            Assert.Equal("out/baseline/open-loop-100.json", paths[199]);
        }

        private static TcpLoopbackRunResult CreatePassingResult(BaselineRunKind kind)
        {
            string resultName = kind == BaselineRunKind.Load ? "load" : "open-loop";
            return new TcpLoopbackRunResult(resultName, resultName, 4096, 100, 30, 1, 1, 1, 0, 1, 0, 0, 0, 10, 20, 20, 20, 1000);
        }

        private static TcpLoopbackRunResult CreateFailingResult(BaselineRunKind kind)
        {
            string resultName = kind == BaselineRunKind.Load ? "load" : "open-loop";
            return new TcpLoopbackRunResult(resultName, resultName, 4096, 100, 30, 1, 1, 0, 0, 1, 0, 0, 0, 10, 20, 20, 20, 1000);
        }
    }
}
