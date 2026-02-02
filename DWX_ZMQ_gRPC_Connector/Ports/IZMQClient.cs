namespace DWX_ZMQ_gRPC_Connector.Ports;

// ReSharper disable once InconsistentNaming
public interface IZMQClient : IDisposable
{
    public event Action<string>? OnRawSocketMessage;

    public event Action<string>? OnCommandResponse;

    public void SendCommand(string command);

    public void Subscribe(string topic);

    public void Unsubscribe(string topic);

    public void StartListening(CancellationToken cancellationToken);

    public void StopListening();

    public bool IsListening { get; }
}