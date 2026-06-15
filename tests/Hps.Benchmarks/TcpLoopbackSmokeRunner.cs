using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Hps.Broker;
using Hps.Buffers;
using Hps.Server;
using Hps.Transport;

namespace Hps.Benchmarks
{
    /// <summary>
    /// Phase 4 TCP load runner 를 만들기 전의 짧은 smoke runner 이다.
    ///
    /// 실제 benchmark 는 4096B payload 를 100Hz 로 30초 동안 보내야 한다. 이 runner 는 같은 payload 크기와
    /// 같은 BrokerServer/SaeaTransport 경로를 쓰되 메시지 수를 작게 제한해, 계측 경계가 안정적으로 동작하는지만 확인한다.
    /// </summary>
    internal static class TcpLoopbackSmokeRunner
    {
        private const int SmokeMessageCount = 8;
        private const int ReceiveTimeoutSeconds = 5;

        public static async Task<TcpLoopbackSmokeResult> RunAsync()
        {
            byte[] payload = new byte[BenchmarkTargets.PayloadBytes];
            long[] latencyTicks = new long[SmokeMessageCount];
            int sent = 0;
            int received = 0;
            Stopwatch elapsed = Stopwatch.StartNew();

            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(BenchmarkTargets.MaxFramePayloadBytes);
            using (SaeaTransport transport = new SaeaTransport())
            using (BrokerServer server = new BrokerServer(transport, pool, BenchmarkTargets.MaxFramePayloadBytes))
            {
                Socket? subscriber = null;
                Socket? publisher = null;

                try
                {
                    await server.StartTcpAsync(new IPEndPoint(IPAddress.Loopback, 0)).ConfigureAwait(false);
                    IPEndPoint boundEndPoint = GetBoundEndPoint(server);

                    subscriber = CreateConnectedTcpClient(boundEndPoint);
                    publisher = CreateConnectedTcpClient(boundEndPoint);

                    await SendFrameAsync(subscriber, Encoding.ASCII.GetBytes("SUBSCRIBE " + BenchmarkTargets.DefaultTopic)).ConfigureAwait(false);
                    await WaitForSubscriberCountAsync(server, BenchmarkTargets.DefaultTopic, 1).ConfigureAwait(false);

                    for (int index = 0; index < SmokeMessageCount; index++)
                    {
                        FillPayload(payload, index);
                        long startTimestamp = Stopwatch.GetTimestamp();
                        BinaryPrimitives.WriteInt64BigEndian(new Span<byte>(payload, 0, 8), startTimestamp);

                        await SendFrameAsync(publisher, CreatePublishCommand(BenchmarkTargets.DefaultTopic, payload)).ConfigureAwait(false);
                        sent++;

                        byte[] receivedPayload = await ReceiveExactAsync(subscriber, payload.Length).ConfigureAwait(false);
                        if (!PayloadEquals(payload, receivedPayload))
                            throw new InvalidOperationException("smoke payload 가 송신 원문과 다르다.");

                        long embeddedTimestamp = BinaryPrimitives.ReadInt64BigEndian(new ReadOnlySpan<byte>(receivedPayload, 0, 8));
                        latencyTicks[received] = Stopwatch.GetTimestamp() - embeddedTimestamp;
                        received++;
                    }

                    await WaitForRentedCountAsync(pool, 0).ConfigureAwait(false);

                    elapsed.Stop();
                    TransportDiagnosticsSnapshot diagnostics = ((ITransportDiagnostics)transport).GetDiagnosticsSnapshot();
                    return CreateResult(sent, received, diagnostics.DroppedPendingSendCount, pool.RentedCount, latencyTicks, elapsed.ElapsedMilliseconds);
                }
                finally
                {
                    subscriber?.Dispose();
                    publisher?.Dispose();
                    await server.StopAsync().ConfigureAwait(false);
                }
            }
        }

