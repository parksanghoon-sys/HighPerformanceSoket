using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using Hps.Transport;

namespace Hps.Broker
{
    /// <summary>
    /// UDP remote subscription lease 를 Broker 계층에서 추적한다.
    ///
    /// 이 타입은 routing table 변경과 lease 변경을 같은 lock 으로 직렬화한다. 이후 sweep 이 들어오면 같은 gate 를 사용해
    /// SUBSCRIBE/UNSUBSCRIBE 와 remote cleanup 이 서로 엇갈려 새 구독을 제거하는 경합을 막는다.
    /// </summary>
    internal sealed class UdpRemoteLeaseTracker
    {
        private readonly object _gate;
        private readonly SubscriptionTable _subscriptions;
        private readonly UdpLeaseOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly Dictionary<UdpRemoteLeaseKey, UdpRemoteLease> _leases;

        internal UdpRemoteLeaseTracker(SubscriptionTable subscriptions, UdpLeaseOptions options, TimeProvider timeProvider)
        {
            if (subscriptions == null)
                throw new ArgumentNullException(nameof(subscriptions));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (timeProvider == null)
                throw new ArgumentNullException(nameof(timeProvider));

            _gate = new object();
            _subscriptions = subscriptions;
            _options = options;
            _timeProvider = timeProvider;
            _leases = new Dictionary<UdpRemoteLeaseKey, UdpRemoteLease>();
        }

        internal int LeaseCount
        {
            get
            {
                lock (_gate)
                {
                    return _leases.Count;
                }
            }
        }

        internal bool Subscribe(string topic, IUdpEndpoint endpoint, EndPoint remoteEndPoint)
        {
            ValidateTopic(topic);
            ValidateEndpoint(endpoint);
            ValidateRemoteEndPoint(remoteEndPoint);

            lock (_gate)
            {
                bool added = _subscriptions.Subscribe(topic, BrokerSubscriber.ForUdp(endpoint, remoteEndPoint));
                if (_options.Enabled)
                    GetOrCreateLease(endpoint, remoteEndPoint).MarkSubscribed(topic, _timeProvider.GetUtcNow());

                return added;
            }
        }

        internal bool Unsubscribe(string topic, IUdpEndpoint endpoint, EndPoint remoteEndPoint)
        {
            ValidateTopic(topic);
            ValidateEndpoint(endpoint);
            ValidateRemoteEndPoint(remoteEndPoint);

            lock (_gate)
            {
                bool removed = _subscriptions.Unsubscribe(topic, BrokerSubscriber.ForUdp(endpoint, remoteEndPoint));
                if (_options.Enabled && removed)
                    MarkUnsubscribed(topic, endpoint, remoteEndPoint);

                return removed;
            }
        }

        internal void MarkPublishActivity(IUdpEndpoint endpoint, EndPoint remoteEndPoint)
        {
            ValidateEndpoint(endpoint);
            ValidateRemoteEndPoint(remoteEndPoint);

            if (!_options.Enabled)
                return;

            lock (_gate)
            {
                UdpRemoteLease? lease;
                if (_leases.TryGetValue(new UdpRemoteLeaseKey(endpoint, remoteEndPoint), out lease))
                    lease.Touch(_timeProvider.GetUtcNow());
            }
        }

        internal void MarkSubscribedTopics(IUdpEndpoint endpoint, EndPoint remoteEndPoint, string[] topics)
        {
            ValidateEndpoint(endpoint);
            ValidateRemoteEndPoint(remoteEndPoint);
            if (topics == null)
                throw new ArgumentNullException(nameof(topics));
            if (!_options.Enabled)
                return;

            lock (_gate)
            {
                UdpRemoteLease lease = GetOrCreateLease(endpoint, remoteEndPoint);
                DateTimeOffset now = _timeProvider.GetUtcNow();
                for (int index = 0; index < topics.Length; index++)
                    lease.MarkSubscribed(topics[index], now);
            }
        }

        internal int RemoveRemote(IUdpEndpoint endpoint, EndPoint remoteEndPoint)
        {
            ValidateEndpoint(endpoint);
            ValidateRemoteEndPoint(remoteEndPoint);

            lock (_gate)
            {
                _leases.Remove(new UdpRemoteLeaseKey(endpoint, remoteEndPoint));
                return _subscriptions.UnsubscribeAll(endpoint, remoteEndPoint);
            }
        }

        internal int RemoveEndpoint(IUdpEndpoint endpoint)
        {
            ValidateEndpoint(endpoint);

            lock (_gate)
            {
                if (_options.Enabled)
                    RemoveLeasesForEndpoint(endpoint);

                return _subscriptions.UnsubscribeAll(endpoint);
            }
        }

