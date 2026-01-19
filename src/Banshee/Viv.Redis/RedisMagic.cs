using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Viv.Redis
{
    public class RedisMagic
    {
        private const int TotalDbCount = 14;

        /// <summary>
        /// 根据RedisKey计算分配的数据库编号（0-13）
        /// </summary>
        /// <param name="redisKey">Redis键</param>
        /// <returns>0-13的数据库编号</returns>
        public static int AllocateDbIndex(string redisKey)
        {
            // 能到这一步 redisKey 一定不为空

            byte[] keyBytes = Encoding.UTF8.GetBytes(redisKey);
            byte[] hashBytes = MD5.HashData(keyBytes);

            long hashValue = BitConverter.ToInt64(hashBytes, 0);
            int dbIndex = (int)(Math.Abs(hashValue) % TotalDbCount);
            dbIndex = Math.Clamp(dbIndex, 0, 13);
            return dbIndex;
        }
    }
}
