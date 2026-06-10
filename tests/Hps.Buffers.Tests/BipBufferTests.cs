using System;
using System.Collections.Generic;
using System.Threading;
using Hps.Buffers;
using Xunit;

namespace Hps.Buffers.Tests
{
    public sealed class BipBufferTests
    {
        // 1바이트 갭 불변식 때문에 실제로 쓸 수 있는 최대 용량은 Capacity - 1 이어야 한다.
        // 이 테스트는 full/empty 구분용 갭을 실수로 덮어쓰지 않는지 보호한다.
        [Fact]
        public void GetWriteSpan_WhenBufferUsesCapacityMinusOne_ReportsNoMoreWritableSpace()
        {
            BipBuffer buffer = new BipBuffer(8);

            Span<byte> write = buffer.GetWriteSpan();
            Assert.Equal(7, write.Length);
            Fill(write, 0, write.Length);
            buffer.Commit(write.Length);

            Assert.Equal(7, buffer.Count);
            Assert.Equal(0, buffer.GetWriteSpan().Length);

            ReadOnlySpan<byte> read = buffer.GetReadSpan();
            Assert.Equal(7, read.Length);
            AssertBytes(read, 0, 7);
        }

        // M1 회귀 테스트: 꼬리 영역을 물리 끝까지 채운 뒤 모두 소비해 read 가 0으로 돌아와도
        // 빈 버퍼가 다시 쓰기 가능해야 한다. `_write == capacity` 상태 저장으로 인한 교착을 막는다.
        [Fact]
        public void GetWriteSpan_WhenTailCommitReachedPhysicalEndAndBufferBecameEmpty_AllowsWritingAgain()
        {
            BipBuffer buffer = new BipBuffer(8);

            Span<byte> first = buffer.GetWriteSpan();
            Assert.True(first.Length >= 4);
            Fill(first, 0, 4);
            buffer.Commit(4);
            buffer.Consume(4);

            Span<byte> tail = buffer.GetWriteSpan();
            Assert.Equal(4, tail.Length);
            Fill(tail, 4, 4);
            buffer.Commit(4);
            buffer.Consume(4);

            Assert.True(buffer.IsEmpty);
            Assert.True(buffer.GetWriteSpan().Length > 0);
        }

        // 부분 commit 계약 테스트: 생산자가 받은 span 전체가 아니라 실제 commit 한 prefix만 소비자에게
        // 보여야 한다. 미커밋 영역 노출은 이후 프레이밍 파서의 데이터 손상으로 이어진다.
        [Fact]
        public void Commit_WhenOnlyPartOfWriteSpanIsCommitted_ExposesOnlyCommittedPrefix()
        {
            BipBuffer buffer = new BipBuffer(8);

            Span<byte> write = buffer.GetWriteSpan();
            Fill(write, 0, 5);
            buffer.Commit(3);

            ReadOnlySpan<byte> firstRead = buffer.GetReadSpan();
            Assert.Equal(3, firstRead.Length);
            AssertBytes(firstRead, 0, 3);

            buffer.Consume(2);

            ReadOnlySpan<byte> secondRead = buffer.GetReadSpan();
            Assert.Equal(1, secondRead.Length);
            Assert.Equal((byte)2, secondRead[0]);
            Assert.Equal(1, buffer.Count);
        }

        // tail 이 요청 최소 크기를 만족하지 못할 때 front wrap 으로 넘어가더라도,
        // 기존 tail 데이터가 먼저 읽히고 그 다음 front 데이터가 읽히는 watermark 순서를 검증한다.
        [Fact]
        public void GetWriteSpan_WhenTailCannotSatisfyMinimumSize_WrapsToFrontAndPreservesWatermarkOrder()
        {
            BipBuffer buffer = new BipBuffer(8);

            Span<byte> initial = buffer.GetWriteSpan();
            Fill(initial, 0, 6);
            buffer.Commit(6);
            buffer.Consume(5);

            Span<byte> front = buffer.GetWriteSpan(3);
            Assert.Equal(4, front.Length);
            Fill(front, 100, 4);
            buffer.Commit(4);

            ReadOnlySpan<byte> tailRead = buffer.GetReadSpan();
            Assert.Equal(1, tailRead.Length);
            Assert.Equal((byte)5, tailRead[0]);
            buffer.Consume(1);

            ReadOnlySpan<byte> frontRead = buffer.GetReadSpan();
            Assert.Equal(4, frontRead.Length);
            AssertBytes(frontRead, 100, 4);
        }

