using System;
using System.Net.Sockets;
using System.Threading;
using Hps.Buffers;

namespace Hps.Transport
{
    /// <summary>
    /// io_uring TCP connection이 사용하는 socket, pinned block, operation context 수명을 함께 소유한다.
    ///
    /// 실제 receive/send SQE 제출은 후속 task에서 붙인다. 이 owner를 먼저 분리해 두면 connection close 시
    /// socket dispose, pinned block 반환, registry token 제거가 한 경로로 수렴한다.
    /// </summary>
    internal sealed class IoUringTcpConnectionResource : IDisposable
    {
        // mixed workload의 10,240B payload와 framing/command envelope를 한 번의 recv SQE에 담는다.
        // 4KiB를 유지하면 frame당 CQE가 세 번 이상 필요해 1ms completion polling 지연이 누적된다.
        private const int ReceiveBlockSize = 16 * 1024;
        private const int TcpLengthPrefixSize = 4;

        private readonly IoUringOperationRegistry _registry;
        private readonly object _lifetimeGate;
        private byte[]? _receiveBlock;
        private byte[]? _lengthPrefixBlock;
        private IoUringOperationContext? _receiveContext;
        private IoUringOperationContext? _sendContext;
        private IoUringFixedSendBufferRegistry? _fixedSendBufferRegistry;
        private int _lifetimeReferenceCount;
        private int _cleanupCompleted;
        private int _disposed;

