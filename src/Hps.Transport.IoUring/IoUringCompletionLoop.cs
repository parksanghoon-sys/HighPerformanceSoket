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
        private CancellationTokenSource? _stopSource;
        private Task? _loopTask;
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

            if (_queue == null)
                return default(ValueTask);
            if (_loopTask != null)
                return default(ValueTask);

            CancellationTokenSource stopSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _stopSource = stopSource;
            _loopTask = Task.Run(delegate()
            {
                return RunLoopAsync(stopSource.Token);
            });

            return default(ValueTask);
        }

        internal async ValueTask StopAsync()
        {
            if (_disposed)
                return;

            CancellationTokenSource? stopSource = _stopSource;
            Task? loopTask = _loopTask;
            _stopSource = null;
            _loopTask = null;

            if (stopSource != null)
                stopSource.Cancel();

            if (loopTask != null)
            {
                try
                {
                    await loopTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            stopSource?.Dispose();
        }

        internal IoUringQueue Queue
        {
            get
            {
                IoUringQueue? queue = _queue;
                if (queue == null)
                    throw new InvalidOperationException("test completion loop에는 io_uring queue가 없습니다.");

                return queue;
            }
        }

        internal Task RunOnceForTestsAsync()
        {
            return DrainAvailableCompletionsAsync(CancellationToken.None);
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

            StopAsync().AsTask().GetAwaiter().GetResult();
            _disposed = true;
            GC.KeepAlive(_queue);
        }

        private async Task RunLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
                await DrainAvailableCompletionsAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task DrainAvailableCompletionsAsync(CancellationToken cancellationToken)
        {
            IoUringQueue? queue = _queue;
            if (queue == null)
                return;

            bool drainedAny = false;
            IoUringCompletion completion;
            while (queue.TryDequeueCompletion(out completion))
            {
                drainedAny = true;
                DispatchCompletion(completion);
            }

            if (!drainedAny)
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(IoUringCompletionLoop));
        }
    }
}
