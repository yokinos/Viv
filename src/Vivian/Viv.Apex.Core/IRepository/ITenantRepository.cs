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

        /// <summary>
        /// 该租户有权使用的 App Id —— 取自它买的版本绑定的 App（<c>AtSystemVersionAppRelation</c>）。
        /// 版本不可原地修改，所以引用版本即快照。
        /// </summary>
        Task<List<long>> GetTenantAuthorizedAppIdsAsync(long tenantId);
    }
}
