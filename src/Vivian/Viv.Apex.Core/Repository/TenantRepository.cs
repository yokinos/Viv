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
using Viv.Entity.Enums;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Base;
using Viv.Redis;

namespace Viv.Apex.Core.Repository
{
    public class TenantRepository : DataAccessCacheBase<EntityBucket<AtTenant>>, ITenantRepository
    {
        public TenantRepository(IVivContext context, IMomoDbContext dbContext, IRedisService redisService, IDistributedLock distributedLock, ILoggerContract logger)
            : base(context, dbContext, redisService, distributedLock, logger)
        {

        }

        public async Task<bool> AddTenantAsync(AtTenant tenant)
        {
            var flag = await _dbContext.InsertAsync(tenant);
            if (flag)
            {
                await RefreshAsync(tenant.Id);
            }
            return flag;
        }

        public async Task<bool> DeleteTenantAsync(long tenantId)
        {
            var flag = await _dbContext.DeleteAsync<AtTenant>(x => x.Id == tenantId);
            if (flag)
            {
                await RefreshAsync(tenantId);
            }
            return flag;
        }

        public async Task<AtTenant?> GetTenantAsync(long tenantId)
        {
            var bucket = await GetCacheAsync(tenantId);
            return bucket?.Entity;
        }

        public async Task<bool> SoftDeleteTenantAsync(long tenantId)
        {
            var flag = await _dbContext.SoftDeleteAsync<AtTenant>(x => x.Id == tenantId);
            if (flag)
            {
                await RefreshAsync(tenantId);
            }
            return flag;
        }

        public async Task<bool> UpdateTenantAsync(AtTenant tenant)
        {
            var flag = await _dbContext.UpdateAsync(tenant);
            if (flag)
            {
                await RefreshAsync(tenant.Id);
            }
            return flag;
        }

        public override async Task<EntityBucket<AtTenant>?> GetDbAsync(params object[] keys)
        {
            var tenantId = keys[0].As<long>();
            var tenant = await _dbContext.SingleOrDefaultAsync<AtTenant>(x => x.Id == tenantId && !x.IsDeleted);
            return tenant is null ? null : new EntityBucket<AtTenant>(tenant);
        }

        public async Task<PagedList<AtTenant>> GetTenantPagedListAsync(IApiPagedRequest request)
        {
            var (sql, parameter) = request.GetSqlQuery();
            return await _dbContext.PageAsync<AtTenant>(sql, request.PageIndex, request.PageSize, parameter);
        }

        public async Task<List<long>> GetTenantAuthorizedAppIdsAsync(long tenantId)
        {
            var bucket = await GetCacheAsync(tenantId);
            var tenant = bucket?.Entity;

            // 没买版本 = 一个 App 都没有。别返回「全部」，那是默认放行。
            if (tenant?.SystemVersionId is null)
            {
                return [];
            }

            var versionId = tenant.SystemVersionId.Value;
            var relations = await _dbContext.FindListAsync<AtSystemVersionAppRelation>(
                x => x.SystemVersionId == versionId && x.Status == EmStatus.Enabled);

            return relations.Select(x => x.ClientAppId).Distinct().ToList();
        }

        public async Task<AtTenant?> GetTenantByCodeAsync(string code)
        {
            return await _dbContext.SingleOrDefaultAsync<AtTenant>(x => x.Code == code && !x.IsDeleted);
        }
    }
}
