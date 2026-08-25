using System;
using System.Collections.Generic;
using System.Text;
using Viv.Delusion.Generic;
using Viv.Elysia.Interface;
using Viv.Entity.Database.Apex;

namespace Viv.Apex.Core.IRepository
{
    public interface IUserRoleRepository
    {
        Task<AtUserRole?> GetAsync(long roleId);

        Task<bool> AddAsync(AtUserRole entity);

        Task<bool> UpdateAsync(AtUserRole entity);

        Task<bool> DeleteAsync(long roleId);

        Task<bool> SoftDeleteAsync(long roleId);

        Task<PagedList<AtUserRole>> GetPagedListAsync(IApiPagedRequest request);
    }
}
