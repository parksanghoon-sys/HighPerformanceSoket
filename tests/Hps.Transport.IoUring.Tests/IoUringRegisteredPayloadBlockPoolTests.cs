using System;
using System.Reflection;
using Hps.Buffers;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringRegisteredPayloadBlockPoolTests
    {
        // registered payload pool shape 테스트: TCP publish payload 를 queue lifetime 에 묶인 fixed buffer 로
        // 대여하려면 owner/source 역할과 fixed index lookup surface 가 먼저 존재해야 한다.
        // 타입 생성 전 Red 를 컴파일 실패가 아닌 assertion failure 로 확인하기 위해 reflection 만 사용한다.
        [Fact]
        public void RegisteredPayloadBlockPool_WhenInspected_ExposesOwnerAndLookupSurface()
        {
            Type? poolType = typeof(IoUringQueue).Assembly.GetType("Hps.Transport.IoUringRegisteredPayloadBlockPool");
            Type? registrationType = typeof(IoUringQueue).Assembly.GetType("Hps.Transport.IIoUringFixedBufferRegistration");

            Assert.NotNull(poolType);
            Assert.NotNull(registrationType);
            Assert.True(typeof(IRefCountedBufferOwner).IsAssignableFrom(poolType));
            Assert.NotNull(poolType!.GetMethod(
                "CreateForRegisteredBuffers",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(int), typeof(int), registrationType! },
                null));
            Assert.NotNull(poolType.GetMethod(
                "TryRentCounted",
                BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(poolType.GetMethod(
                "TryGetBufferIndex",
                BindingFlags.Instance | BindingFlags.NonPublic));
        }

        // capacity 테스트: registered payload pool 은 고정 fixed table slot 수만큼만 block 을 대여해야 한다.
        // slot 이 없을 때 hidden allocation fallback 을 만들면 send hot path registration miss 를 숨기므로 false 로 드러낸다.
        [Fact]
        public void TryRentCounted_WhenCapacityIsExhausted_ReturnsFalseWithoutAllocatingFallback()
        {
            FakeRegistration registration = new FakeRegistration(2);
            using (IoUringRegisteredPayloadBlockPool pool = IoUringRegisteredPayloadBlockPool.CreateForRegisteredBuffers(16, 2, registration))
            {
                RefCountedBuffer? first;
                RefCountedBuffer? second;
                RefCountedBuffer? third;

                Assert.True(pool.TryRentCounted(out first));
                Assert.True(pool.TryRentCounted(out second));
                Assert.False(pool.TryRentCounted(out third));
                Assert.Null(third);

                first!.Release();
                second!.Release();
            }
        }

        // slot 재사용 테스트: 마지막 Release 가 registered pool owner 로 돌아가면 같은 slot 을 다시 free 로 만들어야 한다.
        // 이 계약이 깨지면 장시간 publish churn 에서 registered hit path 가 한 번 소진된 뒤 영구적으로 fallback 으로 떨어진다.
        [Fact]
        public void Release_WhenLastReferenceIsReleased_ReturnsSlotForReuse()
        {
            FakeRegistration registration = new FakeRegistration(1);
            using (IoUringRegisteredPayloadBlockPool pool = IoUringRegisteredPayloadBlockPool.CreateForRegisteredBuffers(8, 1, registration))
            {
                RefCountedBuffer? first;
                RefCountedBuffer? second;

                Assert.True(pool.TryRentCounted(out first));
                Assert.True(pool.TryGetBufferIndex(first!.Memory, out int firstIndex));
                Assert.Equal(0, firstIndex);
                first.Release();

                Assert.True(pool.TryRentCounted(out second));
                Assert.True(pool.TryGetBufferIndex(second!.Memory, out int secondIndex));
                Assert.Equal(0, secondIndex);
                second.Release();
            }
        }

        // fixed index lookup 테스트: send helper 는 backing array identity 로 fixed table index 를 찾아야 한다.
        // Memory slice 의 offset 이 달라도 같은 registered block 이면 같은 fixed buffer index 로 조회되어야 한다.
        [Fact]
        public void TryGetBufferIndex_WhenBufferBelongsToPool_ReturnsStableIndex()
        {
            FakeRegistration registration = new FakeRegistration(2);
            using (IoUringRegisteredPayloadBlockPool pool = IoUringRegisteredPayloadBlockPool.CreateForRegisteredBuffers(16, 2, registration))
            {
                RefCountedBuffer? buffer;
                Assert.True(pool.TryRentCounted(out buffer));

                Assert.True(pool.TryGetBufferIndex(buffer!.Memory.Slice(4, 3), out int index));
                Assert.Equal(0, index);

                buffer.Release();
            }
        }

        private sealed class FakeRegistration : IIoUringFixedBufferRegistration
        {
            internal FakeRegistration(int count)
            {
                RegisteredBufferCount = count;
            }

            public int RegisteredBufferCount { get; private set; }

            public void Dispose()
            {
            }
        }
    }
}
