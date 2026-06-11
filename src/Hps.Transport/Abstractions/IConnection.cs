using System;

namespace Hps.Transport
{
    /// <summary>
    /// Transport 계층이 관리하는 단일 연결 핸들과 수명 경계이다.
    ///
    /// 이 인터페이스는 연결 자체의 종료와 자원 해제에 집중한다. 송신 시도와 버퍼 소유권 판정은
    /// <see cref="ITransport.TrySend(IConnection, TransportSendBuffer)"/> 로 수행해, 큐나 펌프 같은
    /// Transport 내부 구현 세부사항이 연결 핸들 public API 로 새지 않게 한다.
    /// </summary>
    public interface IConnection : IDisposable
    {
        /// <summary>
        /// 연결을 닫고 이후 송신 시도를 거부한다. 구현은 pending 큐 항목, 송신 중 in-flight 항목,
        /// 조립 중 수신 버퍼를 모두 Release 해야 하며, 종료 후 풀 누수가 남아서는 안 된다.
        /// </summary>
        void Close();
    }
}
