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
    /// UDP datagram self-command 기반 loopback benchmark runner 이다.
    ///
    /// TCP runner 와 같은 raw report schema 를 재사용하지만 wire path 는 `BrokerServer.StartUdpAsync(...)`와
    /// `SUBSCRIBE`/`PUBLISH` datagram command 를 사용한다. subscriber outbound 는 length-prefix frame 이 아니라
    /// fan-out payload datagram 자체이므로 receive 검증도 raw datagram payload 를 대상으로 한다.
    /// </summary>
    internal static class UdpLoopbackScenarioRunner
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
                "smoke",
                "-smoke",
                SmokeMessageCount,
                0,
                0,
                false,
                false,
                transportBackend).ConfigureAwait(false);
        }

        public static async Task<TcpLoopbackRunResult> RunLoadAsync(TcpLoopbackTransportBackend transportBackend = TcpLoopbackTransportBackend.Saea)
        {
            return await RunScenarioAsync(
                "load",
                string.Empty,
                BenchmarkTargets.PlannedMessageCount,
                BenchmarkTargets.PublishRateHz,
                BenchmarkTargets.DurationSeconds,
                true,
                false,
                transportBackend).ConfigureAwait(false);
        }

        public static async Task<TcpLoopbackRunResult> RunOpenLoopAsync(TcpLoopbackTransportBackend transportBackend = TcpLoopbackTransportBackend.Saea)
        {
            return await RunScenarioAsync(
                "open-loop",
                "-open-loop",
                BenchmarkTargets.PlannedMessageCount,
                BenchmarkTargets.PublishRateHz,
                BenchmarkTargets.DurationSeconds,
                true,
                true,
                transportBackend).ConfigureAwait(false);
        }

        private static Task<TcpLoopbackRunResult> RunScenarioForTestAsync(
            string resultName,
            string scenarioSuffix,
            int messageCount,
            int publishRateHz,
            int targetDurationSeconds,
            bool pacePublishes,
            bool openLoop,
            TcpLoopbackTransportBackend transportBackend)
        {
            return RunScenarioAsync(
                resultName,
                scenarioSuffix,
                messageCount,
                publishRateHz,
                targetDurationSeconds,
                pacePublishes,
                openLoop,
                transportBackend);
        }

        private static async Task<TcpLoopbackRunResult> RunScenarioAsync(
            string resultName,
            string scenarioSuffix,
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
                    await server.StartUdpAsync(new IPEndPoint(IPAddress.Loopback, 0)).ConfigureAwait(false);
                    IPEndPoint boundEndPoint = GetUdpBoundEndPoint(server);

                    subscriber = CreateBoundUdpClient();
                    publisher = CreateBoundUdpClient();

                    await SendDatagramAsync(
                        subscriber,
                        boundEndPoint,
                        Encoding.ASCII.GetBytes("SUBSCRIBE " + BenchmarkTargets.DefaultTopic)).ConfigureAwait(false);
                    await WaitForSubscriberCountAsync(server, BenchmarkTargets.DefaultTopic, 1).ConfigureAwait(false);

                    pacingClock.Start();
                    if (openLoop)
                    {
                        PayloadExchangeResult exchangeResult = await RunOpenLoopExchangeAsync(
                            subscriber,
                            publisher,
                            boundEndPoint,
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
                            await SendDatagramAsync(
                                publisher,
                                boundEndPoint,
                                CreatePublishCommand(BenchmarkTargets.DefaultTopic, payload)).ConfigureAwait(false);
                            sent++;

                            byte[] receivedPayload = await ReceiveDatagramPayloadAsync(subscriber, BenchmarkTargets.PayloadBytes).ConfigureAwait(false);
                            if (!PayloadEquals(payload, receivedPayload))
                                throw new InvalidOperationException("UDP loopback payload 가 송신 원문과 다릅니다.");

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
                        BuildScenarioName(transportBackend, scenarioSuffix),
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
                        BenchmarkRunIdentity.CaptureForBackendAndProtocol(transportBackend, LoopbackProtocol.Udp));
                }
                finally
                {
                    subscriber?.Dispose();
                    publisher?.Dispose();
                    await server.StopAsync().ConfigureAwait(false);
                }
            }
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
                PercentileLatency(latencyTicks, 0, firstHalfCount, 0.99),
                PercentileLatency(latencyTicks, firstHalfCount, secondHalfCount, 0.99),
                elapsedMilliseconds,
                identity);
        }

        private static async Task<PayloadExchangeResult> RunOpenLoopExchangeAsync(
            Socket subscriber,
            Socket publisher,
            EndPoint serverEndPoint,
            byte[] payload,
            long[] latencyTicks,
            int messageCount,
            int publishRateHz,
            Stopwatch pacingClock)
        {
            // open-loop 는 publisher 가 subscriber receive 완료를 기다리지 않는 경로다.
            // receive task 를 먼저 걸어 둔 뒤 publish loop 는 schedule 만 보고 전송해 UDP send queue/drop 경로를 드러낸다.
            Task<OpenLoopReceiveResult> receiveTask = ReceiveOpenLoopPayloadsAsync(subscriber, latencyTicks, messageCount);
            int sent = 0;

            for (int index = 0; index < messageCount; index++)
            {
                await WaitForScheduledPublishAsync(pacingClock, index, publishRateHz).ConfigureAwait(false);
                PreparePayload(payload, index, Stopwatch.GetTimestamp());
                await SendDatagramAsync(
                    publisher,
                    serverEndPoint,
                    CreatePublishCommand(BenchmarkTargets.DefaultTopic, payload)).ConfigureAwait(false);
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
                    receivedPayload = await ReceiveDatagramPayloadAsync(subscriber, BenchmarkTargets.PayloadBytes).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // UDP open-loop 에서 누락이 생기면 runner 를 예외로 죽이지 않고 failed report 로 남긴다.
                    // 이후 summary/history 가 received < sent 또는 payload-errors 로 hard gate 실패를 관측한다.
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

        private static string BuildScenarioName(TcpLoopbackTransportBackend transportBackend, string suffix)
        {
            string baseName = transportBackend == TcpLoopbackTransportBackend.Rio
                ? "udp-loopback-rio-baseline"
                : "udp-loopback-saea-baseline";

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

        private static IPEndPoint GetUdpBoundEndPoint(BrokerServer server)
        {
            IPEndPoint? endPoint = server.UdpLocalEndPoint as IPEndPoint;
            if (endPoint == null)
                throw new InvalidOperationException("BrokerServer 가 UDP loopback endpoint 에 bind 되지 않았습니다.");

            return endPoint;
        }

        private static Socket CreateBoundUdpClient()
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
                throw new InvalidOperationException("UDP datagram 전체를 송신하지 못했습니다.");
        }

        private static async Task<byte[]> ReceiveDatagramPayloadAsync(Socket socket, int maxLength)
        {
            Task<byte[]> receiveTask = ReceiveDatagramPayloadCoreAsync(socket, maxLength);
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(ReceiveTimeoutSeconds));
            Task completedTask = await Task.WhenAny(receiveTask, timeoutTask).ConfigureAwait(false);

            if (!object.ReferenceEquals(receiveTask, completedTask))
                throw new TimeoutException("UDP loopback payload 수신 시간을 초과했습니다.");

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

            throw new TimeoutException("UDP loopback subscriber 등록 대기가 초과됐습니다.");
        }

        private static SubscriptionTable ReadSubscriptionTable(BrokerServer server)
        {
            // UDP command 에도 ack 가 없으므로 smoke runner 는 TCP runner 와 같은 white-box 경계로
            // subscription table 반영 완료를 확인한 뒤 publish 를 시작한다.
            FieldInfo? field = typeof(BrokerServer).GetField("_subscriptions", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new InvalidOperationException("BrokerServer subscription table 필드를 찾을 수 없습니다.");

            object? value = field.GetValue(server);
            SubscriptionTable? subscriptions = value as SubscriptionTable;
            if (subscriptions == null)
                throw new InvalidOperationException("BrokerServer subscription table 타입이 예상과 다릅니다.");

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

            throw new TimeoutException("UDP loopback 종료 후 pooled buffer 반환 대기가 초과됐습니다.");
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

        private static double PercentileLatency(long[] latencyTicks, int startIndex, int count, double percentile)
        {
            if (count <= 0)
                return 0;

            long[] selectedTicks = new long[count];
            Array.Copy(latencyTicks, startIndex, selectedTicks, 0, count);
            Array.Sort(selectedTicks);
            return ToMicroseconds(selectedTicks[PercentileIndex(selectedTicks.Length, percentile)]);
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
