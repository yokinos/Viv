using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.Entity.Dto.Account;
using Viv.Apex.Core.Entity.Vo.Account;
using Viv.Apex.Core.Interface;
using Viv.Apex.Core.IRepository;
using Viv.Contracts.Attributes;
using Viv.Contracts.Interface;
using Viv.Delusion;
using Viv.Delusion.Extension;
using Viv.Delusion.Magic;
using Viv.Entity.Any;
using Viv.Entity.Enums;
using Viv.Redis;

namespace Viv.Apex.Core.Impl.Login
{
    [VivDependency(EmUserType.OrgUser)]
    public class OrgUserLoginImpl : LoginImplBase, ILoginContract, IDependency
    {
        private readonly IOrgRepository _orgRepository;

        public OrgUserLoginImpl(ITokenService tokenService, IRedisService redisService, IVivContext context, IUserRepository userRepository,
            IClientAppRepository clientAppRepository, IOrgRepository orgRepository)
            : base(tokenService, redisService, context, userRepository, clientAppRepository)
        {
            _orgRepository = orgRepository;
        }

        public async Task<FuncResult<LoginOutput>> LoginAsync(LoginRequest request)
        {
            if (request.UserType != EmUserType.OrgUser)
            {
                return FuncResult<LoginOutput>.Failed("登录类型非法");
            }

            var (org, orgApps) = await _orgRepository.GetOrgByOrgCodeAsync(request.SubjectCode);
            if (org == null)
            {
                return FuncResult<LoginOutput>.Failed("机构不存在");
            }

            if (org.Status != EmStatus.Enabled)
            {
                return FuncResult<LoginOutput>.Failed("机构状态异常");
            }

            if (orgApps.IsNullOrEmpty())
            {
                return FuncResult<LoginOutput>.Failed("客户端授权异常");
            }

            var appRelation = orgApps.SingleOrDefault(x => x.ClientAppId == request.AppId);
            if (appRelation == null)
            {
                return FuncResult<LoginOutput>.Failed("当前组织未获得授权");
            }

            if (appRelation.Status != EmStatus.Enabled)
            {
                return FuncResult<LoginOutput>.Failed("当前组织授权已过期");
            }

            var validateApp = await ValidateAppAsync(request.AppId);
            if (!validateApp.IsSuccess)
            {
                return FuncResult<LoginOutput>.Failed(validateApp.Message);
            }

            var validateUser = await ValidateUserAsync(request.UserType, request.UserName, request.Password);
            if (!validateUser.IsSuccess)
            {
                return FuncResult<LoginOutput>.Failed(validateUser.Message);
            }

            var output = await BuildLoginOutputAsync(request.AppId, validateUser.Data);
            return FuncResult<LoginOutput>.Success("login success", output);
        }
    }
}
