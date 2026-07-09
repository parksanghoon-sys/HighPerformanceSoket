using System;
using System.Threading;
using System.Threading.Tasks;
using Hps.Buffers;
using Xunit;

namespace Hps.Buffers.Tests
{
    public sealed class RefCountedBufferTests
    {
        // owner/source abstraction shape 테스트: registered payload pool 같은 다른 owner 가 RefCountedBuffer 를
        // 만들 수 있으려면 Buffers assembly 에 owner/source 계약과 public owner constructor 가 먼저 있어야 한다.
        // 새 타입을 직접 참조하지 않고 reflection 으로 확인해 Red 가 컴파일 실패가 아니라 assertion failure 로 드러나게 한다.
        [Fact]
        public void RefCountedBuffer_WhenInspected_ExposesOwnerAndSourceAbstractionShape()
        {
            Type assemblyMarker = typeof(RefCountedBuffer);
            Type? ownerType = assemblyMarker.Assembly.GetType("Hps.Buffers.IRefCountedBufferOwner");
            Type? sourceType = assemblyMarker.Assembly.GetType("Hps.Buffers.IRefCountedBufferSource");

            Assert.NotNull(ownerType);
            Assert.NotNull(sourceType);
            Assert.NotNull(typeof(RefCountedBuffer).GetConstructor(new Type[] { ownerType!, typeof(byte[]) }));
            Assert.True(ownerType!.IsAssignableFrom(typeof(PinnedBlockMemoryPool)));
            Assert.True(sourceType!.IsAssignableFrom(typeof(PinnedBlockMemoryPool)));
        }

        // owner abstraction 동작 테스트: RefCountedBuffer 가 concrete pool 이 아니라 owner interface 로
        // 마지막 반환을 수행해야 io_uring registered slot owner 도 같은 Release 계약을 재사용할 수 있다.
        [Fact]
        public void Release_WhenConstructedWithOwnerInterface_ReturnsBlockToOwnerExactlyOnce()
        {
            TestBufferOwner owner = new TestBufferOwner(16);
            byte[] block = new byte[16];
            RefCountedBuffer buffer = new RefCountedBuffer(owner, block);

            buffer.AddRef();
            buffer.Release();
            Assert.Equal(0, owner.ReturnCount);

            buffer.Release();
            Assert.Equal(1, owner.ReturnCount);
            Assert.Same(block, owner.ReturnedBlock);
        }

        // source abstraction 동작 테스트: 기존 pinned pool 도 source 로 동작해야
        // protocol assembler 의 기존 경로와 새 source 주입 경로가 같은 counted buffer 계약을 공유한다.
        [Fact]
        public void PinnedBlockMemoryPool_WhenUsedAsBufferSource_RentsCountedBuffer()
        {
            IRefCountedBufferSource source = new PinnedBlockMemoryPool(32);

            RefCountedBuffer buffer = source.RentCounted();

            Assert.Equal(32, source.BlockSize);
            Assert.Equal(32, buffer.Memory.Length);
            buffer.Release();
        }

        // 최소 수명 계약 테스트: counted buffer 는 pinned pool 에서 블록을 하나 대여하고,
        // 마지막 Release 에서 그 블록을 정확히 반환해야 한다. Length 는 유효 payload 길이만 표시하고
        // Span/Memory 는 TCP 복사 대상 및 UDP 직접 recv 대상이 될 수 있도록 전체 블록을 노출해야 한다.
        [Fact]
        public void RentCounted_ProvidesWritableMemoryAndReturnsOnFinalRelease()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
            RefCountedBuffer buffer = pool.RentCounted();

            Assert.Equal(1, pool.RentedCount);
            Assert.Equal(32, buffer.Memory.Length);
            Assert.Equal(32, buffer.Span.Length);
            Assert.Equal(0, buffer.Length);

            buffer.SetLength(7);
            buffer.Span[0] = 0x5A;

            Assert.Equal(7, buffer.Length);
            Assert.Equal(0x5A, buffer.Memory.Span[0]);

            buffer.Release();

            Assert.Equal(0, pool.RentedCount);
        }

        // 팬아웃 수명 계약 테스트: publish 가드 ref 와 구독자별 AddRef 가 균형을 이룰 때,
        // 마지막 Release 전까지 풀에 반환되면 안 되고 마지막 Release 에서만 누수 없이 반환되어야 한다.
        [Fact]
        public void AddRefAndRelease_WhenBalanced_ReturnsOnlyAfterLastRelease()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
            RefCountedBuffer buffer = pool.RentCounted();

            buffer.AddRef();
            buffer.AddRef();

            buffer.Release();
            Assert.Equal(1, pool.RentedCount);

            buffer.Release();
            Assert.Equal(1, pool.RentedCount);

