using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Viv.Aoi;
using Viv.Contracts.Interface;

namespace Viv.Redis.DbAllocator
{
    public class TenantIdAllocator : IDbAllocator
    {
        public int AllocateDbIndex(string redisKey, int? maxDbIndex)
        {
            var effectiveMaxDb = maxDbIndex ?? 0;
            if (effectiveMaxDb == 0) { return 0; }

            long tenantId = GetTenantId();
            long rawDbIndex = tenantId % (effectiveMaxDb + 1);
            int finalDbIndex = Math.Clamp((int)rawDbIndex, 0, effectiveMaxDb);
            return finalDbIndex;
        }

        [return: NotNull]
        public Dictionary<int, RedisKey[]> AllocateGroupDbIndex(IEnumerable<string> redisKeys, int? maxDbIndex)
        {
            var dict = new Dictionary<int, RedisKey[]>
            {
                { AllocateDbIndex(string.Empty, maxDbIndex), [.. redisKeys.Select(x => (RedisKey)x)] }
            };

            return dict;
        }

        /// <summary>
        /// 调用时解析当前租户，而非构造时缓存。
        /// allocator 是单例，VivContextMiddleware 每请求通过 IVivContextAccessor（静态 AsyncLocal）写入租户；
        /// 若在构造时读会被首个请求的租户（或启动时的 0）永久固化，导致所有请求打到同一个 Redis 库。
        /// </summary>
        private static long GetTenantId()
        {
            return VivLocator.GetService<IVivContextAccessor>().Current?.SubjectId ?? 0;
        }
    }
}
