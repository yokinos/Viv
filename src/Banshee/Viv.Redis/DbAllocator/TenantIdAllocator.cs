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
        private readonly long _tenantId;

        public TenantIdAllocator()
        {
            var context = VivLocator.GetService<IVivContext>();
            _tenantId = context.TenantId;
        }

        public int AllocateDbIndex(string redisKey, int? maxDbIndex)
        {
            var effectiveMaxDb = maxDbIndex ?? 0;
            if (effectiveMaxDb == 0) { return 0; }
            long rawDbIndex = _tenantId % (effectiveMaxDb + 1);
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
    }
}
