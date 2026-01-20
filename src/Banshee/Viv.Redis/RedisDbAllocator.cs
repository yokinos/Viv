using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Viv.Redis
{
    /// <summary>
    /// Redis数据库分片工具类（基于CRC64哈希）
    /// </summary>
    public static class RedisDbAllocator
    {
        public const int TotalDbCount = 14;
        private const int MaxDbIndex = TotalDbCount - 1;

        /// <summary>
        /// 根据单个Redis Key计算分配的数据库编号（0-13）
        /// </summary>
        /// <param name="redisKey">Redis键（不能为空/空白字符串）</param>
        /// <returns>0-13的数据库编号</returns>
        /// <exception cref="ArgumentNullException">redisKey为null时抛出</exception>
        /// <exception cref="ArgumentException">redisKey为空白字符串时抛出</exception>
        public static int AllocateDbIndex(string redisKey)
        {
            ArgumentNullException.ThrowIfNull(redisKey);
            if (string.IsNullOrWhiteSpace(redisKey))
                throw new ArgumentException("Redis键不能为空白字符串（包含全空格）", nameof(redisKey));

            // 调用CRC64工具类计算哈希（替换为你的Crc64Helper路径）
            ulong crcHash = Vva.Crc64Helper.ComputeCrc64(redisKey);
            int dbIndex = (int)(crcHash % (ulong)TotalDbCount);
            return Math.Clamp(dbIndex, 0, MaxDbIndex);
        }

        /// <summary>
        /// 批量计算Redis Key对应的数据库编号
        /// </summary>
        /// <param name="redisKeys">Redis键列表（不能为空，内部会跳过空白Key）</param>
        /// <returns>Key-数据库编号字典（仅包含有效Key）</returns>
        /// <exception cref="ArgumentNullException">redisKeys为null时抛出</exception>
        public static Dictionary<string, int> AllocateDbIndex(IEnumerable<string> redisKeys)
        {
            ArgumentNullException.ThrowIfNull(redisKeys);

            var result = new Dictionary<string, int>();
            foreach (var key in redisKeys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                result[key] = AllocateDbIndex(key);
            }
            return result;
        }

        /// <summary>
        /// 批量计算Redis Key并按数据库编号分组
        /// </summary>
        /// <param name="redisKeys">Redis键列表（不能为空，内部会跳过空白Key）</param>
        /// <returns>数据库编号-对应Key数组的字典（便于按DB批量操作）</returns>
        /// <exception cref="ArgumentNullException">redisKeys为null时抛出</exception>
        public static Dictionary<int, string[]> AllocateGroupDbIndex(IEnumerable<string> redisKeys)
        {
            ArgumentNullException.ThrowIfNull(redisKeys);
            var grouped = new Dictionary<int, List<string>>();
            foreach (var key in redisKeys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;

                int dbIndex = AllocateDbIndex(key);
                if (!grouped.TryGetValue(dbIndex, out List<string>? value))
                {
                    value = [];
                    grouped[dbIndex] = value;
                }

                value.Add(key);
            }

            return grouped.ToDictionary(x => x.Key,x => x.Value.ToArray());
        }

    }
}