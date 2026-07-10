using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Hps.Buffers;
using Hps.Transport;

namespace Hps.Protocol
{
    /// <summary>
    /// Transport TCP receive 콜백을 length-prefix frame 조립기로 연결하는 어댑터이다.
    ///
    /// 이 타입은 connection 별 <see cref="TcpFrameAssembler"/> 를 소유한다. Transport 가 전달하는
    /// <see cref="TransportReceiveBuffer"/> 는 콜백 동안만 유효하므로, 완성된 frame payload 는 assembler 가
    /// <see cref="RefCountedBuffer"/> 로 복사해 <see cref="ITcpFrameHandler"/> 로 소유권을 넘긴다.
    /// </summary>
    public sealed class TcpFrameReceiveHandler : ITransportReceiveHandler
    {
        private readonly object _gate;
        private readonly IRefCountedBufferSource _source;
        private readonly int _maxPayloadLength;
        private readonly ITcpFrameHandler _frameHandler;
        private readonly Dictionary<IConnection, TcpFrameAssembler> _assemblers;
        private readonly ConditionalWeakTable<IConnection, ClosedConnectionMarker> _closedConnections;

        /// <summary>
        /// frame payload 를 저장할 풀, 최대 payload 길이, 완성 frame 수신자를 지정한다.
        ///
        /// <paramref name="maxPayloadLength"/> 는 <paramref name="pool"/> 의 블록 크기 이하여야 한다.
        /// 이 제한이 있어야 frame 하나가 `RefCountedBuffer` 하나에 들어가고, D009/D010의 소유권 단위가 유지된다.
        /// </summary>
        public TcpFrameReceiveHandler(PinnedBlockMemoryPool pool, int maxPayloadLength, ITcpFrameHandler frameHandler)
            : this((IRefCountedBufferSource)pool, maxPayloadLength, frameHandler)
        {
        }

