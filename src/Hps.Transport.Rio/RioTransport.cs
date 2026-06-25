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
    public sealed class RioTransport : TransportBase
    {
        private const int ReceiveBlockSize = 4096;
        private const int CompletionQueueSize = 64;
        private const int MaxOutstandingReceive = 1;
        private const int MaxOutstandingSend = 1;
        private const int SingleDataBufferPerRequest = 1;
        private const int TcpLengthPrefixSize = 4;

        private readonly object _gate;
        private readonly List<RioConnectionListener> _listeners;
        private readonly List<TransportConnection> _connections;
        private RioCompletionPort? _completionPort;
        private bool _started;
        private bool _stopped;

        public RioTransport()
        {
            _gate = new object();
            _listeners = new List<RioConnectionListener>();
            _connections = new List<TransportConnection>();
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

            lock (_gate)
            {
                _stopped = true;
                _started = false;
                listeners = _listeners.ToArray();
                connections = _connections.ToArray();
                _listeners.Clear();
                _connections.Clear();
            }

            for (int i = 0; i < listeners.Length; i++)
                listeners[i].Close();

            for (int i = 0; i < connections.Length; i++)
                connections[i].Close();

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

                await SendRegisteredArrayAsync(
                    resource,
                    connection,
                    segment.Array,
                    segment.Offset + sendBuffer.Offset,
                    sendBuffer.Length).ConfigureAwait(false);
            }
        }

        private async Task SendRegisteredArrayAsync(
            RioConnectionResource resource,
            TransportConnection connection,
            byte[] block,
            int offset,
            int length)
        {
            IntPtr bufferId = IntPtr.Zero;

            try
            {
                bufferId = RegisterPinnedArray(resource.Native, block);
                await SendRegisteredBufferAsync(resource, connection, bufferId, offset, length).ConfigureAwait(false);
            }
            finally
            {
                if (bufferId != IntPtr.Zero)
                    resource.Native.DeregisterBuffer(bufferId);
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

        private static void WriteBigEndianLength(byte[] buffer, int value)
        {
            buffer[0] = (byte)((value >> 24) & 0xFF);
            buffer[1] = (byte)((value >> 16) & 0xFF);
            buffer[2] = (byte)((value >> 8) & 0xFF);
            buffer[3] = (byte)(value & 0xFF);
        }

        private static unsafe IntPtr RegisterPinnedArray(RioNative native, byte[] block)
        {
            fixed (byte* pointer = block)
            {
                return native.RegisterBuffer((IntPtr)pointer, block.Length);
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

                ReceiveSignal.Dispose();
                SendSignal.Dispose();
            }
        }
    }
}
