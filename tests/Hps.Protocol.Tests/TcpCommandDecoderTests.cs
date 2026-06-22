using System;
using System.Reflection;
using System.Text;
using Xunit;

namespace Hps.Protocol.Tests
{
    public sealed class TcpCommandDecoderTests
    {
        // Broker command handler 는 `PUBLISH <topic> <payload>` frame 전체 RefCountedBuffer 에서 payload slice 만 fan-out 해야 한다.
        // decoder 가 payload offset 을 제공하지 않으면 Broker 가 command 문법을 다시 계산해야 하므로 Protocol 계약으로 고정한다.
        [Fact]
        public void TcpCommand_Contract_ExposesPayloadOffset()
        {
            PropertyInfo? payloadOffset = typeof(TcpCommand).GetProperty("PayloadOffset");

            Assert.NotNull(payloadOffset);
            Assert.Equal(typeof(int), payloadOffset!.PropertyType);
        }

        // UNSUBSCRIBE 는 endpoint 가 더 이상 특정 topic 을 받지 않겠다는 제어 명령이다.
        // 먼저 enum 계약을 고정해 decoder/handler 구현이 기존 SUBSCRIBE/PUBLISH 분기만으로 남지 않게 한다.
        [Fact]
        public void TcpCommandKind_Contract_ExposesUnsubscribeCommand()
        {
            Assert.Contains("Unsubscribe", Enum.GetNames(typeof(TcpCommandKind)));
        }

        // REGISTER/UNREGISTER command 계약 테스트는 stable subscriber identity가 protocol command로 구분되는지 먼저 고정한다.
        // enum 이름이 없으면 handler가 UNKNOWN command 경로를 타므로, 구현 전에는 assertion failure로 Red가 나야 한다.
        [Fact]
        public void TcpCommandKind_Contract_ExposesRegisterCommands()
        {
            Assert.Contains("Register", Enum.GetNames(typeof(TcpCommandKind)));
            Assert.Contains("Unregister", Enum.GetNames(typeof(TcpCommandKind)));
        }

        // SUBSCRIBE decode 테스트: frame payload 는 ASCII command 와 topic token 만 포함한다.
        // topic 은 frame buffer 안의 span view 로 반환되어 이후 라우팅 계층이 필요한 방식으로 해석할 수 있어야 한다.
        [Fact]
        public void TryDecode_WhenSubscribeFrameContainsTopic_ReturnsSubscribeCommand()
        {
            TcpCommand command;
            TcpCommandDecodeError error;

            bool decoded = TcpCommandDecoder.TryDecode(Ascii("SUBSCRIBE alpha"), out command, out error);

            Assert.True(decoded);
            Assert.Equal(TcpCommandDecodeError.None, error);
            Assert.Equal(TcpCommandKind.Subscribe, command.Kind);
            Assert.Equal("alpha", AsString(command.Topic));
            Assert.True(command.Payload.IsEmpty);
        }

        // UNSUBSCRIBE decode 테스트: SUBSCRIBE 와 같은 topic-only 문법을 쓰지만 broker handler 에서는 제거 경로로 분기해야 한다.
        // payload 가 비어 있음을 함께 확인해 fan-out payload command 로 잘못 해석되는 회귀를 막는다.
        [Fact]
        public void TryDecode_WhenUnsubscribeFrameContainsTopic_ReturnsUnsubscribeCommand()
        {
            TcpCommand command;
            TcpCommandDecodeError error;

            bool decoded = TcpCommandDecoder.TryDecode(Ascii("UNSUBSCRIBE alpha"), out command, out error);

            Assert.True(decoded);
            Assert.Equal(TcpCommandDecodeError.None, error);
            Assert.Equal(TcpCommandKind.Unsubscribe, command.Kind);
            Assert.Equal("alpha", AsString(command.Topic));
            Assert.True(command.Payload.IsEmpty);
            Assert.Equal(0, command.PayloadOffset);
        }

        // REGISTER decode 테스트는 subscriber identity를 frame buffer 안의 단일 token span으로 반환해야 한다.
        // Broker 계층이 필요할 때만 string으로 복사하고 hot decode 경로는 추가 할당을 만들지 않도록 기존 topic view를 재사용한다.
        [Fact]
        public void TryDecode_WhenRegisterFrameContainsIdentity_ReturnsRegisterCommand()
        {
            TcpCommand command;
            TcpCommandDecodeError error;

            bool decoded = TcpCommandDecoder.TryDecode(Ascii("REGISTER device-a"), out command, out error);

            Assert.True(decoded);
            Assert.Equal(TcpCommandDecodeError.None, error);
            Assert.Equal(TcpCommandKind.Register, command.Kind);
            Assert.Equal("device-a", AsString(command.Topic));
            Assert.True(command.Payload.IsEmpty);
        }

