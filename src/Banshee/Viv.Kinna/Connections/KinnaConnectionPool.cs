using System.Collections.Concurrent;
using Viv.Kinna.Metrics;

namespace Viv.Kinna.Connections
{
    /// <summary>
    /// 进程内连接表，语义见 <see cref="IKinnaConnectionPool"/>。
    /// </summary>
    public sealed class KinnaConnectionPool : IKinnaConnectionPool
    {
        // 按「租户 + 用户」索引，多端同步、按人踢都走它；这是每条消息都要查的那一级。
        // 空桶留着：摘掉最后一个连接时如果把整本字典移除，并发的 Add 可能刚写进这本字典就被一起丢掉。
        private readonly ConcurrentDictionary<(long SubjectId, long UserId), ConcurrentDictionary<string, byte>> _userConnections = new();

        // 按「客户端 + 租户 + 用户」索引，只有在指定客户端查的时候才用
        private readonly ConcurrentDictionary<(long AppId, long SubjectId, long UserId), ConcurrentDictionary<string, byte>> _clientConnections = new();

        // 反查：连接 Id → 身份，移除和清扫靠它
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

            var added = _userConnections
                .GetOrAdd((subjectId, userId), _ => new ConcurrentDictionary<string, byte>())
                .TryAdd(connectionId, 0);

            _clientConnections
                .GetOrAdd((appId, subjectId, userId), _ => new ConcurrentDictionary<string, byte>())
                .TryAdd(connectionId, 0);

            if (added)
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

            if (_userConnections.TryGetValue((key.SubjectId, key.UserId), out var byUser)
                && byUser.TryRemove(connectionId, out _))
            {
                KinnaMetrics.ConnectionRemoved(_hub);
            }

            if (_clientConnections.TryGetValue((key.AppId, key.SubjectId, key.UserId), out var byClient))
            {
                byClient.TryRemove(connectionId, out _);
            }

            return new KinnaConnectionInfo(connectionId, key.AppId, key.SubjectId, key.UserId);
        }

        public List<string> GetConnectionIds(long subjectId, long userId)
            => _userConnections.TryGetValue((subjectId, userId), out var connections)
                ? [.. connections.Keys]
                : [];

        public List<string> GetConnectionIds(long appId, long subjectId, long userId)
            => _clientConnections.TryGetValue((appId, subjectId, userId), out var connections)
                ? [.. connections.Keys]
                : [];

        /// <summary>管理用途（按租户踢人、排障），允许遍历。</summary>
        public List<KinnaConnectionInfo> GetConnections(long subjectId)
        {
            var result = new List<KinnaConnectionInfo>();
            foreach (var kv in _userConnections)
            {
                if (kv.Key.SubjectId != subjectId) continue;

                result.AddRange(kv.Value.Keys.Select(id => new KinnaConnectionInfo(
                    id,
                    _connectionLookup.TryGetValue(id, out var key) ? key.AppId : 0,
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

        /// <summary>测试用：把上次心跳拨到过去，让清扫能立刻判过期。</summary>
        internal void Backdate(string connectionId, TimeSpan age)
        {
            if (_lastSeen.ContainsKey(connectionId))
                _lastSeen[connectionId] = DateTimeOffset.UtcNow - age;
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
                // 收集和摘除之间可能已经 Ping 续租，摘之前再看一眼
                if (_lastSeen.TryGetValue(connectionId, out var seen) && DateTimeOffset.UtcNow - seen <= ttl)
                    continue;

                var info = Remove(connectionId);
                if (info != null) removed.Add(info.Value);
            }

            return removed;
        }

        public KinnaConnectionPoolSnapshot GetSnapshot()
        {
            var count = 0;
            var oldest = DateTimeOffset.MaxValue;

            foreach (var kv in _lastSeen)
            {
                count++;
                if (kv.Value < oldest) oldest = kv.Value;
            }

            if (count == 0)
                return new KinnaConnectionPoolSnapshot(0, TimeSpan.Zero);

            return new KinnaConnectionPoolSnapshot(count, DateTimeOffset.UtcNow - oldest);
        }

        public void Clear()
        {
            _userConnections.Clear();
            _clientConnections.Clear();
            _connectionLookup.Clear();
            _lastSeen.Clear();
        }
    }
}
