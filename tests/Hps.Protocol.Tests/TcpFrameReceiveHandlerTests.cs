using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Hps.Buffers;
using Hps.Transport;
using Xunit;

namespace Hps.Protocol.Tests
{
    public sealed class TcpFrameReceiveHandlerTests
    {
        // public 계약 테스트: 어댑터는 Transport 의 receive handler 로 바로 등록 가능해야 한다.
        // 그래야 SAEA/RIO/io_uring 백엔드 교체와 무관하게 TCP frame 조립 흐름을 같은 위치에 꽂을 수 있다.
        [Fact]
        public void Contract_WhenConstructed_IsTransportReceiveHandler()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            CapturingFrameHandler frameHandler = new CapturingFrameHandler();
            TcpFrameReceiveHandler receiveHandler = new TcpFrameReceiveHandler(pool, 8, frameHandler);

            Assert.IsAssignableFrom<ITransportReceiveHandler>(receiveHandler);
        }

        // 생성자 경계 테스트: maxPayloadLength 는 풀 블록 하나에 담길 수 있어야 한다.
        // 이 제한이 있어야 완성 frame 하나가 RefCountedBuffer 하나라는 D009/D010 소유권 단위를 유지한다.
        [Fact]
        public void Constructor_WhenMaxPayloadExceedsPoolBlockSize_Throws()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(4);
            CapturingFrameHandler frameHandler = new CapturingFrameHandler();

