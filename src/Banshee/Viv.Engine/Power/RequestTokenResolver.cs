using Microsoft.AspNetCore.Http;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Viv.Contracts;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;

namespace Viv.Engine.Power
{
    public static class RequestTokenResolver
    {
        /// <summary>
        /// 从可信内部请求 Header 中获取上下文。
        /// 安全约束：x-viv-* 上下文头只有网关（或持有共享密钥的对等服务）签名后才可信。
        /// 密钥只取 EnvOption.InternalToken；未配置时无法验签，按原行为信任头（该场景无租户数据）。
        /// </summary>
        public static VivContextContent? GetContextFromHeaders(HttpContext context)
        {
            var secret = GetInternalSecret();
            if (!string.IsNullOrWhiteSpace(secret) && !VerifySignature(context.Request.Headers, secret))
            {
                return null;
            }

            if (!TryGetPositiveLong(context, VivRunDefine.AppIdHeader, out var appId))
            {
                return null;
            }

            if (!TryGetPositiveLong(context, VivRunDefine.UserIdHeader, out var userId))
            {
                return null;
            }

            TryGetPositiveLong(context, VivRunDefine.SubjectIdHeader, out var subjectId);

            return new VivContextContent
            {
                AppId = appId,
                SubjectId = subjectId,
                UserId = userId
            };
        }

        /// <summary>
        /// 内部请求共享密钥，只取 EnvOption.InternalToken。不回落 JWT SecretKey。
        /// </summary>
        private static string? GetInternalSecret()
            => string.IsNullOrWhiteSpace(VivEngine.VivOptions?.EnvOption?.InternalToken)
                ? null
                : VivEngine.VivOptions.EnvOption.InternalToken;

        /// <summary>
        /// 对当前 x-viv-* 上下文头组计算 HMAC-SHA256 签名，网关认证后写入 x-request-token。
        /// 无 InternalToken 时返回 null（不签名）。
        /// </summary>
        public static string? SignContextHeaders(IHeaderDictionary headers)
        {
            var secret = GetInternalSecret();
            if (string.IsNullOrWhiteSpace(secret))
            {
                return null;
            }

            return VivRequestToken.Sign(
                headers[VivRunDefine.AppIdHeader].ToString(),
                headers[VivRunDefine.SubjectIdHeader].ToString(),
                headers[VivRunDefine.UserIdHeader].ToString(),
                headers[VivRunDefine.ServiceNameHeader].ToString(),
                secret);
        }

        /// <summary>
        /// 校验 x-request-token：签名匹配、未超时、且时间戳不在未来（允许 <see cref="VivRequestToken.MaxFutureSkewSeconds"/> 偏差）。
        /// </summary>
        public static bool VerifySignature(IHeaderDictionary headers, string secret)
        {
            return VivRequestToken.TryVerify(
                headers[VivRunDefine.InnerRequestTokenHeader].ToString(),
                headers[VivRunDefine.AppIdHeader].ToString(),
                headers[VivRunDefine.SubjectIdHeader].ToString(),
                headers[VivRunDefine.UserIdHeader].ToString(),
                headers[VivRunDefine.ServiceNameHeader].ToString(),
                secret);
        }

        /// <summary>
        /// 从已验证的 JWT principal 中获取上下文（直连下游、绕过网关的场景）。
        /// token 由管道中更早的 UseAuthentication(JwtBearer) 完成验签并填充 context.User，
        /// 此处不再二次验签——只提取 claims，与网关认证后回填的 x-viv-* 头契约一致。
        /// </summary>
        public static Task<VivContextContent?> GetContextFromTokenAsync(HttpContext context)
        {
            var user = context.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                return Task.FromResult<VivContextContent?>(null);
            }

            var userIdText = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var appIdText = user.FindFirstValue(VivClaimTypes.AppId);

            if (!long.TryParse(userIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId) || userId <= 0
                || !long.TryParse(appIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId) || appId <= 0)
            {
                return Task.FromResult<VivContextContent?>(null);
            }

            var subjectIdText = user.FindFirstValue(VivClaimTypes.SubjectId);
            long.TryParse(subjectIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var subjectId);

            return Task.FromResult<VivContextContent?>(new VivContextContent
            {
                AppId = appId,
                SubjectId = subjectId,
                UserId = userId
            });
        }

        private static bool TryGetPositiveLong(HttpContext context, string headerName, out long value)
        {
            value = 0;

            if (!context.Request.Headers.TryGetValue(headerName, out var headerValue))
            {
                return false;
            }

            var text = headerValue.ToString();

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                value = 0;
                return false;
            }

            if (value <= 0)
            {
                value = 0;
                return false;
            }

            return true;
        }
    }
}
