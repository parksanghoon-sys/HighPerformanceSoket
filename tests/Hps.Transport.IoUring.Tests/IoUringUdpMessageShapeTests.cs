using System;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringUdpMessageShapeTests
    {
        // UDP는 remote endpoint 를 주고받아야 하므로 TCP RECV/SEND opcode 만으로는 부족하다.
        // 이 테스트는 recvmsg/sendmsg SQE shape, Linux msghdr layout, IPv4 sockaddr helper 를 먼저 고정한다.
        [Fact]
        public void NativeSubmissionTypes_WhenInspected_ExposeUdpMessageShape()
        {
            Type nativeType = RequiredType("Hps.Transport.IoUringNative, Hps.Transport.IoUring");
            Type messageHeaderType = RequiredType("Hps.Transport.IoUringMessageHeader, Hps.Transport.IoUring");
            Type queueType = RequiredType("Hps.Transport.IoUringQueue, Hps.Transport.IoUring");
            Type sockaddrType = RequiredType("Hps.Transport.IoUringSockaddr, Hps.Transport.IoUring");

            Assert.NotNull(nativeType.GetField("OperationReceiveMessage", BindingFlags.Static | BindingFlags.NonPublic));
            Assert.NotNull(nativeType.GetField("OperationSendMessage", BindingFlags.Static | BindingFlags.NonPublic));
            Assert.True(Marshal.SizeOf(messageHeaderType) >= 56);
            Assert.NotNull(queueType.GetMethod("TrySubmitReceiveMessage", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(queueType.GetMethod("TrySubmitSendMessage", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(sockaddrType.GetMethod("EncodeIPv4", BindingFlags.Static | BindingFlags.NonPublic));
            Assert.NotNull(sockaddrType.GetMethod("DecodeIPv4", BindingFlags.Static | BindingFlags.NonPublic));
        }

        // native sockaddr_in 은 family 는 host byte order, port/address 는 network byte order 이므로
        // encode/decode roundtrip 으로 byte ordering drift 를 잡는다.
        [Fact]
        public void Sockaddr_WhenIpv4EndpointEncoded_DecodesSameEndpoint()
        {
            Type sockaddrType = RequiredType("Hps.Transport.IoUringSockaddr, Hps.Transport.IoUring");
            MethodInfo encode = RequiredMethod(sockaddrType, "EncodeIPv4");
            MethodInfo decode = RequiredMethod(sockaddrType, "DecodeIPv4");
            byte[] block = new byte[32];
            IPEndPoint expected = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 4567);

            encode.Invoke(null, new object[] { expected, block });
            object actual = decode.Invoke(null, new object[] { block, 16 })!;

            Assert.Equal(expected, Assert.IsType<IPEndPoint>(actual));
        }

        // UDP send message metadata 테스트: sendmsg 는 payload pointer 와 remote sockaddr pointer 를 completion 까지 참조한다.
        // PrepareSend 가 header pointer 를 노출하고 sockaddr 를 roundtrip 할 수 있어야 커널 제출 직후 managed metadata lifetime 이 흔들리지 않는다.
        [Fact]
        public unsafe void MessageBuffer_WhenPreparedForSend_ExposesHeaderAndPreservesRemoteEndpoint()
        {
            using (IoUringUdpMessageBuffer messageBuffer = new IoUringUdpMessageBuffer())
            {
                byte[] payload = new byte[] { 10, 20, 30, 40 };
                IPEndPoint expected = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 34567);

                messageBuffer.PrepareSend(payload, 1, 2, expected);

                Assert.NotEqual(IntPtr.Zero, messageBuffer.MessageHeaderPointer);
                Assert.Equal(expected, messageBuffer.DecodeRemoteEndPoint());
            }
        }

        // Dispose 경계 테스트: native SQE 에 넘겨질 pointer holder 는 해제 후 재사용되면 안 된다.
        // ObjectDisposedException 으로 빠르게 실패해야 이미 반환된 sockaddr block 이 다음 receive/send 에 섞이는 일을 막을 수 있다.
        [Fact]
        public unsafe void MessageBuffer_WhenDisposed_RejectsFurtherUse()
        {
            IoUringUdpMessageBuffer messageBuffer = new IoUringUdpMessageBuffer();
            messageBuffer.Dispose();

            Assert.Throws<ObjectDisposedException>(
                delegate
                {
                    messageBuffer.PrepareReceive(new byte[8], 0, 8);
                });
            Assert.Throws<ObjectDisposedException>(
                delegate
                {
                    IntPtr ignored = messageBuffer.MessageHeaderPointer;
                    GC.KeepAlive(ignored);
                });
        }

        private static Type RequiredType(string name)
        {
            Type? type = Type.GetType(name);
            Assert.NotNull(type);
            return type!;
        }

        private static MethodInfo RequiredMethod(Type type, string name)
        {
            MethodInfo? method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return method!;
        }
    }
}
