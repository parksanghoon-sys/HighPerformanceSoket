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

        // UDP datagram 계약 테스트: UDP 는 accept 된 연결이 없으므로 TCP listener/connection 모델에 억지로 끼우지 않는다.
        // 또한 D009에 따라 수신 datagram 은 복사 없이 RefCountedBuffer 소유권으로 handler 에 전달되어야 한다.
        [Fact]
        public void Transport_Contract_ExposesUdpDatagramModelWithoutTcpConnection()
        {
            Type transportType = typeof(ITransport);
            Type? udpEndpointType = Type.GetType("Hps.Transport.IUdpEndpoint, Hps.Transport");
            Type? datagramHandlerType = Type.GetType("Hps.Transport.ITransportDatagramHandler, Hps.Transport");

            Assert.NotNull(udpEndpointType);
            Assert.NotNull(datagramHandlerType);

            MethodInfo? setDatagramHandler = transportType.GetMethod("SetDatagramHandler", new Type[] { datagramHandlerType! });
            MethodInfo? bindUdp = transportType.GetMethod("BindUdpAsync", new Type[] { typeof(EndPoint), typeof(CancellationToken) });
            MethodInfo? trySendTo = transportType.GetMethod("TrySendTo", new Type[] { udpEndpointType!, typeof(EndPoint), typeof(TransportSendBuffer) });
            MethodInfo? close = udpEndpointType!.GetMethod("Close", Type.EmptyTypes);
            PropertyInfo? localEndPoint = udpEndpointType.GetProperty("LocalEndPoint");
            MethodInfo? onDatagramReceived = datagramHandlerType!.GetMethod("OnDatagramReceived", new Type[] { udpEndpointType, typeof(EndPoint), typeof(RefCountedBuffer) });
            MethodInfo? onDatagramEndpointClosed = datagramHandlerType.GetMethod("OnDatagramEndpointClosed", new Type[] { udpEndpointType });

            Assert.NotNull(setDatagramHandler);
            Assert.Equal(typeof(void), setDatagramHandler!.ReturnType);
            Assert.NotNull(bindUdp);
            Assert.Equal(typeof(ValueTask<>).MakeGenericType(udpEndpointType), bindUdp!.ReturnType);
            Assert.NotNull(trySendTo);
            Assert.Equal(typeof(bool), trySendTo!.ReturnType);
            Assert.NotNull(close);
            Assert.NotNull(localEndPoint);
            Assert.Equal(typeof(EndPoint), localEndPoint!.PropertyType);
            Assert.Contains(typeof(IDisposable), udpEndpointType.GetInterfaces());
            Assert.NotNull(onDatagramReceived);
            Assert.Equal(typeof(void), onDatagramReceived!.ReturnType);
            Assert.NotNull(onDatagramEndpointClosed);
            Assert.Equal(typeof(void), onDatagramEndpointClosed!.ReturnType);

            AssertDoesNotExposeRawMemoryParameters(udpEndpointType);
            AssertDoesNotExposeRawMemoryParameters(datagramHandlerType);
            AssertDoesNotExposeRawMemoryParameters(transportType);
        }

        // backend selector 최소 계약 테스트: 상위 계층은 OS별 backend 구현을 직접 new 하지 않고
        // factory 를 통해 ITransport 만 받아야 한다. 현재 단계에서는 모든 환경에서 SAEA 기준선으로 fallback 한다.
        [Fact]
        public void TransportFactory_CreateDefault_ReturnsSaeaFallbackAsITransport()
        {
            using (ITransport transport = TransportFactory.CreateDefault())
            {
                Assert.IsType<SaeaTransport>(transport);
            }
        }

        // 진단 표면 계약 테스트: drop-oldest 관측성은 필요하지만 ITransport 기본 계약을 곧바로 넓히면
        // RIO/io_uring 등 모든 backend 의 필수 API 가 된다. 우선 선택적 diagnostics capability 로만 노출해 수명/송신 계약과 분리한다.
        [Fact]
        public void TransportDiagnostics_Contract_UsesOptionalCapabilityWithoutExpandingITransport()
        {
            Type diagnosticsType = typeof(ITransportDiagnostics);
            Type snapshotType = typeof(TransportDiagnosticsSnapshot);

            MethodInfo? getSnapshot = diagnosticsType.GetMethod("GetDiagnosticsSnapshot", Type.EmptyTypes);
            PropertyInfo? tcpDrops = snapshotType.GetProperty("TcpDroppedPendingSendCount");
            PropertyInfo? udpDrops = snapshotType.GetProperty("UdpDroppedPendingSendCount");
            PropertyInfo? totalDrops = snapshotType.GetProperty("DroppedPendingSendCount");
            PropertyInfo? tcpHighWatermark = snapshotType.GetProperty("TcpPendingSendQueueHighWatermark");
            PropertyInfo? udpHighWatermark = snapshotType.GetProperty("UdpPendingSendQueueHighWatermark");
            ConstructorInfo? extendedConstructor = snapshotType.GetConstructor(new Type[]
            {
                typeof(long),
                typeof(long),
                typeof(int),
                typeof(int)
            });
            TransportDiagnosticsSnapshot snapshot = new TransportDiagnosticsSnapshot(2, 3);

            Assert.NotNull(getSnapshot);
            Assert.Equal(typeof(TransportDiagnosticsSnapshot), getSnapshot!.ReturnType);
            Assert.NotNull(tcpDrops);
            Assert.Equal(typeof(long), tcpDrops!.PropertyType);
            Assert.NotNull(udpDrops);
            Assert.Equal(typeof(long), udpDrops!.PropertyType);
            Assert.NotNull(totalDrops);
            Assert.Equal(typeof(long), totalDrops!.PropertyType);
            Assert.NotNull(tcpHighWatermark);
            Assert.Equal(typeof(int), tcpHighWatermark!.PropertyType);
            Assert.NotNull(udpHighWatermark);
            Assert.Equal(typeof(int), udpHighWatermark!.PropertyType);
            Assert.NotNull(extendedConstructor);
            Assert.True(diagnosticsType.IsAssignableFrom(typeof(TransportBase)));
            Assert.Null(typeof(ITransport).GetMethod("GetDiagnosticsSnapshot", Type.EmptyTypes));
            Assert.Equal(2, snapshot.TcpDroppedPendingSendCount);
            Assert.Equal(3, snapshot.UdpDroppedPendingSendCount);
            Assert.Equal(5, snapshot.DroppedPendingSendCount);

            object extendedSnapshot = extendedConstructor!.Invoke(new object[] { 2L, 3L, 4, 5 });
            Assert.Equal(4, tcpHighWatermark.GetValue(extendedSnapshot));
            Assert.Equal(5, udpHighWatermark.GetValue(extendedSnapshot));
            Assert.Equal(0, tcpHighWatermark.GetValue(snapshot));
            Assert.Equal(0, udpHighWatermark.GetValue(snapshot));

            AssertDoesNotExposeRawMemoryParameters(diagnosticsType);
        }

        // Endpoint snapshot 최소 계약 테스트: Interface Server 는 TCP connection 과 UDP remote 를 같은 logical
        // endpoint 로 관찰해야 한다. 이번 단위는 Broker subscription 을 바꾸기 전에 public identity/snapshot 타입만
        // 고정해, 후속 구현이 어떤 필드를 채워야 하는지 먼저 명확히 한다.
        [Fact]
        public void EndpointSnapshot_Contract_ExposesStableIdentityAndSendDiagnostics()
        {
            Type? endpointIdType = Type.GetType("Hps.Transport.EndpointId, Hps.Transport");
            Type? transportKindType = Type.GetType("Hps.Transport.EndpointTransportKind, Hps.Transport");
            Type? endpointStateType = Type.GetType("Hps.Transport.EndpointState, Hps.Transport");
            Type? endpointSnapshotType = Type.GetType("Hps.Transport.EndpointSnapshot, Hps.Transport");

            Assert.NotNull(endpointIdType);
            Assert.NotNull(transportKindType);
            Assert.NotNull(endpointStateType);
            Assert.NotNull(endpointSnapshotType);

            Type equatableEndpointIdType = typeof(IEquatable<>).MakeGenericType(endpointIdType!);
            ConstructorInfo? endpointIdConstructor = endpointIdType!.GetConstructor(new Type[] { typeof(long) });
            PropertyInfo? endpointIdValue = endpointIdType.GetProperty("Value");
            ConstructorInfo? snapshotConstructor = endpointSnapshotType!.GetConstructor(new Type[]
            {
                endpointIdType,
                transportKindType!,
                endpointStateType!,
                typeof(int),
                typeof(int),
                typeof(long)
            });
            PropertyInfo? id = endpointSnapshotType.GetProperty("Id");
            PropertyInfo? transportKind = endpointSnapshotType.GetProperty("TransportKind");
            PropertyInfo? state = endpointSnapshotType.GetProperty("State");
            PropertyInfo? pendingSendCount = endpointSnapshotType.GetProperty("PendingSendCount");
            PropertyInfo? pendingSendQueueHighWatermark = endpointSnapshotType.GetProperty("PendingSendQueueHighWatermark");
            PropertyInfo? droppedPendingSendCount = endpointSnapshotType.GetProperty("DroppedPendingSendCount");

            Assert.Contains(equatableEndpointIdType, endpointIdType.GetInterfaces());
            Assert.NotNull(endpointIdConstructor);
            Assert.NotNull(endpointIdValue);
            Assert.Equal(typeof(long), endpointIdValue!.PropertyType);
            Assert.True(transportKindType!.IsEnum);
            Assert.True(endpointStateType!.IsEnum);
            Assert.NotNull(Enum.Parse(transportKindType, "Tcp"));
            Assert.NotNull(Enum.Parse(transportKindType, "Udp"));
            Assert.NotNull(Enum.Parse(endpointStateType, "Open"));
            Assert.NotNull(Enum.Parse(endpointStateType, "Closing"));
            Assert.NotNull(Enum.Parse(endpointStateType, "Closed"));
            Assert.NotNull(Enum.Parse(endpointStateType, "Faulted"));
            Assert.NotNull(snapshotConstructor);
            Assert.NotNull(id);
            Assert.Equal(endpointIdType, id!.PropertyType);
            Assert.NotNull(transportKind);
            Assert.Equal(transportKindType, transportKind!.PropertyType);
            Assert.NotNull(state);
            Assert.Equal(endpointStateType, state!.PropertyType);
            Assert.NotNull(pendingSendCount);
            Assert.Equal(typeof(int), pendingSendCount!.PropertyType);
            Assert.NotNull(pendingSendQueueHighWatermark);
            Assert.Equal(typeof(int), pendingSendQueueHighWatermark!.PropertyType);
            Assert.NotNull(droppedPendingSendCount);
            Assert.Equal(typeof(long), droppedPendingSendCount!.PropertyType);

            object endpointId = endpointIdConstructor!.Invoke(new object[] { 42L });
            object tcpKind = Enum.Parse(transportKindType, "Tcp");
            object openState = Enum.Parse(endpointStateType, "Open");
            object snapshot = snapshotConstructor!.Invoke(new object[] { endpointId, tcpKind, openState, 3, 7, 2L });

            Assert.Equal(42L, endpointIdValue.GetValue(endpointId));
            Assert.Equal(endpointId, id.GetValue(snapshot));
            Assert.Equal(tcpKind, transportKind.GetValue(snapshot));
            Assert.Equal(openState, state.GetValue(snapshot));
            Assert.Equal(3, pendingSendCount.GetValue(snapshot));
            Assert.Equal(7, pendingSendQueueHighWatermark.GetValue(snapshot));
            Assert.Equal(2L, droppedPendingSendCount.GetValue(snapshot));
            AssertDoesNotExposeRawMemoryParameters(endpointIdType);
            AssertDoesNotExposeRawMemoryParameters(endpointSnapshotType);
            AssertDoesNotExposeRawMemoryProperties(endpointSnapshotType);
        }

        // Endpoint 진단 capability 계약 테스트: endpoint 목록 관측은 기본 ITransport 송수신 계약이 아니라 선택적 diagnostics 표면이어야 한다.
        // 그래야 backend 교체와 hot path 송수신 API를 넓히지 않으면서 운영자가 active TCP/UDP endpoint snapshot 을 읽을 수 있다.
        [Fact]
        public void EndpointDiagnostics_Contract_UsesOptionalCapabilityWithoutExpandingITransport()
        {
            Type? endpointDiagnosticsType = Type.GetType("Hps.Transport.ITransportEndpointDiagnostics, Hps.Transport");

            Assert.NotNull(endpointDiagnosticsType);

            MethodInfo? getEndpointSnapshots = endpointDiagnosticsType!.GetMethod("GetEndpointSnapshots", Type.EmptyTypes);

            Assert.NotNull(getEndpointSnapshots);
            Assert.Equal(typeof(EndpointSnapshot[]), getEndpointSnapshots!.ReturnType);
            Assert.True(endpointDiagnosticsType.IsAssignableFrom(typeof(SaeaTransport)));
            Assert.Null(typeof(ITransport).GetMethod("GetEndpointSnapshots", Type.EmptyTypes));
            AssertDoesNotExposeRawMemoryParameters(endpointDiagnosticsType);
            AssertDoesNotExposeRawMemoryProperties(endpointDiagnosticsType);
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
