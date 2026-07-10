using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Hps.Buffers;
using Hps.Sample.Dashboard.Models;
using Hps.Server;
using Hps.Transport;

namespace Hps.Sample.Dashboard.Services
{
    public sealed class UdpSmokeTestService
    {
        private const string Topic = "alpha";
        private const int MaxFrameBytes = 128;
        private const int ReceiveTimeoutSeconds = 5;

        public async Task<SmokeRunResult> RunAsync()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(MaxFrameBytes);
            byte[] expectedPayload = new byte[] { 21, 22, 23, 24, 25 };
            int payloadErrors = 0;

            using (SaeaTransport transport = new SaeaTransport())
            using (BrokerServer server = new BrokerServer(transport, pool, MaxFrameBytes))
            {
                Socket? subscriber = null;
                Socket? publisher = null;

                try
                {
                    await server.StartUdpAsync(new IPEndPoint(IPAddress.Loopback, 0)).ConfigureAwait(false);
                    IPEndPoint serverEndPoint = GetUdpEndPoint(server);

                    subscriber = CreateBoundUdpSocket();
                    publisher = CreateBoundUdpSocket();

                    await SendDatagramAsync(subscriber, serverEndPoint, Encoding.ASCII.GetBytes("SUBSCRIBE " + Topic)).ConfigureAwait(false);
                    await server.WaitForSubscriberCountAsync(
                        Topic,
                        1,
                        TimeSpan.FromSeconds(ReceiveTimeoutSeconds)).ConfigureAwait(false);

                    await SendDatagramAsync(publisher, serverEndPoint, CreatePublishCommand(Topic, expectedPayload)).ConfigureAwait(false);

                    byte[] actualPayload = await ReceiveDatagramPayloadAsync(subscriber, 256).ConfigureAwait(false);
                    if (!PayloadEquals(expectedPayload, actualPayload))
                        payloadErrors = 1;

                    TransportDiagnosticsSnapshot snapshot = transport.GetDiagnosticsSnapshot();

                    subscriber.Dispose();
                    subscriber = null;
                    publisher.Dispose();
                    publisher = null;
                    await server.StopAsync().ConfigureAwait(false);
                    await WaitForRentedCountAsync(pool, 0).ConfigureAwait(false);

                    bool succeeded = payloadErrors == 0
                        && snapshot.UdpDroppedPendingSendCount == 0
                        && pool.RentedCount == 0;

                    return new SmokeRunResult(
                        "UDP",
                        succeeded,
                        1,
                        payloadErrors == 0 ? 1 : 0,
                        snapshot.UdpDroppedPendingSendCount,
                        payloadErrors,
                        pool.RentedCount,
                        succeeded ? "UDP smoke 성공" : "UDP smoke 실패");
                }
                catch (Exception ex)
                {
                    return new SmokeRunResult("UDP", false, 1, 0, 0, 1, pool.RentedCount, ex.Message);
                }
                finally
                {
                    subscriber?.Dispose();
                    publisher?.Dispose();
                    await server.StopAsync().ConfigureAwait(false);
                }
            }
        }

        private static IPEndPoint GetUdpEndPoint(BrokerServer server)
        {
            IPEndPoint? endPoint = server.UdpLocalEndPoint as IPEndPoint;
            if (endPoint == null)
                throw new InvalidOperationException("BrokerServer가 UDP loopback endpoint에 bind 되지 않았다.");

            return endPoint;
        }

        private static Socket CreateBoundUdpSocket()
        {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private static byte[] CreatePublishCommand(string topic, byte[] payload)
        {
            byte[] prefix = Encoding.ASCII.GetBytes("PUBLISH " + topic + " ");
            byte[] command = new byte[prefix.Length + payload.Length];

            Buffer.BlockCopy(prefix, 0, command, 0, prefix.Length);
            Buffer.BlockCopy(payload, 0, command, prefix.Length, payload.Length);

            return command;
        }

        private static async Task SendDatagramAsync(Socket socket, EndPoint remoteEndPoint, byte[] payload)
        {
            int sent = await socket.SendToAsync(new ArraySegment<byte>(payload), SocketFlags.None, remoteEndPoint).ConfigureAwait(false);
            if (sent != payload.Length)
                throw new InvalidOperationException("UDP smoke datagram 전체를 전송하지 못했다.");
        }

        private static async Task<byte[]> ReceiveDatagramPayloadAsync(Socket socket, int maxLength)
        {
            Task<byte[]> receiveTask = ReceiveDatagramPayloadCoreAsync(socket, maxLength);
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(ReceiveTimeoutSeconds));
            Task completedTask = await Task.WhenAny(receiveTask, timeoutTask).ConfigureAwait(false);

            if (!object.ReferenceEquals(receiveTask, completedTask))
                throw new TimeoutException("UDP smoke payload 수신 시간이 초과됐다.");

            return await receiveTask.ConfigureAwait(false);
        }

        private static async Task<byte[]> ReceiveDatagramPayloadCoreAsync(Socket socket, int maxLength)
        {
            byte[] receiveBuffer = new byte[maxLength];
            EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            SocketReceiveFromResult result = await socket.ReceiveFromAsync(
                new ArraySegment<byte>(receiveBuffer),
                SocketFlags.None,
                remoteEndPoint).ConfigureAwait(false);

            byte[] payload = new byte[result.ReceivedBytes];
            Buffer.BlockCopy(receiveBuffer, 0, payload, 0, payload.Length);
            return payload;
        }

        private static async Task WaitForRentedCountAsync(PinnedBlockMemoryPool pool, int expected)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(ReceiveTimeoutSeconds);

            while (DateTime.UtcNow < deadline)
            {
                if (pool.RentedCount == expected)
                    return;

                await Task.Delay(10).ConfigureAwait(false);
            }

            throw new InvalidOperationException("UDP smoke 종료 후 pool rented count가 기대값으로 돌아오지 않았다.");
        }

        private static bool PayloadEquals(byte[] expected, byte[] actual)
        {
            if (expected.Length != actual.Length)
                return false;

            for (int index = 0; index < expected.Length; index++)
            {
                if (expected[index] != actual[index])
                    return false;
            }

            return true;
        }
    }
}
