namespace Hps.Protocol
{
    /// <summary>
    /// TCP command decode 실패 원인이다.
    /// </summary>
    public enum TcpCommandDecodeError
    {
        /// <summary>
        /// 오류 없음.
        /// </summary>
        None = 0,

        /// <summary>
        /// frame payload 가 비어 있다.
        /// </summary>
        EmptyFrame = 1,

        /// <summary>
        /// 첫 token 이 알려진 command 가 아니다.
        /// </summary>
        UnknownCommand = 2,

        /// <summary>
        /// command 뒤 topic token 이 없다.
        /// </summary>
        MissingTopic = 3,

        /// <summary>
        /// topic token 이 허용되지 않는 형식이다.
        /// </summary>
        InvalidTopic = 4,

        /// <summary>
        /// `PUBLISH` command 에 topic 과 payload 를 나누는 두 번째 공백이 없다.
        /// </summary>
        MissingPayloadSeparator = 5
    }
}
