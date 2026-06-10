using System;
using System.Threading;
using System.Threading.Tasks;
using Hps.Buffers;

namespace Hps.Transport
{
    /// <summary>
    /// Transport 구현들이 공유할 송신 소유권 처리 골격이다.
    ///
    /// 실제 SAEA/RIO/io_uring 송신은 파생 구현이 붙이지만, <see cref="ITransport.TrySend"/> 의
    /// "성공 시 Transport 소유, 실패 시 호출자 소유" 판정은 backend 마다 달라지면 안 된다.
    /// </summary>
    public abstract class TransportBase : ITransport
    {
        /// <summary>
        /// 새 내부 연결 상태를 만든다. 이후 listen/accept/connect 구현은 이 연결을 <see cref="IConnection"/>
        /// 으로 상위 계층에 넘긴다.
        /// </summary>
        internal TransportConnection CreateConnection()
        {
            return new TransportConnection();
        }

        /// <summary>
        /// 지정한 연결의 pending 송신 큐에 송신 요청을 넣는다.
        /// </summary>
        public bool TrySend(IConnection connection, TransportSendBuffer sendBuffer)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            TransportConnection? transportConnection = connection as TransportConnection;
            if (transportConnection == null)
                throw new ArgumentException("이 Transport 구현이 생성한 연결만 사용할 수 있다.", nameof(connection));

            // Transport 가 큐 소유권을 받기 전에 요청이 실제 live RefCountedBuffer 를 가리키는지 확인한다.
            // TransportSendBuffer 는 struct 이므로 default 값이 public API 로 들어올 수 있고, 이미 반환된 버퍼도
            // 생성 이후 잘못 Release 되었을 수 있다. 여기서 실패시키면 pending drain 시점의 늦은 예외와 누수를 피할 수 있다.
            RefCountedBuffer buffer = sendBuffer.Buffer;
            _ = buffer.Memory;

            return transportConnection.TryAcceptSend(sendBuffer);
        }

        /// <inheritdoc />
        public abstract ValueTask StartAsync(CancellationToken cancellationToken = default);

        /// <inheritdoc />
        public abstract ValueTask StopAsync(CancellationToken cancellationToken = default);

        /// <inheritdoc />
        public virtual void Dispose()
        {
        }
    }
}
