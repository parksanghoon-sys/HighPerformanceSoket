using BrokerSample = Hps.Sample.BrokerServer;
using Xunit;

namespace Hps.Sample.BrokerServer.Tests
{
    public sealed class SampleBrokerServerCommandParserTests
    {
        // 기존 sample 실행 명령은 transport option 없이도 SAEA mode 로 해석되어야 한다.
        // 이 호환성이 깨지면 RIO 선택 기능 추가만으로 기존 sample 실행 경로가 막힌다.
        [Fact]
        public void TryParse_WhenTransportOptionIsOmitted_ReturnsSaeaMode()
        {
            BrokerSample.SampleBrokerServerCommandLine? commandLine;
            string? errorMessage;

            bool parsed = BrokerSample.SampleBrokerServerCommandParser.TryParse(
                new[] { "127.0.0.1", "5000", "65536" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.Null(errorMessage);
            Assert.NotNull(commandLine);
            Assert.Equal("127.0.0.1", commandLine!.Host);
            Assert.Equal(5000, commandLine.Port);
            Assert.Equal(65536, commandLine.MaxFrameBytes);
            Assert.Equal(BrokerSample.SampleTransportMode.Saea, commandLine.TransportMode);
        }

        // RIO 명시 선택은 parser 단계에서 보존되어야 한다.
        // 실제 RIO availability 판단은 selector 가 맡고, parser 는 사용자의 의도를 잃지 않아야 한다.
        [Fact]
        public void TryParse_WhenTransportRioIsProvided_ReturnsRioMode()
        {
            BrokerSample.SampleBrokerServerCommandLine? commandLine;
            string? errorMessage;

            bool parsed = BrokerSample.SampleBrokerServerCommandParser.TryParse(
                new[] { "loopback", "5000", "65536", "--transport", "rio" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.Null(errorMessage);
            Assert.NotNull(commandLine);
            Assert.Equal(BrokerSample.SampleTransportMode.Rio, commandLine!.TransportMode);
        }

        // auto 는 fallback 을 허용하는 preferred policy 이므로 explicit rio 와 다른 값으로 보존해야 한다.
        [Fact]
        public void TryParse_WhenTransportAutoIsProvided_ReturnsAutoMode()
        {
            BrokerSample.SampleBrokerServerCommandLine? commandLine;
            string? errorMessage;

            bool parsed = BrokerSample.SampleBrokerServerCommandParser.TryParse(
                new[] { "loopback", "5000", "65536", "--transport", "auto" },
                out commandLine,
                out errorMessage);

            Assert.True(parsed);
            Assert.Null(errorMessage);
            Assert.NotNull(commandLine);
            Assert.Equal(BrokerSample.SampleTransportMode.Auto, commandLine!.TransportMode);
        }

        // option 값 누락은 broker 시작 전에 usage error 로 멈춰야 한다.
        [Fact]
        public void TryParse_WhenTransportValueIsMissing_ReturnsError()
        {
            BrokerSample.SampleBrokerServerCommandLine? commandLine;
            string? errorMessage;

            bool parsed = BrokerSample.SampleBrokerServerCommandParser.TryParse(
                new[] { "127.0.0.1", "5000", "65536", "--transport" },
                out commandLine,
                out errorMessage);

            Assert.False(parsed);
            Assert.Equal("--transport 옵션에는 saea, rio 또는 auto 값이 필요합니다.", errorMessage);
        }

        // 알 수 없는 transport 값은 fallback 하지 않고 usage error 로 처리한다.
        [Fact]
        public void TryParse_WhenTransportValueIsUnknown_ReturnsError()
        {
            BrokerSample.SampleBrokerServerCommandLine? commandLine;
            string? errorMessage;

            bool parsed = BrokerSample.SampleBrokerServerCommandParser.TryParse(
                new[] { "127.0.0.1", "5000", "65536", "--transport", "fast" },
                out commandLine,
                out errorMessage);

            Assert.False(parsed);
            Assert.Equal("--transport 옵션은 saea, rio 또는 auto 값만 사용할 수 있습니다.", errorMessage);
        }
    }
}
