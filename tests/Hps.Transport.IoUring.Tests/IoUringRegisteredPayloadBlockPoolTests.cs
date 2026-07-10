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

        // composite source shape 테스트: registered slot hit 와 fallback miss 정책을 한 source 에 묶어야
        // 상위 protocol/server 는 backend 구현을 몰라도 같은 IRefCountedBufferSource 계약만 사용할 수 있다.
        [Fact]
        public void CompositePayloadBufferSource_WhenInspected_ExposesSourceConstructor()
        {
            Type? registeredPoolType = typeof(IoUringQueue).Assembly.GetType("Hps.Transport.IoUringRegisteredPayloadBlockPool");
            Type? compositeType = typeof(IoUringQueue).Assembly.GetType("Hps.Transport.IoUringCompositePayloadBufferSource");

            Assert.NotNull(registeredPoolType);
            Assert.NotNull(compositeType);
            Assert.True(typeof(IRefCountedBufferSource).IsAssignableFrom(compositeType));
            Assert.NotNull(compositeType!.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new Type[] { registeredPoolType!, typeof(IRefCountedBufferSource) },
                null));
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

        // composite source 테스트: registered slot 이 있으면 registered pool 을 먼저 쓰고,
        // 없을 때만 fallback pool 로 가야 miss fallback 이 명시적이고 관측 가능하다.
        [Fact]
        public void RentCounted_WhenRegisteredPoolIsFull_UsesFallbackSource()
        {
            FakeRegistration registration = new FakeRegistration(1);
            using (IoUringRegisteredPayloadBlockPool registered = IoUringRegisteredPayloadBlockPool.CreateForRegisteredBuffers(16, 1, registration))
            {
                PinnedBlockMemoryPool fallback = new PinnedBlockMemoryPool(16);
                IoUringCompositePayloadBufferSource source = new IoUringCompositePayloadBufferSource(registered, fallback);

                RefCountedBuffer first = source.RentCounted();
                RefCountedBuffer second = source.RentCounted();

                Assert.True(registered.TryGetBufferIndex(first.Memory, out int firstIndex));
                Assert.Equal(0, firstIndex);
                Assert.False(registered.TryGetBufferIndex(second.Memory, out int secondIndex));
                Assert.Equal(-1, secondIndex);
                Assert.Equal(1, fallback.RentedCount);

                first.Release();
                second.Release();
                Assert.Equal(0, fallback.RentedCount);
            }
        }

        // native registration evidence 테스트: Linux capability available 환경에서는 registered payload pool 이
        // 모든 slot 을 io_uring fixed table 에 한 번 등록하고, dispose 에서 unregister owner 를 정리해야 한다.
        [Fact]
        public void Create_WhenLinuxCapabilityAvailable_RegistersAllPayloadBlocks()
        {
            if (IoUringCapabilityProbe.GetStatus() != IoUringCapabilityStatus.Available)
                return;

            using (IoUringQueue queue = IoUringQueue.CreateForProbe(8))
            using (IoUringRegisteredPayloadBlockPool pool = IoUringRegisteredPayloadBlockPool.Create(queue, 16, 2))
            {
                Assert.Equal(2, pool.SlotCount);
                RefCountedBuffer? buffer;
                Assert.True(pool.TryRentCounted(out buffer));
                Assert.True(pool.TryGetBufferIndex(buffer!.Memory, out int index));
                Assert.InRange(index, 0, 1);
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
