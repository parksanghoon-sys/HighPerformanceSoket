using System;
using System.Text;
using Hps.Buffers;
using Hps.Protocol;
using Hps.Transport;

namespace Hps.Broker
{
    /// <summary>
    /// TCP frame payload command 를 Broker 구독 테이블과 publish fan-out 으로 연결하는 handler 이다.
    /// </summary>
    public sealed class BrokerTcpFrameHandler : ITcpFrameHandler
    {
        private readonly SubscriptionTable _subscriptions;
        private readonly BrokerPublisher _publisher;
        private readonly SubscriberRegistry? _subscriberRegistry;
        private readonly TimeProvider _timeProvider;

        /// <summary>
        /// stable identity 없이 기존 runtime connection 기반 command 처리를 수행한다.
        /// </summary>
        public BrokerTcpFrameHandler(SubscriptionTable subscriptions, BrokerPublisher publisher)
            : this(subscriptions, publisher, null, TimeProvider.System)
        {
        }

        /// <summary>
        /// command 처리를 위한 routing table, publisher, 선택적 stable identity registry 를 지정한다.
        /// </summary>
        internal BrokerTcpFrameHandler(
            SubscriptionTable subscriptions,
            BrokerPublisher publisher,
            SubscriberRegistry? subscriberRegistry,
            TimeProvider timeProvider)
        {
            if (subscriptions == null)
                throw new ArgumentNullException(nameof(subscriptions));
            if (publisher == null)
                throw new ArgumentNullException(nameof(publisher));
            if (timeProvider == null)
                throw new ArgumentNullException(nameof(timeProvider));

            _subscriptions = subscriptions;
            _publisher = publisher;
            _subscriberRegistry = subscriberRegistry;
            _timeProvider = timeProvider;
        }

        /// <summary>
        /// 완성된 TCP frame command 를 처리한다.
        /// </summary>
        public void OnFrame(IConnection connection, RefCountedBuffer frame)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            bool closeConnection = false;
            BrokerSubscriber target = BrokerSubscriber.ForTcp(connection);
            try
            {
                TcpCommand command;
                TcpCommandDecodeError error;
                if (!TcpCommandDecoder.TryDecode(frame.Memory.Span.Slice(0, frame.Length), out command, out error))
                {
                    // malformed command 는 같은 TCP stream 의 이후 frame 경계를 신뢰할 수 없게 만든다.
                    // broker 가 직접 닫는 경로에서도 cleanup 을 보장하기 위해 finally 에서 close 처리로 수렴시킨다.
                    closeConnection = true;
                    return;
                }

                if (command.Kind == TcpCommandKind.Register)
                {
                    closeConnection = !RegisterTcpTarget(connection, target, DecodeTopic(command.Topic));
                    return;
                }

                if (command.Kind == TcpCommandKind.Unregister)
                {
                    if (_subscriberRegistry != null)
                        _subscriberRegistry.Unregister(SubscriberIdentity.Create(DecodeTopic(command.Topic)), target);
                    return;
                }

                if (command.Kind == TcpCommandKind.Subscribe)
                {
                    string topic = DecodeTopic(command.Topic);
                    if (_subscriberRegistry != null)
                        _subscriberRegistry.Subscribe(topic, target);
                    else
                        _subscriptions.Subscribe(topic, connection);
                    return;
                }

                if (command.Kind == TcpCommandKind.Unsubscribe)
                {
                    string topic = DecodeTopic(command.Topic);
                    if (_subscriberRegistry != null)
                        _subscriberRegistry.Unsubscribe(topic, target);
                    else
                        _subscriptions.Unsubscribe(topic, connection);
                    return;
                }

                if (command.Kind == TcpCommandKind.Publish)
                {
                    string topic = DecodeTopic(command.Topic);
                    _publisher.Publish(topic, frame, command.PayloadOffset, command.Payload.Length);
                    return;
                }

                closeConnection = true;
            }
            catch (Exception)
            {
                // decode 이후 identity validation 또는 routing 처리 중 예외가 나면 frame guard ref 는 이 handler 가 정리한다.
                // exception 을 바깥으로 던지면 adapter 와 handler 가 같은 frame 을 중복 Release 할 수 있으므로 close 경로로 수렴한다.
                closeConnection = true;
            }
            finally
            {
                frame.Release();

                if (closeConnection)
                {
                    CleanupConnection(connection, target);
                    connection.Close();
                }
            }
        }

        /// <summary>
        /// 닫힌 connection 의 Broker 구독 상태를 정리한다.
        /// </summary>
        public void OnConnectionClosed(IConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            CleanupConnection(connection, BrokerSubscriber.ForTcp(connection));
        }

        private bool RegisterTcpTarget(IConnection connection, BrokerSubscriber target, string identityValue)
        {
            if (_subscriberRegistry == null)
                return true;

            SubscriberRegistrationResult result = _subscriberRegistry.Register(
                SubscriberIdentity.Create(identityValue),
                target,
                out BrokerSubscriber? replacedTarget,
                out _);

            if (result == SubscriberRegistrationResult.TargetAlreadyRegisteredWithDifferentIdentity)
                return false;

            if (replacedTarget.HasValue
                && replacedTarget.Value.TransportKind == EndpointTransportKind.Tcp
                && !object.ReferenceEquals(replacedTarget.Value.TcpConnection, connection))
            {
                // 같은 logical subscriber id 가 새 TCP connection 으로 재등록되면 예전 runtime target 은 더 이상 fan-out 대상이 아니다.
                // transport close notify 가 늦게 오더라도 registry mapping 은 이미 새 target 으로 이동했으므로 여기서는 socket close 만 요청한다.
                replacedTarget.Value.TcpConnection.Close();
            }

            return true;
        }

        private void CleanupConnection(IConnection connection, BrokerSubscriber target)
        {
            if (_subscriberRegistry != null)
            {
                _subscriberRegistry.RemoveTarget(target, _timeProvider.GetUtcNow());
                return;
            }

            _subscriptions.UnsubscribeAll(connection);
        }

        private static string DecodeTopic(ReadOnlySpan<byte> topic)
        {
            // topic/identity token 은 routing table key 로 connection/frame 수명 이후에도 남는다.
            // payload 는 RefCountedBuffer slice 로 유지하지만 key token 은 명시적으로 string 으로 복사한다.
            return Encoding.ASCII.GetString(topic);
        }
    }
}
