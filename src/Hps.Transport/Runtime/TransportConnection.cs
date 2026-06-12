using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hps.Transport
{
    /// <summary>
    /// Transport 구현이 내부에서 사용하는 연결 상태와 pending 송신 큐이다.
    ///
    /// public 표면은 <see cref="IConnection"/> 으로만 노출하고, 송신 수락은 <see cref="TransportBase.TrySend"/> 가
    /// 이 내부 상태에 위임한다. 이렇게 해야 연결 핸들은 수명에 집중하고, 큐/펌프 구조는 Transport 구현 내부에 남는다.
    /// </summary>
    internal sealed class TransportConnection : IConnection
    {
        private const int DefaultPendingSendCapacity = 16;

        private readonly object _gate;
        private readonly Queue<TransportSendBuffer> _pendingSends;
        private readonly SemaphoreSlim _sendSignal;
        private readonly IDisposable? _transportResource;
        private readonly Action<TransportConnection>? _onClosed;
        private readonly int _pendingSendCapacity;
        private bool _closed;

        internal TransportConnection()
            : this(null)
        {
        }

        internal TransportConnection(IDisposable? transportResource)
            : this(transportResource, null)
        {
        }

        internal TransportConnection(IDisposable? transportResource, Action<TransportConnection>? onClosed)
            : this(transportResource, onClosed, DefaultPendingSendCapacity)
        {
        }

        internal TransportConnection(IDisposable? transportResource, Action<TransportConnection>? onClosed, int pendingSendCapacity)
        {
            if (pendingSendCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(pendingSendCapacity));

            _gate = new object();
            _pendingSends = new Queue<TransportSendBuffer>();
            _sendSignal = new SemaphoreSlim(0);
            _transportResource = transportResource;
            _onClosed = onClosed;
            _pendingSendCapacity = pendingSendCapacity;
        }

        /// <summary>
        /// 테스트와 후속 송신 펌프 구현에서 pending 큐 경계를 확인하기 위한 현재 대기 송신 수이다.
        /// close 와 enqueue 가 같은 lock 을 사용하므로 이 값은 관측 시점의 일관된 스냅샷이다.
        /// </summary>
        internal int PendingSendCount
        {
            get
            {
                lock (_gate)
                {
                    return _pendingSends.Count;
                }
            }
        }

        /// <summary>
        /// open 연결이면 송신 요청을 pending 큐에 넣고 Transport 가 해당 ref 의 소유권을 가진다.
        /// closed 연결이면 큐에 넣지 않고 false 를 반환해 호출자가 자신이 추가한 ref 를 Release 하게 한다.
        /// pending 큐가 이미 가득 찼으면 D012에 따라 가장 오래된 pending 항목을 drop 하고 그 Transport 소유 ref 를 반환한다.
        /// </summary>
        internal bool TryAcceptSend(TransportSendBuffer sendBuffer)
        {
            bool shouldWakePump;
            TransportSendBuffer? evictedSend = null;

            lock (_gate)
            {
                if (_closed)
                    return false;

                shouldWakePump = _pendingSends.Count == 0;

                if (_pendingSends.Count == _pendingSendCapacity)
                    evictedSend = _pendingSends.Dequeue();

                _pendingSends.Enqueue(sendBuffer);
            }

            // evict 대상 선택과 큐 제거는 _gate 안에서 끝낸다. Release 는 pool 반환까지 이어질 수 있으므로
            // lock 밖에서 수행해 producer/consumer/close 직렬화 범위를 queue mutation 으로만 제한한다.
            if (evictedSend.HasValue)
                evictedSend.Value.Buffer.Release();

            // 빈 큐에서 첫 항목이 들어올 때만 깨워도 단일 펌프가 drain 하면서 뒤따라온 항목을 모두 처리한다.
            // 매 enqueue 마다 깨우면 불필요한 signal 토큰이 쌓여 hot path 관측 비용이 커진다.
            if (shouldWakePump)
                _sendSignal.Release();

            return true;
        }

        /// <summary>
        /// 단일 송신 펌프가 pending 큐에서 다음 항목을 가져가 in-flight handle 로 감싼다.
        /// handle 은 완료, 취소, unwind 중 어떤 경로에서도 Dispose/Complete 로 Transport 소유 ref 를 반환한다.
        /// </summary>
        internal bool TryBeginInFlightSend(out InFlightSend? inFlightSend)
        {
            lock (_gate)
            {
                if (_closed || _pendingSends.Count == 0)
                {
                    inFlightSend = null;
                    return false;
                }

                inFlightSend = new InFlightSend(this, _pendingSends.Dequeue());
                return true;
            }
        }

        /// <summary>
        /// 송신 펌프가 pending 항목이 생기거나 close 로 루프를 종료해야 할 때까지 기다린다.
        /// </summary>
        internal Task WaitForSendSignalAsync()
        {
            return _sendSignal.WaitAsync();
        }

        /// <summary>
        /// 송신 펌프가 깨어난 뒤 종료 여부를 확인하기 위한 close 상태 스냅샷이다.
        /// </summary>
        internal bool IsClosed
        {
            get
            {
                lock (_gate)
                {
                    return _closed;
                }
            }
        }

        /// <summary>
        /// 송신 펌프가 in-flight 항목의 완료, 취소, 또는 unwind 를 마칠 때 Transport 소유 ref 를 해제한다.
        /// 이 항목은 이미 pending 큐에서 빠져나왔으므로 close drain 이 다시 만지지 않는다.
        /// </summary>
        private void CompleteInFlightSend(TransportSendBuffer sendBuffer)
        {
            // in-flight 소유권은 단일 송신 펌프가 들고 있으므로 pending 큐 lock 을 다시 잡지 않는다.
            // close 와의 경합에서도 close 는 pending 만 drain 하고, 이미 dequeue 된 ref 는 이 경로에서만 반환된다.
            sendBuffer.Buffer.Release();
        }

        /// <summary>
        /// 송신 펌프가 보유하는 단일 in-flight 송신 소유권 handle 이다.
        /// </summary>
        internal sealed class InFlightSend : IDisposable
        {
            private readonly TransportConnection _connection;
            private readonly TransportSendBuffer _sendBuffer;
            private int _released;

            internal InFlightSend(TransportConnection connection, TransportSendBuffer sendBuffer)
            {
                _connection = connection;
                _sendBuffer = sendBuffer;
            }

            /// <summary>
            /// 실제 socket send 에 넘길 payload 범위이다. handle 이 살아있는 동안만 사용해야 한다.
            /// </summary>
            internal TransportSendBuffer SendBuffer => _sendBuffer;

            /// <summary>
            /// 정상 completion callback 에서 Transport 소유 ref 를 반환한다.
            /// </summary>
            internal void Complete()
            {
                ReleaseOnce();
            }

            /// <summary>
            /// 송신 펌프가 close, 취소, 예외 unwind 로 completion 전 빠져나갈 때도 ref 를 반환한다.
            /// </summary>
            public void Dispose()
            {
                ReleaseOnce();
            }

            private void ReleaseOnce()
            {
                // completion 과 unwind/finally 가 모두 지나더라도 실제 Release 는 한 번만 수행한다.
                // 이중 Dispose 는 펌프 finally 중첩이나 테스트 정리 경로에서 흔히 생길 수 있으므로 idempotent 하게 둔다.
                if (Interlocked.Exchange(ref _released, 1) != 0)
                    return;

                _connection.CompleteInFlightSend(_sendBuffer);
            }
        }

        public void Close()
        {
            IDisposable? transportResource;
            Action<TransportConnection>? onClosed;

            lock (_gate)
            {
                if (_closed)
                    return;

                // closed 표시와 pending drain 을 같은 lock 안에서 처리한다.
                // 그래야 TrySend 가 성공한 항목은 반드시 큐에 있거나 이미 펌프가 가져간 상태 중 하나이고,
                // close 이후 새 항목이 drain 과 경합해 누락되지 않는다.
                _closed = true;

                while (_pendingSends.Count != 0)
                {
                    TransportSendBuffer pending = _pendingSends.Dequeue();
                    pending.Buffer.Release();
                }

                transportResource = _transportResource;
                onClosed = _onClosed;
            }

            // pending 이 비어 펌프가 대기 중일 수 있으므로 close 도 wake-up 신호를 보낸다.
            // 펌프는 깨어난 뒤 IsClosed 를 확인하고 루프를 빠져나간다.
            _sendSignal.Release();

            // close callback 과 socket dispose 는 외부 코드로 나가는 작업이므로 connection lock 밖에서 수행한다.
            // transport tracking 제거를 먼저 수행해 dispose 가 잠깐 블록되더라도 닫힌 연결 참조가 목록에 남지 않게 한다.
            onClosed?.Invoke(this);

            // 연결이 실제 socket 같은 backend 자원을 감싸는 경우, public Close 계약이 그 자원 수명까지
            // 함께 닫아야 한다. 아직 recv 조립 버퍼는 없지만, 이후 추가될 때도 이 close 경로에 묶인다.
            transportResource?.Dispose();
        }

        public void Dispose()
        {
            Close();
        }
    }
}
