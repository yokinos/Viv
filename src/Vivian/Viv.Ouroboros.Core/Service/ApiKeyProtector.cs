using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Options;
using Viv.Contracts.Interface;
using Viv.Contracts.Options;
using Viv.Delusion.Magic;
using Viv.Engine.Options;
using Viv.Log;
using Viv.Ouroboros.Core.IService;

namespace Viv.Ouroboros.Core.Service
{
    /// <summary>
    /// 用 AES 加解密档位密钥。主钥匙优先取专用配置 <see cref="EnvOptions.AiKeySecret"/>（不入库、
    /// 与内部令牌解耦，轮换它不再连带作废档位密钥），没配才回退 <see cref="VivInternalTokenOptions.InternalToken"/>
    /// 并记一次 Warning —— 回退保证既有密文照旧解得开，行为向后兼容。
    ///
    /// 解密额外保留一条旧钥匙的路：专用钥匙刚配上时库里全是旧令牌加密的密文，有它就不必停机迁移，
    /// 代价是运维能看到 Warning（提示跑 api/ModelProfiles/reencrypt 把密文换过来）。
    ///
    /// 实现 <see cref="IDependency"/> 走自动注册（类名不以 Service 结尾，DIOption 的后缀扫描扫不到它）。
    /// 单参构造留给仓库外的种子工具（它只拿得到内部令牌），走的就是回退那条路。
    /// </summary>
    public class ApiKeyProtector : IApiKeyProtector, IDependency
    {
        /// <summary>专用主钥匙的配置位置（只写进日志，不含值）</summary>
        private const string SecretConfigPath = "VivOptions:EnvOption:AiKeySecret";

        /// <summary>"没配专用钥匙、正在回退"只提醒一次：本类是 Scoped，实例级标记会刷满日志</summary>
        private static int _fallbackWarned;

        /// <summary>同上，"存在旧钥匙密文"也只提醒一次；迁移完再配上就是新的进程了</summary>
        private static int _legacyWarned;

        private readonly string _primaryKey;
        private readonly string? _legacyKey;
        private readonly bool _dedicatedKeyConfigured;
        private readonly ILoggerContract? _logger;

        /// <summary>
        /// 构造函数（只拿内部令牌）：仓库外的种子工具用这条，等于显式走回退路径。
        /// </summary>
        /// <param name="tokenOptions">框架共享内部令牌</param>
        public ApiKeyProtector(IOptions<VivInternalTokenOptions> tokenOptions)
            : this(tokenOptions.Value, null, null)
        {
        }

        /// <summary>
        /// 构造函数（容器用的那条）：能读到专用主钥匙与日志。
        /// </summary>
        /// <param name="tokenOptions">框架共享内部令牌（回退用，也作解密兜底）</param>
        /// <param name="envOptions">环境配置（专用主钥匙在这里）</param>
        /// <param name="logger">日志</param>
        public ApiKeyProtector(IOptions<VivInternalTokenOptions> tokenOptions, IOptions<EnvOptions> envOptions,
            ILoggerContract logger)
            : this(tokenOptions.Value, envOptions.Value, logger)
        {
        }

        private ApiKeyProtector(VivInternalTokenOptions tokenOptions, EnvOptions? envOptions, ILoggerContract? logger)
        {
            _logger = logger;

            var internalToken = NullIfBlank(tokenOptions.InternalToken);
            var dedicated = NullIfBlank(envOptions?.AiKeySecret);

            if (dedicated is null)
            {
                if (logger is not null && Interlocked.Exchange(ref _fallbackWarned, 1) == 0)
                    logger.Warning("未配置专用主钥匙 {0}，档位密钥仍用 InternalToken 加密（能读仓库的人可解开全部档位密钥）；" +
                                   "配上它之后请调 api/ModelProfiles/reencrypt 迁移已有密文", SecretConfigPath);

                _primaryKey = internalToken ?? string.Empty;
                _legacyKey = null;
                return;
            }

            _primaryKey = dedicated;
            _dedicatedKeyConfigured = true;
            // 旧钥匙只在"和主钥匙不是同一个值"时才有意义：配成同一个值等于没换钥匙
            _legacyKey = string.Equals(dedicated, internalToken, StringComparison.Ordinal) ? null : internalToken;
        }

        /// <inheritdoc />
        public bool DedicatedKeyConfigured => _dedicatedKeyConfigured;

        /// <inheritdoc />
        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;
            if (string.IsNullOrEmpty(_primaryKey))
                throw new InvalidOperationException($"没有可用的加密主钥匙：请配置 {SecretConfigPath}（或 VivOptions.EnvOption.InternalToken）");

            // EncryptAES 内部 catch 后返回 null：静默返回空会把这一行写成"没配密钥"，宁可当场失败
            return EncryptMagic.EncryptAES(_primaryKey, plainText)
                   ?? throw new InvalidOperationException("档位密钥加密失败（AES 输出为空）");
        }

        /// <inheritdoc />
        public string? Decrypt(string? cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return null;

            var plain = TryDecrypt(_primaryKey, cipherText);
            if (plain is not null) return plain;

            // 旧钥匙兜底：专用钥匙刚配上、迁移还没跑时，库里的密文仍由 InternalToken 加密
            if (_legacyKey is null) return null;

            plain = TryDecrypt(_legacyKey, cipherText);
            if (plain is null) return null;

            if (_logger is not null && Interlocked.Exchange(ref _legacyWarned, 1) == 0)
                _logger.Warning("有档位密钥仍是旧钥匙（InternalToken）加密的，本次已用旧钥匙兜底解开；" +
                                "请调 api/ModelProfiles/reencrypt?force=true 迁移，迁完旧钥匙就再也解不开它们了");

            return plain;
        }

        /// <inheritdoc />
        public bool IsLegacyCipher(string? cipherText)
            => _legacyKey is not null
               && !string.IsNullOrEmpty(cipherText)
               && TryDecrypt(_primaryKey, cipherText) is null
               && TryDecrypt(_legacyKey, cipherText) is not null;

        /// <summary>解不开一律回 null 不抛：钥匙换过、密文被手工改过都会走到这里</summary>
        private static string? TryDecrypt(string key, string cipherText)
        {
            if (string.IsNullOrEmpty(key)) return null;

            try
            {
                return EncryptMagic.DecryptAES(key, cipherText);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>空白一律当没配：空串作密钥会让密文看起来"配了却解不开"</summary>
        private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
