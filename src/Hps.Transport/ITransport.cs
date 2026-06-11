using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Hps.Transport
{
    /// <summary>
    /// OS별 소켓 백엔드(SAEA/RIO/io_uring)를 상위 계층에서 숨기는 Transport 루트 계약이다.
    ///
    /// 이 계약은 backend 구현 타입이나 <c>SocketAsyncEventArgs</c> 같은 세부사항을 노출하지 않고,
    /// 상위 계층이 TCP listener 와 연결 핸들만 다루게 한다. UDP datagram 수신/송신 계약은 accept 개념이
    /// 없으므로 별도 단위에서 추가한다.
    /// </summary>
    public interface ITransport : IDisposable
    {
        /// <summary>
        /// socket recv 로 들어온 byte stream 과 연결 종료 알림을 받을 handler 를 등록한다.
        ///
        /// Transport 는 handler 를 동기적으로 호출하며, 전달되는 <see cref="TransportReceiveBuffer"/> 는 콜백 동안만
        /// 유효하다. 상위 계층이 데이터를 보관해야 하면 자신의 소유권 버퍼로 복사해야 한다.
        /// </summary>
        void SetReceiveHandler(ITransportReceiveHandler receiveHandler);

        /// <summary>
        /// TCP 수신 대기를 시작하고 accept 가능한 listener 를 반환한다.
        ///
        /// 반환된 listener 는 listen socket 수명만 관리한다. accept 된 각 연결의 송신 큐와 수신 조립 버퍼는
        /// <see cref="IConnection"/> 수명 계약으로 별도 관리된다.
        /// </summary>
        ValueTask<IConnectionListener> ListenTcpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default);

        /// <summary>
        /// 원격 TCP endpoint 로 outbound 연결을 만든다.
        ///
        /// 반환된 연결은 inbound accept 연결과 같은 <see cref="IConnection"/> 계약을 따른다. 즉 close 이후
        /// 송신은 원자적으로 거부되어야 하고, pending/in-flight/조립 중 버퍼는 누수 없이 Release 되어야 한다.
        /// </summary>
        ValueTask<IConnection> ConnectTcpAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken = default);

        /// <summary>
        /// 지정한 연결로 송신을 시도한다.
        ///
        /// 반환값이 <c>true</c> 이면 Transport 구현은 <paramref name="sendBuffer"/> 의 버퍼 참조 1개를
        /// 소유한다. 이후 송신 완료, backpressure drop, 또는 연결 close/drain 중 정확히 한 경로에서
        /// 해당 버퍼를 Release 해야 한다.
        ///
        /// 반환값이 <c>false</c> 이면 Transport 는 소유권을 전혀 가져가지 않았다. 호출자는 D009 계약에 따라
        /// 자신이 추가했던 구독자 ref 를 즉시 Release 해야 한다. close 와 send 경합에서도 이 결정은
        /// 원자적으로 보여야 한다.
        /// </summary>
        bool TrySend(IConnection connection, TransportSendBuffer sendBuffer);

        /// <summary>
        /// Transport 구현을 시작한다. 내부 워커, 등록 버퍼, completion queue 같은 backend 자원은
        /// 이 수명주기 안에서 준비되어야 한다.
        /// </summary>
        ValueTask StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Transport 구현을 중지하고 더 이상 새 연결/송신을 받지 않는다. 구현은 보유 중인 연결을 닫아
        /// <see cref="IConnection.Close"/> 계약을 만족시켜야 한다.
        /// </summary>
        ValueTask StopAsync(CancellationToken cancellationToken = default);
    }
}
