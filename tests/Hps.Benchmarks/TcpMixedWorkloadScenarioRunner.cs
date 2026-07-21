using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using Hps.Buffers;
using Hps.Server;
using Hps.Transport;

namespace Hps.Benchmarks
{
    /// <summary>
    /// data와 control을 서로 다른 TCP topic/connection으로 동시에 전송하는 mixed workload runner이다.
    ///
    /// publisher와 subscriber를 stream별로 분리해 한쪽의 pacing 또는 receive 진행이 다른 stream을
    /// 직렬화하지 않게 한다. benchmark client buffer는 주입한 pinned pool에서 connection당 한 번만
    /// 대여하고, message hot path에서는 같은 frame과 receive block을 반복 사용한다.
    /// </summary>
    internal static class TcpMixedWorkloadScenarioRunner
    {
        private const string DataTopic = "data";
        private const string ControlTopic = "control";
        private const byte DataMarker = 0x44;
        private const byte ControlMarker = 0x43;
        private const int LengthPrefixBytes = 4;
        private const int TimestampOffset = 0;
        private const int TimestampBytes = 8;
        private const int SequenceOffset = TimestampOffset + TimestampBytes;
        private const int SequenceBytes = 4;
        private const int MarkerOffset = SequenceOffset + SequenceBytes;
        private const int PatternOffset = MarkerOffset + 1;
        private const int SetupTimeoutSeconds = 5;
        private const int DrainTimeoutSeconds = 10;
        private const int PendingPollMilliseconds = 10;

        /// <summary>
        /// 지정 backend에서 단일 논리 구독자 mixed workload를 실행한다.
        /// </summary>
        /// <param name="options">사전 검증된 전송률, 실행 시간과 구독자 계획이다.</param>
        /// <param name="transportBackend">서버가 사용할 TCP transport backend이다.</param>
        /// <returns>두 stream의 전달·지연과 transport 종료 상태를 결합한 결과이다.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="options"/>가 null이다.</exception>
        /// <exception cref="NotSupportedException">
        /// 현재 Task 범위인 단일 구독자가 아니거나 선택 backend를 현재 OS에서 사용할 수 없다.
        /// </exception>
        public static async Task<MixedWorkloadRunResult> RunAsync(
            MixedWorkloadOptions options,
            TcpLoopbackTransportBackend transportBackend = TcpLoopbackTransportBackend.Saea)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (options.SubscriberCount != 1)
                throw new NotSupportedException("단일 구독자 mixed workload만 현재 구현되어 있습니다.");

            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(MixedWorkloadOptions.MaxFramePayloadBytes);
            SubscriberState dataSubscriberState = new SubscriberState(options.DataMessageCount);
            SubscriberState controlSubscriberState = new SubscriberState(options.ControlMessageCount);
            PublisherState dataPublisherState = new PublisherState();
            PublisherState controlPublisherState = new PublisherState();
            long[] percentileScratch = new long[Math.Max(options.DataMessageCount, options.ControlMessageCount)];
            int timeoutCount = 0;
            int endPendingSendCount = 0;
            TransportDiagnosticsSnapshot diagnostics = default(TransportDiagnosticsSnapshot);

