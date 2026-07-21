using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.IRepository;
using Viv.Contracts.Interface;
using Viv.Delusion.Extension;
using Viv.Elysia.CacheBucket;
using Viv.Entity.Database.Apex;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Base;
using Viv.Redis;

namespace Viv.Apex.Core.Repository
{
    public class UserRepository : DataAccessCacheBase<EntityBucket<AtUser>>, IUserRepository
    {
        public UserRepository(IVivContext context, IMomoDbContext dbContext, IRedisService redisService, ILoggerContract logger)
            : base(context, dbContext, redisService, logger)
        {
        }

        public async Task<AtUser?> GetAsync(long userId)
        {
            var bucket = await GetCacheAsync(userId);
            if (bucket == null) return null;
            return bucket.Entity;
        }

        public override async Task<EntityBucket<AtUser>?> GetDbAsync(params object[] keys)
        {
            var userId = keys[0].As<long>();
            var user = await _dbContext.SingleOrDefaultAsync<AtUser>(x => x.Id == userId && x.IsDeleted == false);
            if (user == null) return null;
            return new EntityBucket<AtUser>(user);
        }
    }
}
