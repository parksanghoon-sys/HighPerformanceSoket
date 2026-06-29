using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Hps.Transport
{
    /// <summary>
    /// Linux io_uring 기반 transport 의 opt-in root type 이다.
    ///
    /// 첫 boundary 에서는 native SQ/CQ, mmap, fixed buffer, TCP/UDP pump 를 만들지 않는다.
    /// 대신 상위 public 계약을 넓히지 않고 lifecycle shell 과 명시적 unsupported operation boundary 만 제공한다.
    /// </summary>
    public sealed class IoUringTransport : TransportBase
    {
        private readonly object _gate;
        private bool _started;
        private bool _stopped;

        /// <summary>
        /// io_uring transport root 를 만든다.
        ///
        /// 생성자는 native 자원을 열지 않는다. 실제 io_uring queue owner 는 후속 native wrapper task 에서
        /// StartAsync 경계 안쪽에 붙이며, 지금은 opt-in backend 를 참조해도 부작용이 없도록 둔다.
        /// </summary>
        public IoUringTransport()
        {
            _gate = new object();
        }

        /// <inheritdoc />
        public override ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (_stopped)
                    throw new InvalidOperationException("이미 중지된 io_uring Transport는 다시 시작할 수 없습니다.");

                _started = true;
            }

            return default(ValueTask);
        }

        /// <inheritdoc />
        public override ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                _stopped = true;
                _started = false;
            }

            return default(ValueTask);
        }

        /// <inheritdoc />
        public override ValueTask<IConnectionListener> ListenTcpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException(nameof(localEndPoint));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureStarted();
            throw CreateUnsupportedException();
        }

        /// <inheritdoc />
        public override ValueTask<IConnection> ConnectTcpAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
        {
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureStarted();
            throw CreateUnsupportedException();
        }

        /// <inheritdoc />
        public override ValueTask<IUdpEndpoint> BindUdpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException(nameof(localEndPoint));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureStarted();
            throw CreateUnsupportedException();
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            lock (_gate)
            {
                _stopped = true;
                _started = false;
            }
        }

        private void EnsureStarted()
        {
            lock (_gate)
            {
                if (!_started || _stopped)
                    throw new InvalidOperationException("io_uring Transport가 시작되지 않았습니다.");
            }
        }

        private static NotSupportedException CreateUnsupportedException()
        {
            IoUringCapabilityStatus status = IoUringCapabilityProbe.GetStatus();

            if (status == IoUringCapabilityStatus.UnsupportedOperatingSystem)
                return new NotSupportedException("io_uring backend는 Linux에서만 사용할 수 있습니다.");

            return new NotSupportedException("io_uring native pump 는 아직 구현되지 않았습니다.");
        }
    }
}
