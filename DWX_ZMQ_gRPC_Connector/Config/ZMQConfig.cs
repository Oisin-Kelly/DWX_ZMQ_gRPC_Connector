namespace DWX_ZMQ_gRPC_Connector.Config;

// ReSharper disable once InconsistentNaming
public class ZMQConfig
{
    public const string SectionName = "ZmqConfig";

    public string Host { get; init; } = "localhost";
    public string Protocol { get; init; } = "tcp";
    public int PushPort { get; init; } = 32768;
    public int PullPort { get; init; } = 32769;
    public int SubPort { get; init; } = 32770;
    public bool UseMock { get; init; } = false;

    public string PushEndpoint => $"{Protocol}://{Host}:{PushPort}";
    public string PullEndpoint => $"{Protocol}://{Host}:{PullPort}";
    public string SubEndpoint => $"{Protocol}://{Host}:{SubPort}";
}