using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Hps.Broker;
using Hps.Buffers;
using Hps.Server;
using Hps.Transport;

namespace Hps.Benchmarks
{
    /// <summary>
    /// Phase 4 TCP loopback benchmark 시나리오 runner 이다.
    ///
    /// smoke 는 짧은 경로 검증으로 빠르게 실패를 잡고, load 는 같은 BrokerServer/SaeaTransport 경로에서
    /// 4096B payload 를 100Hz 로 30초 동안 전송한다. open-loop 는 publisher 와 subscriber receive 를 분리해
    /// closed-loop 가 가리는 send queue/backpressure 경로를 관측한다. 세 경로를 같은 구현으로 묶어
    /// 측정 방식 차이 때문에 결과 해석이 갈라지지 않게 한다.
    /// </summary>
    internal static class TcpLoopbackScenarioRunner
    {
        private const int SmokeMessageCount = 8;
        private const int ReceiveTimeoutSeconds = 5;
        private const int TimestampOffset = 0;
        private const int TimestampBytes = 8;
        private const int SequenceOffset = TimestampOffset + TimestampBytes;
        private const int SequenceBytes = 4;
        private const int PayloadPatternOffset = SequenceOffset + SequenceBytes;

        public static async Task<TcpLoopbackRunResult> RunSmokeAsync(TcpLoopbackTransportBackend transportBackend = TcpLoopbackTransportBackend.Saea)
        {
            return await RunScenarioAsync(
                resultName: "smoke",
                scenario: BuildScenarioName(transportBackend, "-smoke"),
                messageCount: SmokeMessageCount,
                publishRateHz: 0,
                targetDurationSeconds: 0,
                pacePublishes: false,
                openLoop: false,
                transportBackend: transportBackend).ConfigureAwait(false);
        }

        public static async Task<TcpLoopbackRunResult> RunLoadAsync(TcpLoopbackTransportBackend transportBackend = TcpLoopbackTransportBackend.Saea)
        {
            return await RunScenarioAsync(
                resultName: "load",
                scenario: BuildScenarioName(transportBackend, string.Empty),
                messageCount: BenchmarkTargets.PlannedMessageCount,
                publishRateHz: BenchmarkTargets.PublishRateHz,
                targetDurationSeconds: BenchmarkTargets.DurationSeconds,
                pacePublishes: true,
                openLoop: false,
                transportBackend: transportBackend).ConfigureAwait(false);
        }

        public static async Task<TcpLoopbackRunResult> RunOpenLoopAsync(TcpLoopbackTransportBackend transportBackend = TcpLoopbackTransportBackend.Saea)
        {
            return await RunScenarioAsync(
                resultName: "open-loop",
                scenario: BuildScenarioName(transportBackend, "-open-loop"),
                messageCount: BenchmarkTargets.PlannedMessageCount,
                publishRateHz: BenchmarkTargets.PublishRateHz,
                targetDurationSeconds: BenchmarkTargets.DurationSeconds,
                pacePublishes: true,
                openLoop: true,
                transportBackend: transportBackend).ConfigureAwait(false);
        }

