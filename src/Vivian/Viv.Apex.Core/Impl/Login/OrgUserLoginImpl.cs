using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.Entity.Dto.Account.Output;
using Viv.Apex.Core.Entity.Dto.Account.Request;
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
    [VivDependency(Tag = EmUserType.OrgUser)]
    public class OrgUserLoginImpl : ApexLoginBase, ILoginContract, IDependency
    {
        private readonly IUserRepository _userRepository;
        private readonly IOrgRepository _orgRepository;
        private readonly IClientAppRepository _clientAppRepository;

        public OrgUserLoginImpl(ITokenService tokenService, IRedisService redisService, IVivContext context, IUserRepository userRepository,
            IOrgRepository orgRepository, IClientAppRepository clientAppRepository)
            : base(tokenService, redisService, context)
        {
            _userRepository = userRepository;
            _orgRepository = orgRepository;
        }

        public async Task<FuncResult<ApexLoginOutput>> LoginAsync(ApexLoginRequest request)
        {
            if (request.UserType != EmUserType.OrgUser)
            {
                return FuncResult<ApexLoginOutput>.Failed("登录类型非法");
            }

            var (org, orgApps) = await _orgRepository.GetOrgByOrgCodeAsync(request.SubjectCode);
            if (org == null)
            {
                return FuncResult<ApexLoginOutput>.Failed("机构不存在");
            }

            if (org.Status != EmStatus.Enabled)
            {
                return FuncResult<ApexLoginOutput>.Failed("机构状态异常");
            }

            if (orgApps.IsNullOrEmpty())
            {
                return FuncResult<ApexLoginOutput>.Failed("客户端授权异常");
            }

            var appRelation = orgApps.SingleOrDefault(x => x.ClientAppId == request.AppId);
            if (appRelation == null)
            {
                return FuncResult<ApexLoginOutput>.Failed("当前组织未获得授权");
            }

            if (appRelation.Status != EmStatus.Enabled)
            {
                return FuncResult<ApexLoginOutput>.Failed("当前组织授权已过期");
            }

            var clientApp = await _clientAppRepository.GetAsync(request.AppId);
            if (clientApp == null)
            {
                return FuncResult<ApexLoginOutput>.Failed("客户端不存在");
            }

            if (clientApp.Status != EmStatus.Enabled)
            {
                return FuncResult<ApexLoginOutput>.Failed("客户端已禁用");
            }

            var user = await _userRepository.GetByPhoneAsync(request.UserName, request.UserType);
            if (user is null)
            {
                return FuncResult<ApexLoginOutput>.Failed("账号或者密码错误");
            }

            var saltPwd = EncryptMagic.HashMd5($"{request.Password}{user.Salt}");
            if (saltPwd != user.Password)
            {
                return FuncResult<ApexLoginOutput>.Failed("账号或者密码错误");
            }

            if (user.Status != EmStatus.Enabled)
            {
                return FuncResult<ApexLoginOutput>.Failed("账号被禁用");
            }

            if (!user.IsSuperAdmin)
            {
                var roles = await _userRepository.GetAtUserRoleListAsync(user.Id);
                if (roles.IsNullOrEmpty())
                {
                    return FuncResult<ApexLoginOutput>.Failed("用户未绑定角色信息,无法登录");
                }

                if (!roles.Any(x => x.Status == EmStatus.Enabled))
                {
                    return FuncResult<ApexLoginOutput>.Failed("用户绑定角色信息异常,无法登录");
                }
            }

            // 组装登录结果
            var output = await BuildLoginOutputAsync(request.AppId, user);
            return FuncResult<ApexLoginOutput>.Success("login success", output);
        }

        public async Task<bool> LogoutAsync(ApexLoginoutRequest request)
        {
            var redisSessionKey = GetSessionKey(request.AppId, request.UserType, request.UserId);
            return await _redisService.RemoveAsync(redisSessionKey);
        }

        public async Task<FuncResult<ApexLoginOutput>> RefreshTokenAsync(ApexRefreshRequest request)
        {
            var redisSessionKey = GetSessionKey(request.AppId, request.UserType, request.UserId);
            var session = await _redisService.GetAsync<RefreshTokenValue>(redisSessionKey);

            // 校验会话存在 + token匹配
            if (session == null || session.RefreshToken != request.RefreshToken)
            {
                return FuncResult<ApexLoginOutput>.Failed("登录凭证已失效，请重新登录");
            }

            // 查找用户（校验账号状态）
            var user = await _userRepository.GetAsync(session.UserId);
            if (user == null || user.Status != EmStatus.Enabled)
            {
                return FuncResult<ApexLoginOutput>.Failed("账号异常，请重新登录");
            }

            if (!user.IsSuperAdmin)
            {
                var roles = await _userRepository.GetAtUserRoleListAsync(user.Id);
                if (roles.IsNullOrEmpty())
                {
                    return FuncResult<ApexLoginOutput>.Failed("用户未绑定角色信息,无法登录");
                }

                if (!roles.Any(x => x.Status == EmStatus.Enabled))
                {
                    return FuncResult<ApexLoginOutput>.Failed("用户绑定角色信息异常,无法登录");
                }
            }

            // 组装新的登录结果
            var output = await BuildLoginOutputAsync(request.AppId, user);
            return FuncResult<ApexLoginOutput>.Success("success", output);
        }
    }
}
