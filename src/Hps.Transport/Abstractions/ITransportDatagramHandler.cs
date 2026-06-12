using System.Net;
using Hps.Buffers;

namespace Hps.Transport
{
    /// <summary>
    /// Transport UDP receive pump 가 상위 계층으로 datagram 소유권을 넘기는 동기 콜백 계약이다.
    ///
    /// TCP 수신은 byte stream 조각이므로 borrowed <see cref="TransportReceiveBuffer"/> 로 충분하지만,
    /// UDP publish 는 D009에 따라 datagram 을 <see cref="RefCountedBuffer"/> 로 직접 받아 팬아웃 payload 로
    /// 사용할 수 있어야 한다. 따라서 이 handler 는 datagram buffer 의 최초 참조를 넘겨받고,
    /// 처리 완료 또는 fan-out 실패 시 직접 <see cref="RefCountedBuffer.Release"/> 해야 한다.
    /// </summary>
    public interface ITransportDatagramHandler
    {
        /// <summary>
        /// 지정한 UDP endpoint 에 수신된 datagram 을 전달한다.
        ///
        /// <paramref name="datagram"/> 은 수신된 payload 길이로 <see cref="RefCountedBuffer.Length"/> 가 설정된
        /// 소유권 버퍼이다. handler 는 이 참조를 소유하며, 필요하면 fan-out 전에 AddRef 한 뒤 마지막에 Release 해야 한다.
        /// handler 가 예외를 던져도 datagram 참조 반환 책임은 handler 에 남아 있으며, Transport 는 해당 endpoint 를 닫고
        /// <see cref="OnDatagramEndpointClosed"/> 로 수명 종료를 알린다.
        /// </summary>
        void OnDatagramReceived(IUdpEndpoint endpoint, EndPoint remoteEndPoint, RefCountedBuffer datagram);

        /// <summary>
        /// Transport 가 UDP endpoint 종료를 관측했음을 알린다.
        /// </summary>
        void OnDatagramEndpointClosed(IUdpEndpoint endpoint);
    }
}
