using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.IRepository;
using Viv.Contracts.Interface;
using Viv.Delusion.Generic;
using Viv.Elysia.CacheBucket;
using Viv.Elysia.Interface;
using Viv.Entity.Database.Apex;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Base;
using Viv.Redis;

namespace Viv.Apex.Core.Repository
{
    public class UserRoleRepository : DataAccessCacheBase<EntityBucket<AtUserRole>>, IUserRoleRepository
    {
        public UserRoleRepository(IVivContext context, IMomoDbContext dbContext, IRedisService redisService, ILoggerContract logger)
            : base(context, dbContext, redisService, logger)
        { }

        public async Task<bool> AddAsync(AtUserRole entity)
        {
            var flag = await _dbContext.InsertAsync(entity);
            if (flag)
            {
                await RefreshAsync(entity.Id);
            }
            return flag;
        }

        public async Task<bool> DeleteAsync(long roleId)
        {
            var flag = await _dbContext.DeleteAsync<AtUserRole>(roleId);
        }

        public Task<AtUserRole> GetAsync(long roleId)
        {
            throw new NotImplementedException();
        }

        public override Task<EntityBucket<AtUserRole>?> GetDbAsync(params object[] keys)
        {
            throw new NotImplementedException();
        }

        public Task<List<AtUserRole>> GetListAsync(long userId)
        {
            throw new NotImplementedException();
        }

        public Task<PagedList<AtUserRole>> GetPagedListAsync(IApiPagedRequest request)
        {
            throw new NotImplementedException();
        }

        public Task<bool> SoftDeleteAsync(long roleId)
        {
            throw new NotImplementedException();
        }

        public Task<bool> UpdateAsync(AtUserRole entity)
        {
            throw new NotImplementedException();
        }
    }
}
