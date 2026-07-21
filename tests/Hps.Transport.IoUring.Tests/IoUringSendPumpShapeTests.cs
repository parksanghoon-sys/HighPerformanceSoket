using System;
using System.Reflection;
using System.Runtime.CompilerServices;
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

        // recv SQE도 connection close 뒤 CQE를 받은 다음 pinned block과 context를 반환해야 한다.
        // StopAsync가 receive pump를 기다리지 않으면 completion loop/queue가 먼저 닫혀 resource cleanup이 영원히 끝나지 않는다.
        [Fact]
        public async Task StopAsync_WhenTcpReceivePumpTaskIsTracked_WaitsForTaskCompletion()
        {
            using (IoUringTransport transport = new IoUringTransport())
            {
                TaskCompletionSource<bool> receivePumpCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                TrackConnectionReceivePumpTask(transport, receivePumpCompletion.Task);

                Task stopTask = transport.StopAsync().AsTask();
                Assert.False(stopTask.IsCompleted);

                receivePumpCompletion.SetResult(true);
                await stopTask;
            }
        }

        // io_uring pending request는 socket close만으로 취소되지 않으므로 resource Dispose가 cancel SQE를 먼저 제출해야 한다.
        // helper만 존재하고 shutdown path에서 호출하지 않는 불완전 구현을 IL call 검증으로 막는다.
        [Fact]
        public void TcpConnectionResourceDispose_WhenInspected_CallsPendingOperationCancelHelper()
        {
            MethodInfo? disposeMethod = typeof(IoUringTcpConnectionResource).GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.Public);
            MethodInfo? cancelMethod = typeof(IoUringTcpConnectionResource).GetMethod(
                "CancelPendingOperations",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(disposeMethod);
            Assert.NotNull(cancelMethod);
            Assert.True(
                ContainsCall(disposeMethod!, cancelMethod!),
                "IoUringTcpConnectionResource.Dispose가 pending operation cancel helper를 호출해야 합니다.");
        }

        // D210처럼 production path를 바로 fixed write로 바꾸지 않고,
        // registered buffer lookup 기반 WRITE_FIXED helper shape만 먼저 고정한다.
        [Fact]
        public void SendPump_WhenInspected_ExposesOptInFixedRegisteredPayloadHelper()
        {
            Type transportType = typeof(IoUringTransport);

            MethodInfo? helper = transportType.GetMethod(
                "SendFixedRegisteredPayloadAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(helper);
        }

        // fixed payload helper 연결 테스트: helper shape 만 있고 SendInFlightAsync 가 호출하지 않으면
        // registered payload pool hit 이 production send path 에 반영되지 않는다.
        [Fact]
        public void SendInFlightAsync_WhenInspected_CallsFixedRegisteredPayloadHelperBeforeBaselinePayloadSend()
        {
            MethodInfo? sendMethod = typeof(IoUringTransport).GetMethod(
                "SendInFlightAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo? helperMethod = typeof(IoUringTransport).GetMethod(
                "SendFixedRegisteredPayloadAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(sendMethod);
            Assert.NotNull(helperMethod);

            Assert.True(ContainsCall(sendMethod!, helperMethod!), "SendInFlightAsync 가 fixed registered payload helper 를 호출해야 합니다.");
        }

        private static bool ContainsCall(MethodInfo caller, MethodInfo callee)
        {
            MethodInfo inspectedCaller = ResolveAsyncMoveNext(caller);
            MethodBody? body = inspectedCaller.GetMethodBody();
            Assert.NotNull(body);

            byte[]? il = body!.GetILAsByteArray();
            Assert.NotNull(il);

            int expectedToken = callee.MetadataToken;
            for (int index = 0; index <= il!.Length - 5; index++)
            {
                byte opCode = il[index];
                if (opCode != 0x28 && opCode != 0x6F)
                    continue;

                int token = BitConverter.ToInt32(il, index + 1);
                if (token == expectedToken)
                    return true;
            }

            return false;
        }

        private static MethodInfo ResolveAsyncMoveNext(MethodInfo method)
        {
            AsyncStateMachineAttribute? attribute = method.GetCustomAttribute<AsyncStateMachineAttribute>();
            if (attribute == null)
                return method;

            MethodInfo? moveNext = attribute.StateMachineType.GetMethod(
                "MoveNext",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(moveNext);
            return moveNext!;
        }

        private static void TrackConnectionSendPumpTask(IoUringTransport transport, Task task)
        {
            MethodInfo? method = typeof(IoUringTransport).GetMethod(
                "TrackConnectionSendPumpTask",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            method!.Invoke(transport, new object[] { task });
        }

        private static void TrackConnectionReceivePumpTask(IoUringTransport transport, Task task)
        {
            MethodInfo? method = typeof(IoUringTransport).GetMethod(
                "TrackConnectionReceivePumpTask",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            method!.Invoke(transport, new object[] { task });
        }
    }
}
