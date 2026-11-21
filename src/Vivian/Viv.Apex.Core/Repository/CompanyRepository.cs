using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Text;
using Viv.Apex.Core.IRepository;
using Viv.Contracts.Interface;
using Viv.Elysia.CacheBucket;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Base;
using Viv.Redis;

namespace Viv.Apex.Core.Repository
{
    public class CompanyRepository : DataAccessCacheBase<EntityListBucket<AtCompany, AtCompanySystemVersion>>, ICompanyRepository
    {
        public CompanyRepository(IVivContext context, IMomoDbContext dbContext, IRedisService redisService, IDistributedLock distributedLock, ILoggerContract logger)
            : base(context, dbContext, redisService, distributedLock, logger)
        {

        }

        public async Task<EntityListBucket<AtCompany, AtCompanySystemVersion>?> GetBucketAsync(long companyId)
        {
            return await GetCacheAsync(companyId);
        }

        /// <summary>
        /// 该公司有权使用的 App Id —— 取自它买的各版本绑定的 App（<c>AtSystemVersionAppRelation</c>）。
        /// 一个公司可以买多个版本，取并集；停用的版本行与停用的版本-App 关系都不算。
        /// </summary>
        public async Task<List<long>> GetCompanyAuthorizedAppIdsAsync(long companyId)
        {
            var bucket = await GetCacheAsync(companyId);
            var versionIds = (bucket?.Entities ?? [])
                .Where(x => x.Status == EmStatus.Enabled)
                .Select(x => x.SystemVersionId)
                .Distinct()
                .ToList();

            // 没买版本 = 一个 App 都没有。别返回「全部」，那是默认放行。
            if (versionIds.Count == 0)
            {
                return [];
            }

            var relations = await _dbContext.FindListAsync<AtSystemVersionAppRelation>(
                x => versionIds.Contains(x.SystemVersionId) && x.Status == EmStatus.Enabled);

            return relations.Select(x => x.ClientAppId).Distinct().ToList();
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

        public override async Task<EntityListBucket<AtCompany, AtCompanySystemVersion>?> GetDbAsync(params object[] keys)
        {
            var companyId = keys[0].As<long>();
            var company = await _dbContext.SingleOrDefaultAsync<AtCompany>(x => x.Id == companyId && !x.IsDeleted);
            if (company == null) return null;
            return new EntityListBucket<AtCompany, AtCompanySystemVersion>()
            {
                Entity = company,
                Entities = await _dbContext.FindListAsync<AtCompanySystemVersion>(x => x.CompanyId == company.Id)
            };
        }
    }
}
