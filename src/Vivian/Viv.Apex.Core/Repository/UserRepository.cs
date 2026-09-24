using Viv.Apex.Core.Entity.CacheBucket;
using Viv.Apex.Core.IRepository;
using Viv.Contracts.Interface;
using Viv.Delusion.Extension;
using Viv.Delusion.Generic;
using Viv.Elysia.Interface;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Base;
using Viv.Redis;

namespace Viv.Apex.Core.Repository
{
    /// <summary>
    /// 用户仓储
    /// </summary>
    public class UserRepository : DataAccessCacheBase<UserBucket>, IUserRepository
    {
        public UserRepository(IVivContext context, IMomoDbContext dbContext, IRedisService redisService, IDistributedLock distributedLock, ILoggerContract logger)
            : base(context, dbContext, redisService, distributedLock, logger)
        {
        }

        public async Task<bool> AddUserAsync(AtUser user)
        {
            var flag = await _dbContext.InsertAsync(user);
            if (flag)
            {
                await RefreshAsync(user.Id);
            }
            return flag;
        }

        public async Task<bool> UpdateUserAsync(AtUser user)
        {
            var flag = await _dbContext.UpdateAsync(user);
            if (flag)
            {
                await RefreshAsync(user.Id);
            }
            return flag;
        }

        public async Task<bool> DeleteUserAsync(long userId)
        {
            var flag = await _dbContext.DeleteAsync<AtUser>(x => x.Id == userId);
            if (flag)
            {
                await RefreshAsync(userId);
            }
            return flag;
        }

        public async Task<bool> SoftDeleteUserAsync(long userId)
        {
            var flag = await _dbContext.SoftDeleteAsync<AtUser>(x => x.Id == userId);
            if (flag)
            {
                await RefreshAsync(userId);
            }
            return flag;
        }

        public async Task<AtUser?> GetUserAsync(long userId)
        {
            var bucket = await GetCacheAsync(userId);
            return bucket?.User;
        }

        public async Task<AtUser?> GetUserByPhoneAsync(string phone, EmUserType userType)
        {
            return await _dbContext.SingleOrDefaultAsync<AtUser>(x => x.Phone == phone && x.UserType == userType && !x.IsDeleted);
        }

        public async Task<PagedList<AtUser>> GetUserPagedListAsync(IApiPagedRequest request)
        {
            var (sql, parameter) = request.GetSqlQuery();
            return await _dbContext.PageAsync<AtUser>(sql, request.PageIndex, request.PageSize, parameter);
        }

        public async Task<UserBucket?> GetUserBucketAsync(long userId)
        {
            return await GetCacheAsync(userId);
        }

        public async Task<List<AtUserRole>> GetUserRolesAsync(long userId)
        {
            var relations = await _dbContext.FindListAsync<AtUserRoleRelation>(x => x.UserId == userId);
            var roleIds = relations.Select(x => x.RoleId).Distinct().ToList();
            if (roleIds.Count == 0)
            {
                return [];
            }

            return await _dbContext.FindListAsync<AtUserRole>(x => roleIds.Contains(x.Id));
        }

        public async Task<bool> AddRoleAsync(AtUserRole role)
        {
            return await _dbContext.InsertAsync(role);
        }

        public async Task<bool> UpdateRoleAsync(AtUserRole role)
        {
            return await _dbContext.UpdateAsync(role);
        }

        public async Task<bool> DeleteRoleAsync(long roleId)
        {
            return await _dbContext.DeleteAsync<AtUserRole>(roleId);
        }

        public async Task<bool> SoftDeleteRoleAsync(long roleId)
        {
            return await _dbContext.SoftDeleteAsync<AtUserRole>(roleId);
        }

        /// <summary>
        /// 根据Id获取角色
        /// </summary>
        public async Task<AtUserRole?> GetRoleAsync(long roleId)
        {
            return await _dbContext.SingleOrDefaultAsync<AtUserRole>(x => x.Id == roleId && !x.IsDeleted);
        }

        public async Task<PagedList<AtUserRole>> GetRolePagedListAsync(IApiPagedRequest request)
        {
            var (sql, parameter) = request.GetSqlQuery();
            return await _dbContext.PageAsync<AtUserRole>(sql, request.PageIndex, request.PageSize, parameter);
        }

        public override async Task<UserBucket?> GetDbAsync(params object[] keys)
        {
            var userId = keys[0].As<long>();
            var user = await _dbContext.SingleOrDefaultAsync<AtUser>(x => x.Id == userId && !x.IsDeleted);
            if (user == null) return null;

            return new UserBucket
            {
                User = user,
                UserBind = await _dbContext.SingleOrDefaultAsync<AtUserBind>(x => x.UserId == user.Id)
            };
        }
    }
}
