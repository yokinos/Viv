using Microsoft.IdentityModel.Tokens;
using System;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Contracts.Options;
using Viv.Delusion;
using Viv.Delusion.Extension;

namespace Viv.Sandrone.Impl
{
    /// <summary>
    /// JWT令牌服务实现（基于微软官方包）
    /// </summary>
    public class JwtTokenService : ITokenService
    {
        // JwtSecurityTokenHandler 线程安全（ValidateToken/ReadJwtToken），缓存避免每请求 new 分配
        private static readonly JwtSecurityTokenHandler Handler = new();

        private readonly TokenOptions _options;
        private readonly SymmetricSecurityKey _securityKey;
        private readonly TokenValidationParameters _validationParameters;

        public JwtTokenService()
        {
            _options = VivConfigRegistry.Get<TokenOptions>() ?? new TokenOptions(); ;
            if (string.IsNullOrEmpty(_options.SecretKey))
            {
                throw new ArgumentNullException(nameof(_options.SecretKey), "JWT签名密钥不能为空！");
            }

            // 初始化对称加密密钥
            _securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));

            // 验证参数一次性构建并复用（ValidateToken/ParseToken 共用，避免每请求 new TokenValidationParameters）
            _validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _options.Issuer,
                ValidateAudience = true,
                ValidAudience = _options.Audience,
                ValidateLifetime = true,
                IssuerSigningKey = _securityKey,
                ClockSkew = TimeSpan.Zero // 关闭时钟偏移容错，严格校验过期时间
            };
        }

        public string GenerateToken(TokenPayload payload)
        {
            ArgumentNullException.ThrowIfNull(payload);

            // 构建Claims（JWT载荷）
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, payload.UserId.ToString()),
                new Claim(JwtRegisteredClaimNames.Name, payload.UserName),
                new Claim(JwtRegisteredClaimNames.Iss, _options.Issuer),
                new Claim(JwtRegisteredClaimNames.Aud, _options.Audience),
                new Claim(JwtRegisteredClaimNames.Exp, DateTimeOffset.UtcNow.AddMinutes(_options.ExpireMinutes).ToUnixTimeSeconds().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
            };

            // Viv 上下文 Claims：网关验签后透传给下游的 x-viv-appId / x-viv-subjectId 头
            if (payload.AppId > 0)
            {
                claims.Add(new Claim(VivClaimTypes.AppId, payload.AppId.ToString(CultureInfo.InvariantCulture)));
            }

            if (payload.TenantId > 0)
            {
                claims.Add(new Claim(VivClaimTypes.TenantId, payload.TenantId.ToString(CultureInfo.InvariantCulture)));
            }

            // 添加角色Claims
            foreach (var role in payload.Roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            // 添加自定义扩展字段
            foreach (var kv in payload.Extensions)
            {
                claims.Add(new Claim(kv.Key, kv.Value));
            }

            // 生成JWT Token
            var credentials = new SigningCredentials(_securityKey, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer: _options.Issuer,
                audience: _options.Audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_options.ExpireMinutes),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public bool ValidateToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;

            try
            {
                // 验证Token（验证失败会抛出异常）
                Handler.ValidateToken(token, _validationParameters, out _);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public TokenPayload ParseToken(string token)
        {
            try
            {
                // 单次验证：ValidateToken 内部完成验签+过期校验，成功即取回已验证的 JwtSecurityToken，
                // 不再二次 ValidateToken + ReadJwtToken（旧实现每次 Parse 都做 2 次验证、分配 2 个 handler）。
                _ = Handler.ValidateToken(token, _validationParameters, out var validatedToken);
                if (validatedToken is not JwtSecurityToken jwtToken)
                {
                    throw new InvalidTokenException("JWT令牌格式不正确！");
                }

                // 解析核心字段
                var payload = new TokenPayload
                {
                    UserId = (jwtToken.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value).As<long>(),
                    UserName = jwtToken.Claims.First(c => c.Type == JwtRegisteredClaimNames.Name).Value,
                    Roles = jwtToken.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList()
                };

                // 解析 Viv 上下文 Claims（AppId / TenantId），缺失时默认 0
                payload.AppId = jwtToken.Claims.FirstOrDefault(c => c.Type == VivClaimTypes.AppId)?.Value.As<long>() ?? 0;
                payload.TenantId = jwtToken.Claims.FirstOrDefault(c => c.Type == VivClaimTypes.TenantId)?.Value.As<long>() ?? 0;

                // 解析自定义扩展字段（排除内置Claim）
                var builtInClaims = new[] { JwtRegisteredClaimNames.Sub, JwtRegisteredClaimNames.Name, ClaimTypes.Role, JwtRegisteredClaimNames.Iss, JwtRegisteredClaimNames.Aud, JwtRegisteredClaimNames.Exp, JwtRegisteredClaimNames.Iat, VivClaimTypes.AppId, VivClaimTypes.TenantId };
                foreach (var claim in jwtToken.Claims.Where(c => !builtInClaims.Contains(c.Type)))
                {
                    payload.Extensions.Add(claim.Type, claim.Value);
                }

                return payload;
            }
            catch (InvalidTokenException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 验签/过期/格式等任一失败都在这里兜成 InvalidTokenException（对外契约不变）
                throw new InvalidTokenException("JWT令牌无效或已过期！", ex);
            }
        }

        public TokenOptions GetOptions()
        {
            return _options;
        }
    }
}