        /// <summary>
        /// frame payload source, 최대 payload 길이, 완성 frame 수신자를 지정한다.
        ///
        /// source 계약을 사용하면 기본 pinned pool 뿐 아니라 transport backend 가 제공하는 registered payload source 도
        /// 같은 receive handler 경로에서 사용할 수 있다.
        /// </summary>
        public TcpFrameReceiveHandler(IRefCountedBufferSource source, int maxPayloadLength, ITcpFrameHandler frameHandler)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (frameHandler == null)
                throw new ArgumentNullException(nameof(frameHandler));
            if (maxPayloadLength < 0)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadLength));
            if (maxPayloadLength > source.BlockSize)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadLength), "최대 payload 길이는 source 블록 크기를 넘을 수 없다.");

            _gate = new object();
            _source = source;
            _maxPayloadLength = maxPayloadLength;
            _frameHandler = frameHandler;
            _assemblers = new Dictionary<IConnection, TcpFrameAssembler>();
            _closedConnections = new ConditionalWeakTable<IConnection, ClosedConnectionMarker>();
        }

        /// <summary>
        /// Transport 가 전달한 raw TCP byte stream 조각을 현재 connection 의 assembler 로 소비한다.
        /// </summary>
        public void OnReceived(IConnection connection, TransportReceiveBuffer receiveBuffer)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            TcpFrameAssembler assembler = GetOrCreateAssembler(connection);
            ReadOnlySpan<byte> source = receiveBuffer.Span;
            int offset = 0;

            while (offset < source.Length)
            {
                RefCountedBuffer? frame;
                int consumed;
                TcpFrameReadStatus status = assembler.TryReadFrame(source.Slice(offset), out consumed, out frame);
                offset += consumed;

                // 아직 frame 이 완성되지 않았으면 이번 chunk 는 모두 소비된 상태다.
                // 남은 상태는 connection 별 assembler 안에 있으므로 다음 OnReceived 에서 이어 간다.
                if (status == TcpFrameReadStatus.NeedMoreData)
                    return;

                if (status == TcpFrameReadStatus.PayloadTooLarge)
                {
                    CloseConnectionAfterPayloadTooLarge(connection);
                    return;
                }

                if (frame == null)
                    throw new InvalidOperationException("FrameReady 상태에서 frame 이 반환되지 않았다.");

                if (!DispatchFrame(connection, frame))
                    return;
            }
        }

        /// <summary>
        /// Transport 가 connection 종료를 관측했을 때 partial frame payload 를 반환하고 상위 handler 에 알린다.
        /// </summary>
        public void OnConnectionClosed(IConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            DisposeAssembler(connection);
            NotifyConnectionClosedOnce(connection);
        }

        private TcpFrameAssembler GetOrCreateAssembler(IConnection connection)
        {
            // 서로 다른 connection 의 receive loop 가 동시에 들어올 수 있으므로 dictionary 접근만 lock 으로 보호한다.
            // 개별 assembler 는 connection 당 단일 receive loop 가 사용한다는 Transport 경계에 기대어 lock 없이 처리한다.
            lock (_gate)
            {
                TcpFrameAssembler? assembler;
                if (_assemblers.TryGetValue(connection, out assembler))
                    return assembler;

                assembler = new TcpFrameAssembler(_source, _maxPayloadLength);
                _assemblers.Add(connection, assembler);
                return assembler;
            }
        }

        private void CloseConnectionAfterPayloadTooLarge(IConnection connection)
        {
            // TcpFrameAssembler 는 length 초과를 상태로만 보고한다. 이후 stream byte 는 payload 잔여분일 수 있어
            // 새 header 로 복구하면 오해석된다. 따라서 D010 계약대로 해당 connection 을 닫고 assembler 를 버린다.
            DisposeAssembler(connection);
            connection.Close();
            NotifyConnectionClosedOnce(connection);
        }

        private bool DispatchFrame(IConnection connection, RefCountedBuffer frame)
        {
            try
            {
                // OnFrame 이 정상 반환한 뒤부터 frame Release 책임은 ITcpFrameHandler 구현체로 넘어간다.
                // 예외가 발생하면 상위 handler 가 소유권을 완전히 받아 처리했다고 볼 수 없으므로 아래 unwind 가 회수한다.
                _frameHandler.OnFrame(connection, frame);
                return true;
            }
            catch
            {
                // handler 실패는 해당 connection 의 protocol 처리 실패로 본다. 완성 frame 을 회수하지 않으면
                // recv loop 가 멈추는 순간 RefCountedBuffer 가 in-flight 로 남아 D011 누수 0 계약을 깬다.
                frame.Release();
                CloseConnectionAfterFrameHandlerFailure(connection);
                return false;
            }
        }

        private void CloseConnectionAfterFrameHandlerFailure(IConnection connection)
        {
            // handler 예외 이후 남은 stream byte 를 계속 해석하면 같은 connection 의 protocol 상태가 불명확해진다.
            // connection 을 닫고 assembler 를 제거해 이후 Transport close 통지가 다시 오더라도 한 번만 상위에 알린다.
            DisposeAssembler(connection);
            connection.Close();
            NotifyConnectionClosedOnce(connection);
        }

        private void DisposeAssembler(IConnection connection)
        {
            TcpFrameAssembler? assembler = null;

            // remove 와 Dispose 를 분리한다. Dictionary lock 안에서 Release 까지 수행하면 frame handler 나 pool 쪽
            // 후속 변경이 lock 범위를 넓힐 수 있으므로, 소유권 제거만 lock 안에서 끝낸다.
            lock (_gate)
            {
                if (_assemblers.TryGetValue(connection, out assembler))
                    _assemblers.Remove(connection);
            }

            assembler?.Dispose();
        }

        private void NotifyConnectionClosedOnce(IConnection connection)
        {
            if (!TryMarkConnectionClosed(connection))
                return;

            _frameHandler.OnConnectionClosed(connection);
        }

        private bool TryMarkConnectionClosed(IConnection connection)
        {
            // PayloadTooLarge 나 handler 실패에서는 어댑터가 먼저 Close 를 호출하고, 이후 Transport 가 close 를 다시
            // 통지할 수 있다. connection 객체를 강하게 보관하면 단명 connection churn 에서 누수가 되므로 weak table 에
            // "이미 상위에 종료를 알림" 표식만 둔다.
            lock (_gate)
            {
                ClosedConnectionMarker? marker;
                if (_closedConnections.TryGetValue(connection, out marker))
                    return false;

                _closedConnections.Add(connection, ClosedConnectionMarker.Instance);
                return true;
            }
        }

        private sealed class ClosedConnectionMarker
        {
            internal static readonly ClosedConnectionMarker Instance = new ClosedConnectionMarker();

            private ClosedConnectionMarker()
            {
            }
        }
    }
}