            using (ITransport transport = CreateTransport(transportBackend))
            using (BrokerServer server = new BrokerServer(
                transport,
                pool,
                MixedWorkloadOptions.MaxFramePayloadBytes))
            using (CancellationTokenSource runCancellation = new CancellationTokenSource())
            {
                ITransportDiagnostics transportDiagnostics = GetTransportDiagnostics(transport);
                ITransportEndpointDiagnostics endpointDiagnostics = GetEndpointDiagnostics(transport);
                Socket? dataSubscriber = null;
                Socket? controlSubscriber = null;
                Socket? dataPublisher = null;
                Socket? controlPublisher = null;
                byte[]? dataSubscriberBuffer = null;
                byte[]? controlSubscriberBuffer = null;
                byte[]? dataPublisherBuffer = null;
                byte[]? controlPublisherBuffer = null;
                TaskCompletionSource<long>? startTickSource = null;
                Task? dataReceiveTask = null;
                Task? controlReceiveTask = null;
                Task? dataPublishTask = null;
                Task? controlPublishTask = null;

                runCancellation.CancelAfter(TimeSpan.FromSeconds(
                    options.DurationSeconds + SetupTimeoutSeconds + DrainTimeoutSeconds));

                try
                {
                    // 같은 pool을 server fallback payload와 benchmark client가 공유한다.
                    // 종료 뒤 RentedCount 하나로 두 소유권 경계의 누수를 함께 확인할 수 있다.
                    dataSubscriberBuffer = pool.Rent();
                    controlSubscriberBuffer = pool.Rent();
                    dataPublisherBuffer = pool.Rent();
                    controlPublisherBuffer = pool.Rent();

                    await server.StartTcpAsync(
                        new IPEndPoint(IPAddress.Loopback, 0),
                        runCancellation.Token).ConfigureAwait(false);
                    IPEndPoint boundEndPoint = GetBoundEndPoint(server);

                    dataSubscriber = CreateConnectedTcpClient(boundEndPoint);
                    controlSubscriber = CreateConnectedTcpClient(boundEndPoint);
                    dataPublisher = CreateConnectedTcpClient(boundEndPoint);
                    controlPublisher = CreateConnectedTcpClient(boundEndPoint);

                    int dataSubscriptionLength = PrepareSubscriptionFrame(dataSubscriberBuffer, DataTopic);
                    int controlSubscriptionLength = PrepareSubscriptionFrame(controlSubscriberBuffer, ControlTopic);
                    await SendAllAsync(
                        dataSubscriber,
                        dataSubscriberBuffer,
                        dataSubscriptionLength,
                        runCancellation.Token).ConfigureAwait(false);
                    await SendAllAsync(
                        controlSubscriber,
                        controlSubscriberBuffer,
                        controlSubscriptionLength,
                        runCancellation.Token).ConfigureAwait(false);

                    await server.WaitForSubscriberCountAsync(
                        DataTopic,
                        1,
                        TimeSpan.FromSeconds(SetupTimeoutSeconds),
                        runCancellation.Token).ConfigureAwait(false);
                    await server.WaitForSubscriberCountAsync(
                        ControlTopic,
                        1,
                        TimeSpan.FromSeconds(SetupTimeoutSeconds),
                        runCancellation.Token).ConfigureAwait(false);

                    // receive를 먼저 걸고 publisher 둘은 같은 monotonic start tick을 기다린다.
                    // RunContinuationsAsynchronously는 SetResult 호출 스레드에서 publisher 하나가 먼저 hot loop를
                    // 독점해 다른 stream 시작을 늦추는 inline continuation 편향을 줄인다.
                    startTickSource = new TaskCompletionSource<long>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    dataReceiveTask = ReceiveStreamAsync(
                        dataSubscriber,
                        dataSubscriberBuffer,
                        MixedWorkloadOptions.DataPayloadBytes,
                        DataMarker,
                        dataSubscriberState,
                        runCancellation.Token);
                    controlReceiveTask = ReceiveStreamAsync(
                        controlSubscriber,
                        controlSubscriberBuffer,
                        MixedWorkloadOptions.ControlPayloadBytes,
                        ControlMarker,
                        controlSubscriberState,
                        runCancellation.Token);
                    dataPublishTask = PublishStreamAsync(
                        dataPublisher,
                        dataPublisherBuffer,
                        DataTopic,
                        MixedWorkloadOptions.DataPayloadBytes,
                        DataMarker,
                        options.DataRateHz,
                        options.DataMessageCount,
                        startTickSource.Task,
                        dataPublisherState,
                        runCancellation.Token);
                    controlPublishTask = PublishStreamAsync(
                        controlPublisher,
                        controlPublisherBuffer,
                        ControlTopic,
                        MixedWorkloadOptions.ControlPayloadBytes,
                        ControlMarker,
                        MixedWorkloadOptions.ControlRateHz,
                        options.ControlMessageCount,
                        startTickSource.Task,
                        controlPublisherState,
                        runCancellation.Token);

                    startTickSource.SetResult(Stopwatch.GetTimestamp());
                    await Task.WhenAll(dataPublishTask, controlPublishTask).ConfigureAwait(false);
                    await Task.WhenAll(dataReceiveTask, controlReceiveTask).ConfigureAwait(false);
                    endPendingSendCount = await WaitForPendingSendsToDrainAsync(
                        endpointDiagnostics,
                        runCancellation.Token).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    timeoutCount = 1;
                    endPendingSendCount = CountPendingSends(endpointDiagnostics.GetEndpointSnapshots());
                }
                catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
                {
                    timeoutCount = 1;
                    endPendingSendCount = CountPendingSends(endpointDiagnostics.GetEndpointSnapshots());
                }
                finally
                {
                    diagnostics = transportDiagnostics.GetDiagnosticsSnapshot();

                    // 예상하지 못한 publisher/receiver 실패에서는 반대편 task가 아직 I/O 중일 수 있다.
                    // start gate와 run token을 모두 닫고 socket을 dispose한 뒤 모든 task completion을 관측해야
                    // 해당 task가 pinned block을 더 이상 사용하지 않는다는 수명 경계가 성립한다.
                    runCancellation.Cancel();
                    startTickSource?.TrySetCanceled(runCancellation.Token);
                    dataSubscriber?.Dispose();
                    controlSubscriber?.Dispose();
                    dataPublisher?.Dispose();
                    controlPublisher?.Dispose();

                    await ObserveCleanupTaskAsync(dataReceiveTask).ConfigureAwait(false);
                    await ObserveCleanupTaskAsync(controlReceiveTask).ConfigureAwait(false);
                    await ObserveCleanupTaskAsync(dataPublishTask).ConfigureAwait(false);
                    await ObserveCleanupTaskAsync(controlPublishTask).ConfigureAwait(false);

                    try
                    {
                        await server.StopAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        ReturnBuffer(pool, dataSubscriberBuffer);
                        ReturnBuffer(pool, controlSubscriberBuffer);
                        ReturnBuffer(pool, dataPublisherBuffer);
                        ReturnBuffer(pool, controlPublisherBuffer);
                    }
                }

                SubscriberLatencySummary dataLatency = AggregateSubscriberLatencies(new[]
                {
                    CalculateSubscriberLatency(
                        dataSubscriberState.LatencyTicks,
                        dataSubscriberState.Received,
                        percentileScratch)
                });
                SubscriberLatencySummary controlLatency = AggregateSubscriberLatencies(new[]
                {
                    CalculateSubscriberLatency(
                        controlSubscriberState.LatencyTicks,
                        controlSubscriberState.Received,
                        percentileScratch)
                });
                MixedWorkloadStreamResult dataResult = CreateStreamResult(
                    "data",
                    DataTopic,
                    MixedWorkloadOptions.DataPayloadBytes,
                    options.DataRateHz,
                    options.DurationSeconds,
                    options.DataMessageCount,
                    options.DataDeliveryCount,
                    dataPublisherState,
                    dataSubscriberState,
                    dataLatency);
                MixedWorkloadStreamResult controlResult = CreateStreamResult(
                    "control",
                    ControlTopic,
                    MixedWorkloadOptions.ControlPayloadBytes,
                    MixedWorkloadOptions.ControlRateHz,
                    options.DurationSeconds,
                    options.ControlMessageCount,
                    options.ControlDeliveryCount,
                    controlPublisherState,
                    controlSubscriberState,
                    controlLatency);

                return new MixedWorkloadRunResult(
                    BuildScenarioName(transportBackend),
                    options.DurationSeconds,
                    options.SubscriberCount,
                    options.ClientConnectionCount,
                    options.EstimatedLatencyStorageBytes,
                    dataResult,
                    controlResult,
                    diagnostics.DroppedPendingSendCount,
                    diagnostics.TcpPendingSendQueueHighWatermark,
                    endPendingSendCount,
                    pool.RentedCount,
                    timeoutCount,
                    BenchmarkRunIdentity.CaptureForMixedTcpBackend(transportBackend));
            }
        }

