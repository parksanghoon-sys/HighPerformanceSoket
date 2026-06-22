using System;

namespace Hps.Protocol
{
    /// <summary>
    /// TCP frame payload 를 broker command 로 해석하는 decoder 이다.
    /// </summary>
    public static class TcpCommandDecoder
    {
        private const byte Space = 0x20;

        /// <summary>
        /// TCP frame payload 를 command 로 해석한다.
        ///
        /// 정상 decode 시 반환되는 <see cref="TcpCommand"/> 는 입력 <paramref name="frame"/> 의 span view 를
        /// 그대로 가리킨다. 따라서 caller 는 이 command 를 frame buffer 수명 안에서만 사용해야 한다.
        /// </summary>
        public static bool TryDecode(ReadOnlySpan<byte> frame, out TcpCommand command, out TcpCommandDecodeError error)
        {
            command = default(TcpCommand);
            error = TcpCommandDecodeError.None;

            if (frame.Length == 0)
            {
                error = TcpCommandDecodeError.EmptyFrame;
                return false;
            }

            int commandSeparator = IndexOfSpace(frame);
            if (commandSeparator < 0)
            {
                if (IsSubscribeCommand(frame)
                    || IsUnsubscribeCommand(frame)
                    || IsRegisterCommand(frame)
                    || IsUnregisterCommand(frame)
                    || IsPublishCommand(frame))
                    error = TcpCommandDecodeError.MissingTopic;
                else
                    error = TcpCommandDecodeError.UnknownCommand;

                return false;
            }

            if (commandSeparator == 0)
            {
                error = TcpCommandDecodeError.UnknownCommand;
                return false;
            }

            ReadOnlySpan<byte> commandName = frame.Slice(0, commandSeparator);
            ReadOnlySpan<byte> commandBody = frame.Slice(commandSeparator + 1);
            int commandBodyOffset = commandSeparator + 1;

            if (IsSubscribeCommand(commandName))
                return TryDecodeTopicOnlyCommand(commandBody, TcpCommandKind.Subscribe, out command, out error);

            if (IsUnsubscribeCommand(commandName))
                return TryDecodeTopicOnlyCommand(commandBody, TcpCommandKind.Unsubscribe, out command, out error);

            if (IsRegisterCommand(commandName))
                return TryDecodeTopicOnlyCommand(commandBody, TcpCommandKind.Register, out command, out error);

            if (IsUnregisterCommand(commandName))
                return TryDecodeTopicOnlyCommand(commandBody, TcpCommandKind.Unregister, out command, out error);

            if (IsPublishCommand(commandName))
                return TryDecodePublish(commandBody, commandBodyOffset, out command, out error);

            error = TcpCommandDecodeError.UnknownCommand;
            return false;
        }

        private static bool TryDecodeTopicOnlyCommand(ReadOnlySpan<byte> commandBody, TcpCommandKind kind, out TcpCommand command, out TcpCommandDecodeError error)
        {
            command = default(TcpCommand);

            if (commandBody.Length == 0)
            {
                error = TcpCommandDecodeError.MissingTopic;
                return false;
            }

            // SUBSCRIBE/UNSUBSCRIBE 는 topic 하나만 받는다.
            // 공백을 허용하면 PUBLISH 처럼 payload 가 뒤따르는 명령과 topic token 경계가 모호해진다.
            if (IndexOfSpace(commandBody) >= 0)
            {
                error = TcpCommandDecodeError.InvalidTopic;
                return false;
            }

            command = new TcpCommand(kind, commandBody, ReadOnlySpan<byte>.Empty);
            error = TcpCommandDecodeError.None;
            return true;
        }

        private static bool TryDecodePublish(ReadOnlySpan<byte> commandBody, int commandBodyOffset, out TcpCommand command, out TcpCommandDecodeError error)
        {
            command = default(TcpCommand);

            if (commandBody.Length == 0)
            {
                error = TcpCommandDecodeError.MissingTopic;
                return false;
            }

            int payloadSeparator = IndexOfSpace(commandBody);
            if (payloadSeparator < 0)
            {
                error = TcpCommandDecodeError.MissingPayloadSeparator;
                return false;
            }

            if (payloadSeparator == 0)
            {
                error = TcpCommandDecodeError.MissingTopic;
                return false;
            }

            ReadOnlySpan<byte> topic = commandBody.Slice(0, payloadSeparator);
            ReadOnlySpan<byte> payload = commandBody.Slice(payloadSeparator + 1);
            int payloadOffset = commandBodyOffset + payloadSeparator + 1;

            command = new TcpCommand(TcpCommandKind.Publish, topic, payload, payloadOffset);
            error = TcpCommandDecodeError.None;
            return true;
        }

        private static int IndexOfSpace(ReadOnlySpan<byte> span)
        {
            for (int index = 0; index < span.Length; index++)
            {
                if (span[index] == Space)
                    return index;
            }

            return -1;
        }

        private static bool IsSubscribeCommand(ReadOnlySpan<byte> commandName)
        {
            return commandName.Length == 9
                && commandName[0] == (byte)'S'
                && commandName[1] == (byte)'U'
                && commandName[2] == (byte)'B'
                && commandName[3] == (byte)'S'
                && commandName[4] == (byte)'C'
                && commandName[5] == (byte)'R'
                && commandName[6] == (byte)'I'
                && commandName[7] == (byte)'B'
                && commandName[8] == (byte)'E';
        }

        private static bool IsPublishCommand(ReadOnlySpan<byte> commandName)
        {
            return commandName.Length == 7
                && commandName[0] == (byte)'P'
                && commandName[1] == (byte)'U'
                && commandName[2] == (byte)'B'
                && commandName[3] == (byte)'L'
                && commandName[4] == (byte)'I'
                && commandName[5] == (byte)'S'
                && commandName[6] == (byte)'H';
        }

        // command name 비교는 frame span 위에서 직접 수행한다.
        // 여기서 string 으로 변환하면 모든 수신 command 마다 관리힙 할당이 생기므로 byte 비교로 고정한다.
        private static bool IsRegisterCommand(ReadOnlySpan<byte> commandName)
        {
            return commandName.Length == 8
                && commandName[0] == (byte)'R'
                && commandName[1] == (byte)'E'
                && commandName[2] == (byte)'G'
                && commandName[3] == (byte)'I'
                && commandName[4] == (byte)'S'
                && commandName[5] == (byte)'T'
                && commandName[6] == (byte)'E'
                && commandName[7] == (byte)'R';
        }

        private static bool IsUnregisterCommand(ReadOnlySpan<byte> commandName)
        {
            return commandName.Length == 10
                && commandName[0] == (byte)'U'
                && commandName[1] == (byte)'N'
                && commandName[2] == (byte)'R'
                && commandName[3] == (byte)'E'
                && commandName[4] == (byte)'G'
                && commandName[5] == (byte)'I'
                && commandName[6] == (byte)'S'
                && commandName[7] == (byte)'T'
                && commandName[8] == (byte)'E'
                && commandName[9] == (byte)'R';
        }

        private static bool IsUnsubscribeCommand(ReadOnlySpan<byte> commandName)
        {
            return commandName.Length == 11
                && commandName[0] == (byte)'U'
                && commandName[1] == (byte)'N'
                && commandName[2] == (byte)'S'
                && commandName[3] == (byte)'U'
                && commandName[4] == (byte)'B'
                && commandName[5] == (byte)'S'
                && commandName[6] == (byte)'C'
                && commandName[7] == (byte)'R'
                && commandName[8] == (byte)'I'
                && commandName[9] == (byte)'B'
                && commandName[10] == (byte)'E';
        }
    }
}
