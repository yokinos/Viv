using System;
using System.Collections.Generic;
using System.Text;
using Viv.Elysia.CacheBucket;
using Viv.Entity.Database.Apex;

namespace Viv.Apex.Core.IRepository
{
    public interface ICompanyRepository
    {
        Task<EntityListBucket<AtCompany, AtCompanySystemVersion>?> GetBucketAsync(long companyId);

        /// <summary>
        /// 该公司有权使用的 App Id —— 取自它买的各版本绑定的 App，取并集。
        /// </summary>
        Task<List<long>> GetCompanyAuthorizedAppIdsAsync(long companyId);

        Task<AtCompany?> GetCompanyAsync(long companyId);

        Task<AtCompany?> GetCompanyAsync(string code);
    }
}
