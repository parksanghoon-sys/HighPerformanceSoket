using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hps.Buffers;
using Hps.Transport;
using Xunit;

namespace Hps.Transport.Tests
{
    public sealed class TransportSendQueueTests
    {
        // pending 송신 소유권 테스트: TrySend 가 true 를 반환하면 Transport 가 구독자 ref 하나를 소유한다.
        // publish 가드 ref 가 먼저 해제되어도 close drain 전까지 버퍼가 풀로 돌아가면 안 되고, close 에서 정확히 반환되어야 한다.
        [Fact]
        public void TrySend_WhenConnectionIsOpen_KeepsPendingRefUntilCloseDrains()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
            RefCountedBuffer buffer = pool.RentCounted();
            buffer.SetLength(4);
            buffer.AddRef();
            TransportSendBuffer sendBuffer = new TransportSendBuffer(buffer, 0, 4);
            TestTransport transport = new TestTransport();
            TransportConnection connection = transport.CreateConnection();

            Assert.True(transport.TrySend(connection, sendBuffer));
            Assert.Equal(1, connection.PendingSendCount);

            buffer.Release();
            Assert.Equal(1, pool.RentedCount);

            connection.Close();

            Assert.Equal(0, connection.PendingSendCount);
            Assert.Equal(0, pool.RentedCount);
        }

        // close 이후 reject 테스트: 닫힌 연결에 TrySend 가 false 를 반환하면 Transport 는 소유권을 가져가지 않는다.
        // 호출자가 자신이 추가한 구독자 ref 를 Release 해야 하므로, 이 경로에서 Transport 가 대신 Release 하면 이중 반환 위험이 생긴다.
        [Fact]
        public void TrySend_WhenConnectionIsClosed_ReturnsFalseAndLeavesReleaseToCaller()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
            RefCountedBuffer buffer = pool.RentCounted();
            buffer.SetLength(4);
            buffer.AddRef();
            TransportSendBuffer sendBuffer = new TransportSendBuffer(buffer, 0, 4);
            TestTransport transport = new TestTransport();
            TransportConnection connection = transport.CreateConnection();

            connection.Close();

            Assert.False(transport.TrySend(connection, sendBuffer));
            Assert.Equal(1, pool.RentedCount);

            buffer.Release();
            Assert.Equal(1, pool.RentedCount);

