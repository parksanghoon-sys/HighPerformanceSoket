namespace Hps.Transport
{
    /// <summary>
    /// native CQE에서 읽은 completion 결과를 managed pump가 다루기 쉬운 값으로 보존한다.
    ///
    /// Token은 SQE `user_data`와 같은 값이며, registry가 이 값을 사용해 완료 대상 context를 찾는다.
    /// Result는 성공 시 byte count 또는 fd 같은 양수/0 값이고, 실패 시 Linux errno의 음수 값이다.
    /// </summary>
    internal readonly struct IoUringCompletion
    {
        internal IoUringCompletion(ulong token, int result, uint flags)
        {
            Token = token;
            Result = result;
            Flags = flags;
        }

        internal ulong Token { get; }

        internal int Result { get; }

        internal uint Flags { get; }
    }
}
