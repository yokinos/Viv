using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using Viv.Vva.Magic;

namespace Viv.Redis
{
    /// <summary>
    /// Redis数据库分片工具类（基于CRC64哈希）
    /// </summary>
    public static class RedisDbAllocator
    {
        /// <summary>
        /// 根据单个Redis Key计算分配的数据库编号（0-13）
        /// </summary>
        /// <param name="redisKey">Redis键（不能为空/空白字符串）</param>
        /// <returns>0-13的数据库编号</returns>
        /// <exception cref="ArgumentNullException">redisKey为null时抛出</exception>
        /// <exception cref="ArgumentException">redisKey为空白字符串时抛出</exception>
        public static int AllocateDbIndex(string redisKey, int? maxDbIndex)
        {
            ArgumentNullException.ThrowIfNull(redisKey);
            if (string.IsNullOrWhiteSpace(redisKey))
                throw new ArgumentException("Redis键不能为空白字符串（包含全空格）", nameof(redisKey));

            if (maxDbIndex == null) return 0;

            // 调用CRC64工具类计算哈希
            ulong crcHash = Crc64Magic.ComputeCrc64(redisKey);
            int dbIndex = (int)(crcHash % (ulong)(maxDbIndex + 1));
            return Math.Clamp(dbIndex, 0, maxDbIndex.Value);
        }

        /// <summary>
        /// 批量计算Redis Key对应的数据库编号
        /// </summary>
        /// <param name="redisKeys">Redis键列表（不能为空，内部会跳过空白Key）</param>
        /// <returns>Key-数据库编号字典（仅包含有效Key）</returns>
        /// <exception cref="ArgumentNullException">redisKeys为null时抛出</exception>
        public static Dictionary<RedisKey, int> AllocateDbIndex(IEnumerable<string> redisKeys, int? maxDbIndex)
        {
            ArgumentNullException.ThrowIfNull(redisKeys);

            var result = new Dictionary<RedisKey, int>();
            foreach (var key in redisKeys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                result[(RedisKey)key] = AllocateDbIndex(key, maxDbIndex);
            }
            return result;
        }

        /// <summary>
        /// 批量计算Redis Key并按数据库编号分组
        /// </summary>
        /// <param name="redisKeys">Redis键列表（不能为空，内部会跳过空白Key）</param>
        /// <returns>数据库编号-对应Key数组的字典（便于按DB批量操作）</returns>
        /// <exception cref="ArgumentNullException">redisKeys为null时抛出</exception>
        public static Dictionary<int, RedisKey[]> AllocateGroupDbIndex(IEnumerable<string> redisKeys, int? maxDbIndex)
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