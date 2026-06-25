using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Hps.Buffers;
using Hps.Transport;
using Xunit;

namespace Hps.Transport.Rio.Tests
{
    public sealed class RioCapabilityProbeTests
    {
        // RIO backend는 Windows 전용 opt-in 경로다.
        // 이 테스트는 비 Windows 환경에서 RIO를 사용할 수 있다고 오판하지 않게 막는다.
        [Fact]
        public void GetStatus_WhenNotWindows_ReturnsUnsupportedOperatingSystem()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            Assert.Equal(RioCapabilityStatus.UnsupportedOperatingSystem, RioCapabilityProbe.GetStatus());
        }

        // 기본 factory는 Phase 5 초기에 SAEA를 유지해야 한다.
        // RIO가 일부 구현됐더라도 TCP/UDP parity 전까지 default backend를 바꾸면 기존 통합 경로가 흔들린다.
        [Fact]
        public void CreateDefault_DuringRioOptInPhase_ReturnsSaeaTransport()
        {
            ITransport transport = TransportFactory.CreateDefault();

            Assert.IsType<SaeaTransport>(transport);
            transport.Dispose();
        }

        // Windows에서 RIO function table load 결과는 Available 또는 Unavailable로 수렴해야 한다.
        // 예외가 escape하면 factory probe가 fallback 대신 process failure를 일으킬 수 있다.
        [Fact]
        public void GetStatus_WhenWindows_DoesNotThrow()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            RioCapabilityStatus status = RioCapabilityProbe.GetStatus();

