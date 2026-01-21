using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Viv.Contracts;

namespace Viv.Redis.DbAllocator
{
    public class NoneAllocator : IDbAllocator
    {
        public int AllocateDbIndex(string redisKey, int? maxDbIndex)
        {
            var options = VivConfigRegistry.Get<RedisOptions>();
            return options?.DefaultDatabase ?? 0;
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
