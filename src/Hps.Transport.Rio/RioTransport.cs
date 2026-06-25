using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Hps.Transport
{
    /// <summary>
    /// Windows RIO backend root다.
    /// 초기 task에서는 opt-in construction과 수명 경계만 만들고 실제 socket pump는 후속 task에서 붙인다.
    /// </summary>
    public sealed class RioTransport : TransportBase
    {
        private bool _started;
        private bool _stopped;

        public override ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_stopped)
                throw new InvalidOperationException("이미 중지된 RIO Transport는 다시 시작할 수 없습니다.");

            _started = true;
            return default(ValueTask);
        }

        public override ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _stopped = true;
            _started = false;
            return default(ValueTask);
        }

        public override ValueTask<IConnectionListener> ListenTcpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException(nameof(localEndPoint));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureRunning();
            throw new NotSupportedException("RIO TCP listen은 후속 task에서 구현합니다.");
        }

        public override ValueTask<IConnection> ConnectTcpAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
        {
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureRunning();
            throw new NotSupportedException("RIO TCP connect는 후속 task에서 구현합니다.");
        }

        private void EnsureRunning()
        {
            if (!_started || _stopped)
                throw new InvalidOperationException("RIO Transport가 실행 중이 아닙니다.");
        }
    }
}