        internal int SweepExpired(DateTimeOffset now)
        {
            if (!_options.Enabled)
                return 0;

            lock (_gate)
            {
                int removed = 0;
                List<UdpRemoteLeaseKey> expiredKeys = new List<UdpRemoteLeaseKey>();

                // sweep 은 SUBSCRIBE/UNSUBSCRIBE 와 같은 gate 안에서 실행한다. 이렇게 해야 key 를 골라낸 뒤
                // routing table cleanup 사이에 같은 remote 의 새 구독이 끼어들어 함께 제거되는 경합을 막을 수 있다.
                foreach (KeyValuePair<UdpRemoteLeaseKey, UdpRemoteLease> pair in _leases)
                {
                    if (now - pair.Value.LastSeen > _options.IdleTimeout)
                        expiredKeys.Add(pair.Key);
                }

                for (int index = 0; index < expiredKeys.Count; index++)
                {
                    UdpRemoteLeaseKey key = expiredKeys[index];
                    removed += _subscriptions.UnsubscribeAll(key.Endpoint, key.RemoteEndPoint);
                    _leases.Remove(key);
                }

                return removed;
            }
        }

        private UdpRemoteLease GetOrCreateLease(IUdpEndpoint endpoint, EndPoint remoteEndPoint)
        {
            UdpRemoteLeaseKey key = new UdpRemoteLeaseKey(endpoint, remoteEndPoint);
            UdpRemoteLease? lease;
            if (!_leases.TryGetValue(key, out lease))
            {
                lease = new UdpRemoteLease();
                _leases.Add(key, lease);
            }

            return lease;
        }

        private void MarkUnsubscribed(string topic, IUdpEndpoint endpoint, EndPoint remoteEndPoint)
        {
            UdpRemoteLeaseKey key = new UdpRemoteLeaseKey(endpoint, remoteEndPoint);
            UdpRemoteLease? lease;
            if (!_leases.TryGetValue(key, out lease))
                return;

            lease.MarkUnsubscribed(topic, _timeProvider.GetUtcNow());
            if (lease.TopicCount == 0)
                _leases.Remove(key);
        }

        private void RemoveLeasesForEndpoint(IUdpEndpoint endpoint)
        {
            List<UdpRemoteLeaseKey> removeKeys = new List<UdpRemoteLeaseKey>();

            // Dictionary 는 열거 중 삭제할 수 없으므로 먼저 key snapshot 을 만든다.
            // endpoint close 는 hot publish 경로가 아니어서 이 작은 임시 리스트 비용을 허용한다.
            foreach (KeyValuePair<UdpRemoteLeaseKey, UdpRemoteLease> pair in _leases)
            {
                if (object.ReferenceEquals(pair.Key.Endpoint, endpoint))
                    removeKeys.Add(pair.Key);
            }

            for (int index = 0; index < removeKeys.Count; index++)
            {
                _leases.Remove(removeKeys[index]);
            }
        }

        private static void ValidateTopic(string topic)
        {
            if (topic == null)
                throw new ArgumentNullException(nameof(topic));
            if (topic.Length == 0)
                throw new ArgumentException("Topic 은 비어 있을 수 없다.", nameof(topic));
        }

        private static void ValidateEndpoint(IUdpEndpoint endpoint)
        {
            if (endpoint == null)
                throw new ArgumentNullException(nameof(endpoint));
        }

        private static void ValidateRemoteEndPoint(EndPoint remoteEndPoint)
        {
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));
        }

        private readonly struct UdpRemoteLeaseKey : IEquatable<UdpRemoteLeaseKey>
        {
            internal UdpRemoteLeaseKey(IUdpEndpoint endpoint, EndPoint remoteEndPoint)
            {
                Endpoint = endpoint;
                RemoteEndPoint = remoteEndPoint;
            }

            internal IUdpEndpoint Endpoint { get; }

            internal EndPoint RemoteEndPoint { get; }

            public bool Equals(UdpRemoteLeaseKey other)
            {
                return object.ReferenceEquals(Endpoint, other.Endpoint)
                    && object.Equals(RemoteEndPoint, other.RemoteEndPoint);
            }

            public override bool Equals(object? obj)
            {
                if (obj is UdpRemoteLeaseKey)
                    return Equals((UdpRemoteLeaseKey)obj);

                return false;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (RuntimeHelpers.GetHashCode(Endpoint) * 397) ^ RemoteEndPoint.GetHashCode();
                }
            }
        }

        private sealed class UdpRemoteLease
        {
            private readonly HashSet<string> _topics;

            internal UdpRemoteLease()
            {
                _topics = new HashSet<string>(StringComparer.Ordinal);
            }

            internal DateTimeOffset LastSeen { get; private set; }

            internal int TopicCount
            {
                get { return _topics.Count; }
            }

            internal void MarkSubscribed(string topic, DateTimeOffset now)
            {
                _topics.Add(topic);
                LastSeen = now;
            }

            internal void MarkUnsubscribed(string topic, DateTimeOffset now)
            {
                _topics.Remove(topic);
                LastSeen = now;
            }

            internal void Touch(DateTimeOffset now)
            {
                LastSeen = now;
            }
        }
    }
}