        /// <summary>
        /// 한 subscriber의 원본 latency를 percentile과 전후반 추세로 요약한다.
        /// scratch는 호출자가 순차 재사용하며 이 메서드는 추가 배열을 만들지 않는다.
        /// </summary>
        internal static SubscriberLatencySummary CalculateSubscriberLatency(
            long[] latencyTicks,
            int count,
            long[] scratch)
        {
            if (latencyTicks == null)
                throw new ArgumentNullException(nameof(latencyTicks));
            if (scratch == null)
                throw new ArgumentNullException(nameof(scratch));
            if (count < 0 || count > latencyTicks.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (scratch.Length < count)
                throw new ArgumentException("scratch 길이는 계산할 latency 수 이상이어야 합니다.", nameof(scratch));
            if (count == 0)
                return SubscriberLatencySummary.Zero;

            Array.Copy(latencyTicks, 0, scratch, 0, count);
            Array.Sort(scratch, 0, count);
            double p50 = ReadPercentileMicroseconds(scratch, count, 0.50);
            double p99 = ReadPercentileMicroseconds(scratch, count, 0.99);
            double p999 = ReadPercentileMicroseconds(scratch, count, 0.999);

            int firstHalfCount = count / 2;
            int secondHalfCount = count - firstHalfCount;
            double firstHalfP99 = CalculateRangePercentileMicroseconds(
                latencyTicks,
                0,
                firstHalfCount,
                scratch,
                0.99);
            double secondHalfP99 = CalculateRangePercentileMicroseconds(
                latencyTicks,
                firstHalfCount,
                secondHalfCount,
                scratch,
                0.99);

            // 첫 구간이 비어 있거나 0이면 증가율의 분모가 정의되지 않는다.
            // 이 경우 0으로 기록하고 percentile hard gate가 실제 지연 위반을 별도로 판정한다.
            double growthRatio = firstHalfP99 > 0 ? secondHalfP99 / firstHalfP99 : 0;
            int failedCount = p99 > MixedWorkloadOptions.P99LatencyBudgetMicroseconds
                || p999 > MixedWorkloadOptions.P999LatencyBudgetMicroseconds
                ? 1
                : 0;

            return new SubscriberLatencySummary(
                p50,
                p99,
                p999,
                firstHalfP99,
                secondHalfP99,
                growthRatio,
                failedCount);
        }

        /// <summary>
        /// subscriber별 요약을 stream의 worst-subscriber 값으로 집계한다.
        /// Task 4에서는 단일 구독자만 허용하며 다중 집계는 다음 fan-out 작업에서 확장한다.
        /// </summary>
        internal static SubscriberLatencySummary AggregateSubscriberLatencies(
            SubscriberLatencySummary[] summaries)
        {
            if (summaries == null)
                throw new ArgumentNullException(nameof(summaries));
            if (summaries.Length == 0)
                return SubscriberLatencySummary.Zero;
            if (summaries.Length != 1)
                throw new NotSupportedException("다중 구독자 latency 집계는 아직 구현되지 않았습니다.");

            return summaries[0];
        }

        /// <summary>
        /// wire timestamp가 현재 monotonic clock과 일관되는지 확인하고 latency tick을 계산한다.
        /// </summary>
        internal static bool TryCalculateLatency(
            long embeddedTimestamp,
            long receivedTimestamp,
            out long latencyTicks)
        {
            latencyTicks = 0;
            if (embeddedTimestamp <= 0 || embeddedTimestamp > receivedTimestamp)
                return false;

            latencyTicks = receivedTimestamp - embeddedTimestamp;
            return true;
        }

        private static async Task PublishStreamAsync(
            Socket socket,
            byte[] frame,
            string topic,
            int payloadLength,
            byte marker,
            int rateHz,
            int messageCount,
            Task<long> startTickTask,
            PublisherState state,
            CancellationToken cancellationToken)
        {
            int frameLength = PreparePublisherFrame(frame, topic, payloadLength, marker);
            int payloadOffset = frameLength - payloadLength;
            long startTick = await startTickTask.ConfigureAwait(false);

            // message마다 Task.Delay/보조 async state machine을 만들면 같은 process에서 재는 tail latency에
            // GC jitter가 섞인다. publisher마다 waiter 하나만 만들고 공통 start 기준 absolute deadline으로 재무장한다.
            using (AbsoluteDeadlineWaiter pacingWaiter = new AbsoluteDeadlineWaiter(cancellationToken))
            {
                for (int messageIndex = 0; messageIndex < messageCount; messageIndex++)
                {
                    long targetTick = startTick + ((long)messageIndex * Stopwatch.Frequency / rateHz);
                    await pacingWaiter.WaitUntilAsync(targetTick).ConfigureAwait(false);

                    long timestamp = Stopwatch.GetTimestamp();
                    UpdatePublisherPayload(
                        frame,
                        payloadOffset,
                        payloadLength,
                        marker,
                        messageIndex,
                        timestamp);

                    int sentTotal = 0;
                    while (sentTotal < frameLength)
                    {
                        int sent = await socket.SendAsync(
                            new ReadOnlyMemory<byte>(frame, sentTotal, frameLength - sentTotal),
                            SocketFlags.None,
                            cancellationToken).ConfigureAwait(false);
                        if (sent == 0)
                            throw new InvalidOperationException("TCP frame 전송 중 socket이 먼저 닫혔습니다.");

                        sentTotal += sent;
                    }

                    long completionTick = Stopwatch.GetTimestamp();
                    if (state.Sent == 0)
                        state.FirstCompletionTick = completionTick;
                    state.LastCompletionTick = completionTick;
                    state.Sent++;
                }
            }
        }

        private static async Task ReceiveStreamAsync(
            Socket socket,
            byte[] buffer,
            int expectedPayloadLength,
            byte marker,
            SubscriberState state,
            CancellationToken cancellationToken)
        {
            try
            {
                while (state.Received < state.LatencyTicks.Length)
                {
                    int headerReceived = 0;
                    while (headerReceived < LengthPrefixBytes)
                    {
                        int received = await socket.ReceiveAsync(
                            new Memory<byte>(buffer, headerReceived, LengthPrefixBytes - headerReceived),
                            SocketFlags.None,
                            cancellationToken).ConfigureAwait(false);
                        if (received == 0)
                            throw new InvalidOperationException("TCP frame header 수신 중 socket이 먼저 닫혔습니다.");

                        headerReceived += received;
                    }

                    int payloadLength = BinaryPrimitives.ReadInt32BigEndian(
                        new ReadOnlySpan<byte>(buffer, 0, LengthPrefixBytes));

                    // 잘못된 길이를 정상 payload 크기로 가정해 읽으면 다음 frame 경계를 잃는다.
                    // block 안에 들어오는 길이는 한 번 읽어 socket 진행을 끝낸 뒤 오류로 종료하고,
                    // 음수 또는 block 초과 길이는 읽지 않고 socket cleanup에 맡긴다.
                    if (payloadLength < 0 || payloadLength > buffer.Length)
                    {
                        state.PayloadErrors++;
                        return;
                    }

                    int payloadReceived = 0;
                    while (payloadReceived < payloadLength)
                    {
                        int received = await socket.ReceiveAsync(
                            new Memory<byte>(buffer, payloadReceived, payloadLength - payloadReceived),
                            SocketFlags.None,
                            cancellationToken).ConfigureAwait(false);
                        if (received == 0)
                            throw new InvalidOperationException("TCP frame payload 수신 중 socket이 먼저 닫혔습니다.");

                        payloadReceived += received;
                    }

                    if (payloadLength != expectedPayloadLength)
                    {
                        state.PayloadErrors++;
                        return;
                    }

                    int receiveIndex = state.Received;
                    int sequence = BinaryPrimitives.ReadInt32BigEndian(
                        new ReadOnlySpan<byte>(buffer, SequenceOffset, SequenceBytes));
                    if (sequence != receiveIndex)
                        state.SequenceErrors++;
                    if (!PayloadMatches(buffer, payloadLength, marker, sequence))
                        state.PayloadErrors++;

                    long embeddedTimestamp = BinaryPrimitives.ReadInt64BigEndian(
                        new ReadOnlySpan<byte>(buffer, TimestampOffset, TimestampBytes));
                    long receivedTimestamp = Stopwatch.GetTimestamp();
                    if (TryCalculateLatency(embeddedTimestamp, receivedTimestamp, out long latencyTicks))
                        state.LatencyTicks[receiveIndex] = latencyTicks;
                    else
                        state.PayloadErrors++;

                    state.Received++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                state.TimedOut = true;
                throw;
            }
        }

        private static int PrepareSubscriptionFrame(byte[] frame, string topic)
        {
            string command = "SUBSCRIBE " + topic;
            int commandLength = Encoding.ASCII.GetByteCount(command);
            int frameLength = LengthPrefixBytes + commandLength;
            if (frameLength > frame.Length)
                throw new InvalidOperationException("SUBSCRIBE frame이 benchmark client block을 초과합니다.");

            BinaryPrimitives.WriteInt32BigEndian(
                new Span<byte>(frame, 0, LengthPrefixBytes),
                commandLength);
            Encoding.ASCII.GetBytes(command, 0, command.Length, frame, LengthPrefixBytes);
            return frameLength;
        }

        private static int PreparePublisherFrame(
            byte[] frame,
            string topic,
            int payloadLength,
            byte marker)
        {
            if (payloadLength < PatternOffset)
                throw new ArgumentOutOfRangeException(nameof(payloadLength));

            string commandPrefix = "PUBLISH " + topic + " ";
            int commandPrefixLength = Encoding.ASCII.GetByteCount(commandPrefix);
            int commandLength = commandPrefixLength + payloadLength;
            int frameLength = LengthPrefixBytes + commandLength;
            if (frameLength > frame.Length)
                throw new InvalidOperationException("PUBLISH frame이 benchmark client block을 초과합니다.");

            BinaryPrimitives.WriteInt32BigEndian(
                new Span<byte>(frame, 0, LengthPrefixBytes),
                commandLength);
            Encoding.ASCII.GetBytes(
                commandPrefix,
                0,
                commandPrefix.Length,
                frame,
                LengthPrefixBytes);
            UpdatePublisherPayload(
                frame,
                LengthPrefixBytes + commandPrefixLength,
                payloadLength,
                marker,
                0,
                0);
            return frameLength;
        }

        private static void UpdatePublisherPayload(
            byte[] frame,
            int payloadOffset,
            int payloadLength,
            byte marker,
            int sequence,
            long timestamp)
        {
            Span<byte> payload = new Span<byte>(frame, payloadOffset, payloadLength);
            BinaryPrimitives.WriteInt64BigEndian(payload.Slice(TimestampOffset, TimestampBytes), timestamp);
            BinaryPrimitives.WriteInt32BigEndian(payload.Slice(SequenceOffset, SequenceBytes), sequence);
            payload[MarkerOffset] = marker;

            for (int payloadIndex = PatternOffset; payloadIndex < payloadLength; payloadIndex++)
                payload[payloadIndex] = (byte)((sequence + payloadIndex) & 0xFF);
        }

        private static bool PayloadMatches(byte[] payload, int payloadLength, byte marker, int sequence)
        {
            if (payload[MarkerOffset] != marker)
                return false;

            for (int payloadIndex = PatternOffset; payloadIndex < payloadLength; payloadIndex++)
            {
                if (payload[payloadIndex] != (byte)((sequence + payloadIndex) & 0xFF))
                    return false;
            }

            return true;
        }

        private static async ValueTask SendAllAsync(
            Socket socket,
            byte[] buffer,
            int length,
            CancellationToken cancellationToken)
        {
            int offset = 0;
            while (offset < length)
            {
                int sent = await socket.SendAsync(
                    new ReadOnlyMemory<byte>(buffer, offset, length - offset),
                    SocketFlags.None,
                    cancellationToken).ConfigureAwait(false);
                if (sent == 0)
                    throw new InvalidOperationException("TCP frame 전송 중 socket이 먼저 닫혔습니다.");

                offset += sent;
            }
        }

        private static async Task<int> WaitForPendingSendsToDrainAsync(
            ITransportEndpointDiagnostics endpointDiagnostics,
            CancellationToken cancellationToken)
        {
            long startedAt = Stopwatch.GetTimestamp();
            while (true)
            {
                int pendingCount = CountPendingSends(endpointDiagnostics.GetEndpointSnapshots());
                if (pendingCount == 0)
                    return 0;
                if (Stopwatch.GetElapsedTime(startedAt) >= TimeSpan.FromSeconds(DrainTimeoutSeconds))
                    throw new TimeoutException("mixed workload pending send drain 시간이 초과됐습니다.");

                await Task.Delay(PendingPollMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }

        private static int CountPendingSends(EndpointSnapshot[] snapshots)
        {
            int pendingCount = 0;
            for (int index = 0; index < snapshots.Length; index++)
                pendingCount = checked(pendingCount + snapshots[index].PendingSendCount);

            return pendingCount;
        }

        private static MixedWorkloadStreamResult CreateStreamResult(
            string name,
            string topic,
            int payloadBytes,
            int targetRateHz,
            int targetDurationSeconds,
            int plannedMessageCount,
            int plannedDeliveryCount,
            PublisherState publisher,
            SubscriberState subscriber,
            SubscriberLatencySummary latency)
        {
            int deliveryFailedSubscriberCount = subscriber.Received != plannedMessageCount
                || subscriber.SequenceErrors != 0
                || subscriber.PayloadErrors != 0
                || subscriber.TimedOut
                ? 1
                : 0;
            long publisherElapsedTicks = publisher.Sent >= 2
                ? publisher.LastCompletionTick - publisher.FirstCompletionTick
                : 0;

            return new MixedWorkloadStreamResult(
                name,
                topic,
                payloadBytes,
                targetRateHz,
                targetDurationSeconds,
                plannedMessageCount,
                publisher.Sent,
                1,
                plannedDeliveryCount,
                subscriber.Received,
                subscriber.Received,
                subscriber.Received,
                deliveryFailedSubscriberCount,
                latency.LatencyFailedSubscriberCount,
                subscriber.SequenceErrors,
                subscriber.PayloadErrors,
                latency.P50,
                latency.P99,
                latency.P999,
                latency.FirstHalfP99,
                latency.SecondHalfP99,
                latency.P99GrowthRatio,
                publisherElapsedTicks);
        }

        private static double CalculateRangePercentileMicroseconds(
            long[] latencyTicks,
            int startIndex,
            int count,
            long[] scratch,
            double percentile)
        {
            if (count == 0)
                return 0;

            Array.Copy(latencyTicks, startIndex, scratch, 0, count);
            Array.Sort(scratch, 0, count);
            return ReadPercentileMicroseconds(scratch, count, percentile);
        }

        private static double ReadPercentileMicroseconds(long[] sortedTicks, int count, double percentile)
        {
            int index = (int)Math.Ceiling(count * percentile) - 1;
            if (index < 0)
                index = 0;
            else if (index >= count)
                index = count - 1;

            return sortedTicks[index] * 1000000.0 / Stopwatch.Frequency;
        }

        private static ITransport CreateTransport(TcpLoopbackTransportBackend transportBackend)
        {
            if (transportBackend == TcpLoopbackTransportBackend.Rio)
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    || RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
                {
                    throw new NotSupportedException("RIO benchmark backend를 현재 환경에서 사용할 수 없습니다.");
                }

                return new RioTransport();
            }

            if (transportBackend == TcpLoopbackTransportBackend.IoUring)
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    || IoUringCapabilityProbe.GetStatus() != IoUringCapabilityStatus.Available)
                {
                    throw new NotSupportedException("io_uring benchmark backend를 현재 환경에서 사용할 수 없습니다.");
                }

                return new IoUringTransport();
            }

            return new SaeaTransport();
        }

        private static string BuildScenarioName(TcpLoopbackTransportBackend transportBackend)
        {
            if (transportBackend == TcpLoopbackTransportBackend.Rio)
                return "tcp-mixed-load-rio";
            if (transportBackend == TcpLoopbackTransportBackend.IoUring)
                return "tcp-mixed-load-iouring";

            return "tcp-mixed-load-saea";
        }

        private static ITransportDiagnostics GetTransportDiagnostics(ITransport transport)
        {
            ITransportDiagnostics? diagnostics = transport as ITransportDiagnostics;
            if (diagnostics == null)
                throw new InvalidOperationException("선택 transport가 benchmark diagnostics를 제공하지 않습니다.");

            return diagnostics;
        }

        private static ITransportEndpointDiagnostics GetEndpointDiagnostics(ITransport transport)
        {
            ITransportEndpointDiagnostics? diagnostics = transport as ITransportEndpointDiagnostics;
            if (diagnostics == null)
                throw new InvalidOperationException("선택 transport가 endpoint diagnostics를 제공하지 않습니다.");

            return diagnostics;
        }

        private static IPEndPoint GetBoundEndPoint(BrokerServer server)
        {
            IPEndPoint? endPoint = server.LocalEndPoint as IPEndPoint;
            if (endPoint == null)
                throw new InvalidOperationException("BrokerServer가 TCP loopback endpoint에 bind되지 않았습니다.");

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

        private static void ReturnBuffer(PinnedBlockMemoryPool pool, byte[]? buffer)
        {
            if (buffer != null)
                pool.Return(buffer);
        }

        private static async Task ObserveCleanupTaskAsync(Task? task)
        {
            if (task == null)
                return;

            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
                // 원래 실행 예외 또는 timeout 결과가 이미 주 경로에 보존되어 있다.
                // cleanup에서는 예외를 다시 던져 원인을 가리지 않고 task/buffer 수명 종료만 보장한다.
            }
        }

        private sealed class PublisherState
        {
            public int Sent;
            public long FirstCompletionTick;
            public long LastCompletionTick;
        }

        private sealed class SubscriberState
        {
            public SubscriberState(int plannedMessageCount)
            {
                LatencyTicks = new long[plannedMessageCount];
            }

            public long[] LatencyTicks { get; }
            public int Received;
            public int SequenceErrors;
            public int PayloadErrors;
            public bool TimedOut;
        }

        /// <summary>
        /// publisher 하나가 message마다 새 Task/Timer를 만들지 않고 공통 monotonic deadline까지 기다리게 한다.
        ///
        /// 대기는 순차 호출 전용이다. timer callback과 cancellation callback은 gate로 직렬화하고,
        /// 정상 hot path에서는 같은 ManualResetValueTaskSourceCore를 reset해 재사용한다.
        /// </summary>
        private sealed class AbsoluteDeadlineWaiter : IValueTaskSource<bool>, IDisposable
        {
            private readonly object _gate;
            private readonly Timer _timer;
            private readonly CancellationToken _cancellationToken;
            private readonly CancellationTokenRegistration _cancellationRegistration;
            private ManualResetValueTaskSourceCore<bool> _source;
            private long _targetTick;
            private bool _pending;
            private bool _disposed;

            public AbsoluteDeadlineWaiter(CancellationToken cancellationToken)
            {
                _gate = new object();
                _cancellationToken = cancellationToken;
                _source = new ManualResetValueTaskSourceCore<bool>
                {
                    RunContinuationsAsynchronously = true
                };
                _timer = new Timer(OnTimer, this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _cancellationRegistration = cancellationToken.Register(OnCancellation, this);
            }

            /// <summary>
            /// Stopwatch의 절대 target tick에 도달할 때까지 allocation 없이 비동기로 기다린다.
            /// 이미 deadline을 지났으면 동기 완료 ValueTask를 반환해 뒤처진 publisher가 catch-up할 수 있게 한다.
            /// </summary>
            public ValueTask<bool> WaitUntilAsync(long targetTick)
            {
                lock (_gate)
                {
                    if (_disposed)
                        throw new ObjectDisposedException(nameof(AbsoluteDeadlineWaiter));
                    if (_pending)
                        throw new InvalidOperationException("absolute deadline 대기는 동시에 두 번 시작할 수 없습니다.");

                    // deadline이 이미 지났더라도 취소 상태를 먼저 반영해야 cleanup 직전 마지막 message가
                    // 성공으로 빠져나가지 않는다. 취소는 같은 reusable source의 failure completion으로 반환한다.
                    if (_cancellationToken.IsCancellationRequested)
                    {
                        _source.Reset();
                        _pending = true;
                        short canceledToken = _source.Version;
                        CompleteCanceledLocked();
                        return new ValueTask<bool>(this, canceledToken);
                    }
                    if (Stopwatch.GetTimestamp() >= targetTick)
                        return new ValueTask<bool>(true);

                    _source.Reset();
                    _targetTick = targetTick;
                    _pending = true;
                    short token = _source.Version;

                    ArmTimerLocked();

                    return new ValueTask<bool>(this, token);
                }
            }

            public void Dispose()
            {
                _cancellationRegistration.Dispose();

                lock (_gate)
                {
                    if (_disposed)
                        return;

                    _disposed = true;
                    _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    if (_pending)
                    {
                        _pending = false;
                        _source.SetException(new ObjectDisposedException(nameof(AbsoluteDeadlineWaiter)));
                    }
                }

                _timer.Dispose();
            }

            bool IValueTaskSource<bool>.GetResult(short token)
            {
                return _source.GetResult(token);
            }

            ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token)
            {
                return _source.GetStatus(token);
            }

            void IValueTaskSource<bool>.OnCompleted(
                Action<object?> continuation,
                object? state,
                short token,
                ValueTaskSourceOnCompletedFlags flags)
            {
                _source.OnCompleted(continuation, state, token, flags);
            }

            private static void OnTimer(object? state)
            {
                AbsoluteDeadlineWaiter? waiter = state as AbsoluteDeadlineWaiter;
                waiter?.HandleTimer();
            }

            private static void OnCancellation(object? state)
            {
                AbsoluteDeadlineWaiter? waiter = state as AbsoluteDeadlineWaiter;
                waiter?.HandleCancellation();
            }

            private void HandleTimer()
            {
                lock (_gate)
                {
                    if (!_pending || _disposed)
                        return;

                    if (Stopwatch.GetTimestamp() < _targetTick)
                    {
                        // OS timer가 resolution 경계에서 일찍 깨어나면 남은 절대 시간으로 다시 무장한다.
                        // 주기 timer처럼 최초 생성 시점의 위상 오차를 다음 message까지 누적하지 않는다.
                        ArmTimerLocked();
                        return;
                    }

                    _pending = false;
                    _source.SetResult(true);
                }
            }

            private void HandleCancellation()
            {
                lock (_gate)
                {
                    if (!_pending || _disposed)
                        return;

                    CompleteCanceledLocked();
                }
            }

            private void ArmTimerLocked()
            {
                long remainingStopwatchTicks = _targetTick - Stopwatch.GetTimestamp();
                if (remainingStopwatchTicks <= 0)
                {
                    _pending = false;
                    _source.SetResult(true);
                    return;
                }

                long dueTimeTicks = (long)Math.Ceiling(
                    (double)remainingStopwatchTicks * TimeSpan.TicksPerSecond / Stopwatch.Frequency);
                if (dueTimeTicks < 1)
                    dueTimeTicks = 1;

                _timer.Change(TimeSpan.FromTicks(dueTimeTicks), Timeout.InfiniteTimeSpan);
            }

            private void CompleteCanceledLocked()
            {
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _pending = false;
                _source.SetException(new OperationCanceledException(_cancellationToken));
            }
        }
    }

