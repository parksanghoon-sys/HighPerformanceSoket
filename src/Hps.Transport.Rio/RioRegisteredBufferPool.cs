using System;
using System.Collections.Generic;
using Hps.Buffers;

namespace Hps.Transport
{
    /// <summary>
    /// RIO registered buffer id와 pinned block 수명을 함께 소유하는 owner다.
    /// 초기 구현은 native 등록 id 없이 outstanding request 수명 규칙을 먼저 고정한다.
    /// </summary>
    internal sealed class RioRegisteredBufferPool : IDisposable
    {
        private readonly object _gate;
        private readonly PinnedBlockMemoryPool _pool;
        private readonly HashSet<RefCountedBuffer> _outstanding;
        private bool _disposed;

        internal RioRegisteredBufferPool(int blockSize)
        {
            _gate = new object();
            _pool = new PinnedBlockMemoryPool(blockSize);
            _outstanding = new HashSet<RefCountedBuffer>();
        }

        internal int RentedCount => _pool.RentedCount;

        internal RefCountedBuffer RentReceiveBlock()
        {
            lock (_gate)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(RioRegisteredBufferPool));

                RefCountedBuffer buffer = _pool.RentCounted();
                _outstanding.Add(buffer);
                return buffer;
            }
        }

        internal void CompleteRequest(RefCountedBuffer buffer)
        {
            bool shouldRelease;

            lock (_gate)
            {
                shouldRelease = _outstanding.Remove(buffer);
            }

            if (shouldRelease)
                buffer.Release();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
            }
        }
    }
}
