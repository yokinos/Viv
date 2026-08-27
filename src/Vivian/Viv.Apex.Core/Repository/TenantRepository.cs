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
    public class TenantRepository : DataAccessCacheBase<EntityListBucket<AtTenant, AtTenantAppRelation>>, ITenantRepository
    {
        public TenantRepository(IVivContext context, IMomoDbContext dbContext, IRedisService redisService, ILoggerContract logger)
            : base(context, dbContext, redisService, logger)
        {

        }

        public async Task<bool> AddAsync(AtTenant tenant)
        {
            var flag = await _dbContext.InsertAsync(tenant);
            if (flag)
            {
                await RefreshAsync(tenant.Id);
            }
            return flag;
        }

        public async Task<bool> DeleteAsync(long tenantId)
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

        public async Task<bool> SoftDeleteAsync(long tenantId)
        {
            var flag = await _dbContext.SoftDeleteAsync<AtTenant>(x => x.Id == tenantId);
            if (flag)
            {
                await RefreshAsync(tenantId);
            }
            return flag;
        }

        public async Task<bool> UpdateAsync(AtTenant tenant)
        {
            var flag = await _dbContext.UpdateAsync(tenant);
            if (flag)
            {
                await RefreshAsync(tenant.Id);
            }
            return flag;
        }

        public override async Task<EntityListBucket<AtTenant, AtTenantAppRelation>?> GetDbAsync(params object[] keys)
        {
            var tenantId = keys[0].As<long>();
            var tenant = await _dbContext.SingleOrDefaultAsync<AtTenant>(x => x.Id == tenantId && !x.IsDeleted);
            if (tenant == null) return null;
            return new EntityListBucket<AtTenant, AtTenantAppRelation>()
            {
                Entity = tenant,
                Entities = await _dbContext.FindListAsync<AtTenantAppRelation>(x => x.TenantId == tenant.Id)
            };
        }

        public async Task<PagedList<AtTenant>> GetPagedListAsync(IApiPagedRequest request)
        {
            var (sql, parameter) = request.GetSqlQuery();
            return await _dbContext.PageAsync<AtTenant>(sql, request.PageIndex, request.PageSize, parameter);
        }

        public async Task<List<AtTenantAppRelation>> GetAtTenantAppsAsync(long tenantId)
        {
            var bucket = await GetCacheAsync(tenantId);
            return bucket?.Entities ?? [];
        }

        public async Task<AtTenant?> GetTenantByCodeAsync(string code)
        {
            return await _dbContext.SingleOrDefaultAsync<AtTenant>(x => x.Code == code && !x.IsDeleted);
        }
    }
}
