using System;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Hps.Sample
{
    /// <summary>
    /// 샘플 publisher/subscriber 가 broker TCP wire format 을 직접 쓰기 위한 작은 helper 이다.
    ///
    /// Broker TCP protocol 은 client->broker command 와 broker->subscriber fan-out 모두
    /// `4바이트 big-endian 길이 + payload` frame 을 사용한다. TCP stream 은 message boundary 를
    /// 보존하지 않으므로 샘플도 항상 frame 단위로 읽고 쓴다.
    /// </summary>
    internal static class SampleTcpFrames
    {
        internal static bool IsValidTopic(string topic)
        {
            if (string.IsNullOrEmpty(topic))
                return false;

            for (int index = 0; index < topic.Length; index++)
            {
                char ch = topic[index];
                if (ch <= 0x20 || ch > 0x7E)
                    return false;
            }

            return true;
        }

        internal static byte[] CreateSubscribeCommand(string topic)
        {
            return Encoding.ASCII.GetBytes("SUBSCRIBE " + topic);
        }

        internal static byte[] CreatePublishCommand(string topic, byte[] payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            byte[] prefix = Encoding.ASCII.GetBytes("PUBLISH " + topic + " ");
            byte[] command = new byte[prefix.Length + payload.Length];

            Buffer.BlockCopy(prefix, 0, command, 0, prefix.Length);
            Buffer.BlockCopy(payload, 0, command, prefix.Length, payload.Length);

            return command;
        }

        internal static async Task SendFrameAsync(Socket socket, byte[] payload)
        {
            if (socket == null)
                throw new ArgumentNullException(nameof(socket));
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            byte[] frame = new byte[4 + payload.Length];
            BinaryPrimitives.WriteInt32BigEndian(new Span<byte>(frame, 0, 4), payload.Length);
            Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);

            int offset = 0;
            while (offset < frame.Length)
            {
                int sent = await socket.SendAsync(new ArraySegment<byte>(frame, offset, frame.Length - offset), SocketFlags.None).ConfigureAwait(false);
                if (sent == 0)
                    throw new InvalidOperationException("TCP frame 전송 중 socket 이 먼저 닫혔다.");

                offset += sent;
            }
        }

        internal static async Task<byte[]?> ReceiveFrameOrNullAsync(Socket socket, int maxPayloadBytes)
        {
            if (socket == null)
                throw new ArgumentNullException(nameof(socket));
            if (maxPayloadBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));

            byte[] header = new byte[4];
            bool hasHeader = await ReceiveExactOrClosedAsync(socket, header, 0, header.Length).ConfigureAwait(false);
            if (!hasHeader)
                return null;

            int payloadLength = BinaryPrimitives.ReadInt32BigEndian(new ReadOnlySpan<byte>(header));
            if (payloadLength < 0 || payloadLength > maxPayloadBytes)
                throw new InvalidOperationException("수신 frame payload 길이가 허용 범위를 벗어났다.");

            byte[] payload = new byte[payloadLength];
            bool hasPayload = await ReceiveExactOrClosedAsync(socket, payload, 0, payload.Length).ConfigureAwait(false);
            if (!hasPayload)
                throw new InvalidOperationException("TCP frame payload 수신 중 socket 이 먼저 닫혔다.");

            return payload;
        }

        private static async Task<bool> ReceiveExactOrClosedAsync(Socket socket, byte[] buffer, int offset, int length)
        {
            int cursor = offset;
            int remaining = length;

            while (remaining != 0)
            {
                int received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, cursor, remaining), SocketFlags.None).ConfigureAwait(false);
                if (received == 0)
                {
                    if (cursor == offset)
                        return false;

                    throw new InvalidOperationException("TCP frame 수신 중 socket 이 먼저 닫혔다.");
                }

                cursor += received;
                remaining -= received;
            }

            return true;
        }
    }
}
