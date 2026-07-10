using System;
using System.Buffers.Binary;
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
    public sealed class TcpSmokeTestService
    {
        private const string Topic = "alpha";
        private const int MaxFrameBytes = 65536;
        private const int ReceiveTimeoutSeconds = 5;

        public async Task<SmokeRunResult> RunAsync()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(MaxFrameBytes);
            byte[] expectedPayload = new byte[] { 11, 12, 13, 14, 15 };
            int payloadErrors = 0;

            using (SaeaTransport transport = new SaeaTransport())
            using (BrokerServer server = new BrokerServer(transport, pool, MaxFrameBytes))
            {
                Socket? subscriber = null;
                Socket? publisher = null;

                try
                {
                    await server.StartTcpAsync(new IPEndPoint(IPAddress.Loopback, 0)).ConfigureAwait(false);
                    IPEndPoint serverEndPoint = GetTcpEndPoint(server);

                    subscriber = CreateConnectedTcpClient(serverEndPoint);
                    publisher = CreateConnectedTcpClient(serverEndPoint);

                    await SendFrameAsync(subscriber, Encoding.ASCII.GetBytes("SUBSCRIBE " + Topic)).ConfigureAwait(false);
                    await server.WaitForSubscriberCountAsync(
                        Topic,
                        1,
                        TimeSpan.FromSeconds(ReceiveTimeoutSeconds)).ConfigureAwait(false);

                    await SendFrameAsync(publisher, CreatePublishCommand(Topic, expectedPayload)).ConfigureAwait(false);

                    byte[] actualPayload = await ReceiveFrameAsync(subscriber).ConfigureAwait(false);
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
                        && snapshot.TcpDroppedPendingSendCount == 0
                        && pool.RentedCount == 0;

                    return new SmokeRunResult(
                        "TCP",
                        succeeded,
                        1,
                        payloadErrors == 0 ? 1 : 0,
                        snapshot.TcpDroppedPendingSendCount,
                        payloadErrors,
                        pool.RentedCount,
                        succeeded ? "TCP smoke 성공" : "TCP smoke 실패");
                }
                catch (Exception ex)
                {
                    return new SmokeRunResult("TCP", false, 1, 0, 0, 1, pool.RentedCount, ex.Message);
                }
                finally
                {
                    subscriber?.Dispose();
                    publisher?.Dispose();
                    await server.StopAsync().ConfigureAwait(false);
                }
            }
        }

        private static IPEndPoint GetTcpEndPoint(BrokerServer server)
        {
            IPEndPoint? endPoint = server.LocalEndPoint as IPEndPoint;
            if (endPoint == null)
                throw new InvalidOperationException("BrokerServer가 TCP loopback endpoint에 bind 되지 않았다.");

            return endPoint;
        }

        private static Socket CreateConnectedTcpClient(IPEndPoint remoteEndPoint)
        {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                socket.NoDelay = true;
                socket.Connect(remoteEndPoint);
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

        private static async Task SendFrameAsync(Socket socket, byte[] payload)
        {
            byte[] frame = new byte[4 + payload.Length];
            BinaryPrimitives.WriteInt32BigEndian(new Span<byte>(frame, 0, 4), payload.Length);
            Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);

            int offset = 0;
            while (offset < frame.Length)
            {
                int sent = await socket.SendAsync(
                    new ArraySegment<byte>(frame, offset, frame.Length - offset),
                    SocketFlags.None).ConfigureAwait(false);
                if (sent == 0)
                    throw new InvalidOperationException("TCP frame 전송 중 socket이 닫혔다.");

                offset += sent;
            }
        }

        private static async Task<byte[]> ReceiveFrameAsync(Socket socket)
        {
            byte[] header = await ReceiveExactAsync(socket, 4).ConfigureAwait(false);
            int payloadLength = BinaryPrimitives.ReadInt32BigEndian(new ReadOnlySpan<byte>(header));
            if (payloadLength < 0 || payloadLength > MaxFrameBytes)
                throw new InvalidOperationException("TCP smoke outbound frame 길이가 허용 범위를 벗어났다.");

            return await ReceiveExactAsync(socket, payloadLength).ConfigureAwait(false);
        }

        private static async Task<byte[]> ReceiveExactAsync(Socket socket, int length)
        {
            Task<byte[]> receiveTask = ReceiveExactCoreAsync(socket, length);
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(ReceiveTimeoutSeconds));
            Task completedTask = await Task.WhenAny(receiveTask, timeoutTask).ConfigureAwait(false);

            if (!object.ReferenceEquals(receiveTask, completedTask))
                throw new TimeoutException("TCP smoke payload 수신 시간이 초과됐다.");

            return await receiveTask.ConfigureAwait(false);
        }

        private static async Task<byte[]> ReceiveExactCoreAsync(Socket socket, int length)
        {
            byte[] buffer = new byte[length];
            int offset = 0;

            while (offset < length)
            {
                int received = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer, offset, length - offset),
                    SocketFlags.None).ConfigureAwait(false);
                if (received == 0)
                    throw new InvalidOperationException("TCP smoke payload 수신 중 socket이 닫혔다.");

                offset += received;
            }

            return buffer;
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

            throw new InvalidOperationException("TCP smoke 종료 후 pool rented count가 기대값으로 돌아오지 않았다.");
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
