using DWX_ZMQ_gRPC_Connector.Ports;

namespace DWX_ZMQ_gRPC_Connector.Clients.Mocks;

// ReSharper disable once InconsistentNaming
public class MockZMQClient : IZMQClient
{
    private readonly ILogger<MockZMQClient> _logger;
    private readonly Timer _tickTimer;
    private readonly Random _random = new();
    private readonly HashSet<string> _subscribedSymbols = new();
    private readonly object _lock = new();

    private CancellationTokenSource? _listenerCts;
    private bool _isListening;

    public event Action<string>? OnRawSocketMessage;
    public event Action<string>? OnCommandResponse;
    public bool IsListening => _isListening;

    public MockZMQClient(ILogger<MockZMQClient> logger)
    {
        _logger = logger;
        _tickTimer = new Timer(GenerateMockTicks, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void SendCommand(string command)
    {
        _logger.LogDebug("Mock: Received command: {Command}", command);

        if (command.StartsWith("TRACK_PRICES"))
        {
            var parts = command.Split(';');
            var symbols = parts.Skip(1).ToList();

            var response = symbols.Any()
                ? $"{{'_action': 'TRACK_PRICES', '_data': {{'symbol_count':{symbols.Count}, 'error_symbols':[]}}}}"
                : "{'_action': 'TRACK_PRICES', '_data': {'symbol_count':0, 'error_symbols':[]}}";

            Task.Run(async () =>
            {
                await Task.Delay(100);
                OnCommandResponse?.Invoke(response);
                _logger.LogDebug("Mock: Sent response: {Response}", response);
            });
        }
    }

    public void Subscribe(string topic)
    {
        lock (_lock)
        {
            _subscribedSymbols.Add(topic);
            _logger.LogDebug("Mock: Subscribed to topic: {Topic}", topic);
        }
    }

    public void Unsubscribe(string topic)
    {
        lock (_lock)
        {
            _subscribedSymbols.Remove(topic);
            _logger.LogDebug("Mock: Unsubscribed from topic: {Topic}", topic);
        }
    }

    public void StartListening(CancellationToken cancellationToken)
    {
        if (_isListening)
            return;

        _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isListening = true;

        _tickTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(500));

        _logger.LogInformation("Mock: Started listening for market data");
    }

    public void StopListening()
    {
        if (!_isListening)
            return;

        _tickTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _listenerCts?.Cancel();
        _isListening = false;

        _logger.LogInformation("Mock: Stopped listening");
    }

    private void GenerateMockTicks(object? state)
    {
        if (!_isListening)
            return;

        List<string> symbols;
        lock (_lock)
        {
            symbols = _subscribedSymbols.ToList();
        }

        foreach (var symbol in symbols)
        {
            var (bid, ask) = GenerateMockPrice();
            var message = $"{symbol}:|:{bid:F5};{ask:F5}";

            try
            {
                OnRawSocketMessage?.Invoke(message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Mock: Error invoking OnRawSocketMessage for {Symbol}", symbol);
            }
        }
    }

    private (double bid, double ask) GenerateMockPrice()
    {
        var variation = (_random.NextDouble() - 0.5) * 0.001;
        var bid = 1 + variation;
        var ask = bid + 0.0001;

        return (bid, ask);
    }

    public void Dispose()
    {
        StopListening();
        _tickTimer.Dispose();
        _listenerCts?.Dispose();
        _logger.LogInformation("Mock: ZMQ client disposed");
    }
}