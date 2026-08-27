using System;
using System.Collections.Generic;
using System.Text;
using Viv.Delusion.Generic;
using Viv.Elysia.Interface;
using Viv.Entity.Database.Apex;

namespace Viv.Apex.Core.IRepository
{
    public interface ITenantRepository
    {
        Task<bool> AddAsync(AtTenant tenant);
        Task<bool> UpdateAsync(AtTenant tenant);
        Task<bool> DeleteAsync(long tenantId);
        Task<bool> SoftDeleteAsync(long tenantId);
        Task<AtTenant?> GetTenantAsync(long tenantId);

        Task<AtTenant?> GetTenantByCodeAsync(string code);

        Task<PagedList<AtTenant>> GetPagedListAsync(IApiPagedRequest request);
        Task<List<AtTenantAppRelation>> GetAtTenantAppsAsync(long tenantId);
    }
}
