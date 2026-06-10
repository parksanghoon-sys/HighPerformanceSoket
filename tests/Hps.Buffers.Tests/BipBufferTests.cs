using System;
using System.Threading;
using Hps.Buffers;
using Xunit;

namespace Hps.Buffers.Tests
{
    public sealed class BipBufferTests
    {
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
    }
}
