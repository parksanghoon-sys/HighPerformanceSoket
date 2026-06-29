namespace Hps.Transport
{
    /// <summary>
    /// io_uring SQE 하나가 어떤 transport 작업을 대표하는지 구분한다.
    ///
    /// CQE에는 native `user_data` token만 돌아오므로, managed 쪽에서는 token이 가리키는 context의
    /// 종류를 별도로 보존해야 receive/send/accept 후처리를 안전하게 분기할 수 있다.
    /// </summary>
    internal enum IoUringOperationKind
    {
        Receive = 1,
        Send = 2,
        Accept = 3
    }
}
