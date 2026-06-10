using System;
using Hps.Buffers;
using Xunit;

namespace Hps.Buffers.Tests
{
    public sealed class RefCountedBufferTests
    {
        // 최소 수명 계약 테스트: counted buffer 는 pinned pool 에서 블록을 하나 대여하고,
        // 마지막 Release 에서 그 블록을 정확히 반환해야 한다. Length 는 유효 payload 길이만 표시하고
        // Span/Memory 는 TCP 복사 대상 및 UDP 직접 recv 대상이 될 수 있도록 전체 블록을 노출해야 한다.
        [Fact]
        public void RentCounted_ProvidesWritableMemoryAndReturnsOnFinalRelease()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
            RefCountedBuffer buffer = pool.RentCounted();

            Assert.Equal(1, pool.RentedCount);
            Assert.Equal(32, buffer.Memory.Length);
            Assert.Equal(32, buffer.Span.Length);
            Assert.Equal(0, buffer.Length);

            buffer.SetLength(7);
            buffer.Span[0] = 0x5A;

            Assert.Equal(7, buffer.Length);
            Assert.Equal(0x5A, buffer.Memory.Span[0]);

            buffer.Release();

            Assert.Equal(0, pool.RentedCount);
        }

        // 팬아웃 수명 계약 테스트: publish 가드 ref 와 구독자별 AddRef 가 균형을 이룰 때,
        // 마지막 Release 전까지 풀에 반환되면 안 되고 마지막 Release 에서만 누수 없이 반환되어야 한다.
        [Fact]
        public void AddRefAndRelease_WhenBalanced_ReturnsOnlyAfterLastRelease()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            RefCountedBuffer buffer = pool.RentCounted();

            buffer.AddRef();
            buffer.AddRef();

            buffer.Release();
            Assert.Equal(1, pool.RentedCount);

            buffer.Release();
            Assert.Equal(1, pool.RentedCount);

            buffer.Release();
            Assert.Equal(0, pool.RentedCount);
        }

        // 과다 반환 방어 테스트: 이미 0에 도달해 풀로 돌아간 버퍼를 다시 Release 하면
        // 참조계수 음수나 이중 반환으로 이어지므로 즉시 계약 위반 예외로 드러나야 한다.
        [Fact]
        public void Release_WhenCalledAfterFinalRelease_ThrowsAndDoesNotCorruptPool()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(8);
            RefCountedBuffer buffer = pool.RentCounted();

            buffer.Release();

            Assert.Throws<InvalidOperationException>(delegate()
            {
                buffer.Release();
            });
            Assert.Equal(0, pool.RentedCount);
        }

        // 부활 방어 테스트: D006 계약상 AddRef 는 어떤 Release 가 0에 도달하기 전에 끝나야 한다.
        // 반환된 버퍼를 다시 AddRef 할 수 있으면 use-after-free 가 되므로 반드시 거부해야 한다.
        [Fact]
        public void AddRef_WhenCalledAfterFinalRelease_Throws()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(8);
            RefCountedBuffer buffer = pool.RentCounted();

            buffer.Release();

            Assert.Throws<InvalidOperationException>(delegate()
            {
                buffer.AddRef();
            });
            Assert.Equal(0, pool.RentedCount);
        }

        // 길이 경계 테스트: Length 는 payload 유효 범위이므로 음수나 블록 용량 초과 값을 허용하면
        // 이후 send view 또는 프레임 조립이 잘못된 범위를 보게 된다.
        [Fact]
        public void SetLength_WhenOutOfRange_ThrowsAndKeepsPreviousLength()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(4);
            RefCountedBuffer buffer = pool.RentCounted();

            buffer.SetLength(2);

            Assert.Throws<ArgumentOutOfRangeException>(delegate()
            {
                buffer.SetLength(5);
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate()
            {
                buffer.SetLength(-1);
            });
            Assert.Equal(2, buffer.Length);

            buffer.Release();
            Assert.Equal(0, pool.RentedCount);
        }
    }
}
