using System;
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
    [VivDependency(Tag = (int)EmUserType.Master)]
    public class MasterUserLoginImpl : ILoginContract, IDependency
    {
        private readonly IUserRepository _userRepository;
        private readonly ITokenService _tokenService;
        private readonly IRedisService _redisService;

        private const string RefreshTokenSessionKeyPrefix = "rt:apex:";

        public MasterUserLoginImpl(IUserRepository userRepository, ITokenService tokenService, IRedisService redisService)
        {
            _userRepository = userRepository;
            _tokenService = tokenService;
            _redisService = redisService;
        }

        private string GetSessionKey(long appId, long userId)
        {
            return $"{RefreshTokenSessionKeyPrefix}{appId}:{userId}";
        }

        public async Task<FuncResult<ApexLoginOutput>> LoginAsync(ApexLoginRequest request)
        {
            if (!request.UserType.HasValue || request.UserType != EmUserType.Master)
            {
                return FuncResult<ApexLoginOutput>.Failed("登录类型非法");
            }

            var user = await _userRepository.GetByPhoneAsync(request.UserName, request.UserType.Value);
            if (user is null)
            {
                return FuncResult<ApexLoginOutput>.Failed("账号或者密码错误");
            }

            var saltPwd = EncryptMagic.HashMd5($"{request.Password}{user.Salt}");
            if (saltPwd != user.Password)
            {
                return FuncResult<ApexLoginOutput>.Failed("账号或者密码错误");
            }

            if (user.Status != EmStatus.Normal)
            {
                return FuncResult<ApexLoginOutput>.Failed("账号被禁用");
            }

            // 组装登录结果
            var output = await BuildLoginOutputAsync(request.AppId, user);
            return FuncResult<ApexLoginOutput>.Success("login success", output);
        }

        public async Task<FuncResult<ApexLoginOutput>> RefreshTokenAsync(ApexRefreshRequest request)
        {
            string redisSessionKey = GetSessionKey(request.AppId, request.UserId);
            var session = await _redisService.GetAsync<RefreshTokenValue>(redisSessionKey);

            // 校验会话存在 + token匹配
            if (session == null || session.RefreshToken != request.RefreshToken)
            {
                return FuncResult<ApexLoginOutput>.Failed("登录凭证已失效，请重新登录");
            }

            // 查找用户（校验账号状态）
            var user = await _userRepository.GetAsync(session.UserId);
            if (user == null || user.Status != EmStatus.Normal)
            {
                return FuncResult<ApexLoginOutput>.Failed("账号异常，请重新登录");
            }

            // 组装新的登录结果（内部会自动更新 Redis 并处理旧 Token 宽限期）
            var output = await BuildLoginOutputAsync(request.AppId, user);
            return FuncResult<ApexLoginOutput>.Success("success", output);
        }

        public async Task<bool> LogoutAsync(ApexLoginoutRequest request)
        {
            return false;
        }

        /// <summary>
        /// 提取公共逻辑：生成Token、更新Redis、组装输出对象
        /// </summary>
        private async Task<ApexLoginOutput> BuildLoginOutputAsync(long appId, AtUser user)
        {
            var tokenOptions = _tokenService.GetOptions();
            var newAccessToken = _tokenService.GenerateToken(new Contracts.Models.TokenPayload
            {
                AppId = appId,
                UserId = user.Id,
                UserName = user.Name
            });

            var newRefreshToken = StringMagic.GenerateFastString(64);
            var redisSessionKey = GetSessionKey(appId, user.Id);

            var newExpire = CacheTimeProvider.GetRandomDays(30, 45);
            var tokenValue = new RefreshTokenValue
            {
                AppId = appId,
                UserId = user.Id,
                RefreshToken = newRefreshToken
            };

            await _redisService.AddAsync(redisSessionKey, tokenValue, newExpire);

            return new ApexLoginOutput
            {
                AccessToken = newAccessToken,
                AccessTokenExpires = DateTime.UtcNow.AddMinutes(tokenOptions.ExpireMinutes),
                RefreshToken = newRefreshToken,
                RefreshTokenExpires = DateTime.UtcNow.AddDays(newExpire.TotalDays),
                AvatarUrl = user.AvatarUrl,
                Name = user.Name,
                NickName = user.NickName,
                Phone = user.Phone,
                UserId = user.Id,
            };
        }
    }
}