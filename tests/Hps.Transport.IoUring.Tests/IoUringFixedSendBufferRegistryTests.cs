using System;
using System.Reflection;
using Hps.Buffers;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringFixedSendBufferRegistryTests
    {
        [Fact]
        public void RegistryContract_WhenInspected_ExposesFixedSendLookupSurface()
        {
            // TCP send pump가 send마다 fixed buffer table을 갈아끼우지 않도록,
            // connection-scoped lookup owner의 최소 surface를 reflection으로 먼저 고정한다.
            Type? registryType = typeof(IoUringQueue).Assembly.GetType("Hps.Transport.IoUringFixedSendBufferRegistry");
            Type? slotType = typeof(IoUringQueue).Assembly.GetType("Hps.Transport.IoUringFixedSendBufferSlot");

            Assert.NotNull(registryType);
            Assert.NotNull(slotType);
            Assert.NotNull(registryType!.GetMethod(
                "CreateForRegisteredBuffers",
                BindingFlags.Static | BindingFlags.NonPublic));
            Assert.NotNull(registryType.GetMethod(
                "TryGetSlot",
                BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(slotType!.GetProperty(
                "RegisteredArray",
                BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(slotType.GetProperty(
                "BufferIndex",
                BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(slotType.GetProperty(
                "PayloadOffset",
                BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(slotType.GetProperty(
                "PayloadLength",
                BindingFlags.Instance | BindingFlags.NonPublic));
        }

        [Fact]
        public void Registry_WhenBufferIsRegistered_ReturnsStableBufferIndexAndPayloadRange()
        {
            // 이미 등록된 RefCountedBuffer block은 send마다 다시 등록하지 않고,
            // 같은 fixed buffer index와 현재 payload slice 범위만 lookup해야 한다.
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(8);
            RefCountedBuffer buffer = pool.RentCounted();
            buffer.SetLength(8);

            CountingRegistration registration = new CountingRegistration(1);
            using (IoUringFixedSendBufferRegistry registry = IoUringFixedSendBufferRegistry.CreateForRegisteredBuffers(
                registration,
                new TransportSendBuffer[] { new TransportSendBuffer(buffer, 0, 8) },
                1))
            {
                IoUringFixedSendBufferSlot slot;
                Assert.True(registry.TryGetSlot(new TransportSendBuffer(buffer, 2, 3), out slot));
                Assert.Equal(0, slot.BufferIndex);
                Assert.Equal(2, slot.PayloadOffset);
                Assert.Equal(3, slot.PayloadLength);
                Assert.NotNull(slot.RegisteredArray);
            }

            Assert.Equal(1, registration.DisposeCount);
            buffer.Release();
            Assert.Equal(0, pool.RentedCount);
        }

        [Fact]
        public void Registry_WhenCapacityIsExceeded_ReturnsMissWithoutEvictingExistingSlots()
        {
            // bounded fixed table이 가득 찼을 때 active table을 교체하면 진행 중 WRITE_FIXED와 충돌할 수 있다.
            // 따라서 초과 항목은 miss로 남기고 기존 slot은 유지해 fallback send path가 선택될 수 있어야 한다.
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(8);
            RefCountedBuffer first = pool.RentCounted();
            RefCountedBuffer second = pool.RentCounted();
            first.SetLength(8);
            second.SetLength(8);

            CountingRegistration registration = new CountingRegistration(1);
            using (IoUringFixedSendBufferRegistry registry = IoUringFixedSendBufferRegistry.CreateForRegisteredBuffers(
                registration,
                new TransportSendBuffer[]
                {
                    new TransportSendBuffer(first, 0, 8),
                    new TransportSendBuffer(second, 0, 8)
                },
                1))
            {
                IoUringFixedSendBufferSlot slot;
                Assert.True(registry.TryGetSlot(new TransportSendBuffer(first, 1, 2), out slot));
                Assert.Equal(0, slot.BufferIndex);
                Assert.False(registry.TryGetSlot(new TransportSendBuffer(second, 1, 2), out slot));
            }

            Assert.Equal(1, registration.DisposeCount);
            first.Release();
            second.Release();
            Assert.Equal(0, pool.RentedCount);
        }

        [Fact]
        public void RegistryFactory_WhenInspected_ExposesQueueBasedCreateMethod()
        {
            // production resource wiring이 raw RegisterBuffers를 직접 호출하지 않도록,
            // queue 기반 native registration factory shape를 registry owner 쪽에 고정한다.
            MethodInfo? method = typeof(IoUringFixedSendBufferRegistry).GetMethod(
                "Create",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(IoUringQueue), typeof(TransportSendBuffer[]), typeof(int) },
                null);

            Assert.NotNull(method);
        }

        [Fact]
        public void Registry_WhenLinuxCapabilityAvailable_RegistersPayloadBlockAndReturnsFixedSlot()
        {
            // Linux native path에서 registry owner가 queue-level fixed table에 payload block을 등록하고,
            // 이후 같은 block의 slice를 fixed buffer index로 조회할 수 있는지 검증한다.
            IoUringCapabilityStatus status = IoUringCapabilityProbe.GetStatus();
            if (status != IoUringCapabilityStatus.Available)
                return;

            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(4);
            RefCountedBuffer buffer = pool.RentCounted();
            buffer.Memory.Span[0] = 10;
            buffer.Memory.Span[1] = 20;
            buffer.Memory.Span[2] = 30;
            buffer.Memory.Span[3] = 40;
            buffer.SetLength(4);

            using (IoUringQueue queue = IoUringQueue.CreateForProbe(4))
            using (IoUringFixedSendBufferRegistry registry = IoUringFixedSendBufferRegistry.Create(
                queue,
                new TransportSendBuffer[] { new TransportSendBuffer(buffer, 0, 4) },
                1))
            {
                IoUringFixedSendBufferSlot slot;
                Assert.True(registry.TryGetSlot(new TransportSendBuffer(buffer, 1, 2), out slot));
                Assert.Equal(0, slot.BufferIndex);
                Assert.Equal(1, slot.PayloadOffset);
                Assert.Equal(2, slot.PayloadLength);
                Assert.NotNull(slot.RegisteredArray);
            }

            buffer.Release();
            Assert.Equal(0, pool.RentedCount);
        }

        private sealed class CountingRegistration : IIoUringFixedBufferRegistration
        {
            public CountingRegistration(int registeredBufferCount)
            {
                RegisteredBufferCount = registeredBufferCount;
            }

            public int RegisteredBufferCount { get; private set; }

            public int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
            }
        }
    }
}
