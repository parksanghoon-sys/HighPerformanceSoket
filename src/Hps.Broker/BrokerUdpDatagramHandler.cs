using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Hps.Buffers;
using Hps.Protocol;
using Hps.Transport;

namespace Hps.Broker
{
    /// <summary>
    /// UDP datagram self-command 를 Broker routing table 과 fan-out 경로로 연결하는 handler 이다.
    ///
    /// UDP receive callback 과 host timer callback 은 서로 다른 thread 에서 들어올 수 있으므로,
    /// lease tracker 와 stable registry 를 함께 갱신하는 구간은 handler gate 로 직렬화한다.
    /// </summary>
    public sealed class BrokerUdpDatagramHandler : ITransportDatagramHandler
    {
        private readonly object _gate;
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

            _gate = new object();
            _subscriptions = subscriptions;
            _publisher = publisher;
            _subscriberRegistry = subscriberRegistry;
            _timeProvider = timeProvider;
            _udpLeases = new UdpRemoteLeaseTracker(subscriptions, leaseOptions, timeProvider);
        }

        internal int UdpLeaseCount
        {
            // optional lease tracker 는 routing table 과 별개의 수명 상태를 가진다.
            // 테스트는 이 값을 통해 REGISTER/rebind/endpoint-close 가 lease metadata 까지 정리하는지 확인한다.
            get { return _udpLeases.LeaseCount; }
        }

        /// <summary>
        /// UDP datagram command 를 처리한다.
        ///
        /// 상태 mutation 은 `_gate` 안에서 직렬화하지만, PUBLISH fan-out 은 lock 밖에서 수행한다.
        /// transport send 경로까지 handler gate 로 묶으면 느린 subscriber 가 다른 UDP command 처리를 지연시킬 수 있기 때문이다.
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
                    // UDP endpoint 는 여러 remote 가 공유할 수 있으므로 malformed datagram 하나 때문에
                    // endpoint 전체를 닫지 않는다. TCP 와 달리 현재 datagram 만 버리는 것이 v1 정책이다.
                    return;
                }

                BrokerSubscriber target = BrokerSubscriber.ForUdp(endpoint, remoteEndPoint);
                string? publishTopic = null;
                int publishOffset = 0;
                int publishLength = 0;
                bool shouldPublish = false;

                lock (_gate)
                {
                    if (command.Kind == TcpCommandKind.Register)
                    {
                        SubscriberIdentity identity;
                        if (!TryDecodeIdentity(command.Topic, out identity))
                            return;

                        RegisterUdpTarget(target, identity);
                        return;
                    }

                    if (command.Kind == TcpCommandKind.Unregister)
                    {
                        SubscriberIdentity identity;
                        if (!TryDecodeIdentity(command.Topic, out identity))
                            return;

                        if (_subscriberRegistry != null)
                            _subscriberRegistry.Unregister(identity, target);
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
                        publishTopic = DecodeTopic(command.Topic);
                        publishOffset = command.PayloadOffset;
                        publishLength = command.Payload.Length;
                        _udpLeases.MarkPublishActivity(endpoint, remoteEndPoint);
                        shouldPublish = true;
                    }
                }

                if (shouldPublish)
                {
                    _publisher.Publish(publishTopic!, datagram, publishOffset, publishLength);
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

            lock (_gate)
            {
                if (_subscriberRegistry != null)
                    _subscriberRegistry.RemoveUdpEndpoint(endpoint, _timeProvider.GetUtcNow());

                _udpLeases.RemoveEndpoint(endpoint);
            }
        }

        internal int SweepExpiredUdpLeases(DateTimeOffset now)
        {
            lock (_gate)
            {
                if (_subscriberRegistry == null)
                    return _udpLeases.SweepExpired(now);

                List<BrokerSubscriber> expiredTargets = new List<BrokerSubscriber>();
                int removed = _udpLeases.SweepExpired(now, expiredTargets);

                // lease tracker 는 routing/lease table owner 이고 registry 는 stable identity current target owner 이다.
                // 같은 handler gate 안에서 snapshot cleanup 까지 끝내야 동시 REGISTER 가 expired snapshot 과
                // registry cleanup 사이에 끼어들어 새 online 상태를 stale disconnect 로 덮지 못한다.
                for (int index = 0; index < expiredTargets.Count; index++)
                    _subscriberRegistry.RemoveTarget(expiredTargets[index], now);

                return removed;
            }
        }

        private void RegisterUdpTarget(BrokerSubscriber target, SubscriberIdentity identity)
        {
            if (_subscriberRegistry == null)
                return;

            SubscriberRegistrationResult result = _subscriberRegistry.Register(
                identity,
                target,
                out BrokerSubscriber? replacedTarget,
                out string[] reboundTopics);

            if (result == SubscriberRegistrationResult.TargetAlreadyRegisteredWithDifferentIdentity)
                return;

            if (replacedTarget.HasValue && replacedTarget.Value.TransportKind == EndpointTransportKind.Udp)
                _udpLeases.RemoveRemote(replacedTarget.Value.UdpEndpoint, replacedTarget.Value.UdpRemoteEndPoint);

            _udpLeases.ReplaceSubscribedTopics(target.UdpEndpoint, target.UdpRemoteEndPoint, reboundTopics);
        }

        private static bool TryDecodeIdentity(ReadOnlySpan<byte> topic, out SubscriberIdentity identity)
        {
            string value = DecodeTopic(topic);
            identity = default(SubscriberIdentity);

            if (value.Length == 0)
                return false;

            // UDP endpoint 는 여러 remote 가 공유하므로 identity validation 실패를 예외로 흘리면
            // SAEA receive loop 에서 endpoint 전체 close 로 이어질 수 있다. wire input 은 먼저 검사해 datagram drop 으로 격리한다.
            for (int index = 0; index < value.Length; index++)
            {
                if (char.IsWhiteSpace(value[index]))
                    return false;
            }

            identity = SubscriberIdentity.Create(value);
            return true;
        }

        private static string DecodeTopic(ReadOnlySpan<byte> topic)
        {
            // topic/identity token 은 routing table key 로 datagram 수명 이후에도 남는다.
            // payload 는 RefCountedBuffer range 로 유지하지만 key token 은 명시적으로 string 으로 복사한다.
            return Encoding.ASCII.GetString(topic);
        }
    }
}
