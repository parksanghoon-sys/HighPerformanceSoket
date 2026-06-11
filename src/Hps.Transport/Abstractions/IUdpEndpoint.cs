using System;
using System.Net;

namespace Hps.Transport
{
    /// <summary>
    /// UDP datagram 송수신을 위해 Transport 가 소유하는 bind endpoint 수명 경계이다.
    ///
    /// UDP 는 TCP 처럼 accept 된 연결이 없으므로 <see cref="IConnection"/> 으로 표현하지 않는다.
    /// 이 핸들은 bind 된 socket 의 수명과 로컬 endpoint 만 노출하고, 실제 datagram 송신 시도와
    /// 버퍼 소유권 판정은 <see cref="ITransport.TrySendTo(IUdpEndpoint, EndPoint, TransportSendBuffer)"/> 가 맡는다.
    /// </summary>
    public interface IUdpEndpoint : IDisposable
    {
        /// <summary>
        /// 실제 bind 된 로컬 endpoint 이다. 포트 0으로 bind 한 경우 OS가 선택한 포트를 포함한다.
        /// </summary>
        EndPoint LocalEndPoint { get; }

        /// <summary>
        /// UDP endpoint 를 닫고 이후 datagram 송신 시도를 거부한다.
        /// </summary>
        void Close();
    }
}
