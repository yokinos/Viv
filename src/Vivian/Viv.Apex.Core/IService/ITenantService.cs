using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.Entity.Vo.Tenant;
using Viv.Elysia.Request;
using Viv.Engine;

namespace Viv.Apex.Core.IService
{
    public interface ITenantService
    {
        Task<VivApiResult<GetTenantOutput>> GetTenantAsync(ApiIdRequest request);
    }
}
