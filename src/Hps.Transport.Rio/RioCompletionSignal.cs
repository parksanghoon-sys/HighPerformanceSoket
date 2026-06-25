using System;
using System.Threading.Tasks;

namespace Hps.Transport
{
    /// <summary>
    /// 하나의 RIO completion queue notification 을 기다리는 managed signal owner 다.
    /// 실제 IOCP wiring 전에는 pump 가 호출할 wake/fault/dispose 경계만 먼저 고정한다.
    /// </summary>
    internal sealed class RioCompletionSignal : IDisposable
    {
        private readonly object _gate;
        private readonly RioCompletionPort _owner;
        private TaskCompletionSource<bool>? _waiter;
        private Exception? _fault;
        private bool _disposed;

        internal RioCompletionSignal(RioCompletionPort owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _gate = new object();
        }

        internal Task WaitAsync()
        {
            lock (_gate)
            {
                if (_fault != null)
                    return Task.FromException(_fault);

                if (_disposed)
                    return Task.FromException(new ObjectDisposedException(nameof(RioCompletionSignal)));

                if (_waiter == null || _waiter.Task.IsCompleted)
                    _waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                return _waiter.Task;
            }
        }

        internal void CompleteFromPump()
        {
            TaskCompletionSource<bool>? waiter;

            lock (_gate)
            {
                if (_disposed)
                    return;

                waiter = _waiter;
                _waiter = null;
            }

            waiter?.TrySetResult(true);
        }

        internal void FaultFromPump(Exception exception)
        {
            if (exception == null)
                throw new ArgumentNullException(nameof(exception));

            TaskCompletionSource<bool>? waiter;

            lock (_gate)
            {
                if (_fault == null)
                    _fault = exception;

                waiter = _waiter;
                _waiter = null;
            }

            waiter?.TrySetException(exception);
        }

        internal void CompleteForTests()
        {
            CompleteFromPump();
        }

        public void Dispose()
        {
            TaskCompletionSource<bool>? waiter;

            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                waiter = _waiter;
                _waiter = null;
            }

            _owner.Unregister(this);
            waiter?.TrySetException(new ObjectDisposedException(nameof(RioCompletionSignal)));
        }
    }
}
