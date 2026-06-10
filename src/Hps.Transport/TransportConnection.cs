using System;
using System.Collections.Generic;

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
        private readonly object _gate;
        private readonly Queue<TransportSendBuffer> _pendingSends;
        private bool _closed;

        internal TransportConnection()
        {
            _gate = new object();
            _pendingSends = new Queue<TransportSendBuffer>();
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
        /// </summary>
        internal bool TryAcceptSend(TransportSendBuffer sendBuffer)
        {
            lock (_gate)
            {
                if (_closed)
                    return false;

                _pendingSends.Enqueue(sendBuffer);
                return true;
            }
        }

        /// <summary>
        /// 단일 송신 펌프가 pending 큐에서 다음 항목을 가져간다.
        /// dequeue 에 성공한 항목은 더 이상 close drain 대상이 아니며, 펌프 완료 경로가 Release 해야 한다.
        /// </summary>
        internal bool TryDequeueSend(out TransportSendBuffer sendBuffer)
        {
            lock (_gate)
            {
                if (_closed || _pendingSends.Count == 0)
                {
                    sendBuffer = default(TransportSendBuffer);
                    return false;
                }

                sendBuffer = _pendingSends.Dequeue();
                return true;
            }
        }

        /// <summary>
        /// 송신 펌프가 in-flight 항목의 완료, 취소, 또는 unwind 를 마칠 때 Transport 소유 ref 를 해제한다.
        /// 이 항목은 이미 pending 큐에서 빠져나왔으므로 close drain 이 다시 만지지 않는다.
        /// </summary>
        internal void CompleteInFlightSend(TransportSendBuffer sendBuffer)
        {
            // in-flight 소유권은 단일 송신 펌프가 들고 있으므로 pending 큐 lock 을 다시 잡지 않는다.
            // close 와의 경합에서도 close 는 pending 만 drain 하고, 이미 dequeue 된 ref 는 이 경로에서만 반환된다.
            sendBuffer.Buffer.Release();
        }

        public void Close()
        {
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
            }
        }

        public void Dispose()
        {
            Close();
        }
    }
}
