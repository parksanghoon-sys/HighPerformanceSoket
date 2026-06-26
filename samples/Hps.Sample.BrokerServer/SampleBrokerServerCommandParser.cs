using System;

namespace Hps.Sample.BrokerServer
{
    /// <summary>
    /// sample broker host 전용 CLI parser 다.
    /// 기존 positional argument 호환성을 유지하면서 transport 선택 option 만 추가한다.
    /// </summary>
    public static class SampleBrokerServerCommandParser
    {
        public const string MessagePortInvalid = "port 는 1~65535 범위의 숫자여야 합니다.";
        public const string MessageMaxFrameBytesInvalid = "max-frame-bytes 는 1 이상의 숫자여야 합니다.";
        public const string MessageTransportValueRequired = "--transport 옵션에는 saea, rio 또는 auto 값이 필요합니다.";
        public const string MessageTransportValueInvalid = "--transport 옵션은 saea, rio 또는 auto 값만 사용할 수 있습니다.";

        public static bool TryParse(string[] args, out SampleBrokerServerCommandLine? commandLine, out string? errorMessage)
        {
            commandLine = null;
            errorMessage = null;

            if (args == null)
                throw new ArgumentNullException(nameof(args));

            if (args.Length == 4 && string.Equals(args[3], "--transport", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = MessageTransportValueRequired;
                return false;
            }

            if (args.Length != 3 && args.Length != 5)
                return false;

            int port;
            if (!int.TryParse(args[1], out port) || port <= 0 || port > 65535)
            {
                errorMessage = MessagePortInvalid;
                return false;
            }

            int maxFrameBytes;
            if (!int.TryParse(args[2], out maxFrameBytes) || maxFrameBytes <= 0)
            {
                errorMessage = MessageMaxFrameBytesInvalid;
                return false;
            }

            SampleTransportMode transportMode = SampleTransportMode.Saea;
            if (args.Length == 5)
            {
                if (!string.Equals(args[3], "--transport", StringComparison.OrdinalIgnoreCase))
                    return false;

                if (!TryParseTransportMode(args[4], out transportMode, out errorMessage))
                    return false;
            }

            commandLine = new SampleBrokerServerCommandLine(args[0], port, maxFrameBytes, transportMode);
            return true;
        }

        private static bool TryParseTransportMode(string value, out SampleTransportMode mode, out string? errorMessage)
        {
            mode = SampleTransportMode.Saea;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                errorMessage = MessageTransportValueRequired;
                return false;
            }

            if (string.Equals(value, "saea", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(value, "rio", StringComparison.OrdinalIgnoreCase))
            {
                mode = SampleTransportMode.Rio;
                return true;
            }

            if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
            {
                mode = SampleTransportMode.Auto;
                return true;
            }

            errorMessage = MessageTransportValueInvalid;
            return false;
        }
    }
}
