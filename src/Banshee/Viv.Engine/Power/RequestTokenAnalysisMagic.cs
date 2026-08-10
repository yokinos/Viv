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
    public class RequestTokenAnalysisMagic : IDependency
    {
        public const string AppIdHeader = "x-viv-appId"; // 这个指的是客户端的AppId
        public const string SubjectIdHeader = "x-viv-subjectId";
        public const string UserIdHeader = "x-viv-userId";
        public const string ServiceNameHeader = "x-viv-serviceName"; // 这个指的是服务的名称，比如 viv.apex.api
        public const string InnerRequestTokenHeader = "x-request-token"; // 这个指的是内部请求的 Token，于验证内部请求的合法性，设置到appsettings.json 中设置。


        /// <summary>
        /// 从可信内部请求 Header 中获取上下文。
        /// 安全约束：x-viv-* 上下文头只有网关（或持有共享密钥的对等服务）签名后才可信，
        /// 否则直连下游的客户端可伪造头冒充任意租户/用户。
        /// 未配置 TokenOption（匿名服务，如 hertalink）无法验签，按原行为信任头（该场景无租户数据）。
        /// </summary>
        public VivContextContent? GetContextFromHeaders(HttpContext context)
        {
            var secret = GetInternalSecret();
            if (!string.IsNullOrWhiteSpace(secret) && !VerifySignature(context.Request.Headers, secret))
            {
                return null;
            }

            if (!TryGetPositiveLong(context, AppIdHeader, out var appId))
            {
                return null;
            }

            if (!TryGetPositiveLong(context, UserIdHeader, out var userId))
            {
                return null;
            }

            TryGetPositiveLong(context, SubjectIdHeader, out var subjectId);

            return new VivContextContent
            {
                AppId = appId,
                SubjectId = subjectId,
                UserId = userId
            };
        }

        /// <summary>
        /// 内部请求共享密钥（TokenOption.SecretKey）。
        /// 网关与持有 TokenOption 的服务必须一致才能互通签名；匿名服务（TokenOption 为 null）返回 null。
        /// </summary>
        private static string? GetInternalSecret()
            => VivConfigRegistry.Get<TokenOptions>()?.SecretKey;

        /// <summary>
        /// 对当前 x-viv-* 上下文头组计算 HMAC-SHA256 签名，网关认证后写入 x-request-token。
        /// 无共享密钥时返回 null（不签名）。
        /// </summary>
        public static string? SignContextHeaders(IHeaderDictionary headers)
        {
            var secret = GetInternalSecret();
            return string.IsNullOrWhiteSpace(secret) ? null : ComputeSignature(headers, secret);
        }

        /// <summary>
        /// 校验 x-request-token 是否为持有共享密钥的一方对当前头组生成的签名。
        /// </summary>
        public static bool VerifySignature(IHeaderDictionary headers, string secret)
        {
            var provided = headers[InnerRequestTokenHeader].ToString();
            if (string.IsNullOrWhiteSpace(provided))
            {
                return false;
            }

            var expected = ComputeSignature(headers, secret);
            return expected != null && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(provided),
                Encoding.UTF8.GetBytes(expected));
        }

        /// <summary>
        /// 固定顺序拼接 4 个 x-viv-* 头值（缺失补空串），保证签名对头值顺序不敏感、对缺失值稳定。
        /// </summary>
        private static string? ComputeSignature(IHeaderDictionary headers, string secret)
        {
            var payload = string.Join('\n',
                headers[AppIdHeader].ToString(),
                headers[SubjectIdHeader].ToString(),
                headers[UserIdHeader].ToString(),
                headers[ServiceNameHeader].ToString());

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }

        /// <summary>
        /// 从已验证的 JWT principal 中获取上下文（直连下游、绕过网关的场景）。
        /// token 由管道中更早的 UseAuthentication(JwtBearer) 完成验签并填充 context.User，
        /// 此处不再二次验签——只提取 claims，与网关认证后回填的 x-viv-* 头契约一致。
        /// </summary>
        public Task<VivContextContent?> GetContextFromTokenAsync(HttpContext context)
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

            var tenantIdText = user.FindFirstValue(VivClaimTypes.TenantId);
            long.TryParse(tenantIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tenantId);

            return Task.FromResult<VivContextContent?>(new VivContextContent
            {
                AppId = appId,
                SubjectId = tenantId,
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
