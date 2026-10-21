using System.Collections.Concurrent;

namespace Viv.ServiceProxy.Apex
{
    /// <summary>
    /// AppId 校验结果的进程内缓存。查询本身是 Scoped（要拿 gRPC 客户端和请求上下文），
    /// 缓存放单例里，几条连接之间才共用。
    /// </summary>
    public sealed class ClientAppCheckCache
    {
        private readonly ConcurrentDictionary<long, Entry> _entries = new();

        public bool TryGet(long appId, out ClientAppCheck check)
        {
            if (_entries.TryGetValue(appId, out var entry) && entry.ExpireAt > DateTimeOffset.UtcNow)
            {
                check = entry.Check;
                return true;
            }

            check = default;
            return false;
        }

        public void Set(long appId, ClientAppCheck check, TimeSpan ttl)
            => _entries[appId] = new Entry(check, DateTimeOffset.UtcNow.Add(ttl));

        private readonly record struct Entry(ClientAppCheck Check, DateTimeOffset ExpireAt);
    }
}
