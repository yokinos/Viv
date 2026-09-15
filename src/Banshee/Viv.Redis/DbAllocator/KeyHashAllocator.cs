using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Delusion.Magic;

namespace Viv.Redis.DbAllocator
{
    public class KeyHashAllocator : IDbAllocator
    {
        public int AllocateDbIndex(string redisKey, int? maxDbIndex)
        {
            ArgumentNullException.ThrowIfNull(redisKey);
            if (string.IsNullOrWhiteSpace(redisKey))
                throw new ArgumentException("Redis键不能为空白字符串（包含全空格）", nameof(redisKey));

            if (maxDbIndex == null) return 0;

            // 用 XxHash64 而非 CRC64：非反射 CRC 的低位退化，取模分桶会退化成周期分布（16 库只落到 8 个）
            ulong hash = HashMagic.Compute(HashMode.XxHash64, redisKey);
            int dbIndex = (int)(hash % (ulong)(maxDbIndex + 1));
            return Math.Clamp(dbIndex, 0, maxDbIndex.Value);
        }

        public Dictionary<int, RedisKey[]> AllocateGroupDbIndex(IEnumerable<string> redisKeys, int? maxDbIndex)
        {
            ArgumentNullException.ThrowIfNull(redisKeys);
            var grouped = new Dictionary<int, List<RedisKey>>();
            foreach (var key in redisKeys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;

                int dbIndex = AllocateDbIndex(key, maxDbIndex);
                if (!grouped.TryGetValue(dbIndex, out var value))
                {
                    value = [];
                    grouped[dbIndex] = value;
                }

                value.Add((RedisKey)key);
            }

            return grouped.ToDictionary(x => x.Key, x => x.Value.ToArray());
        }
    }
}
