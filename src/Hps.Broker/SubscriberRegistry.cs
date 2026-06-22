using System;
using System.Collections.Generic;
using Hps.Transport;

namespace Hps.Broker
{
    /// <summary>
    /// stable subscriber identity 와 현재 runtime fan-out target 을 연결한다.
    ///
    /// 이 타입은 payload 를 보관하지 않는다. identity 별 topic metadata 만 보존하고,
    /// 실제 fan-out 대상은 항상 <see cref="SubscriptionTable"/> 에 들어 있는 online <see cref="BrokerSubscriber"/> 이다.
    /// </summary>
    internal sealed class SubscriberRegistry
    {
        private readonly object _gate;
        private readonly SubscriptionTable _subscriptions;
        private readonly Dictionary<SubscriberIdentity, Entry> _entries;
        private readonly Dictionary<BrokerSubscriber, SubscriberIdentity> _targetToIdentity;

        internal SubscriberRegistry(SubscriptionTable subscriptions)
        {
            if (subscriptions == null)
                throw new ArgumentNullException(nameof(subscriptions));

            _gate = new object();
            _subscriptions = subscriptions;
            _entries = new Dictionary<SubscriberIdentity, Entry>();
            _targetToIdentity = new Dictionary<BrokerSubscriber, SubscriberIdentity>();
        }

        /// <summary>
        /// 현재 보존 중인 stable identity 수이다.
        /// </summary>
        internal int IdentityCount
        {
            get
            {
                lock (_gate)
                {
                    return _entries.Count;
                }
            }
        }

        /// <summary>
        /// stable identity 를 현재 runtime target 에 연결한다.
        ///
        /// 같은 id 의 새 target 이 들어오면 기존 target 은 routing table 에서 제거하고,
        /// 보존된 topic set 을 새 target 으로 다시 붙인다. 같은 runtime target 이 다른 id 로 등록되는 경우는
        /// cleanup 기준이 모호해지므로 reject 결과만 반환한다.
        /// </summary>
        internal SubscriberRegistrationResult Register(
            SubscriberIdentity identity,
            BrokerSubscriber target,
            out BrokerSubscriber? replacedTarget,
            out string[] reboundTopics)
        {
            ValidateTarget(target);

            replacedTarget = null;
            reboundTopics = Array.Empty<string>();

            lock (_gate)
            {
                SubscriberIdentity existingIdentity;
                bool targetAlreadyMapped = _targetToIdentity.TryGetValue(target, out existingIdentity);
                if (targetAlreadyMapped && !existingIdentity.Equals(identity))
                    return SubscriberRegistrationResult.TargetAlreadyRegisteredWithDifferentIdentity;

                Entry? entry;
                if (!_entries.TryGetValue(identity, out entry))
                {
                    entry = new Entry();
                    _entries.Add(identity, entry);
                }

                if (entry.CurrentTarget.HasValue && entry.CurrentTarget.Value.Equals(target))
                {
                    reboundTopics = entry.CopyTopics();
                    return SubscriberRegistrationResult.AlreadyRegistered;
                }

                if (entry.CurrentTarget.HasValue)
                {
                    BrokerSubscriber oldTarget = entry.CurrentTarget.Value;
                    replacedTarget = oldTarget;
                    RemoveCurrentTargetFromTopics(entry, oldTarget);
                    _targetToIdentity.Remove(oldTarget);
                }

                if (!targetAlreadyMapped)
                {
                    // REGISTER 는 runtime-target subscription 에서 stable-identity subscription 으로 넘어가는 경계다.
                    // REGISTER 전에 만든 구독은 identity topic metadata 에 없으므로 여기서 제거하지 않으면 close cleanup 이 놓친다.
                    RemoveRuntimeTarget(target);
                }

                entry.CurrentTarget = target;
                entry.LastDisconnectedAt = null;
                _targetToIdentity[target] = identity;
                AddCurrentTargetToTopics(entry, target);
                reboundTopics = entry.CopyTopics();

                if (replacedTarget.HasValue)
                    return SubscriberRegistrationResult.Rebound;

                return SubscriberRegistrationResult.Registered;
            }
        }

        /// <summary>
        /// topic 을 target 의 stable identity metadata 에 추가하고 현재 online target 을 routing table 에 등록한다.
        /// 등록되지 않은 target 은 기존 runtime target subscription 과 동일하게 바로 routing table 로 위임한다.
        /// </summary>
        internal bool Subscribe(string topic, BrokerSubscriber target)
        {
            ValidateTopic(topic);
            ValidateTarget(target);

            lock (_gate)
            {
                SubscriberIdentity identity;
                if (!_targetToIdentity.TryGetValue(target, out identity))
                    return _subscriptions.Subscribe(topic, target);

                Entry entry = _entries[identity];
                entry.Topics.Add(topic);

                if (entry.CurrentTarget.HasValue)
                    return _subscriptions.Subscribe(topic, entry.CurrentTarget.Value);

                return false;
            }
        }

        /// <summary>
        /// topic 을 stable identity metadata 와 현재 routing table 에서 제거한다.
        /// metadata 에서 먼저 제거해야 이후 reconnect 때 사용자가 해제한 topic 이 되살아나지 않는다.
        /// </summary>
        internal bool Unsubscribe(string topic, BrokerSubscriber target)
        {
            ValidateTopic(topic);
            ValidateTarget(target);

            lock (_gate)
            {
                SubscriberIdentity identity;
                if (!_targetToIdentity.TryGetValue(target, out identity))
                    return _subscriptions.Unsubscribe(topic, target);

                Entry entry = _entries[identity];
                entry.Topics.Remove(topic);

                if (entry.CurrentTarget.HasValue)
                    return _subscriptions.Unsubscribe(topic, entry.CurrentTarget.Value);

                return false;
            }
        }

