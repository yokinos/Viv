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
    public class ClientAppRepository : DataAccessCacheBase<EntityListBucket<AtClientApp>>, IClientAppRepository
    {
        public ClientAppRepository(IVivContext context, IMomoDbContext dbContext, IRedisService redisService, ILoggerContract logger)
            : base(context, dbContext, redisService, logger)
        {
        }

        // ==================== AtClientApp ====================

        public async Task<bool> AddAsync(AtClientApp app)
        {
            var flag = await _dbContext.InsertAsync(app);
            if (flag)
            {
                await RefreshAsync(app.Id);
            }
            return flag;
        }

        public async Task<bool> UpdateAsync(AtClientApp app)
        {
            var flag = await _dbContext.UpdateAsync(app);
            if (flag)
            {
                await RefreshAsync(app.Id);
            }
            return flag;
        }

        public async Task<bool> DeleteAsync(long appId)
        {
            var flag = await _dbContext.DeleteAsync<AtClientApp>(x => x.Id == appId);
            if (flag)
            {
                await RefreshAsync(appId);
            }
            return flag;
        }

        public async Task<bool> SoftDeleteAsync(long appId)
        {
            var flag = await _dbContext.SoftDeleteAsync<AtClientApp>(x => x.Id == appId);
            if (flag)
            {
                await RefreshAsync(appId);
            }
            return flag;
        }

        public async Task<AtClientApp?> GetAsync(long appId)
        {
            var bucket = await GetCacheAsync(appId);
            return bucket?.Entity;
        }

        public override async Task<EntityListBucket<AtClientApp>?> GetDbAsync(params object[] keys)
        {
            var appId = keys[0].As<long>();
            var app = await _dbContext.SingleOrDefaultAsync<AtClientApp>(x => x.Id == appId && !x.IsDeleted);
            if (app == null) return null;
            return new EntityListBucket<AtClientApp>(app);
        }

        public async Task<PagedList<AtClientApp>> GetPagedListAsync(IApiPagedRequest request)
        {
            var (sql, parameter) = request.GetSqlQuery();
            return await _dbContext.PageAsync<AtClientApp>(sql, request.PageIndex, request.PageSize, parameter);
        }

        // ==================== AtClientAppVersion ====================

        public async Task<bool> AddVersionAsync(AtClientAppVersion version)
        {
            return await _dbContext.InsertAsync(version);
        }

        public async Task<bool> UpdateVersionAsync(AtClientAppVersion version)
        {
            return await _dbContext.UpdateAsync(version);
        }

        public async Task<bool> DeleteVersionAsync(long versionId)
        {
            return await _dbContext.DeleteAsync<AtClientAppVersion>(x => x.Id == versionId);
        }

        public async Task<bool> SoftDeleteVersionAsync(long versionId)
        {
            return await _dbContext.SoftDeleteAsync<AtClientAppVersion>(x => x.Id == versionId);
        }

        public async Task<AtClientAppVersion?> GetVersionAsync(long versionId)
        {
            return await _dbContext.SingleOrDefaultAsync<AtClientAppVersion>(x => x.Id == versionId && !x.IsDeleted);
        }

        public async Task<List<AtClientAppVersion>> GetVersionsByAppAsync(long clientAppId)
        {
            return await _dbContext.FindListAsync<AtClientAppVersion>(x => x.ClientAppId == clientAppId && !x.IsDeleted);
        }

        public async Task<PagedList<AtClientAppVersion>> GetPagedVersionListAsync(IApiPagedRequest request)
        {
            var (sql, parameter) = request.GetSqlQuery();
            return await _dbContext.PageAsync<AtClientAppVersion>(sql, request.PageIndex, request.PageSize, parameter);
        }

        // ==================== AtClientAppNotice ====================

        public async Task<bool> AddNoticeAsync(AtClientAppNotice notice)
        {
            return await _dbContext.InsertAsync(notice);
        }

        public async Task<bool> UpdateNoticeAsync(AtClientAppNotice notice)
        {
            return await _dbContext.UpdateAsync(notice);
        }

        public async Task<bool> DeleteNoticeAsync(long noticeId)
        {
            return await _dbContext.DeleteAsync<AtClientAppNotice>(x => x.Id == noticeId);
        }

        public async Task<bool> SoftDeleteNoticeAsync(long noticeId)
        {
            return await _dbContext.SoftDeleteAsync<AtClientAppNotice>(x => x.Id == noticeId);
        }

        public async Task<AtClientAppNotice?> GetNoticeAsync(long noticeId)
        {
            return await _dbContext.SingleOrDefaultAsync<AtClientAppNotice>(x => x.Id == noticeId && !x.IsDeleted);
        }

        public async Task<List<AtClientAppNotice>> GetNoticesByAppAsync(long clientAppId)
        {
            return await _dbContext.FindListAsync<AtClientAppNotice>(x => x.ClientAppId == clientAppId && !x.IsDeleted);
        }

        public async Task<PagedList<AtClientAppNotice>> GetPagedNoticeListAsync(IApiPagedRequest request)
        {
            var (sql, parameter) = request.GetSqlQuery();
            return await _dbContext.PageAsync<AtClientAppNotice>(sql, request.PageIndex, request.PageSize, parameter);
        }
    }
}