            buffer.Release();
            Assert.Equal(0, pool.RentedCount);
        }

        // 팬아웃 동시 반환 테스트: publish 가드 ref 와 여러 구독자 송신 완료 ref 가 같은 시점에 Release 되더라도
        // 마지막 0 도달 경로 하나만 풀 반환을 수행해야 한다. 구독자 0명도 즉시 반환되는 정상 경로이므로 함께 검증한다.
        [Fact]
        public void Release_WhenFanOutSubscribersReleaseConcurrently_ReturnsExactlyOnce()
        {
            int[] subscriberCounts = new int[] { 0, 1, 2, 4, 8, 32 };

            for (int countIndex = 0; countIndex < subscriberCounts.Length; countIndex++)
            {
                int subscriberCount = subscriberCounts[countIndex];

                for (int iteration = 0; iteration < 64; iteration++)
                {
                    PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
                    RefCountedBuffer buffer = pool.RentCounted();

                    for (int subscriberIndex = 0; subscriberIndex < subscriberCount; subscriberIndex++)
                    {
                        buffer.AddRef();
                    }

                    Action[] releases = new Action[subscriberCount + 1];
                    for (int releaseIndex = 0; releaseIndex < releases.Length; releaseIndex++)
                    {
                        releases[releaseIndex] = delegate()
                        {
                            buffer.Release();
                        };
                    }

                    Assert.Equal(1, pool.RentedCount);

                    RunConcurrentActions(releases);

                    Assert.Equal(0, pool.RentedCount);
                }
            }
        }

        // 다수 버퍼 동시 in-flight 테스트: 실제 브로커에서는 여러 publish payload 가 동시에 송신 큐와 완료 경로를 지난다.
        // 각 버퍼의 참조계수 경쟁이 서로 섞여도 누수 없이 모두 반환되어야 다음 fan-out 단계에서 풀 고갈을 만들지 않는다.
        [Fact]
        public void Release_WhenManyBuffersAreInFlightConcurrently_FinishesWithNoLeaks()
        {
            const int bufferCount = 64;
            const int subscriberRefsPerBuffer = 3;

            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(128);
            Action[] releases = new Action[bufferCount * (subscriberRefsPerBuffer + 1)];
            int releaseIndex = 0;

            for (int bufferIndex = 0; bufferIndex < bufferCount; bufferIndex++)
            {
                RefCountedBuffer buffer = pool.RentCounted();

                for (int subscriberIndex = 0; subscriberIndex < subscriberRefsPerBuffer; subscriberIndex++)
                {
                    buffer.AddRef();
                }

                for (int refIndex = 0; refIndex < subscriberRefsPerBuffer + 1; refIndex++)
                {
                    RefCountedBuffer captured = buffer;
                    releases[releaseIndex] = delegate()
                    {
                        captured.Release();
                    };
                    releaseIndex++;
                }
            }

            Assert.Equal(bufferCount, pool.RentedCount);

            RunConcurrentActions(releases);

            Assert.Equal(0, pool.RentedCount);
        }

        // 과다 반환 방어 테스트: 이미 0에 도달해 풀로 돌아간 버퍼를 다시 Release 하면
        // 참조계수 음수나 이중 반환으로 이어지므로 즉시 계약 위반 예외로 드러나야 한다.
        [Fact]
        public void Release_WhenCalledAfterFinalRelease_ThrowsAndDoesNotCorruptPool()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(8);
            RefCountedBuffer buffer = pool.RentCounted();

            buffer.Release();

            Assert.Throws<InvalidOperationException>(delegate()
            {
                buffer.Release();
            });
            Assert.Equal(0, pool.RentedCount);
        }

        // 부활 방어 테스트: D006 계약상 AddRef 는 어떤 Release 가 0에 도달하기 전에 끝나야 한다.
        // 반환된 버퍼를 다시 AddRef 할 수 있으면 use-after-free 가 되므로 반드시 거부해야 한다.
        [Fact]
        public void AddRef_WhenCalledAfterFinalRelease_Throws()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(8);
            RefCountedBuffer buffer = pool.RentCounted();

            buffer.Release();

            Assert.Throws<InvalidOperationException>(delegate()
            {
                buffer.AddRef();
            });
            Assert.Equal(0, pool.RentedCount);
        }

        // 길이 경계 테스트: Length 는 payload 유효 범위이므로 음수나 블록 용량 초과 값을 허용하면
        // 이후 send view 또는 프레임 조립이 잘못된 범위를 보게 된다.
        [Fact]
        public void SetLength_WhenOutOfRange_ThrowsAndKeepsPreviousLength()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(4);
            RefCountedBuffer buffer = pool.RentCounted();

            buffer.SetLength(2);

            Assert.Throws<ArgumentOutOfRangeException>(delegate()
            {
                buffer.SetLength(5);
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate()
            {
                buffer.SetLength(-1);
            });
            Assert.Equal(2, buffer.Length);

            buffer.Release();
            Assert.Equal(0, pool.RentedCount);
        }

        // 동시성 테스트 보조 함수: 모든 작업을 시작 게이트 뒤에 세워 마지막 Release 주변의 경쟁을 의도적으로 만든다.
        // timeout 은 실패 시 테스트가 무한 대기하지 않도록 하는 안전장치이고, 작업 예외는 Task.WaitAll 이 그대로 보고한다.
        private static void RunConcurrentActions(Action[] actions)
        {
            using (ManualResetEventSlim start = new ManualResetEventSlim(false))
            {
                Task[] tasks = new Task[actions.Length];

                for (int actionIndex = 0; actionIndex < actions.Length; actionIndex++)
                {
                    Action action = actions[actionIndex];
                    tasks[actionIndex] = Task.Run(delegate()
                    {
                        start.Wait();
                        action();
                    });
                }

                start.Set();

                Assert.True(
                    Task.WaitAll(tasks, TimeSpan.FromSeconds(10)),
                    "동시 Release 작업이 시간 안에 모두 끝나야 한다.");
            }
        }

        private sealed class TestBufferOwner : IRefCountedBufferOwner
        {
            internal TestBufferOwner(int blockSize)
            {
                BlockSize = blockSize;
            }

            public int BlockSize { get; private set; }

            internal int ReturnCount { get; private set; }

            internal byte[]? ReturnedBlock { get; private set; }

            public void Return(byte[] block)
            {
                ReturnCount++;
                ReturnedBlock = block;
            }
        }
    }
}
