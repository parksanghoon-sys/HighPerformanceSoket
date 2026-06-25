using System;

namespace Hps.Transport
{
    /// <summary>
    /// socket별 RIO_RQ outstanding quota를 관리한다.
    /// native RIO queue는 synchronization을 제공하지 않으므로 이 owner를 통해 posting을 직렬화한다.
    /// </summary>
    internal sealed class RioRequestQueue : IDisposable
    {
        private readonly object _gate;
        private readonly int _maxOutstandingReceive;
        private readonly int _maxOutstandingSend;
        private int _outstandingReceive;
        private int _outstandingSend;
        private bool _disposed;

        internal RioRequestQueue(int maxOutstandingReceive, int maxOutstandingSend)
        {
            if (maxOutstandingReceive <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxOutstandingReceive));
            if (maxOutstandingSend <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxOutstandingSend));

            _gate = new object();
            _maxOutstandingReceive = maxOutstandingReceive;
            _maxOutstandingSend = maxOutstandingSend;
        }

        internal bool TryReserveReceive()
        {
            lock (_gate)
            {
                if (_disposed || _outstandingReceive == _maxOutstandingReceive)
                    return false;

                _outstandingReceive++;
                return true;
            }
        }

        internal bool TryReserveSend()
        {
            lock (_gate)
            {
                if (_disposed || _outstandingSend == _maxOutstandingSend)
                    return false;

                _outstandingSend++;
                return true;
            }
        }

        internal void CompleteReceive()
        {
            lock (_gate)
            {
                if (_outstandingReceive != 0)
                    _outstandingReceive--;
            }
        }

        internal void CompleteSend()
        {
            lock (_gate)
            {
                if (_outstandingSend != 0)
                    _outstandingSend--;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
            }
        }
    }
}