        private static TcpLoopbackSmokeResult CreateResult(
            int sent,
            int received,
            long dropped,
            int poolRented,
            long[] latencyTicks,
            long elapsedMilliseconds)
        {
            long[] completedTicks = new long[received];
            Array.Copy(latencyTicks, completedTicks, received);
            Array.Sort(completedTicks);

            double p50 = completedTicks.Length == 0 ? 0 : ToMicroseconds(completedTicks[PercentileIndex(completedTicks.Length, 0.50)]);
            double p99 = completedTicks.Length == 0 ? 0 : ToMicroseconds(completedTicks[PercentileIndex(completedTicks.Length, 0.99)]);

            return new TcpLoopbackSmokeResult(
                BenchmarkTargets.TcpLoopbackBaselineName + "-smoke",
                BenchmarkTargets.PayloadBytes,
                sent,
                received,
                dropped,
                poolRented,
                p50,
                p99,
                elapsedMilliseconds);
        }

        private static int PercentileIndex(int count, double percentile)
        {
            int index = (int)Math.Ceiling(count * percentile) - 1;
            if (index < 0)
                return 0;
            if (index >= count)
                return count - 1;

            return index;
        }

        private static double ToMicroseconds(long stopwatchTicks)
        {
            return stopwatchTicks * 1000000.0 / Stopwatch.Frequency;
        }

        private static IPEndPoint GetBoundEndPoint(BrokerServer server)
        {
            IPEndPoint? endPoint = server.LocalEndPoint as IPEndPoint;
            if (endPoint == null)
                throw new InvalidOperationException("BrokerServer 가 TCP loopback endpoint 에 bind 되지 않았다.");

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
                int sent = await socket.SendAsync(new ArraySegment<byte>(frame, offset, frame.Length - offset), SocketFlags.None).ConfigureAwait(false);
                if (sent == 0)
                    throw new InvalidOperationException("TCP frame 전송 중 socket 이 먼저 닫혔다.");

                offset += sent;
            }
        }

        private static async Task<byte[]> ReceiveExactAsync(Socket socket, int length)
        {
            Task<byte[]> receiveTask = ReceiveExactCoreAsync(socket, length);
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(ReceiveTimeoutSeconds));
            Task completedTask = await Task.WhenAny(receiveTask, timeoutTask).ConfigureAwait(false);

            if (!object.ReferenceEquals(receiveTask, completedTask))
                throw new TimeoutException("smoke payload 수신 시간이 초과됐다.");

            return await receiveTask.ConfigureAwait(false);
        }

        private static async Task<byte[]> ReceiveExactCoreAsync(Socket socket, int length)
        {
            byte[] buffer = new byte[length];
            int offset = 0;

            while (offset < length)
            {
                int received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, offset, length - offset), SocketFlags.None).ConfigureAwait(false);
                if (received == 0)
                    throw new InvalidOperationException("payload 수신 중 socket 이 먼저 닫혔다.");

                offset += received;
            }

            return buffer;
        }

        private static async Task WaitForSubscriberCountAsync(BrokerServer server, string topic, int expected)
        {
            SubscriptionTable subscriptions = ReadSubscriptionTable(server);
            DateTime deadline = DateTime.UtcNow.AddSeconds(ReceiveTimeoutSeconds);

            while (DateTime.UtcNow < deadline)
            {
                if (subscriptions.CountSubscribers(topic) == expected)
                    return;

                await Task.Delay(10).ConfigureAwait(false);
            }

            throw new TimeoutException("smoke subscriber 등록 대기가 초과됐다.");
        }

        private static SubscriptionTable ReadSubscriptionTable(BrokerServer server)
        {
            // 현재 wire protocol 에는 SUBSCRIBE ack 가 없다. 부하 runner 에서 publish 시작 race 를 피하기 위해
            // 통합 테스트와 같은 white-box 경계로 구독 등록 완료만 확인한다.
            FieldInfo? field = typeof(BrokerServer).GetField("_subscriptions", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new InvalidOperationException("BrokerServer subscription table 필드를 찾을 수 없다.");

            object? value = field.GetValue(server);
            SubscriptionTable? subscriptions = value as SubscriptionTable;
            if (subscriptions == null)
                throw new InvalidOperationException("BrokerServer subscription table 타입이 예상과 다르다.");

            return subscriptions;
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

            throw new TimeoutException("smoke 종료 후 pooled buffer 반환 대기가 초과됐다.");
        }

        private static void FillPayload(byte[] payload, int messageIndex)
        {
            for (int index = 0; index < payload.Length; index++)
                payload[index] = (byte)((messageIndex + index) & 0xFF);
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
