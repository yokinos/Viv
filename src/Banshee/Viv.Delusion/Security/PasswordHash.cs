using System.Security.Cryptography;
using System.Text;
using Viv.Delusion.Magic;

namespace Viv.Delusion.Security
{
    /// <summary>
    /// 口令哈希（PBKDF2-SHA256 + 每用户独立盐 + 固定时间比较）。
    ///
    /// 存储格式：<c>Hash = "pbkdf2-sha256${迭代次数}${base64(key)}"</c>，<c>Salt = base64(随机盐)</c>。
    /// 迭代次数写进哈希串，将来调高默认值不必迁移旧口令。
    ///
    /// 兼容旧格式：<c>Hash</c> 是 32 位十六进制（<c>MD5(密码 + 盐)</c>）时按旧算法验证，
    /// 验证通过后由调用方决定是否重新哈希升级。MD5 无迭代、可被彩虹表/GPU 暴力破解，
    /// 只作为过渡期的登录通道，不要再用它写入新口令。
    /// </summary>
    public static class PasswordHash
    {
        private const string Prefix = "pbkdf2-sha256";
        private const int SaltSize = 16;
        private const int KeySize = 32;

        /// <summary>当前默认迭代次数。OWASP 2023 对 PBKDF2-SHA256 的建议下限是 600k，这里先对齐仓库既有的 100k。</summary>
        public const int DefaultIterations = 100_000;

        /// <summary>
        /// 可接受的迭代次数上限。迭代次数写在哈希串里（便于将来调高不必迁移），
        /// 但也意味着能改库的人可以塞一个天文数字，让每次登录徒劳跑满 CPU。
        /// 上限取默认值的 20 倍，留够调优空间，同时挡住恶意值。
        /// </summary>
        private const int MaxSupportedIterations = DefaultIterations * 20;

        /// <summary>派生密钥长度下限 / 上限（字节）。默认 32；留出兼容旧值的余量。</summary>
        private const int MinKeySize = 16;
        private const int MaxKeySize = 64;

        /// <summary>生成新口令哈希。</summary>
        public static (string Hash, string Salt) Hash(string password)
        {
            ArgumentNullException.ThrowIfNull(password);

            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, DefaultIterations, HashAlgorithmName.SHA256, KeySize);

            return ($"{Prefix}${DefaultIterations}${Convert.ToBase64String(key)}", Convert.ToBase64String(salt));
        }

        /// <summary>
        /// 校验口令。<paramref name="needsRehash"/> 为 true 表示这次是旧格式（或迭代次数偏低）验证通过，
        /// 调用方应写入 <see cref="Hash(string)"/> 的新值完成升级。
        /// </summary>
        public static bool Verify(string? hash, string? salt, string? password, out bool needsRehash)
        {
            needsRehash = false;

            if (string.IsNullOrEmpty(hash) || password is null)
            {
                return false;
            }

            if (IsModern(hash))
            {
                return VerifyModern(hash, salt, password, out needsRehash);
            }

            // 旧格式：MD5(密码 + 盐)，盐可能为空（历史上允许）
            var legacy = EncryptMagic.HashMd5(password + salt);
            var ok = FixedTimeEqualsAscii(legacy, hash);
            needsRehash = ok;
            return ok;
        }

        private static bool IsModern(string hash)
            => hash.StartsWith(Prefix + "$", StringComparison.Ordinal);

        private static bool VerifyModern(string hash, string? salt, string password, out bool needsRehash)
        {
            needsRehash = false;

            if (string.IsNullOrEmpty(salt))
            {
                return false;
            }

            // pbkdf2-sha256$迭代次数$base64(key)
            var parts = hash.Split('$');
            if (parts.Length != 3
                || !int.TryParse(parts[1], out var iterations)
                || iterations <= 0
                || iterations > MaxSupportedIterations)
            {
                return false;
            }

            try
            {
                var saltBytes = Convert.FromBase64String(salt);
                var expected = Convert.FromBase64String(parts[2]);
                // 长度也来自库里的值：过短说明数据损坏，过长会让 PBKDF2 多跑若干块，同样要限住。
                if (expected.Length is < MinKeySize or > MaxKeySize)
                {
                    return false;
                }

                var actual = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, iterations, HashAlgorithmName.SHA256, expected.Length);
                var ok = CryptographicOperations.FixedTimeEquals(actual, expected);

                // 老哈希的迭代次数低于当前标准时，登录成功后顺手升级
                needsRehash = ok && iterations < DefaultIterations;
                return ok;
            }
            catch (FormatException)
            {
                // 库里的盐/哈希不是合法 Base64，等同验证失败
                return false;
            }
        }

        /// <summary>固定时间比较两段 ASCII（长度不同直接失败，长度本身不是秘密）。</summary>
        private static bool FixedTimeEqualsAscii(string expected, string actual)
        {
            var expectedBytes = Encoding.ASCII.GetBytes(expected);
            var actualBytes = Encoding.ASCII.GetBytes(actual);
            return expectedBytes.Length == actualBytes.Length
                   && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }
    }
}
