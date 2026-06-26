using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Hps.Sample.BrokerServer.Tests
{
    public sealed class SampleBrokerServerProgramTests
    {
        // Program wiring 은 parser error 를 broker start 이전에 exit code 2로 반환하고,
        // transport selector 사용법을 함께 보여야 한다.
        [Fact]
        public async Task Main_WhenTransportValueIsMissing_ReturnsInvalidArgumentsAndTransportUsage()
        {
            Tuple<int, string> result = await InvokeMainWithCapturedErrorAsync(
                new[] { "127.0.0.1", "5000", "65536", "--transport" });

            Assert.Equal(2, result.Item1);
            Assert.Contains("--transport <saea|rio|auto>", result.Item2);
        }

        // unknown transport 는 fallback 하지 않고 usage error 로 끝난다.
        [Fact]
        public async Task Main_WhenTransportValueIsUnknown_ReturnsInvalidArgumentsAndTransportUsage()
        {
            Tuple<int, string> result = await InvokeMainWithCapturedErrorAsync(
                new[] { "127.0.0.1", "5000", "65536", "--transport", "fast" });

            Assert.Equal(2, result.Item1);
            Assert.Contains("--transport <saea|rio|auto>", result.Item2);
        }

        private static async Task<Tuple<int, string>> InvokeMainWithCapturedErrorAsync(string[] args)
        {
            TextWriter originalError = Console.Error;
            using (StringWriter writer = new StringWriter())
            {
                Console.SetError(writer);
                try
                {
                    int exitCode = await InvokeMainAsync(args).ConfigureAwait(false);
                    return Tuple.Create(exitCode, writer.ToString());
                }
                finally
                {
                    Console.SetError(originalError);
                }
            }
        }

        private static async Task<int> InvokeMainAsync(string[] args)
        {
            Assembly assembly = Assembly.Load("Hps.Sample.BrokerServer");
            Type? programType = assembly.GetType("Hps.Sample.BrokerServer.Program");
            Assert.NotNull(programType);
            MethodInfo? main = programType!.GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(main);

            Task<int> task = (Task<int>)main!.Invoke(null, new object[] { args })!;
            return await task.ConfigureAwait(false);
        }
    }
}
