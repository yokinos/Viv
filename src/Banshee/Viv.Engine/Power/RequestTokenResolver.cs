using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Contracts.Options;
using Viv.Delusion;
using Viv.Delusion.Extension;

namespace Viv.Engine.Power
{
    public static class RequestTokenResolver
    {
        /// <summary>
        /// 从可信内部请求 Header 中获取上下文。
        /// 安全约束：x-viv-* 上下文头只有网关（或持有共享密钥的对等服务）签名后才可信，
        /// 否则直连下游的客户端可伪造头冒充任意租户/用户。
        /// 密钥取 EnvOption.InternalToken（缺省回落 TokenOption.SecretKey）；两者皆 null 时无法验签，按原行为信任头（该场景无租户数据）。
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
        /// 内部请求共享密钥。优先取 EnvOption.InternalToken（appsettings.json 的 VivOptions.EnvOption 显式配置，网关与所有服务配同一个值）；
        /// 未配置时回落到 TokenOption.SecretKey（向后兼容，匿名服务两者皆 null 则返回 null）。
        /// </summary>
        private static string? GetInternalSecret()
            => !string.IsNullOrWhiteSpace(VivEngine.VivOptions?.EnvOption?.InternalToken)
                ? VivEngine.VivOptions.EnvOption.InternalToken
                : VivConfigRegistry.Get<TokenOptions>()?.SecretKey;

        /// <summary>
        /// 签名有效期（秒）。网关签完后下游在秒级内收到，5 分钟足够抵消时钟偏差；
        /// 超过该窗口的请求视为重放攻击，下游拒绝。
        /// </summary>
        private const long MaxReplayAgeSeconds = 300;

        /// <summary>
        /// 对当前 x-viv-* 上下文头组计算 HMAC-SHA256 签名，网关认证后写入 x-request-token。
        /// 签名载荷包含 unix 时间戳（格式 {unixSeconds}:{base64Sig}），下游验签时校验时效，防止无限期重放。
        /// 无共享密钥时返回 null（不签名）。
        /// </summary>
        public static string? SignContextHeaders(IHeaderDictionary headers)
        {
            var secret = GetInternalSecret();
            if (string.IsNullOrWhiteSpace(secret))
            {
                return null;
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return timestamp.ToString(CultureInfo.InvariantCulture) + ":" + ComputeSignature(headers, secret, timestamp);
        }

        /// <summary>
        /// 校验 x-request-token 是否为持有共享密钥的一方对当前头组生成的签名，且时间戳未超时（<see cref="MaxReplayAgeSeconds"/>）。
        /// 旧格式（无时间戳前缀）或已超时的令牌一律拒绝。
        /// </summary>
        public static bool VerifySignature(IHeaderDictionary headers, string secret)
        {
            var provided = headers[VivRunDefine.InnerRequestTokenHeader].ToString();
            if (string.IsNullOrWhiteSpace(provided))
            {
                return false;
            }

            // 格式 {unixSeconds}:{base64Sig}（base64 字符集不含 ':'，首个冒号安全切分）
            var sep = provided.IndexOf(':');
            if (sep <= 0 || sep >= provided.Length - 1)
            {
                return false;
            }

            if (!long.TryParse(provided.AsSpan(0, sep), NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp))
            {
                return false;
            }

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp > MaxReplayAgeSeconds)
            {
                return false;
            }

            var expected = ComputeSignature(headers, secret, timestamp);
            return expected != null && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(provided.Substring(sep + 1)),
                Encoding.UTF8.GetBytes(expected));
        }

        /// <summary>
        /// 固定顺序拼接 4 个 x-viv-* 头值（缺失补空串）+ 签名时间戳，保证签名对头值顺序不敏感、对缺失值稳定。
        /// </summary>
        private static string? ComputeSignature(IHeaderDictionary headers, string secret, long timestamp)
        {
            var payload = string.Join('\n',
                headers[VivRunDefine.AppIdHeader].ToString(),
                headers[VivRunDefine.SubjectIdHeader].ToString(),
                headers[VivRunDefine.UserIdHeader].ToString(),
                headers[VivRunDefine.ServiceNameHeader].ToString(),
                timestamp.ToString(CultureInfo.InvariantCulture));

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
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

            // .NET 10 JwtBearer 只映射 sub → NameIdentifier（与网关读取模式保持一致，两种都兼容）
            var userIdText = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var appIdText = user.FindFirstValue(VivClaimTypes.AppId);

            // 与旧实现语义一致：AppId、UserId 必须有效且大于 0，否则视为无有效上下文
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

        /// <summary>
        /// 从请求 Header 中读取大于 0 的 long 值。
        /// </summary>
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
