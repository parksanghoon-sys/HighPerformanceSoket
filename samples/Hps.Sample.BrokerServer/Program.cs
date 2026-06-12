using System;
using System.Net;
using System.Threading.Tasks;
using Hps.Buffers;
using Hps.Transport;
using BrokerHost = Hps.Server.BrokerServer;

namespace Hps.Sample.BrokerServer
{
    internal static class Program
    {
        private const int SuccessExitCode = 0;
        private const int InvalidArgumentsExitCode = 2;

        public static async Task<int> Main(string[] args)
        {
            if (args.Length != 3)
            {
                PrintUsage();
                return InvalidArgumentsExitCode;
            }

            IPAddress address;
            if (!TryParseAddress(args[0], out address))
            {
                Console.Error.WriteLine("host 는 IP 주소, localhost, loopback, any 또는 * 이어야 한다.");
                return InvalidArgumentsExitCode;
            }

            int port;
            if (!TryParsePort(args[1], out port))
            {
                Console.Error.WriteLine("port 는 1~65535 범위의 숫자여야 한다.");
                return InvalidArgumentsExitCode;
            }

            int maxFrameBytes;
            if (!TryParsePositiveInt(args[2], out maxFrameBytes))
            {
                Console.Error.WriteLine("max-frame-bytes 는 1 이상의 숫자여야 한다.");
                return InvalidArgumentsExitCode;
            }

            using (ITransport transport = TransportFactory.CreateDefault())
            {
                PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(maxFrameBytes);
                using (BrokerHost server = new BrokerHost(transport, pool, maxFrameBytes))
                {
                    IPEndPoint listenEndPoint = new IPEndPoint(address, port);
                    await server.StartTcpAsync(listenEndPoint).ConfigureAwait(false);

                    Console.WriteLine("broker 시작: endpoint={0}, max-frame-bytes={1}", server.LocalEndPoint, maxFrameBytes);
                    Console.WriteLine("종료하려면 Ctrl+C 를 누르십시오.");

                    await WaitForCtrlCAsync().ConfigureAwait(false);
                    await server.StopAsync().ConfigureAwait(false);
                }
            }

            Console.WriteLine("broker 종료");
            return SuccessExitCode;
        }

        private static Task WaitForCtrlCAsync()
        {
            TaskCompletionSource<bool> stopSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ConsoleCancelEventHandler? handler = null;

            handler = delegate(object? sender, ConsoleCancelEventArgs eventArgs)
            {
                // 콘솔 샘플도 BrokerServer.StopAsync 경로를 통과해야 listener, accept loop, transport 자원을 정리한다.
                // 기본 Ctrl+C 동작을 그대로 두면 프로세스가 즉시 종료되어 수명 정리 경로를 수동 검증할 수 없다.
                eventArgs.Cancel = true;
                stopSignal.TrySetResult(true);
            };

            Console.CancelKeyPress += handler;
            return WaitForStopSignalAsync(stopSignal.Task, handler);
        }

        private static async Task WaitForStopSignalAsync(Task stopSignal, ConsoleCancelEventHandler handler)
        {
            try
            {
                await stopSignal.ConfigureAwait(false);
            }
            finally
            {
                Console.CancelKeyPress -= handler;
            }
        }

        private static bool TryParseAddress(string value, out IPAddress address)
        {
            if (string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "loopback", StringComparison.OrdinalIgnoreCase))
            {
                address = IPAddress.Loopback;
                return true;
            }

            if (string.Equals(value, "any", StringComparison.OrdinalIgnoreCase) || value == "*")
            {
                address = IPAddress.Any;
                return true;
            }

            return IPAddress.TryParse(value, out address!);
        }

        private static bool TryParsePort(string value, out int port)
        {
            if (!int.TryParse(value, out port))
                return false;

            return port > 0 && port <= 65535;
        }

        private static bool TryParsePositiveInt(string value, out int parsed)
        {
            if (!int.TryParse(value, out parsed))
                return false;

            return parsed > 0;
        }

        private static void PrintUsage()
        {
            Console.Error.WriteLine("사용법: Hps.Sample.BrokerServer <host> <port> <max-frame-bytes>");
            Console.Error.WriteLine("예시: Hps.Sample.BrokerServer 127.0.0.1 5000 65536");
        }
    }
}
