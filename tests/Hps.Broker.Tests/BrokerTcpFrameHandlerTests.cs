using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hps.Buffers;
using Hps.Protocol;
using Hps.Transport;
using Xunit;

namespace Hps.Broker.Tests
{
    public sealed class BrokerTcpFrameHandlerTests
    {
        // Broker TCP handler 는 Protocol frame callback 을 Broker 라우팅/fan-out 으로 연결하는 첫 end-to-end 결선이다.
        // 타입과 생성자 계약을 먼저 고정해 이후 동작 테스트가 실제 public 진입점으로 검증되도록 한다.
        [Fact]
        public void BrokerTcpFrameHandler_Contract_ExistsAndImplementsFrameHandler()
        {
            Type? handlerType = Type.GetType("Hps.Broker.BrokerTcpFrameHandler, Hps.Broker");

            Assert.NotNull(handlerType);
            Assert.True(typeof(ITcpFrameHandler).IsAssignableFrom(handlerType));
            Assert.NotNull(handlerType!.GetConstructor(new Type[] { typeof(SubscriptionTable), typeof(BrokerPublisher) }));
        }

        // SUBSCRIBE frame 처리 테스트: frame handler 는 command topic 을 routing table key 로 복사해 등록하고,
        // 수락한 frame guard ref 를 처리 후 Release 해야 TCP recv 조립 버퍼가 누수되지 않는다.
        [Fact]
        public void OnFrame_WhenSubscribeCommandArrives_AddsConnectionToTopicAndReleasesFrame()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            RefCountedBuffer frame = RentFrame(pool, "SUBSCRIBE alpha");
            SubscriptionTable subscriptions = new SubscriptionTable();
            FakeTransport transport = new FakeTransport();
            BrokerTcpFrameHandler handler = CreateHandler(subscriptions, transport);
            FakeConnection connection = new FakeConnection();

            handler.OnFrame(connection, frame);

            Assert.True(subscriptions.IsSubscribed("alpha", connection));
            Assert.Equal(0, pool.RentedCount);
        }

        // PUBLISH frame 처리 테스트: handler 는 command prefix 전체가 담긴 RefCountedBuffer 를 새로 복사하지 않고
        // decoder 가 계산한 payload offset/length 만 BrokerPublisher 에 넘겨 구독자에게 실제 payload slice 만 송신해야 한다.
        [Fact]
        public void OnFrame_WhenPublishCommandArrives_FanoutsPayloadRangeAndReleasesFrameGuard()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            RefCountedBuffer frame = RentFrame(pool, "PUBLISH alpha PAYLOAD");
            SubscriptionTable subscriptions = new SubscriptionTable();
            FakeConnection subscriber = new FakeConnection();
            subscriptions.Subscribe("alpha", subscriber);

            FakeTransport transport = new FakeTransport();
            BrokerTcpFrameHandler handler = CreateHandler(subscriptions, transport);
            FakeConnection publisherConnection = new FakeConnection();

            handler.OnFrame(publisherConnection, frame);

            Assert.Single(transport.AcceptedSends);
            Assert.Same(subscriber, transport.AcceptedSends[0].Connection);
            Assert.Same(frame, transport.AcceptedSends[0].Buffer.Buffer);
            Assert.Equal(14, transport.AcceptedSends[0].Buffer.Offset);
            Assert.Equal(7, transport.AcceptedSends[0].Buffer.Length);

