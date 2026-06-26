using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hps.Transport;
using BrokerSample = Hps.Sample.BrokerServer;
using Xunit;

namespace Hps.Sample.BrokerServer.Tests
{
    public sealed class SampleTransportSelectorTests
    {
        // saea mode 는 capability probe 없이 SAEA factory 만 호출해야 한다.
        [Fact]
        public void Select_WhenModeIsSaea_ReturnsSaeaTransport()
        {
            BrokerSample.SampleTransportSelection selection = Select(BrokerSample.SampleTransportMode.Saea, RioCapabilityStatus.Available);

            Assert.True(selection.Succeeded);
            Assert.Equal("SaeaTransport", selection.SelectedBackendName);
            Assert.Null(selection.ErrorMessage);
        }

        // explicit rio 는 available 일 때만 RIO backend 를 선택한다.
        [Fact]
        public void Select_WhenModeIsRioAndAvailable_ReturnsRioTransport()
        {
            BrokerSample.SampleTransportSelection selection = Select(BrokerSample.SampleTransportMode.Rio, RioCapabilityStatus.Available);

            Assert.True(selection.Succeeded);
            Assert.Equal("RioTransport", selection.SelectedBackendName);
        }

        // explicit rio 는 unavailable 시 fallback 하지 않고 실패한다.
        [Fact]
        public void Select_WhenModeIsRioAndUnavailable_ReturnsFailure()
        {
            BrokerSample.SampleTransportSelection selection = Select(BrokerSample.SampleTransportMode.Rio, RioCapabilityStatus.Unavailable);

            Assert.False(selection.Succeeded);
            Assert.Equal(1, selection.ExitCode);
            Assert.Contains("RIO transport를 사용할 수 없습니다.", selection.ErrorMessage!);
        }

        // auto 는 RIO available 시 RIO를 선택한다.
        [Fact]
        public void Select_WhenModeIsAutoAndAvailable_ReturnsRioTransport()
        {
            BrokerSample.SampleTransportSelection selection = Select(BrokerSample.SampleTransportMode.Auto, RioCapabilityStatus.Available);

            Assert.True(selection.Succeeded);
            Assert.Equal("RioTransport", selection.SelectedBackendName);
            Assert.Null(selection.NoticeMessage);
        }

        // auto 는 unsupported/unavailable 시 SAEA로 fallback 하고 그 사실을 notice 로 남긴다.
        [Fact]
        public void Select_WhenModeIsAutoAndUnsupported_ReturnsSaeaWithNotice()
        {
            BrokerSample.SampleTransportSelection selection = Select(BrokerSample.SampleTransportMode.Auto, RioCapabilityStatus.UnsupportedOperatingSystem);

            Assert.True(selection.Succeeded);
            Assert.Equal("SaeaTransport", selection.SelectedBackendName);
            Assert.Contains("RIO unavailable", selection.NoticeMessage!);
        }

        // parser 밖에서 selector 를 직접 호출하는 경우에도 정의되지 않은 enum 값이 auto fallback 으로 해석되면 안 된다.
        // 잘못된 enum 은 사용자 입력이 아니라 호출자 계약 위반이므로 즉시 프로그래밍 오류로 드러낸다.
        [Fact]
        public void Select_WhenModeIsUndefined_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                delegate
                {
                    Select((BrokerSample.SampleTransportMode)99, RioCapabilityStatus.Unavailable);
                });
        }

        private static BrokerSample.SampleTransportSelection Select(BrokerSample.SampleTransportMode mode, RioCapabilityStatus status)
        {
            Func<RioCapabilityStatus> probe = delegate { return status; };
            Func<ITransport> createSaea = delegate { return new FakeTransport("SaeaTransport"); };
            Func<ITransport> createRio = delegate { return new FakeTransport("RioTransport"); };
            return BrokerSample.SampleTransportSelector.Select(mode, probe, createSaea, createRio);
        }

        private sealed class FakeTransport : TransportBase
        {
            public FakeTransport(string name)
            {
                Name = name;
            }

            public string Name { get; }

            public override ValueTask StartAsync(CancellationToken cancellationToken = default)
            {
                return new ValueTask();
            }

            public override ValueTask StopAsync(CancellationToken cancellationToken = default)
            {
                return new ValueTask();
            }

            public override ValueTask<IConnectionListener> ListenTcpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public override ValueTask<IConnection> ConnectTcpAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
        }
    }
}
