using System;

namespace Hps.Protocol
{
    /// <summary>
    /// TCP frame payload 에서 해석한 broker command view 이다.
    ///
    /// 이 타입은 topic/payload 를 복사하지 않고 원본 frame span 을 가리킨다. 따라서 `ref struct`로 두어
    /// 힙 저장과 async 경계 통과를 막고, frame 을 소유한 caller 가 Release 하기 전 동기 범위에서만 사용하게 한다.
    /// </summary>
    public readonly ref struct TcpCommand
    {
        private readonly ReadOnlySpan<byte> _topic;
        private readonly ReadOnlySpan<byte> _payload;

        /// <summary>
        /// command 종류와 topic/payload span view 를 지정한다.
        /// </summary>
        public TcpCommand(TcpCommandKind kind, ReadOnlySpan<byte> topic, ReadOnlySpan<byte> payload)
        {
            Kind = kind;
            _topic = topic;
            _payload = payload;
        }

        /// <summary>
        /// 해석된 broker command 종류이다.
        /// </summary>
        public TcpCommandKind Kind { get; }

        /// <summary>
        /// topic token 이다. 원본 frame 을 가리키므로 command 사용자가 별도 보관하려면 필요한 시점에 복사해야 한다.
        /// </summary>
        public ReadOnlySpan<byte> Topic => _topic;

        /// <summary>
        /// publish payload 이다. `SUBSCRIBE`에서는 빈 span 이고, `PUBLISH`에서는 topic 뒤 공백 이후의 나머지 전체이다.
        /// </summary>
        public ReadOnlySpan<byte> Payload => _payload;
    }
}
