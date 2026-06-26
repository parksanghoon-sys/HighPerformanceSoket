namespace Hps.Sample.BrokerServer
{
    /// <summary>
    /// sample broker host 의 CLI parsing 결과다.
    /// Program 은 이 값만 사용하고, transport availability 판단은 후속 selector 단계에서 수행한다.
    /// </summary>
    public sealed class SampleBrokerServerCommandLine
    {
        internal SampleBrokerServerCommandLine(string host, int port, int maxFrameBytes, SampleTransportMode transportMode)
        {
            Host = host;
            Port = port;
            MaxFrameBytes = maxFrameBytes;
            TransportMode = transportMode;
        }

        public string Host { get; }

        public int Port { get; }

        public int MaxFrameBytes { get; }

        public SampleTransportMode TransportMode { get; }
    }
}
