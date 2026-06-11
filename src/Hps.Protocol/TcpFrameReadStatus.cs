namespace Hps.Protocol
{
    /// <summary>
    /// TCP length-prefix 조립기가 입력 chunk 를 처리한 결과이다.
    /// </summary>
    public enum TcpFrameReadStatus
    {
        /// <summary>
        /// 현재 chunk 를 소비했지만 아직 완성된 frame 은 없다.
        /// </summary>
        NeedMoreData = 0,

        /// <summary>
        /// payload 를 소유한 <c>RefCountedBuffer</c> frame 하나가 완성됐다.
        /// </summary>
        FrameReady = 1,

        /// <summary>
        /// wire header 의 payload 길이가 허용 상한을 넘었다.
        /// </summary>
        PayloadTooLarge = 2
    }
}
