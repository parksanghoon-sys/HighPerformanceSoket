using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hps.Transport
{
    /// <summary>
    /// RIO transport 단위 completion notification owner 다.
    /// Task 2에서는 signal registry 와 dispose wake 만 제공하고, 실제 IOCP pump 는 Task 3에서 연결한다.
    /// </summary>
    internal sealed class RioCompletionPort : IDisposable
    {
        private readonly object _gate;
        private readonly Dictionary<ulong, RioCompletionSignal> _signals;
        private readonly IntPtr _completionPortHandle;
        private readonly Task? _pumpTask;
        private long _nextCompletionKey;
        private bool _disposed;
        private const ulong ShutdownCompletionKey = ulong.MaxValue;

        private RioCompletionPort(IntPtr completionPortHandle, bool startPump)
        {
            _gate = new object();
            _signals = new Dictionary<ulong, RioCompletionSignal>();
            _completionPortHandle = completionPortHandle;

            if (startPump)
            {
                _pumpTask = Task.Run(delegate()
                {
                    PumpLoop();
                });
            }
        }

        internal static RioCompletionPort CreateForTests()
        {
            return new RioCompletionPort(IntPtr.Zero, startPump: false);
        }

        internal static RioCompletionPort Create()
        {
            return new RioCompletionPort(RioNative.CreateIoCompletionPortHandle(0), startPump: true);
        }

        internal RioCompletionSignal CreateSignalForTests()
        {
            return CreateSignal();
        }

        internal RioCompletionSignal CreateSignal()
        {
            lock (_gate)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(RioCompletionPort));

                RioCompletionSignal signal;
                if (_completionPortHandle == IntPtr.Zero)
                {
                    signal = new RioCompletionSignal(this);
                }
                else
                {
                    ulong key = checked((ulong)Interlocked.Increment(ref _nextCompletionKey));
                    signal = new RioCompletionSignal(this, new UIntPtr(key), _completionPortHandle);
                    _signals.Add(key, signal);
                }

                return signal;
            }
        }

        internal void Unregister(RioCompletionSignal signal)
        {
            if (signal == null)
                throw new ArgumentNullException(nameof(signal));

            lock (_gate)
            {
                if (signal.CompletionKey != UIntPtr.Zero)
                    _signals.Remove(signal.CompletionKey.ToUInt64());
            }
        }

        private void PumpLoop()
        {
            NativeOverlappedEntry[] entries = new NativeOverlappedEntry[16];

            while (true)
            {
                uint removed;
                try
                {
                    removed = RioNative.GetQueuedCompletionStatusEx(_completionPortHandle, entries, uint.MaxValue);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    FaultAll(exception);
                    return;
                }

                for (int i = 0; i < removed; i++)
                {
                    ulong key = entries[i].CompletionKey.ToUInt64();
                    if (key == ShutdownCompletionKey)
                        return;

                    RioCompletionSignal? signal = FindSignal(key);
                    if (signal != null)
                        signal.CompleteFromPump();
                }
            }
        }

        private RioCompletionSignal? FindSignal(ulong key)
        {
            lock (_gate)
            {
                RioCompletionSignal signal;
                if (_signals.TryGetValue(key, out signal!))
                    return signal;

                return null;
            }
        }

        private void FaultAll(Exception exception)
        {
            RioCompletionSignal[] signals;

            lock (_gate)
            {
                signals = new RioCompletionSignal[_signals.Count];
                _signals.Values.CopyTo(signals, 0);
            }

            for (int i = 0; i < signals.Length; i++)
                signals[i].FaultFromPump(exception);
        }

        public void Dispose()
        {
            RioCompletionSignal[] signals;

            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                signals = new RioCompletionSignal[_signals.Count];
                _signals.Values.CopyTo(signals, 0);
                _signals.Clear();
            }

            for (int i = 0; i < signals.Length; i++)
                signals[i].Dispose();

            if (_completionPortHandle != IntPtr.Zero)
            {
                try
                {
                    RioNative.PostQueuedCompletionStatus(_completionPortHandle, new UIntPtr(ShutdownCompletionKey), IntPtr.Zero);
                    _pumpTask?.Wait(TimeSpan.FromSeconds(1));
                }
                catch (AggregateException)
                {
                }
                finally
                {
                    RioNative.CloseNativeHandle(_completionPortHandle);
                }
            }
        }
    }
}
