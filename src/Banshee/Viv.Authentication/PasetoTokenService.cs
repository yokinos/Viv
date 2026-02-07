//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using Paseto;
//using Paseto.Builder;
//using Viv.Contracts.Exceptions;

//namespace Viv.Authentication
//{
//    public class PasetoTokenService : ITokenService
//    {
//        private readonly TokenOptions _options;
//        private readonly byte[] _secretKey;

//        public PasetoTokenService(TokenOptions options)
//        {
//            _options = options ?? throw new ArgumentNullException(nameof(options));

//            if (string.IsNullOrEmpty(_options.SecretKey))
//                throw new ArgumentException("密钥不能为空", nameof(options.SecretKey));

//            // 确保密钥是32字节
//            var key = Encoding.UTF8.GetBytes(_options.SecretKey);
//            if (key.Length < 32)
//            {
//                // 填充到32字节
//                var paddedKey = new byte[32];
//                Array.Copy(key, paddedKey, Math.Min(key.Length, 32));
//                // 如果还不够，用固定值填充
//                for (int i = key.Length; i < 32; i++)
//                    paddedKey[i] = 0x42; // 任意填充值
//                _secretKey = paddedKey;
//            }
//            else if (key.Length > 32)
//            {
//                // 截断到32字节
//                _secretKey = new byte[32];
//                Array.Copy(key, _secretKey, 32);
//            }
//            else
//            {
//                _secretKey = key;
//            }
//        }

//        public string GenerateToken(TokenPayload payload)
//        {
//            if (payload == null) throw new ArgumentNullException(nameof(payload));
//            if (string.IsNullOrEmpty(payload.UserId))
//                throw new ArgumentException("UserId不能为空", nameof(payload.UserId));

//            var claims = new Dictionary<string, object>
//            {
//                ["sub"] = payload.UserId,
//                ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
//                ["exp"] = DateTimeOffset.UtcNow.AddMinutes(_options.ExpireMinutes).ToUnixTimeSeconds()
//            };

//            if (!string.IsNullOrEmpty(_options.Issuer))
//                claims["iss"] = _options.Issuer;

//            if (!string.IsNullOrEmpty(_options.Audience))
//                claims["aud"] = _options.Audience;

//            if (!string.IsNullOrEmpty(payload.UserName))
//                claims["name"] = payload.UserName;

//            if (payload.Roles != null && payload.Roles.Any())
//                claims["roles"] = payload.Roles;

//            if (payload.Extensions != null)
//            {
//                foreach (var ext in payload.Extensions)
//                    if (!string.IsNullOrEmpty(ext.Key))
//                        claims[ext.Key] = ext.Value;
//            }

//            return PasetoBuilder.EncodeLocal(_secretKey, claims, _options.ExpireMinutes * 60);
//        }

//        public bool ValidateToken(string token)
//        {
//            if (string.IsNullOrEmpty(token)) return false;

//            try
//            {
//                var result = PasetoBuilder.DecodeLocal(token, _secretKey);
//                return result != null;
//            }
//            catch
//            {
//                return false;
//            }
//        }

//        public TokenPayload ParseToken(string token)
//        {
//            if (string.IsNullOrEmpty(token))
//                throw new ArgumentException("Token不能为空", nameof(token));

//            try
//            {
//                var result = PasetoBuilder.DecodeLocal(token, _secretKey);

//                if (result == null)
//                    throw new InvalidTokenException("令牌无效");

//                var payload = new TokenPayload
//                {
//                    UserId = GetStringValue(result, "sub") ?? string.Empty,
//                    UserName = GetStringValue(result, "name") ?? string.Empty,
//                    Roles = new List<string>(),
//                    Extensions = new Dictionary<string, string>()
//                };

//                // 解析角色
//                if (result.TryGetValue("roles", out var rolesObj))
//                {
//                    if (rolesObj is IEnumerable<object> rolesEnumerable)
//                        payload.Roles.AddRange(rolesEnumerable.Select(r => r?.ToString() ?? ""));
//                    else if (rolesObj is string roleStr)
//                        payload.Roles.Add(roleStr);
//                }

//                // 解析扩展字段
//                var standardFields = new HashSet<string> { "sub", "iss", "aud", "exp", "iat", "nbf", "jti", "name", "roles" };
//                foreach (var kvp in result)
//                {
//                    if (!standardFields.Contains(kvp.Key) && kvp.Value != null)
//                        payload.Extensions[kvp.Key] = kvp.Value.ToString() ?? "";
//                }

//                return payload;
//            }
//            catch (Exception ex) when (ex is not InvalidTokenException)
//            {
//                throw new InvalidTokenException("解析令牌失败", ex);
//            }
//        }

//        private string? GetStringValue(IDictionary<string, object> dict, string key)
//        {
//            return dict.TryGetValue(key, out var value) ? value?.ToString() : null;
//        }
//    }
//}