using Viv.Herta.Core.IService;
using Viv.Log;
using Viv.Redis;

namespace Viv.Herta.Core.Service
{
    /// <summary>
    /// 在线态：键存连接数，TTL 当租约。
    /// 连接建立 INCR + EXPIRE，断开 DECR，Ping 续期；进程被 kill 时没人 DECR，靠 TTL 自愈。
    /// </summary>
    public class PresenceService : IPresenceStore
    {
        /// <summary>租约时长，必须大于客户端 Ping 间隔（20s）</summary>
        private static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(90);

        private const string KeyPrefix = "viv:herta:presence";

        private readonly IRedisService _redis;
        private readonly ILoggerContract _logger;

        public PresenceService(IRedisService redis, ILoggerContract logger)
        {
            _redis = redis;
            _logger = logger;
        }

        public async Task OnlineAsync(long tenantId, long userId)
        {
            var key = BuildKey(tenantId, userId);

            try
            {
                await _redis.IncrementAsync(key).ConfigureAwait(false);
                await _redis.SetKeyExpireAsync(key, LeaseTtl).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 在线态是展示信息，失败不能把连接建立挡掉
                _logger.Warning($"在线态写入失败: {key} {ex.Message}");
            }
        }

        public async Task OfflineAsync(long tenantId, long userId)
        {
            var key = BuildKey(tenantId, userId);

            try
            {
                // 键已经过期时 DECR 会得到负数，此时直接删掉，不留负值
                var remaining = await _redis.IncrementAsync(key, -1).ConfigureAwait(false);
                if (remaining <= 0)
                    await _redis.RemoveAsync(key).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning($"在线态写入失败: {key} {ex.Message}");
            }
        }

        public async Task RenewAsync(long tenantId, long userId)
        {
            var key = BuildKey(tenantId, userId);

            try
            {
                // 键可能已经过期（连接表与 Redis 的清理不同步），这时续期是空操作。
                // 重新建一份，否则这条还活着的连接会一直显示离线。
                if (!await _redis.ExistAsync(key).ConfigureAwait(false))
                    await _redis.IncrementAsync(key).ConfigureAwait(false);

                await _redis.SetKeyExpireAsync(key, LeaseTtl).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning($"在线态续期失败: {key} {ex.Message}");
            }
        }

        public async Task<bool?> IsOnlineAsync(long tenantId, long userId)
        {
            var key = BuildKey(tenantId, userId);

            try
            {
                if (!await _redis.ExistAsync(key).ConfigureAwait(false))
                    return false;

                return await _redis.GetAsync<long>(key).ConfigureAwait(false) > 0;
            }
            catch (Exception ex)
            {
                _logger.Warning($"在线态查询失败: {key} {ex.Message}");
                return null;
            }
        }

        public async Task<Dictionary<long, bool?>> IsOnlineAsync(long tenantId, IReadOnlyList<long> userIds)
        {
            var ids = userIds.Distinct().ToList();
            var result = new Dictionary<long, bool?>(ids.Count);
            if (ids.Count == 0) return result;

            try
            {
                // 一条 MGET 查完：逐用户 EXISTS + GET 是 2N 次往返
                var values = await _redis
                    .GetManyAsync([.. ids.Select(x => BuildKey(tenantId, x))])
                    .ConfigureAwait(false);

                for (var i = 0; i < ids.Count; i++)
                {
                    // 键里存的是 INCR 写下的裸数字，直接解析，不走 JSON 反序列化
                    result[ids[i]] = values[i] is { } raw && long.TryParse(raw, out var count) && count > 0;
                }
            }
            catch (Exception ex)
            {
                // 一个查询失败就整批给 null（不知道），调用方按未知处理
                _logger.Warning($"在线态批量查询失败: tenant={tenantId} {ex.Message}");
                foreach (var id in ids)
                {
                    result[id] = null;
                }
            }

            return result;
        }

        private static string BuildKey(long tenantId, long userId) => $"{KeyPrefix}:{tenantId}:{userId}";
    }
}
