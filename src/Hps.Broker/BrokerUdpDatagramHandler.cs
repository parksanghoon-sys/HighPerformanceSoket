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
        private readonly SubscriberRegistry? _subscriberRegistry;
        private readonly TimeProvider _timeProvider;

        /// <summary>
        /// stable identity 와 lease sweep 없이 기본 UDP command 처리를 수행한다.
        /// </summary>
        public BrokerUdpDatagramHandler(SubscriptionTable subscriptions, BrokerPublisher publisher)
            : this(subscriptions, publisher, UdpLeaseOptions.Disabled, TimeProvider.System, null)
        {
        }

        internal BrokerUdpDatagramHandler(
            SubscriptionTable subscriptions,
            BrokerPublisher publisher,
            UdpLeaseOptions leaseOptions,
            TimeProvider timeProvider)
            : this(subscriptions, publisher, leaseOptions, timeProvider, null)
        {
        }

        internal BrokerUdpDatagramHandler(
            SubscriptionTable subscriptions,
            BrokerPublisher publisher,
            UdpLeaseOptions leaseOptions,
            TimeProvider timeProvider,
            SubscriberRegistry? subscriberRegistry)
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
            _subscriberRegistry = subscriberRegistry;
            _timeProvider = timeProvider;
            _udpLeases = new UdpRemoteLeaseTracker(subscriptions, leaseOptions, timeProvider);
        }

        internal int UdpLeaseCount
        {
            // optional lease tracker 는 routing table 과 별개 수명 상태를 가진다.
            // internal 테스트는 이 값을 통해 REGISTER/rebind/endpoint-close 가 lease metadata 까지 정리하는지 확인한다.
            get { return _udpLeases.LeaseCount; }
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
                    // UDP endpoint 는 여러 remote 가 공유할 수 있으므로 malformed datagram 하나 때문에 endpoint 전체를 닫지 않는다.
                    // TCP와 달리 datagram 경계는 이미 보존되므로 현재 datagram 만 폐기하는 것이 v1 정책이다.
                    return;
                }

                BrokerSubscriber target = BrokerSubscriber.ForUdp(endpoint, remoteEndPoint);

                if (command.Kind == TcpCommandKind.Register)
                {
                    RegisterUdpTarget(target, DecodeTopic(command.Topic));
                    return;
                }

                if (command.Kind == TcpCommandKind.Unregister)
                {
                    if (_subscriberRegistry != null)
                        _subscriberRegistry.Unregister(SubscriberIdentity.Create(DecodeTopic(command.Topic)), target);
                    _udpLeases.RemoveRemote(endpoint, remoteEndPoint);
                    return;
                }

                if (command.Kind == TcpCommandKind.Subscribe)
                {
                    string topic = DecodeTopic(command.Topic);
                    _udpLeases.Subscribe(topic, endpoint, remoteEndPoint);
                    if (_subscriberRegistry != null)
                        _subscriberRegistry.Subscribe(topic, target);
                    return;
                }

                if (command.Kind == TcpCommandKind.Unsubscribe)
                {
                    string topic = DecodeTopic(command.Topic);
                    _udpLeases.Unsubscribe(topic, endpoint, remoteEndPoint);
                    if (_subscriberRegistry != null)
                        _subscriberRegistry.Unsubscribe(topic, target);
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

            if (_subscriberRegistry != null)
                _subscriberRegistry.RemoveUdpEndpoint(endpoint, _timeProvider.GetUtcNow());

            _udpLeases.RemoveEndpoint(endpoint);
        }

        internal int SweepExpiredUdpLeases(DateTimeOffset now)
        {
            return _udpLeases.SweepExpired(now);
        }

        private void RegisterUdpTarget(BrokerSubscriber target, string identityValue)
        {
            if (_subscriberRegistry == null)
                return;

            SubscriberRegistrationResult result = _subscriberRegistry.Register(
                SubscriberIdentity.Create(identityValue),
                target,
                out BrokerSubscriber? replacedTarget,
                out string[] reboundTopics);

            if (result == SubscriberRegistrationResult.TargetAlreadyRegisteredWithDifferentIdentity)
                return;

            if (replacedTarget.HasValue && replacedTarget.Value.TransportKind == EndpointTransportKind.Udp)
                _udpLeases.RemoveRemote(replacedTarget.Value.UdpEndpoint, replacedTarget.Value.UdpRemoteEndPoint);

            _udpLeases.ReplaceSubscribedTopics(target.UdpEndpoint, target.UdpRemoteEndPoint, reboundTopics);
        }

        private static string DecodeTopic(ReadOnlySpan<byte> topic)
        {
            // topic/identity token 은 routing table key 로 datagram 수명 이후에도 남는다.
            // payload 는 RefCountedBuffer range 로 유지하지만 key token 은 명시적으로 string 으로 복사한다.
            return Encoding.ASCII.GetString(topic);
        }
    }
}
