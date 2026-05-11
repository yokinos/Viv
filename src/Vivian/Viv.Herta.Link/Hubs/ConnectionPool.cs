using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace Viv.Herta.Link.Hubs
{
    public class ConnectionPool : IConnectionPool
    {
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ConcurrentDictionary<ConnectionKey, ConcurrentDictionary<string, byte>> _userConnections = new();
        private readonly ConcurrentDictionary<string, ConnectionKey> _connectionLookup = new();

        public ConnectionPool(IHubContext<ChatHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public void Add(string connectionId, long tenantId, long userId, long appId)
        {
            var key = new ConnectionKey(tenantId, userId, appId);
            var connections = _userConnections.GetOrAdd(key, _ => new ConcurrentDictionary<string, byte>());

            connections[connectionId] = 0;
            _connectionLookup[connectionId] = key;
        }

        public void Remove(string connectionId)
        {
            if (!_connectionLookup.TryRemove(connectionId, out var key)) return;

            if (_userConnections.TryGetValue(key, out var connections))
            {
                connections.TryRemove(connectionId, out _);
                if (connections.IsEmpty)
                    _userConnections.TryRemove(key, out _);
            }
        }

        public List<string> GetConnectionIds(long tenantId, long userId)
        {
            var result = new List<string>();
            foreach (var kv in _userConnections)
            {
                if (kv.Key.TenantId == tenantId && kv.Key.UserId == userId)
                    result.AddRange(kv.Value.Keys);
            }

            return result;
        }

        public List<string> GetConnectionIds(long tenantId, long userId, long appId)
        {
            var key = new ConnectionKey(tenantId, userId, appId);
            return _userConnections.TryGetValue(key, out var connections)
                ? [.. connections.Keys]
                : [];
        }

        public List<ConnectionInfo> GetConnections(long tenantId)
        {
            var result = new List<ConnectionInfo>();
            foreach (var kv in _userConnections)
            {
                if (kv.Key.TenantId != tenantId) continue;

                result.AddRange(kv.Value.Keys.Select(id => new ConnectionInfo(
                    id,
                    kv.Key.TenantId,
                    kv.Key.UserId,
                    kv.Key.AppId)));
            }

            return result;
        }

        public async Task ForceDisconnectAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            Remove(connectionId);
            await _hubContext.Clients.Client(connectionId)
                .SendAsync(HertaLinkClientMethods.ForceDisconnect, cancellationToken);
        }

        public async Task ForceDisconnectUserAsync(long tenantId, long userId, CancellationToken cancellationToken = default)
        {
            var ids = GetConnectionIds(tenantId, userId);
            foreach (var id in ids)
            {
                await ForceDisconnectAsync(id, cancellationToken);
            }
        }

        public async Task ForceDisconnectTenantAsync(long tenantId, CancellationToken cancellationToken = default)
        {
            var connections = GetConnections(tenantId);
            foreach (var connection in connections)
            {
                await ForceDisconnectAsync(connection.ConnectionId, cancellationToken);
            }
        }

        public void Clear()
        {
            _userConnections.Clear();
            _connectionLookup.Clear();
        }
    }
}
