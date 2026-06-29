using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hps.Transport
{
    /// <summary>
    /// io_uring CQE를 managed operation context로 전달하는 completion loop 경계다.
    ///
    /// 이 단계에서는 native CQ drain thread를 아직 만들지 않고 pure dispatch 계약을 먼저 고정한다.
    /// 후속 TCP receive/send pump는 같은 DispatchCompletion 경로를 사용하므로, token routing 오류를
    /// Linux 전용 syscall loop와 분리해 검증할 수 있다.
    /// </summary>
    internal sealed class IoUringCompletionLoop : IDisposable
    {
        private readonly IoUringQueue? _queue;
        private readonly IoUringOperationRegistry _registry;
        private bool _disposed;

        internal IoUringCompletionLoop(IoUringQueue queue, IoUringOperationRegistry registry)
            : this(queue, registry, requireQueue: true)
        {
        }

        private IoUringCompletionLoop(IoUringQueue? queue, IoUringOperationRegistry registry, bool requireQueue)
        {
            if (requireQueue && queue == null)
                throw new ArgumentNullException(nameof(queue));
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            _queue = queue;
            _registry = registry;
        }

        internal static IoUringCompletionLoop CreateForTests(IoUringOperationRegistry registry)
        {
            return new IoUringCompletionLoop(null, registry, requireQueue: false);
        }

        internal ValueTask StartAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            return default(ValueTask);
        }

        internal ValueTask StopAsync()
        {
            if (_disposed)
                return default(ValueTask);

            return default(ValueTask);
        }

        internal void DispatchCompletion(IoUringCompletion completion)
        {
            ThrowIfDisposed();

            IoUringOperationContext context = _registry.Resolve(completion.Token);
            context.Complete(completion);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            GC.KeepAlive(_queue);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(IoUringCompletionLoop));
        }
    }
}