        // 단일스레드 fuzz: 랜덤 write/read/partial consume 시퀀스를 참조 Queue 와 비교한다.
        // 작은 capacity 와 고정 seed 조합으로 wrap, empty non-zero cursor, watermark 전환 회귀를 재현 가능하게 잡는다.
        [Fact]
        public void FuzzRandomWriteReadSequences_MatchReferenceQueue()
        {
            int[] capacities = new int[] { 2, 3, 4, 8, 17, 64 };
            int[] seeds = new int[] { 0x1234, 0x5678, 0x1357, 0x2468 };

            for (int capacityIndex = 0; capacityIndex < capacities.Length; capacityIndex++)
            {
                for (int seedIndex = 0; seedIndex < seeds.Length; seedIndex++)
                {
                    RunFuzzCase(capacities[capacityIndex], seeds[seedIndex]);
                }
            }
        }

        // M2 회귀 테스트: SPSC 환경에서 소비자가 커밋되지 않은 바이트를 관측하거나 Count 를 음수로
        // 떨어뜨리지 않아야 한다. `_count` 기준 read span clamp 의 동시성 계약을 보호한다.
        [Fact]
        public void SpscStress_DoesNotExposeUncommittedBytesOrDriveCountNegative()
        {
            const int totalBytes = 2_000_000;
            BipBuffer buffer = new BipBuffer(256);
            Exception? producerFailure = null;
            Exception? consumerFailure = null;
            int producerDone = 0;
            int stopRequested = 0;
            int minimumObservedCount = 0;

            Thread producer = new Thread(delegate()
            {
                try
                {
                    int produced = 0;
                    while (produced < totalBytes && Volatile.Read(ref stopRequested) == 0)
                    {
                        Span<byte> write = buffer.GetWriteSpan(1);
                        if (write.Length == 0)
                        {
                            Thread.Yield();
                            continue;
                        }

                        int length = write.Length;
                        int remaining = totalBytes - produced;
                        if (length > remaining)
                            length = remaining;

                        for (int i = 0; i < length; i++)
                            write[i] = unchecked((byte)(produced + i));

                        buffer.Commit(length);
                        produced += length;
                    }
                }
                catch (Exception ex)
                {
                    producerFailure = ex;
                    Volatile.Write(ref stopRequested, 1);
                }
                finally
                {
                    Volatile.Write(ref producerDone, 1);
                }
            });

            Thread consumer = new Thread(delegate()
            {
                try
                {
                    int consumed = 0;
                    while (consumed < totalBytes && Volatile.Read(ref stopRequested) == 0)
                    {
                        ReadOnlySpan<byte> read = buffer.GetReadSpan();
                        if (read.Length == 0)
                        {
                            if (Volatile.Read(ref producerDone) != 0 && buffer.IsEmpty)
                                break;

                            Thread.Yield();
                            continue;
                        }

                        for (int i = 0; i < read.Length; i++)
                        {
                            byte expected = unchecked((byte)(consumed + i));
                            if (read[i] != expected)
                                throw new InvalidOperationException("소비자가 생산 순서와 다른 바이트를 관측했다.");
                        }

                        buffer.Consume(read.Length);
                        consumed += read.Length;

                        int count = buffer.Count;
                        if (count < minimumObservedCount)
                            minimumObservedCount = count;
                    }

                    if (consumed != totalBytes)
                        throw new InvalidOperationException("소비한 전체 바이트 수가 생산 목표와 일치하지 않는다.");
                }
                catch (Exception ex)
                {
                    consumerFailure = ex;
                    Volatile.Write(ref stopRequested, 1);
                }
            });

            producer.Start();
            consumer.Start();
            Assert.True(producer.Join(TimeSpan.FromSeconds(10)), "생산자 스레드가 시간 안에 끝나야 한다.");
            Assert.True(consumer.Join(TimeSpan.FromSeconds(10)), "소비자 스레드가 시간 안에 끝나야 한다.");

            if (producerFailure != null)
                throw producerFailure;
            if (consumerFailure != null)
                throw consumerFailure;

            Assert.Equal(0, buffer.Count);
            Assert.True(minimumObservedCount >= 0);
        }

        private static void Fill(Span<byte> target, int start, int length)
        {
            for (int i = 0; i < length; i++)
                target[i] = unchecked((byte)(start + i));
        }

