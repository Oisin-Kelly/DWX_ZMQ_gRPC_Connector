using System.Text.Json;
using System.Text.Json.Serialization;
using DWX_ZMQ_gRPC_Connector.Models;
using DWX_ZMQ_gRPC_Connector.Ports;

namespace DWX_ZMQ_gRPC_Connector.Services;

internal record TrackPricesResponse
{
    [JsonPropertyName("symbol_count")] public int SymbolCount { get; init; }

    [JsonPropertyName("error_symbols")] public List<string> ErrorSymbols { get; init; } = new();
}

internal record CommandResponse
{
    [JsonPropertyName("_action")] public string Action { get; init; } = string.Empty;

    [JsonPropertyName("_data")] public JsonElement? Data { get; init; }
}

public class MarketDataService : IMarketDataService
{
    private readonly IZMQClient _zmqClient;
    private readonly ILogger<MarketDataService> _logger;
    private readonly ISubscriptionManager _subscriptionManager;

    private const string MainDelimiter = ":|:";
    private const string DataDelimiter = ";";

    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private TaskCompletionSource<string>? _pendingResponse;

    public event Action<TickData>? OnTick;
    public bool IsRunning => _zmqClient.IsListening;

    public MarketDataService(IZMQClient zmqClient, ILogger<MarketDataService> logger,
        ISubscriptionManager subscriptionManager)
    {
        _zmqClient = zmqClient;
        _logger = logger;
        _subscriptionManager = subscriptionManager;

        _zmqClient.OnRawSocketMessage += HandleRawMessage;
        _zmqClient.OnCommandResponse += HandleCommandResponse;
    }

    public async Task<SubscriptionResult> SubscribeToSymbols(IEnumerable<string> symbols)
    {
        var symbolsList = symbols.ToList();

        await _commandLock.WaitAsync();

        try
        {
            _pendingResponse = new TaskCompletionSource<string>();

            SubscribeToNewSymbols(symbolsList);
            SendTrackPricesCommand(symbolsList);

            var response = await WaitForSubscriptionResponse();
            if (response is null)
                return HandleSubscriptionTimeout(symbolsList);

            var result = ParseTrackPricesResponse(response, symbolsList);
            HandleFailedSymbols(result.FailedSymbols);

            _logger.LogInformation("Subscription completed: {Success} successful, {Failed} failed",
                result.SuccessfulSymbols.Count, result.FailedSymbols.Count);

            return result;
        }
        finally
        {
            _pendingResponse = null;
            _commandLock.Release();
        }
    }

    private SubscriptionResult ParseTrackPricesResponse(string responseData, List<string> requestedSymbols)
    {
        try
        {
            var response = JsonSerializer.Deserialize<TrackPricesResponse>(responseData);

            if (response is null)
            {
                _logger.LogWarning("Failed to deserialize TRACK_PRICES response");
                return new SubscriptionResult(new List<string>(), requestedSymbols);
            }

            var failedSymbols = response.ErrorSymbols;
            var successfulSymbols = requestedSymbols.Except(failedSymbols).ToList();

            _logger.LogDebug("Parsed TRACK_PRICES response: {Success} successful, {Failed} failed",
                successfulSymbols.Count, failedSymbols.Count);

            if (failedSymbols.Any())
                _logger.LogWarning("Failed to subscribe to symbols: {FailedSymbols}", string.Join(", ", failedSymbols));

            return new SubscriptionResult(successfulSymbols, failedSymbols);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse TRACK_PRICES response: {Data}", responseData);
            return new SubscriptionResult(new List<string>(), requestedSymbols);
        }
    }

    public void UnsubscribeFromSymbols(IEnumerable<string> symbols)
    {
        var symbolsList = symbols.ToList();

        UnsubscribeFromTopics(symbolsList);
        UpdateExpertAdvisorTrackedSymbols();

        _logger.LogInformation("Unsubscribed from: {Symbols}", string.Join(", ", symbolsList));
    }

    public void Start(CancellationToken cancellationToken)
    {
        _zmqClient.StartListening(cancellationToken);

        _logger.LogInformation("Market data service started");
    }

    public void Stop()
    {
        try
        {
            _zmqClient.SendCommand("TRACK_PRICES");
            _logger.LogInformation("Sent TRACK_PRICES stop command to MetaTrader");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send stop command to MetaTrader");
        }

        _zmqClient.StopListening();

        _logger.LogInformation("Market data service stopped");
    }

