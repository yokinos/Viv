using System.Globalization;

namespace Viv.Redis
{
    /// <summary>
    /// 分布式锁 Redis 值编码与 Lua 脚本（加锁 / 释放 / 续期共用）。
    ///
    /// 非重入：值 = holderId 原文（可含 <c>_数字</c>）。
    /// 重入：值 = holderId + '\n' + count。换行分隔避免贪婪剥 <c>_N</c> 把
    /// <c>order-123_1</c> 误认成 holder=<c>order-123</c>（续期失败 / 认成别人的锁）。
    /// 归属判定：先精确相等，再按 <c>^(.*)\n%d+$</c> 取重入前缀。
    /// </summary>
    internal static class RedisLockScripts
    {
        public const char ReentrantSeparator = '\n';

        /// <summary>
        /// Lua：currentVal 是否属于 holder（与 <see cref="OwnedBy"/> 同规则）。
        /// </summary>
        public const string OwnedByFn = """
        local function owned_by(currentVal, holder)
            if currentVal == holder then return true end
            local prefix = string.match(currentVal, '^(.*)\n%d+$')
            return prefix == holder
        end

        """;

        public const string ReentrantAcquire = OwnedByFn + """
        local currentVal = redis.call('GET', KEYS[1])
        if currentVal == false then
            redis.call('SET', KEYS[1], ARGV[1] .. '\n1', 'EX', ARGV[2])
            return 1
        end
        if owned_by(currentVal, ARGV[1]) then
            local count = tonumber(string.match(currentVal, '\n(%d+)$')) or 1
            redis.call('SET', KEYS[1], ARGV[1] .. '\n' .. (count + 1), 'EX', ARGV[2])
            return 1
        end
        return 0
        """;

        /// <summary>
        /// 返回 0=无权/不存在，1=重入计数减一仍持有，2=完全释放（DEL）。
        /// </summary>
        public const string ReentrantRelease = OwnedByFn + """
        local currentVal = redis.call('GET', KEYS[1])
        if currentVal == false then
            return 0
        end
        if not owned_by(currentVal, ARGV[1]) then
            return 0
        end
        local count = tonumber(string.match(currentVal, '\n(%d+)$'))
        if count and count > 1 then
            redis.call('SET', KEYS[1], ARGV[1] .. '\n' .. (count - 1), 'EX', ARGV[2])
            return 1
        end
        redis.call('DEL', KEYS[1])
        return 2
        """;

        public const string Renew = OwnedByFn + """
        local current = redis.call('GET', KEYS[1])
        if current and owned_by(current, ARGV[1]) then
            redis.call('EXPIRE', KEYS[1], ARGV[2])
            return 1
        end
        return 0
        """;

        public static string EncodeReentrant(string holderId, int count)
            => holderId + ReentrantSeparator + count.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// 与 Lua <c>owned_by</c> 同规则，供单测钉死解析、无需连 Redis。
        /// </summary>
        public static bool OwnedBy(string currentVal, string holderId)
        {
            if (currentVal == holderId)
            {
                return true;
            }

            var sep = currentVal.LastIndexOf(ReentrantSeparator);
            if (sep <= 0)
            {
                return false;
            }

            var countSpan = currentVal.AsSpan(sep + 1);
            if (!int.TryParse(countSpan, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }

            return currentVal.AsSpan(0, sep).SequenceEqual(holderId.AsSpan());
        }
    }
}
