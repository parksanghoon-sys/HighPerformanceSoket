using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Hps.Transport
{
    /// <summary>
    /// TCP 수신 대기 소켓의 수명과 accept 경계를 나타내는 Transport 계층 계약이다.
    ///
    /// listener 는 연결을 직접 송신하지 않는다. 역할은 수신 대기 자원 관리와 accept 된
    /// <see cref="IConnection"/> 반환에 한정한다. 이미 accept 되어 호출자에게 반환된 연결은
    /// 각 <see cref="IConnection.Close"/> 계약에 따라 별도로 종료되어야 한다.
    /// </summary>
    public interface IConnectionListener : IDisposable
    {
        /// <summary>
        /// 실제로 바인딩된 로컬 endpoint 를 반환한다. 호출자가 포트 0을 넘긴 경우 구현은 OS가 선택한
        /// 포트를 반영해야 하므로, 테스트와 샘플은 listen 요청값이 아니라 이 값을 기준으로 connect 해야 한다.
        /// </summary>
        EndPoint LocalEndPoint { get; }

        /// <summary>
        /// 다음 TCP 연결을 수락한다.
        ///
        /// 반환된 연결의 pending 송신, in-flight 송신, 조립 중 수신 버퍼 수명은
        /// <see cref="IConnection.Close"/> 계약이 책임진다. listener 가 닫히거나
        /// <paramref name="cancellationToken"/> 이 취소되면 구현은 정상 흐름으로 대기 중인 accept 를 끝내야 한다.
        /// </summary>
        ValueTask<IConnection> AcceptAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 수신 대기를 중지하고 이후 accept 를 거부한다.
        /// 이미 accept 되어 반환된 연결의 종료는 각 연결 소유자가 별도로 수행한다.
        /// </summary>
        void Close();
    }
}
