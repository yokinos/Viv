using System;
using System.Collections.Generic;
using System.Text;
using Viv.Cache.Redis;
using Viv.Engine.Enums;

namespace Viv.Engine.Options
{
    public record CacheOptions(CacheProviderType CacheProviderType, RedisOptions RedisOptions);
}