    /// <summary>
    /// 한 subscriber에서 계산한 latency percentile과 시간 구간별 증가율이다.
    /// 원본 sample을 보관하지 않아 fan-out 집계가 큰 배열의 수명을 연장하지 않게 한다.
    /// </summary>
    internal readonly struct SubscriberLatencySummary
    {
        public SubscriberLatencySummary(
            double p50,
            double p99,
            double p999,
            double firstHalfP99,
            double secondHalfP99,
            double p99GrowthRatio,
            int latencyFailedSubscriberCount)
        {
            P50 = p50;
            P99 = p99;
            P999 = p999;
            FirstHalfP99 = firstHalfP99;
            SecondHalfP99 = secondHalfP99;
            P99GrowthRatio = p99GrowthRatio;
            LatencyFailedSubscriberCount = latencyFailedSubscriberCount;
        }

        public static SubscriberLatencySummary Zero
        {
            get { return new SubscriberLatencySummary(0, 0, 0, 0, 0, 0, 0); }
        }

        public double P50 { get; }
        public double P99 { get; }
        public double P999 { get; }
        public double FirstHalfP99 { get; }
        public double SecondHalfP99 { get; }
        public double P99GrowthRatio { get; }
        public int LatencyFailedSubscriberCount { get; }
    }
}
