using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.Entity.Dto.Account;
using Viv.Apex.Core.Entity.Vo.Account;
using Viv.Apex.Core.Interface;
using Viv.Apex.Core.IRepository;
using Viv.Apex.Core.Repository;
using Viv.Contracts.Attributes;
using Viv.Contracts.Interface;
using Viv.Delusion;
using Viv.Entity.Enums;
using Viv.Redis;

namespace Viv.Apex.Core.Impl.Login
{
    [VivDependency(EmUserType.CompanyUser)]
    public class CompanyUserLoginImpl : LoginImplBase, ILoginContract, IDependency
    {
        private readonly ICompanyRepository _companyRepository;

        public CompanyUserLoginImpl(ITokenService tokenService, IRedisService redisService, IVivContext context, IUserRepository userRepository,
            IClientAppRepository clientAppRepository, ICompanyRepository companyRepository)
            : base(tokenService, redisService, context, userRepository, clientAppRepository)
        {
            _companyRepository = companyRepository;
        }

        public async Task<FuncResult<LoginOutput>> LoginAsync(LoginRequest request)
        {
            if (request.UserType != EmUserType.CompanyUser)
            {
                return FuncResult<LoginOutput>.Failed("登录类型非法");
            }

            var tenant = await _companyRepository.GetCompanyAsync(request.SubjectCode);
            if (tenant is null)
            {
                return FuncResult<LoginOutput>.Failed("集团不存在");
            }

            if (tenant.Status != EmStatus.Enabled)
            {
                return FuncResult<LoginOutput>.Failed("集团状态异常");
            }

            var companyApps = await _companyRepository.GetCompanyAppsAsync(tenant.Id);
            if (companyApps.IsNullOrEmpty())
            {
                return FuncResult<LoginOutput>.Failed("客户端授权异常");
            }

            var appRelation = companyApps.SingleOrDefault(x => x.ClientAppId == request.AppId);
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

            return FuncResult<LoginOutput>.Success();
        }
    }
}
