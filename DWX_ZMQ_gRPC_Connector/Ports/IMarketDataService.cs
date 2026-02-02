using DWX_ZMQ_gRPC_Connector.Models;

namespace DWX_ZMQ_gRPC_Connector.Ports;

public interface IMarketDataService : IDisposable
{
    public event Action<TickData>? OnTick;

    public Task<SubscriptionResult> SubscribeToSymbols(IEnumerable<string> symbols);

    public void UnsubscribeFromSymbols(IEnumerable<string> symbols);

    public void Start(CancellationToken cancellationToken);

    public void Stop();

    public bool IsRunning { get; }
}

public record SubscriptionResult(List<string> SuccessfulSymbols, List<string> FailedSymbols);