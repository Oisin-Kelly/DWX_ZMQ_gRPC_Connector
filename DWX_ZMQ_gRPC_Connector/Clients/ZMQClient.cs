namespace DWX_ZMQ_gRPC_Connector.Clients;

using Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Config;
using NetMQ;
using NetMQ.Sockets;

// ReSharper disable once InconsistentNaming
public class ZMQClient : IZMQClient
{
    private readonly ILogger<ZMQClient> _logger;

    private readonly PushSocket _pushSocket;
    private readonly PullSocket _pullSocket;
    private readonly SubscriberSocket _subSocket;

    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;

    public event Action<string>? OnRawSocketMessage;
    public event Action<string>? OnCommandResponse;
    public bool IsListening => _listenerTask is { IsCompleted: false };

    private const int ListenerShutdownTimeoutSeconds = 5;

    public ZMQClient(IOptions<ZMQConfig> settings, ILogger<ZMQClient> logger)
    {
        var zmqSettings = settings.Value;
        _logger = logger;

        _pushSocket = new PushSocket();
        _pushSocket.Connect(zmqSettings.PushEndpoint);
        _logger.LogInformation("Connected to PUSH socket at {Endpoint}", zmqSettings.PushEndpoint);

        _pullSocket = new PullSocket();
        _pullSocket.Connect(zmqSettings.PullEndpoint);
        _logger.LogInformation("Connected to PULL socket at {Endpoint}", zmqSettings.PullEndpoint);

        _subSocket = new SubscriberSocket();
        _subSocket.Connect(zmqSettings.SubEndpoint);
        _logger.LogInformation("Connected to SUB socket at {Endpoint}", zmqSettings.SubEndpoint);
    }

    public void Subscribe(string topic)
    {
        _subSocket.Subscribe(topic);
        _logger.LogInformation("Subscribed to topic: {Topic}", topic);
    }

    public void Unsubscribe(string topic)
    {
        _subSocket.Unsubscribe(topic);
        _logger.LogInformation("Unsubscribed from topic: {Topic}", topic);
    }

    public void SendCommand(string command)
    {
        _pushSocket.SendFrame(command);
        _logger.LogDebug("Sent command: {Command}", command);
    }

    public void StartListening(CancellationToken cancellationToken)
    {
        if (IsListening)
        {
            _logger.LogWarning("ZMQ listener is already running");
            return;
        }

        _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listenerTask = Task.Run(() => ListenForMessages(_listenerCts.Token), _listenerCts.Token);
        _logger.LogInformation("Started ZMQ listener");
    }

    public void StopListening()
    {
        if (_listenerCts is not null)
        {
            _listenerCts.Cancel();
            _listenerCts.Dispose();
            _listenerCts = null;
        }

        _logger.LogInformation("Stopped ZMQ listener");
    }

    private void ListenForMessages(CancellationToken token)
    {
        _logger.LogInformation("ZMQ listener thread started");

        using var poller = new NetMQPoller();
        poller.Add(_pullSocket);
        poller.Add(_subSocket);

        _pullSocket.ReceiveReady += (_, e) =>
        {
            if (!e.Socket.TryReceiveFrameString(out var msg) || string.IsNullOrEmpty(msg)) return;

            _logger.LogDebug("Received PULL message: {Message}", msg);
            OnCommandResponse?.Invoke(msg);
        };

        _subSocket.ReceiveReady += (_, e) =>
        {
            if (!e.Socket.TryReceiveFrameString(out var msg) || string.IsNullOrEmpty(msg)) return;

            _logger.LogDebug("Received SUB message: {Message}", msg);
            OnRawSocketMessage?.Invoke(msg);
        };

        poller.RunAsync();

        token.WaitHandle.WaitOne();

        poller.Stop();

        _logger.LogInformation("ZMQ listener thread stopped");
    }

    public void Dispose()
    {
        StopListening();

        _listenerTask?.Wait(TimeSpan.FromSeconds(ListenerShutdownTimeoutSeconds));

        _pushSocket.Dispose();
        _pullSocket.Dispose();
        _subSocket.Dispose();

        _logger.LogInformation("ZmqClient disposed");
    }
}