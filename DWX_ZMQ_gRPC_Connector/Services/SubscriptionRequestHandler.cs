using DWX_ZMQ_gRPC_Connector.Models;
using DWX_ZMQ_gRPC_Connector.Ports;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace DWX_ZMQ_gRPC_Connector.Services;

internal sealed class SubscriptionRequestHandler : IDisposable
{
    private readonly IClientSubscriptionSession _session;
    private readonly IMarketDataService _marketDataService;
    private readonly ISubscriptionManager _subscriptionManager;
    private readonly IServerStreamWriter<SubscribeResponse> _responseStream;
    private readonly ILogger _logger;
    private readonly List<string> _requestedSymbols;

    private Action<TickData>? _tickHandler;

    public SubscriptionRequestHandler(
        IClientSubscriptionSession session,
        IMarketDataService marketDataService,
        ISubscriptionManager subscriptionManager,
        IServerStreamWriter<SubscribeResponse> responseStream,
        List<string> requestedSymbols,
        ILogger logger)
    {
        _session = session;
        _marketDataService = marketDataService;
        _subscriptionManager = subscriptionManager;
        _responseStream = responseStream;
        _requestedSymbols = requestedSymbols;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        try
        {
            SetupTickHandler();
            await SubscribeToSymbols();
            await StreamTicksToClient();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Client {Client} disconnected from market data stream for: {Symbols}",
                _session.ClientPeer, string.Join(", ", _session.SubscribedSymbols));
        }
        catch (RpcException ex)
        {
            _logger.LogInformation("RPC error for client {Client} on symbols {Symbols}: {Message}",
                _session.ClientPeer, string.Join(", ", _session.SubscribedSymbols), ex.Status.Detail);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming market data for: {Symbols}, to client {Client}",
                string.Join(", ", _session.SubscribedSymbols), _session.ClientPeer);
            throw new RpcException(new Status(StatusCode.Internal, "Error streaming market data"));
        }
    }

    private void SetupTickHandler()
    {
        _tickHandler = tick => _session.WriteTick(tick);
        _marketDataService.OnTick += _tickHandler;
    }

    private async Task SubscribeToSymbols()
    {
        var subscriptionResult = await _marketDataService.SubscribeToSymbols(_requestedSymbols);

        await SendSubscriptionStatusResponse(subscriptionResult);

        if (!subscriptionResult.SuccessfulSymbols.Any())
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"All symbols failed to subscribe: {string.Join(", ", subscriptionResult.FailedSymbols)}"));
        }

        _subscriptionManager.RegisterClientSymbols(_session, subscriptionResult.SuccessfulSymbols);
    }

    private async Task SendSubscriptionStatusResponse(SubscriptionResult subscriptionResult)
    {
        var statusMessage = subscriptionResult.FailedSymbols.Any()
            ? $"Subscribed to {subscriptionResult.SuccessfulSymbols.Count} symbol(s). Failed: {string.Join(", ", subscriptionResult.FailedSymbols)}"
            : $"Successfully subscribed to {subscriptionResult.SuccessfulSymbols.Count} symbol(s)";

        var statusResponse = new SubscribeResponse
        {
            Status = new SubscriptionStatus { Message = statusMessage }
        };
        statusResponse.Status.SuccessfulSymbols.AddRange(subscriptionResult.SuccessfulSymbols);
        statusResponse.Status.FailedSymbols.AddRange(subscriptionResult.FailedSymbols);

        await _responseStream.WriteAsync(statusResponse);
    }

    private async Task StreamTicksToClient()
    {
        await foreach (var tick in _session.TickReader.ReadAllAsync(_session.CancellationToken))
        {
            var tickResponse = new SubscribeResponse
            {
                Tick = new MarketTick
                {
                    Symbol = tick.Symbol,
                    Bid = tick.Bid,
                    Ask = tick.Ask,
                    Timestamp = Timestamp.FromDateTime(tick.Timestamp.ToUniversalTime())
                }
            };

            await _responseStream.WriteAsync(tickResponse, _session.CancellationToken);

            _logger.LogDebug("Streamed tick for {Symbol}: Bid={Bid}, Ask={Ask} to client {Client}",
                tick.Symbol, tick.Bid, tick.Ask, _session.ClientPeer);
        }
    }

    public void Dispose()
    {
        if (_tickHandler is not null)
            _marketDataService.OnTick -= _tickHandler;

        _marketDataService.UnsubscribeFromSymbols(_session.SubscribedSymbols);
        _session.CompleteChannel();
        _subscriptionManager.UnregisterClient(_session.ClientId);

        _logger.LogInformation("Cleaned up subscription for: {Symbols}, for client {Client}",
            string.Join(", ", _session.SubscribedSymbols), _session.ClientPeer);
    }
}