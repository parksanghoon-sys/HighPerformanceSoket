using System;

namespace Hps.Transport
{
    /// <summary>
    /// Transport 수신 펌프가 동기 콜백 동안만 빌려주는 수신 데이터 view 이다.
    ///
    /// 이 타입은 <c>ref struct</c> 이므로 힙에 저장하거나 async 경계를 넘길 수 없다. 그 제약이 곧
    /// 소유권 계약이다. Protocol 계층이 데이터를 콜백 이후에도 보관해야 하면 D010/D009에 따라
    /// 필요한 범위만 `RefCountedBuffer`로 복사해야 한다.
    /// </summary>
    public readonly ref struct TransportReceiveBuffer
    {
        private readonly ReadOnlySpan<byte> _span;

        public TransportReceiveBuffer(ReadOnlySpan<byte> span)
        {
            _span = span;
        }

        /// <summary>
        /// 이번 콜백 동안만 유효한 수신 데이터 범위이다. Transport 는 이 span 이 가리키는 recv ring 또는
        /// pinned receive block 을 콜백 반환 뒤 재사용할 수 있다.
        /// </summary>
        public ReadOnlySpan<byte> Span => _span;

        /// <summary>
        /// 수신 데이터의 byte 길이이다.
        /// </summary>
        public int Length => _span.Length;
    }
}
