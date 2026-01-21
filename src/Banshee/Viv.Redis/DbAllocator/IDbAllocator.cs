using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Viv.Redis.DbAllocator
{
    public interface IDbAllocator
    {
        int AllocateDbIndex(string redisKey, int? maxDbIndex);

        [return:NotNull]
        Dictionary<int, RedisKey[]> AllocateGroupDbIndex(IEnumerable<string> redisKeys, int? maxDbIndex);
    }
}