        // BipBuffer 의 공개 동작을 단순 Queue 모델과 계속 대조해, 내부 커서 상태를 몰라도
        // 바이트 순서와 Count 불변식이 깨지는 지점을 재현 가능한 seed/iteration 으로 남긴다.
        private static void RunFuzzCase(int capacity, int seed)
        {
            const int iterations = 20_000;
            BipBuffer buffer = new BipBuffer(capacity);
            Queue<byte> reference = new Queue<byte>();
            Random random = new Random(seed);
            int nextValue = 0;
            string[] recentOperations = new string[32];

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                bool shouldWrite = reference.Count == 0 || random.Next(2) == 0;

                if (shouldWrite)
                {
                    Span<byte> write = buffer.GetWriteSpan(random.Next(1, capacity));
                    if (write.Length == 0)
                    {
                        RecordOperation(recentOperations, iteration, "write span 0, draining");
                        DrainOnce(buffer, reference, random, capacity, seed, iteration, recentOperations);
                    }
                    else
                    {
                        int bytes = random.Next(1, write.Length + 1);
                        for (int i = 0; i < bytes; i++)
                        {
                            byte value = unchecked((byte)nextValue);
                            write[i] = value;
                            reference.Enqueue(value);
                            nextValue++;
                        }

                        buffer.Commit(bytes);
                        RecordOperation(
                            recentOperations,
                            iteration,
                            "write span " + write.Length + ", commit " + bytes +
                            ", reference.Count=" + reference.Count + ", buffer.Count=" + buffer.Count);
                    }
                }
                else
                {
                    DrainOnce(buffer, reference, random, capacity, seed, iteration, recentOperations);
                }

                Assert.Equal(reference.Count, buffer.Count);
            }

            while (reference.Count > 0)
            {
                DrainOnce(buffer, reference, random, capacity, seed, iterations, recentOperations);
                Assert.Equal(reference.Count, buffer.Count);
            }

            Assert.True(buffer.IsEmpty);
        }

        // fuzz 실패 시 최근 operation 로그를 함께 남겨, 같은 seed 로 재실행하지 않아도
        // 어떤 write/read 전환에서 공개 계약이 깨졌는지 바로 볼 수 있게 한다.
        private static void DrainOnce(
            BipBuffer buffer,
            Queue<byte> reference,
            Random random,
            int capacity,
            int seed,
            int iteration,
            string[] recentOperations)
        {
            ReadOnlySpan<byte> read = buffer.GetReadSpan();
            Assert.True(read.Length <= reference.Count);

            if (read.Length == 0)
            {
                Assert.True(
                    reference.Count == 0,
                    "GetReadSpan 이 빈 span 을 반환했지만 참조 큐에는 데이터가 남아 있다. " +
                    "capacity=" + capacity + ", seed=" + seed + ", iteration=" + iteration +
                    ", reference.Count=" + reference.Count + ", buffer.Count=" + buffer.Count +
                    ", recent=" + FormatRecentOperations(recentOperations, iteration));
                return;
            }

            int bytes = random.Next(1, read.Length + 1);
            for (int i = 0; i < bytes; i++)
                Assert.Equal(reference.Dequeue(), read[i]);

            buffer.Consume(bytes);
            RecordOperation(
                recentOperations,
                iteration,
                "read span " + read.Length + ", consume " + bytes +
                ", reference.Count=" + reference.Count + ", buffer.Count=" + buffer.Count);
        }

        private static void RecordOperation(string[] recentOperations, int iteration, string text)
        {
            recentOperations[iteration % recentOperations.Length] = iteration + ": " + text;
        }

        private static string FormatRecentOperations(string[] recentOperations, int iteration)
        {
            string result = string.Empty;
            int start = iteration - recentOperations.Length + 1;
            if (start < 0) start = 0;

            for (int i = start; i <= iteration; i++)
            {
                string item = recentOperations[i % recentOperations.Length];
                if (item == null)
                    continue;

                if (result.Length != 0)
                    result += " | ";

                result += item;
            }

            return result;
        }

        private static void AssertBytes(ReadOnlySpan<byte> actual, int start, int length)
        {
            Assert.Equal(length, actual.Length);
            for (int i = 0; i < length; i++)
                Assert.Equal(unchecked((byte)(start + i)), actual[i]);
        }
    }
}
