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
        Task<bool> AddTenantAsync(AtTenant tenant);
        Task<bool> UpdateTenantAsync(AtTenant tenant);
        Task<bool> DeleteTenantAsync(long tenantId);
        Task<bool> SoftDeleteTenantAsync(long tenantId);
        Task<AtTenant?> GetTenantAsync(long tenantId);

        Task<AtTenant?> GetTenantByCodeAsync(string code);

        Task<PagedList<AtTenant>> GetTenantPagedListAsync(IApiPagedRequest request);
        Task<List<AtTenantAppRelation>> GetTenantAppRelationsAsync(long tenantId);
    }
}
