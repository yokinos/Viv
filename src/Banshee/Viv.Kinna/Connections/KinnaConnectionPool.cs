using System.Collections.Concurrent;
using Viv.Kinna.Metrics;

namespace Viv.Kinna.Connections
{
    /// <summary>进程内连接表，语义见 <see cref="IKinnaConnectionPool"/>。</summary>
    public sealed class KinnaConnectionPool : IKinnaConnectionPool
    {
        private readonly ConcurrentDictionary<KinnaConnectionKey, ConcurrentDictionary<string, byte>> _userConnections = new();
        private readonly ConcurrentDictionary<string, KinnaConnectionKey> _connectionLookup = new();
        private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSeen = new();
        private readonly string _hub;

        /// <param name="hub">模块名，只用于指标标签</param>
        public KinnaConnectionPool(string hub = "kinna")
        {
            _hub = hub;
        }

        public void Add(string connectionId, long appId, long subjectId, long userId)
        {
            var key = new KinnaConnectionKey(appId, subjectId, userId);
            var connections = _userConnections.GetOrAdd(key, _ => new ConcurrentDictionary<string, byte>());

            if (connections.TryAdd(connectionId, 0))
                KinnaMetrics.ConnectionAdded(_hub);

            _connectionLookup[connectionId] = key;
            _lastSeen[connectionId] = DateTimeOffset.UtcNow;
        }

        public KinnaConnectionInfo? Remove(string connectionId)
        {
            _lastSeen.TryRemove(connectionId, out _);

            // 查表与摘除是一步，谁拿到 key 谁负责后续清理
            if (!_connectionLookup.TryRemove(connectionId, out var key))
                return null;

            if (_userConnections.TryGetValue(key, out var connections))
            {
                if (connections.TryRemove(connectionId, out _))
                    KinnaMetrics.ConnectionRemoved(_hub);

                if (connections.IsEmpty)
                    _userConnections.TryRemove(key, out _);
            }

            return new KinnaConnectionInfo(connectionId, key.AppId, key.SubjectId, key.UserId);
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
            var key = new KinnaConnectionKey(appId, subjectId, userId);
            return _userConnections.TryGetValue(key, out var connections)
                ? [.. connections.Keys]
                : [];
        }

        public List<KinnaConnectionInfo> GetConnections(long subjectId)
        {
            var result = new List<KinnaConnectionInfo>();
            foreach (var kv in _userConnections)
            {
                if (kv.Key.SubjectId != subjectId) continue;

                result.AddRange(kv.Value.Keys.Select(id => new KinnaConnectionInfo(
                    id,
                    kv.Key.AppId,
                    kv.Key.SubjectId,
                    kv.Key.UserId)));
            }

            return result;
        }

        public KinnaConnectionInfo? TryGet(string connectionId)
            => _connectionLookup.TryGetValue(connectionId, out var key)
                ? new KinnaConnectionInfo(connectionId, key.AppId, key.SubjectId, key.UserId)
                : null;

        public void Touch(string connectionId)
        {
            if (_connectionLookup.ContainsKey(connectionId))
                _lastSeen[connectionId] = DateTimeOffset.UtcNow;
        }

        public List<KinnaConnectionInfo> SweepExpired(TimeSpan ttl)
        {
            var now = DateTimeOffset.UtcNow;
            var expired = new List<string>();

            foreach (var kv in _lastSeen)
            {
                if (now - kv.Value <= ttl) continue;
                expired.Add(kv.Key);
            }

            var removed = new List<KinnaConnectionInfo>(expired.Count);
            foreach (var connectionId in expired)
            {
                var info = Remove(connectionId);
                if (info != null) removed.Add(info.Value);
            }

            return removed;
        }

        public void Clear()
        {
            _userConnections.Clear();
            _connectionLookup.Clear();
            _lastSeen.Clear();
        }
    }
}
