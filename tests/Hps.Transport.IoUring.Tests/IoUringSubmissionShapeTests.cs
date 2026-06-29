using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringSubmissionShapeTests
    {
        // TCP pump 는 SQE opcode 와 CQE result layout 을 직접 사용한다.
        // ABI shape 가 없으면 transport 구현이 raw pointer 상수를 흩뿌리게 되므로 native adapter 경계에 먼저 고정한다.
        [Fact]
        public void NativeSubmissionTypes_WhenInspected_ExposeTcpSendReceiveShape()
        {
            Type? sqeType = Type.GetType("Hps.Transport.IoUringSubmissionQueueEntry, Hps.Transport.IoUring");
            Type? cqeType = Type.GetType("Hps.Transport.IoUringCompletionQueueEntry, Hps.Transport.IoUring");
            Type? nativeType = Type.GetType("Hps.Transport.IoUringNative, Hps.Transport.IoUring");

            Assert.NotNull(sqeType);
            Assert.NotNull(cqeType);
            Assert.NotNull(nativeType);
            Assert.True(Marshal.SizeOf(sqeType!) >= 64);
            Assert.Equal(16, Marshal.SizeOf(cqeType!));
            Assert.NotNull(nativeType!.GetField("OperationReceive", BindingFlags.Static | BindingFlags.NonPublic));
            Assert.NotNull(nativeType.GetField("OperationSend", BindingFlags.Static | BindingFlags.NonPublic));
            Assert.NotNull(nativeType.GetMethod("Enter", BindingFlags.Static | BindingFlags.NonPublic));
        }
    }
}
