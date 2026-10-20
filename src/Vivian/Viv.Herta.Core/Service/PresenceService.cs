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
            var result = new Dictionary<long, bool?>(userIds.Count);

            foreach (var userId in userIds.Distinct())
            {
                result[userId] = await IsOnlineAsync(tenantId, userId).ConfigureAwait(false);
            }

            return result;
        }

        private static string BuildKey(long tenantId, long userId) => $"{KeyPrefix}:{tenantId}:{userId}";
    }
}
