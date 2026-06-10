using System;
using System.Threading;
using Hps.Buffers;
using Xunit;

namespace Hps.Buffers.Tests
{
    public sealed class PinnedBlockMemoryPoolTests
    {
        // 최소 API 계약 테스트: Rent 는 BlockSize 길이의 블록을 돌려주고 RentedCount 를 증가시켜야 하며,
        // Return 은 같은 대여를 반납 처리해 누수 감지 카운트를 0으로 되돌려야 한다.
        [Fact]
        public void RentAndReturn_TrackRentedCountAndBlockSize()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(4096);

            Assert.Equal(4096, pool.BlockSize);
            Assert.Equal(0, pool.RentedCount);

            byte[] block = pool.Rent();

            Assert.Equal(4096, block.Length);
            Assert.Equal(1, pool.RentedCount);

            pool.Return(block);

            Assert.Equal(0, pool.RentedCount);
        }

        // 풀 재사용 계약 테스트: 반납된 블록은 다음 Rent 에서 재사용될 수 있어야 한다.
        // 이 동작은 반복 I/O에서 관리힙 할당을 계속 늘리지 않기 위한 최소 조건이다.
        [Fact]
        public void Rent_AfterReturn_ReusesReturnedBlock()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(128);
            byte[] first = pool.Rent();
            pool.Return(first);

            byte[] second = pool.Rent();

            Assert.Same(first, second);
            Assert.Equal(1, pool.RentedCount);
        }

        // 반환 방어 계약 테스트: 다른 크기의 배열이 섞이면 RIO/io_uring 등록 단위와 풀 불변식이 깨진다.
        // 따라서 Return 은 BlockSize 와 다른 배열을 받아들이지 않아야 하며 카운트도 오염시키면 안 된다.
        [Fact]
        public void Return_WhenBlockSizeDoesNotMatch_ThrowsAndKeepsRentedCount()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);

            ArgumentException exception = Assert.Throws<ArgumentException>(delegate()
            {
                pool.Return(new byte[63]);
            });

            Assert.Contains("BlockSize", exception.Message);
            Assert.Equal(0, pool.RentedCount);
        }

        // 생성자 계약 테스트: 0 이하 크기의 블록은 소켓 I/O 버퍼로 의미가 없고,
        // 이후 Rent 에서 빈 배열을 내보내면 상위 계층이 진행 불가능한 상태가 된다.
        [Fact]
        public void Constructor_WhenBlockSizeIsNotPositive_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(delegate()
            {
                new PinnedBlockMemoryPool(0);
            });
        }

        // 멀티스레드 계약 테스트: 여러 I/O 워커가 동시에 대여/반환하더라도 풀 내부 큐와 RentedCount 가
        // 일관성을 잃지 않아야 한다. 종료 시 RentedCount==0 이 아니면 누수나 카운트 경합이 있다는 뜻이다.
        [Fact]
        public void RentAndReturn_WhenCalledFromMultipleThreads_FinishesWithNoLeaks()
        {
            const int workerCount = 8;
            const int iterationsPerWorker = 10_000;
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(256);
            Exception?[] failures = new Exception?[workerCount];
            Thread[] workers = new Thread[workerCount];
            int start = 0;

            for (int workerIndex = 0; workerIndex < workerCount; workerIndex++)
            {
                int capturedWorkerIndex = workerIndex;
                workers[workerIndex] = new Thread(delegate()
                {
                    try
                    {
                        SpinWait spinner = new SpinWait();
                        while (Volatile.Read(ref start) == 0)
                            spinner.SpinOnce();

                        for (int iteration = 0; iteration < iterationsPerWorker; iteration++)
                        {
                            byte[] block = pool.Rent();
                            if (block.Length != pool.BlockSize)
                                throw new InvalidOperationException("대여한 블록 길이는 BlockSize 와 같아야 한다.");

                            block[0] = unchecked((byte)(capturedWorkerIndex + iteration));
                            pool.Return(block);
                        }
                    }
                    catch (Exception ex)
                    {
                        failures[capturedWorkerIndex] = ex;
                    }
                });

                workers[workerIndex].Start();
            }

            Volatile.Write(ref start, 1);

            for (int workerIndex = 0; workerIndex < workers.Length; workerIndex++)
                Assert.True(workers[workerIndex].Join(TimeSpan.FromSeconds(10)), "worker 가 시간 안에 끝나야 한다.");

            for (int workerIndex = 0; workerIndex < failures.Length; workerIndex++)
            {
                if (failures[workerIndex] != null)
                    throw failures[workerIndex]!;
            }

            Assert.Equal(0, pool.RentedCount);
        }
    }
}
