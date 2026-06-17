using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Hps.Sample.Subscriber
{
    internal static class Program
    {
        private const int SuccessExitCode = 0;
        private const int InvalidArgumentsExitCode = 2;
        private const int MaxPayloadBytes = 1024 * 1024;

        public static async Task<int> Main(string[] args)
        {
            if (args.Length != 3)
            {
                PrintUsage();
                return InvalidArgumentsExitCode;
            }

            string host = args[0];
            int port;
            if (!TryParsePort(args[1], out port))
            {
                Console.Error.WriteLine("port 는 1~65535 범위의 숫자여야 한다.");
                return InvalidArgumentsExitCode;
            }

            string topic = args[2];
            if (!SampleTcpFrames.IsValidTopic(topic))
            {
                Console.Error.WriteLine("topic 은 공백 없는 printable ASCII 문자열이어야 한다.");
                return InvalidArgumentsExitCode;
            }

            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                socket.NoDelay = true;
                await socket.ConnectAsync(host, port).ConfigureAwait(false);

                byte[] command = SampleTcpFrames.CreateSubscribeCommand(topic);
                await SampleTcpFrames.SendFrameAsync(socket, command).ConfigureAwait(false);

                Console.WriteLine("subscribe 시작: topic={0}", topic);
                await PrintReceivedPayloadsAsync(socket).ConfigureAwait(false);
            }

            return SuccessExitCode;
        }

        private static async Task PrintReceivedPayloadsAsync(Socket socket)
        {
            while (true)
            {
                byte[]? payload = await SampleTcpFrames.ReceiveFrameOrNullAsync(socket, MaxPayloadBytes).ConfigureAwait(false);
                if (payload == null)
                    return;

                Console.WriteLine(Encoding.UTF8.GetString(payload, 0, payload.Length));
            }
        }

        private static bool TryParsePort(string value, out int port)
        {
            if (!int.TryParse(value, out port))
                return false;

            return port > 0 && port <= 65535;
        }

        private static void PrintUsage()
        {
            Console.Error.WriteLine("사용법: Hps.Sample.Subscriber <host> <port> <topic>");
        }
    }
}
