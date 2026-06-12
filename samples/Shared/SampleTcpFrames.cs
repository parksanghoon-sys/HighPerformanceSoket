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
    /// Broker protocol 은 `4바이트 big-endian 길이 + command payload` 이고, subscriber 로 나가는
    /// fan-out payload 는 현재 raw payload 이므로 수신 쪽 framing helper 는 두지 않는다.
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
    }
}
