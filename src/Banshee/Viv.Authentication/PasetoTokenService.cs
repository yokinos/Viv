using Paseto;
using Paseto.Builder;
using Paseto.Cryptography;
using Paseto.Validators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Viv.Authentication
{
    /// <summary>
    /// PASETO令牌服务实现（基于官方维护的Paseto.Core包）
    /// </summary>
    public class PasetoTokenService : ITokenService
    {
        private readonly TokenOptions _options;
        private readonly byte[] _secretKey;

        public PasetoTokenService(TokenOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrEmpty(_options.SecretKey))
            {
                throw new ArgumentNullException(nameof(options.SecretKey), "PASETO加密密钥不能为空！");
            }

            // PASETO V2版本要求密钥至少32字节，不足则补全
            _secretKey = Encoding.UTF8.GetBytes(_options.SecretKey);
            if (_secretKey.Length < 32)
            {
                var tempKey = new byte[32];
                Array.Copy(_secretKey, tempKey, _secretKey.Length);
                _secretKey = tempKey;
            }
        }

        public string GenerateToken(TokenPayload payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));

            // 构建PASETO Payload
            var pasetoBuilder = new PasetoBuilder()
                .UseVersion(PasetoVersion.V2) // V2版本（推荐，支持加密+签名）
                .UsePurpose(PasetoPurpose.Local) // Local=加密模式，Public=签名模式
                .WithSecretKey(_secretKey)
                .AddClaim("sub", payload.UserId)
                .AddClaim("name", payload.UserName)
                .AddClaim("iss", _options.Issuer)
                .AddClaim("aud", _options.Audience)
                .AddClaim("exp", DateTime.UtcNow.AddMinutes(_options.ExpireMinutes))
                .AddClaim("iat", DateTime.UtcNow);

            // 添加角色（数组形式）
            pasetoBuilder.AddClaim("roles", payload.Roles.ToArray());

            // 添加自定义扩展字段
            foreach (var kv in payload.Extensions)
            {
                pasetoBuilder.AddClaim(kv.Key, kv.Value);
            }

            return pasetoBuilder.Build();
        }

        public bool ValidateToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;

            try
            {
                var validator = new PasetoValidator();
                var validationResult = validator.Validate(token, new PasetoValidationParameters
                {
                    SecretKey = _secretKey,
                    ValidIssuer = _options.Issuer,
                    ValidAudience = _options.Audience,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                });

                return validationResult.IsValid;
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
                throw new InvalidTokenException("PASETO令牌无效或已过期！");
            }

            try
            {
                var pasetoToken = PasetoParser.Parse(token);
                var payloadDict = pasetoToken.Payload.ToDictionary();

                // 解析核心字段
                var payload = new TokenPayload
                {
                    UserId = payloadDict["sub"].ToString() ?? string.Empty,
                    UserName = payloadDict["name"].ToString() ?? string.Empty,
                    Roles = ((IEnumerable<object>)payloadDict["roles"]).Select(r => r.ToString() ?? string.Empty).ToList()
                };

                // 解析自定义扩展字段（排除内置字段）
                var builtInFields = new[] { "sub", "name", "roles", "iss", "aud", "exp", "iat" };
                foreach (var kv in payloadDict.Where(kv => !builtInFields.Contains(kv.Key)))
                {
                    payload.Extensions.Add(kv.Key, kv.Value.ToString() ?? string.Empty);
                }

                return payload;
            }
            catch (Exception ex)
            {
                throw new InvalidTokenException("解析PASETO令牌失败！", ex);
            }
        }
    }
}