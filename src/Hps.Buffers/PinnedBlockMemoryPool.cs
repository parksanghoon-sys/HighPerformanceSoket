using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Hps.Buffers
{
    /// <summary>
    /// 고정 크기 byte 배열 블록을 대여/반환하는 풀이다.
    ///
    /// 모든 I/O 버퍼는 GC 이동으로부터 안전해야 하므로 새 블록은 POH pinned 배열로 생성한다.
    /// 상위 계층은 <see cref="Rent"/> 로 받은 블록을 사용한 뒤 반드시 <see cref="Return"/> 으로
    /// 반납해야 하며, <see cref="RentedCount"/> 는 테스트와 종료 경로에서 누수 여부를 확인하는
    /// 관측 지점이다.
    ///
    /// 동시성: 여러 스레드가 동시에 Rent/Return 할 수 있다. 풀 내부 캐시는 <see cref="ConcurrentQueue{T}"/>
    /// 로 공유하고, 대여 카운트는 Interlocked/Volatile 로 갱신한다.
    /// </summary>
    public sealed class PinnedBlockMemoryPool
    {
        private readonly ConcurrentQueue<byte[]> _available;
        private int _rentedCount;

        /// <summary>
        /// 새 풀을 만든다.
        /// </summary>
        /// <param name="blockSize">모든 블록의 고정 크기. 1 이상이어야 한다.</param>
        public PinnedBlockMemoryPool(int blockSize)
        {
            if (blockSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(blockSize), "blockSize 는 1 이상이어야 한다.");

            BlockSize = blockSize;
            _available = new ConcurrentQueue<byte[]>();
        }

        /// <summary>이 풀에서 대여하는 모든 블록의 바이트 길이.</summary>
        public int BlockSize { get; }

        /// <summary>
        /// 현재 대여 중인 블록 수. 정상 종료 후 0이어야 하며, 테스트에서는 누수 감지 지표로 사용한다.
        /// </summary>
        public int RentedCount => Volatile.Read(ref _rentedCount);

        /// <summary>
        /// 고정 크기 블록을 하나 대여한다. 캐시에 반납된 블록이 없으면 POH pinned 배열을 새로 만든다.
        /// </summary>
        public byte[] Rent()
        {
            byte[]? cached;
            byte[] block = _available.TryDequeue(out cached) && cached != null
                ? cached
                : GC.AllocateUninitializedArray<byte>(BlockSize, pinned: true);

            Interlocked.Increment(ref _rentedCount);
            return block;
        }

        /// <summary>
        /// 대여했던 블록을 풀에 반납한다. 다른 크기의 배열은 풀 불변식을 깨므로 거부한다.
        /// </summary>
        public void Return(byte[] block)
        {
            if (block == null)
                throw new ArgumentNullException(nameof(block));
            if (block.Length != BlockSize)
                throw new ArgumentException("반납 블록의 길이가 Pool BlockSize 와 일치해야 한다.", nameof(block));

            DecrementRentedCount();
            _available.Enqueue(block);
        }

        private void DecrementRentedCount()
        {
            while (true)
            {
                int current = Volatile.Read(ref _rentedCount);
                if (current == 0)
                    throw new InvalidOperationException("대여 중인 블록이 없는데 Return 이 호출됐다.");

                if (Interlocked.CompareExchange(ref _rentedCount, current - 1, current) == current)
                    return;
            }
        }
    }
}
