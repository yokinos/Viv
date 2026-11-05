using Azure.Core;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.Entity.Dto.Account;
using Viv.Apex.Core.Entity.Vo.Account;
using Viv.Apex.Core.IRepository;
using Viv.Contracts.Interface;
using Viv.Delusion;
using Viv.Delusion.Extension;
using Viv.Delusion.Magic;
using Viv.Delusion.Security;
using Viv.Elysia;
using Viv.Entity.Any;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;
using Viv.Redis;

namespace Viv.Apex.Core.Module.Login
{
    public class LoginImplBase
    {
        protected readonly ITokenService _tokenService;

        protected readonly IRedisService _redisService;

        protected readonly IVivContext _context;

        protected readonly IUserRepository _userRepository;

        protected readonly IClientAppRepository _clientAppRepository;

        public LoginImplBase(ITokenService tokenService, IRedisService redisService, IVivContext context,
            IUserRepository userRepository, IClientAppRepository clientAppRepository)
        {
            _tokenService = tokenService;
            _redisService = redisService;
            _context = context;
            _userRepository = userRepository;
            _clientAppRepository = clientAppRepository;
        }

        /// <summary>
        /// 生成 缓存Key（clientAppId 是主键，不是对外的 AppKey）
        /// </summary>
        /// <param name="clientAppId"></param>
        /// <param name="userType"></param>
        /// <param name="userId"></param>
        /// <returns></returns>
        public virtual string GetSessionKey(long clientAppId, EmUserType userType, long userId)
        {
            return $"LoginRefreshKey:{clientAppId}:{userType}:{userId}";
        }

