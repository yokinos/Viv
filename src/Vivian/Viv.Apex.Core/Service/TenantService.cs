using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.Entity.Dto.Tenant.Output;
using Viv.Apex.Core.IRepository;
using Viv.Apex.Core.IService;
using Viv.Elysia.Request;
using Viv.Engine;

namespace Viv.Apex.Core.Service
{
    public class TenantService : ITenantService
    {
        private readonly ITenantRepository _tenantRepository;

        public TenantService(ITenantRepository tenantRepository)
        {
            _tenantRepository = tenantRepository;
        }

        public async Task<VivApiResult<GetTenantOutput>> GetTenantAsync(ApiIdRequest request)
        {
            var tenant = await _tenantRepository.GetTenantAsync(request.Id);
            if (tenant == null)
            {
                return VivApiResult<GetTenantOutput>.Failed("租户不存在");
            }

            var output = new GetTenantOutput()
            {
                TenantId = tenant.Id,
                TenantCode = tenant.Code,
                TenantName = tenant.Name
            };

            return VivApiResult<GetTenantOutput>.Success(output);
        }
    }
}
