using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>
    /// 密钥保护：落库前加密、读取时解密。
    /// 主钥匙优先取专用配置 <c>VivOptions.EnvOption.AiKeySecret</c>（不入库，与内部令牌解耦），
    /// 没配才回退框架共享内部令牌（行为向后兼容：既有密文照旧能解开）。
    /// </summary>
    public interface IApiKeyProtector
    {
        /// <summary>加密（空入参原样返回空）</summary>
        string Encrypt(string plainText);

        /// <summary>解密；解不开返回 null（不抛，交由调用方决定是告警还是拒绝）</summary>
        string? Decrypt(string? cipherText);

        /// <summary>
        /// 是否已配置专用主钥匙。false = 仍在回退用内部令牌 ——
        /// 此时重加密只是换一次密文，不解决"能读仓库就能解档位密钥"。
        /// </summary>
        bool DedicatedKeyConfigured { get; }

        /// <summary>
        /// 密文是否只能用旧钥匙（内部令牌）解开。专用钥匙未配置时恒为 false（没有"旧"可言）。
        /// 供重加密迁移统计"还有多少行没迁"。
        /// </summary>
        bool IsLegacyCipher(string? cipherText);
    }
}
