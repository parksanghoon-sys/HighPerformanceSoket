using System;
using System.Buffers;
using Hps.Buffers;
using Hps.Transport;

namespace Hps.Broker
{
    /// <summary>
    /// Broker publish 요청을 구독자별 Transport send 요청으로 fan-out 하는 경계다.
    /// </summary>
    public sealed class BrokerPublisher
    {
        private readonly SubscriptionTable _subscriptions;
        private readonly ITransport _transport;

        /// <summary>
        /// 구독자 라우팅 테이블과 Transport 송신 계약을 사용하는 publisher 를 만든다.
        /// </summary>
        public BrokerPublisher(SubscriptionTable subscriptions, ITransport transport)
        {
            if (subscriptions == null)
                throw new ArgumentNullException(nameof(subscriptions));
            if (transport == null)
                throw new ArgumentNullException(nameof(transport));

            _subscriptions = subscriptions;
            _transport = transport;
        }

        /// <summary>
        /// 지정 topic 의 현재 구독자에게 payload 전송을 시도하고, Transport 가 수락한 전송 수를 반환한다.
        ///
        /// 이 메서드는 publish guard ref 를 해제하지 않는다. caller 는 TCP/UDP 입력 경계에서 받은 원래
        /// <paramref name="payload"/> 소유권을 유지하고, Publish 반환 뒤 자신의 ref 를 Release 해야 한다.
        /// 각 구독자 전송은 D009 계약에 따라 AddRef 후 Transport 로 넘기며, TrySend 실패 시 즉시 Release 한다.
        /// </summary>
        public int Publish(string topic, RefCountedBuffer payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            return Publish(topic, payload, 0, payload.Length);
        }

        /// <summary>
        /// 지정 topic 의 현재 구독자에게 payload buffer 안의 일부 범위만 전송한다.
        ///
        /// TCP command frame 처럼 명령, 토픽, 실제 payload 가 같은 <paramref name="payload"/> 안에 같이 있을 때
        /// 추가 복사 없이 실제 payload slice 만 fan-out 하기 위한 진입점이다.
        /// </summary>
        public int Publish(string topic, RefCountedBuffer payload, int offset, int length)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            ValidatePayloadRange(payload, offset, length);

            IConnection[] subscribers = RentSubscriberSnapshotBuffer(topic);
            try
            {
                int subscriberCount = CopySubscribersIntoSnapshot(topic, ref subscribers);
                int acceptedCount = 0;

                for (int index = 0; index < subscriberCount; index++)
                {
                    if (TrySendToSubscriber(subscribers[index], payload, offset, length))
                        acceptedCount++;
                }

                return acceptedCount;
            }
            finally
            {
                ArrayPool<IConnection>.Shared.Return(subscribers, clearArray: true);
            }
        }

        // 현재 구독자 수를 기준으로 스냅샷 배열을 빌린다. Count 이후 구독자가 늘 수 있으므로 이 크기는
        // 최종 확정값이 아니라 첫 시도용 힌트이며, 실제 부족 여부는 CopySubscribers 반환값으로 다시 판단한다.
        private IConnection[] RentSubscriberSnapshotBuffer(string topic)
        {
            int subscriberCount = _subscriptions.CountSubscribers(topic);
            if (subscriberCount <= 0)
                subscriberCount = 1;

            return ArrayPool<IConnection>.Shared.Rent(subscriberCount);
        }

        // SubscriptionTable.CopySubscribers 는 destination 에 담긴 수가 아니라 전체 구독자 수를 반환한다.
        // 반환값이 배열 길이보다 크면 mutation 중 구독자가 늘었거나 최초 힌트가 작았다는 뜻이므로 더 큰 배열로 재시도한다.
        private int CopySubscribersIntoSnapshot(string topic, ref IConnection[] subscribers)
        {
            while (true)
            {
                int subscriberCount = _subscriptions.CopySubscribers(topic, subscribers);
                if (subscriberCount <= subscribers.Length)
                    return subscriberCount;

                ArrayPool<IConnection>.Shared.Return(subscribers, clearArray: true);
                subscribers = ArrayPool<IConnection>.Shared.Rent(subscriberCount);
            }
        }

        private static void ValidatePayloadRange(RefCountedBuffer payload, int offset, int length)
        {
            int payloadLength = payload.Length;
            if (offset < 0 || offset > payloadLength)
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset 은 payload 길이 안에 있어야 한다.");
            if (length < 0 || length > payloadLength - offset)
                throw new ArgumentOutOfRangeException(nameof(length), "Length 는 Offset 이후 payload 범위 안에 있어야 한다.");
        }

        // 구독자별 ref 는 Transport 가 TrySend true 를 반환했을 때만 Transport 소유가 된다.
        // false 또는 생성/전송 중 예외 경로에서는 Broker 가 방금 추가한 ref 를 되돌려 publish guard ref 만 남긴다.
        private bool TrySendToSubscriber(IConnection subscriber, RefCountedBuffer payload, int offset, int length)
        {
            payload.AddRef();
            bool accepted = false;
            try
            {
                TransportSendBuffer sendBuffer = new TransportSendBuffer(payload, offset, length);
                accepted = _transport.TrySend(subscriber, sendBuffer);
                return accepted;
            }
            finally
            {
                if (!accepted)
                    payload.Release();
            }
        }
    }
}