        // UNREGISTER decode 테스트는 identity registry에서 명시 제거할 id도 REGISTER와 같은 token-only 문법을 사용함을 고정한다.
        // payload가 생기면 publish command와 경계가 흐려지므로 단일 token만 허용한다.
        [Fact]
        public void TryDecode_WhenUnregisterFrameContainsIdentity_ReturnsUnregisterCommand()
        {
            TcpCommand command;
            TcpCommandDecodeError error;

            bool decoded = TcpCommandDecoder.TryDecode(Ascii("UNREGISTER device-a"), out command, out error);

            Assert.True(decoded);
            Assert.Equal(TcpCommandDecodeError.None, error);
            Assert.Equal(TcpCommandKind.Unregister, command.Kind);
            Assert.Equal("device-a", AsString(command.Topic));
            Assert.True(command.Payload.IsEmpty);
        }

        // PUBLISH decode 테스트는 payload 를 topic 뒤 공백 다음의 나머지 전체 byte 로 둔다.
        // payload 안의 공백이나 임의 byte 를 잘라내면 publish 데이터가 손상되므로 topic token 뒤는 그대로 보존한다.
        [Fact]
        public void TryDecode_WhenPublishFrameContainsPayload_ReturnsPublishCommandWithRemainingPayload()
        {
            TcpCommand command;
            TcpCommandDecodeError error;
            byte[] frame = new byte[]
            {
                (byte)'P', (byte)'U', (byte)'B', (byte)'L', (byte)'I', (byte)'S', (byte)'H', (byte)' ',
                (byte)'a', (byte)'l', (byte)'p', (byte)'h', (byte)'a', (byte)' ',
                1, 2, (byte)' ', 3
            };

            bool decoded = TcpCommandDecoder.TryDecode(frame, out command, out error);

            Assert.True(decoded);
            Assert.Equal(TcpCommandDecodeError.None, error);
            Assert.Equal(TcpCommandKind.Publish, command.Kind);
            Assert.Equal("alpha", AsString(command.Topic));
            Assert.Equal(new byte[] { 1, 2, (byte)' ', 3 }, command.Payload.ToArray());
            Assert.Equal(14, command.PayloadOffset);
        }

        // 빈 publish payload 경계 테스트: `PUBLISH <topic> `처럼 두 번째 구분자 뒤에 byte 가 없어도
        // 0바이트 payload publish 는 유효해야 RefCountedBuffer.Length=0 frame 과 일관된다.
        [Fact]
        public void TryDecode_WhenPublishPayloadIsEmpty_ReturnsPublishCommandWithEmptyPayload()
        {
            TcpCommand command;
            TcpCommandDecodeError error;

            bool decoded = TcpCommandDecoder.TryDecode(Ascii("PUBLISH alpha "), out command, out error);

            Assert.True(decoded);
            Assert.Equal(TcpCommandDecodeError.None, error);
            Assert.Equal(TcpCommandKind.Publish, command.Kind);
            Assert.Equal("alpha", AsString(command.Topic));
            Assert.True(command.Payload.IsEmpty);
            Assert.Equal(14, command.PayloadOffset);
        }

        // malformed command 테스트: 정상 흐름 제어에 예외를 쓰지 않고 false+error 로 반환해야
        // 상위 계층이 connection close, protocol error 응답 같은 정책을 선택할 수 있다.
        [Theory]
        [InlineData("", TcpCommandDecodeError.EmptyFrame)]
        [InlineData("UNKNOWN alpha", TcpCommandDecodeError.UnknownCommand)]
        [InlineData("SUBSCRIBE ", TcpCommandDecodeError.MissingTopic)]
        [InlineData("SUBSCRIBE alpha beta", TcpCommandDecodeError.InvalidTopic)]
        [InlineData("UNSUBSCRIBE", TcpCommandDecodeError.MissingTopic)]
        [InlineData("UNSUBSCRIBE ", TcpCommandDecodeError.MissingTopic)]
        [InlineData("UNSUBSCRIBE alpha beta", TcpCommandDecodeError.InvalidTopic)]
        [InlineData("REGISTER", TcpCommandDecodeError.MissingTopic)]
        [InlineData("REGISTER ", TcpCommandDecodeError.MissingTopic)]
        [InlineData("REGISTER device-a extra", TcpCommandDecodeError.InvalidTopic)]
        [InlineData("UNREGISTER", TcpCommandDecodeError.MissingTopic)]
        [InlineData("UNREGISTER ", TcpCommandDecodeError.MissingTopic)]
        [InlineData("UNREGISTER device-a extra", TcpCommandDecodeError.InvalidTopic)]
        [InlineData("PUBLISH ", TcpCommandDecodeError.MissingTopic)]
        [InlineData("PUBLISH alpha", TcpCommandDecodeError.MissingPayloadSeparator)]
        public void TryDecode_WhenFrameIsMalformed_ReturnsFalseWithError(string frameText, TcpCommandDecodeError expectedError)
        {
            TcpCommand command;
            TcpCommandDecodeError error;

            bool decoded = TcpCommandDecoder.TryDecode(Ascii(frameText), out command, out error);

            Assert.False(decoded);
            Assert.Equal(expectedError, error);
        }

        private static byte[] Ascii(string text)
        {
            return Encoding.ASCII.GetBytes(text);
        }

        private static string AsString(ReadOnlySpan<byte> span)
        {
            return Encoding.ASCII.GetString(span.ToArray());
        }
    }
}
