namespace Hps.Protocol
{
    /// <summary>
    /// TCP frame payload 안에 들어온 broker command 종류이다.
    /// </summary>
    public enum TcpCommandKind
    {
        /// <summary>
        /// 지정 topic 을 구독한다.
        /// </summary>
        Subscribe = 1,

        /// <summary>
        /// 지정 topic 으로 payload 를 발행한다.
        /// </summary>
        Publish = 2,

        /// <summary>
        /// 지정 topic 구독을 해제한다.
        /// </summary>
        Unsubscribe = 3
    }
}
