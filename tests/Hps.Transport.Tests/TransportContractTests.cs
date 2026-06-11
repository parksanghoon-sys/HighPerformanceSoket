using System;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hps.Buffers;
using Hps.Transport;
using Xunit;

namespace Hps.Transport.Tests
{
    public sealed class TransportContractTests
    {
        // Transport 송신 계약 테스트: Phase 2 진입점은 raw Memory<byte>가 아니라 RefCountedBuffer 기반 핸들을
        // 받아야 한다. 그래야 RIO/io_uring 등록 버퍼 식별과 송신 완료 후 Release 책임을 인터페이스에서 잃지 않는다.
        [Fact]
        public void TransportSendBuffer_WhenCreated_ExposesRefCountedBufferAndPayloadRange()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
            RefCountedBuffer buffer = pool.RentCounted();
            buffer.SetLength(12);

            TransportSendBuffer sendBuffer = new TransportSendBuffer(buffer, 3, 7);

            Assert.Same(buffer, sendBuffer.Buffer);
            Assert.Equal(3, sendBuffer.Offset);
            Assert.Equal(7, sendBuffer.Length);

            buffer.Release();
            Assert.Equal(0, pool.RentedCount);
        }

        // 송신 범위 경계 테스트: Transport 는 RefCountedBuffer 전체 블록이 아니라 Length 로 publish 된 payload 범위만
        // 전송해야 한다. offset/length 가 payload 밖을 가리키면 이후 송신 펌프가 미초기화 영역을 보낼 수 있다.
        [Fact]
        public void TransportSendBuffer_WhenRangeIsOutsidePayloadLength_Throws()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
            RefCountedBuffer buffer = pool.RentCounted();
            buffer.SetLength(4);

