using System.Collections.Concurrent;
using Viv.Kinna.Contracts;
using Viv.Kinna.Metrics;

namespace Viv.Kinna.Connections
{
    /// <summary>
    /// 进程内连接表，语义见 <see cref="IKinnaConnectionPool"/>。
    ///
    /// 两级索引（按人、按客户端）都靠 <see cref="_connectionLookup"/> 反查维护。
    /// 结构变动（连上来、断开）走 <see cref="_structureLock"/>，读侧不加锁：
    /// 读和写抢同一把锁会把每条消息的投递路径串行化，而这里的写只发生在连接建立/断开时。
    /// </summary>
    public sealed class KinnaConnectionPool : IKinnaConnectionPool
    {
        // 按「租户 + 用户」索引，多端同步、按人踢都走它；这是每条消息都要查的那一级。
        private readonly ConcurrentDictionary<(long SubjectId, long UserId), ConcurrentDictionary<string, byte>> _userConnections = new();

        // 按「客户端 + 租户 + 用户」索引，只有在指定客户端查的时候才用
        private readonly ConcurrentDictionary<(long AppId, long SubjectId, long UserId), ConcurrentDictionary<string, byte>> _clientConnections = new();

        // 反查：连接 Id → 身份，移除和清扫靠它
        private readonly ConcurrentDictionary<string, KinnaConnectionKey> _connectionLookup = new();

        private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSeen = new();

        // 只保护索引结构（建桶、摘桶），不保护桶里的元素——那是并发字典自己的事
        private readonly object _structureLock = new();

        private readonly string _hub;

        // 时间从外部注入：租约是「时间到没到」的判断，用假时钟才能在测试里推进到边界
        private readonly TimeProvider _time;

        /// <param name="hub">模块名，只用于指标标签</param>
        /// <param name="timeProvider">时间源，不传就用系统时钟</param>
        public KinnaConnectionPool(string hub = "kinna", TimeProvider? timeProvider = null)
        {
            _hub = hub;
            _time = timeProvider ?? TimeProvider.System;
        }

        public void Add(string connectionId, long appId, long subjectId, long userId)
        {
            var key = new KinnaConnectionKey(appId, subjectId, userId);
            var added = false;

            lock (_structureLock)
            {
                added = _userConnections
                    .GetOrAdd((subjectId, userId), _ => new ConcurrentDictionary<string, byte>())
                    .TryAdd(connectionId, 0);

                _clientConnections
                    .GetOrAdd((appId, subjectId, userId), _ => new ConcurrentDictionary<string, byte>())
                    .TryAdd(connectionId, 0);

                _connectionLookup[connectionId] = key;
                _lastSeen[connectionId] = _time.GetUtcNow();
            }

            // transport 从连接 Id 前缀推断：两套传输共用这本表，指标要能分开看
            if (added)
                KinnaMetrics.ConnectionAdded(_hub, KinnaTransports.Of(connectionId));
        }

        public KinnaConnectionInfo? Remove(string connectionId)
        {
            KinnaConnectionKey key;
            var removedFromUserIndex = false;

            lock (_structureLock)
            {
                _lastSeen.TryRemove(connectionId, out _);

                // 查表与摘除是一步，谁拿到 key 谁负责后续清理
                if (!_connectionLookup.TryRemove(connectionId, out key))
                    return null;

                var userKey = (key.SubjectId, key.UserId);
                if (_userConnections.TryGetValue(userKey, out var byUser) && byUser.TryRemove(connectionId, out _))
                {
                    removedFromUserIndex = true;

                    // 桶空了就一起摘掉。空桶留着的话，每个注册过的用户都会永久占一本空字典，
                    // 百万用户就是几百 MB。Add 走同一把锁，不会出现「刚写进去就被摘掉」。
                    if (byUser.IsEmpty)
                        _userConnections.TryRemove(userKey, out _);
                }

                var clientKey = (key.AppId, key.SubjectId, key.UserId);
                if (_clientConnections.TryGetValue(clientKey, out var byClient)
                    && byClient.TryRemove(connectionId, out _)
                    && byClient.IsEmpty)
                {
                    _clientConnections.TryRemove(clientKey, out _);
                }
            }

            if (removedFromUserIndex)
                KinnaMetrics.ConnectionRemoved(_hub, KinnaTransports.Of(connectionId));

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
            var now = _time.GetUtcNow();

            // 用 TryUpdate 循环而不是「ContainsKey 后赋值」：后者中间被并发 Remove 时
            // 会把一条已经没有身份的连接重新写回心跳表，快照计数虚高到下一轮清扫
            while (_lastSeen.TryGetValue(connectionId, out var current))
            {
                if (_lastSeen.TryUpdate(connectionId, now, current))
                    return;
            }
        }

        public List<KinnaConnectionInfo> SweepExpired(TimeSpan ttl)
        {
            var now = _time.GetUtcNow();
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
                if (_lastSeen.TryGetValue(connectionId, out var seen) && _time.GetUtcNow() - seen <= ttl)
                    continue;

                var info = Remove(connectionId);
                if (info != null) removed.Add(info.Value);
            }

            return removed;
        }

        /// <summary>索引里还剩几本桶，排障和测试用。空桶应当被回收，这个数不会随连接数长期增长。</summary>
        public int BucketCount => _userConnections.Count + _clientConnections.Count;

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

            return new KinnaConnectionPoolSnapshot(count, _time.GetUtcNow() - oldest);
        }

        public void Clear()
        {
            lock (_structureLock)
            {
                _userConnections.Clear();
                _clientConnections.Clear();
                _connectionLookup.Clear();
                _lastSeen.Clear();
            }
        }
    }
}
