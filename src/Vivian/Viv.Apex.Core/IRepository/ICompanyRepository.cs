using System;
using System.Collections.Generic;
using System.Text;
using Viv.Elysia.CacheBucket;
using Viv.Entity.Database.Apex;

namespace Viv.Apex.Core.IRepository
{
    public interface ICompanyRepository
    {
        Task<EntityListBucket<AtCompany, AtCompanyAppRelation>?> GetBucketAsync(long companyId);

        Task<List<AtCompanyAppRelation>> GetCompanyAppsAsync(long companyId);

        Task<AtCompany?> GetCompanyAsync(long companyId);

        Task<AtCompany?> GetCompanyAsync(string code);
    }
}
