using System;
using System.Text;
using Hps.Buffers;
using Hps.Protocol;
using Hps.Transport;

namespace Hps.Broker
{
    /// <summary>
    /// TCP frame payload command 를 Broker 의 구독 테이블과 publish fan-out 으로 연결하는 handler 이다.
    /// </summary>
    public sealed class BrokerTcpFrameHandler : ITcpFrameHandler
    {
        private readonly SubscriptionTable _subscriptions;
        private readonly BrokerPublisher _publisher;

        /// <summary>
        /// command 처리를 위한 routing table 과 publisher 를 지정한다.
        /// </summary>
        public BrokerTcpFrameHandler(SubscriptionTable subscriptions, BrokerPublisher publisher)
        {
            if (subscriptions == null)
                throw new ArgumentNullException(nameof(subscriptions));
            if (publisher == null)
                throw new ArgumentNullException(nameof(publisher));

            _subscriptions = subscriptions;
            _publisher = publisher;
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
            try
            {
                TcpCommand command;
                TcpCommandDecodeError error;
                if (!TcpCommandDecoder.TryDecode(frame.Memory.Span.Slice(0, frame.Length), out command, out error))
                {
                    // 현재 Phase 에는 protocol error 응답 프레임이 없다. malformed command 는 같은 TCP stream 의
                    // 이후 해석 상태를 신뢰하기 어렵기 때문에 frame 을 회수한 뒤 connection 을 닫는 최소 정책을 쓴다.
                    closeConnection = true;
                    return;
                }

                if (command.Kind == TcpCommandKind.Subscribe)
                {
                    string topic = DecodeTopic(command.Topic);
                    _subscriptions.Subscribe(topic, connection);
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
                // ITcpFrameHandler 계약상 정상 반환하면 handler 가 frame 소유권을 받은 것이다.
                // 내부 처리 중 예외가 난 뒤 그대로 throw 하면 Protocol adapter 가 같은 frame 을 다시 Release 할 수 있으므로
                // 여기서 frame 을 정리하고 connection 을 닫는 정책으로 수렴한다.
                closeConnection = true;
            }
            finally
            {
                frame.Release();

                if (closeConnection)
                    connection.Close();
            }
        }

        /// <summary>
        /// 닫힌 connection 의 Broker 구독을 정리한다.
        /// </summary>
        public void OnConnectionClosed(IConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            _subscriptions.UnsubscribeAll(connection);
        }

        private static string DecodeTopic(ReadOnlySpan<byte> topic)
        {
            // topic 은 routing table key 로 connection 수명 이후에도 남을 수 있으므로 string 으로 명시 복사한다.
            // payload 는 RefCountedBuffer slice 로 유지하지만, topic key 는 dictionary lookup 을 위해 안정적인 관리 객체가 필요하다.
            return Encoding.ASCII.GetString(topic);
        }
    }
}
