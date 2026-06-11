using System;
using System.Buffers.Binary;
using Hps.Buffers;

namespace Hps.Protocol
{
    /// <summary>
    /// TCP byte stream 에서 4바이트 big-endian 길이 프리픽스 frame 을 조립하는 per-connection 상태 객체이다.
    /// </summary>
    public sealed class TcpFrameAssembler : IDisposable
    {
        private const int HeaderLength = 4;

        private readonly PinnedBlockMemoryPool _pool;
        private readonly int _maxPayloadLength;
        private readonly byte[] _header;
        private int _headerBytesRead;
        private int _expectedPayloadLength;
        private int _payloadBytesRead;
        private RefCountedBuffer? _payload;
        private bool _disposed;

        /// <summary>
        /// frame payload 를 저장할 풀과 최대 payload 길이를 지정한다.
        /// </summary>
        public TcpFrameAssembler(PinnedBlockMemoryPool pool, int maxPayloadLength)
        {
            if (pool == null)
                throw new ArgumentNullException(nameof(pool));
            if (maxPayloadLength < 0)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadLength));
            if (maxPayloadLength > pool.BlockSize)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadLength), "최대 payload 길이는 풀 블록 크기를 넘을 수 없다.");

            _pool = pool;
            _maxPayloadLength = maxPayloadLength;
            _header = new byte[HeaderLength];
            _expectedPayloadLength = -1;
        }

        /// <summary>
        /// 입력 chunk 를 현재 연결의 frame 조립 상태에 반영한다.
        /// </summary>
        public TcpFrameReadStatus TryReadFrame(ReadOnlySpan<byte> source, out int consumed, out RefCountedBuffer? frame)
        {
            frame = null;
            consumed = 0;

            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpFrameAssembler));

            while (consumed < source.Length)
            {
                if (_expectedPayloadLength < 0)
                {
                    TcpFrameReadStatus headerStatus = ReadHeader(source, ref consumed);
                    if (headerStatus != TcpFrameReadStatus.NeedMoreData)
                        return headerStatus;

                    if (_expectedPayloadLength < 0)
                        return TcpFrameReadStatus.NeedMoreData;
                }

                if (_expectedPayloadLength == 0)
                {
                    frame = CompleteFrame();
                    return TcpFrameReadStatus.FrameReady;
                }

                ReadPayload(source, ref consumed);

                if (_payloadBytesRead == _expectedPayloadLength)
                {
                    frame = CompleteFrame();
                    return TcpFrameReadStatus.FrameReady;
                }
            }

            return TcpFrameReadStatus.NeedMoreData;
        }

        /// <summary>
        /// 조립 중인 payload buffer 가 있다면 반환한다.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            ReleasePartialPayload();
        }

        private TcpFrameReadStatus ReadHeader(ReadOnlySpan<byte> source, ref int consumed)
        {
            int headerRemaining = HeaderLength - _headerBytesRead;
            int available = source.Length - consumed;
            int toCopy = Math.Min(headerRemaining, available);

            source.Slice(consumed, toCopy).CopyTo(new Span<byte>(_header, _headerBytesRead, toCopy));
            _headerBytesRead += toCopy;
            consumed += toCopy;

            if (_headerBytesRead != HeaderLength)
                return TcpFrameReadStatus.NeedMoreData;

            int payloadLength = BinaryPrimitives.ReadInt32BigEndian(_header);
            if (payloadLength < 0 || payloadLength > _maxPayloadLength)
            {
                ResetHeader();
                return TcpFrameReadStatus.PayloadTooLarge;
            }

            _expectedPayloadLength = payloadLength;
            _payloadBytesRead = 0;
            _payload = _pool.RentCounted();
            return TcpFrameReadStatus.NeedMoreData;
        }

        private void ReadPayload(ReadOnlySpan<byte> source, ref int consumed)
        {
            if (_payload == null)
                throw new InvalidOperationException("payload buffer 가 준비되지 않았다.");

            int payloadRemaining = _expectedPayloadLength - _payloadBytesRead;
            int available = source.Length - consumed;
            int toCopy = Math.Min(payloadRemaining, available);

            // TCP receive chunk 는 콜백 이후 재사용되므로 payload 는 여기서 소유권 있는 RefCountedBuffer 로 복사한다(D009/D010).
            source.Slice(consumed, toCopy).CopyTo(_payload.Span.Slice(_payloadBytesRead, toCopy));
            _payloadBytesRead += toCopy;
            consumed += toCopy;
        }

        private RefCountedBuffer CompleteFrame()
        {
            if (_payload == null)
                throw new InvalidOperationException("완성할 payload buffer 가 없다.");

            RefCountedBuffer completed = _payload;
            completed.SetLength(_expectedPayloadLength);

            _payload = null;
            _payloadBytesRead = 0;
            _expectedPayloadLength = -1;
            ResetHeader();

            return completed;
        }

        private void ResetHeader()
        {
            _headerBytesRead = 0;
        }

        private void ReleasePartialPayload()
        {
            if (_payload == null)
                return;

            _payload.Release();
            _payload = null;
            _payloadBytesRead = 0;
            _expectedPayloadLength = -1;
            ResetHeader();
        }
    }
}
