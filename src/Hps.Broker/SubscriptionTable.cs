using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Hps.Transport;

namespace Hps.Broker
{
    /// <summary>
    /// Broker 의 topic 별 구독자 집합을 관리하는 라우팅 테이블이다.
    ///
    /// 내부 값은 TCP connection 자체가 아니라 Broker 발행 대상인 <see cref="BrokerSubscriber"/> 로 저장한다.
    /// 현재 TCP command path 는 compatibility overload 로 들어오지만, UDP broker 를 붙일 때 같은 테이블에
    /// UDP endpoint target 을 담을 수 있도록 routing value 경계를 먼저 분리한다.
    /// </summary>
    public sealed class SubscriptionTable
    {
        private readonly ConcurrentDictionary<string, TopicSubscriptions> _topics;

        /// <summary>
        /// 빈 라우팅 테이블을 만든다.
        /// </summary>
        public SubscriptionTable()
        {
            _topics = new ConcurrentDictionary<string, TopicSubscriptions>(StringComparer.Ordinal);
        }

        /// <summary>
        /// 지정 topic 에 TCP connection 을 구독자로 추가한다.
        ///
        /// TCP command handler 의 기존 진입점을 보존하기 위한 overload 이며, 내부 저장은 endpoint target 값으로 변환한다.
        /// </summary>
        public bool Subscribe(string topic, IConnection connection)
        {
            ValidateTopic(topic);
            ValidateConnection(connection);

            return Subscribe(topic, BrokerSubscriber.ForTcp(connection));
        }

        /// <summary>
        /// 지정 topic 에 Broker 발행 대상 endpoint 를 구독자로 추가한다.
        /// </summary>
        public bool Subscribe(string topic, BrokerSubscriber subscriber)
        {
            ValidateTopic(topic);
            ValidateSubscriber(subscriber);

            TopicSubscriptions subscriptions = _topics.GetOrAdd(topic, CreateTopicSubscriptions);
            return subscriptions.Add(subscriber);
        }

        /// <summary>
        /// 지정 topic 에서 TCP connection 구독을 제거한다.
        /// </summary>
        public bool Unsubscribe(string topic, IConnection connection)
        {
            ValidateTopic(topic);
            ValidateConnection(connection);

            return Unsubscribe(topic, BrokerSubscriber.ForTcp(connection));
        }

        /// <summary>
        /// 지정 topic 에서 Broker 발행 대상 endpoint 구독을 제거한다.
        /// </summary>
        public bool Unsubscribe(string topic, BrokerSubscriber subscriber)
        {
            ValidateTopic(topic);
            ValidateSubscriber(subscriber);

            TopicSubscriptions? subscriptions;
            if (!_topics.TryGetValue(topic, out subscriptions))
                return false;

            // D008: 구독자 set 이 비어도 topic entry 를 즉시 제거하지 않는다.
            // 마지막 제거와 새 구독 추가가 겹칠 때 새 구독자가 사라지는 eager-cleanup 경합을 피하기 위한 정책이다.
            return subscriptions.Remove(subscriber);
        }

        /// <summary>
        /// 지정 connection 을 모든 topic 구독 set 에서 제거하고 실제 제거된 topic 수를 반환한다.
        ///
        /// Transport/Protocol 계층이 connection 종료를 통지했을 때 topic 이름을 모르는 상태에서도 Broker routing table 에
        /// dead TCP connection 참조가 남지 않게 하는 정리 경계다. D008 NoCleanup 정책에 따라 topic entry 자체는 제거하지 않는다.
        /// </summary>
        public int UnsubscribeAll(IConnection connection)
        {
            ValidateConnection(connection);

            BrokerSubscriber subscriber = BrokerSubscriber.ForTcp(connection);
            int removed = 0;

            // ConcurrentDictionary 열거는 mutation 과 동시에 안전하다. connection 종료 정리는 hot publish 경로가 아니므로
            // 전체 topic 을 한 번 훑는 비용을 받아들이고, topic entry 제거 경합을 만들지 않는다.
            foreach (KeyValuePair<string, TopicSubscriptions> pair in _topics)
            {
                if (pair.Value.Remove(subscriber))
                    removed++;
            }

            return removed;
        }

