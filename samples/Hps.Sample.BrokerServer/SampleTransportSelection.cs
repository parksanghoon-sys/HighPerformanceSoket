using Hps.Transport;

namespace Hps.Sample.BrokerServer
{
    /// <summary>
    /// sample host 의 transport 선택 결과다.
    /// 실패와 fallback notice 를 transport 생성과 함께 반환해 Program 이 같은 경로로 출력/종료를 처리하게 한다.
    /// </summary>
    public sealed class SampleTransportSelection
    {
        private SampleTransportSelection(
            bool succeeded,
            ITransport? transport,
            string? selectedBackendName,
            string? noticeMessage,
            string? errorMessage,
            int exitCode)
        {
            Succeeded = succeeded;
            Transport = transport;
            SelectedBackendName = selectedBackendName;
            NoticeMessage = noticeMessage;
            ErrorMessage = errorMessage;
            ExitCode = exitCode;
        }

        public bool Succeeded { get; }

        public ITransport? Transport { get; }

        public string? SelectedBackendName { get; }

        public string? NoticeMessage { get; }

        public string? ErrorMessage { get; }

        public int ExitCode { get; }

        public static SampleTransportSelection Success(ITransport transport, string selectedBackendName, string? noticeMessage)
        {
            return new SampleTransportSelection(true, transport, selectedBackendName, noticeMessage, null, 0);
        }

        public static SampleTransportSelection Failure(string errorMessage, int exitCode)
        {
            return new SampleTransportSelection(false, null, null, null, errorMessage, exitCode);
        }
    }
}
