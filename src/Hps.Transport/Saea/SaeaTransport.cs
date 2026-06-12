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
    /// 크로스플랫폼 기준선 Transport 구현이다.
    ///
    /// 이름은 SAEA 기준선을 나타내지만, 현재 구현은 TCP listen/connect/accept 와 최소 send/receive chunk 전달만 다룬다.
    /// 명시적인 SocketAsyncEventArgs 기반 최적화와 프레이밍은 후속 단위에서 붙인다.
    /// </summary>
    public sealed class SaeaTransport : TransportBase
    {
        private const int ListenBacklog = 512;
        private const int ReceiveBlockSize = 8192;

        private readonly object _gate;
        private readonly List<SaeaConnectionListener> _listeners;
        private readonly List<TransportConnection> _connections;
        private readonly List<SaeaUdpEndpoint> _udpEndpoints;
        private readonly PinnedBlockMemoryPool _receivePool;
        private bool _started;
        private bool _stopped;

        public SaeaTransport()
        {
            _gate = new object();
            _listeners = new List<SaeaConnectionListener>();
            _connections = new List<TransportConnection>();
            _udpEndpoints = new List<SaeaUdpEndpoint>();
            _receivePool = new PinnedBlockMemoryPool(ReceiveBlockSize);
        }

        /// <inheritdoc />
        public override ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (_stopped)
                    throw new InvalidOperationException("이미 중지된 Transport 는 다시 시작할 수 없다.");

                _started = true;
            }

            return default(ValueTask);
        }

        /// <inheritdoc />
        public override ValueTask<IConnectionListener> ListenTcpAsync(EndPoint localEndPoint, CancellationToken cancellationToken = default)
        {
            if (localEndPoint == null)
                throw new ArgumentNullException(nameof(localEndPoint));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureRunning();

            Socket listenSocket = CreateTcpSocket(localEndPoint);
            SaeaConnectionListener? listener = null;

            try
            {
                // 포트 0을 허용해야 테스트와 샘플이 OS가 고른 임시 포트를 안전하게 사용할 수 있다.
                // 실제 connect 대상은 요청 endpoint 가 아니라 listener.LocalEndPoint 로 다시 읽는다.
                listenSocket.Bind(localEndPoint);
                listenSocket.Listen(ListenBacklog);

                listener = new SaeaConnectionListener(this, listenSocket);
                RegisterListener(listener);

                return new ValueTask<IConnectionListener>(listener);
            }
            catch
            {
                listener?.Close();
                listenSocket.Dispose();
                throw;
            }
        }

        /// <inheritdoc />
        public override async ValueTask<IConnection> ConnectTcpAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
        {
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));

            cancellationToken.ThrowIfCancellationRequested();
            EnsureRunning();

            Socket? socket = CreateTcpSocket(remoteEndPoint);

            try
            {
                ConfigureTcpConnectionSocket(socket);
                await socket.ConnectAsync(remoteEndPoint, cancellationToken).ConfigureAwait(false);

                TransportConnection connection = CreateSocketConnection(socket);
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

            Socket socket = CreateUdpSocket(localEndPoint);
            SaeaUdpEndpoint? udpEndpoint = null;

            try
            {
                socket.Bind(localEndPoint);
                udpEndpoint = new SaeaUdpEndpoint(this, socket);
                RegisterUdpEndpoint(udpEndpoint);
                StartUdpReceiveLoop(udpEndpoint);
                StartUdpSendLoop(udpEndpoint);
                socket = null!;

                return new ValueTask<IUdpEndpoint>(udpEndpoint);
            }
            finally
            {
                socket?.Dispose();
            }
        }

        /// <inheritdoc />
        public override bool TrySendTo(IUdpEndpoint endpoint, EndPoint remoteEndPoint, TransportSendBuffer sendBuffer)
        {
            if (endpoint == null)
                throw new ArgumentNullException(nameof(endpoint));
            if (remoteEndPoint == null)
                throw new ArgumentNullException(nameof(remoteEndPoint));

            SaeaUdpEndpoint? udpEndpoint = endpoint as SaeaUdpEndpoint;
            if (udpEndpoint == null)
                throw new ArgumentException("이 Transport 구현이 생성한 UDP endpoint 만 사용할 수 있다.", nameof(endpoint));

            // TCP TrySend 와 같은 소유권 경계다. Transport 가 true 를 반환하기 전에 live buffer 여부를 확인해
            // default(TransportSendBuffer) 나 이미 반환된 버퍼가 background send task 로 넘어가지 않게 한다.
            RefCountedBuffer buffer = sendBuffer.Buffer;
            _ = buffer.Memory;

            if (udpEndpoint.IsClosed)
                return false;

            return udpEndpoint.TryAcceptSend(remoteEndPoint, sendBuffer);
        }

        /// <inheritdoc />
        public override ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCore();
            return default(ValueTask);
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

            ConfigureTcpConnectionSocket(socket);
            return CreateSocketConnection(socket);
        }

        internal void UnregisterListener(SaeaConnectionListener listener)
        {
            lock (_gate)
            {
                _listeners.Remove(listener);
            }
        }

        internal void UnregisterUdpEndpoint(SaeaUdpEndpoint udpEndpoint)
        {
            lock (_gate)
            {
                _udpEndpoints.Remove(udpEndpoint);
            }
        }

        private TransportConnection CreateSocketConnection(Socket socket)
        {
            TransportConnection connection = new TransportConnection(socket, UnregisterConnection, RecordTcpPendingSendDrop);

            try
            {
                RegisterConnection(connection);
                StartReceiveLoop(connection, socket);
                StartSendLoop(connection, socket);
                return connection;
            }
            catch
            {
                connection.Close();
                throw;
            }
        }

        private void StartReceiveLoop(TransportConnection connection, Socket socket)
        {
            // 이번 단위의 SAEA 기준선은 실제 SocketAsyncEventArgs pump 가 아니라 raw Socket receive loop 이다.
            // 다만 I/O buffer 는 규칙대로 pinned pool 에서 대여해 이후 SAEA/RIO/io_uring 등록 버퍼 경계와 충돌하지 않게 한다.
            _ = Task.Run(delegate()
            {
                return ReceiveLoopAsync(connection, socket);
            });
        }

        private void StartUdpReceiveLoop(SaeaUdpEndpoint udpEndpoint)
        {
            // UDP 는 1 datagram = 1 message 이므로 TCP stream 조립용 borrowed receive block 과 다르게
            // RefCountedBuffer 를 직접 대여해 handler 로 소유권을 넘긴다(D009의 UDP 직접 recv 기준선).
            _ = Task.Run(delegate()
            {
                return UdpReceiveLoopAsync(udpEndpoint);
            });
        }

        private void StartUdpSendLoop(SaeaUdpEndpoint udpEndpoint)
        {
            // UDP 는 연결이 없지만 endpoint 단위로 단일 송신 pump 를 둔다.
            // TrySendTo 호출마다 독립 Task 를 만들면 고빈도 publish 에서 thread-pool 이 송신 큐 역할을 하게 되므로,
            // pending queue -> 단일 pump 경계로 직렬화해 TCP 송신 경로와 같은 소유권 반환 규율을 유지한다.
            _ = Task.Run(delegate()
            {
                return UdpSendLoopAsync(udpEndpoint);
            });
        }

        private async Task UdpSendLoopAsync(SaeaUdpEndpoint udpEndpoint)
        {
            while (true)
            {
                await udpEndpoint.WaitForSendSignalAsync().ConfigureAwait(false);

                while (udpEndpoint.TryBeginSend(out SaeaUdpEndpoint.UdpSendRequest sendRequest))
                {
                    await SendUdpDatagramAsync(udpEndpoint, sendRequest.RemoteEndPoint, sendRequest.SendBuffer).ConfigureAwait(false);
                }

                if (udpEndpoint.IsClosed)
                    return;
            }
        }

        private async Task UdpReceiveLoopAsync(SaeaUdpEndpoint udpEndpoint)
        {
            while (true)
            {
                RefCountedBuffer? datagram = _receivePool.RentCounted();

                try
                {
                    ArraySegment<byte> receiveSegment = GetRefCountedBlockSegment(datagram, 0, _receivePool.BlockSize);
                    SocketReceiveFromResult result = await udpEndpoint.Socket.ReceiveFromAsync(
                        receiveSegment,
                        SocketFlags.None,
                        udpEndpoint.CreateReceiveRemoteEndPoint()).ConfigureAwait(false);

                    datagram.SetLength(result.ReceivedBytes);

                    // handler 호출 시점부터 datagram 의 Release 책임은 handler 계약으로 넘어간다.
                    // 호출 뒤에 null 로 끊으면 handler 예외 경로에서 loop catch 가 같은 ref 를 다시 Release 할 수 있다.
                    RefCountedBuffer ownedDatagram = datagram;
                    datagram = null;
                    DispatchDatagramReceived(udpEndpoint, result.RemoteEndPoint, ownedDatagram);
                }
                catch (ObjectDisposedException)
                {
                    datagram?.Release();
                    return;
                }
                catch (SocketException)
                {
                    datagram?.Release();
                    NotifyUdpEndpointClosed(udpEndpoint);
                    return;
                }
                catch
                {
                    datagram?.Release();

                    // handler 로 소유권을 넘긴 뒤 예외가 발생해도 background receive loop 를 fault 상태로 방치하지 않는다.
                    // 현재 public surface 에는 fault 관측 API 가 없으므로 endpoint close 알림으로 수명 상태를 명확히 만들고 loop 를 종료한다.
                    NotifyUdpEndpointClosed(udpEndpoint);
                    return;
                }
            }
        }

        private void StartSendLoop(TransportConnection connection, Socket socket)
        {
            // 송신도 현재는 SocketAsyncEventArgs completion pump 가 아니라 baseline raw Socket loop 이다.
            // 다만 pending -> in-flight handle -> completion Release 경계는 이후 SAEA/RIO/io_uring 구현이 그대로 재사용한다.
            _ = Task.Run(delegate()
            {
                return SendLoopAsync(connection, socket);
            });
        }

        private async Task SendLoopAsync(TransportConnection connection, Socket socket)
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
                            await SendInFlightAsync(socket, inFlight.SendBuffer).ConfigureAwait(false);
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

        private static async Task SendInFlightAsync(Socket socket, TransportSendBuffer sendBuffer)
        {
            int offset = sendBuffer.Offset;
            int remaining = sendBuffer.Length;

            while (remaining != 0)
            {
                ArraySegment<byte> segment = GetSocketSendSegment(sendBuffer, offset, remaining);
                int sent = await socket.SendAsync(segment, SocketFlags.None).ConfigureAwait(false);
                if (sent == 0)
                    throw new SocketException((int)SocketError.ConnectionReset);

                offset += sent;
                remaining -= sent;
            }
        }

        private static async Task SendUdpDatagramAsync(SaeaUdpEndpoint udpEndpoint, EndPoint remoteEndPoint, TransportSendBuffer sendBuffer)
        {
            RefCountedBuffer buffer = sendBuffer.Buffer;

            try
            {
                ArraySegment<byte> segment = GetRefCountedBlockSegment(buffer, sendBuffer.Offset, sendBuffer.Length);
                int sent = await udpEndpoint.Socket.SendToAsync(segment, SocketFlags.None, remoteEndPoint).ConfigureAwait(false);
                if (sent != sendBuffer.Length)
                    throw new SocketException((int)SocketError.MessageSize);
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

        private static ArraySegment<byte> GetSocketSendSegment(TransportSendBuffer sendBuffer, int offset, int length)
        {
            return GetRefCountedBlockSegment(sendBuffer.Buffer, offset, length);
        }

        private static ArraySegment<byte> GetRefCountedBlockSegment(RefCountedBuffer buffer, int offset, int length)
        {
            Memory<byte> memory = buffer.Memory.Slice(offset, length);
            ArraySegment<byte> segment;

            if (!MemoryMarshal.TryGetArray(memory, out segment))
                throw new InvalidOperationException("SAEA 기준선은 pinned byte[] 기반 RefCountedBuffer 만 지원한다.");

            return segment;
        }

        private async Task ReceiveLoopAsync(TransportConnection connection, Socket socket)
        {
            byte[] receiveBlock = _receivePool.Rent();

            try
            {
                ArraySegment<byte> receiveSegment = new ArraySegment<byte>(receiveBlock);

                while (true)
                {
                    int received;

                    try
                    {
                        received = await socket.ReceiveAsync(receiveSegment, SocketFlags.None).ConfigureAwait(false);
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

                    if (received == 0)
                    {
                        NotifyConnectionClosed(connection);
                        return;
                    }

                    DispatchReceived(connection, receiveBlock, received);
                }
            }
            finally
            {
                _receivePool.Return(receiveBlock);
            }
        }

        private void DispatchReceived(TransportConnection connection, byte[] receiveBlock, int received)
        {
            ITransportReceiveHandler? receiveHandler = ReadReceiveHandlerSnapshot();
            if (receiveHandler == null)
                return;

            // TransportReceiveBuffer 는 ref struct 이므로 async 메서드 안에서 보관하지 않는다.
            // 이 동기 dispatch 범위 안에서만 span view 를 만들고 handler 반환 즉시 receive block 은 다시 재사용 가능해진다.
            receiveHandler.OnReceived(connection, new TransportReceiveBuffer(new ReadOnlySpan<byte>(receiveBlock, 0, received)));
        }

        private void NotifyConnectionClosed(TransportConnection connection)
        {
            ITransportReceiveHandler? receiveHandler = ReadReceiveHandlerSnapshot();
            if (receiveHandler != null)
                receiveHandler.OnConnectionClosed(connection);

            connection.Close();
        }

        private void DispatchDatagramReceived(SaeaUdpEndpoint udpEndpoint, EndPoint remoteEndPoint, RefCountedBuffer datagram)
        {
            ITransportDatagramHandler? datagramHandler = ReadDatagramHandlerSnapshot();
            if (datagramHandler == null)
            {
                datagram.Release();
                return;
            }

            datagramHandler.OnDatagramReceived(udpEndpoint, remoteEndPoint, datagram);
        }

        private void NotifyUdpEndpointClosed(SaeaUdpEndpoint udpEndpoint)
        {
            ITransportDatagramHandler? datagramHandler = ReadDatagramHandlerSnapshot();
            if (datagramHandler != null)
                datagramHandler.OnDatagramEndpointClosed(udpEndpoint);

            udpEndpoint.Close();
        }

        private void RegisterListener(SaeaConnectionListener listener)
        {
            lock (_gate)
            {
                EnsureRunningLocked();
                _listeners.Add(listener);
            }
        }

        private void RegisterUdpEndpoint(SaeaUdpEndpoint udpEndpoint)
        {
            lock (_gate)
            {
                EnsureRunningLocked();
                _udpEndpoints.Add(udpEndpoint);
            }
        }

        private void RegisterConnection(TransportConnection connection)
        {
            lock (_gate)
            {
                EnsureRunningLocked();
                _connections.Add(connection);
            }
        }

        private void UnregisterConnection(TransportConnection connection)
        {
            lock (_gate)
            {
                // StopCore 가 이미 snapshot 을 뜨고 목록을 비운 뒤 close 하는 경로에서도 이 호출은 안전하다.
                // 개별 Close 경로에서는 listener 의 unregister 와 동일하게 transport 수명 추적 참조를 즉시 제거한다.
                _connections.Remove(connection);
            }
        }

        private void StopCore()
        {
            SaeaConnectionListener[] listeners;
            TransportConnection[] connections;
            SaeaUdpEndpoint[] udpEndpoints;

            lock (_gate)
            {
                if (_stopped)
                    return;

                _stopped = true;
                listeners = _listeners.ToArray();
                connections = _connections.ToArray();
                udpEndpoints = _udpEndpoints.ToArray();
                _listeners.Clear();
                _connections.Clear();
                _udpEndpoints.Clear();
            }

            // Close/Dispose 는 각 객체 내부에서 idempotent 하게 처리한다.
            // Transport lock 밖에서 닫아야 listener.Close() 의 unregister 와 socket dispose 가 재진입해도 교착되지 않는다.
            for (int index = 0; index < listeners.Length; index++)
            {
                listeners[index].Close();
            }

            for (int index = 0; index < connections.Length; index++)
            {
                connections[index].Close();
            }

            for (int index = 0; index < udpEndpoints.Length; index++)
            {
                udpEndpoints[index].Close();
            }
        }

        private void EnsureRunning()
        {
            lock (_gate)
            {
                EnsureRunningLocked();
            }
        }

        private void EnsureRunningLocked()
        {
            if (!_started)
                throw new InvalidOperationException("Transport 를 시작한 뒤에 작업을 수행해야 한다.");

            if (_stopped)
                throw new ObjectDisposedException(nameof(SaeaTransport));
        }

        private static Socket CreateTcpSocket(EndPoint endPoint)
        {
            AddressFamily addressFamily = endPoint.AddressFamily;
            if (addressFamily == AddressFamily.Unspecified || addressFamily == AddressFamily.Unknown)
                throw new NotSupportedException("TCP endpoint 의 AddressFamily 를 확인할 수 없다.");

            return new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
        }

        private static Socket CreateUdpSocket(EndPoint endPoint)
        {
            AddressFamily addressFamily = endPoint.AddressFamily;
            if (addressFamily == AddressFamily.Unspecified || addressFamily == AddressFamily.Unknown)
                throw new NotSupportedException("UDP endpoint 의 AddressFamily 를 확인할 수 없다.");

            return new Socket(addressFamily, SocketType.Dgram, ProtocolType.Udp);
        }

        private static void ConfigureTcpConnectionSocket(Socket socket)
        {
            // 브로커의 작은 메시지 지연을 줄이기 위한 기본값이다. 실제 튜닝은 Phase 7에서 다시 측정한다.
            socket.NoDelay = true;
        }
    }
}
