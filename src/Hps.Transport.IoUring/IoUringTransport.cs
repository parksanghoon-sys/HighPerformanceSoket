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
    /// Linux io_uring 기반 opt-in transport root다.
    ///
    /// Phase 6의 TCP-first 단계에서는 listen/connect/accept control plane은 .NET Socket을 사용하고,
    /// accepted/connected socket의 data plane을 후속 task에서 io_uring SQE/CQE pump로 연결한다.
    /// 기본 backend 승격은 하지 않으며, unsupported OS에서는 명시적 NotSupportedException으로 수렴한다.
    /// </summary>
    public sealed class IoUringTransport : TransportBase, ITransportEndpointDiagnostics
    {
        private const int ListenBacklog = 512;
        private const uint QueueEntries = 64;

        private readonly object _gate;
        private readonly List<IoUringConnectionListener> _listeners;
        private readonly List<TransportConnection> _connections;
        private readonly List<IoUringUdpEndpoint> _udpEndpoints;
        private IoUringQueue? _queue;
        private IoUringOperationRegistry? _operationRegistry;
        private IoUringCompletionLoop? _completionLoop;
        private bool _started;
        private bool _stopped;

        /// <summary>
        /// io_uring transport root를 만든다.
        ///
        /// 생성자는 native 자원을 열지 않는다. 실제 queue setup은 StartAsync에서 capability가 Available일 때만 수행해
        /// Windows 개발/테스트 환경에서 opt-in type을 참조하는 것만으로 native syscall에 들어가지 않게 한다.
        /// </summary>
        public IoUringTransport()
        {
            _gate = new object();
            _listeners = new List<IoUringConnectionListener>();
            _connections = new List<TransportConnection>();
            _udpEndpoints = new List<IoUringUdpEndpoint>();
        }

        /// <inheritdoc />
        public override ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IoUringCompletionLoop? completionLoop;

            lock (_gate)
            {
                if (_stopped)
                    throw new InvalidOperationException("이미 중지된 io_uring Transport는 다시 시작할 수 없습니다.");
                if (_started)
                    return default(ValueTask);

                if (IoUringCapabilityProbe.GetStatus() == IoUringCapabilityStatus.Available)
                {
                    _queue = IoUringQueue.CreateForProbe(QueueEntries);
                    _operationRegistry = new IoUringOperationRegistry();
                    _completionLoop = new IoUringCompletionLoop(_queue, _operationRegistry);
                }

                completionLoop = _completionLoop;
                _started = true;
            }

            if (completionLoop != null)
                return completionLoop.StartAsync(cancellationToken);

            return default(ValueTask);
        }

        /// <inheritdoc />
        public override ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCore();
            return default(ValueTask);
        }

        /// <inheritdoc />
        public override ValueTask<IConnectionListener> ListenTcpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException(nameof(localEndPoint));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureRunning();
            EnsureTcpAvailable();

            Socket listenSocket = CreateTcpSocket(localEndPoint);
            IoUringConnectionListener? listener = null;

            try
            {
                listenSocket.NoDelay = true;
                listenSocket.Bind(localEndPoint);
                listenSocket.Listen(ListenBacklog);

                listener = new IoUringConnectionListener(this, listenSocket);
                RegisterListener(listener);
                listenSocket = null!;
                return new ValueTask<IConnectionListener>(listener);
            }
            finally
            {
                if (listenSocket != null)
                    listenSocket.Dispose();
            }
        }

        /// <inheritdoc />
        public override async ValueTask<IConnection> ConnectTcpAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
        {
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureRunning();
            EnsureTcpAvailable();

            Socket? socket = CreateTcpSocket(remoteEndPoint);

            try
            {
                socket.NoDelay = true;
                await socket.ConnectAsync(remoteEndPoint, cancellationToken).ConfigureAwait(false);

                TransportConnection connection = CreateIoUringConnection(socket);
                socket = null;
                return connection;
            }
            finally
            {
                socket?.Dispose();
            }
        }

        /// <inheritdoc />
        public override ValueTask<IUdpEndpoint> BindUdpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException(nameof(localEndPoint));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureRunning();
            EnsureUdpAvailable();

            IoUringOperationRegistry registry;
            IoUringCompletionLoop completionLoop;

            lock (_gate)
            {
                if (_operationRegistry == null || _completionLoop == null)
                    throw CreateUnsupportedException();

                registry = _operationRegistry;
                completionLoop = _completionLoop;
            }

            Socket? socket = CreateUdpSocket(localEndPoint);
            IoUringUdpEndpoint? udpEndpoint = null;

            try
            {
                socket.Bind(localEndPoint);
                udpEndpoint = new IoUringUdpEndpoint(this, socket, registry, completionLoop);
                RegisterUdpEndpoint(udpEndpoint);
                StartUdpReceiveLoop(udpEndpoint);
                StartUdpSendLoop(udpEndpoint);
                socket = null;
                return new ValueTask<IUdpEndpoint>(udpEndpoint);
            }
            finally
            {
                if (socket != null)
                {
                    udpEndpoint?.Dispose();
                    socket.Dispose();
                }
            }
        }

        /// <inheritdoc />
        public override bool TrySendTo(IUdpEndpoint endpoint, EndPoint remoteEndPoint, TransportSendBuffer sendBuffer)
        {
            if (endpoint == null)
                throw new ArgumentNullException(nameof(endpoint));
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));

            IoUringUdpEndpoint? udpEndpoint = endpoint as IoUringUdpEndpoint;
            if (udpEndpoint == null)
                throw new ArgumentException("이 Transport 구현이 생성한 UDP endpoint만 사용할 수 있습니다.", nameof(endpoint));

            IPEndPoint? ipEndPoint = remoteEndPoint as IPEndPoint;
            if (ipEndPoint == null || ipEndPoint.AddressFamily != AddressFamily.InterNetwork)
                return false;

            RefCountedBuffer buffer = sendBuffer.Buffer;
            _ = buffer.Memory;

            if (udpEndpoint.IsClosed || udpEndpoint.IsDisposed)
                return false;

            return udpEndpoint.TryAcceptSend(ipEndPoint, sendBuffer);
        }

        /// <inheritdoc />
        public EndpointSnapshot[] GetEndpointSnapshots()
        {
            TransportConnection[] connections;
            IoUringUdpEndpoint[] udpEndpoints;

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

        /// <inheritdoc />
        public override void Dispose()
        {
            StopCore();
        }

        internal TransportConnection CreateAcceptedConnection(Socket socket)
        {
            if (socket == null)
                throw new ArgumentNullException(nameof(socket));

            socket.NoDelay = true;
            return CreateIoUringConnection(socket);
        }

        internal void UnregisterListener(IoUringConnectionListener listener)
        {
            lock (_gate)
            {
                _listeners.Remove(listener);
            }
        }

        internal void UnregisterUdpEndpoint(IoUringUdpEndpoint udpEndpoint)
        {
            lock (_gate)
            {
                _udpEndpoints.Remove(udpEndpoint);
            }
        }

        private TransportConnection CreateIoUringConnection(Socket socket)
        {
            IoUringOperationRegistry registry;
            IoUringCompletionLoop completionLoop;

            lock (_gate)
            {
                if (_operationRegistry == null || _completionLoop == null)
                    throw CreateUnsupportedException();

                registry = _operationRegistry;
                completionLoop = _completionLoop;
            }

            IoUringTcpConnectionResource resource = new IoUringTcpConnectionResource(socket, registry, completionLoop);
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

        private void RegisterListener(IoUringConnectionListener listener)
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

        private void RegisterUdpEndpoint(IoUringUdpEndpoint udpEndpoint)
        {
            lock (_gate)
            {
                _udpEndpoints.Add(udpEndpoint);
            }
        }

        private void UnregisterConnection(TransportConnection connection)
        {
            lock (_gate)
            {
                _connections.Remove(connection);
            }
        }

        private void StartReceiveLoop(TransportConnection connection, IoUringTcpConnectionResource resource)
        {
            _ = Task.Run(delegate()
            {
                return ReceiveLoopAsync(connection, resource);
            });
        }

        private void StartSendLoop(TransportConnection connection, IoUringTcpConnectionResource resource)
        {
            _ = Task.Run(delegate()
            {
                return SendLoopAsync(connection, resource);
            });
        }

        private void StartUdpReceiveLoop(IoUringUdpEndpoint udpEndpoint)
        {
            _ = Task.Run(delegate()
            {
                return UdpReceiveLoopAsync(udpEndpoint);
            });
        }

        private void StartUdpSendLoop(IoUringUdpEndpoint udpEndpoint)
        {
            _ = Task.Run(delegate()
            {
                return UdpSendLoopAsync(udpEndpoint);
            });
        }

        private async Task ReceiveLoopAsync(TransportConnection connection, IoUringTcpConnectionResource resource)
        {
            try
            {
                while (true)
                {
                    if (connection.IsClosed || resource.IsDisposed)
                        return;

                    IoUringOperationContext context = resource.ReceiveContext;
                    context.Reset(context.Token, IoUringOperationKind.Receive);
                    ValueTask<IoUringCompletion> wait = context.WaitAsync();

                    byte[] receiveBlock = resource.ReceiveBlock;
                    bool submitted = resource.Queue.TrySubmitReceive(
                        resource.SocketFileDescriptor,
                        receiveBlock,
                        receiveBlock.Length,
                        context.Token);
                    if (!submitted)
                        throw new SocketException((int)SocketError.NoBufferSpaceAvailable);

                    IoUringCompletion completion = await wait.ConfigureAwait(false);
                    if (completion.Result <= 0 || completion.Result > receiveBlock.Length)
                    {
                        NotifyConnectionClosed(connection);
                        return;
                    }

                    DispatchReceived(connection, receiveBlock, completion.Result);
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                NotifyConnectionClosed(connection);
            }
            catch
            {
                NotifyConnectionClosed(connection);
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

        private async Task UdpReceiveLoopAsync(IoUringUdpEndpoint udpEndpoint)
        {
            RefCountedBuffer? datagram = null;

            try
            {
                while (true)
                {
                    if (udpEndpoint.IsClosed || udpEndpoint.IsDisposed)
                        return;

                    datagram = udpEndpoint.ReceivePool.RentCounted();
                    ArraySegment<byte> receiveSegment = GetRefCountedBlockSegment(datagram, 0, udpEndpoint.ReceivePool.BlockSize);
                    if (receiveSegment.Array == null)
                        throw new InvalidOperationException("io_uring UDP receive는 pinned byte[] 기반 RefCountedBuffer만 지원합니다.");

                    IoUringOperationContext context = udpEndpoint.ReceiveContext;
                    context.Reset(context.Token, IoUringOperationKind.UdpReceive);
                    udpEndpoint.ReceiveMessage.PrepareReceive(receiveSegment.Array, receiveSegment.Offset, receiveSegment.Count);
                    ValueTask<IoUringCompletion> wait = context.WaitAsync();

                    bool submitted = udpEndpoint.Queue.TrySubmitReceiveMessage(
                        udpEndpoint.SocketFileDescriptor,
                        udpEndpoint.ReceiveMessage.MessageHeaderPointer,
                        context.Token);
                    if (!submitted)
                        throw new SocketException((int)SocketError.NoBufferSpaceAvailable);

                    IoUringCompletion completion = await wait.ConfigureAwait(false);
                    if (completion.Result < 0)
                        throw new SocketException(-completion.Result);
                    if (completion.Result > receiveSegment.Count)
                        throw new SocketException((int)SocketError.MessageSize);

                    datagram.SetLength(completion.Result);
                    EndPoint remoteEndPoint = udpEndpoint.ReceiveMessage.DecodeRemoteEndPoint();

                    RefCountedBuffer ownedDatagram = datagram;
                    datagram = null;
                    DispatchDatagramReceived(udpEndpoint, remoteEndPoint, ownedDatagram);
                }
            }
            catch (ObjectDisposedException)
            {
                datagram?.Release();
            }
            catch (SocketException)
            {
                datagram?.Release();
                NotifyUdpEndpointClosed(udpEndpoint);
            }
            catch
            {
                datagram?.Release();
                NotifyUdpEndpointClosed(udpEndpoint);
            }
        }

        private void DispatchDatagramReceived(IoUringUdpEndpoint udpEndpoint, EndPoint remoteEndPoint, RefCountedBuffer datagram)
        {
            ITransportDatagramHandler? datagramHandler = ReadDatagramHandlerSnapshot();
            if (datagramHandler == null)
            {
                datagram.Release();
                return;
            }

            datagramHandler.OnDatagramReceived(udpEndpoint, remoteEndPoint, datagram);
        }

        private void NotifyUdpEndpointClosed(IoUringUdpEndpoint udpEndpoint)
        {
            ITransportDatagramHandler? datagramHandler = ReadDatagramHandlerSnapshot();
            if (datagramHandler != null)
                datagramHandler.OnDatagramEndpointClosed(udpEndpoint);

            udpEndpoint.Close();
        }

        private async Task UdpSendLoopAsync(IoUringUdpEndpoint udpEndpoint)
        {
            while (true)
            {
                await udpEndpoint.WaitForSendSignalAsync().ConfigureAwait(false);

                while (udpEndpoint.TryBeginSend(out IoUringUdpEndpoint.UdpSendRequest sendRequest))
                {
                    try
                    {
                        await SendUdpDatagramAsync(udpEndpoint, sendRequest.RemoteEndPoint, sendRequest.SendBuffer).ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (SocketException)
                    {
                        NotifyUdpEndpointClosed(udpEndpoint);
                        return;
                    }
                }

                if (udpEndpoint.IsClosed || udpEndpoint.IsDisposed)
                    return;
            }
        }

        private async Task SendUdpDatagramAsync(
            IoUringUdpEndpoint udpEndpoint,
            EndPoint remoteEndPoint,
            TransportSendBuffer sendBuffer)
        {
            RefCountedBuffer buffer = sendBuffer.Buffer;

            try
            {
                IPEndPoint? ipEndPoint = remoteEndPoint as IPEndPoint;
                if (ipEndPoint == null || ipEndPoint.AddressFamily != AddressFamily.InterNetwork)
                    throw new SocketException((int)SocketError.AddressFamilyNotSupported);

                ArraySegment<byte> segment = GetRefCountedBlockSegment(buffer, sendBuffer.Offset, sendBuffer.Length);
                if (segment.Array == null)
                    throw new InvalidOperationException("io_uring UDP send는 pinned byte[] 기반 RefCountedBuffer만 지원합니다.");

                IoUringOperationContext context = udpEndpoint.SendContext;
                context.Reset(context.Token, IoUringOperationKind.UdpSend);
                udpEndpoint.SendMessage.PrepareSend(segment.Array, segment.Offset, segment.Count, ipEndPoint);
                ValueTask<IoUringCompletion> wait = context.WaitAsync();

                bool submitted = udpEndpoint.Queue.TrySubmitSendMessage(
                    udpEndpoint.SocketFileDescriptor,
                    udpEndpoint.SendMessage.MessageHeaderPointer,
                    context.Token);
                if (!submitted)
                    throw new SocketException((int)SocketError.NoBufferSpaceAvailable);

                IoUringCompletion completion = await wait.ConfigureAwait(false);
                if (completion.Result < 0)
                    throw new SocketException(-completion.Result);
                if (completion.Result != segment.Count)
                    throw new SocketException((int)SocketError.MessageSize);
            }
            finally
            {
                buffer.Release();
            }
        }

        private async Task SendLoopAsync(TransportConnection connection, IoUringTcpConnectionResource resource)
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
            IoUringTcpConnectionResource resource,
            TransportConnection connection,
            TransportSendBuffer sendBuffer)
        {
            if (sendBuffer.PrependLengthPrefix)
            {
                WriteBigEndianLength(resource.LengthPrefixBlock, sendBuffer.Length);
                await SendArrayAsync(resource, connection, resource.LengthPrefixBlock, 0, 4).ConfigureAwait(false);
            }

            if (sendBuffer.Length == 0)
                return;

            ArraySegment<byte> segment = GetRefCountedBlockSegment(sendBuffer.Buffer, sendBuffer.Offset, sendBuffer.Length);
            if (segment.Array == null)
                throw new InvalidOperationException("io_uring TCP send는 pinned byte[] 기반 RefCountedBuffer만 지원합니다.");

            await SendArrayAsync(resource, connection, segment.Array, segment.Offset, segment.Count).ConfigureAwait(false);
        }

        private static async Task SendArrayAsync(
            IoUringTcpConnectionResource resource,
            TransportConnection connection,
            byte[] buffer,
            int offset,
            int length)
        {
            int currentOffset = offset;
            int remaining = length;

            while (remaining != 0)
            {
                if (connection.IsClosed || resource.IsDisposed)
                    throw new ObjectDisposedException(nameof(TransportConnection));

                IoUringOperationContext context = resource.SendContext;
                context.Reset(context.Token, IoUringOperationKind.Send);
                ValueTask<IoUringCompletion> wait = context.WaitAsync();

                bool submitted = resource.Queue.TrySubmitSend(
                    resource.SocketFileDescriptor,
                    buffer,
                    currentOffset,
                    remaining,
                    context.Token);
                if (!submitted)
                    throw new SocketException((int)SocketError.NoBufferSpaceAvailable);

                IoUringCompletion completion = await wait.ConfigureAwait(false);
                if (completion.Result <= 0 || completion.Result > remaining)
                    throw new SocketException((int)SocketError.ConnectionReset);

                currentOffset += completion.Result;
                remaining -= completion.Result;
            }
        }

        private static void WriteBigEndianLength(byte[] buffer, int value)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (buffer.Length < 4)
                throw new ArgumentException("TCP length prefix buffer는 최소 4바이트여야 합니다.", nameof(buffer));

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
                throw new InvalidOperationException("io_uring TCP send는 pinned byte[] 기반 RefCountedBuffer만 지원합니다.");

            return segment;
        }

        private void StopCore()
        {
            IoUringConnectionListener[] listeners;
            TransportConnection[] connections;
            IoUringUdpEndpoint[] udpEndpoints;
            IoUringCompletionLoop? completionLoop;
            IoUringQueue? queue;

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

                completionLoop = _completionLoop;
                queue = _queue;
                _completionLoop = null;
                _operationRegistry = null;
                _queue = null;
            }

            for (int index = 0; index < listeners.Length; index++)
                listeners[index].Close();

            for (int index = 0; index < connections.Length; index++)
                connections[index].Close();

            for (int index = 0; index < udpEndpoints.Length; index++)
                udpEndpoints[index].Dispose();

            completionLoop?.Dispose();
            queue?.Dispose();
        }

        private void EnsureRunning()
        {
            lock (_gate)
            {
                if (!_started || _stopped)
                    throw new InvalidOperationException("io_uring Transport가 실행 중이 아닙니다.");
            }
        }

        private void EnsureTcpAvailable()
        {
            if (IoUringCapabilityProbe.GetStatus() != IoUringCapabilityStatus.Available)
                throw CreateUnsupportedException();

            lock (_gate)
            {
                if (_queue == null || _operationRegistry == null || _completionLoop == null)
                    throw new NotSupportedException("io_uring TCP queue가 아직 초기화되지 않았습니다.");
            }
        }

        private void EnsureUdpAvailable()
        {
            if (IoUringCapabilityProbe.GetStatus() != IoUringCapabilityStatus.Available)
                throw CreateUnsupportedException();

            lock (_gate)
            {
                if (_queue == null || _operationRegistry == null || _completionLoop == null)
                    throw new NotSupportedException("io_uring UDP queue가 아직 초기화되지 않았습니다.");
            }
        }

        private static Socket CreateTcpSocket(EndPoint endPoint)
        {
            IPEndPoint? ipEndPoint = endPoint as IPEndPoint;
            if (ipEndPoint == null)
                throw new NotSupportedException("io_uring TCP v1은 IPEndPoint만 지원합니다.");

            return new Socket(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        }

        private static Socket CreateUdpSocket(EndPoint endPoint)
        {
            IPEndPoint? ipEndPoint = endPoint as IPEndPoint;
            if (ipEndPoint == null)
                throw new NotSupportedException("io_uring UDP v1은 IPEndPoint만 지원합니다.");
            if (ipEndPoint.AddressFamily != AddressFamily.InterNetwork)
                throw new NotSupportedException("io_uring UDP v1은 IPv4 IPEndPoint만 지원합니다.");

            return new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        }

        private static NotSupportedException CreateUnsupportedException()
        {
            IoUringCapabilityStatus status = IoUringCapabilityProbe.GetStatus();

            if (status == IoUringCapabilityStatus.UnsupportedOperatingSystem)
                return new NotSupportedException("io_uring backend는 Linux에서만 사용할 수 있습니다.");

            return new NotSupportedException("현재 환경에서 io_uring native TCP pump를 사용할 수 없습니다.");
        }
    }
}
