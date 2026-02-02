using DWX_ZMQ_gRPC_Connector.Ports;
using Grpc.Core;
using GrpcMarketDataService = DWX_ZMQ_gRPC_Connector.MarketDataService;

namespace DWX_ZMQ_gRPC_Connector.Services;

public class MarketDataGrpcService : GrpcMarketDataService.MarketDataServiceBase, IDisposable
{
    private readonly ILogger<MarketDataGrpcService> _logger;
    private readonly IMarketDataService _marketDataService;
    private readonly ISubscriptionManager _subscriptionManager;

    public MarketDataGrpcService(
        ILogger<MarketDataGrpcService> logger,
        IMarketDataService marketDataService,
        ISubscriptionManager subscriptionManager)
    {
        _logger = logger;
        _marketDataService = marketDataService;
        _subscriptionManager = subscriptionManager;
    }

    public override async Task SubscribeMarketData(
        SubscribeRequest request,
        IServerStreamWriter<SubscribeResponse> responseStream,
        ServerCallContext context)
    {
        var requestedSymbols = ValidateRequest(request);
        var session = _subscriptionManager.CreateClientSession(context.Peer, context.CancellationToken);

        using var handler = new SubscriptionRequestHandler(
            session,
            _marketDataService,
            _subscriptionManager,
            responseStream,
            requestedSymbols,
            _logger);

        await handler.ExecuteAsync();
    }

    public override Task<UnsubscribeResponse> UnsubscribeMarketData(
        UnsubscribeRequest request,
        ServerCallContext context)
    {
        var symbols = request.Symbols.ToList();

        _marketDataService.UnsubscribeFromSymbols(symbols);

        _logger.LogInformation("Client {Client} unsubscribed from: {Symbols}",
            context.Peer, string.Join(", ", symbols));

        return Task.FromResult(new UnsubscribeResponse
        {
            Success = true,
            Message = $"Unsubscribed from {symbols.Count} symbol(s)"
        });
    }

    private List<string> ValidateRequest(SubscribeRequest request)
    {
        var symbols = request.Symbols.Distinct().ToList();

        if (symbols.Count == 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "At least one symbol must be given"));
        }

        return symbols;
    }

    public void Dispose()
    {
        _subscriptionManager.DisconnectAllClients();
    }
}