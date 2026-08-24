using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.Entity.Dto.Account.Output;
using Viv.Apex.Core.IRepository;
using Viv.Contracts.Interface;
using Viv.Delusion.Magic;
using Viv.Elysia;
using Viv.Entity.Any;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;
using Viv.Redis;

namespace Viv.Apex.Core.Impl.Login
{
    public class ApexLoginBase
    {
        protected readonly ITokenService _tokenService;
        protected readonly IRedisService _redisService;
        protected readonly IVivContext _context;

        public ApexLoginBase(ITokenService tokenService, IRedisService redisService, IVivContext context)
        {
            _tokenService = tokenService;
            _redisService = redisService;
            _context = context;
        }

        /// <summary>
        /// 生成 缓存Key
        /// </summary>
        /// <param name="appId"></param>
        /// <param name="userType"></param>
        /// <param name="userId"></param>
        /// <returns></returns>
        public virtual string GetSessionKey(long appId, EmUserType userType, long userId)
        {
            return $"LoginRefreshKey:{appId}:{userType}:{userId}";
        }

        /// <summary>
        /// 生成Token、更新Redis、组装输出对象
        /// </summary>
        public virtual async Task<ApexLoginOutput> BuildLoginOutputAsync(long appId, AtUser user)
        {
            var tokenOptions = _tokenService.GetOptions();
            var newAccessToken = _tokenService.GenerateToken(new Contracts.Models.TokenPayload
            {
                AppId = appId,
                UserId = user.Id,
                UserName = user.Name
            });

            var newRefreshToken = StringMagic.GenerateFastString(64);
            var redisSessionKey = GetSessionKey(appId, user.UserType, user.Id);

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
