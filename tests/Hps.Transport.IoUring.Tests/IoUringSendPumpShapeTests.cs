using System;
using System.Reflection;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringSendPumpShapeTests
    {
        // TCP send pump는 pending queue에서 꺼낸 buffer를 SEND SQE로 submit해야 한다.
        // Windows 환경에서도 send entry point 부재를 Red로 확인해 Linux 전용 loopback skip에 가려지지 않게 한다.
        [Fact]
        public void QueueAndTransport_WhenInspected_ExposeSendPumpShape()
        {
            Type queueType = typeof(IoUringQueue);
            Type transportType = typeof(IoUringTransport);

            Assert.NotNull(queueType.GetMethod("TrySubmitSend", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(transportType.GetMethod("SendLoopAsync", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(transportType.GetMethod("SendInFlightAsync", BindingFlags.Instance | BindingFlags.NonPublic));
        }
    }
}
