using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.Entity.Vo.Tenant;
using Viv.Apex.Core.IRepository;
using Viv.Apex.Core.IService;
using Viv.Contracts.Interface;
using Viv.Elysia.Request;
using Viv.Engine;

namespace Viv.Apex.Core.Service
{
    public class TenantService : ITenantService
    {
        private readonly ITenantRepository _tenantRepository;

        private readonly IVivUnitOfWork _unitOfWork;

        public TenantService(ITenantRepository tenantRepository, IVivUnitOfWork unitOfWork)
        {
            _tenantRepository = tenantRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<VivApiResult<GetTenantOutput>> GetTenantAsync(ApiIdRequest request)
        {
            var tenant = await _tenantRepository.GetTenantAsync(request.Id);
            if (tenant == null)
            {
                return VivApiResult<GetTenantOutput>.Failed("租户不存在");
            }

            using var tx = await _unitOfWork.BeginAsync();

            var output = new GetTenantOutput()
            {
                TenantId = tenant.Id,
                TenantCode = tenant.Code,
                Name = tenant.Name,
            };

            return VivApiResult<GetTenantOutput>.Success(output);
        }
    }
}
