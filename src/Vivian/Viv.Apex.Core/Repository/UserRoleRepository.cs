using JasperFx.Events;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.IRepository;
using Viv.Contracts.Interface;
using Viv.Delusion.Extension;
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
    public class UserRoleRepository : DataAccessCacheBase<EntityListBucket<AtUserRole>>, IUserRoleRepository
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
            if (flag)
            {
                await RefreshAsync(roleId);
            }
            return flag;
        }

        public async Task<AtUserRole?> GetAsync(long roleId)
        {
            var bucket = await GetCacheAsync(roleId);
            return bucket?.Entity;
        }

        public async override Task<EntityListBucket<AtUserRole>?> GetDbAsync(params object[] keys)
        {
            var roleId = keys[0].As<long>();
            var userRole = await _dbContext.SingleOrDefaultAsync<AtUserRole>(x => x.Id == roleId && !x.IsDeleted);
            if (userRole == null) return null;
            return new EntityListBucket<AtUserRole>(userRole);
        }

        public async Task<PagedList<AtUserRole>> GetPagedListAsync(IApiPagedRequest request)
        {
            var (sql, parameter) = request.GetSqlQuery();
            return await _dbContext.PageAsync<AtUserRole>(sql, request.PageIndex, request.PageSize, parameter);
        }

        public async Task<bool> SoftDeleteAsync(long roleId)
        {
            var flag = await _dbContext.SoftDeleteAsync<AtUserRole>(roleId);
            if (flag)
            {
                await RefreshAsync(roleId);
            }
            return flag;
        }

        public async Task<bool> UpdateAsync(AtUserRole entity)
        {
            var flag = await _dbContext.UpdateAsync(entity);
            if (flag)
            {
                await RefreshAsync(entity.Id);
            }
            return flag;
        }
    }
}