        /// <summary>
        /// 지정 UDP endpoint 에 묶인 모든 remote 구독을 모든 topic set 에서 제거하고 실제 제거된 구독 수를 반환한다.
        ///
        /// UDP endpoint 는 여러 remote sender 를 공유할 수 있으므로 endpoint close 시점에는 remote endpoint 를 특정할 수 없다.
        /// 따라서 같은 local endpoint handle 을 가진 UDP 구독자를 모두 제거해 닫힌 socket 으로 fan-out 이 계속 시도되지 않게 한다.
        /// D008 NoCleanup 정책에 따라 topic entry 자체는 제거하지 않는다.
        /// </summary>
        public int UnsubscribeAll(IUdpEndpoint endpoint)
        {
            ValidateUdpEndpoint(endpoint);

            int removed = 0;

            // Endpoint close cleanup 은 hot publish 경로가 아니므로 전체 topic 을 순회한다.
            // ConcurrentDictionary 열거는 mutation 과 동시에 안전하며, 각 topic set 내부 제거도 TryRemove 로 처리한다.
            foreach (KeyValuePair<string, TopicSubscriptions> pair in _topics)
            {
                removed += pair.Value.RemoveUdpEndpoint(endpoint);
            }

            return removed;
        }

        /// <summary>
        /// 지정 TCP connection 이 topic 에 현재 구독되어 있는지 확인한다.
        /// </summary>
        public bool IsSubscribed(string topic, IConnection connection)
        {
            ValidateTopic(topic);
            ValidateConnection(connection);

            return IsSubscribed(topic, BrokerSubscriber.ForTcp(connection));
        }

        /// <summary>
        /// 지정 endpoint target 이 topic 에 현재 구독되어 있는지 확인한다.
        /// </summary>
        public bool IsSubscribed(string topic, BrokerSubscriber subscriber)
        {
            ValidateTopic(topic);
            ValidateSubscriber(subscriber);

            TopicSubscriptions? subscriptions;
            return _topics.TryGetValue(topic, out subscriptions) && subscriptions.Contains(subscriber);
        }

        /// <summary>
        /// 지정 topic 의 현재 구독자 수를 반환한다.
        /// </summary>
        public int CountSubscribers(string topic)
        {
            ValidateTopic(topic);

            TopicSubscriptions? subscriptions;
            if (!_topics.TryGetValue(topic, out subscriptions))
                return 0;

            return subscriptions.Count;
        }

        /// <summary>
        /// 지정 topic 의 현재 TCP 구독자 connection 을 caller 제공 배열로 복사한다.
        ///
        /// 기존 TCP-only 테스트와 점검 경로를 위한 compatibility API 다. 신규 fan-out 경로는
        /// <see cref="CopySubscribers(string, BrokerSubscriber[])"/> 를 사용한다.
        /// </summary>
        public int CopySubscribers(string topic, IConnection[] destination)
        {
            ValidateTopic(topic);
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            TopicSubscriptions? subscriptions;
            if (!_topics.TryGetValue(topic, out subscriptions))
                return 0;

            return subscriptions.CopyTcpConnectionsTo(destination);
        }

        /// <summary>
        /// 지정 topic 의 현재 구독 endpoint target 들을 caller 제공 배열로 복사한다.
        ///
        /// 반환값은 destination 에 실제로 담긴 개수가 아니라 현재 관측된 전체 구독자 수다. caller 는 반환값이
        /// destination 길이보다 크면 더 큰 배열로 다시 snapshot 을 시도해야 fan-out 대상을 빠뜨리지 않는다.
        /// </summary>
        public int CopySubscribers(string topic, BrokerSubscriber[] destination)
        {
            ValidateTopic(topic);
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            TopicSubscriptions? subscriptions;
            if (!_topics.TryGetValue(topic, out subscriptions))
                return 0;

            return subscriptions.CopyTo(destination);
        }

