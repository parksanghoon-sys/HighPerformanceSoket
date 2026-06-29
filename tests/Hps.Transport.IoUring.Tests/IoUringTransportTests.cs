using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Hps.Transport;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringTransportTests
    {
        // skeleton root 는 opt-in construction 과 Start/Stop 수명만 먼저 제공한다.
        // 이 경계가 안정적이어야 후속 native queue owner 를 같은 root type 에 붙일 수 있다.
        [Fact]
        public async Task IoUringTransport_WhenConstructed_StartStopDoesNotThrow()
        {
            Type? transportType = Type.GetType("Hps.Transport.IoUringTransport, Hps.Transport.IoUring");

            Assert.NotNull(transportType);

            using (ITransport transport = (ITransport)Activator.CreateInstance(transportType!)!)
            {
                await transport.StartAsync();
                await transport.StopAsync();
            }
        }

        // non-Linux 에서 TCP listen 이 native 구현으로 진입하면 안 된다.
        // 명시적 NotSupportedException 으로 수렴해야 host selector 가 fallback 판단을 할 수 있다.
        [Fact]
        public async Task ListenTcpAsync_WhenNotLinux_ThrowsNotSupportedException()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            Type? transportType = Type.GetType("Hps.Transport.IoUringTransport, Hps.Transport.IoUring");

            Assert.NotNull(transportType);

            using (ITransport transport = (ITransport)Activator.CreateInstance(transportType!)!)
            {
                await transport.StartAsync();

                await Assert.ThrowsAsync<NotSupportedException>(async delegate()
                {
                    await transport.ListenTcpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                });
            }
        }

        // TCP connect 도 listen 과 같은 unsupported boundary 로 막는다.
        // 두 경로의 예외 정책이 갈라지면 backend selector 의 오류 처리와 사용자 메시지가 흔들린다.
        [Fact]
        public async Task ConnectTcpAsync_WhenNotLinux_ThrowsNotSupportedException()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            Type? transportType = Type.GetType("Hps.Transport.IoUringTransport, Hps.Transport.IoUring");

            Assert.NotNull(transportType);

            using (ITransport transport = (ITransport)Activator.CreateInstance(transportType!)!)
            {
                await transport.StartAsync();

                await Assert.ThrowsAsync<NotSupportedException>(async delegate()
                {
                    await transport.ConnectTcpAsync(new IPEndPoint(IPAddress.Loopback, 9));
                });
            }
        }

        // UDP는 첫 boundary 에서 아직 지원한다고 보이면 안 된다.
        // ReceiveMsg/SendMsg owner 가 생기기 전까지 명시적 unsupported 로 남겨 후속 구현 범위를 분리한다.
        [Fact]
        public async Task BindUdpAsync_WhenNotLinux_ThrowsNotSupportedException()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            Type? transportType = Type.GetType("Hps.Transport.IoUringTransport, Hps.Transport.IoUring");

            Assert.NotNull(transportType);

            using (ITransport transport = (ITransport)Activator.CreateInstance(transportType!)!)
            {
                await transport.StartAsync();

                await Assert.ThrowsAsync<NotSupportedException>(async delegate()
                {
                    await transport.BindUdpAsync(new IPEndPoint(IPAddress.Loopback, 0));
                });
            }
        }
    }
}
