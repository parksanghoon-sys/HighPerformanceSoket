using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Hps.Buffers;

namespace Hps.Transport
{
    /// <summary>
    /// Windows Registered I/O 기반 TCP transport 이다.
    ///
    /// 이 구현은 아직 기본 factory 경로에 연결하지 않는 opt-in backend 이며,
    /// SAEA 기준선과 같은 <see cref="ITransport"/> 계약을 RIO native RQ/CQ 위에서 먼저 검증한다.
    /// </summary>
    public sealed class RioTransport : TransportBase, ITransportEndpointDiagnostics
    {
        private const int ReceiveBlockSize = 4096;
        private const int CompletionQueueSize = 64;
        private const int MaxOutstandingReceive = 1;
        private const int MaxOutstandingSend = 1;
        private const int SingleDataBufferPerRequest = 1;
        private const int TcpLengthPrefixSize = 4;
        private const int UdpAddressBlockSize = 32;
        private const int UdpCloseDrainDelayBudget = 8;

        private readonly object _gate;
        private readonly List<RioConnectionListener> _listeners;
        private readonly List<TransportConnection> _connections;
        private readonly List<RioUdpEndpoint> _udpEndpoints;
        private RioCompletionPort? _completionPort;
        private bool _started;
        private bool _stopped;

        public RioTransport()
        {
            _gate = new object();
            _listeners = new List<RioConnectionListener>();
            _connections = new List<TransportConnection>();
            _udpEndpoints = new List<RioUdpEndpoint>();
        }

        public override ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (_stopped)
                    throw new InvalidOperationException("이미 중지된 RIO Transport는 다시 시작할 수 없습니다.");

                _started = true;
            }

