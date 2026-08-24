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
using Viv.Delusion.Magic;
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

        public OrgUserLoginImpl(ITokenService tokenService, IRedisService redisService, IVivContext context, IUserRepository userRepository, IOrgRepository orgRepository,
            IClientAppRepository clientAppRepository)
            : base(tokenService, redisService, context)
        {
            _userRepository = userRepository;
            _orgRepository = orgRepository;
            _clientAppRepository = clientAppRepository;
        }

        public async Task<FuncResult<ApexLoginOutput>> LoginAsync(ApexLoginRequest request)
        {
            if (request.UserType != EmUserType.OrgUser)
            {
                return FuncResult<ApexLoginOutput>.Failed("登录类型非法");
            }

            // var org = await _orgRepository.GetAsync(request.SubjectCode);
            // 检查这个组织是否允许登录这个App 
            // 检查组织状态
            // 检查用户角色

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

            // 组装登录结果
            var output = await BuildLoginOutputAsync(request.AppId, user);
            return FuncResult<ApexLoginOutput>.Success("login success", output);
        }

        public Task<bool> LogoutAsync(ApexLoginoutRequest request)
        {
            throw new NotImplementedException();
        }

        public Task<FuncResult<ApexLoginOutput>> RefreshTokenAsync(ApexRefreshRequest request)
        {
            throw new NotImplementedException();
        }
    }
}
