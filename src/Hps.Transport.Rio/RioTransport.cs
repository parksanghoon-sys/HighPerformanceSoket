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
        private const int FastCompletionPollYieldCount = 4096;
        private const int CompletionPollDelayMilliseconds = 1;
        private const int TcpLengthPrefixSize = 4;

        private readonly object _gate;
        private readonly List<RioConnectionListener> _listeners;
        private readonly List<TransportConnection> _connections;
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
            RioConnectionResource resource = new RioConnectionResource(native, socket);
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
                byte[] receiveBlock = resource.ReceivePool.Rent();
                IntPtr bufferId = IntPtr.Zero;

                try
                {
                    bufferId = RegisterPinnedArray(resource.Native, receiveBlock);
                    RioBufferSegment[] segments = new RioBufferSegment[]
                    {
                        new RioBufferSegment(bufferId, 0, receiveBlock.Length)
                    };

                    if (!resource.Native.Receive(resource.RequestQueue, segments, IntPtr.Zero))
                        throw new SocketException((int)SocketError.ConnectionReset);

                    RioResult completion = await WaitForCompletionAsync(
                        resource,
                        resource.ReceiveCompletionQueue,
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
                finally
                {
                    if (bufferId != IntPtr.Zero)
                        resource.Native.DeregisterBuffer(bufferId);

                    resource.ReceivePool.Return(receiveBlock);
                }
            }
        }

        private async Task SendLoopAsync(TransportConnection connection, RioConnectionResource resource)
        {
            byte[] lengthPrefixBuffer = GC.AllocateUninitializedArray<byte>(TcpLengthPrefixSize, pinned: true);

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
                            await SendInFlightAsync(resource, connection, inFlight.SendBuffer, lengthPrefixBuffer).ConfigureAwait(false);
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
            TransportSendBuffer sendBuffer,
            byte[] lengthPrefixBuffer)
        {
            if (sendBuffer.PrependLengthPrefix)
            {
                WriteBigEndianLength(lengthPrefixBuffer, sendBuffer.Length);
                await SendRegisteredArrayAsync(resource, connection, lengthPrefixBuffer, 0, TcpLengthPrefixSize).ConfigureAwait(false);
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
                        connection).ConfigureAwait(false);

                    if (completion.Status != 0 || completion.BytesTransferred == 0 || completion.BytesTransferred > remaining)
                        throw new SocketException((int)SocketError.ConnectionReset);

                    int sent = checked((int)completion.BytesTransferred);
                    currentOffset += sent;
                    remaining -= sent;
                }
            }
            finally
            {
                if (bufferId != IntPtr.Zero)
                    resource.Native.DeregisterBuffer(bufferId);
            }
        }

        private static async Task<RioResult> WaitForCompletionAsync(
            RioConnectionResource resource,
            IntPtr completionQueue,
            TransportConnection connection)
        {
            RioResult[] results = new RioResult[1];
            int emptyPollCount = 0;

            while (true)
            {
                if (connection.IsClosed || resource.IsDisposed)
                    throw new ObjectDisposedException(nameof(TransportConnection));

                uint completed = resource.DequeueCompletion(completionQueue, results);
                if (completed != 0)
                    return results[0];

                if (connection.IsClosed)
                    throw new ObjectDisposedException(nameof(TransportConnection));

                emptyPollCount++;
                if (emptyPollCount <= FastCompletionPollYieldCount)
                {
                    // RIO completion 은 보통 post 직후 짧은 시간 안에 들어온다.
                    // 빈 CQ마다 곧바로 Task.Delay(1)로 내려가면 Windows timer granularity 때문에
                    // 작은 loopback 메시지도 15ms 안팎으로 밀릴 수 있으므로, 제한된 횟수만큼은
                    // timer sleep 없이 scheduler 에 양보한 뒤 다시 dequeue 한다.
                    await Task.Yield();
                    continue;
                }

                // 장시간 idle 이거나 peer 가 아무 completion 도 만들지 않는 경우에는 busy-spin 으로
                // CPU를 점유하지 않도록 기존 1ms polling fallback 을 유지한다.
                await Task.Delay(CompletionPollDelayMilliseconds).ConfigureAwait(false);
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
            private int _disposed;

            internal RioConnectionResource(RioNative native, Socket socket)
            {
                Native = native ?? throw new ArgumentNullException(nameof(native));
                Socket = socket ?? throw new ArgumentNullException(nameof(socket));
                _completionGate = new object();
                ReceivePool = new PinnedBlockMemoryPool(ReceiveBlockSize);
                ReceiveCompletionQueue = IntPtr.Zero;
                SendCompletionQueue = IntPtr.Zero;
                RequestQueue = IntPtr.Zero;

                try
                {
                    ReceiveCompletionQueue = Native.CreateCompletionQueue(CompletionQueueSize);
                    SendCompletionQueue = Native.CreateCompletionQueue(CompletionQueueSize);
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

            internal IntPtr ReceiveCompletionQueue { get; private set; }

            internal IntPtr SendCompletionQueue { get; private set; }

            internal IntPtr RequestQueue { get; private set; }

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
            }
        }
    }
}
