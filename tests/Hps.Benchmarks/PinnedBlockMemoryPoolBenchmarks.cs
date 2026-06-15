using System;
using BenchmarkDotNet.Attributes;
using Hps.Buffers;

namespace Hps.Benchmarks
{
    /// <summary>
    /// Phase 4의 첫 microbench 이다.
    ///
    /// broker 부하 하니스는 이후 TCP end-to-end 지연을 보겠지만, 그 전에 fan-out 소유권의 기반인
    /// pinned counted buffer 대여/반환 비용을 분리해서 기록해야 Transport/Protocol 변경의 영향을 구분할 수 있다.
    /// </summary>
    [MemoryDiagnoser]
    public class PinnedBlockMemoryPoolBenchmarks
    {
        private PinnedBlockMemoryPool? _pool;

        [Params(BenchmarkTargets.PayloadBytes)]
        public int BlockSize { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _pool = new PinnedBlockMemoryPool(BlockSize);
        }

        [Benchmark(Description = "RentCounted + Release")]
        public void RentCountedAndRelease()
        {
            PinnedBlockMemoryPool pool = GetPool();
            RefCountedBuffer buffer = pool.RentCounted();
            buffer.Release();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            PinnedBlockMemoryPool pool = GetPool();
            if (pool.RentedCount != 0)
                throw new InvalidOperationException("벤치마크 종료 후 반환되지 않은 pooled buffer 가 남아 있다.");
        }

        private PinnedBlockMemoryPool GetPool()
        {
            if (_pool == null)
                throw new InvalidOperationException("BenchmarkDotNet GlobalSetup 이 먼저 실행되어야 한다.");

            return _pool;
        }
    }
}
