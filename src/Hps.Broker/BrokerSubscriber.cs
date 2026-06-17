using System;
using System.Net;
using System.Runtime.CompilerServices;
using Hps.Transport;

namespace Hps.Broker
{
    /// <summary>
    /// Broker routing table 에 저장되는 발행 대상 값이다.
    ///
    /// 현재 구현은 TCP connection 만 실제 send target 으로 감싸지만, routing table 과 publisher 가
    /// raw <see cref="IConnection"/> 배열에 직접 묶이지 않도록 먼저 경계를 분리한다. 이후 UDP broker 를
    /// 붙일 때도 같은 구독자 값 모델 안에 UDP endpoint send target 을 추가할 수 있다.
    /// </summary>
    public readonly struct BrokerSubscriber : IEquatable<BrokerSubscriber>
    {
        private readonly IConnection? _tcpConnection;
        private readonly IUdpEndpoint? _udpEndpoint;
        private readonly EndPoint? _udpRemoteEndPoint;

        private BrokerSubscriber(
            EndpointTransportKind transportKind,
            IConnection? tcpConnection,
            IUdpEndpoint? udpEndpoint,
            EndPoint? udpRemoteEndPoint)
        {
            TransportKind = transportKind;
            _tcpConnection = tcpConnection;
            _udpEndpoint = udpEndpoint;
            _udpRemoteEndPoint = udpRemoteEndPoint;
        }

        /// <summary>
        /// 이 구독자가 사용하는 transport 종류다.
        ///
        /// TCP/UDP endpoint 를 같은 routing table 에 담더라도 fan-out 경로가 어떤 send 계약을 호출해야 하는지
        /// publisher 가 분기할 수 있게 하는 최소 식별값이다.
        /// </summary>
        public EndpointTransportKind TransportKind { get; }

        internal bool IsValid
        {
            get
            {
                if (TransportKind == EndpointTransportKind.Tcp)
                    return _tcpConnection != null;

                if (TransportKind == EndpointTransportKind.Udp)
                    return _udpEndpoint != null && _udpRemoteEndPoint != null;

                return false;
            }
        }

        internal IConnection TcpConnection
        {
            get
            {
                if (TransportKind != EndpointTransportKind.Tcp || _tcpConnection == null)
                    throw new InvalidOperationException("TCP 구독자가 아닌 BrokerSubscriber 에서 TCP connection 을 꺼낼 수 없다.");

                return _tcpConnection;
            }
        }

        internal IUdpEndpoint UdpEndpoint
        {
            get
            {
                if (TransportKind != EndpointTransportKind.Udp || _udpEndpoint == null)
                    throw new InvalidOperationException("UDP 구독자가 아닌 BrokerSubscriber 에서 UDP endpoint 를 꺼낼 수 없다.");

                return _udpEndpoint;
            }
        }

        internal EndPoint UdpRemoteEndPoint
        {
            get
            {
                if (TransportKind != EndpointTransportKind.Udp || _udpRemoteEndPoint == null)
                    throw new InvalidOperationException("UDP 구독자가 아닌 BrokerSubscriber 에서 remote endpoint 를 꺼낼 수 없다.");

                return _udpRemoteEndPoint;
            }
        }

        /// <summary>
        /// TCP connection 을 Broker 발행 대상으로 감싼다.
        ///
        /// connection 객체의 reference identity 가 구독자 identity 이므로 reconnect 이후 같은 endpoint 로 취급해야 하는
        /// stable id 바인딩은 이 값 타입이 아니라 후속 endpoint registry 단계에서 다룬다.
        /// </summary>
        public static BrokerSubscriber ForTcp(IConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            return new BrokerSubscriber(EndpointTransportKind.Tcp, connection, null, null);
        }

        /// <summary>
        /// UDP local endpoint 와 remote endpoint 조합을 Broker 발행 대상으로 감싼다.
        ///
        /// D060의 v1 정책은 stable subscriber id 를 만들지 않고, 실제 datagram 송신에 필요한 runtime handle 조합을
        /// 구독자 identity 로 사용한다. 따라서 같은 local endpoint 객체와 같은 remote endpoint 값이 같은 UDP 구독자를 뜻한다.
        /// </summary>
        public static BrokerSubscriber ForUdp(IUdpEndpoint endpoint, EndPoint remoteEndPoint)
        {
            if (endpoint == null)
                throw new ArgumentNullException(nameof(endpoint));
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));

            return new BrokerSubscriber(EndpointTransportKind.Udp, null, endpoint, remoteEndPoint);
        }

        internal bool TrySend(ITransport transport, TransportSendBuffer sendBuffer)
        {
            if (transport == null)
                throw new ArgumentNullException(nameof(transport));

            if (TransportKind == EndpointTransportKind.Tcp)
            {
                // TCP subscriber outbound 는 D065에 따라 message boundary 를 length prefix 로 싣는다.
                // payload 자체는 기존 RefCountedBuffer slice 를 공유하고, header 는 Transport send item metadata 로만 붙인다.
                return transport.TrySend(TcpConnection, sendBuffer.WithLengthPrefix());
            }

            if (TransportKind == EndpointTransportKind.Udp)
                return transport.TrySendTo(UdpEndpoint, UdpRemoteEndPoint, sendBuffer);

            throw new InvalidOperationException("지원하지 않는 BrokerSubscriber transport kind 이다.");
        }

        /// <summary>
        /// 같은 transport kind 이고 같은 endpoint handle 을 가리키면 같은 구독자로 취급한다.
        ///
        /// TCP v1 에서는 <see cref="IConnection"/> reference identity 를 사용한다. 값 비교를 사용하면 서로 다른
        /// connection 구현이 Equals 를 재정의했을 때 topic set 의 중복 제거 기준이 transport lifecycle 과 어긋날 수 있다.
        /// </summary>
        public bool Equals(BrokerSubscriber other)
        {
            if (TransportKind != other.TransportKind)
                return false;

            if (TransportKind == EndpointTransportKind.Tcp)
                return object.ReferenceEquals(_tcpConnection, other._tcpConnection);

            if (TransportKind == EndpointTransportKind.Udp)
            {
                return object.ReferenceEquals(_udpEndpoint, other._udpEndpoint)
                    && object.Equals(_udpRemoteEndPoint, other._udpRemoteEndPoint);
            }

            return false;
        }

        public override bool Equals(object? obj)
        {
            if (obj is BrokerSubscriber)
                return Equals((BrokerSubscriber)obj);

            return false;
        }

        public override int GetHashCode()
        {
            if (TransportKind == EndpointTransportKind.Tcp && _tcpConnection != null)
                return ((int)EndpointTransportKind.Tcp * 397) ^ RuntimeHelpers.GetHashCode(_tcpConnection);

            if (TransportKind == EndpointTransportKind.Udp && _udpEndpoint != null && _udpRemoteEndPoint != null)
            {
                unchecked
                {
                    int hash = ((int)EndpointTransportKind.Udp * 397) ^ RuntimeHelpers.GetHashCode(_udpEndpoint);
                    return (hash * 397) ^ _udpRemoteEndPoint.GetHashCode();
                }
            }

            return (int)TransportKind;
        }
    }
}