        /// <summary>
        /// runtime target 종료를 registry 에 반영한다.
        ///
        /// routing table 에서는 제거하지만 topic metadata 는 retention sweep 이 지울 때까지 남긴다.
        /// </summary>
        internal int RemoveTarget(BrokerSubscriber target, DateTimeOffset now)
        {
            ValidateTarget(target);

            lock (_gate)
            {
                return RemoveTargetCore(target, now);
            }
        }

        /// <summary>
        /// UDP local endpoint 에 묶인 현재 stable target 들을 모두 disconnected 상태로 전환한다.
        /// </summary>
        internal int RemoveUdpEndpoint(IUdpEndpoint endpoint, DateTimeOffset now)
        {
            if (endpoint == null)
                throw new ArgumentNullException(nameof(endpoint));

            lock (_gate)
            {
                int removed = 0;
                List<BrokerSubscriber> targets = new List<BrokerSubscriber>();

                foreach (KeyValuePair<BrokerSubscriber, SubscriberIdentity> pair in _targetToIdentity)
                {
                    BrokerSubscriber target = pair.Key;
                    if (target.TransportKind == EndpointTransportKind.Udp
                        && object.ReferenceEquals(target.UdpEndpoint, endpoint))
                    {
                        targets.Add(target);
                    }
                }

                for (int index = 0; index < targets.Count; index++)
                    removed += RemoveTargetCore(targets[index], now);

                return removed;
            }
        }

        /// <summary>
        /// explicit UNREGISTER 를 처리해 identity metadata 와 현재 routing target 을 함께 제거한다.
        /// </summary>
        internal int Unregister(SubscriberIdentity identity, BrokerSubscriber target)
        {
            ValidateTarget(target);

            lock (_gate)
            {
                Entry? entry;
                if (!_entries.TryGetValue(identity, out entry))
                    return 0;
                if (!entry.CurrentTarget.HasValue || !entry.CurrentTarget.Value.Equals(target))
                    return 0;

                int removed = RemoveCurrentTargetFromTopics(entry, target);
                _targetToIdentity.Remove(target);
                _entries.Remove(identity);
                return removed;
            }
        }

        /// <summary>
        /// retention timeout 을 넘긴 disconnected identity metadata 를 제거한다.
        /// </summary>
        internal int SweepDisconnected(DateTimeOffset now, TimeSpan retentionTimeout)
        {
            if (retentionTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(retentionTimeout));

            lock (_gate)
            {
                List<SubscriberIdentity> expired = new List<SubscriberIdentity>();
                foreach (KeyValuePair<SubscriberIdentity, Entry> pair in _entries)
                {
                    Entry entry = pair.Value;
                    if (!entry.CurrentTarget.HasValue
                        && entry.LastDisconnectedAt.HasValue
                        && now - entry.LastDisconnectedAt.Value > retentionTimeout)
                    {
                        expired.Add(pair.Key);
                    }
                }

                for (int index = 0; index < expired.Count; index++)
                    _entries.Remove(expired[index]);

                return expired.Count;
            }
        }

        private int RemoveTargetCore(BrokerSubscriber target, DateTimeOffset now)
        {
            SubscriberIdentity identity;
            if (!_targetToIdentity.TryGetValue(target, out identity))
                return RemoveRuntimeTarget(target);

            Entry entry = _entries[identity];
            int removed = RemoveCurrentTargetFromTopics(entry, target);
            entry.CurrentTarget = null;
            entry.LastDisconnectedAt = now;
            _targetToIdentity.Remove(target);
            return removed;
        }

        private int RemoveCurrentTargetFromTopics(Entry entry, BrokerSubscriber target)
        {
            int removed = 0;

            foreach (string topic in entry.Topics)
            {
                if (_subscriptions.Unsubscribe(topic, target))
                    removed++;
            }

            return removed;
        }

        private void AddCurrentTargetToTopics(Entry entry, BrokerSubscriber target)
        {
            foreach (string topic in entry.Topics)
                _subscriptions.Subscribe(topic, target);
        }

        private int RemoveRuntimeTarget(BrokerSubscriber target)
        {
            if (target.TransportKind == EndpointTransportKind.Tcp)
                return _subscriptions.UnsubscribeAll(target.TcpConnection);

            if (target.TransportKind == EndpointTransportKind.Udp)
                return _subscriptions.UnsubscribeAll(target.UdpEndpoint, target.UdpRemoteEndPoint);

            return 0;
        }

        private static void ValidateTopic(string topic)
        {
            if (topic == null)
                throw new ArgumentNullException(nameof(topic));
            if (topic.Length == 0)
                throw new ArgumentException("Topic 은 비어 있을 수 없다.", nameof(topic));
        }

        private static void ValidateTarget(BrokerSubscriber target)
        {
            if (!target.IsValid)
                throw new ArgumentException("Stable subscriber target 은 유효한 BrokerSubscriber 여야 한다.", nameof(target));
        }

        private sealed class Entry
        {
            internal readonly HashSet<string> Topics = new HashSet<string>(StringComparer.Ordinal);
            internal BrokerSubscriber? CurrentTarget;
            internal DateTimeOffset? LastDisconnectedAt;

            internal string[] CopyTopics()
            {
                string[] topics = new string[Topics.Count];
                Topics.CopyTo(topics);
                return topics;
            }
        }
    }
}
