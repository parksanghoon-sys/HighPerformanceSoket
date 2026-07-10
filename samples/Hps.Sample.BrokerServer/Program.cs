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
        private const int RuntimeFailureExitCode = 1;
        private const int InvalidArgumentsExitCode = 2;

        public static async Task<int> Main(string[] args)
        {
            SampleBrokerServerCommandLine? commandLine;
            string? parseError;
            if (!SampleBrokerServerCommandParser.TryParse(args, out commandLine, out parseError))
            {
                if (parseError != null)
                    Console.Error.WriteLine(parseError);

                PrintUsage();
                return InvalidArgumentsExitCode;
            }

            SampleBrokerServerCommandLine parsedCommandLine = commandLine!;

            IPAddress address;
            if (!TryParseAddress(parsedCommandLine.Host, out address))
            {
                Console.Error.WriteLine("host 는 IP 주소, localhost, loopback, any 또는 * 이어야 합니다.");
                return InvalidArgumentsExitCode;
            }

            SampleTransportSelection selection = SampleTransportSelector.Select(
                parsedCommandLine.TransportMode,
                address.AddressFamily,
                RioCapabilityProbe.GetStatus,
                IoUringCapabilityProbe.GetStatus,
                delegate { return new SaeaTransport(); },
                delegate { return new RioTransport(); },
                delegate { return new IoUringTransport(); });

            if (!selection.Succeeded)
            {
                Console.Error.WriteLine(selection.ErrorMessage);
                return selection.ExitCode == 0 ? RuntimeFailureExitCode : selection.ExitCode;
            }

            if (selection.NoticeMessage != null)
                Console.Error.WriteLine(selection.NoticeMessage);

            using (ITransport transport = selection.Transport!)
            {
                PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(parsedCommandLine.MaxFrameBytes);
                using (BrokerHost server = new BrokerHost(transport, pool, parsedCommandLine.MaxFrameBytes))
                {
                    IPEndPoint listenEndPoint = new IPEndPoint(address, parsedCommandLine.Port);
                    await server.StartTcpAsync(listenEndPoint).ConfigureAwait(false);

                    Console.WriteLine(
                        "broker 시작: endpoint={0}, max-frame-bytes={1}, transport={2}",
                        server.LocalEndPoint,
                        parsedCommandLine.MaxFrameBytes,
                        selection.SelectedBackendName);
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
                // sample broker 는 Ctrl+C 때도 BrokerServer.StopAsync 경로를 지나야 listener, accept loop,
                // transport 자원이 정상 정리되는 경로를 수동으로 확인할 수 있다.
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

        private static void PrintUsage()
        {
            Console.Error.WriteLine("사용법: Hps.Sample.BrokerServer <host> <port> <max-frame-bytes> [--transport <saea|rio|iouring|auto>]");
            Console.Error.WriteLine("예시: Hps.Sample.BrokerServer 127.0.0.1 5000 65536");
            Console.Error.WriteLine("예시: Hps.Sample.BrokerServer 127.0.0.1 5000 65536 --transport auto");
            Console.Error.WriteLine("예시: Hps.Sample.BrokerServer 127.0.0.1 5000 65536 --transport iouring");
        }
    }
}
