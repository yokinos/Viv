using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>
    /// 密钥保护：落库前加密、读取时解密。
    /// 密钥本身来自框架的共享内部令牌，不额外引入配置项。
    /// </summary>
    public interface IApiKeyProtector
    {
        /// <summary>加密（空入参原样返回空）</summary>
        string Encrypt(string plainText);

        /// <summary>解密；解不开返回 null（不抛，交由调用方决定是告警还是拒绝）</summary>
        string? Decrypt(string? cipherText);
    }
}
