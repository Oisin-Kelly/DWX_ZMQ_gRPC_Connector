using DWX_ZMQ_gRPC_Connector;
using Grpc.Net.Client;

Console.WriteLine("Starting Market Data gRPC Client...");

var channel = GrpcChannel.ForAddress("http://localhost:5277", new GrpcChannelOptions
{
    HttpHandler = new SocketsHttpHandler
    {
        EnableMultipleHttp2Connections = true
    }
});
var client = new MarketDataService.MarketDataServiceClient(channel);

// Subscribe to market data
var request = new SubscribeRequest();

// valid symbols 
request.Symbols.Add("BTCUSD");
request.Symbols.Add("AAPL");
request.Symbols.Add("GOOG");
request.Symbols.Add("$INVALID1");
request.Symbols.Add("INVALID2#");

Console.WriteLine($"Subscribing to: {string.Join(", ", request.Symbols)}");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    var call = client.SubscribeMarketData(request, cancellationToken: cts.Token);
    
    while (await call.ResponseStream.MoveNext(cts.Token))
    {
        var response = call.ResponseStream.Current;
        
        if (response.ResponseCase == SubscribeResponse.ResponseOneofCase.Status)
        {
            Console.WriteLine($"[STATUS] {response.Status.Message}");
            Console.WriteLine($"  Successful: {string.Join(", ", response.Status.SuccessfulSymbols)}");
            if (response.Status.FailedSymbols.Count > 0)
            {
                Console.WriteLine($"  Failed: {string.Join(", ", response.Status.FailedSymbols)}");
            }
            Console.WriteLine();
        }
        else if (response.ResponseCase == SubscribeResponse.ResponseOneofCase.Tick)
        {
            var tick = response.Tick;
            Console.WriteLine($"[{tick.Symbol}] {tick.Timestamp.ToDateTime():HH:mm:ss.fff} - Bid: {tick.Bid:F5}, Ask: {tick.Ask:F5}");
        }
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("\nShutting down gracefully...");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

await channel.ShutdownAsync();
Console.WriteLine("Client stopped.");