            Assert.True(status == RioCapabilityStatus.Available || status == RioCapabilityStatus.Unavailable);
        }

        // Windows RIO backend 는 실제 function table 을 얻을 수 있어야 이후 TCP pump 로 진입할 수 있다.
        // 이 테스트는 placeholder 로더가 항상 Unavailable 을 반환하는 상태를 막는 회귀 방어선이다.
        [Fact]
        public void GetStatus_WhenWindows_LoadsRioFunctionTable()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            Assert.Equal(RioCapabilityStatus.Available, RioCapabilityProbe.GetStatus());
        }

        // native loader 자체도 fallback 가능한 bool 결과로 수렴해야 한다.
        // 호출자가 SocketException 같은 native 실패를 직접 처리하지 않게 하는 방어선이다.
        [Fact]
        public void TryLoadFunctionTable_DoesNotThrow()
        {
            RioNative? native;

            bool loaded = RioNative.TryLoadFunctionTable(out native);

            Assert.True(loaded || native == null);
        }

        // function table pointer 를 얻는 것만으로는 충분하지 않다.
        // pump 가 쓰기 전 최소 buffer registration delegate 를 실제 pinned block 에 대해 호출할 수 있어야 한다.
        [Fact]
        public unsafe void RegisterBuffer_WhenRioAvailable_ReturnsBufferIdAndDeregisters()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
            {
                return;
            }

            RioNative? native;
            Assert.True(RioNative.TryLoadFunctionTable(out native));
            Assert.NotNull(native);

            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            byte[] block = pool.Rent();

            try
            {
                fixed (byte* pointer = block)
                {
                    IntPtr bufferId = native.RegisterBuffer((IntPtr)pointer, block.Length);

                    Assert.NotEqual(IntPtr.Zero, bufferId);
                    native.DeregisterBuffer(bufferId);
                }
            }
            finally
            {
                pool.Return(block);
            }

            Assert.Equal(0, pool.RentedCount);
        }

        // completion queue 는 RIO receive/send completion 을 모으는 pump 의 중심 자원이다.
        // 실제 pump 전에 native CQ handle 을 만들고 닫을 수 있는지 먼저 좁게 검증한다.
        [Fact]
        public void CreateCompletionQueue_WhenRioAvailable_ReturnsQueueAndCloses()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
            {
                return;
            }

            RioNative? native;
            Assert.True(RioNative.TryLoadFunctionTable(out native));
            Assert.NotNull(native);

            IntPtr completionQueue = native.CreateCompletionQueue(8);

            Assert.NotEqual(IntPtr.Zero, completionQueue);
            native.CloseCompletionQueue(completionQueue);
        }

        // request queue 는 socket 과 completion queue 를 연결하는 RIO send/receive posting 지점이다.
        // pump 구현 전 socket 하나에 RQ handle 을 만들 수 있어야 이후 receive/send delegate 를 검증할 수 있다.
        [Fact]
        public void CreateRequestQueue_WhenRioAvailable_ReturnsQueue()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
            {
                return;
            }

            RioNative? native;
            Assert.True(RioNative.TryLoadFunctionTable(out native));
            Assert.NotNull(native);

            using (Socket socket = RioNative.CreateTcpSocket())
            {
                IntPtr completionQueue = native.CreateCompletionQueue(8);

                try
                {
                    IntPtr requestQueue = native.CreateRequestQueue(socket, 1, 1, 1, 1, completionQueue, completionQueue);

                    Assert.NotEqual(IntPtr.Zero, requestQueue);
                }
                finally
                {
                    native.CloseCompletionQueue(completionQueue);
                }
            }
        }

        // skeleton transport는 아직 opt-in construction만 허용한다.
        // StartAsync가 예외 없이 끝나면 후속 task가 같은 root type 위에 queue/resource를 붙일 수 있다.
        // dequeue delegate 는 RIO pump 가 CQ에서 완료 이벤트를 읽는 마지막 native boundary 다.
        // 우선 method boundary 를 Red로 잡고, Green 뒤 빈 CQ에서 0개 completion 반환까지 직접 검증한다.
        [Fact]
        public void DequeueCompletion_WhenQueueIsEmpty_ReturnsZero()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
            {
                return;
            }

            RioNative? native;
            Assert.True(RioNative.TryLoadFunctionTable(out native));
            Assert.NotNull(native);

            IntPtr completionQueue = native.CreateCompletionQueue(8);

            try
            {
                RioResult[] results = new RioResult[1];
                uint count = native.DequeueCompletion(completionQueue, results);

                Assert.Equal(0u, count);
            }
            finally
            {
                native.CloseCompletionQueue(completionQueue);
            }
        }

        // receive/send delegate surface 는 RIO_BUF marshalling 을 pump 밖에서 먼저 고정한다.
        // 실제 connected posting completion 은 다음 단위에서 별도 loopback 으로 검증한다.
        [Fact]
        public void ReceiveSendOperations_WhenMissing_AreDetectedBeforePump()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
            {
                return;
            }

            RioNative? native;
            Assert.True(RioNative.TryLoadFunctionTable(out native));
            Assert.NotNull(native);

            RioBufferSegment[] buffers = new[] { new RioBufferSegment(new IntPtr(1), 0, 1) };

            Assert.Throws<ArgumentException>(delegate()
            {
                native.Receive(IntPtr.Zero, buffers, IntPtr.Zero);
            });
            Assert.Throws<ArgumentException>(delegate()
            {
                native.Send(IntPtr.Zero, buffers, IntPtr.Zero);
            });
        }

        // 실제 connected socket 에 receive 를 post 하고 CQ 에서 completion 을 관측한다.
        // Transport pump 를 만들기 전 native receive posting, buffer registration, dequeue 가 함께 맞물리는지 확인한다.
        [Fact]
        public unsafe void Receive_WhenPeerSendsByte_CompletesAndWritesIntoRegisteredBuffer()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
            {
                return;
            }

            RioNative? native;
            Assert.True(RioNative.TryLoadFunctionTable(out native));
            Assert.NotNull(native);

            using (Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            using (Socket client = RioNative.CreateTcpSocket())
            {
                listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                listener.Listen(1);

                client.Connect(listener.LocalEndPoint!);
                using (Socket server = listener.Accept())
                {
                    IntPtr completionQueue = native.CreateCompletionQueue(8);
                    PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
                    byte[] block = pool.Rent();

                    try
                    {
                        IntPtr requestQueue = native.CreateRequestQueue(client, 1, 1, 1, 1, completionQueue, completionQueue);

                        fixed (byte* pointer = block)
                        {
                            IntPtr bufferId = native.RegisterBuffer((IntPtr)pointer, block.Length);

                            try
                            {
                                RioBufferSegment[] buffers = new[] { new RioBufferSegment(bufferId, 0, 1) };

                                Assert.True(native.Receive(requestQueue, buffers, new IntPtr(101)));
                                Assert.Equal(1, server.Send(new byte[] { 42 }));

                                RioResult completion = WaitForCompletion(native, completionQueue);

                                Assert.Equal(0, completion.Status);
                                Assert.Equal(1u, completion.BytesTransferred);
                                Assert.Equal(101UL, completion.RequestContext);
                                Assert.Equal(42, block[0]);
                            }
                            finally
                            {
                                native.DeregisterBuffer(bufferId);
                            }
                        }
                    }
                    finally
                    {
                        pool.Return(block);
                        native.CloseCompletionQueue(completionQueue);
                    }

                    Assert.Equal(0, pool.RentedCount);
                }
            }
        }

        // 실제 connected socket 에 send 를 post 하고 CQ completion 과 peer receive 를 함께 확인한다.
        // 이 경계가 green 이어야 이후 Transport send pump 가 native posting 결과를 in-flight 완료로 바꿀 수 있다.
        [Fact]
        public unsafe void Send_WhenPosted_CompletesAndPeerReceivesByte()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
            {
                return;
            }

            RioNative? native;
            Assert.True(RioNative.TryLoadFunctionTable(out native));
            Assert.NotNull(native);

            using (Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            using (Socket client = RioNative.CreateTcpSocket())
            {
                listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                listener.Listen(1);

                client.Connect(listener.LocalEndPoint!);
                using (Socket server = listener.Accept())
                {
                    IntPtr completionQueue = native.CreateCompletionQueue(8);
                    PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(16);
                    byte[] block = pool.Rent();

                    try
                    {
                        IntPtr requestQueue = native.CreateRequestQueue(client, 1, 1, 1, 1, completionQueue, completionQueue);
                        block[0] = 77;

                        fixed (byte* pointer = block)
                        {
                            IntPtr bufferId = native.RegisterBuffer((IntPtr)pointer, block.Length);

                            try
                            {
                                RioBufferSegment[] buffers = new[] { new RioBufferSegment(bufferId, 0, 1) };

                                Assert.True(native.Send(requestQueue, buffers, new IntPtr(202)));
                                RioResult completion = WaitForCompletion(native, completionQueue);

                                byte[] received = new byte[1];
                                Assert.Equal(1, server.Receive(received));

                                Assert.Equal(0, completion.Status);
                                Assert.Equal(1u, completion.BytesTransferred);
                                Assert.Equal(202UL, completion.RequestContext);
                                Assert.Equal(77, received[0]);
                            }
                            finally
                            {
                                native.DeregisterBuffer(bufferId);
                            }
                        }
                    }
                    finally
                    {
                        pool.Return(block);
                        native.CloseCompletionQueue(completionQueue);
                    }

                    Assert.Equal(0, pool.RentedCount);
                }
            }
        }

        [Fact]
        public async Task RioTransport_WhenConstructed_StartStopDoesNotThrow()
        {
            using (ITransport transport = new RioTransport())
            {
                await transport.StartAsync();
                await transport.StopAsync();
            }
        }

        private static RioResult WaitForCompletion(RioNative native, IntPtr completionQueue)
        {
            RioResult[] results = new RioResult[1];

            for (int attempt = 0; attempt < 100; attempt++)
            {
                uint count = native.DequeueCompletion(completionQueue, results);
                if (count != 0)
                    return results[0];

                Thread.Sleep(10);
            }

            throw new TimeoutException("RIO completion 이 제한 시간 안에 관측되지 않았습니다.");
        }
    }
}
