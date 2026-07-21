using System;
using System.Reflection;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringReceivePumpShapeTests
    {
        // TCP receive pump는 queue에 RECV SQE를 submit하고 CQE를 completion loop가 drain하는 형태여야 한다.
        // Linux loopback을 실행할 수 없는 Windows 환경에서도 이 low-level entry point 부재를 Red로 확인한다.
        [Fact]
        public void QueueAndTransport_WhenInspected_ExposeReceivePumpShape()
        {
            Type queueType = typeof(IoUringQueue);
            Type transportType = typeof(IoUringTransport);

            Assert.NotNull(queueType.GetMethod("TrySubmitReceive", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(queueType.GetMethod("TryDequeueCompletion", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(transportType.GetMethod("ReceiveLoopAsync", BindingFlags.Instance | BindingFlags.NonPublic));
        }

        // close(fd)는 io_uring이 보유한 pending request reference를 취소하지 않는다.
        // resource shutdown이 recv/send user_data token을 명시적으로 취소할 수 있도록 queue helper shape를 고정한다.
        [Fact]
        public void Queue_WhenInspected_ExposesTokenBasedAsyncCancelHelper()
        {
            MethodInfo? cancelMethod = typeof(IoUringQueue).GetMethod(
                "TrySubmitCancel",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(cancelMethod);
        }
    }
}
