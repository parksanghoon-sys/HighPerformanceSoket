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
