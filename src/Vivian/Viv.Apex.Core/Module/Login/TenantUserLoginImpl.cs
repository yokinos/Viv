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

            // 到期即不可用：状态只表达人工开关，到期是时间函数（不靠定时任务去改状态）
            if (!tenant.IsActive(DateTimeOffset.UtcNow))
            {
                return FuncResult<LoginOutput>.Failed(tenant.Status != EmStatus.Enabled ? "租户状态异常" : "租户授权已到期");
            }

            // 租户买的是一个版本，版本绑定的 App 就是它有权用的 App（版本不可原地改，引用即快照）
            var authorizedAppIds = await _tenantRepository.GetTenantAuthorizedAppIdsAsync(tenant.Id);
            if (authorizedAppIds.IsNullOrEmpty())
            {
                return FuncResult<LoginOutput>.Failed("客户端授权异常");
            }

            if (!authorizedAppIds.Contains(clientAppId))
            {
                return FuncResult<LoginOutput>.Failed("当前组织未获得授权");
            }

            // 主体就是本次 SubjectCode 解析出的机构：同一手机号在别的机构另有账号，不能串
            var validateUser = await ValidateUserAsync(request.UserType, request.UserName, request.Password, tenant.Id);
            if (!validateUser.IsSuccess)
            {
                return FuncResult<LoginOutput>.Failed(validateUser.Message);
            }

            var output = await BuildLoginOutputAsync(clientAppId, validateUser.Data);

            return FuncResult<LoginOutput>.Success("login success", output);
        }
    }
}
