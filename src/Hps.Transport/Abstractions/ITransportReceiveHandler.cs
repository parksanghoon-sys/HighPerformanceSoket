namespace Hps.Transport
{
    /// <summary>
    /// Transport 수신 펌프가 상위 계층으로 raw TCP byte stream 과 연결 종료를 전달하는 동기 콜백 계약이다.
    ///
    /// 이 인터페이스는 socket, SAEA, RIO/io_uring completion 타입을 노출하지 않는다. 수신 데이터는
    /// <see cref="TransportReceiveBuffer"/> 로 빌려주며, handler 는 콜백 안에서 처리하거나 필요한 바이트만
    /// 별도 소유권 버퍼로 복사해야 한다.
    /// </summary>
    public interface ITransportReceiveHandler
    {
        /// <summary>
        /// 지정한 연결에서 수신된 byte stream 조각을 전달한다.
        ///
        /// <paramref name="receiveBuffer"/> 는 콜백이 반환될 때까지만 유효하다. TCP 프레이밍 계층은 이 span 을
        /// 즉시 소비하고, 완성된 payload 수명이 콜백 이후로 이어져야 할 때만 `RefCountedBuffer`로 복사한다.
        /// handler 가 예외를 던지면 Transport 는 해당 연결을 닫고 <see cref="OnConnectionClosed"/> 알림으로 수렴시킨다.
        /// </summary>
        void OnReceived(IConnection connection, TransportReceiveBuffer receiveBuffer);

        /// <summary>
        /// Transport 가 연결 종료를 관측했음을 알린다. 프레이밍 계층은 이 시점에 조립 중이던
        /// `RefCountedBuffer`가 있다면 Release 해야 한다.
        /// </summary>
        void OnConnectionClosed(IConnection connection);
    }
}
