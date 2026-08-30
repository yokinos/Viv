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
    public class ClientAppSettingRepository : DataAccessCacheBase<EntityBucket<AtClientAppSetting>>, IClientAppSettingRepository
    {
        public ClientAppSettingRepository(IVivContext context, IMomoDbContext dbContext, IRedisService redisService, ILoggerContract logger)
            : base(context, dbContext, redisService, logger)
        {
        }

        public async Task<bool> AddAsync(AtClientAppSetting setting)
        {
            var flag = await _dbContext.InsertAsync(setting);
            if (flag && setting.ConfigKey != null)
            {
                await RefreshAsync(setting.ClientAppId, setting.ConfigKey);
            }
            return flag;
        }

        public async Task<bool> UpdateAsync(AtClientAppSetting setting)
        {
            var flag = await _dbContext.UpdateAsync(setting);
            if (flag && setting.ConfigKey != null)
            {
                await RefreshAsync(setting.ClientAppId, setting.ConfigKey);
            }
            return flag;
        }

        public async Task<bool> DeleteAsync(long clientAppId, string configKey)
        {
            var flag = await _dbContext.DeleteAsync<AtClientAppSetting>(x => x.ClientAppId == clientAppId && x.ConfigKey == configKey);
            if (flag)
            {
                await RefreshAsync(clientAppId, configKey);
            }
            return flag;
        }

        public async Task<bool> SoftDeleteAsync(long clientAppId, string configKey)
        {
            var flag = await _dbContext.SoftDeleteAsync<AtClientAppSetting>(x => x.ClientAppId == clientAppId && x.ConfigKey == configKey);
            if (flag)
            {
                await RefreshAsync(clientAppId, configKey);
            }
            return flag;
        }

        public async Task<AtClientAppSetting?> GetAsync(long clientAppId, string configKey)
        {
            var bucket = await GetCacheAsync(clientAppId, configKey);
            return bucket?.Entity;
        }

        public override async Task<EntityBucket<AtClientAppSetting>?> GetDbAsync(params object[] keys)
        {
            var clientAppId = keys[0].As<long>();
            var configKey = keys[1].As<string>();
            var setting = await _dbContext.SingleOrDefaultAsync<AtClientAppSetting>(
                x => x.ClientAppId == clientAppId && x.ConfigKey == configKey && !x.IsDeleted);
            if (setting == null) return null;
            return new EntityBucket<AtClientAppSetting>(setting);
        }

        public async Task<List<AtClientAppSetting>> GetSettingsByAppAsync(long clientAppId)
        {
            return await _dbContext.FindListAsync<AtClientAppSetting>(x => x.ClientAppId == clientAppId && !x.IsDeleted);
        }

        public async Task<PagedList<AtClientAppSetting>> GetPagedListAsync(IApiPagedRequest request)
        {
            var (sql, parameter) = request.GetSqlQuery();
            return await _dbContext.PageAsync<AtClientAppSetting>(sql, request.PageIndex, request.PageSize, parameter);
        }
    }
}
