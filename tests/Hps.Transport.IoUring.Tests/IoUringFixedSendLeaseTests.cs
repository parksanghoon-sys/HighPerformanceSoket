using System;
using System.Reflection;
using Hps.Buffers;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringFixedSendLeaseTests
    {
        [Fact]
        public void LeaseContract_WhenInspected_ExposesPureOwnershipSurface()
        {
            // Red 단계가 컴파일 실패가 아니라 명확한 assertion failure 가 되도록 reflection 으로 contract surface 를 먼저 고정한다.
            Type? leaseType = typeof(IoUringQueue).Assembly.GetType("Hps.Transport.IoUringFixedSendLease");
            Type? registrationType = typeof(IoUringQueue).Assembly.GetType("Hps.Transport.IIoUringFixedBufferRegistration");

            Assert.NotNull(leaseType);
            Assert.NotNull(registrationType);
            Assert.NotNull(leaseType!.GetMethod("CreateForRegisteredBuffer", BindingFlags.Static | BindingFlags.NonPublic));
            Assert.NotNull(leaseType.GetProperty("RegisteredArray", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(leaseType.GetProperty("BufferIndex", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(leaseType.GetProperty("PayloadOffset", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(leaseType.GetProperty("PayloadLength", BindingFlags.Instance | BindingFlags.NonPublic));
        }

        [Fact]
        public void Lease_WhenDisposed_ReleasesPayloadRefAndRegistrationOnce()
        {
            // fixed-write pump 연결 전, lease 가 Transport 소유 payload ref 와 registration owner 를
            // dispose 중 정확히 한 번만 정리하는지 검증한다.
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(8);
            RefCountedBuffer buffer = pool.RentCounted();
            buffer.Memory.Span.Slice(0, 4).Fill(7);
            buffer.SetLength(4);
            buffer.AddRef();

            CountingRegistration registration = new CountingRegistration();
            IoUringFixedSendLease lease = IoUringFixedSendLease.CreateForRegisteredBuffer(
                new TransportSendBuffer(buffer, 1, 2),
                registration);

            Assert.Equal(1, pool.RentedCount);
            Assert.Equal(0, lease.BufferIndex);
            Assert.Equal(1, lease.PayloadOffset);
            Assert.Equal(2, lease.PayloadLength);
            Assert.NotNull(lease.RegisteredArray);

            lease.Dispose();
            lease.Dispose();

            Assert.Equal(1, registration.DisposeCount);
            Assert.Equal(1, pool.RentedCount);

            buffer.Release();
            Assert.Equal(0, pool.RentedCount);
        }

        [Fact]
        public void Lease_WhenSendBufferUsesSlice_ExposesUnderlyingArrayAndRange()
        {
            // WRITE_FIXED 에 넘길 pointer offset 은 RefCountedBuffer 전체 배열 기준 offset 이어야 하므로,
            // payload slice metadata 가 lease surface 에 그대로 드러나는지 검증한다.
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(8);
            RefCountedBuffer buffer = pool.RentCounted();
            buffer.Memory.Span[0] = 10;
            buffer.Memory.Span[1] = 20;
            buffer.Memory.Span[2] = 30;
            buffer.Memory.Span[3] = 40;
            buffer.SetLength(4);
            buffer.AddRef();

            CountingRegistration registration = new CountingRegistration();
            IoUringFixedSendLease lease = IoUringFixedSendLease.CreateForRegisteredBuffer(
                new TransportSendBuffer(buffer, 1, 2),
                registration);

            Assert.NotNull(lease.RegisteredArray);
            Assert.Equal(1, lease.PayloadOffset);
            Assert.Equal(2, lease.PayloadLength);

            lease.Dispose();
            buffer.Release();
        }

        [Fact]
        public void LeaseFactory_WhenInspected_ExposesQueueBasedCreateMethod()
        {
            // 다음 native evidence task 가 production helper 를 우회하지 않도록 queue 기반 factory shape 를 고정한다.
            MethodInfo? method = typeof(IoUringFixedSendLease).GetMethod(
                "Create",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(IoUringQueue), typeof(TransportSendBuffer) },
                null);

            Assert.NotNull(method);
        }

        private sealed class CountingRegistration : IIoUringFixedBufferRegistration
        {
            public int DisposeCount { get; private set; }

            public int RegisteredBufferCount
            {
                get { return 1; }
            }

            public void Dispose()
            {
                DisposeCount++;
            }
        }
    }
}
