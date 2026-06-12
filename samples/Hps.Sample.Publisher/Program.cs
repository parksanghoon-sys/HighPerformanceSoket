using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Hps.Sample.Publisher
{
    internal static class Program
    {
        private const int SuccessExitCode = 0;
        private const int InvalidArgumentsExitCode = 2;

        public static async Task<int> Main(string[] args)
        {
            if (args.Length != 4)
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

            byte[] message = Encoding.UTF8.GetBytes(args[3]);
            byte[] command = SampleTcpFrames.CreatePublishCommand(topic, message);

            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                socket.NoDelay = true;
                await socket.ConnectAsync(host, port).ConfigureAwait(false);
                await SampleTcpFrames.SendFrameAsync(socket, command).ConfigureAwait(false);
            }

            Console.WriteLine("publish 완료: topic={0}, bytes={1}", topic, message.Length);
            return SuccessExitCode;
        }

        private static bool TryParsePort(string value, out int port)
        {
            if (!int.TryParse(value, out port))
                return false;

            return port > 0 && port <= 65535;
        }

        private static void PrintUsage()
        {
            Console.Error.WriteLine("사용법: Hps.Sample.Publisher <host> <port> <topic> <message>");
        }
    }
}
