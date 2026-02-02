namespace DWX_ZMQ_gRPC_Connector.Services;

using Microsoft.Extensions.Logging;
using Models;
using Ports;

public class SubscriptionManager : ISubscriptionManager
{
    private readonly ILogger<SubscriptionManager> _logger;
    private readonly Dictionary<string, int> _symbolSubscriptionCounts = new();
    private readonly Dictionary<string, ClientSubscriptionSession> _clientSessions = new();
    private readonly object _lock = new();

    public SubscriptionManager(ILogger<SubscriptionManager> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<string> Subscribe(IEnumerable<string> symbols)
    {
        var newSubscriptions = new List<string>();

        lock (_lock)
        {
            foreach (var symbol in symbols)
            {
                if (_symbolSubscriptionCounts.TryGetValue(symbol, out var count))
                {
                    _symbolSubscriptionCounts[symbol] = count + 1;
                }
                else
                {
                    _symbolSubscriptionCounts[symbol] = 1;
                    newSubscriptions.Add(symbol);
                }
            }
        }

        return newSubscriptions;
    }

    public IReadOnlyList<string> Unsubscribe(IEnumerable<string> symbols)
    {
        var removedSubscriptions = new List<string>();

        lock (_lock)
        {
            foreach (var symbol in symbols)
            {
                if (!_symbolSubscriptionCounts.TryGetValue(symbol, out var count))
                {
                    _logger.LogWarning("Attempted to unsubscribe from {Symbol} but no subscription found", symbol);
                    continue;
                }

                if (count <= 1)
                {
                    _symbolSubscriptionCounts.Remove(symbol);
                    removedSubscriptions.Add(symbol);
                }
                else
                {
                    _symbolSubscriptionCounts[symbol] = count - 1;
                }
            }
        }

        return removedSubscriptions;
    }

    public IReadOnlyList<string> GetActiveSymbols()
    {
        lock (_lock)
        {
            return _symbolSubscriptionCounts.Keys.ToList();
        }
    }

    public IClientSubscriptionSession CreateClientSession(string clientPeer, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var clientId = Guid.NewGuid().ToString();
            var session = new ClientSubscriptionSession(clientId, clientPeer, cancellationToken, _logger);

            _clientSessions[clientId] = session;

            _logger.LogDebug("Created session for client {ClientId}. Total active clients: {Count}",
                clientId, _clientSessions.Count);

            return session;
        }
    }

    public IReadOnlyList<string> GetClientSymbols(string clientId)
    {
        lock (_lock)
        {
            if (_clientSessions.TryGetValue(clientId, out var session))
                return session.SubscribedSymbols.ToList();

            return new List<string>();
        }
    }

    public void RegisterClientSymbols(IClientSubscriptionSession session, IEnumerable<string> symbols)
    {
        lock (_lock)
        {
            session.AddSymbols(symbols);
        }
    }

    public void UnregisterClient(string clientId)
    {
        lock (_lock)
        {
            if (_clientSessions.Remove(clientId, out var session))
            {
                session.Dispose();
                _logger.LogDebug("Unregistered client {ClientId}. Total active clients: {Count}",
                    clientId, _clientSessions.Count);
            }
        }
    }

    public void DisconnectAllClients()
    {
        List<ClientSubscriptionSession> sessionsToDisconnect;

        lock (_lock)
        {
            sessionsToDisconnect = _clientSessions.Values.ToList();
            _clientSessions.Clear();
        }

        _logger.LogInformation("Disconnecting {Count} connected client(s)...", sessionsToDisconnect.Count);

        foreach (var session in sessionsToDisconnect)
        {
            try
            {
                session.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disconnecting client session {ClientId}", session.ClientId);
            }
        }

        _logger.LogInformation("All clients disconnected");
    }
}