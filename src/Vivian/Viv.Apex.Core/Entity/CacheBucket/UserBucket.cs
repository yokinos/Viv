using System;
using System.Collections.Generic;
using System.Text;
using Viv.Elysia;
using Viv.Entity.Database.Apex;
using Viv.Momo.Interface;

namespace Viv.Apex.Core.Entity.CacheBucket
{
    public class UserBucket : ICacheBucket
    {
        public AtUser? User { get; set; }

        public AtUserBind? UserBind { get; set; }

        public List<AtUserRoleRelation>? UserRoleList { get; set; }

        public TimeSpan CacheTime => CacheTimeProvider.GetRandomDays();

        public string GetCacheKey(params object[] keys)
        {
            return $"UserBucket_{string.Join(",", keys)}";
        }
    }
}
