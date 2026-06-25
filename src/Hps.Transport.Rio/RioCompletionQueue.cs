using System;

namespace Hps.Transport
{
    /// <summary>
    /// RIO completion queue 수명을 소유한다.
    /// 초기 구현은 native handle 없이 Dispose 경계만 만들고, native CQ는 pump task에서 연결한다.
    /// </summary>
    internal sealed class RioCompletionQueue : IDisposable
    {
        private bool _disposed;

        internal bool IsDisposed => _disposed;

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
