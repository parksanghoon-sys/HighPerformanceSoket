using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hps.Transport;

namespace Hps.Broker.Tests
{
    internal sealed class CapturedSend
    {
        internal CapturedSend(IConnection connection, TransportSendBuffer buffer)
        {
            Connection = connection;
            Buffer = buffer;
        }

        internal IConnection Connection { get; }

        internal TransportSendBuffer Buffer { get; }
    }

    internal sealed class FakeTransport : ITransport
    {
        private readonly List<CapturedSend> _acceptedSends;

        internal FakeTransport()
        {
            _acceptedSends = new List<CapturedSend>();
        }

        internal IConnection? RejectConnection { get; set; }

        internal List<CapturedSend> AcceptedSends => _acceptedSends;

        public void SetReceiveHandler(ITransportReceiveHandler receiveHandler)
        {
        }

        public void SetDatagramHandler(ITransportDatagramHandler datagramHandler)
        {
        }

        public ValueTask<IConnectionListener> ListenTcpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Broker 단위 테스트용 transport 는 TCP listener 를 열지 않는다.");
        }

        public ValueTask<IConnection> ConnectTcpAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Broker 단위 테스트용 transport 는 TCP connect 를 수행하지 않는다.");
        }

        public ValueTask<IUdpEndpoint> BindUdpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Broker 단위 테스트용 transport 는 UDP bind 를 수행하지 않는다.");
        }

        public bool TrySend(IConnection connection, TransportSendBuffer sendBuffer)
        {
            if (object.ReferenceEquals(connection, RejectConnection))
                return false;

            _acceptedSends.Add(new CapturedSend(connection, sendBuffer));
            return true;
        }

        public bool TrySendTo(IUdpEndpoint endpoint, EndPoint remoteEndPoint, TransportSendBuffer sendBuffer)
        {
            throw new NotSupportedException("Broker 단위 테스트용 transport 는 UDP send 를 수행하지 않는다.");
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
