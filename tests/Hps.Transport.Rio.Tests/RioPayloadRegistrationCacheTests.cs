using System;
using Xunit;

namespace Hps.Transport.Rio.Tests
{
    public sealed class RioPayloadRegistrationCacheTests
    {
        // 같은 backing byte[]는 같은 native buffer id 를 재사용해야 한다.
        // 이 테스트는 payload send 두 번이 register/deregister 두 번으로 퇴행하지 않도록 cache hit 계약을 고정한다.
        [Fact]
        public void Acquire_WhenSameBlockIsReused_RegistersOnlyOnce()
        {
            RecordingRegistrar registrar = new RecordingRegistrar();
            using (RioPayloadRegistrationCache cache = new RioPayloadRegistrationCache(registrar, capacity: 4))
            {
                byte[] block = new byte[16];

                using (RioPayloadRegistrationCache.RioPayloadBufferLease first = cache.Acquire(block))
                using (RioPayloadRegistrationCache.RioPayloadBufferLease second = cache.Acquire(block))
                {
                    Assert.Equal(first.BufferId, second.BufferId);
                }

                Assert.Equal(1, registrar.RegisterCallCount);
                Assert.Equal(0, registrar.DeregisterCallCount);
                Assert.Equal(1, cache.CachedCount);
            }

            Assert.Equal(1, registrar.DeregisterCallCount);
        }

        // cache capacity 를 넘으면 outstanding lease 가 없는 가장 오래된 entry 를 해제해야 한다.
        // 이렇게 해야 장시간 실행 중 registered memory footprint 가 무한히 증가하지 않는다.
        [Fact]
        public void Acquire_WhenCapacityIsExceeded_EvictsIdleOldestEntry()
        {
            RecordingRegistrar registrar = new RecordingRegistrar();
            using (RioPayloadRegistrationCache cache = new RioPayloadRegistrationCache(registrar, capacity: 1))
            {
                byte[] firstBlock = new byte[16];
                byte[] secondBlock = new byte[16];

                cache.Acquire(firstBlock).Dispose();
                cache.Acquire(secondBlock).Dispose();

                Assert.Equal(2, registrar.RegisterCallCount);
                Assert.Equal(1, registrar.DeregisterCallCount);
                Assert.Equal(1, cache.CachedCount);
            }

            Assert.Equal(2, registrar.DeregisterCallCount);
        }

        // outstanding send 가 있는 entry 는 cache dispose 시점에 바로 deregister 하면 안 된다.
        // 마지막 lease release 가 들어올 때까지 deregister 를 지연해야 RIO outstanding request 계약을 지킨다.
        [Fact]
        public void Dispose_WhenLeaseIsOutstanding_DeregistersAfterLeaseRelease()
        {
            RecordingRegistrar registrar = new RecordingRegistrar();
            RioPayloadRegistrationCache.RioPayloadBufferLease lease;
            using (RioPayloadRegistrationCache cache = new RioPayloadRegistrationCache(registrar, capacity: 4))
            {
                lease = cache.Acquire(new byte[16]);
                cache.Dispose();

                Assert.Equal(0, registrar.DeregisterCallCount);
            }

            lease.Dispose();

            Assert.Equal(1, registrar.DeregisterCallCount);
        }

        // capacity 가 가득 찼고 모든 entry 가 outstanding 이면 unsafe eviction 대신 per-operation fallback lease 를 쓴다.
        // fallback lease 는 release 때 바로 deregister 되어 cache entry 로 남지 않는다.
        [Fact]
        public void Acquire_WhenCapacityIsFullAndAllEntriesAreOutstanding_UsesUncachedLease()
        {
            RecordingRegistrar registrar = new RecordingRegistrar();
            using (RioPayloadRegistrationCache cache = new RioPayloadRegistrationCache(registrar, capacity: 1))
            {
                RioPayloadRegistrationCache.RioPayloadBufferLease cached = cache.Acquire(new byte[16]);
                RioPayloadRegistrationCache.RioPayloadBufferLease fallback = cache.Acquire(new byte[16]);

                Assert.Equal(2, registrar.RegisterCallCount);
                Assert.Equal(1, cache.CachedCount);

                fallback.Dispose();
                Assert.Equal(1, registrar.DeregisterCallCount);

                cached.Dispose();
            }

            Assert.Equal(2, registrar.DeregisterCallCount);
        }

        private sealed class RecordingRegistrar : IRioBufferRegistrar
        {
            private int _next;

            internal int RegisterCallCount { get; private set; }

            internal int DeregisterCallCount { get; private set; }

            public IntPtr Register(byte[] block)
            {
                RegisterCallCount++;
                _next++;
                return new IntPtr(_next);
            }

            public void Deregister(IntPtr bufferId)
            {
                DeregisterCallCount++;
            }
        }
    }
}
