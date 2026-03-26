using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Viv.Contracts.Exceptions;
using Viv.Vva;
using Viv.Vva.Extension;

namespace Viv.Authentication
{
    /// <summary>
    /// JWT令牌服务实现（基于微软官方包）
    /// </summary>
    public class JwtTokenService : ITokenService
    {
        private readonly TokenOptions _options;
        private readonly SymmetricSecurityKey _securityKey;

        public JwtTokenService()
        {
            _options = VivConfigRegistry.Get<TokenOptions>() ?? new TokenOptions(); ;
            if (string.IsNullOrEmpty(_options.SecretKey))
            {
                throw new ArgumentNullException(nameof(_options.SecretKey), "JWT签名密钥不能为空！");
            }

            // 初始化对称加密密钥
            _securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
        }

        public string GenerateToken(TokenPayload payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));

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
                var tokenHandler = new JwtSecurityTokenHandler();
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = _options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = _options.Audience,
                    ValidateLifetime = true,
                    IssuerSigningKey = _securityKey,
                    ClockSkew = TimeSpan.Zero // 关闭时钟偏移容错，严格校验过期时间
                };

                // 验证Token（验证失败会抛出异常）
                tokenHandler.ValidateToken(token, validationParameters, out _);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public TokenPayload ParseToken(string token)
        {
            if (!ValidateToken(token))
            {
                throw new InvalidTokenException("JWT令牌无效或已过期！");
            }

            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwtToken = tokenHandler.ReadJwtToken(token);

                // 解析核心字段
                var payload = new TokenPayload
                {
                    UserId = (jwtToken.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value).As<long>(),
                    UserName = jwtToken.Claims.First(c => c.Type == JwtRegisteredClaimNames.Name).Value,
                    Roles = jwtToken.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList()
                };

                // 解析自定义扩展字段（排除内置Claim）
                var builtInClaims = new[] { JwtRegisteredClaimNames.Sub, JwtRegisteredClaimNames.Name, ClaimTypes.Role, JwtRegisteredClaimNames.Iss, JwtRegisteredClaimNames.Aud, JwtRegisteredClaimNames.Exp, JwtRegisteredClaimNames.Iat };
                foreach (var claim in jwtToken.Claims.Where(c => !builtInClaims.Contains(c.Type)))
                {
                    payload.Extensions.Add(claim.Type, claim.Value);
                }

                return payload;
            }
            catch (Exception ex)
            {
                throw new InvalidTokenException("解析JWT令牌失败！", ex);
            }
        }
    }
}