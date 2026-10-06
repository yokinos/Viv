using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Options;
using Viv.Contracts.Interface;
using Viv.Contracts.Options;
using Viv.Delusion.Magic;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// 用 AES + 框架共享内部令牌做加解密。
    /// 令牌来自 <see cref="VivInternalTokenOptions"/>（框架恒注册，值取自 EnvOption.InternalToken），
    /// 全服务同值 —— 否则一个服务写的密文另一个解不开。
    ///
    /// 实现 <see cref="IDependency"/> 走自动注册（类名不以 Service 结尾，DIOption 的后缀扫描扫不到它）。
    /// </summary>
    public class ApiKeyProtector : IApiKeyProtector, IDependency
    {
        private readonly string _secret;

        public ApiKeyProtector(IOptions<VivInternalTokenOptions> tokenOptions)
        {
            _secret = tokenOptions.Value.InternalToken ?? string.Empty;
        }

        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;
            if (string.IsNullOrEmpty(_secret))
                throw new InvalidOperationException("内部令牌为空，无法加密密钥：检查 VivOptions.EnvOption.InternalToken");

            return EncryptMagic.EncryptAES(_secret, plainText);
        }

        public string? Decrypt(string? cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return null;

            try
            {
                return EncryptMagic.DecryptAES(_secret, cipherText);
            }
            catch
            {
                // 令牌换过、或密文被手工改过都会走到这里 —— 返回 null 让上层记 Error，不要把异常甩到调用链上
                return null;
            }
        }
    }
}