            Assert.Throws<ArgumentOutOfRangeException>(delegate()
            {
                new TransportSendBuffer(buffer, -1, 1);
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate()
            {
                new TransportSendBuffer(buffer, 5, 0);
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate()
            {
                new TransportSendBuffer(buffer, 2, 3);
            });

            buffer.Release();
            Assert.Equal(0, pool.RentedCount);
        }

        // 반환된 버퍼 방어 테스트: 길이 0 요청이라도 이미 풀에 돌아간 RefCountedBuffer 를 송신 큐에 넣으면
        // 송신 펌프가 나중에 반환된 블록에 접근하게 된다. 계약 타입 생성 시점에서 즉시 거부해야 원인을 좁힐 수 있다.
        [Fact]
        public void TransportSendBuffer_WhenBufferAlreadyReturned_Throws()
        {
            PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(32);
            RefCountedBuffer buffer = pool.RentCounted();

            buffer.Release();

            Assert.Throws<ObjectDisposedException>(delegate()
            {
                new TransportSendBuffer(buffer, 0, 0);
            });
            Assert.Equal(0, pool.RentedCount);
        }

        // 송신 위치 계약 테스트: IConnection 은 연결 핸들과 수명에 집중하고, 송신 시도와 소유권 판정은
        // Transport 가 맡아야 한다. 큐라는 내부 구현 세부사항이 연결 public API 로 새면 책임 경계가 흐려진다.
        [Fact]
        public void Transport_Contract_SendsThroughTransportWithoutConnectionQueueMethod()
        {
            Type connectionType = typeof(IConnection);
            Type transportType = typeof(ITransport);

            MethodInfo? trySend = transportType.GetMethod("TrySend", new Type[] { typeof(IConnection), typeof(TransportSendBuffer) });
            Assert.NotNull(trySend);
            Assert.Equal(typeof(bool), trySend!.ReturnType);

            Assert.DoesNotContain(connectionType.GetMethods(), delegate(MethodInfo method)
            {
                ParameterInfo[] parameters = method.GetParameters();
                for (int parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
                {
                    if (parameters[parameterIndex].ParameterType == typeof(TransportSendBuffer))
                        return true;
                }

                return false;
            });

            AssertDoesNotExposeRawMemoryParameters(connectionType);
            AssertDoesNotExposeRawMemoryParameters(transportType);
        }

        // TCP 연결 획득 계약 테스트: SAEA 기준선을 구현하기 전에 상위 계층이 어떤 public API 로
        // listen/connect/accept 된 IConnection 을 얻는지 먼저 고정한다. UDP 는 accept 개념이 없으므로 이 계약에 섞지 않는다.
        [Fact]
        public void Transport_Contract_ExposesTcpListenConnectAcceptModel()
        {
            Type transportType = typeof(ITransport);
            Type connectionType = typeof(IConnection);
            Type listenerType = typeof(IConnectionListener);

            MethodInfo? listen = transportType.GetMethod("ListenTcpAsync", new Type[] { typeof(EndPoint), typeof(CancellationToken) });
            MethodInfo? connect = transportType.GetMethod("ConnectTcpAsync", new Type[] { typeof(EndPoint), typeof(CancellationToken) });
            MethodInfo? accept = listenerType.GetMethod("AcceptAsync", new Type[] { typeof(CancellationToken) });
            MethodInfo? close = listenerType.GetMethod("Close", Type.EmptyTypes);
            PropertyInfo? localEndPoint = listenerType.GetProperty("LocalEndPoint");

            Assert.NotNull(listen);
            Assert.Equal(typeof(ValueTask<>).MakeGenericType(listenerType), listen!.ReturnType);
            Assert.NotNull(connect);
            Assert.Equal(typeof(ValueTask<IConnection>), connect!.ReturnType);
            Assert.NotNull(accept);
            Assert.Equal(typeof(ValueTask<IConnection>), accept!.ReturnType);
            Assert.NotNull(close);
            Assert.NotNull(localEndPoint);
            Assert.Equal(typeof(EndPoint), localEndPoint!.PropertyType);
            Assert.Contains(typeof(IDisposable), listenerType.GetInterfaces());

            AssertDoesNotExposeRawMemoryParameters(connectionType);
            AssertDoesNotExposeRawMemoryParameters(listenerType);
            AssertDoesNotExposeRawMemoryParameters(transportType);
        }

        // 수신 전달 계약 테스트: Transport 는 socket recv 버퍼를 상위 계층에 raw Memory 로 넘기지 않고,
        // 동기 콜백 동안만 유효한 ref struct view 로 전달해야 한다. 그래야 recv ring/pinned buffer 수명이 밖으로 새지 않는다.
        [Fact]
        public void Transport_Contract_ExposesBorrowedReceiveDeliveryBoundary()
        {
            Type transportType = typeof(ITransport);
            Type connectionType = typeof(IConnection);
            Type receiveHandlerType = typeof(ITransportReceiveHandler);
            Type receiveBufferType = typeof(TransportReceiveBuffer);

            Assert.True(receiveBufferType.IsByRefLike);

            MethodInfo? setReceiveHandler = transportType.GetMethod("SetReceiveHandler", new Type[] { receiveHandlerType });
            MethodInfo? onReceived = receiveHandlerType.GetMethod("OnReceived", new Type[] { typeof(IConnection), receiveBufferType });
            MethodInfo? onConnectionClosed = receiveHandlerType.GetMethod("OnConnectionClosed", new Type[] { typeof(IConnection) });
            PropertyInfo? span = receiveBufferType.GetProperty("Span");
            PropertyInfo? length = receiveBufferType.GetProperty("Length");

            Assert.NotNull(setReceiveHandler);
            Assert.Equal(typeof(void), setReceiveHandler!.ReturnType);
            Assert.NotNull(onReceived);
            Assert.Equal(typeof(void), onReceived!.ReturnType);
            Assert.NotNull(onConnectionClosed);
            Assert.Equal(typeof(void), onConnectionClosed!.ReturnType);
            Assert.NotNull(span);
            Assert.Equal(typeof(ReadOnlySpan<byte>), span!.PropertyType);
            Assert.NotNull(length);
            Assert.Equal(typeof(int), length!.PropertyType);

            byte[] sample = new byte[] { 1, 2, 3 };
            TransportReceiveBuffer receiveBuffer = new TransportReceiveBuffer(sample);
            Assert.Equal(3, receiveBuffer.Length);
            Assert.Equal(2, receiveBuffer.Span[1]);

            AssertDoesNotExposeRawMemoryParameters(connectionType);
            AssertDoesNotExposeRawMemoryParameters(receiveHandlerType);
            AssertDoesNotExposeRawMemoryParameters(transportType);
            AssertDoesNotExposeRawMemoryProperties(receiveBufferType);
        }

        private static void AssertDoesNotExposeRawMemoryParameters(Type contractType)
        {
            Assert.DoesNotContain(contractType.GetMethods(), delegate(MethodInfo method)
            {
                ParameterInfo[] parameters = method.GetParameters();
                for (int parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
                {
                    Type parameterType = parameters[parameterIndex].ParameterType;
                    if (parameterType == typeof(Memory<byte>) || parameterType == typeof(ReadOnlyMemory<byte>))
                        return true;
                }

                return false;
            });
        }

        private static void AssertDoesNotExposeRawMemoryProperties(Type contractType)
        {
            Assert.DoesNotContain(contractType.GetProperties(), delegate(PropertyInfo property)
            {
                Type propertyType = property.PropertyType;
                return propertyType == typeof(Memory<byte>) || propertyType == typeof(ReadOnlyMemory<byte>);
            });
        }
    }
}
