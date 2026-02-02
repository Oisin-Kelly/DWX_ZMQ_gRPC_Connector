using System.Threading.Channels;
using DWX_ZMQ_gRPC_Connector.Models;

namespace DWX_ZMQ_gRPC_Connector.Ports;

public interface IClientSubscriptionSession : IDisposable
{
    public string ClientId { get; }

    public string ClientPeer { get; }

    public CancellationToken CancellationToken { get; }

    public IReadOnlySet<string> SubscribedSymbols { get; }

    public ChannelReader<TickData> TickReader { get; }

    public void AddSymbols(IEnumerable<string> symbols);

    public void WriteTick(TickData tick);

    public void CompleteChannel();
}