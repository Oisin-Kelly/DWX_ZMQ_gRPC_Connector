namespace DWX_ZMQ_gRPC_Connector.Ports;

public interface ISubscriptionManager
{
    public IReadOnlyList<string> Subscribe(IEnumerable<string> symbols);

    public IReadOnlyList<string> Unsubscribe(IEnumerable<string> symbols);

    public IReadOnlyList<string> GetActiveSymbols();

    public IClientSubscriptionSession CreateClientSession(string clientPeer, CancellationToken cancellationToken);

    public void RegisterClientSymbols(IClientSubscriptionSession session, IEnumerable<string> symbols);

    public IReadOnlyList<string> GetClientSymbols(string clientId);

    public void UnregisterClient(string clientId);

    public void DisconnectAllClients();
}