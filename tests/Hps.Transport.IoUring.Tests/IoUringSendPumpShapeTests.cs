using System;
using System.Reflection;
using System.Threading.Tasks;
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

        // remote Linux gate 에서 발견된 shutdown race 회귀 테스트:
        // connection.Close()가 먼저 unregister 된 connection 은 StopCore 의 open-connection snapshot 에 없을 수 있다.
        // 그래서 transport 는 자신이 시작한 TCP send pump task 를 별도로 추적하고 StopAsync 에서 완료를 기다려야 한다.
        [Fact]
        public async Task StopAsync_WhenTcpSendPumpTaskIsTracked_WaitsForTaskCompletion()
        {
            using (IoUringTransport transport = new IoUringTransport())
            {
                TaskCompletionSource<bool> sendPumpCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                TrackConnectionSendPumpTask(transport, sendPumpCompletion.Task);

                Task stopTask = transport.StopAsync().AsTask();
                Assert.False(stopTask.IsCompleted);

                sendPumpCompletion.SetResult(true);
                await stopTask;
            }
        }

        private static void TrackConnectionSendPumpTask(IoUringTransport transport, Task task)
        {
            MethodInfo? method = typeof(IoUringTransport).GetMethod(
                "TrackConnectionSendPumpTask",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            method!.Invoke(transport, new object[] { task });
        }
    }
}
