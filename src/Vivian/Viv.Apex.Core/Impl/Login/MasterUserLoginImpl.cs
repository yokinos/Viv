using System;
using System.Numerics;
using System.Threading.Tasks;
using Viv.Apex.Core.Entity.Dto.Account.Output;
using Viv.Apex.Core.Entity.Dto.Account.Request;
using Viv.Apex.Core.Interface;
using Viv.Apex.Core.IRepository;
using Viv.Contracts.Attributes;
using Viv.Contracts.Interface;
using Viv.Delusion;
using Viv.Delusion.Magic;
using Viv.Elysia;
using Viv.Entity.Any;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;
using Viv.Redis;

namespace Viv.Apex.Core.Impl.Login
{
    [VivDependency(Tag = EmUserType.Master)]
    public class MasterUserLoginImpl : ApexLoginBase, ILoginContract, IDependency
    {
        private readonly IUserRepository _userRepository;

        private readonly IClientAppRepository _clientAppRepository;

        public MasterUserLoginImpl(ITokenService tokenService, IRedisService redisService, IVivContext context, IUserRepository userRepository, IClientAppRepository clientAppRepository)
            : base(tokenService, redisService, context)
        {
            _userRepository = userRepository;
            _clientAppRepository = clientAppRepository;
        }

        /// <summary>
        /// 登录
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async Task<FuncResult<ApexLoginOutput>> LoginAsync(ApexLoginRequest request)
        {
            if (request.UserType != EmUserType.Master)
            {
                return FuncResult<ApexLoginOutput>.Failed("登录类型非法");
            }

            var app = await _clientAppRepository.GetAsync(request.AppId);
            if (app == null)
            {
                return FuncResult<ApexLoginOutput>.Failed("不存在的客户端");
            }

            if (app.Status != EmStatus.Enabled)
            {
                return FuncResult<ApexLoginOutput>.Failed("客户端已禁用，不允许登录");
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

            // 组装登录结果
            var output = await BuildLoginOutputAsync(request.AppId, user);
            return FuncResult<ApexLoginOutput>.Success("login success", output);
        }

        /// <summary>
        /// 刷新令牌
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
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

            // 组装新的登录结果
            var output = await BuildLoginOutputAsync(request.AppId, user);
            return FuncResult<ApexLoginOutput>.Success("success", output);
        }

        /// <summary>
        /// 退出登录
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async Task<bool> LogoutAsync(ApexLoginoutRequest request)
        {
            var redisSessionKey = GetSessionKey(request.AppId, request.UserType, request.UserId);
            return await _redisService.RemoveAsync(redisSessionKey);
        }
    }
}