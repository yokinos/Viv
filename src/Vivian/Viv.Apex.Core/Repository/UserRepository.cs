using Viv.Apex.Core.IRepository;
using Viv.Contracts.Interface;
using Viv.Delusion.Extension;
using Viv.Delusion.Generic;
using Viv.Elysia.CacheBucket;
using Viv.Elysia.Interface;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;
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

        public async Task<bool> AddAsync(AtUser user)
        {
            var flag = await _dbContext.InsertAsync(user);
            if (flag)
            {
                await RefreshAsync(user.Id);
            }
            return flag;
        }

        public async Task<bool> UpdateAsync(AtUser user)
        {
            var flag = await _dbContext.UpdateAsync(user);
            if (flag)
            {
                await RefreshAsync(user.Id);
            }
            return flag;
        }

        public async Task<bool> DeleteAsync(long userId)
        {
            var flag = await _dbContext.DeleteAsync<AtUser>(x => x.Id == userId);
            if (flag)
            {
                await RefreshAsync(userId);
            }
            return flag;
        }

        public async Task<bool> SoftDeleteAsync(long userId)
        {
            var flag = await _dbContext.SoftDeleteAsync<AtUser>(x => x.Id == userId);
            if (flag)
            {
                await RefreshAsync(userId);
            }
            return flag;
        }

        public async Task<AtUser?> GetAsync(long userId)
        {
            var bucket = await GetCacheAsync(userId);
            return bucket?.Entity;
        }

        public override async Task<EntityBucket<AtUser>?> GetDbAsync(params object[] keys)
        {
            var userId = keys[0].As<long>();
            var user = await _dbContext.SingleOrDefaultAsync<AtUser>(x => x.Id == userId && !x.IsDeleted);
            if (user == null) return null;
            return new EntityBucket<AtUser>(user);
        }

        public async Task<AtUser?> GetByPhoneAsync(string phone, EmUserType userType)
        {
            return await _dbContext.SingleOrDefaultAsync<AtUser>(x => x.Phone == phone && x.UserType == userType && !x.IsDeleted);
        }

        public async Task<PagedList<AtUser>> GetPagedListAsync(IApiPagedRequest request)
        {
            var (sql, parameter) = request.GetSqlQuery();
            return await _dbContext.PageAsync<AtUser>(sql, request.PageIndex, request.PageSize, parameter);
        }
    }
}