    private void HandleRawMessage(string rawMessage)
    {
        try
        {
            var parts = rawMessage.Split(MainDelimiter);
            if (parts.Length != 2)
            {
                _logger.LogWarning("Invalid message format: {Message}", rawMessage);
                return;
            }

            var symbol = parts[0];
            ParseAndPublishTick(symbol, parts[1], rawMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing market data message: {Message}", rawMessage);
        }
    }

    private void ParseAndPublishTick(string symbol, string data, string rawMessage)
    {
        var dataParts = data.Split(DataDelimiter);

        if (dataParts.Length != 2)
        {
            _logger.LogWarning("Unexpected data format for {Symbol}: {Data}", symbol, data);
            return;
        }

        if (!double.TryParse(dataParts[0], out var bid) || !double.TryParse(dataParts[1], out var ask))
        {
            _logger.LogWarning("Failed to parse bid/ask from message: {Message}", rawMessage);
            return;
        }

        var tick = new TickData(symbol, bid, ask, DateTime.UtcNow);
        OnTick?.Invoke(tick);

        _logger.LogDebug("[{Symbol}] Bid: {Bid}, Ask: {Ask}", symbol, bid, ask);
    }

    private void HandleCommandResponse(string response)
    {
        try
        {
            _logger.LogDebug("Received command response: {Response}", response);

            if (_pendingResponse is null)
                return;

            var validJson = response.Replace('\'', '"');
            var commandResponse = JsonSerializer.Deserialize<CommandResponse>(validJson);

            if (commandResponse is not { Action: "TRACK_PRICES" })
                return;

            SetPendingResponse(commandResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling command response: {Response}", response);
            _pendingResponse?.TrySetException(ex);
        }
    }


    private void SubscribeToNewSymbols(List<string> symbols)
    {
        var newSymbols = _subscriptionManager.Subscribe(symbols);

        foreach (var symbol in newSymbols)
            _zmqClient.Subscribe(symbol);
    }

    private void SendTrackPricesCommand(List<string> symbols)
    {
        var command = "TRACK_PRICES;" + string.Join(";", symbols);
        _zmqClient.SendCommand(command);

        _logger.LogInformation("Sent subscription request for: {Symbols}", string.Join(", ", symbols));
    }

    private async Task<string?> WaitForSubscriptionResponse()
    {
        var responseTask = await Task.WhenAny(_pendingResponse!.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        if (responseTask != _pendingResponse.Task)
            return null;

        return await _pendingResponse.Task;
    }

    private SubscriptionResult HandleSubscriptionTimeout(List<string> symbols)
    {
        _logger.LogWarning("Timeout waiting for TRACK_PRICES response");

        var toUnsubscribe = _subscriptionManager.Unsubscribe(symbols);
        foreach (var symbol in toUnsubscribe)
            _zmqClient.Unsubscribe(symbol);

        return new SubscriptionResult(new List<string>(), symbols);
    }

    private void HandleFailedSymbols(List<string> failedSymbols)
    {
        if (!failedSymbols.Any())
            return;

        var toUnsubscribe = _subscriptionManager.Unsubscribe(failedSymbols);

        foreach (var symbol in toUnsubscribe)
            _zmqClient.Unsubscribe(symbol);
    }

    private void UnsubscribeFromTopics(List<string> symbols)
    {
        var toUnsubscribe = _subscriptionManager.Unsubscribe(symbols);

        foreach (var symbol in toUnsubscribe)
            _zmqClient.Unsubscribe(symbol);
    }

    private void UpdateExpertAdvisorTrackedSymbols()
    {
        var activeSymbols = _subscriptionManager.GetActiveSymbols();

        if (activeSymbols.Any())
        {
            var command = "TRACK_PRICES;" + string.Join(";", activeSymbols);
            _zmqClient.SendCommand(command);
            _logger.LogInformation("Updated EA to track: {Symbols}", string.Join(", ", activeSymbols));
        }
        else
        {
            _zmqClient.SendCommand("TRACK_PRICES");
            _logger.LogInformation("Stopped EA from tracking all symbols");
        }
    }


    private void SetPendingResponse(CommandResponse commandResponse)
    {
        if (commandResponse.Data.HasValue)
        {
            var dataJson = commandResponse.Data.Value.GetRawText();
            _pendingResponse!.TrySetResult(dataJson);
        }
        else
        {
            _logger.LogWarning("TRACK_PRICES response missing _data field");
            _pendingResponse!.TrySetResult("{}");
        }
    }

    public void Dispose()
    {
        _zmqClient.OnRawSocketMessage -= HandleRawMessage;
        _zmqClient.OnCommandResponse -= HandleCommandResponse;

        _logger.LogInformation("MarketDataService disposed");
    }
}