            buffer.Release();
            Assert.Equal(0, pool.RentedCount);
        }

        // default 송신 요청 방어 테스트: TransportSendBuffer 는 struct 이므로 호출자가 생성자를 거치지 않은
        // default 값을 넘길 수 있다. 이 값이 pending 큐에 들어가면 close drain 시점에야 실패하므로, 수락 경계에서 즉시 거부해야 한다.
        [Fact]
        public void TrySend_WhenSendBufferIsDefault_ThrowsBeforeQueueOwnership()
        {
            TestTransport transport = new TestTransport();
            TransportConnection connection = transport.CreateConnection();

            Assert.Throws<InvalidOperationException>(delegate()
            {
                transport.TrySend(connection, default(TransportSendBuffer));
            });

            Assert.Equal(0, connection.PendingSendCount);
        }

        // close idempotency 테스트: close 는 pending 항목을 한 번만 drain 해야 한다.
        // 두 번째 Close 가 같은 send buffer 를 다시 Release 하면 RefCountedBuffer 이중 반환 가드가 예외로 드러난다.
        [Fact]
        public void Close_WhenCalledTwice_DrainsPendingOnlyOnce()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
            RefCountedBuffer buffer = pool.RentCounted();
            buffer.SetLength(4);
            buffer.AddRef();
            TransportSendBuffer sendBuffer = new TransportSendBuffer(buffer, 0, 4);
            TestTransport transport = new TestTransport();
            TransportConnection connection = transport.CreateConnection();

            Assert.True(transport.TrySend(connection, sendBuffer));
            buffer.Release();

            connection.Close();
            connection.Close();

            Assert.Equal(0, pool.RentedCount);
        }

        // 펌프 dequeue 소유권 테스트: 송신 펌프가 큐에서 항목을 꺼낸 뒤에는 그 ref 는 더 이상 pending 이 아니다.
        // close 는 pending 만 drain 하고, in-flight ref 는 펌프 완료 경로가 Release 해야 이중 release 를 피할 수 있다.
        [Fact]
        public void Close_WhenSendWasDequeuedForPump_DoesNotReleaseInFlightBuffer()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
            RefCountedBuffer buffer = pool.RentCounted();
            buffer.SetLength(4);
            buffer.AddRef();
            TransportSendBuffer sendBuffer = new TransportSendBuffer(buffer, 0, 4);
            TestTransport transport = new TestTransport();
            TransportConnection connection = transport.CreateConnection();

            Assert.True(transport.TrySend(connection, sendBuffer));
            Assert.True(connection.TryBeginInFlightSend(out TransportConnection.InFlightSend? inFlight));
            Assert.NotNull(inFlight);
            Assert.Equal(0, connection.PendingSendCount);

            buffer.Release();
            connection.Close();

            Assert.Equal(1, pool.RentedCount);

            inFlight!.Dispose();
            Assert.Equal(0, pool.RentedCount);
        }

        // in-flight 완료 Release 테스트: 송신 펌프가 dequeue 한 항목은 close drain 대상이 아니므로,
        // completion callback 이 사용할 명시적 경로에서 Transport 소유 ref 를 정확히 반환해야 한다.
        [Fact]
        public void InFlightSend_WhenPumpCompletesDequeuedSend_CompleteReleasesTransportOwnedRef()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
            RefCountedBuffer buffer = pool.RentCounted();
            buffer.SetLength(4);
            buffer.AddRef();
            TransportSendBuffer sendBuffer = new TransportSendBuffer(buffer, 0, 4);
            TestTransport transport = new TestTransport();
            TransportConnection connection = transport.CreateConnection();

            Assert.True(transport.TrySend(connection, sendBuffer));
            Assert.True(connection.TryBeginInFlightSend(out TransportConnection.InFlightSend? inFlight));
            Assert.NotNull(inFlight);

            buffer.Release();
            Assert.Equal(1, pool.RentedCount);

            inFlight!.Complete();
            inFlight.Dispose();

            Assert.Equal(0, pool.RentedCount);
        }

        // 펌프 abandon 누수 방어 테스트: 실제 송신 펌프가 큐에서 항목을 꺼낸 뒤 close/unwind 로 루프를 빠져나가면
        // completion callback 이 오지 않을 수 있다. in-flight 소유권을 handle 로 감싸 Dispose 경로에서 반드시 반환해야 한다.
        [Fact]
        public void InFlightSend_WhenPumpAbandonsAfterClose_DisposePathReleasesTransportOwnedRef()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
            RefCountedBuffer buffer = pool.RentCounted();
            buffer.SetLength(4);
            buffer.AddRef();
            TransportSendBuffer sendBuffer = new TransportSendBuffer(buffer, 0, 4);
            TestTransport transport = new TestTransport();
            TransportConnection connection = transport.CreateConnection();

            Assert.True(transport.TrySend(connection, sendBuffer));
            Assert.True(connection.TryBeginInFlightSend(out TransportConnection.InFlightSend? inFlight));
            Assert.NotNull(inFlight);

            buffer.Release();
            connection.Close();
            Assert.Equal(1, pool.RentedCount);

            inFlight!.Dispose();
            inFlight.Dispose();

            Assert.Equal(0, pool.RentedCount);
        }

        // TCP backpressure drop-oldest 테스트: pending queue 가 용량을 넘으면 이미 enqueue 된 가장 오래된 항목을 제거하고
        // 그 Transport 소유 ref 를 즉시 Release 해야 한다. 그래야 느린 소비자에서 큐가 무한 증가하지 않고 D012의 evict-release 계약을 지킨다.
        [Fact]
        public void TrySend_WhenPendingQueueExceedsCapacity_DropsOldestAndReleasesEvictedRef()
        {
            const int Capacity = 16;
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
            RefCountedBuffer[] buffers = RentNumberedBuffers(pool, Capacity + 1);
            TestTransport transport = new TestTransport();
            TransportConnection connection = transport.CreateConnection();

            for (int index = 0; index < buffers.Length; index++)
            {
                buffers[index].AddRef();
                Assert.True(transport.TrySend(connection, new TransportSendBuffer(buffers[index], 0, buffers[index].Length)));
            }

            Assert.Equal(Capacity, connection.PendingSendCount);

            ReleasePublisherRefs(buffers);
            Assert.Equal(Capacity, pool.RentedCount);

            for (int expected = 1; expected <= Capacity; expected++)
            {
                Assert.True(connection.TryBeginInFlightSend(out TransportConnection.InFlightSend? inFlight));
                Assert.NotNull(inFlight);
                Assert.Equal((byte)expected, inFlight!.SendBuffer.Buffer.Span[0]);
                inFlight.Complete();
                inFlight.Dispose();
            }

            Assert.Equal(0, connection.PendingSendCount);
            Assert.Equal(0, pool.RentedCount);
        }

        // TCP backpressure close 경합 테스트: drop-oldest 로 이미 evict 된 항목은 close drain 이 다시 만지면 안 된다.
        // overflow 뒤 publisher guard ref 를 놓고 Close 하면 남아 있는 pending 항목만 반환되어 누수와 이중 반환이 모두 없어야 한다.
        [Fact]
        public void Close_WhenPendingQueueAlreadyEvictedOldest_DrainsOnlyRemainingPendingRefs()
        {
            const int Capacity = 16;
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
            RefCountedBuffer[] buffers = RentNumberedBuffers(pool, Capacity + 1);
            TestTransport transport = new TestTransport();
            TransportConnection connection = transport.CreateConnection();

            for (int index = 0; index < buffers.Length; index++)
            {
                buffers[index].AddRef();
                Assert.True(transport.TrySend(connection, new TransportSendBuffer(buffers[index], 0, buffers[index].Length)));
            }

            ReleasePublisherRefs(buffers);
            Assert.Equal(Capacity, pool.RentedCount);

            connection.Close();

            Assert.Equal(0, connection.PendingSendCount);
            Assert.Equal(0, pool.RentedCount);
        }

        // TCP drop 관측성 테스트: drop-oldest 는 데이터를 조용히 버리므로 최소한 connection 내부 counter 로
        // evict 횟수를 확인할 수 있어야 한다. 이 단위는 public metric API가 아니라 low-overhead 내부 진단 표면만 고정한다.
        [Fact]
        public void TrySend_WhenPendingQueueDropsOldest_IncrementsDroppedPendingSendCount()
        {
            const int Capacity = 16;
            const int SendCount = Capacity + 2;

            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
            RefCountedBuffer[] buffers = RentNumberedBuffers(pool, SendCount);
            TestTransport transport = new TestTransport();
            TransportConnection connection = transport.CreateConnection();
            bool publisherRefsReleased = false;

            try
            {
                for (int index = 0; index < buffers.Length; index++)
                {
                    buffers[index].AddRef();
                    Assert.True(transport.TrySend(connection, new TransportSendBuffer(buffers[index], 0, buffers[index].Length)));
                }

                Assert.Equal(2, connection.DroppedPendingSendCount);

                ReleasePublisherRefs(buffers);
                publisherRefsReleased = true;

                connection.Close();

                Assert.Equal(0, pool.RentedCount);
            }
            finally
            {
                if (!publisherRefsReleased)
                    ReleasePublisherRefs(buffers);

                connection.Close();
            }
        }

        // TCP public 진단 누적 테스트: connection 내부 counter 만 있으면 close 된 연결의 drop 이 운영 표면에서 사라진다.
        // Transport 단위 snapshot 은 느린 소비자 drop 을 connection 수명과 무관하게 누적해서 읽을 수 있어야 한다.
        [Fact]
        public void TrySend_WhenPendingQueueDropsOldest_IncrementsTransportDiagnosticsSnapshot()
        {
            const int Capacity = 16;
            const int SendCount = Capacity + 2;

            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
            RefCountedBuffer[] buffers = RentNumberedBuffers(pool, SendCount);
            TestTransport transport = new TestTransport();
            ITransportDiagnostics diagnostics = transport;
            TransportConnection connection = transport.CreateConnection();
            bool publisherRefsReleased = false;

            try
            {
                for (int index = 0; index < buffers.Length; index++)
                {
                    buffers[index].AddRef();
                    Assert.True(transport.TrySend(connection, new TransportSendBuffer(buffers[index], 0, buffers[index].Length)));
                }

                TransportDiagnosticsSnapshot snapshot = diagnostics.GetDiagnosticsSnapshot();

                Assert.Equal(2, snapshot.TcpDroppedPendingSendCount);
                Assert.Equal(0, snapshot.UdpDroppedPendingSendCount);
                Assert.Equal(2, snapshot.DroppedPendingSendCount);

                ReleasePublisherRefs(buffers);
                publisherRefsReleased = true;

                connection.Close();

                TransportDiagnosticsSnapshot afterCloseSnapshot = diagnostics.GetDiagnosticsSnapshot();
                Assert.Equal(2, afterCloseSnapshot.DroppedPendingSendCount);
                Assert.Equal(0, pool.RentedCount);
            }
            finally
            {
                if (!publisherRefsReleased)
                    ReleasePublisherRefs(buffers);

                connection.Close();
            }
        }

        private static RefCountedBuffer[] RentNumberedBuffers(PinnedBlockMemoryPool pool, int count)
        {
            RefCountedBuffer[] buffers = new RefCountedBuffer[count];

            for (int index = 0; index < count; index++)
            {
                RefCountedBuffer buffer = pool.RentCounted();
                buffer.Span[0] = (byte)index;
                buffer.SetLength(1);
                buffers[index] = buffer;
            }

            return buffers;
        }

        private static void ReleasePublisherRefs(RefCountedBuffer[] buffers)
        {
            for (int index = 0; index < buffers.Length; index++)
            {
                buffers[index].Release();
            }
        }

        private sealed class TestTransport : TransportBase
        {
            public override ValueTask<IConnectionListener> ListenTcpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public override ValueTask<IConnection> ConnectTcpAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public override ValueTask StartAsync(CancellationToken cancellationToken = default)
            {
                return default(ValueTask);
            }

            public override ValueTask StopAsync(CancellationToken cancellationToken = default)
            {
                return default(ValueTask);
            }
        }
    }
}
