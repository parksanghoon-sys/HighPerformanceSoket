using System;
using Hps.Buffers;

namespace Hps.Transport
{
    /// <summary>
    /// 연결 송신 큐에 들어가는 풀 소유 payload 참조와 전송 범위이다.
    ///
    /// 이 값은 raw <see cref="Memory{T}"/> 를 넘기지 않기 위한 경계 타입이다. Transport 구현은
    /// <see cref="Buffer"/> 의 고정 블록 출처와 참조계수 수명을 알고 있어야 RIO/io_uring 등록 버퍼,
    /// 송신 완료 콜백, 연결 종료 drain 에서 같은 소유권 규칙을 적용할 수 있다.
    ///
    /// 소유권: 이 값 자체는 참조계수를 늘리거나 줄이지 않는다. 호출자는 Transport 로 넘기기 전에
    /// 구독자 몫 <see cref="RefCountedBuffer.AddRef"/> 를 끝내야 한다. Transport 가 send 를 수락하면
    /// 그 참조 1개를 소유하고, 송신 완료·drop·close 중 정확히 한 곳에서 <see cref="RefCountedBuffer.Release"/> 해야 한다.
    /// send 가 거부되면 Transport 는 소유권을 갖지 않으므로 호출자가 즉시 Release 해야 한다.
    /// </summary>
    public readonly struct TransportSendBuffer
    {
        private readonly RefCountedBuffer? _buffer;
        private readonly bool _prependLengthPrefix;

        /// <summary>
        /// 송신할 payload 를 담은 참조계수 버퍼이다. default 값처럼 버퍼가 없는 요청은 계약 위반이다.
        /// </summary>
        public RefCountedBuffer Buffer
        {
            get
            {
                if (_buffer == null)
                    throw new InvalidOperationException("TransportSendBuffer 에 버퍼가 설정되지 않았다.");

                return _buffer;
            }
        }

        /// <summary>
        /// <see cref="RefCountedBuffer.Length"/> 기준 payload 범위 안에서 전송을 시작할 오프셋이다.
        /// </summary>
        public int Offset { get; }

        /// <summary>
        /// <see cref="Offset"/> 부터 전송할 바이트 수이다. 0 길이 송신은 프레임/프로토콜 계층에서
        /// 유효 payload 로 사용할 수 있으므로 허용한다.
        /// </summary>
        public int Length { get; }

        /// <summary>
        /// TCP stream message frame 으로 보낼 때 payload 앞에 4바이트 big-endian length prefix 를 붙일지 여부다.
        ///
        /// 이 flag 는 payload 를 새 버퍼로 합치지 않고 header metadata 와 payload slice 를 하나의 logical send item 으로
        /// 유지하기 위한 값이다. Transport 의 소유권은 여전히 <see cref="Buffer"/> ref 1개에만 걸려 있다.
        /// </summary>
        public bool PrependLengthPrefix => _prependLengthPrefix;

        /// <summary>
        /// 참조계수 버퍼와 유효 payload 내부 전송 범위를 만든다.
        /// </summary>
        public TransportSendBuffer(RefCountedBuffer buffer, int offset, int length)
            : this(buffer, offset, length, prependLengthPrefix: false)
        {
        }

        private TransportSendBuffer(RefCountedBuffer buffer, int offset, int length, bool prependLengthPrefix)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            // Length 0 요청은 payload 범위만 보면 유효할 수 있다. 그래도 이미 풀로 돌아간 버퍼는
            // 송신 큐에 들어가면 안 되므로 Memory 접근으로 live block 여부를 먼저 확인한다.
            _ = buffer.Memory;

            int payloadLength = buffer.Length;
            if (offset < 0 || offset > payloadLength)
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset 은 payload 길이 안에 있어야 한다.");
            if (length < 0 || length > payloadLength - offset)
                throw new ArgumentOutOfRangeException(nameof(length), "Length 는 Offset 이후 payload 범위 안에 있어야 한다.");

            _buffer = buffer;
            Offset = offset;
            Length = length;
            _prependLengthPrefix = prependLengthPrefix;
        }

        /// <summary>
        /// 같은 payload slice 를 TCP length-prefixed message frame 으로 보내는 logical send item 을 만든다.
        ///
        /// 새 payload 버퍼를 만들지 않고 값 타입 metadata 만 바꾸므로, 호출자는 기존 fan-out refcount 규칙을 그대로 유지한다.
        /// </summary>
        public TransportSendBuffer WithLengthPrefix()
        {
            return new TransportSendBuffer(Buffer, Offset, Length, prependLengthPrefix: true);
        }
    }
}
