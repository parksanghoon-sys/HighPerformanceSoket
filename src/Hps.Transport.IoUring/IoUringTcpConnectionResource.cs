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
        private const int ReceiveBlockSize = 4096;
        private const int TcpLengthPrefixSize = 4;

        private readonly IoUringOperationRegistry _registry;
        private byte[]? _receiveBlock;
        private byte[]? _lengthPrefixBlock;
        private IoUringOperationContext? _receiveContext;
        private IoUringOperationContext? _sendContext;
        private IoUringFixedSendBufferRegistry? _fixedSendBufferRegistry;
        private int _disposed;

        internal IoUringTcpConnectionResource(
            Socket socket,
            IoUringOperationRegistry registry,
            IoUringCompletionLoop completionLoop)
        {
            Socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            CompletionLoop = completionLoop ?? throw new ArgumentNullException(nameof(completionLoop));
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
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            IoUringOperationContext? receiveContext = _receiveContext;
            _receiveContext = null;
            if (receiveContext != null)
                _registry.Unregister(receiveContext.Token);

            IoUringOperationContext? sendContext = _sendContext;
            _sendContext = null;
            if (sendContext != null)
                _registry.Unregister(sendContext.Token);

            Socket.Dispose();

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
