using System;
using System.Collections.Generic;
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
        private readonly PinnedBlockMemoryPool _pool;
        private readonly int _maxPayloadLength;
        private readonly ITcpFrameHandler _frameHandler;
        private readonly Dictionary<IConnection, TcpFrameAssembler> _assemblers;

        /// <summary>
        /// frame payload 를 저장할 풀, 최대 payload 길이, 완성 frame 수신자를 지정한다.
        ///
        /// <paramref name="maxPayloadLength"/> 는 <paramref name="pool"/> 의 블록 크기 이하여야 한다.
        /// 이 제한이 있어야 frame 하나가 `RefCountedBuffer` 하나에 들어가고, D009/D010의 소유권 단위가 유지된다.
        /// </summary>
        public TcpFrameReceiveHandler(PinnedBlockMemoryPool pool, int maxPayloadLength, ITcpFrameHandler frameHandler)
        {
            if (pool == null)
                throw new ArgumentNullException(nameof(pool));
            if (frameHandler == null)
                throw new ArgumentNullException(nameof(frameHandler));
            if (maxPayloadLength < 0)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadLength));
            if (maxPayloadLength > pool.BlockSize)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadLength), "최대 payload 길이는 풀 블록 크기를 넘을 수 없다.");

            _gate = new object();
            _pool = pool;
            _maxPayloadLength = maxPayloadLength;
            _frameHandler = frameHandler;
            _assemblers = new Dictionary<IConnection, TcpFrameAssembler>();
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

                // OnFrame 호출 시점부터 frame Release 책임은 ITcpFrameHandler 구현체로 넘어간다.
                // 이 어댑터가 여기서 Release 하면 fan-out 이 사용할 payload 를 조기 반환하게 된다.
                _frameHandler.OnFrame(connection, frame);
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
            _frameHandler.OnConnectionClosed(connection);
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

                assembler = new TcpFrameAssembler(_pool, _maxPayloadLength);
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
            _frameHandler.OnConnectionClosed(connection);
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
    }
}
