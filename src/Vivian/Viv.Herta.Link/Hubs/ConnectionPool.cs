using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Viv.Herta.Link.Metrics;

namespace Viv.Herta.Link.Hubs
{
    /// <summary>
    /// 进程内连接表
    /// </summary>
    public class ConnectionPool : IConnectionPool
    {
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ConcurrentDictionary<ConnectionKey, ConcurrentDictionary<string, byte>> _userConnections = new();
        private readonly ConcurrentDictionary<string, ConnectionKey> _connectionLookup = new();
        private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSeen = new();

        public ConnectionPool(IHubContext<ChatHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public void Add(string connectionId, long appId, long subjectId, long userId)
        {
            var key = new ConnectionKey(appId, subjectId, userId);
            var connections = _userConnections.GetOrAdd(key, _ => new ConcurrentDictionary<string, byte>());

            if (connections.TryAdd(connectionId, 0))
                HertaLinkMetrics.ConnectionAdded();

            _connectionLookup[connectionId] = key;
            _lastSeen[connectionId] = DateTimeOffset.UtcNow;
        }

        public void Remove(string connectionId)
        {
            _lastSeen.TryRemove(connectionId, out _);

            if (!_connectionLookup.TryRemove(connectionId, out var key)) return;

            if (_userConnections.TryGetValue(key, out var connections))
            {
                if (connections.TryRemove(connectionId, out _))
                    HertaLinkMetrics.ConnectionRemoved();

                if (connections.IsEmpty)
                    _userConnections.TryRemove(key, out _);
            }
        }

        public List<string> GetConnectionIds(long subjectId, long userId)
        {
            var result = new List<string>();
            foreach (var kv in _userConnections)
            {
                if (kv.Key.SubjectId == subjectId && kv.Key.UserId == userId)
                    result.AddRange(kv.Value.Keys);
            }

            return result;
        }

        public List<string> GetConnectionIds(long appId, long subjectId, long userId)
        {
            var key = new ConnectionKey(appId, subjectId, userId);
            return _userConnections.TryGetValue(key, out var connections)
                ? [.. connections.Keys]
                : [];
        }

        public List<ConnectionInfo> GetConnections(long subjectId)
        {
            var result = new List<ConnectionInfo>();
            foreach (var kv in _userConnections)
            {
                if (kv.Key.SubjectId != subjectId) continue;

                result.AddRange(kv.Value.Keys.Select(id => new ConnectionInfo(
                    id,
                    kv.Key.AppId,
                    kv.Key.SubjectId,
                    kv.Key.UserId)));
            }

            return result;
        }

        public ConnectionInfo? TryGet(string connectionId)
            => _connectionLookup.TryGetValue(connectionId, out var key)
                ? new ConnectionInfo(connectionId, key.AppId, key.SubjectId, key.UserId)
                : null;

        public void Touch(string connectionId)
        {
            if (_connectionLookup.ContainsKey(connectionId))
                _lastSeen[connectionId] = DateTimeOffset.UtcNow;
        }

        public List<string> SweepExpired(TimeSpan ttl)
        {
            var now = DateTimeOffset.UtcNow;
            var expired = new List<string>();

            foreach (var kv in _lastSeen)
            {
                if (now - kv.Value <= ttl) continue;
                expired.Add(kv.Key);
            }

            foreach (var connectionId in expired)
                Remove(connectionId);

            return expired;
        }

        public async Task ForceDisconnectAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            Remove(connectionId);
            await _hubContext.Clients.Client(connectionId)
                .SendAsync(HertaLinkClientMethods.ForceDisconnect, cancellationToken);
        }

        public async Task ForceDisconnectUserAsync(long subjectId, long userId, CancellationToken cancellationToken = default)
        {
            var ids = GetConnectionIds(subjectId, userId);
            foreach (var id in ids)
            {
                await ForceDisconnectAsync(id, cancellationToken);
            }
        }

        public async Task ForceDisconnectSubjectAsync(long subjectId, CancellationToken cancellationToken = default)
        {
            var connections = GetConnections(subjectId);
            foreach (var connection in connections)
            {
                await ForceDisconnectAsync(connection.ConnectionId, cancellationToken);
            }
        }

        public void Clear()
        {
            _userConnections.Clear();
            _connectionLookup.Clear();
            _lastSeen.Clear();
        }
    }
}
