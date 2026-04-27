using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace DialogflowChatApi.WebSockets;

/// ----------------------SUMMARY------------------------
/// Tracks active WebSocket connections by a server-generated connection id.
/// Keeping this as a singleton lets other parts of the app (e.g. the
/// fulfillment webhook) push messages to connected clients if required.
/// 
public interface IConnectionManager
{
    string Register(WebSocket socket);
    bool TryGet(string connectionId, out WebSocket? socket);
    void Remove(string connectionId);
    int ActiveCount { get; }
}

public sealed class ConnectionManager : IConnectionManager
{
    private readonly ConcurrentDictionary<string, WebSocket> _connections = new();

    public int ActiveCount => _connections.Count;

    public string Register(WebSocket socket)
    {
        var id = Guid.NewGuid().ToString("N");
        _connections[id] = socket;
        return id;
    }

    public bool TryGet(string connectionId, out WebSocket? socket)
        => _connections.TryGetValue(connectionId, out socket);

    public void Remove(string connectionId)
        => _connections.TryRemove(connectionId, out _);
}
