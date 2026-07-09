namespace Hps.Buffers
{
    /// <summary>
    /// <see cref="RefCountedBuffer"/> 의 마지막 <c>Release</c> 가 내부 block 을 돌려줄 owner 계약이다.
    /// 일반 pinned pool 뿐 아니라 io_uring registered slot owner 도 같은 반환 경계를 사용할 수 있다.
    /// </summary>
    public interface IRefCountedBufferOwner
    {
        /// <summary>
        /// 이 owner 가 소유하는 모든 block 의 고정 바이트 길이다.
        /// </summary>
        int BlockSize { get; }

        /// <summary>
        /// 마지막 참조가 해제된 block 을 owner 로 돌려준다.
        /// </summary>
        void Return(byte[] block);
    }
}
