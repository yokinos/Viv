using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.Entity.Dto.Account;
using Viv.Apex.Core.Entity.Vo.Account;
using Viv.Apex.Core.IRepository;
using Viv.Contracts.Attributes;
using Viv.Contracts.Interface;
using Viv.Delusion;
using Viv.Delusion.Extension;
using Viv.Entity.Any;
using Viv.Entity.Enums;
using Viv.Redis;

namespace Viv.Apex.Core.Module.Login
{
    [VivDependency(EmUserType.TenantUser)]
    public class TenantUserLoginImpl : LoginImplBase, ILoginContract, IDependency
    {
        private readonly ITenantRepository _tenantRepository;

        public TenantUserLoginImpl(ITokenService tokenService, IRedisService redisService, IVivContext context, IUserRepository userRepository,
            IClientAppRepository clientAppRepository, ITenantRepository tenantRepository)
            : base(tokenService, redisService, context, userRepository, clientAppRepository)
        {
            _tenantRepository = tenantRepository;
        }

        public async Task<FuncResult<LoginOutput>> LoginAsync(LoginRequest request)
        {
            if (request.UserType != EmUserType.TenantUser)
            {
                return FuncResult<LoginOutput>.Failed("登录类型非法");
            }

            var validateApp = await ValidateAppAsync(request.AppKey);
            if (!validateApp.IsSuccess)
            {
                return FuncResult<LoginOutput>.Failed(validateApp.Message);
            }

            var clientAppId = validateApp.Data.Id;

            var tenant = await _tenantRepository.GetTenantByCodeAsync(request.SubjectCode);
            if (tenant is null)
            {
                return FuncResult<LoginOutput>.Failed("租户不存在");
            }

            if (tenant.Status != EmStatus.Enabled)
            {
                return FuncResult<LoginOutput>.Failed("租户状态异常");
            }

            var tenantApps = await _tenantRepository.GetTenantAppRelationsAsync(tenant.Id);
            if (tenantApps.IsNullOrEmpty())
            {
                return FuncResult<LoginOutput>.Failed("客户端授权异常");
            }


            var appRelation = tenantApps.SingleOrDefault(x => x.ClientAppId == clientAppId);
            if (appRelation == null)
            {
                return FuncResult<LoginOutput>.Failed("当前组织未获得授权");
            }

            if (appRelation.Status != EmStatus.Enabled)
            {
                return FuncResult<LoginOutput>.Failed("当前组织授权已过期");
            }

            var validateUser = await ValidateUserAsync(request.UserType, request.UserName, request.Password);
            if (!validateUser.IsSuccess)
            {
                return FuncResult<LoginOutput>.Failed(validateUser.Message);
            }

            var output = await BuildLoginOutputAsync(clientAppId, validateUser.Data);

            return FuncResult<LoginOutput>.Success("login success", output);
        }
    }
}