            transport.ReleaseAcceptedBuffers();
            Assert.Equal(0, pool.RentedCount);
        }

        // 연결 종료 처리 테스트: Protocol adapter 는 topic 이름 없이 connection close 만 알려준다.
        // handler 가 UnsubscribeAll 을 호출해야 끊긴 connection 이 여러 topic 의 구독 set 에 dead reference 로 남지 않는다.
        [Fact]
        public void OnConnectionClosed_WhenConnectionHadSubscriptions_RemovesConnectionFromAllTopics()
        {
            SubscriptionTable subscriptions = new SubscriptionTable();
            FakeConnection closed = new FakeConnection();
            FakeConnection survivor = new FakeConnection();
            subscriptions.Subscribe("alpha", closed);
            subscriptions.Subscribe("beta", closed);
            subscriptions.Subscribe("alpha", survivor);

            BrokerTcpFrameHandler handler = CreateHandler(subscriptions, new FakeTransport());

            handler.OnConnectionClosed(closed);

            Assert.False(subscriptions.IsSubscribed("alpha", closed));
            Assert.False(subscriptions.IsSubscribed("beta", closed));
            Assert.True(subscriptions.IsSubscribed("alpha", survivor));
            Assert.Equal(1, subscriptions.CountSubscribers("alpha"));
            Assert.Equal(0, subscriptions.CountSubscribers("beta"));
        }

        // malformed frame 처리 테스트: command decode 실패는 정상 흐름 오류이므로 handler 밖으로 예외를 던지지 않는다.
        // 대신 frame 을 Release 하고 connection 을 닫아 이후 같은 TCP stream 에서 모호한 protocol 상태가 이어지지 않게 한다.
        [Fact]
        public void OnFrame_WhenCommandIsMalformed_ReleasesFrameAndClosesConnection()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            RefCountedBuffer frame = RentFrame(pool, "UNKNOWN alpha");
            BrokerTcpFrameHandler handler = CreateHandler(new SubscriptionTable(), new FakeTransport());
            FakeConnection connection = new FakeConnection();

            handler.OnFrame(connection, frame);

            Assert.Equal(1, connection.CloseCallCount);
            Assert.Equal(0, pool.RentedCount);
        }

        // malformed command 후 transport close notify 가 다시 오지 않아도 Broker routing table 이 오염되면 안 된다.
        // SAEA receive loop 의 dispose 종료처럼 close 통지가 생략되는 경로를 대비해 protocol-error close 경로가 직접 cleanup 해야 한다.
        [Fact]
        public void OnFrame_WhenSubscribedConnectionSendsMalformedCommand_RemovesConnectionFromAllTopics()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(64);
            RefCountedBuffer frame = RentFrame(pool, "UNKNOWN alpha");
            SubscriptionTable subscriptions = new SubscriptionTable();
            FakeConnection connection = new FakeConnection();
            FakeConnection survivor = new FakeConnection();
            subscriptions.Subscribe("alpha", connection);
            subscriptions.Subscribe("beta", connection);
            subscriptions.Subscribe("alpha", survivor);

            BrokerTcpFrameHandler handler = CreateHandler(subscriptions, new FakeTransport());

            handler.OnFrame(connection, frame);

            Assert.Equal(1, connection.CloseCallCount);
            Assert.False(subscriptions.IsSubscribed("alpha", connection));
            Assert.False(subscriptions.IsSubscribed("beta", connection));
            Assert.True(subscriptions.IsSubscribed("alpha", survivor));
            Assert.Equal(1, subscriptions.CountSubscribers("alpha"));
            Assert.Equal(0, subscriptions.CountSubscribers("beta"));
            Assert.Equal(0, pool.RentedCount);
        }

        private static BrokerTcpFrameHandler CreateHandler(SubscriptionTable subscriptions, FakeTransport transport)
        {
            BrokerPublisher publisher = new BrokerPublisher(subscriptions, transport);
            return new BrokerTcpFrameHandler(subscriptions, publisher);
        }

        private static RefCountedBuffer RentFrame(PinnedBlockMemoryPool pool, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            RefCountedBuffer frame = pool.RentCounted();
            bytes.CopyTo(frame.Span);
            frame.SetLength(bytes.Length);
            return frame;
        }

        private sealed class CapturedSend
        {
            internal CapturedSend(IConnection connection, TransportSendBuffer buffer)
            {
                Connection = connection;
                Buffer = buffer;
            }

            internal IConnection Connection { get; }

            internal TransportSendBuffer Buffer { get; }
        }

        private sealed class FakeConnection : IConnection
        {
            internal int CloseCallCount { get; private set; }

            public void Close()
            {
                CloseCallCount++;
            }

            public void Dispose()
            {
            }
        }

        private sealed class FakeTransport : ITransport
        {
            private readonly List<CapturedSend> _acceptedSends;

            internal FakeTransport()
            {
                _acceptedSends = new List<CapturedSend>();
            }

            internal List<CapturedSend> AcceptedSends => _acceptedSends;

            public void SetReceiveHandler(ITransportReceiveHandler receiveHandler)
            {
            }

            public void SetDatagramHandler(ITransportDatagramHandler datagramHandler)
            {
            }

            public ValueTask<IConnectionListener> ListenTcpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public ValueTask<IConnection> ConnectTcpAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public ValueTask<IUdpEndpoint> BindUdpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public bool TrySend(IConnection connection, TransportSendBuffer sendBuffer)
            {
                _acceptedSends.Add(new CapturedSend(connection, sendBuffer));
                return true;
            }

            public bool TrySendTo(IUdpEndpoint endpoint, EndPoint remoteEndPoint, TransportSendBuffer sendBuffer)
            {
                throw new NotImplementedException();
            }

            public ValueTask StartAsync(CancellationToken cancellationToken = default)
            {
                return default(ValueTask);
            }

            public ValueTask StopAsync(CancellationToken cancellationToken = default)
            {
                return default(ValueTask);
            }

            public void Dispose()
            {
            }

            internal void ReleaseAcceptedBuffers()
            {
                for (int index = 0; index < _acceptedSends.Count; index++)
                {
                    _acceptedSends[index].Buffer.Buffer.Release();
                }

                _acceptedSends.Clear();
            }
        }
    }
}
