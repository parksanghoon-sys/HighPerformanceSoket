using System;
using System.Collections.Generic;

namespace Hps.Transport
{
    /// <summary>
    /// RIO transport 단위 completion notification owner 다.
    /// Task 2에서는 signal registry 와 dispose wake 만 제공하고, 실제 IOCP pump 는 Task 3에서 연결한다.
    /// </summary>
    internal sealed class RioCompletionPort : IDisposable
    {
        private readonly object _gate;
        private readonly List<RioCompletionSignal> _signals;
        private bool _disposed;

        private RioCompletionPort()
        {
            _gate = new object();
            _signals = new List<RioCompletionSignal>();
        }

        internal static RioCompletionPort CreateForTests()
        {
            return new RioCompletionPort();
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

                RioCompletionSignal signal = new RioCompletionSignal(this);
                _signals.Add(signal);
                return signal;
            }
        }

        internal void Unregister(RioCompletionSignal signal)
        {
            if (signal == null)
                throw new ArgumentNullException(nameof(signal));

            lock (_gate)
            {
                _signals.Remove(signal);
            }
        }

        public void Dispose()
        {
            RioCompletionSignal[] signals;

            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                signals = _signals.ToArray();
                _signals.Clear();
            }

            for (int i = 0; i < signals.Length; i++)
                signals[i].Dispose();
        }
    }
}
