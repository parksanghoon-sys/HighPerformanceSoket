using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Hps.Transport;

namespace Hps.Broker
{
    /// <summary>
    /// Broker 의 topic 별 TCP 구독 connection 집합을 관리하는 라우팅 테이블이다.
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
        /// 지정 topic 에 connection 을 구독자로 추가한다.
        /// </summary>
        public bool Subscribe(string topic, IConnection connection)
        {
            ValidateTopic(topic);
            ValidateConnection(connection);

            TopicSubscriptions subscriptions = _topics.GetOrAdd(topic, CreateTopicSubscriptions);
            return subscriptions.Add(connection);
        }

        /// <summary>
        /// 지정 topic 에서 connection 구독을 제거한다.
        /// </summary>
        public bool Unsubscribe(string topic, IConnection connection)
        {
            ValidateTopic(topic);
            ValidateConnection(connection);

            TopicSubscriptions? subscriptions;
            if (!_topics.TryGetValue(topic, out subscriptions))
                return false;

            // D008: 구독자 set 이 비어도 topic entry 를 즉시 제거하지 않는다.
            // "새 구독 추가"와 "마지막 구독 해지 후 빈 정리"가 겹치면 새 구독이 제거된 set 에 들어가 유실될 수 있다.
            return subscriptions.Remove(connection);
        }

        /// <summary>
        /// 지정 connection 이 topic 에 현재 구독되어 있는지 확인한다.
        /// </summary>
        public bool IsSubscribed(string topic, IConnection connection)
        {
            ValidateTopic(topic);
            ValidateConnection(connection);

            TopicSubscriptions? subscriptions;
            return _topics.TryGetValue(topic, out subscriptions) && subscriptions.Contains(connection);
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
        /// 지정 topic 의 현재 구독자를 caller 제공 배열로 복사한다.
        /// </summary>
        public int CopySubscribers(string topic, IConnection[] destination)
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

        private sealed class TopicSubscriptions
        {
            private readonly ConcurrentDictionary<IConnection, byte> _connections;

            internal TopicSubscriptions()
            {
                _connections = new ConcurrentDictionary<IConnection, byte>(ConnectionReferenceComparer.Instance);
            }

            internal int Count => _connections.Count;

            internal bool Add(IConnection connection)
            {
                return _connections.TryAdd(connection, 0);
            }

            internal bool Remove(IConnection connection)
            {
                byte ignored;
                return _connections.TryRemove(connection, out ignored);
            }

            internal bool Contains(IConnection connection)
            {
                return _connections.ContainsKey(connection);
            }

            internal int CopyTo(IConnection[] destination)
            {
                int total = 0;
                int copied = 0;

                // ConcurrentDictionary 열거자는 mutation 과 동시에 안전한 snapshot 성격의 열거를 제공한다.
                // fan-out caller 는 반환된 total 이 destination 길이보다 크면 더 큰 버퍼로 재시도할 수 있다.
                foreach (KeyValuePair<IConnection, byte> pair in _connections)
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
        }

        private sealed class ConnectionReferenceComparer : IEqualityComparer<IConnection>
        {
            internal static readonly ConnectionReferenceComparer Instance = new ConnectionReferenceComparer();

            private ConnectionReferenceComparer()
            {
            }

            public bool Equals(IConnection? x, IConnection? y)
            {
                return object.ReferenceEquals(x, y);
            }

            public int GetHashCode(IConnection obj)
            {
                if (obj == null)
                    throw new ArgumentNullException(nameof(obj));

                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
