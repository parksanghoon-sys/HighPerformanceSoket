using System;
using Hps.Buffers;
using Hps.Protocol;
using Xunit;

namespace Hps.Protocol.Tests
{
    public sealed class TcpFrameAssemblerTests
    {
        // TCP stream 조립 테스트: header 와 payload 가 여러 receive chunk 로 쪼개져도
        // assembler 는 payload 를 RefCountedBuffer 로 누적 복사하고 완성 시 caller 에 소유권을 넘겨야 한다.
        [Fact]
        public void TryReadFrame_WhenFrameArrivesAcrossChunks_ReturnsOwnedPayloadBuffer()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            TcpFrameAssembler assembler = new TcpFrameAssembler(pool, 8);
            RefCountedBuffer? frame;
            int consumed;

            Assert.Equal(TcpFrameReadStatus.NeedMoreData, assembler.TryReadFrame(new byte[] { 0 }, out consumed, out frame));
            Assert.Equal(1, consumed);
            Assert.Null(frame);
            Assert.Equal(0, pool.RentedCount);

            Assert.Equal(TcpFrameReadStatus.NeedMoreData, assembler.TryReadFrame(new byte[] { 0, 0 }, out consumed, out frame));
            Assert.Equal(2, consumed);
            Assert.Null(frame);
            Assert.Equal(0, pool.RentedCount);

            Assert.Equal(TcpFrameReadStatus.NeedMoreData, assembler.TryReadFrame(new byte[] { 5, 10, 11 }, out consumed, out frame));
            Assert.Equal(3, consumed);
            Assert.Null(frame);
            Assert.Equal(1, pool.RentedCount);

            Assert.Equal(TcpFrameReadStatus.FrameReady, assembler.TryReadFrame(new byte[] { 12, 13, 14 }, out consumed, out frame));
            Assert.Equal(3, consumed);
            Assert.NotNull(frame);

            try
            {
                Assert.Equal(5, frame!.Length);
                Assert.Equal(new byte[] { 10, 11, 12, 13, 14 }, frame.Span.Slice(0, frame.Length).ToArray());
                Assert.Equal(1, pool.RentedCount);
            }
            finally
            {
                frame?.Release();
            }

            Assert.Equal(0, pool.RentedCount);
        }

        // DoS 방어 테스트: wire length 가 maxPayloadLength 를 넘으면 payload buffer 를 대여하지 않고
        // 명시적인 PayloadTooLarge 상태로 반환해야 호출자가 연결 종료 같은 정책을 선택할 수 있다.
        [Fact]
        public void TryReadFrame_WhenPayloadLengthExceedsMax_ReturnsPayloadTooLargeWithoutRentingBuffer()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            TcpFrameAssembler assembler = new TcpFrameAssembler(pool, 4);
            RefCountedBuffer? frame;
            int consumed;

            TcpFrameReadStatus status = assembler.TryReadFrame(new byte[] { 0, 0, 0, 5 }, out consumed, out frame);

            Assert.Equal(TcpFrameReadStatus.PayloadTooLarge, status);
            Assert.Equal(4, consumed);
            Assert.Null(frame);
            Assert.Equal(0, pool.RentedCount);
        }

        // 연결 종료 소유권 테스트: frame payload 조립 중 connection 이 닫히면 assembler 가 들고 있던
        // partial RefCountedBuffer 를 Dispose 경로에서 반환해야 D011 종료 누수 0 계약을 지킬 수 있다.
        [Fact]
        public void Dispose_WhenPayloadAssemblyIsIncomplete_ReleasesPartialPayloadBuffer()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            TcpFrameAssembler assembler = new TcpFrameAssembler(pool, 8);
            RefCountedBuffer? frame;
            int consumed;

            Assert.Equal(TcpFrameReadStatus.NeedMoreData, assembler.TryReadFrame(new byte[] { 0, 0, 0, 4, 1 }, out consumed, out frame));
            Assert.Equal(5, consumed);
            Assert.Null(frame);
            Assert.Equal(1, pool.RentedCount);

            assembler.Dispose();
            assembler.Dispose();

            Assert.Equal(0, pool.RentedCount);
        }
    }
}