            return default(ValueTask);
        }

        public override ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RioConnectionListener[] listeners;
            TransportConnection[] connections;
            RioUdpEndpoint[] udpEndpoints;

            lock (_gate)
            {
                _stopped = true;
                _started = false;
                listeners = _listeners.ToArray();
                connections = _connections.ToArray();
                udpEndpoints = _udpEndpoints.ToArray();
                _listeners.Clear();
                _connections.Clear();
                _udpEndpoints.Clear();
            }

            for (int i = 0; i < listeners.Length; i++)
                listeners[i].Close();

            for (int i = 0; i < connections.Length; i++)
                connections[i].Close();

            for (int i = 0; i < udpEndpoints.Length; i++)
                udpEndpoints[i].Close();

            RioCompletionPort? completionPort = _completionPort;
            _completionPort = null;
            completionPort?.Dispose();

            return default(ValueTask);
        }

        public override ValueTask<IConnectionListener> ListenTcpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException(nameof(localEndPoint));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureRunning();
            ThrowIfUnsupportedTcpEndPoint(localEndPoint);
            EnsureRioAvailable();

            Socket listenSocket = RioNative.CreateTcpSocket();
            try
            {
                listenSocket.NoDelay = true;
                listenSocket.Bind(localEndPoint);
                listenSocket.Listen(backlog: 128);

                RioConnectionListener listener = new RioConnectionListener(this, listenSocket);
                RegisterListener(listener);
                listenSocket = null!;
                return new ValueTask<IConnectionListener>(listener);
            }
            finally
            {
                listenSocket?.Dispose();
            }
        }

        public override async ValueTask<IConnection> ConnectTcpAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
        {
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureRunning();
            ThrowIfUnsupportedTcpEndPoint(remoteEndPoint);
            EnsureRioAvailable();

            Socket socket = RioNative.CreateTcpSocket();
            try
            {
                socket.NoDelay = true;
                await socket.ConnectAsync(remoteEndPoint, cancellationToken).ConfigureAwait(false);

                TransportConnection connection = CreateRioConnection(socket);
                socket = null!;
                return connection;
            }
            finally
            {
                socket?.Dispose();
            }
        }

        public override ValueTask<IUdpEndpoint> BindUdpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException(nameof(localEndPoint));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureRunning();
            RioNative native = LoadRioNative();
            if (!native.SupportsDatagramOperations)
                throw new NotSupportedException("현재 RIO provider 는 RIO datagram operation 을 제공하지 않습니다.");

            ThrowIfUnsupportedUdpLocalEndPoint(localEndPoint);

            Socket socket = RioNative.CreateUdpSocket();
            RioUdpEndpoint? endpoint = null;
            try
            {
                socket.Bind(localEndPoint);
                endpoint = new RioUdpEndpoint(this, socket, native, GetOrCreateCompletionPort());
                RegisterUdpEndpoint(endpoint);
                endpoint.MarkPumpsStarted();
                StartUdpReceiveLoop(endpoint);
                StartUdpSendLoop(endpoint);
                socket = null!;
                return new ValueTask<IUdpEndpoint>(endpoint);
            }
            finally
            {
                socket?.Dispose();
            }
        }

        public override bool TrySendTo(IUdpEndpoint endpoint, EndPoint remoteEndPoint, TransportSendBuffer sendBuffer)
        {
            if (endpoint == null)
                throw new ArgumentNullException(nameof(endpoint));
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));

            RioUdpEndpoint? udpEndpoint = endpoint as RioUdpEndpoint;
            if (udpEndpoint == null)
                throw new ArgumentException("이 Transport 구현이 생성한 UDP endpoint 만 사용할 수 있습니다.", nameof(endpoint));

            RefCountedBuffer buffer = sendBuffer.Buffer;
            _ = buffer.Memory;

            if (!IsSupportedUdpEndPoint(remoteEndPoint))
                return false;

            if (udpEndpoint.IsClosed)
                return false;

            return udpEndpoint.TryAcceptSend(remoteEndPoint, sendBuffer);
        }

        public EndpointSnapshot[] GetEndpointSnapshots()
        {
            TransportConnection[] connections;
            RioUdpEndpoint[] udpEndpoints;

            lock (_gate)
            {
                connections = _connections.ToArray();
                udpEndpoints = _udpEndpoints.ToArray();
            }

            EndpointSnapshot[] snapshots = new EndpointSnapshot[connections.Length + udpEndpoints.Length];
            int snapshotIndex = 0;

            for (int index = 0; index < connections.Length; index++)
            {
                snapshots[snapshotIndex] = connections[index].CreateSnapshot(EndpointTransportKind.Tcp);
                snapshotIndex++;
            }

            for (int index = 0; index < udpEndpoints.Length; index++)
            {
                snapshots[snapshotIndex] = udpEndpoints[index].CreateSnapshot();
                snapshotIndex++;
            }

            return snapshots;
        }

        internal TransportConnection CreateAcceptedConnection(Socket socket)
        {
            if (socket == null)
                throw new ArgumentNullException(nameof(socket));

            socket.NoDelay = true;
            return CreateRioConnection(socket);
        }

        internal void UnregisterListener(RioConnectionListener listener)
        {
            lock (_gate)
            {
                _listeners.Remove(listener);
            }
        }

        private TransportConnection CreateRioConnection(Socket socket)
        {
            RioNative native = LoadRioNative();
            RioConnectionResource resource = new RioConnectionResource(native, socket, GetOrCreateCompletionPort());
            TransportConnection connection = new TransportConnection(
                CreateEndpointId(),
                resource,
                UnregisterConnection,
                RecordTcpPendingSendDrop,
                RecordTcpPendingSendDepth);

            try
            {
                RegisterConnection(connection);
                StartReceiveLoop(connection, resource);
                StartSendLoop(connection, resource);
                return connection;
            }
            catch
            {
                connection.Close();
                throw;
            }
        }

        private RioCompletionPort GetOrCreateCompletionPort()
        {
            lock (_gate)
            {
                if (_completionPort == null)
                    _completionPort = RioCompletionPort.Create();

                return _completionPort;
            }
        }

        private void RegisterListener(RioConnectionListener listener)
        {
            lock (_gate)
            {
                _listeners.Add(listener);
            }
        }

        private void RegisterConnection(TransportConnection connection)
        {
            lock (_gate)
            {
                _connections.Add(connection);
            }
        }

        private void UnregisterConnection(TransportConnection connection)
        {
            lock (_gate)
            {
                _connections.Remove(connection);
            }
        }

        internal void UnregisterUdpEndpoint(RioUdpEndpoint endpoint)
        {
            lock (_gate)
            {
                _udpEndpoints.Remove(endpoint);
            }
        }

        private void RegisterUdpEndpoint(RioUdpEndpoint endpoint)
        {
            lock (_gate)
            {
                _udpEndpoints.Add(endpoint);
            }
        }

        private void StartReceiveLoop(TransportConnection connection, RioConnectionResource resource)
        {
            // RIO는 completion 기반 API지만 이번 opt-in 기준선은 notification 없이 poll/dequeue 모델로 검증한다.
            // pump task 하나가 receive post와 completion dequeue를 직렬화해 RQ quota 1개 불변식을 단순하게 유지한다.
            _ = Task.Run(delegate()
            {
                return ReceiveLoopAsync(connection, resource);
            });
        }

        private void StartSendLoop(TransportConnection connection, RioConnectionResource resource)
        {
            // 기존 TransportConnection pending queue를 그대로 재사용한다.
            // 따라서 drop-oldest, in-flight ref release, close drain 규칙은 SAEA와 같은 런타임 모델을 따른다.
            _ = Task.Run(delegate()
            {
                return SendLoopAsync(connection, resource);
            });
        }

        private void StartUdpReceiveLoop(RioUdpEndpoint endpoint)
        {
            // RIO UDP receive 는 handler dispatch 전에 다음 ReceiveEx 를 하나만 미리 post 한다.
            // handler 병렬 호출 없이 receive-not-armed window 를 줄이되, receive operation owner 가 current/next buffer 수명을 단일 경로로 정리한다.
            // async method 를 직접 시작하면 첫 await 전까지 현재 call stack 에서 실행되므로 BindUdpAsync 반환 전에 첫 ReceiveEx 가 post 된다.
            // RIO UDP 는 receive post 이전에 도착한 datagram 을 안정적으로 completion 하지 않을 수 있어, Task.Run scheduling race 를 피한다.
            _ = UdpReceiveLoopAsync(endpoint);
        }

        private void StartUdpSendLoop(RioUdpEndpoint endpoint)
        {
            // UDP send 는 endpoint 단위 pending queue 를 단일 pump 가 drain 한다.
            // remote address scratch buffer 를 completion 전까지 재사용하지 않기 위해 MaxOutstandingSend=1 모델을 유지한다.
            _ = Task.Run(delegate()
            {
                return UdpSendLoopAsync(endpoint);
            });
        }

        private async Task UdpSendLoopAsync(RioUdpEndpoint endpoint)
        {
            try
            {
                while (true)
                {
                    await endpoint.WaitForSendSignalAsync().ConfigureAwait(false);

                    while (endpoint.TryBeginSend(out RioUdpEndpoint.UdpSendRequest sendRequest))
                    {
                        await SendUdpDatagramAsync(endpoint, sendRequest.RemoteEndPoint, sendRequest.SendBuffer).ConfigureAwait(false);
                    }

                    if (endpoint.IsClosed)
                        return;
                }
            }
            finally
            {
                endpoint.CompleteSendDrain();
            }
        }

        private async Task UdpReceiveLoopAsync(RioUdpEndpoint endpoint)
        {
            RioUdpReceiveSlot[]? slots = null;
            ReceivedRioUdpDatagram? received = null;

            try
            {
                slots = CreateUdpReceiveSlots(endpoint);
                for (int index = 0; index < slots.Length; index++)
                    slots[index].Post();

                while (true)
                {
                    RioResult completion = await WaitForUdpCompletionAsync(
                        endpoint,
                        endpoint.ReceiveCompletionQueue,
                        endpoint.ReceiveSignal,
                        allowAfterClose: true).ConfigureAwait(false);

                    RioUdpReceiveSlot slot = FindUdpReceiveSlot(slots, completion.RequestContext);
                    received = slot.Complete(completion);

                    if (!endpoint.IsClosed)
                        slot.Post();

                    try
                    {
                        RefCountedBuffer dispatchDatagram = received.Datagram;
                        EndPoint dispatchRemoteEndPoint = received.RemoteEndPoint;
                        received = null;
                        DispatchDatagramReceived(endpoint, dispatchRemoteEndPoint, dispatchDatagram);
                    }
                    catch
                    {
                        NotifyUdpEndpointClosed(endpoint);
                        return;
                    }

                    if (endpoint.IsClosed)
                        return;
                }
            }
            catch (ObjectDisposedException)
            {
                if (received != null)
                {
                    received.Datagram.Release();
                    received = null;
                }

                return;
            }
            catch (SocketException)
            {
                if (received != null)
                {
                    received.Datagram.Release();
                    received = null;
                }

                NotifyUdpEndpointClosed(endpoint);
                return;
            }
            catch
            {
                if (received != null)
                {
                    received.Datagram.Release();
                    received = null;
                }

                NotifyUdpEndpointClosed(endpoint);
                return;
            }
            finally
            {
                if (received != null)
                    received.Datagram.Release();

                DisposeUdpReceiveSlots(slots);
                endpoint.CompleteReceiveDrain();
            }
        }

        private static RioUdpReceiveSlot[] CreateUdpReceiveSlots(RioUdpEndpoint endpoint)
        {
            RioUdpReceiveSlot[] slots = new RioUdpReceiveSlot[RioUdpEndpoint.ReceiveWindowSize];
            int created = 0;

            try
            {
                for (int index = 0; index < slots.Length; index++)
                {
                    slots[index] = new RioUdpReceiveSlot(endpoint, index);
                    created++;
                }
            }
            catch
            {
                for (int index = 0; index < created; index++)
                    slots[index].Dispose();

                throw;
            }

            return slots;
        }

        private static RioUdpReceiveSlot FindUdpReceiveSlot(RioUdpReceiveSlot[] slots, ulong requestContext)
        {
            if (requestContext == 0 || requestContext > (ulong)slots.Length)
                throw new SocketException((int)SocketError.ConnectionReset);

            return slots[checked((int)requestContext - 1)];
        }

        private static void DisposeUdpReceiveSlots(RioUdpReceiveSlot[]? slots)
        {
            if (slots == null)
                return;

            for (int index = 0; index < slots.Length; index++)
                slots[index].Dispose();
        }

        private sealed class ReceivedRioUdpDatagram
        {
            internal ReceivedRioUdpDatagram(RefCountedBuffer datagram, EndPoint remoteEndPoint)
            {
                Datagram = datagram;
                RemoteEndPoint = remoteEndPoint;
            }

            internal RefCountedBuffer Datagram { get; }

            internal EndPoint RemoteEndPoint { get; }
        }

        private sealed class RioUdpReceiveSlot : IDisposable
        {
            private readonly RioUdpEndpoint _endpoint;
            private readonly byte[] _remoteAddressBlock;
            private IntPtr _remoteAddressBufferId;
            private RefCountedBuffer? _datagram;
            private IntPtr _receiveBufferId;

            internal RioUdpReceiveSlot(RioUdpEndpoint endpoint, int slotId)
            {
                _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
                if (slotId < 0)
                    throw new ArgumentOutOfRangeException(nameof(slotId));

                RequestContext = checked((ulong)(slotId + 1));
                _remoteAddressBlock = endpoint.RentRemoteAddressBlock();
                _remoteAddressBufferId = IntPtr.Zero;
                _receiveBufferId = IntPtr.Zero;
                _datagram = null;

                try
                {
                    _remoteAddressBufferId = RioTransport.RegisterPinnedArray(endpoint.Native, _remoteAddressBlock);
                }
                catch
                {
                    endpoint.ReturnRemoteAddressBlock(_remoteAddressBlock);
                    throw;
                }
            }

            internal ulong RequestContext { get; }

            internal void Post()
            {
                if (_datagram != null)
                    throw new InvalidOperationException("RIO UDP receive slot 에 이미 outstanding receive 가 있습니다.");

                _datagram = _endpoint.ReceivePool.RentCounted();

                try
                {
                    PostCore();
                }
                catch
                {
                    ReleaseRegistration();
                    ReleaseDatagram();
                    throw;
                }
            }

            private void PostCore()
            {
                RefCountedBuffer datagram = RequireDatagram();
                ArraySegment<byte> receiveSegment = RioTransport.GetRefCountedBlockSegment(datagram, 0, _endpoint.ReceivePool.BlockSize);
                if (receiveSegment.Array == null)
                    throw new InvalidOperationException("RIO UDP receive 는 pinned byte[] 기반 RefCountedBuffer 만 지원합니다.");

                _receiveBufferId = RioTransport.RegisterPinnedArray(_endpoint.Native, receiveSegment.Array);
                RioBufferSegment dataSegment = new RioBufferSegment(
                    _receiveBufferId,
                    receiveSegment.Offset,
                    receiveSegment.Count);
                RioBufferSegment remoteAddressSegment = new RioBufferSegment(
                    _remoteAddressBufferId,
                    0,
                    RioUdpEndpoint.SockaddrInetBlockSize);
                IntPtr requestContext = new IntPtr(checked((long)RequestContext));

                if (!_endpoint.Native.ReceiveEx(_endpoint.RequestQueue, dataSegment, null, remoteAddressSegment, requestContext))
                    throw new SocketException((int)SocketError.ConnectionReset);
            }

            internal ReceivedRioUdpDatagram Complete(RioResult completion)
            {
                if (completion.RequestContext != RequestContext)
                    throw new SocketException((int)SocketError.ConnectionReset);

                RefCountedBuffer datagram = RequireDatagram();

                if (completion.Status != 0 || completion.BytesTransferred > _endpoint.ReceivePool.BlockSize)
                    throw new SocketException((int)SocketError.ConnectionReset);

                datagram.SetLength(checked((int)completion.BytesTransferred));
                EndPoint remoteEndPoint = RioTransport.DecodeSockaddrInet(_remoteAddressBlock);

                // completion 후 data buffer registration 을 먼저 해제해야 handler fan-out send path 가 같은 backing byte[]를
                // 독립 payload registration 으로 잡을 수 있다. datagram ref 자체는 handler 로 넘기기 전까지 이 owner 가 보유한다.
                ReleaseRegistration();

                _datagram = null;
                return new ReceivedRioUdpDatagram(datagram, remoteEndPoint);
            }

            public void Dispose()
            {
                ReleaseRegistration();
                ReleaseRemoteAddressRegistration();
                _endpoint.ReturnRemoteAddressBlock(_remoteAddressBlock);
                ReleaseDatagram();
            }

            private void ReleaseDatagram()
            {
                RefCountedBuffer? datagram = _datagram;
                _datagram = null;
                if (datagram != null)
                    datagram.Release();
            }

            private RefCountedBuffer RequireDatagram()
            {
                RefCountedBuffer? datagram = _datagram;
                if (datagram == null)
                    throw new ObjectDisposedException(nameof(RioUdpReceiveSlot));

                return datagram;
            }

            private void ReleaseRegistration()
            {
                IntPtr receiveBufferId = _receiveBufferId;
                _receiveBufferId = IntPtr.Zero;
                if (receiveBufferId != IntPtr.Zero)
                    _endpoint.Native.DeregisterBuffer(receiveBufferId);
            }

            private void ReleaseRemoteAddressRegistration()
            {
                IntPtr remoteAddressBufferId = _remoteAddressBufferId;
                _remoteAddressBufferId = IntPtr.Zero;
                if (remoteAddressBufferId != IntPtr.Zero)
                    _endpoint.Native.DeregisterBuffer(remoteAddressBufferId);
            }
        }

        private async Task ReceiveLoopAsync(TransportConnection connection, RioConnectionResource resource)
        {
            while (!connection.IsClosed)
            {
                try
                {
                    byte[] receiveBlock = resource.ReceiveBlock;
                    RioBufferSegment[] segments = new RioBufferSegment[]
                    {
                        new RioBufferSegment(resource.ReceiveBufferId, 0, receiveBlock.Length)
                    };

                    if (!resource.Native.Receive(resource.RequestQueue, segments, IntPtr.Zero))
                        throw new SocketException((int)SocketError.ConnectionReset);

                    RioResult completion = await WaitForCompletionAsync(
                        resource,
                        resource.ReceiveCompletionQueue,
                        resource.ReceiveSignal,
                        connection).ConfigureAwait(false);

                    if (completion.Status != 0 || completion.BytesTransferred == 0)
                    {
                        NotifyConnectionClosed(connection);
                        return;
                    }

                    DispatchReceived(connection, receiveBlock, checked((int)completion.BytesTransferred));
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    NotifyConnectionClosed(connection);
                    return;
                }
                catch
                {
                    // handler 예외나 native post/dequeue 예외를 background task fault로 방치하면
                    // broker close cleanup 이 누락될 수 있으므로 TCP도 UDP와 같은 close notification으로 수렴시킨다.
                    NotifyConnectionClosed(connection);
                    return;
                }
            }
        }

        private async Task SendLoopAsync(TransportConnection connection, RioConnectionResource resource)
        {
            while (true)
            {
                await connection.WaitForSendSignalAsync().ConfigureAwait(false);

                while (connection.TryBeginInFlightSend(out TransportConnection.InFlightSend? inFlightSend))
                {
                    TransportConnection.InFlightSend inFlight = inFlightSend!;

                    using (inFlight)
                    {
                        try
                        {
                            await SendInFlightAsync(resource, connection, inFlight.SendBuffer).ConfigureAwait(false);
                            inFlight.Complete();
                        }
                        catch (ObjectDisposedException)
                        {
                            return;
                        }
                        catch (SocketException)
                        {
                            NotifyConnectionClosed(connection);
                            return;
                        }
                    }
                }

                if (connection.IsClosed)
                    return;
            }
        }

        private async Task SendInFlightAsync(
            RioConnectionResource resource,
            TransportConnection connection,
            TransportSendBuffer sendBuffer)
        {
            if (sendBuffer.PrependLengthPrefix)
            {
                WriteBigEndianLength(resource.LengthPrefixBlock, sendBuffer.Length);
                await SendRegisteredBufferAsync(
                    resource,
                    connection,
                    resource.LengthPrefixBufferId,
                    0,
                    TcpLengthPrefixSize).ConfigureAwait(false);
            }

            if (sendBuffer.Length != 0)
            {
                RefCountedBuffer buffer = sendBuffer.Buffer;
                Memory<byte> memory = buffer.Memory;
                ArraySegment<byte> segment;

                if (!MemoryMarshal.TryGetArray(memory, out segment) || segment.Array == null)
                    throw new InvalidOperationException("RIO transport는 pinned byte[] 기반 RefCountedBuffer만 전송할 수 있습니다.");

                using (RioPayloadRegistrationCache.RioPayloadBufferLease lease = resource.PayloadRegistrationCache.Acquire(segment.Array))
                {
                    await SendRegisteredBufferAsync(
                        resource,
                        connection,
                        lease.BufferId,
                        segment.Offset + sendBuffer.Offset,
                        sendBuffer.Length).ConfigureAwait(false);
                }
            }
        }

        private static async Task SendUdpDatagramAsync(
            RioUdpEndpoint endpoint,
            EndPoint remoteEndPoint,
            TransportSendBuffer sendBuffer)
        {
            RefCountedBuffer buffer = sendBuffer.Buffer;

            try
            {
                ArraySegment<byte> segment = GetRefCountedBlockSegment(buffer, sendBuffer.Offset, sendBuffer.Length);
                if (segment.Array == null)
                    throw new InvalidOperationException("RIO UDP send 는 pinned byte[] 기반 RefCountedBuffer 만 지원합니다.");

                EncodeSockaddrInet(remoteEndPoint, endpoint.SendAddressBlock);
                RioBufferSegment dataSegment;
                using (RioPayloadRegistrationCache.RioPayloadBufferLease lease = endpoint.PayloadRegistrationCache.Acquire(segment.Array))
                {
                    dataSegment = new RioBufferSegment(lease.BufferId, segment.Offset, segment.Count);
                    RioBufferSegment remoteAddressSegment = endpoint.SendAddressSegment;

                    if (!endpoint.Native.SendEx(endpoint.RequestQueue, dataSegment, remoteAddressSegment, IntPtr.Zero))
                        throw new SocketException((int)SocketError.ConnectionReset);

                    RioResult completion = await WaitForUdpCompletionAsync(
                        endpoint,
                        endpoint.SendCompletionQueue,
                        endpoint.SendSignal,
                        allowAfterClose: false).ConfigureAwait(false);

                    if (completion.Status != 0 || completion.BytesTransferred != sendBuffer.Length)
                        throw new SocketException((int)SocketError.MessageSize);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
            finally
            {
                buffer.Release();
            }
        }

        private static async Task SendRegisteredBufferAsync(
            RioConnectionResource resource,
            TransportConnection connection,
            IntPtr bufferId,
            int offset,
            int length)
        {
            int currentOffset = offset;
            int remaining = length;

            while (remaining != 0)
            {
                RioBufferSegment[] segments = new RioBufferSegment[]
                {
                    new RioBufferSegment(bufferId, currentOffset, remaining)
                };

                if (!resource.Native.Send(resource.RequestQueue, segments, IntPtr.Zero))
                    throw new SocketException((int)SocketError.ConnectionReset);

                RioResult completion = await WaitForCompletionAsync(
                    resource,
                    resource.SendCompletionQueue,
                    resource.SendSignal,
                    connection).ConfigureAwait(false);

                if (completion.Status != 0 || completion.BytesTransferred == 0 || completion.BytesTransferred > remaining)
                    throw new SocketException((int)SocketError.ConnectionReset);

                int sent = checked((int)completion.BytesTransferred);
                currentOffset += sent;
                remaining -= sent;
            }
        }

        private static async Task<RioResult> WaitForCompletionAsync(
            RioConnectionResource resource,
            IntPtr completionQueue,
            RioCompletionSignal signal,
            TransportConnection connection)
        {
            RioResult[] results = new RioResult[1];

            while (true)
            {
                if (connection.IsClosed || resource.IsDisposed)
                    throw new ObjectDisposedException(nameof(TransportConnection));

                uint completed = resource.DequeueCompletion(completionQueue, results);
                if (completed != 0)
                    return results[0];

                if (connection.IsClosed)
                    throw new ObjectDisposedException(nameof(TransportConnection));

                resource.ArmNotification(completionQueue, signal);
                await signal.WaitAsync().ConfigureAwait(false);
            }
        }

        private static async Task<RioResult> WaitForUdpCompletionAsync(
            RioUdpEndpoint endpoint,
            IntPtr completionQueue,
            RioCompletionSignal signal,
            bool allowAfterClose)
        {
            RioResult[] results = new RioResult[1];
            int closeDelayAttempts = 0;

            while (true)
            {
                if (endpoint.IsDisposed)
                    throw new ObjectDisposedException(nameof(RioUdpEndpoint));

                uint completed = endpoint.DequeueCompletion(completionQueue, results);
                if (completed != 0)
                    return results[0];

                if (endpoint.IsClosed)
                {
                    if (!allowAfterClose)
                        throw new ObjectDisposedException(nameof(RioUdpEndpoint));

                    if (closeDelayAttempts < UdpCloseDrainDelayBudget)
                    {
                        closeDelayAttempts++;
                        await Task.Delay(1).ConfigureAwait(false);
                        continue;
                    }

                    throw new ObjectDisposedException(nameof(RioUdpEndpoint));
                }

                // 정상 open 경로는 TCP RIO와 동일하게 CQ notification을 arm한 뒤 IOCP 신호를 기다린다.
                // close-drain 경로만 owner 정리를 위해 제한된 delay fallback을 유지한다.
                endpoint.ArmNotification(completionQueue, signal);
                await signal.WaitAsync().ConfigureAwait(false);
            }
        }

        private void DispatchReceived(TransportConnection connection, byte[] receiveBlock, int received)
        {
            ITransportReceiveHandler? receiveHandler = ReadReceiveHandlerSnapshot();
            if (receiveHandler == null)
                return;

            receiveHandler.OnReceived(connection, new TransportReceiveBuffer(new ReadOnlySpan<byte>(receiveBlock, 0, received)));
        }

        private void NotifyConnectionClosed(TransportConnection connection)
        {
            if (!connection.TryClose())
                return;

            ITransportReceiveHandler? receiveHandler = ReadReceiveHandlerSnapshot();
            if (receiveHandler != null)
                receiveHandler.OnConnectionClosed(connection);
        }

        private void DispatchDatagramReceived(RioUdpEndpoint endpoint, EndPoint remoteEndPoint, RefCountedBuffer datagram)
        {
            ITransportDatagramHandler? datagramHandler = ReadDatagramHandlerSnapshot();
            if (datagramHandler == null)
            {
                datagram.Release();
                return;
            }

            datagramHandler.OnDatagramReceived(endpoint, remoteEndPoint, datagram);
        }

        private void NotifyUdpEndpointClosed(RioUdpEndpoint endpoint)
        {
            if (!endpoint.RequestClose())
                return;

            ITransportDatagramHandler? datagramHandler = ReadDatagramHandlerSnapshot();
            if (datagramHandler != null)
                datagramHandler.OnDatagramEndpointClosed(endpoint);
        }

        private void EnsureRunning()
        {
            lock (_gate)
            {
                if (!_started || _stopped)
                    throw new InvalidOperationException("RIO Transport가 실행 중이 아닙니다.");
            }
        }

        private static void EnsureRioAvailable()
        {
            if (RioCapabilityProbe.GetStatus() != RioCapabilityStatus.Available)
                throw new NotSupportedException("현재 환경에서 Windows RIO function table을 사용할 수 없습니다.");
        }

        private static RioNative LoadRioNative()
        {
            RioNative? native;
            if (!RioNative.TryLoadFunctionTable(out native) || native == null)
                throw new NotSupportedException("현재 환경에서 Windows RIO function table을 사용할 수 없습니다.");

            return native;
        }

        private static bool IsSupportedUdpEndPoint(EndPoint endPoint)
        {
            return IsSupportedRioIpEndPoint(endPoint);
        }

        private static bool IsSupportedRioIpEndPoint(EndPoint endPoint)
        {
            IPEndPoint? ipEndPoint = endPoint as IPEndPoint;
            return ipEndPoint != null && ipEndPoint.AddressFamily == AddressFamily.InterNetwork;
        }

        private static void ThrowIfUnsupportedTcpEndPoint(EndPoint endPoint)
        {
            if (!IsSupportedRioIpEndPoint(endPoint))
                throw new NotSupportedException("RIO TCP v1은 IPv4 IPEndPoint 만 지원합니다.");
        }

        private static void ThrowIfUnsupportedUdpLocalEndPoint(EndPoint endPoint)
        {
            if (!IsSupportedUdpEndPoint(endPoint))
                throw new NotSupportedException("RIO UDP v1은 IPv4 IPEndPoint 만 지원합니다.");
        }

        private static void WriteBigEndianLength(byte[] buffer, int value)
        {
            buffer[0] = (byte)((value >> 24) & 0xFF);
            buffer[1] = (byte)((value >> 16) & 0xFF);
            buffer[2] = (byte)((value >> 8) & 0xFF);
            buffer[3] = (byte)(value & 0xFF);
        }

        private static ArraySegment<byte> GetRefCountedBlockSegment(RefCountedBuffer buffer, int offset, int length)
        {
            Memory<byte> memory = buffer.Memory.Slice(offset, length);
            ArraySegment<byte> segment;

            if (!MemoryMarshal.TryGetArray(memory, out segment))
                throw new InvalidOperationException("RIO transport 는 pinned byte[] 기반 RefCountedBuffer 만 지원합니다.");

            return segment;
        }

        private static EndPoint DecodeSockaddrInet(byte[] addressBlock)
        {
            if (addressBlock == null)
                throw new ArgumentNullException(nameof(addressBlock));
            if (addressBlock.Length < UdpAddressBlockSize)
                throw new ArgumentException("SOCKADDR_INET buffer 크기가 부족합니다.", nameof(addressBlock));

            int addressFamily = addressBlock[0] | (addressBlock[1] << 8);
            if (addressFamily != (int)AddressFamily.InterNetwork)
                throw new NotSupportedException("현재 RIO UDP receive 는 IPv4 SOCKADDR_INET 만 decode 합니다.");

            // SOCKADDR_IN 에서 family 만 host byte order 이고 port/address 는 network byte order 이다.
            int port = (addressBlock[2] << 8) | addressBlock[3];
            byte[] addressBytes = new byte[]
            {
                addressBlock[4],
                addressBlock[5],
                addressBlock[6],
                addressBlock[7]
            };

            return new IPEndPoint(new IPAddress(addressBytes), port);
        }

        private static void EncodeSockaddrInet(EndPoint remoteEndPoint, byte[] addressBlock)
        {
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));
            if (addressBlock == null)
                throw new ArgumentNullException(nameof(addressBlock));
            if (addressBlock.Length < UdpAddressBlockSize)
                throw new ArgumentException("SOCKADDR_INET buffer 크기가 부족합니다.", nameof(addressBlock));

            IPEndPoint? ipEndPoint = remoteEndPoint as IPEndPoint;
            if (ipEndPoint == null || ipEndPoint.AddressFamily != AddressFamily.InterNetwork)
                throw new NotSupportedException("현재 RIO UDP send 는 IPv4 IPEndPoint 만 지원합니다.");

            byte[] addressBytes = ipEndPoint.Address.GetAddressBytes();
            if (addressBytes.Length != 4)
                throw new NotSupportedException("현재 RIO UDP send 는 IPv4 address 만 지원합니다.");

            Array.Clear(addressBlock, 0, UdpAddressBlockSize);
            addressBlock[0] = (byte)((int)AddressFamily.InterNetwork & 0xFF);
            addressBlock[1] = (byte)(((int)AddressFamily.InterNetwork >> 8) & 0xFF);
            addressBlock[2] = (byte)((ipEndPoint.Port >> 8) & 0xFF);
            addressBlock[3] = (byte)(ipEndPoint.Port & 0xFF);
            addressBlock[4] = addressBytes[0];
            addressBlock[5] = addressBytes[1];
            addressBlock[6] = addressBytes[2];
            addressBlock[7] = addressBytes[3];
        }

        private static unsafe IntPtr RegisterPinnedArray(RioNative native, byte[] block)
        {
            fixed (byte* pointer = block)
            {
                return native.RegisterBuffer((IntPtr)pointer, block.Length);
            }
        }

        private sealed class RioNativeBufferRegistrar : IRioBufferRegistrar
        {
            private readonly RioNative _native;

            internal RioNativeBufferRegistrar(RioNative native)
            {
                _native = native ?? throw new ArgumentNullException(nameof(native));
            }

            public IntPtr Register(byte[] block)
            {
                return RegisterPinnedArray(_native, block);
            }

            public void Deregister(IntPtr bufferId)
            {
                _native.DeregisterBuffer(bufferId);
            }
        }

        private sealed class RioConnectionResource : IDisposable
        {
            private readonly object _completionGate;
            private byte[]? _receiveBlock;
            private byte[]? _lengthPrefixBlock;
            private int _disposed;

            internal RioConnectionResource(RioNative native, Socket socket, RioCompletionPort completionPort)
            {
                Native = native ?? throw new ArgumentNullException(nameof(native));
                Socket = socket ?? throw new ArgumentNullException(nameof(socket));
                if (completionPort == null)
                    throw new ArgumentNullException(nameof(completionPort));

                _completionGate = new object();
                ReceivePool = new PinnedBlockMemoryPool(ReceiveBlockSize);
                _receiveBlock = null;
                _lengthPrefixBlock = null;
                ReceiveBufferId = IntPtr.Zero;
                LengthPrefixBufferId = IntPtr.Zero;
                PayloadRegistrationCache = new RioPayloadRegistrationCache(new RioNativeBufferRegistrar(Native), capacity: 64);
                ReceiveCompletionQueue = IntPtr.Zero;
                SendCompletionQueue = IntPtr.Zero;
                RequestQueue = IntPtr.Zero;
                ReceiveSignal = completionPort.CreateSignal();
                SendSignal = completionPort.CreateSignal();

                try
                {
                    // receive pump 는 MaxOutstandingReceive=1 로 직렬화되어 있으므로 connection 마다 receive block 하나만
                    // 등록해도 다음 receive post 에 같은 buffer id 를 안전하게 재사용할 수 있다.
                    _receiveBlock = ReceivePool.Rent();
                    ReceiveBufferId = RegisterPinnedArray(Native, _receiveBlock);

                    // TCP outbound length prefix 는 send pump 전용 4-byte scratch 이다.
                    // prefix completion 뒤에만 payload send 로 넘어가므로 같은 registered block 을 반복 사용한다.
                    _lengthPrefixBlock = GC.AllocateUninitializedArray<byte>(TcpLengthPrefixSize, pinned: true);
                    LengthPrefixBufferId = RegisterPinnedArray(Native, _lengthPrefixBlock);

                    ReceiveCompletionQueue = Native.CreateCompletionQueue(CompletionQueueSize, ReceiveSignal.NotificationCompletionPointer);
                    SendCompletionQueue = Native.CreateCompletionQueue(CompletionQueueSize, SendSignal.NotificationCompletionPointer);
                    RequestQueue = Native.CreateRequestQueue(
                        Socket,
                        MaxOutstandingReceive,
                        SingleDataBufferPerRequest,
                        MaxOutstandingSend,
                        SingleDataBufferPerRequest,
                        ReceiveCompletionQueue,
                        SendCompletionQueue);

                    if (RequestQueue == IntPtr.Zero)
                        throw new InvalidOperationException("RIO request queue를 생성하지 못했습니다.");
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            internal RioNative Native { get; }

            internal Socket Socket { get; }

            internal PinnedBlockMemoryPool ReceivePool { get; }

            internal byte[] ReceiveBlock
            {
                get
                {
                    byte[]? receiveBlock = _receiveBlock;
                    if (receiveBlock == null)
                        throw new ObjectDisposedException(nameof(RioConnectionResource));

                    return receiveBlock;
                }
            }

            internal IntPtr ReceiveBufferId { get; private set; }

            internal byte[] LengthPrefixBlock
            {
                get
                {
                    byte[]? lengthPrefixBlock = _lengthPrefixBlock;
                    if (lengthPrefixBlock == null)
                        throw new ObjectDisposedException(nameof(RioConnectionResource));

                    return lengthPrefixBlock;
                }
            }

            internal IntPtr LengthPrefixBufferId { get; private set; }

            internal RioPayloadRegistrationCache PayloadRegistrationCache { get; }

            internal IntPtr ReceiveCompletionQueue { get; private set; }

            internal IntPtr SendCompletionQueue { get; private set; }

            internal IntPtr RequestQueue { get; private set; }

            internal RioCompletionSignal ReceiveSignal { get; }

            internal RioCompletionSignal SendSignal { get; }

            internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

            internal uint DequeueCompletion(IntPtr completionQueue, RioResult[] results)
            {
                lock (_completionGate)
                {
                    if (IsDisposed)
                        throw new ObjectDisposedException(nameof(RioConnectionResource));

                    return Native.DequeueCompletion(completionQueue, results);
                }
            }

            internal void ArmNotification(IntPtr completionQueue, RioCompletionSignal signal)
            {
                lock (_completionGate)
                {
                    if (IsDisposed)
                        throw new ObjectDisposedException(nameof(RioConnectionResource));

                    if (!signal.TryArmNotification())
                        return;

                    int notifyResult = Native.Notify(completionQueue);
                    if (notifyResult == 0)
                        return;

                    const int WsaEAlready = 10037;
                    if (notifyResult == WsaEAlready)
                        return;

                    signal.MarkNotificationArmFailed();
                    throw new SocketException(notifyResult);
                }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;

                Socket.Dispose();

                lock (_completionGate)
                {
                    // RIODequeueCompletion 과 RIOCloseCompletionQueue 가 같은 handle 에 동시에 접근하면
                    // managed 예외가 아니라 native access violation 으로 끝날 수 있다. close 는 dequeue 와 같은 gate 로 직렬화한다.
                    IntPtr receiveCompletionQueue = ReceiveCompletionQueue;
                    ReceiveCompletionQueue = IntPtr.Zero;
                    if (receiveCompletionQueue != IntPtr.Zero)
                        Native.CloseCompletionQueue(receiveCompletionQueue);

                    IntPtr sendCompletionQueue = SendCompletionQueue;
                    SendCompletionQueue = IntPtr.Zero;
                    if (sendCompletionQueue != IntPtr.Zero)
                        Native.CloseCompletionQueue(sendCompletionQueue);
                }

                IntPtr receiveBufferId = ReceiveBufferId;
                ReceiveBufferId = IntPtr.Zero;
                if (receiveBufferId != IntPtr.Zero)
                    Native.DeregisterBuffer(receiveBufferId);

                IntPtr lengthPrefixBufferId = LengthPrefixBufferId;
                LengthPrefixBufferId = IntPtr.Zero;
                if (lengthPrefixBufferId != IntPtr.Zero)
                    Native.DeregisterBuffer(lengthPrefixBufferId);

                byte[]? receiveBlock = _receiveBlock;
                _receiveBlock = null;
                if (receiveBlock != null)
                    ReceivePool.Return(receiveBlock);

                _lengthPrefixBlock = null;
                PayloadRegistrationCache.Dispose();

                ReceiveSignal.Dispose();
                SendSignal.Dispose();
            }
        }
    }
}