            Assert.Throws<ArgumentOutOfRangeException>(delegate()
            {
                _ = new TcpFrameReceiveHandler(pool, 5, frameHandler);
            });
        }

        // raw TCP receive chunk 연결 테스트: Transport 는 frame 경계를 보장하지 않으므로
        // 어댑터가 조각난 입력과 한 chunk 안의 다중 frame 을 모두 조립해 상위 frame handler 로 넘겨야 한다.
        [Fact]
        public void OnReceived_WhenChunksContainMultipleFrames_ForwardsFramesInOrder()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            CapturingFrameHandler frameHandler = new CapturingFrameHandler();
            TcpFrameReceiveHandler receiveHandler = new TcpFrameReceiveHandler(pool, 8, frameHandler);
            FakeConnection connection = new FakeConnection();
            byte[] stream = Combine(
                CreateWireFrame(new byte[] { 1, 2, 3 }),
                CreateWireFrame(Array.Empty<byte>()),
                CreateWireFrame(new byte[] { 9 }));

            receiveHandler.OnReceived(connection, new TransportReceiveBuffer(new ReadOnlySpan<byte>(stream, 0, 2)));
            receiveHandler.OnReceived(connection, new TransportReceiveBuffer(new ReadOnlySpan<byte>(stream, 2, stream.Length - 2)));

            Assert.Equal(3, frameHandler.Frames.Count);
            Assert.Same(connection, frameHandler.Frames[0].Connection);
            Assert.Equal(new byte[] { 1, 2, 3 }, frameHandler.Frames[0].Payload);
            Assert.Equal(Array.Empty<byte>(), frameHandler.Frames[1].Payload);
            Assert.Equal(new byte[] { 9 }, frameHandler.Frames[2].Payload);
            Assert.Equal(0, pool.RentedCount);
            Assert.Equal(0, connection.CloseCount);
        }

        // 연결 종료 release 테스트: payload 조립 중인 RefCountedBuffer 는 어댑터가 connection 별 assembler 를 소유하므로
        // Transport 의 OnConnectionClosed 알림에서 Dispose 되어야 D011 종료 누수 0 계약을 지킬 수 있다.
        [Fact]
        public void OnConnectionClosed_WhenPayloadAssemblyIsIncomplete_ReleasesPartialPayload()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            CapturingFrameHandler frameHandler = new CapturingFrameHandler();
            TcpFrameReceiveHandler receiveHandler = new TcpFrameReceiveHandler(pool, 8, frameHandler);
            FakeConnection connection = new FakeConnection();

            receiveHandler.OnReceived(connection, new TransportReceiveBuffer(new byte[] { 0, 0, 0, 4, 1 }));

            Assert.Equal(1, pool.RentedCount);

            receiveHandler.OnConnectionClosed(connection);

            Assert.Equal(0, pool.RentedCount);
            IConnection closedConnection = Assert.Single(frameHandler.ClosedConnections);
            Assert.Same(connection, closedConnection);
        }

        // 과대 payload 정책 테스트: TcpFrameAssembler 는 초과 상태만 보고하고 stream 복구는 하지 않는다.
        // 통합 어댑터는 D010 계약대로 초과를 관측하면 연결을 닫고 상위 계층에 close 를 알려야 한다.
        [Fact]
        public void OnReceived_WhenPayloadLengthExceedsMax_ClosesConnectionWithoutRentingFrame()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            CapturingFrameHandler frameHandler = new CapturingFrameHandler();
            TcpFrameReceiveHandler receiveHandler = new TcpFrameReceiveHandler(pool, 4, frameHandler);
            FakeConnection connection = new FakeConnection();

            receiveHandler.OnReceived(connection, new TransportReceiveBuffer(new byte[] { 0, 0, 0, 5 }));

            Assert.Equal(0, pool.RentedCount);
            Assert.Empty(frameHandler.Frames);
            Assert.Equal(1, connection.CloseCount);
            IConnection closedConnection = Assert.Single(frameHandler.ClosedConnections);
            Assert.Same(connection, closedConnection);
        }

        // close 통지 멱등성 테스트: PayloadTooLarge 경로는 어댑터가 직접 connection 을 닫고 close 를 통지한다.
        // 이후 Transport 구현이 close 알림을 다시 보내더라도 상위 계층에는 connection 별 종료가 한 번만 보여야 한다.
        [Fact]
        public void OnConnectionClosed_AfterPayloadTooLarge_NotifiesFrameHandlerOnlyOnce()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            CapturingFrameHandler frameHandler = new CapturingFrameHandler();
            TcpFrameReceiveHandler receiveHandler = new TcpFrameReceiveHandler(pool, 4, frameHandler);
            FakeConnection connection = new FakeConnection();

            receiveHandler.OnReceived(connection, new TransportReceiveBuffer(new byte[] { 0, 0, 0, 5 }));
            receiveHandler.OnConnectionClosed(connection);

            Assert.Equal(0, pool.RentedCount);
            Assert.Equal(1, connection.CloseCount);
            IConnection closedConnection = Assert.Single(frameHandler.ClosedConnections);
            Assert.Same(connection, closedConnection);
        }

        // frame handler 예외 방어 테스트: OnFrame 이 실패하면 완성 frame 의 소유권이 애매해져 누수되기 쉽다.
        // 어댑터는 예외를 connection 실패로 취급해 frame 을 반환하고 connection 을 닫아 D011 누수 0 경계를 유지해야 한다.
        [Fact]
        public void OnReceived_WhenFrameHandlerThrows_ReleasesFrameAndClosesConnection()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            ThrowingFrameHandler frameHandler = new ThrowingFrameHandler();
            TcpFrameReceiveHandler receiveHandler = new TcpFrameReceiveHandler(pool, 8, frameHandler);
            FakeConnection connection = new FakeConnection();
            byte[] wireFrame = CreateWireFrame(new byte[] { 1, 2, 3 });

            Exception? exception = Record.Exception(delegate()
            {
                receiveHandler.OnReceived(connection, new TransportReceiveBuffer(wireFrame));
            });

            Assert.Equal(0, pool.RentedCount);
            Assert.Equal(1, connection.CloseCount);
            IConnection closedConnection = Assert.Single(frameHandler.ClosedConnections);
            Assert.Same(connection, closedConnection);
            Assert.Null(exception);
        }

        private static byte[] CreateWireFrame(byte[] payload)
        {
            byte[] frame = new byte[4 + payload.Length];
            BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, 4), payload.Length);
            payload.CopyTo(frame, 4);
            return frame;
        }

        private static byte[] Combine(params byte[][] segments)
        {
            int totalLength = 0;
            for (int index = 0; index < segments.Length; index++)
            {
                totalLength += segments[index].Length;
            }

            byte[] combined = new byte[totalLength];
            int offset = 0;
            for (int index = 0; index < segments.Length; index++)
            {
                segments[index].CopyTo(combined, offset);
                offset += segments[index].Length;
            }

            return combined;
        }

        private sealed class FakeConnection : IConnection
        {
            internal int CloseCount { get; private set; }

            public void Close()
            {
                CloseCount++;
            }

            public void Dispose()
            {
                Close();
            }
        }

        private sealed class CapturingFrameHandler : ITcpFrameHandler
        {
            internal CapturingFrameHandler()
            {
                Frames = new List<CapturedFrame>();
                ClosedConnections = new List<IConnection>();
            }

            internal List<CapturedFrame> Frames { get; }

            internal List<IConnection> ClosedConnections { get; }

            public void OnFrame(IConnection connection, RefCountedBuffer frame)
            {
                // OnFrame 진입 시점부터 이 handler 가 frame 소유권을 받는다. 테스트는 payload 를 복사한 뒤 즉시 Release 해 누수 여부를 단언한다.
                byte[] payload = frame.Span.Slice(0, frame.Length).ToArray();
                frame.Release();
                Frames.Add(new CapturedFrame(connection, payload));
            }

            public void OnConnectionClosed(IConnection connection)
            {
                ClosedConnections.Add(connection);
            }
        }

        private sealed class ThrowingFrameHandler : ITcpFrameHandler
        {
            internal ThrowingFrameHandler()
            {
                ClosedConnections = new List<IConnection>();
            }

            internal List<IConnection> ClosedConnections { get; }

            public void OnFrame(IConnection connection, RefCountedBuffer frame)
            {
                throw new InvalidOperationException("테스트용 frame handler 실패");
            }

            public void OnConnectionClosed(IConnection connection)
            {
                ClosedConnections.Add(connection);
            }
        }

        private sealed class CapturedFrame
        {
            internal CapturedFrame(IConnection connection, byte[] payload)
            {
                Connection = connection;
                Payload = payload;
            }

            internal IConnection Connection { get; }

            internal byte[] Payload { get; }
        }
    }
}
