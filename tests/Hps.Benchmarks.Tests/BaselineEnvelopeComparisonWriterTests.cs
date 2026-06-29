using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Hps.Benchmarks.Tests
{
    public sealed class BaselineEnvelopeComparisonWriterTests
    {
        // envelope JSON 은 자동화가 읽을 canonical artifact 다.
        // writer 타입이 없으면 warning-count 와 분리된 review signal 을 파일로 남길 수 없다.
        [Fact]
        public void Contract_BaselineEnvelopeComparisonWriterExists()
        {
            Assert.NotNull(typeof(BenchmarkCommandParser).Assembly.GetType("Hps.Benchmarks.BaselineEnvelopeComparisonWriter"));
        }

        // Markdown 은 사람이 regression signal 을 빠르게 읽는 보조 artifact 다.
        // JSON writer 와 별도 타입으로 두어도 같은 comparison model 을 입력으로 받아야 한다.
        [Fact]
        public void Contract_BaselineEnvelopeComparisonMarkdownWriterExists()
        {
            Assert.NotNull(typeof(BenchmarkCommandParser).Assembly.GetType("Hps.Benchmarks.BaselineEnvelopeComparisonMarkdownWriter"));
        }

        // envelope JSON 은 자동화가 읽을 canonical artifact 다.
        // signal-count 와 metric row 가 없으면 warning-count 와 분리된 review signal 을 기계적으로 추적할 수 없다.
        [Fact]
        public void Write_WhenComparisonHasMetrics_WritesEnvelopeJsonShape()
        {
            string directory = CreateTempDirectory();
            string path = Path.Combine(directory, "envelope.json");
            BaselineEnvelopeComparison comparison = CreateComparison(signaled: true);

            BaselineEnvelopeComparisonWriter.Write(path, comparison);

            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement root = document.RootElement;
                Assert.Equal(1, root.GetProperty("envelope-version").GetInt32());
                Assert.False(root.GetProperty("envelope-compatible").GetBoolean());
                Assert.Equal(1, root.GetProperty("envelope-signal-count").GetInt32());
                Assert.Equal("runner-a", root.GetProperty("reference-key").GetProperty("runner-id").GetString());
                Assert.Equal("runner-a", root.GetProperty("candidate-key").GetProperty("runner-id").GetString());

                JsonElement p99 = root.GetProperty("by-kind").GetProperty("load").GetProperty("p99-max-us");
                Assert.Equal("upper", p99.GetProperty("direction").GetString());
                Assert.Equal(935.6, p99.GetProperty("reference").GetDouble());
                Assert.Equal(1122.72, p99.GetProperty("limit").GetDouble(), 2);
                Assert.Equal(1200.0, p99.GetProperty("candidate").GetDouble());
                Assert.True(p99.GetProperty("signaled").GetBoolean());
                Assert.Equal("p99-max-us", root.GetProperty("signals")[0].GetProperty("metric").GetString());
            }
        }

        // Markdown 은 사람이 regression signal 을 빠르게 읽는 보조 artifact 다.
        // JSON 과 같은 comparison model 을 써야 수동 리뷰와 자동화가 다른 값을 보지 않는다.
        [Fact]
        public void MarkdownWriter_WhenComparisonHasSignals_WritesMetricAndSignalSections()
        {
            BaselineEnvelopeComparison comparison = CreateComparison(signaled: true);
            StringWriter writer = new StringWriter();

            BaselineEnvelopeComparisonMarkdownWriter.Write(writer, comparison);

            string markdown = writer.ToString();
            Assert.Contains("# Baseline Envelope Comparison", markdown);
            Assert.Contains("- envelope-compatible: false", markdown);
            Assert.Contains("- envelope-signal-count: 1", markdown);
            Assert.Contains("| load | p99-max-us | upper | 935.6 | 1122.72 | 1200 | true |", markdown);
            Assert.Contains("| load | p99-max-us | upper | 1122.72 | 1200 |", markdown);
        }

        private static string CreateTempDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "hps-envelope-writer-tests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static BaselineEnvelopeComparison CreateComparison(bool signaled)
        {
            BaselineComparisonKey key = CreateKey("runner-a");
            List<BaselineEnvelopeSignal> signals = new List<BaselineEnvelopeSignal>();
            if (signaled)
                signals.Add(new BaselineEnvelopeSignal("load", "p99-max-us", "upper", 935.6, 1122.72, 1200.0));

            return new BaselineEnvelopeComparison(
                "reference/history.json",
                "candidate/summary.json",
                !signaled,
                key,
                key,
                new[]
                {
                    new BaselineEnvelopeKindComparison(
                        "load",
                        new[]
                        {
                            new BaselineEnvelopeMetricComparison("p99-max-us", "upper", 935.6, 1122.72, 1200.0, signaled)
                        })
                },
                new BaselineEnvelopeMismatch[0],
                signals);
        }

        private static BaselineComparisonKey CreateKey(string runnerId)
        {
            return new BaselineComparisonKey(
                "tcp-loopback-saea-v1",
                runnerId,
                "local",
                "SaeaTransport",
                "Windows",
                "X64",
                "X64",
                ".NET 9.0",
                new[]
                {
                    new BaselineComparisonCase("load", "tcp-loopback-saea-load", 4096, 100.0, 30)
                });
        }
    }
}
