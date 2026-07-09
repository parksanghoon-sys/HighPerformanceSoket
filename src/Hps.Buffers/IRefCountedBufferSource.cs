namespace Hps.Buffers
{
    /// <summary>
    /// TCP frame assembler 같은 상위 조립기가 counted payload buffer 를 대여하는 source 계약이다.
    /// 구현체는 일반 pool, registered pool, 또는 explicit fallback composite source 일 수 있다.
    /// </summary>
    public interface IRefCountedBufferSource
    {
        /// <summary>
        /// 이 source 가 대여하는 모든 block 의 고정 바이트 길이다.
        /// </summary>
        int BlockSize { get; }

        /// <summary>
        /// fan-out payload 공유에 사용할 참조계수 버퍼를 대여한다.
        /// </summary>
        RefCountedBuffer RentCounted();
    }
}
