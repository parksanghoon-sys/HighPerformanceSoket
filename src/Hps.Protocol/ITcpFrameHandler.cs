using Hps.Buffers;
using Hps.Transport;

namespace Hps.Protocol
{
    /// <summary>
    /// TCP length-prefix frame 조립기가 완성된 payload 와 연결 종료를 상위 계층에 전달하는 콜백 계약이다.
    ///
    /// <see cref="OnFrame"/> 이 정상 반환하면 <see cref="RefCountedBuffer"/> 의 소유권은 구현체에게 있다.
    /// 구현체는 publish/fan-out 이 끝난 뒤 반드시 Release 해야 한다. 반대로 <see cref="OnFrame"/> 이 예외를
    /// 던지면 frame 을 수락하지 못한 것으로 보고 어댑터가 Release 와 연결 정리를 수행한다.
    /// </summary>
    public interface ITcpFrameHandler
    {
        /// <summary>
        /// 지정한 연결에서 payload frame 하나가 완성됐음을 알린다.
        /// </summary>
        void OnFrame(IConnection connection, RefCountedBuffer frame);

        /// <summary>
        /// 지정한 연결이 닫혀 더 이상 frame 이 도착하지 않음을 알린다.
        /// </summary>
        void OnConnectionClosed(IConnection connection);
    }
}
