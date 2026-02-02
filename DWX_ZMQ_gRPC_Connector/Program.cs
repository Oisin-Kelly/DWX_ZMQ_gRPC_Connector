using DWX_ZMQ_gRPC_Connector.Clients;
using DWX_ZMQ_gRPC_Connector.Clients.Mocks;
using DWX_ZMQ_gRPC_Connector.Config;
using DWX_ZMQ_gRPC_Connector.Ports;
using DWX_ZMQ_gRPC_Connector.Services;
using GrpcMarketDataService = DWX_ZMQ_gRPC_Connector.Services.MarketDataGrpcService;

var builder = WebApplication.CreateBuilder(args);

var zmqConfig = builder.Configuration.GetSection(ZMQConfig.SectionName).Get<ZMQConfig>() ?? new ZMQConfig();

builder.Services.Configure<ZMQConfig>(
    builder.Configuration.GetSection(ZMQConfig.SectionName));

if (zmqConfig.UseMock)
{
    builder.Services.AddSingleton<IZMQClient, MockZMQClient>();
    builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Debug);
}
else
{
    builder.Services.AddSingleton<IZMQClient, ZMQClient>();
}

builder.Services.AddSingleton<ISubscriptionManager, SubscriptionManager>();

builder.Services.AddSingleton<IMarketDataService, MarketDataService>();

builder.Services.AddSingleton<GrpcMarketDataService>();

builder.Services.AddGrpc();

var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Starting DWX ZMQ gRPC Connector in {Mode} mode",
    zmqConfig.UseMock ? "MOCK" : "PRODUCTION");

var marketDataService = app.Services.GetRequiredService<IMarketDataService>();
var grpcService = app.Services.GetRequiredService<GrpcMarketDataService>();
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

lifetime.ApplicationStarted.Register(() => { marketDataService.Start(lifetime.ApplicationStopping); });

lifetime.ApplicationStopping.Register(() =>
{
    grpcService.Dispose();
    marketDataService.Stop();
    marketDataService.Dispose();
});

app.MapGrpcService<GrpcMarketDataService>();

app.MapGet("/",
    () =>
        "DWX ZMQ gRPC Connector - Use a gRPC client to connect. Services: MarketDataService for streaming market data from a MT4 terminal.");

app.Run();