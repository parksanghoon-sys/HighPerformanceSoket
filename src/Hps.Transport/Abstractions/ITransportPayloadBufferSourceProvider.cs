using Hps.Buffers;

namespace Hps.Transport
{
    /// <summary>
    /// Transport backend 가 TCP frame payload 조립에 사용할 buffer source 를 선택적으로 제공하는 계약이다.
    ///
    /// Server 는 backend concrete type 을 몰라야 하므로 이 계약만 보고 source 를 요청한다.
    /// 구현체는 fallback pool 을 그대로 반환할 수도 있고, backend native resource 에 묶인 source 와 fallback 을
    /// 합성한 source 를 반환할 수도 있다.
    /// </summary>
    public interface ITransportPayloadBufferSourceProvider
    {
        /// <summary>
        /// TCP receive frame assembler 가 사용할 payload buffer source 를 만든다.
        /// </summary>
        /// <param name="fallbackPool">backend 전용 source 를 사용할 수 없을 때 그대로 사용할 기본 pinned pool.</param>
        /// <returns>TCP payload 조립에 사용할 counted buffer source.</returns>
        IRefCountedBufferSource CreateTcpPayloadBufferSource(PinnedBlockMemoryPool fallbackPool);
    }
}