        private static async Task<TcpLoopbackRunResult> RunScenarioAsync(
            string resultName,
            string scenario,
            int messageCount,
            int publishRateHz,
            int targetDurationSeconds,
            bool pacePublishes,
            bool openLoop,
            TcpLoopbackTransportBackend transportBackend)
        {
            byte[] payload = new byte[BenchmarkTargets.PayloadBytes];
            long[] latencyTicks = new long[messageCount];
            int sent = 0;
            int received = 0;
            int payloadErrors = 0;
            Stopwatch elapsed = Stopwatch.StartNew();
            Stopwatch pacingClock = new Stopwatch();

            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(BenchmarkTargets.MaxFramePayloadBytes);
            using (ITransport transport = CreateTransport(transportBackend))
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

                    pacingClock.Start();
                    if (openLoop)
                    {
                        PayloadExchangeResult exchangeResult = await RunOpenLoopExchangeAsync(
                            subscriber,
                            publisher,
                            payload,
                            latencyTicks,
                            messageCount,
                            publishRateHz,
                            pacingClock).ConfigureAwait(false);
                        sent = exchangeResult.Sent;
                        received = exchangeResult.Received;
                        payloadErrors = exchangeResult.PayloadErrors;
                    }
                    else
                    {
                        for (int index = 0; index < messageCount; index++)
                        {
                            if (pacePublishes)
                                await WaitForScheduledPublishAsync(pacingClock, index, publishRateHz).ConfigureAwait(false);

                            PreparePayload(payload, index, Stopwatch.GetTimestamp());

                            await SendFrameAsync(publisher, CreatePublishCommand(BenchmarkTargets.DefaultTopic, payload)).ConfigureAwait(false);
                            sent++;

                            byte[] receivedPayload = await ReceiveFrameAsync(subscriber).ConfigureAwait(false);
                            if (!PayloadEquals(payload, receivedPayload))
                                throw new InvalidOperationException("loopback payload 가 송신 원문과 다르다.");

                            long embeddedTimestamp = BinaryPrimitives.ReadInt64BigEndian(new ReadOnlySpan<byte>(receivedPayload, TimestampOffset, TimestampBytes));
                            latencyTicks[received] = Stopwatch.GetTimestamp() - embeddedTimestamp;
                            received++;
                        }
                    }

                    await WaitForRentedCountAsync(pool, 0).ConfigureAwait(false);

                    elapsed.Stop();
                    TransportDiagnosticsSnapshot diagnostics = ((ITransportDiagnostics)transport).GetDiagnosticsSnapshot();
                    return CreateResult(
                        resultName,
                        scenario,
                        publishRateHz,
                        targetDurationSeconds,
                        messageCount,
                        sent,
                        received,
                        diagnostics.DroppedPendingSendCount,
                        diagnostics.TcpPendingSendQueueHighWatermark,
                        diagnostics.UdpPendingSendQueueHighWatermark,
                        payloadErrors,
                        pool.RentedCount,
                        latencyTicks,
                        elapsed.ElapsedMilliseconds,
                        BenchmarkRunIdentity.CaptureForBackend(transportBackend));
                }
                finally
                {
                    subscriber?.Dispose();
                    publisher?.Dispose();
                    await server.StopAsync().ConfigureAwait(false);
                }
            }
        }

        private static string BuildScenarioName(TcpLoopbackTransportBackend transportBackend, string suffix)
        {
            string baseName = transportBackend == TcpLoopbackTransportBackend.Rio
                ? "tcp-loopback-rio-baseline"
                : BenchmarkTargets.TcpLoopbackBaselineName;

            return baseName + suffix;
        }

        private static ITransport CreateTransport(TcpLoopbackTransportBackend transportBackend)
        {
            if (transportBackend == TcpLoopbackTransportBackend.Rio)
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                    RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
                {
                    throw new NotSupportedException("RIO benchmark backend 를 현재 환경에서 사용할 수 없습니다.");
                }

                return new RioTransport();
            }

            return new SaeaTransport();
        }

        private static TcpLoopbackRunResult CreateResult(
            string resultName,
            string scenario,
            int targetRateHz,
            int targetDurationSeconds,
            int plannedMessageCount,
            int sent,
            int received,
            long dropped,
            int tcpPendingSendQueueHighWatermark,
            int udpPendingSendQueueHighWatermark,
            int payloadErrors,
            int poolRented,
            long[] latencyTicks,
            long elapsedMilliseconds,
            BenchmarkRunIdentity identity)
        {
            long[] completedTicks = new long[received];
            Array.Copy(latencyTicks, completedTicks, received);
            Array.Sort(completedTicks);

            double p50 = completedTicks.Length == 0 ? 0 : ToMicroseconds(completedTicks[PercentileIndex(completedTicks.Length, 0.50)]);
            double p99 = completedTicks.Length == 0 ? 0 : ToMicroseconds(completedTicks[PercentileIndex(completedTicks.Length, 0.99)]);
            int firstHalfCount = received / 2;
            int secondHalfCount = received - firstHalfCount;
            double firstHalfP99 = PercentileLatency(latencyTicks, 0, firstHalfCount, 0.99);
            double secondHalfP99 = PercentileLatency(latencyTicks, firstHalfCount, secondHalfCount, 0.99);

            return new TcpLoopbackRunResult(
                resultName,
                scenario,
                BenchmarkTargets.PayloadBytes,
                targetRateHz,
                targetDurationSeconds,
                plannedMessageCount,
                sent,
                received,
                dropped,
                tcpPendingSendQueueHighWatermark,
                udpPendingSendQueueHighWatermark,
                payloadErrors,
                poolRented,
                p50,
                p99,
                firstHalfP99,
                secondHalfP99,
                elapsedMilliseconds,
                identity);
        }

        private static double PercentileLatency(long[] latencyTicks, int startIndex, int count, double percentile)
        {
            if (count <= 0)
                return 0;

            long[] selectedTicks = new long[count];
            Array.Copy(latencyTicks, startIndex, selectedTicks, 0, count);
            Array.Sort(selectedTicks);
            return ToMicroseconds(selectedTicks[PercentileIndex(selectedTicks.Length, percentile)]);
        }

        private static async Task<PayloadExchangeResult> RunOpenLoopExchangeAsync(
            Socket subscriber,
            Socket publisher,
            byte[] payload,
            long[] latencyTicks,
            int messageCount,
            int publishRateHz,
            Stopwatch pacingClock)
        {
            // open-loop 의 핵심은 publisher 가 subscriber receive 완료를 기다리지 않는 것이다.
            // receiver 를 먼저 걸어 둔 뒤 publish loop 는 시간표만 보고 전송하므로, send queue/backpressure 경로가
            // closed-loop 보다 더 쉽게 드러난다.
            Task<OpenLoopReceiveResult> receiveTask = ReceiveOpenLoopPayloadsAsync(subscriber, latencyTicks, messageCount);
            int sent = 0;

            for (int index = 0; index < messageCount; index++)
            {
                await WaitForScheduledPublishAsync(pacingClock, index, publishRateHz).ConfigureAwait(false);
                PreparePayload(payload, index, Stopwatch.GetTimestamp());
                await SendFrameAsync(publisher, CreatePublishCommand(BenchmarkTargets.DefaultTopic, payload)).ConfigureAwait(false);
                sent++;
            }

            OpenLoopReceiveResult receiveResult = await receiveTask.ConfigureAwait(false);
            return new PayloadExchangeResult(sent, receiveResult.Received, receiveResult.PayloadErrors);
        }

        private static async Task<OpenLoopReceiveResult> ReceiveOpenLoopPayloadsAsync(Socket subscriber, long[] latencyTicks, int messageCount)
        {
            int received = 0;
            int payloadErrors = 0;

            while (received < messageCount)
            {
                byte[] receivedPayload;
                try
                {
                    receivedPayload = await ReceiveFrameAsync(subscriber).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // open-loop 에서 일부 payload 가 누락되면 전체 runner 를 예외로 죽이지 않고 fail summary 를 남긴다.
                    // 누락 자체는 received < sent 와 dropped/payload-errors 를 통해 관측한다.
                    break;
                }

                int sequence = BinaryPrimitives.ReadInt32BigEndian(new ReadOnlySpan<byte>(receivedPayload, SequenceOffset, SequenceBytes));
                if (!PayloadMatchesSequencePattern(receivedPayload, sequence, received))
                    payloadErrors++;

                long embeddedTimestamp = BinaryPrimitives.ReadInt64BigEndian(new ReadOnlySpan<byte>(receivedPayload, TimestampOffset, TimestampBytes));
                latencyTicks[received] = Stopwatch.GetTimestamp() - embeddedTimestamp;
                received++;
            }

            return new OpenLoopReceiveResult(received, payloadErrors);
        }

        private static async Task WaitForScheduledPublishAsync(Stopwatch stopwatch, int messageIndex, int publishRateHz)
        {
            if (publishRateHz <= 0)
                return;

            long targetTicks = (long)messageIndex * Stopwatch.Frequency / publishRateHz;
            while (stopwatch.ElapsedTicks < targetTicks)
            {
                long remainingTicks = targetTicks - stopwatch.ElapsedTicks;
                int remainingMilliseconds = (int)(remainingTicks * 1000 / Stopwatch.Frequency);

                if (remainingMilliseconds > 1)
                    await Task.Delay(remainingMilliseconds - 1).ConfigureAwait(false);
                else
                    await Task.Yield();
            }
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
                throw new TimeoutException("loopback payload 수신 시간이 초과됐다.");

            return await receiveTask.ConfigureAwait(false);
        }

        private static async Task<byte[]> ReceiveFrameAsync(Socket socket)
        {
            byte[] header = await ReceiveExactAsync(socket, 4).ConfigureAwait(false);
            int payloadLength = BinaryPrimitives.ReadInt32BigEndian(new ReadOnlySpan<byte>(header));
            if (payloadLength < 0 || payloadLength > BenchmarkTargets.MaxFramePayloadBytes)
                throw new InvalidOperationException("loopback outbound frame 길이가 benchmark 허용 범위를 벗어났다.");

            return await ReceiveExactAsync(socket, payloadLength).ConfigureAwait(false);
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

            throw new TimeoutException("loopback subscriber 등록 대기가 초과됐다.");
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

            throw new TimeoutException("loopback 종료 후 pooled buffer 반환 대기가 초과됐다.");
        }

        private static void PreparePayload(byte[] payload, int messageIndex, long timestamp)
        {
            FillPayload(payload, messageIndex);
            BinaryPrimitives.WriteInt64BigEndian(new Span<byte>(payload, TimestampOffset, TimestampBytes), timestamp);
            BinaryPrimitives.WriteInt32BigEndian(new Span<byte>(payload, SequenceOffset, SequenceBytes), messageIndex);
        }

        private static void FillPayload(byte[] payload, int messageIndex)
        {
            for (int index = 0; index < payload.Length; index++)
                payload[index] = (byte)((messageIndex + index) & 0xFF);
        }

        private static bool PayloadMatchesSequencePattern(byte[] actual, int sequence, int expectedReceiveIndex)
        {
            if (sequence != expectedReceiveIndex)
                return false;

            for (int index = PayloadPatternOffset; index < actual.Length; index++)
            {
                if (actual[index] != (byte)((sequence + index) & 0xFF))
                    return false;
            }

            return true;
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

        private readonly struct PayloadExchangeResult
        {
            public PayloadExchangeResult(int sent, int received, int payloadErrors)
            {
                Sent = sent;
                Received = received;
                PayloadErrors = payloadErrors;
            }

            public int Sent { get; }

            public int Received { get; }

            public int PayloadErrors { get; }
        }

        private readonly struct OpenLoopReceiveResult
        {
            public OpenLoopReceiveResult(int received, int payloadErrors)
            {
                Received = received;
                PayloadErrors = payloadErrors;
            }

            public int Received { get; }

            public int PayloadErrors { get; }
        }
    }
}
