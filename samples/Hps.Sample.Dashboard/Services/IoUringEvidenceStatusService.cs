namespace Hps.Sample.Dashboard.Services
{
    public sealed class IoUringEvidenceStatusService
    {
        public string GetStatusText()
        {
            return "Windows UI에서는 io_uring native path를 직접 실행하지 않는다. D181/D182 evidence는 원격 iouring-linux-contract gate에서 확인한다.";
        }
    }
}
