using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Hps.Transport
{
    internal interface IRioBufferRegistrar
    {
        IntPtr Register(byte[] block);

        void Deregister(IntPtr bufferId);
    }

    internal sealed class RioPayloadRegistrationCache : IDisposable
    {
        private readonly object _gate;
        private readonly IRioBufferRegistrar _registrar;
        private readonly Dictionary<byte[], Entry> _entries;
        private readonly int _capacity;
        private long _tick;
        private bool _disposed;

        internal RioPayloadRegistrationCache(IRioBufferRegistrar registrar, int capacity)
        {
            if (registrar == null)
                throw new ArgumentNullException(nameof(registrar));
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "RIO payload registration cache capacity 는 1 이상이어야 합니다.");

            _gate = new object();
            _registrar = registrar;
            _capacity = capacity;
            _entries = new Dictionary<byte[], Entry>(ReferenceByteArrayComparer.Instance);
        }

        internal int CachedCount
        {
            get
            {
                lock (_gate)
                {
                    return _entries.Count;
                }
            }
        }

        internal RioPayloadBufferLease Acquire(byte[] block)
        {
            if (block == null)
                throw new ArgumentNullException(nameof(block));

            // idle entry 선택과 dictionary 제거는 cache lock 으로 직렬화하지만,
            // 실제 native deregister 는 OS 호출이므로 정상 경로에서는 lock 밖으로 밀어낸다.
            IntPtr evictedBufferId = IntPtr.Zero;
            RioPayloadBufferLease lease;
            lock (_gate)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(RioPayloadRegistrationCache));

                Entry? entry;
                if (_entries.TryGetValue(block, out entry))
                {
                    entry.OutstandingLeaseCount++;
                    entry.LastUsedTick = NextTick();
                    return RioPayloadBufferLease.CreateCached(this, entry.BufferId, entry);
                }

                if (_entries.Count >= _capacity && !TryEvictIdleEntry(out evictedBufferId))
                {
                    IntPtr fallbackBufferId = _registrar.Register(block);
                    return RioPayloadBufferLease.CreateUncached(_registrar, fallbackBufferId);
                }

                IntPtr bufferId;
                try
                {
                    bufferId = _registrar.Register(block);
                }
                catch
                {
                    // 새 registration 이 실패하면 이미 cache 에서 제거한 idle entry 를 여기서 정리한다.
                    // 이 예외 경로는 드물지만, 방치하면 unreachable native registration 이 남는다.
                    if (evictedBufferId != IntPtr.Zero)
                    {
                        _registrar.Deregister(evictedBufferId);
                        evictedBufferId = IntPtr.Zero;
                    }

                    throw;
                }

                entry = new Entry(block, bufferId, NextTick());
                entry.OutstandingLeaseCount = 1;
                _entries.Add(block, entry);
                lease = RioPayloadBufferLease.CreateCached(this, bufferId, entry);
            }

            if (evictedBufferId != IntPtr.Zero)
                _registrar.Deregister(evictedBufferId);

            return lease;
        }

        public void Dispose()
        {
            List<IntPtr> idleBufferIds = new List<IntPtr>();

            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;

                List<byte[]> idleKeys = new List<byte[]>();
                foreach (KeyValuePair<byte[], Entry> item in _entries)
                {
                    Entry entry = item.Value;
                    if (entry.OutstandingLeaseCount == 0)
                    {
                        idleKeys.Add(item.Key);
                        idleBufferIds.Add(entry.BufferId);
                    }
                    else
                    {
                        entry.DeregisterWhenIdle = true;
                    }
                }

                for (int i = 0; i < idleKeys.Count; i++)
                    _entries.Remove(idleKeys[i]);
            }

            DeregisterAll(idleBufferIds);
        }

        private void ReleaseCached(Entry entry)
        {
            IntPtr bufferIdToDeregister = IntPtr.Zero;

            lock (_gate)
            {
                if (entry.OutstandingLeaseCount <= 0)
                    throw new InvalidOperationException("RIO payload registration lease 가 이미 반환되었습니다.");

                entry.OutstandingLeaseCount--;
                if (entry.OutstandingLeaseCount == 0 && entry.DeregisterWhenIdle)
                {
                    if (_entries.Remove(entry.Block))
                        bufferIdToDeregister = entry.BufferId;
                }
            }

            if (bufferIdToDeregister != IntPtr.Zero)
                _registrar.Deregister(bufferIdToDeregister);
        }

        private bool TryEvictIdleEntry(out IntPtr evictedBufferId)
        {
            evictedBufferId = IntPtr.Zero;
            Entry? oldest = null;
            foreach (KeyValuePair<byte[], Entry> item in _entries)
            {
                Entry entry = item.Value;
                if (entry.OutstandingLeaseCount != 0)
                    continue;

                if (oldest == null || entry.LastUsedTick < oldest.LastUsedTick)
                    oldest = entry;
            }

            if (oldest == null)
                return false;

            _entries.Remove(oldest.Block);
            evictedBufferId = oldest.BufferId;
            return true;
        }

        private long NextTick()
        {
            _tick++;
            return _tick;
        }

        private void DeregisterAll(List<IntPtr> bufferIds)
        {
            for (int i = 0; i < bufferIds.Count; i++)
                _registrar.Deregister(bufferIds[i]);
        }

        internal sealed class Entry
        {
            internal Entry(byte[] block, IntPtr bufferId, long lastUsedTick)
            {
                Block = block;
                BufferId = bufferId;
                LastUsedTick = lastUsedTick;
            }

            internal byte[] Block { get; }

            internal IntPtr BufferId { get; }

            internal int OutstandingLeaseCount;

            internal long LastUsedTick;

            internal bool DeregisterWhenIdle;
        }

        private sealed class ReferenceByteArrayComparer : IEqualityComparer<byte[]>
        {
            internal static readonly ReferenceByteArrayComparer Instance = new ReferenceByteArrayComparer();

            private ReferenceByteArrayComparer()
            {
            }

            public bool Equals(byte[]? x, byte[]? y)
            {
                return object.ReferenceEquals(x, y);
            }

            public int GetHashCode(byte[] obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        internal sealed class RioPayloadBufferLease : IDisposable
        {
            private readonly RioPayloadRegistrationCache? _cache;
            private readonly IRioBufferRegistrar? _registrar;
            private readonly Entry? _entry;
            private int _disposed;

            private RioPayloadBufferLease(
                IntPtr bufferId,
                RioPayloadRegistrationCache? cache,
                IRioBufferRegistrar? registrar,
                Entry? entry)
            {
                BufferId = bufferId;
                _cache = cache;
                _registrar = registrar;
                _entry = entry;
            }

            internal IntPtr BufferId { get; }

            internal static RioPayloadBufferLease CreateCached(RioPayloadRegistrationCache cache, IntPtr bufferId, Entry entry)
            {
                return new RioPayloadBufferLease(bufferId, cache, null, entry);
            }

            internal static RioPayloadBufferLease CreateUncached(IRioBufferRegistrar registrar, IntPtr bufferId)
            {
                return new RioPayloadBufferLease(bufferId, null, registrar, null);
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;

                if (_cache != null && _entry != null)
                {
                    _cache.ReleaseCached(_entry);
                    return;
                }

                if (_registrar != null)
                    _registrar.Deregister(BufferId);
            }
        }
    }
}
