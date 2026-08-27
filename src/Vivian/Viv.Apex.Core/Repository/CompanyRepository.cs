using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Text;
using Viv.Apex.Core.IRepository;
using Viv.Contracts.Interface;
using Viv.Elysia.CacheBucket;
using Viv.Entity.Database.Apex;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Base;
using Viv.Redis;

namespace Viv.Apex.Core.Repository
{
    public class CompanyRepository : DataAccessCacheBase<EntityListBucket<AtCompany, AtCompanyAppRelation>>, ICompanyRepository
    {
        public CompanyRepository(IVivContext context, IMomoDbContext dbContext, IRedisService redisService, ILoggerContract logger)
            : base(context, dbContext, redisService, logger)
        {

        }

        public async Task<EntityListBucket<AtCompany, AtCompanyAppRelation>?> GetBucketAsync(long companyId)
        {
            return await GetCacheAsync(companyId);
        }

        public async Task<List<AtCompanyAppRelation>> GetCompanyAppsAsync(long companyId)
        {
            var bucket = await GetCacheAsync(companyId);
            return bucket?.Entities ?? [];
        }

        public async Task<AtCompany?> GetCompanyAsync(long companyId)
        {
            var bucket = await GetCacheAsync(companyId);
            return bucket?.Entity;
        }

        public async Task<AtCompany?> GetCompanyAsync(string code)
        {
            return await _dbContext.SingleOrDefaultAsync<AtCompany>(x => x.CompanyCode == code && !x.IsDeleted);
        }

        public override async Task<EntityListBucket<AtCompany, AtCompanyAppRelation>?> GetDbAsync(params object[] keys)
        {
            var companyId = keys[0].As<long>();
            var company = await _dbContext.SingleOrDefaultAsync<AtCompany>(x => x.Id == companyId && !x.IsDeleted);
            if (company == null) return null;
            return new EntityListBucket<AtCompany, AtCompanyAppRelation>()
            {
                Entity = company,
                Entities = await _dbContext.FindListAsync<AtCompanyAppRelation>(x => x.CompanyId == company.Id)
            };
        }
    }
}
