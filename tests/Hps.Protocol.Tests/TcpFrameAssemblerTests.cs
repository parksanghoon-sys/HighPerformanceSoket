using System;
using System.Buffers.Binary;
using System.Collections.Generic;
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

        // 0바이트 payload 경계 테스트: TCP length-prefix 에서 빈 메시지도 합법 frame 이므로
        // payload 복사 없이 즉시 완성하되 caller 가 Release 할 소유권 있는 buffer 를 받아야 한다.
        [Fact]
        public void TryReadFrame_WhenPayloadLengthIsZero_ReturnsEmptyOwnedFrame()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            TcpFrameAssembler assembler = new TcpFrameAssembler(pool, 8);
            RefCountedBuffer? frame;
            int consumed;

            TcpFrameReadStatus status = assembler.TryReadFrame(new byte[] { 0, 0, 0, 0 }, out consumed, out frame);

            Assert.Equal(TcpFrameReadStatus.FrameReady, status);
            Assert.Equal(4, consumed);
            Assert.NotNull(frame);

            try
            {
                Assert.Equal(0, frame!.Length);
                Assert.Equal(1, pool.RentedCount);
            }
            finally
            {
                frame?.Release();
            }

            Assert.Equal(0, pool.RentedCount);
        }

        // caller loop 계약 테스트: 하나의 TCP receive chunk 에 frame 이 여러 개 붙어도
        // TryReadFrame 은 첫 frame 까지만 소비하고, caller 가 remaining slice 로 재호출해 다음 frame 을 읽어야 한다.
        [Fact]
        public void TryReadFrame_WhenMultipleFramesShareOneChunk_ConsumesOnlyFirstFrame()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            TcpFrameAssembler assembler = new TcpFrameAssembler(pool, 8);
            byte[] firstPayload = new byte[] { 1, 2, 3 };
            byte[] secondPayload = new byte[] { 9, 8 };
            byte[] firstFrame = CreateWireFrame(firstPayload);
            byte[] secondFrame = CreateWireFrame(secondPayload);
            byte[] combined = Combine(firstFrame, secondFrame);
            RefCountedBuffer? frame;
            int consumed;

            TcpFrameReadStatus firstStatus = assembler.TryReadFrame(combined, out consumed, out frame);

            Assert.Equal(TcpFrameReadStatus.FrameReady, firstStatus);
            Assert.Equal(firstFrame.Length, consumed);
            AssertFramePayload(pool, frame, firstPayload);

            TcpFrameReadStatus secondStatus = assembler.TryReadFrame(new ReadOnlySpan<byte>(combined, consumed, combined.Length - consumed), out consumed, out frame);

            Assert.Equal(TcpFrameReadStatus.FrameReady, secondStatus);
            Assert.Equal(secondFrame.Length, consumed);
            AssertFramePayload(pool, frame, secondPayload);
            Assert.Equal(0, pool.RentedCount);
        }

        // maxPayload 정확 경계 테스트: 초과는 거부하지만 maxPayloadLength 와 같은 길이는 허용해야
        // 구성에서 정한 최대 메시지 크기(예: 4096B)를 오프바이원 없이 사용할 수 있다.
        [Fact]
        public void TryReadFrame_WhenPayloadLengthEqualsMax_ReturnsFrame()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(8);
            TcpFrameAssembler assembler = new TcpFrameAssembler(pool, 8);
            byte[] payload = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 };
            RefCountedBuffer? frame;
            int consumed;

            TcpFrameReadStatus status = assembler.TryReadFrame(CreateWireFrame(payload), out consumed, out frame);

            Assert.Equal(TcpFrameReadStatus.FrameReady, status);
            Assert.Equal(12, consumed);
            AssertFramePayload(pool, frame, payload);
            Assert.Equal(0, pool.RentedCount);
        }

        // 결정적 fuzz 테스트: header/payload/다중 frame 을 작은 chunk 로 적대적으로 쪼개도
        // consumed 기반 caller loop 가 참조 payload 목록과 같은 순서·내용으로 frame 을 복원해야 한다.
        [Fact]
        public void TryReadFrame_WhenChunksAreFragmentedDeterministically_PreservesAllFramesAndReturnsBuffers()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
            TcpFrameAssembler assembler = new TcpFrameAssembler(pool, 32);
            byte[][] expectedPayloads = CreateDeterministicPayloads(24, 32);
            byte[] stream = CreateWireStream(expectedPayloads);
            int[] chunkPattern = new int[] { 1, 2, 7, 3, 11, 5, 1, 13, 4 };
            List<byte[]> actualPayloads = new List<byte[]>();
            int streamOffset = 0;
            int patternIndex = 0;

            while (streamOffset < stream.Length)
            {
                int chunkLength = Math.Min(chunkPattern[patternIndex % chunkPattern.Length], stream.Length - streamOffset);
                ReadOnlySpan<byte> chunk = new ReadOnlySpan<byte>(stream, streamOffset, chunkLength);
                int chunkOffset = 0;

                while (chunkOffset < chunk.Length)
                {
                    RefCountedBuffer? frame;
                    int consumed;
                    TcpFrameReadStatus status = assembler.TryReadFrame(chunk.Slice(chunkOffset), out consumed, out frame);

                    Assert.True(consumed > 0 || status == TcpFrameReadStatus.FrameReady);
                    chunkOffset += consumed;

                    if (status == TcpFrameReadStatus.NeedMoreData)
                    {
                        Assert.Null(frame);
                        break;
                    }

                    Assert.Equal(TcpFrameReadStatus.FrameReady, status);
                    Assert.NotNull(frame);

                    try
                    {
                        actualPayloads.Add(frame!.Span.Slice(0, frame.Length).ToArray());
                    }
                    finally
                    {
                        frame?.Release();
                    }
                }

                streamOffset += chunkLength;
                patternIndex++;
            }

            Assert.Equal(expectedPayloads.Length, actualPayloads.Count);
            for (int i = 0; i < expectedPayloads.Length; i++)
            {
                Assert.Equal(expectedPayloads[i], actualPayloads[i]);
            }

            Assert.Equal(0, pool.RentedCount);
        }

        // 랜덤 적대적 fuzz 테스트: frame 길이와 receive chunk 길이를 seed 별로 바꿔 header 1바이트 분할,
        // 0바이트 payload, max payload, 한 chunk 안의 다중 frame 을 함께 때린다. 실패 시 seed 로 재현 가능해야 하며,
        // 모든 완성 frame 을 Release 한 뒤 pool 대여 수가 0으로 돌아와야 D010/D011 소유권 경계가 유지된다.
        [Theory]
        [InlineData(0x1234)]
        [InlineData(0x5678)]
        [InlineData(0x10203)]
        [InlineData(0x70809)]
        public void TryReadFrame_WhenChunksAreFragmentedRandomly_PreservesAllFramesAndReturnsBuffers(int seed)
        {
            const int MaxPayloadLength = 48;
            const int FrameCount = 64;

            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(MaxPayloadLength);
            TcpFrameAssembler assembler = new TcpFrameAssembler(pool, MaxPayloadLength);
            Random random = new Random(seed);
            byte[][] expectedPayloads = CreateRandomPayloads(random, FrameCount, MaxPayloadLength);
            byte[] stream = CreateWireStream(expectedPayloads);
            List<byte[]> actualPayloads = new List<byte[]>();
            int streamOffset = 0;

            while (streamOffset < stream.Length)
            {
                int chunkLength = Math.Min(CreateRandomChunkLength(random), stream.Length - streamOffset);
                ReadOnlySpan<byte> chunk = new ReadOnlySpan<byte>(stream, streamOffset, chunkLength);
                int chunkOffset = 0;

                while (chunkOffset < chunk.Length)
                {
                    RefCountedBuffer? frame;
                    int consumed;
                    TcpFrameReadStatus status = assembler.TryReadFrame(chunk.Slice(chunkOffset), out consumed, out frame);

                    Assert.True(consumed > 0 || status == TcpFrameReadStatus.FrameReady);
                    chunkOffset += consumed;

                    if (status == TcpFrameReadStatus.NeedMoreData)
                    {
                        Assert.Null(frame);
                        break;
                    }

                    Assert.Equal(TcpFrameReadStatus.FrameReady, status);
                    Assert.NotNull(frame);

                    try
                    {
                        actualPayloads.Add(frame!.Span.Slice(0, frame.Length).ToArray());
                    }
                    finally
                    {
                        frame?.Release();
                    }
                }

                streamOffset += chunkLength;
            }

            Assert.Equal(expectedPayloads.Length, actualPayloads.Count);
            for (int i = 0; i < expectedPayloads.Length; i++)
            {
                Assert.Equal(expectedPayloads[i], actualPayloads[i]);
            }

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

        // frame payload 내용뿐 아니라 caller 가 받은 소유권을 Release 했을 때 풀 누수가 없어지는지 함께 확인한다.
        private static void AssertFramePayload(PinnedBlockMemoryPool pool, RefCountedBuffer? frame, byte[] expectedPayload)
        {
            Assert.NotNull(frame);

            try
            {
                Assert.Equal(expectedPayload.Length, frame!.Length);
                Assert.Equal(expectedPayload, frame.Span.Slice(0, frame.Length).ToArray());
                Assert.Equal(1, pool.RentedCount);
            }
            finally
            {
                frame?.Release();
            }
        }

        // 테스트 입력은 production wire format 과 같은 4바이트 big-endian length prefix 로 만든다.
        private static byte[] CreateWireFrame(byte[] payload)
        {
            byte[] frame = new byte[4 + payload.Length];
            BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, 4), payload.Length);
            payload.CopyTo(frame, 4);
            return frame;
        }

        // 다중 frame chunk 테스트와 fuzz 테스트가 같은 참조 stream 을 사용하도록 frame 배열을 단일 byte stream 으로 붙인다.
        private static byte[] CreateWireStream(byte[][] payloads)
        {
            int totalLength = 0;
            for (int i = 0; i < payloads.Length; i++)
            {
                totalLength += 4 + payloads[i].Length;
            }

            byte[] stream = new byte[totalLength];
            int offset = 0;
            for (int i = 0; i < payloads.Length; i++)
            {
                byte[] frame = CreateWireFrame(payloads[i]);
                frame.CopyTo(stream, offset);
                offset += frame.Length;
            }

            return stream;
        }

        // 한 receive chunk 에 frame 이 연속으로 붙는 경계를 명확히 만들기 위한 작은 결합 helper 이다.
        private static byte[] Combine(byte[] first, byte[] second)
        {
            byte[] combined = new byte[first.Length + second.Length];
            first.CopyTo(combined, 0);
            second.CopyTo(combined, first.Length);
            return combined;
        }

        // 랜덤 대신 결정적 payload 집합을 써서 실패 시 frame index 와 byte index 를 재현 가능하게 만든다.
        private static byte[][] CreateDeterministicPayloads(int count, int maxPayloadLength)
        {
            byte[][] payloads = new byte[count][];
            for (int frameIndex = 0; frameIndex < payloads.Length; frameIndex++)
            {
                int length = (frameIndex * 7) % (maxPayloadLength + 1);
                byte[] payload = new byte[length];
                for (int byteIndex = 0; byteIndex < payload.Length; byteIndex++)
                {
                    payload[byteIndex] = unchecked((byte)(frameIndex * 31 + byteIndex * 17));
                }

                payloads[frameIndex] = payload;
            }

            return payloads;
        }

        // seed 기반 fuzz 에서도 경계 frame 이 반드시 섞이도록 일부 index 는 0/max 길이로 고정한다.
        private static byte[][] CreateRandomPayloads(Random random, int count, int maxPayloadLength)
        {
            byte[][] payloads = new byte[count][];
            for (int frameIndex = 0; frameIndex < payloads.Length; frameIndex++)
            {
                int length;
                if (frameIndex % 13 == 0)
                    length = 0;
                else if (frameIndex % 11 == 0)
                    length = maxPayloadLength;
                else
                    length = random.Next(0, maxPayloadLength + 1);

                byte[] payload = new byte[length];
                random.NextBytes(payload);
                payloads[frameIndex] = payload;
            }

            return payloads;
        }

        // 작은 chunk 와 큰 chunk 를 섞어 header/payload 분할과 다중 frame 동시 도착을 모두 만들기 위한 길이 선택이다.
        private static int CreateRandomChunkLength(Random random)
        {
            if (random.Next(0, 4) == 0)
                return 1;

            if (random.Next(0, 5) == 0)
                return random.Next(16, 65);

            return random.Next(2, 17);
        }
    }
}
