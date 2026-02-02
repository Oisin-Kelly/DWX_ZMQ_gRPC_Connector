using System.Threading.Channels;
using DWX_ZMQ_gRPC_Connector.Ports;

namespace DWX_ZMQ_gRPC_Connector.Models;

public sealed class ClientSubscriptionSession : IClientSubscriptionSession
{
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Channel<TickData> _tickChannel;
    private readonly HashSet<string> _subscribedSymbols;
    private readonly ILogger _logger;

    public string ClientId { get; }
    public string ClientPeer { get; }

    public CancellationToken CancellationToken => _cancellationTokenSource.Token;
    public IReadOnlySet<string> SubscribedSymbols => _subscribedSymbols;
    public ChannelReader<TickData> TickReader => _tickChannel.Reader;

    private const int MaxChannelCapacity = 1000;

    public ClientSubscriptionSession(
        string clientId,
        string clientPeer,
        CancellationToken contextCancellationToken,
        ILogger logger)
    {
        ClientId = clientId;
        ClientPeer = clientPeer;

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(contextCancellationToken);
        _subscribedSymbols = new HashSet<string>();
        _logger = logger;

        _tickChannel = Channel.CreateBounded<TickData>(new BoundedChannelOptions(MaxChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true
        });
    }

    public void AddSymbols(IEnumerable<string> symbols)
    {
        foreach (var symbol in symbols)
            _subscribedSymbols.Add(symbol);

        _logger.LogDebug("Client {ClientId} now subscribed to {Count} symbol(s)", ClientId, _subscribedSymbols.Count);
    }

    private bool ShouldReceiveTick(string symbol)
    {
        return _subscribedSymbols.Contains(symbol);
    }

    public void WriteTick(TickData tick)
    {
        if (ShouldReceiveTick(tick.Symbol))
            _tickChannel.Writer.TryWrite(tick);
    }

    public void CompleteChannel()
    {
        _tickChannel.Writer.Complete();
    }

    public void Dispose()
    {
        _tickChannel.Writer.Complete();
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();

        _logger.LogDebug("Disposed subscription session for client {ClientId}", ClientId);
    }
}