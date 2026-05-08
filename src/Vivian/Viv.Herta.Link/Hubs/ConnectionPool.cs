using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace Viv.Herta.Link.Hubs
{
    internal readonly record struct ConnectionKey(long TenantId, long UserId, long AppId);

    public static class ConnectionPool
    {
        private static readonly ConcurrentDictionary<ConnectionKey, HashSet<string>> _userConnections = new();
        private static readonly ConcurrentDictionary<string, ConnectionKey> _connectionLookup = new();
        private static IHubContext<ChatHub>? _hubContext;

        public static void Initialize(IHubContext<ChatHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public static void Add(string connectionId, long tenantId, long userId, long appId)
        {
            var key = new ConnectionKey(tenantId, userId, appId);

            _userConnections.AddOrUpdate(key,
                _ => [connectionId],
                (_, set) => { set.Add(connectionId); return set; });

            _connectionLookup[connectionId] = key;
        }

        public static void Remove(string connectionId)
        {
            if (_connectionLookup.TryRemove(connectionId, out var key)
                && _userConnections.TryGetValue(key, out var connections))
            {
                connections.Remove(connectionId);
                if (connections.Count == 0)
                    _userConnections.TryRemove(key, out _);
            }
        }

        /// <summary>按 (TenantId, UserId) 跨 AppId 查找连接</summary>
        public static List<string> GetConnectionIds(long tenantId, long userId)
        {
            var result = new List<string>();
            foreach (var kv in _userConnections)
            {
                if (kv.Key.TenantId == tenantId && kv.Key.UserId == userId)
                    result.AddRange(kv.Value);
            }
            return result;
        }

        /// <summary>按 (TenantId, UserId, AppId) 精确查找连接</summary>
        public static List<string> GetConnectionIds(long tenantId, long userId, long appId)
        {
            var key = new ConnectionKey(tenantId, userId, appId);
            return _userConnections.TryGetValue(key, out var connections)
                ? [.. connections]
                : [];
        }

        /// <summary>强制断开单个连接</summary>
        public static async Task ForceDisconnectAsync(string connectionId)
        {
            var ctx = _hubContext;
            if (ctx == null) return;

            if (_connectionLookup.Remove(connectionId, out var key)
                && _userConnections.TryGetValue(key, out var connections))
            {
                connections.Remove(connectionId);
                if (connections.Count == 0)
                    _userConnections.TryRemove(key, out _);
            }

            await ctx.Clients.Client(connectionId).SendAsync("ForceDisconnect");
        }

        /// <summary>强制断开指定用户的所有连接</summary>
        public static async Task ForceDisconnectUserAsync(long tenantId, long userId)
        {
            var ids = GetConnectionIds(tenantId, userId);
            foreach (var id in ids)
                await ForceDisconnectAsync(id);
        }

        /// <summary>强制断开指定租户的所有连接</summary>
        public static async Task ForceDisconnectTenantAsync(long tenantId)
        {
            var keys = _userConnections.Keys.Where(k => k.TenantId == tenantId).ToList();
            foreach (var key in keys)
            {
                if (_userConnections.TryRemove(key, out var connections))
                {
                    foreach (var id in connections)
                    {
                        _connectionLookup.TryRemove(id, out _);
                        var ctx = _hubContext;
                        if (ctx != null)
                            await ctx.Clients.Client(id).SendAsync("ForceDisconnect");
                    }
                }
            }
        }

        /// <summary>清空所有连接池数据</summary>
        public static void Clear()
        {
            _userConnections.Clear();
            _connectionLookup.Clear();
        }
    }
}
