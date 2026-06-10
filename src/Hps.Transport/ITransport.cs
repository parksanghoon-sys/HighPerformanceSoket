using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hps.Transport
{
    /// <summary>
    /// OS별 소켓 백엔드(SAEA/RIO/io_uring)를 상위 계층에서 숨기는 Transport 루트 계약이다.
    ///
    /// 이번 계약 단위에서는 실제 listen/connect API 를 확정하지 않고, 구현 수명주기와 연결 소유권
    /// 경계만 먼저 둔다. 구체적인 수락/연결 모델은 SAEA 기준선 구현 단위에서 별도 테스트와 함께 확장한다.
    /// </summary>
    public interface ITransport : IDisposable
    {
        /// <summary>
        /// Transport 구현을 시작한다. 내부 워커, 등록 버퍼, completion queue 같은 backend 자원은
        /// 이 수명주기 안에서 준비되어야 한다.
        /// </summary>
        ValueTask StartAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Transport 구현을 중지하고 더 이상 새 연결/송신을 받지 않는다. 구현은 보유 중인 연결을 닫아
        /// <see cref="IConnection.Close"/> 계약을 만족시켜야 한다.
        /// </summary>
        ValueTask StopAsync(CancellationToken cancellationToken);
    }
}
