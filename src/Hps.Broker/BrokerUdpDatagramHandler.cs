using System;
using System.Net;
using System.Text;
using Hps.Buffers;
using Hps.Protocol;
using Hps.Transport;

namespace Hps.Broker
{
    /// <summary>
    /// UDP datagram self-command 를 Broker routing table 과 fan-out 경로로 연결하는 handler 이다.
    /// </summary>
    public sealed class BrokerUdpDatagramHandler : ITransportDatagramHandler
    {
        private readonly SubscriptionTable _subscriptions;
        private readonly BrokerPublisher _publisher;
        private readonly UdpRemoteLeaseTracker _udpLeases;

        /// <summary>
        /// UDP command 처리를 위한 routing table 과 publisher 를 지정한다.
        /// </summary>
        public BrokerUdpDatagramHandler(SubscriptionTable subscriptions, BrokerPublisher publisher)
            : this(subscriptions, publisher, UdpLeaseOptions.Disabled, TimeProvider.System)
        {
        }

        internal BrokerUdpDatagramHandler(
            SubscriptionTable subscriptions,
            BrokerPublisher publisher,
            UdpLeaseOptions leaseOptions,
            TimeProvider timeProvider)
        {
            if (subscriptions == null)
                throw new ArgumentNullException(nameof(subscriptions));
            if (publisher == null)
                throw new ArgumentNullException(nameof(publisher));
            if (leaseOptions == null)
                throw new ArgumentNullException(nameof(leaseOptions));
            if (timeProvider == null)
                throw new ArgumentNullException(nameof(timeProvider));

            _subscriptions = subscriptions;
            _publisher = publisher;
            _udpLeases = new UdpRemoteLeaseTracker(subscriptions, leaseOptions, timeProvider);
        }

        /// <summary>
        /// UDP datagram command 를 처리한다.
        /// </summary>
        public void OnDatagramReceived(IUdpEndpoint endpoint, EndPoint remoteEndPoint, RefCountedBuffer datagram)
        {
            if (endpoint == null)
                throw new ArgumentNullException(nameof(endpoint));
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));
            if (datagram == null)
                throw new ArgumentNullException(nameof(datagram));

            try
            {
                TcpCommand command;
                TcpCommandDecodeError error;
                if (!TcpCommandDecoder.TryDecode(datagram.Memory.Span.Slice(0, datagram.Length), out command, out error))
                {
                    // UDP endpoint 는 여러 remote 가 공유할 수 있다. 따라서 malformed datagram 하나 때문에 endpoint 전체를
                    // 닫지 않고, D060 정책대로 현재 datagram 만 폐기한다. protocol error 응답은 아직 범위 밖이다.
                    return;
                }

                if (command.Kind == TcpCommandKind.Subscribe)
                {
                    string topic = DecodeTopic(command.Topic);
                    _udpLeases.Subscribe(topic, endpoint, remoteEndPoint);
                    return;
                }

                if (command.Kind == TcpCommandKind.Unsubscribe)
                {
                    string topic = DecodeTopic(command.Topic);
                    _udpLeases.Unsubscribe(topic, endpoint, remoteEndPoint);
                    return;
                }

                if (command.Kind == TcpCommandKind.Publish)
                {
                    string topic = DecodeTopic(command.Topic);
                    _udpLeases.MarkPublishActivity(endpoint, remoteEndPoint);
                    _publisher.Publish(topic, datagram, command.PayloadOffset, command.Payload.Length);
                    return;
                }
            }
            finally
            {
                datagram.Release();
            }
        }

        /// <summary>
        /// 닫힌 UDP endpoint 에 묶인 broker 구독을 정리한다.
        /// </summary>
        public void OnDatagramEndpointClosed(IUdpEndpoint endpoint)
        {
            if (endpoint == null)
                throw new ArgumentNullException(nameof(endpoint));

            _udpLeases.RemoveEndpoint(endpoint);
        }

        internal int SweepExpiredUdpLeases(DateTimeOffset now)
        {
            return _udpLeases.SweepExpired(now);
        }

        private static string DecodeTopic(ReadOnlySpan<byte> topic)
        {
            // topic 은 routing table key 로 datagram 수명 이후에도 남을 수 있으므로 string 으로 명시 복사한다.
            // payload 는 RefCountedBuffer range 로 유지하지만 topic key 는 dictionary lookup 에 안정적인 관리 객체가 필요하다.
            return Encoding.ASCII.GetString(topic);
        }
    }
}