        internal IoUringTcpConnectionResource(
            Socket socket,
            IoUringOperationRegistry registry,
            IoUringCompletionLoop completionLoop)
        {
            Socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            CompletionLoop = completionLoop ?? throw new ArgumentNullException(nameof(completionLoop));
            _lifetimeGate = new object();
            _lifetimeReferenceCount = 1;
            ReceivePool = new PinnedBlockMemoryPool(ReceiveBlockSize);
            LengthPrefixPool = new PinnedBlockMemoryPool(TcpLengthPrefixSize);

            try
            {
                _receiveContext = _registry.Register(IoUringOperationKind.Receive);
                _sendContext = _registry.Register(IoUringOperationKind.Send);
                _receiveBlock = ReceivePool.Rent();
                _lengthPrefixBlock = LengthPrefixPool.Rent();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal Socket Socket { get; }

        internal int SocketFileDescriptor
        {
            get
            {
                long handle = Socket.SafeHandle.DangerousGetHandle().ToInt64();
                if (handle < 0 || handle > int.MaxValue)
                    throw new InvalidOperationException("socket file descriptor가 io_uring syscall 범위에 맞지 않습니다.");

                return checked((int)handle);
            }
        }

        internal PinnedBlockMemoryPool ReceivePool { get; }

        internal PinnedBlockMemoryPool LengthPrefixPool { get; }

        internal IoUringCompletionLoop CompletionLoop { get; }

        internal IoUringQueue Queue
        {
            get { return CompletionLoop.Queue; }
        }

        internal IoUringOperationContext ReceiveContext
        {
            get
            {
                IoUringOperationContext? context = _receiveContext;
                if (context == null)
                    throw new ObjectDisposedException(nameof(IoUringTcpConnectionResource));

                return context;
            }
        }

        internal IoUringOperationContext SendContext
        {
            get
            {
                IoUringOperationContext? context = _sendContext;
                if (context == null)
                    throw new ObjectDisposedException(nameof(IoUringTcpConnectionResource));

                return context;
            }
        }

        internal byte[] ReceiveBlock
        {
            get
            {
                byte[]? block = _receiveBlock;
                if (block == null)
                    throw new ObjectDisposedException(nameof(IoUringTcpConnectionResource));

                return block;
            }
        }

        internal byte[] LengthPrefixBlock
        {
            get
            {
                byte[]? block = _lengthPrefixBlock;
                if (block == null)
                    throw new ObjectDisposedException(nameof(IoUringTcpConnectionResource));

                return block;
            }
        }

        internal IoUringFixedSendBufferRegistry? FixedSendBufferRegistry
        {
            get { return _fixedSendBufferRegistry; }
        }

        internal bool IsDisposed
        {
            get { return Volatile.Read(ref _disposed) != 0; }
        }

        /// <summary>
        /// receive/send pump가 제출한 SQE와 pinned block 수명을 보호하는 reference를 획득한다.
        /// connection owner가 close를 시작한 뒤에는 새 pump가 생기면 안 되므로 획득을 거부한다.
        /// </summary>
        internal void AddPumpReference()
        {
            lock (_lifetimeGate)
            {
                if (_disposed != 0)
                    throw new ObjectDisposedException(nameof(IoUringTcpConnectionResource));

                _lifetimeReferenceCount++;
            }
        }

        /// <summary>
        /// pump가 마지막 CQE 또는 close unwind를 관측한 뒤 수명 reference를 반환한다.
        /// 마지막 reference가 반환될 때만 operation context와 pinned block을 실제 정리한다.
        /// </summary>
        internal void ReleasePumpReference()
        {
            ReleaseLifetimeReference();
        }

        internal void SetFixedSendBufferRegistryForTests(IoUringFixedSendBufferRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            // Task 3에서는 production send path에 아직 연결하지 않고 owner 수명 경계만 고정한다.
            // 테스트 seam은 기존 owner를 교체할 때 이전 owner를 즉시 정리해 중복 fixed table owner를 남기지 않는다.
            IoUringFixedSendBufferRegistry? previous = Interlocked.Exchange(ref _fixedSendBufferRegistry, registry);
            if (previous != null)
                previous.Dispose();
        }

        public void Dispose()
        {
            lock (_lifetimeGate)
            {
                if (_disposed != 0)
                    return;

                // IsDisposed를 먼저 게시해 pump가 close 이후 새 SQE를 제출하지 못하게 한다.
                Volatile.Write(ref _disposed, 1);
            }

            try
            {
                CancelPendingOperations();
            }
            finally
            {
                try
                {
                    Socket.Dispose();
                }
                finally
                {
                    ReleaseLifetimeReference();
                }
            }
        }

        private void CancelPendingOperations()
        {
            IoUringQueue? queue;
            if (!CompletionLoop.TryGetQueue(out queue) || queue == null)
                return;

            // close(fd)만으로는 io_uring이 보유한 pending request reference가 취소되지 않는다.
            // context를 unregister하거나 pinned block을 반환하기 전에 두 고정 token을 명시적으로 취소해
            // 대상 CQE가 receive/send pump waiter를 깨우도록 한다. 대기 중인 send가 없으면 cancel CQE는
            // -ENOENT control 결과만 남고, completion loop가 예약 token 0으로 안전하게 버린다.
            IoUringOperationContext? receiveContext = _receiveContext;
            if (receiveContext != null && !queue.TrySubmitCancel(receiveContext.Token))
                throw new SocketException((int)SocketError.NoBufferSpaceAvailable);

            IoUringOperationContext? sendContext = _sendContext;
            if (sendContext != null && !queue.TrySubmitCancel(sendContext.Token))
                throw new SocketException((int)SocketError.NoBufferSpaceAvailable);
        }

        private void ReleaseLifetimeReference()
        {
            bool cleanup;

            lock (_lifetimeGate)
            {
                if (_lifetimeReferenceCount <= 0)
                    throw new InvalidOperationException("io_uring TCP resource lifetime reference가 이미 0입니다.");

                _lifetimeReferenceCount--;
                cleanup = _lifetimeReferenceCount == 0;
            }

            if (cleanup)
                CleanupNativeResources();
        }

        private void CleanupNativeResources()
        {
            if (Interlocked.Exchange(ref _cleanupCompleted, 1) != 0)
                return;

            IoUringOperationContext? receiveContext = _receiveContext;
            _receiveContext = null;
            if (receiveContext != null)
                _registry.Unregister(receiveContext.Token);

            IoUringOperationContext? sendContext = _sendContext;
            _sendContext = null;
            if (sendContext != null)
                _registry.Unregister(sendContext.Token);

            byte[]? receiveBlock = _receiveBlock;
            _receiveBlock = null;
            if (receiveBlock != null)
                ReceivePool.Return(receiveBlock);

            byte[]? lengthPrefixBlock = _lengthPrefixBlock;
            _lengthPrefixBlock = null;
            if (lengthPrefixBlock != null)
                LengthPrefixPool.Return(lengthPrefixBlock);

            IoUringFixedSendBufferRegistry? fixedSendBufferRegistry = Interlocked.Exchange(ref _fixedSendBufferRegistry, null);
            if (fixedSendBufferRegistry != null)
                fixedSendBufferRegistry.Dispose();

            GC.KeepAlive(CompletionLoop);
        }
    }
}