        private static TopicSubscriptions CreateTopicSubscriptions(string topic)
        {
            return new TopicSubscriptions();
        }

        private static void ValidateTopic(string topic)
        {
            if (topic == null)
                throw new ArgumentNullException(nameof(topic));
            if (topic.Length == 0)
                throw new ArgumentException("Topic 은 비어 있을 수 없다.", nameof(topic));
        }

        private static void ValidateConnection(IConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
        }

        private static void ValidateUdpEndpoint(IUdpEndpoint endpoint)
        {
            if (endpoint == null)
                throw new ArgumentNullException(nameof(endpoint));
        }

        private static void ValidateSubscriber(BrokerSubscriber subscriber)
        {
            if (!subscriber.IsValid)
                throw new ArgumentException("Broker 구독자는 유효한 TCP/UDP endpoint target 이어야 한다.", nameof(subscriber));
        }

        private sealed class TopicSubscriptions
        {
            private readonly ConcurrentDictionary<BrokerSubscriber, byte> _subscribers;

            internal TopicSubscriptions()
            {
                _subscribers = new ConcurrentDictionary<BrokerSubscriber, byte>();
            }

            internal int Count => _subscribers.Count;

            internal bool Add(BrokerSubscriber subscriber)
            {
                return _subscribers.TryAdd(subscriber, 0);
            }

            internal bool Remove(BrokerSubscriber subscriber)
            {
                byte ignored;
                return _subscribers.TryRemove(subscriber, out ignored);
            }

            internal int RemoveUdpEndpoint(IUdpEndpoint endpoint)
            {
                int removed = 0;

                // 같은 local UDP endpoint 에 여러 remote 가 구독되어 있을 수 있으므로 remote 값을 모르는 close cleanup 에서는
                // endpoint reference 가 같은 모든 UDP subscriber 를 제거한다. TCP subscriber 는 같은 topic 에 있어도 건드리지 않는다.
                foreach (KeyValuePair<BrokerSubscriber, byte> pair in _subscribers)
                {
                    BrokerSubscriber subscriber = pair.Key;
                    if (subscriber.TransportKind == EndpointTransportKind.Udp
                        && object.ReferenceEquals(subscriber.UdpEndpoint, endpoint)
                        && Remove(subscriber))
                    {
                        removed++;
                    }
                }

                return removed;
            }

            internal bool Contains(BrokerSubscriber subscriber)
            {
                return _subscribers.ContainsKey(subscriber);
            }

            internal int CopyTo(BrokerSubscriber[] destination)
            {
                int total = 0;
                int copied = 0;

                // ConcurrentDictionary 열거는 mutation 과 동시에 안전한 snapshot 성격의 열거를 제공한다.
                // fan-out caller 는 반환된 total 이 destination 길이보다 크면 더 큰 버퍼로 다시 시도해야 한다.
                foreach (KeyValuePair<BrokerSubscriber, byte> pair in _subscribers)
                {
                    if (copied < destination.Length)
                    {
                        destination[copied] = pair.Key;
                        copied++;
                    }

                    total++;
                }

                return total;
            }

            internal int CopyTcpConnectionsTo(IConnection[] destination)
            {
                int total = 0;
                int copied = 0;

                // 내부 모델은 endpoint target 이지만 기존 TCP command/test 경계는 connection 배열을 확인한다.
                // UDP target 이 추가되면 이 compatibility API 는 TCP 구독자만 복사하며 신규 fan-out 은 target snapshot 을 사용한다.
                foreach (KeyValuePair<BrokerSubscriber, byte> pair in _subscribers)
                {
                    if (pair.Key.TransportKind == EndpointTransportKind.Tcp)
                    {
                        if (copied < destination.Length)
                        {
                            destination[copied] = pair.Key.TcpConnection;
                            copied++;
                        }

                        total++;
                    }
                }

                return total;
            }
        }
    }
}
