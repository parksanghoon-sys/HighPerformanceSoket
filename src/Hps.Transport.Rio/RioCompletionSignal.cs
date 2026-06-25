using System;
using System.Runtime.InteropServices;
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
        private readonly bool _ownsNativeMemory;
        private IntPtr _overlappedPointer;
        private IntPtr _notificationCompletionPointer;
        private TaskCompletionSource<bool>? _waiter;
        private Exception? _fault;
        private bool _notifyArmed;
        private bool _signaled;
        private bool _disposed;

        internal RioCompletionSignal(RioCompletionPort owner)
            : this(owner, UIntPtr.Zero, IntPtr.Zero, ownsNativeMemory: false)
        {
        }

        internal RioCompletionSignal(RioCompletionPort owner, UIntPtr completionKey, IntPtr completionPortHandle)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _gate = new object();
            CompletionKey = completionKey;
            _ownsNativeMemory = true;

            _overlappedPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped64>());
            Marshal.StructureToPtr(new NativeOverlapped64(), _overlappedPointer, false);

            RioNotificationCompletion notification = RioNotificationCompletion.ForIocp(
                completionPortHandle,
                completionKey,
                _overlappedPointer);

            _notificationCompletionPointer = Marshal.AllocHGlobal(Marshal.SizeOf<RioNotificationCompletion>());
            Marshal.StructureToPtr(notification, _notificationCompletionPointer, false);
        }

        private RioCompletionSignal(
            RioCompletionPort owner,
            UIntPtr completionKey,
            IntPtr notificationCompletionPointer,
            bool ownsNativeMemory)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _gate = new object();
            CompletionKey = completionKey;
            _notificationCompletionPointer = notificationCompletionPointer;
            _ownsNativeMemory = ownsNativeMemory;
        }

        internal UIntPtr CompletionKey { get; }

        internal IntPtr NotificationCompletionPointer
        {
            get { return _notificationCompletionPointer; }
        }

        internal Task WaitAsync()
        {
            lock (_gate)
            {
                if (_fault != null)
                    return Task.FromException(_fault);

                if (_disposed)
                    return Task.FromException(new ObjectDisposedException(nameof(RioCompletionSignal)));

                if (_signaled)
                {
                    _signaled = false;
                    return Task.CompletedTask;
                }

                if (_waiter == null || _waiter.Task.IsCompleted)
                    _waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                return _waiter.Task;
            }
        }

        internal bool TryArmNotification()
        {
            lock (_gate)
            {
                if (_fault != null)
                    throw _fault;

                if (_disposed)
                    throw new ObjectDisposedException(nameof(RioCompletionSignal));

                if (_notifyArmed)
                    return false;

                _notifyArmed = true;
                return true;
            }
        }

        internal void MarkNotificationArmFailed()
        {
            lock (_gate)
            {
                _notifyArmed = false;
            }
        }

        internal void CompleteFromPump()
        {
            TaskCompletionSource<bool>? waiter;

            lock (_gate)
            {
                if (_disposed)
                    return;

                _notifyArmed = false;
                waiter = _waiter;
                _waiter = null;
                if (waiter == null)
                    _signaled = true;
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

                _notifyArmed = false;
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
                _notifyArmed = false;
                waiter = _waiter;
                _waiter = null;
            }

            _owner.Unregister(this);
            waiter?.TrySetException(new ObjectDisposedException(nameof(RioCompletionSignal)));

            if (_ownsNativeMemory)
            {
                IntPtr notificationCompletionPointer = _notificationCompletionPointer;
                _notificationCompletionPointer = IntPtr.Zero;
                if (notificationCompletionPointer != IntPtr.Zero)
                    Marshal.FreeHGlobal(notificationCompletionPointer);

                IntPtr overlappedPointer = _overlappedPointer;
                _overlappedPointer = IntPtr.Zero;
                if (overlappedPointer != IntPtr.Zero)
                    Marshal.FreeHGlobal(overlappedPointer);
            }
        }
    }
}