        /// <summary>
        /// 生成Token、更新Redis、组装输出对象
        /// </summary>
        /// <param name="clientAppId">客户端应用主键（<c>AtClientApp.Id</c>），token claim 里放的就是它</param>
        /// <param name="user">登录用户</param>
        public virtual async Task<LoginOutput> BuildLoginOutputAsync(long clientAppId, AtUser user)
        {
            var tokenOptions = _tokenService.GetOptions();
            var newAccessToken = _tokenService.GenerateToken(new Contracts.Models.TokenPayload
            {
                AppId = clientAppId,
                UserId = user.Id,
                UserName = user.Name
            });

            // refresh token 是 30–45 天的长期凭证，必须用 CSPRNG；GenerateFastString 走的是 Random.Shared，
            // 状态可被推导，等于把长期会话交给伪随机数。字符池与长度保持不变，已签发的 token 不受影响。
            var newRefreshToken = StringMagic.GenerateSecureString(64);
            var redisSessionKey = GetSessionKey(clientAppId, user.UserType, user.Id);

            var newExpire = CacheTimeProvider.GetRandomDays(30, 45);
            var tokenValue = new RefreshTokenValue
            {
                AppId = clientAppId,
                UserId = user.Id,
                RefreshToken = newRefreshToken
            };

            await _redisService.AddAsync(redisSessionKey, tokenValue, newExpire);

            return new LoginOutput
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

        /// <summary>
        /// 校验客户端应用。入参是对外的 AppKey，返回的实体里带主键 ——
        /// 从这里往下（session key、token claim、外键）一律用 <c>clientApp.Id</c>，不要再传字符串。
        /// </summary>
        public virtual async Task<FuncResult<AtClientApp>> ValidateAppAsync(string? appKey)
        {
            if (string.IsNullOrWhiteSpace(appKey))
            {
                return FuncResult<AtClientApp>.Failed("缺少客户端 AppKey");
            }

            var clientApp = await _clientAppRepository.GetAppByAppKeyAsync(appKey);
            if (clientApp == null)
            {
                return FuncResult<AtClientApp>.Failed("客户端不存在");
            }

            if (clientApp.Status != EmStatus.Enabled)
            {
                return FuncResult<AtClientApp>.Failed("客户端已禁用");
            }

            return FuncResult<AtClientApp>.Success(clientApp);
        }

        public virtual async Task<FuncResult<AtUser>> ValidateUserAsync(long userId)
        {
            var user = await _userRepository.GetUserAsync(userId);
            if (user == null || user.Status != EmStatus.Enabled)
            {
                return FuncResult<AtUser>.Failed("账号异常");
            }

            return await ValidateUserAsync(user);
        }

        public virtual async Task<FuncResult<AtUser>> ValidateUserAsync(EmUserType userType, string userName, string password)
        {
            var user = await _userRepository.GetUserByPhoneAsync(userName, userType);
            if (user is null)
            {
                return FuncResult<AtUser>.Failed("账号或者密码错误");
            }

            // 兼容两代口令：PBKDF2（新）与 MD5(密码+盐)（旧）。旧格式验证通过后顺手升级。
            if (!PasswordHash.Verify(user.Password, user.Salt, password, out var needsRehash))
            {
                return FuncResult<AtUser>.Failed("账号或者密码错误");
            }

            if (needsRehash)
            {
                await UpgradePasswordHashAsync(user, password);
            }

            return await ValidateUserAsync(user);
        }

        /// <summary>
        /// 旧 MD5 口令登录成功后升级为 PBKDF2。升级失败不影响本次登录（密码已核对通过），下次登录再试。
        /// </summary>
        private async Task UpgradePasswordHashAsync(AtUser user, string password)
        {
            try
            {
                var (hash, salt) = PasswordHash.Hash(password);
                user.Password = hash;
                user.Salt = salt;
                await _userRepository.UpdateUserAsync(user);
            }
            catch (Exception)
            {
                // 写库失败就保持旧哈希；绝不能因为升级失败把已经通过校验的用户挡在门外
            }
        }

        public virtual async Task<FuncResult<AtUser>> ValidateUserAsync(AtUser user)
        {
            if (user.Status != EmStatus.Enabled)
            {
                return FuncResult<AtUser>.Failed("账号被禁用");
            }

            if (!user.IsSuperAdmin)
            {
                var roles = await _userRepository.GetUserRolesAsync(user.Id);
                if (roles.IsNullOrEmpty())
                {
                    return FuncResult<AtUser>.Failed("用户未绑定角色信息,无法登录");
                }

                if (!roles.Any(x => x.Status == EmStatus.Enabled))
                {
                    return FuncResult<AtUser>.Failed("用户绑定角色信息异常,无法登录");
                }
            }

            return FuncResult<AtUser>.Success(user);
        }

        public virtual async Task<FuncResult<LoginOutput>> RefreshTokenAsync(RefreshRequest request)
        {
            var validateApp = await ValidateAppAsync(request.AppKey);
            if (!validateApp.IsSuccess)
            {
                return FuncResult<LoginOutput>.Failed(validateApp.Message);
            }

            var clientAppId = validateApp.Data.Id;
            var redisSessionKey = GetSessionKey(clientAppId, request.UserType, request.UserId);
            var session = await _redisService.GetAsync<RefreshTokenValue>(redisSessionKey);

            if (session == null || session.RefreshToken != request.RefreshToken)
            {
                return FuncResult<LoginOutput>.Failed("登录凭证已失效，请重新登录");
            }

            var validateUser = await ValidateUserAsync(session.UserId);
            if (!validateUser.IsSuccess)
            {
                return FuncResult<LoginOutput>.Failed(validateUser.Message);
            }

            var output = await BuildLoginOutputAsync(clientAppId, validateUser.Data);
            return FuncResult<LoginOutput>.Success("success", output);
        }

        public virtual async Task<bool> LogoutAsync(LoginoutRequest request)
        {
            var userId = _context.UserId;
            if (userId <= 0)
            {
                return false;
            }

            var userType = request.UserType;
            var user = await _userRepository.GetUserAsync(userId);
            if (user != null)
            {
                userType = user.UserType;
            }

            var redisSessionKey = GetSessionKey(_context.AppId, userType, userId);
            if (await _redisService.RemoveAsync(redisSessionKey))
            {
                return true;
            }

            return !await _redisService.ExistAsync(redisSessionKey);
        }
    }
}
