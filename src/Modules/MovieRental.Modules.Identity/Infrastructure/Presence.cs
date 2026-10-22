using System.Collections.Concurrent;

namespace MovieRental.Modules.Identity.Infrastructure;

/// <summary>
/// Who is online right now: anyone with at least one live connection to the chat hub (every
/// signed-in page opens one). Counted per connection, so a second tab closing does not mark
/// the person offline.
/// ponytail: in memory, one server. Behind a load balancer this moves to the SignalR backplane
/// store (Redis) so every server sees every connection.
/// </summary>
public sealed class Presence
{
    private readonly ConcurrentDictionary<Guid, int> _connections = new();

    public void Connected(Guid userId) => _connections.AddOrUpdate(userId, 1, (_, n) => n + 1);

    public void Disconnected(Guid userId)
    {
        if (_connections.AddOrUpdate(userId, 0, (_, n) => n - 1) <= 0)
            _connections.TryRemove(new KeyValuePair<Guid, int>(userId, 0));
    }

    public bool IsOnline(Guid userId) => _connections.ContainsKey(userId);

    /// <summary>A snapshot of everyone online, for filtering a database query.</summary>
    public Guid[] OnlineIds() => [.. _connections.Keys];
}
