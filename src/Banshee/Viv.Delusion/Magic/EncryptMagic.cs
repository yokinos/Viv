using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Viv.Delusion;

namespace Viv.Delusion.Magic
{
    /// <summary>
    /// 加密解密工具类
    /// 提供MD5/SHA256哈希加密、DES/3DES/AES对称加密/解密功能
    /// </summary>
    /// <remarks>
    /// 1. 哈希加密为不可逆加密，适用于密码存储、数据完整性校验；
    /// 2. 对称加密为可逆加密，适用于敏感数据传输/存储，需妥善保管密钥；
    /// 3. 所有方法均做空值校验，加密/解密失败时返回null，不抛出异常（保障业务稳定性）
    /// </remarks>
    public static class EncryptMagic
    {
        /// <summary>
        /// 默认编码格式（UTF-8）
        /// </summary>
        private static readonly Encoding Utf8 = Encoding.UTF8;

        /// <summary>
        /// MD5哈希加密（32位小写）
        /// </summary>
        /// <param name="input">待加密的字符串（不能为空）</param>
        /// <param name="encoding">字符编码格式，默认UTF-8</param>
        /// <returns>32位小写MD5哈希字符串；input为null时抛出ArgumentNullException</returns>
        /// <exception cref="ArgumentNullException">input为null时抛出</exception>
        /// <example>
        /// 示例：HashMD5("123456") → "e10adc3949ba59abbe56e057f20f883e"
        /// </example>
        /// <remarks>
        /// 1. 手动实现十六进制转换，比BitConverter+Replace更高效；
        /// 2. MD5为128位哈希算法，输出固定32位十六进制字符串；
        /// 3. 不可逆加密，无法从哈希值还原原始字符串
        /// </remarks>
        public static string HashMd5(string input, Encoding? encoding = null)
        {
            ArgumentNullException.ThrowIfNull(input);
            encoding ??= Utf8;
            var bytes = encoding.GetBytes(input);
            byte[] hashBytes = MD5.HashData(bytes);
            static char GetHexChar(int value) => (char)(value < 10 ? value + '0' : value - 10 + 'a');
            return string.Create(32, hashBytes, (span, bytesArr) =>
            {
                for (int i = 0; i < bytesArr.Length; i++)
                {
                    byte b = bytesArr[i];
                    span[2 * i] = GetHexChar(b >> 4);
                    span[2 * i + 1] = GetHexChar(b & 0x0F);
                }
            });
        }

        /// <summary>
        /// SHA256哈希加密（Base64编码）
        /// </summary>
        /// <param name="input">待加密的字符串（不能为空）</param>
        /// <param name="encoding">字符编码格式，默认UTF-8</param>
        /// <returns>SHA256哈希值的Base64字符串；input为null时抛出ArgumentNullException</returns>
        /// <exception cref="ArgumentNullException">input为null时抛出</exception>
        /// <remarks>
        /// 1. SHA256为256位哈希算法，安全性高于MD5；
        /// 2. 输出为Base64编码字符串，长度约44个字符；
        /// 3. 不可逆加密，适用于高安全性的哈希校验场景
        /// </remarks>
        public static string HashSHA256(string input, Encoding? encoding = null)
        {
            ArgumentNullException.ThrowIfNull(input);
            encoding ??= Utf8;
            byte[] buffer = SHA256.HashData(encoding.GetBytes(input));
            return Convert.ToBase64String(buffer);
        }

        /// <summary>
        /// DES对称加密（Base64输出）
        /// </summary>
        /// <param name="key">加密密钥（不能为空）</param>
        /// <param name="text">待加密的明文（不能为空）</param>
        /// <param name="options">加密选项（包含模式、填充方式、IV等，为null时使用默认配置）</param>
        /// <returns>加密后的Base64字符串；密钥/明文为空或加密失败时返回null</returns>
        /// <remarks>DES为56位密钥对称加密算法，安全性较低，建议优先使用AES</remarks>
        [return: MaybeNull]
        public static string EncryptDES(string key, string text, EncrypOptions? options = default) => SymmetricTransform(EncrypType.DES, key, text, options, true);

        /// <summary>
        /// DES对称解密
        /// </summary>
        /// <param name="key">解密密钥（需与加密密钥一致）</param>
        /// <param name="text">待解密的Base64密文（不能为空）</param>
        /// <param name="options">解密选项（需与加密选项一致，为null时使用默认配置）</param>
        /// <returns>解密后的明文字符串；密钥/密文为空或解密失败时返回null</returns>
        /// <remarks>解密选项（Mode/Padding/IV）必须与加密时一致，否则解密失败</remarks>
        [return: MaybeNull]
        public static string DecryptDES(string key, string text, EncrypOptions? options = default) => SymmetricTransform(EncrypType.DES, key, text, options, false);

        /// <summary>
        /// 3DES（TripleDES）对称加密（Base64输出）
        /// </summary>
        /// <param name="key">加密密钥（不能为空）</param>
        /// <param name="text">待加密的明文（不能为空）</param>
        /// <param name="options">加密选项（包含模式、填充方式、IV等，为null时使用默认配置）</param>
        /// <returns>加密后的Base64字符串；密钥/明文为空或加密失败时返回null</returns>
        /// <remarks>3DES为168位密钥对称加密算法，安全性高于DES，兼容DES</remarks>
        [return: MaybeNull]
        public static string Encrypt3DES(string key, string text, EncrypOptions? options = default) => SymmetricTransform(EncrypType.TripleDES, key, text, options, true);

        /// <summary>
        /// 3DES（TripleDES）对称解密
        /// </summary>
        /// <param name="key">解密密钥（需与加密密钥一致）</param>
        /// <param name="text">待解密的Base64密文（不能为空）</param>
        /// <param name="options">解密选项（需与加密选项一致，为null时使用默认配置）</param>
        /// <returns>解密后的明文字符串；密钥/密文为空或解密失败时返回null</returns>
        [return: MaybeNull]
        public static string Decrypt3DES(string key, string text, EncrypOptions? options = default) => SymmetricTransform(EncrypType.TripleDES, key, text, options, false);

        /// <summary>
        /// 解密第三方/指定参数格式的密文（互通场景）。
        /// 与 <see cref="DecryptAES(string, string, EncrypOptions)"/> 的区别：
        /// key/iv 直接按字节使用（不做 SHA256 派生）、不校验 HMAC、IV 由调用方显式传入。
        /// 仅用于第三方约定格式的数据；Viv 自己的数据请继续走 DecryptAES（安全路径）。
        /// </summary>
        /// <param name="type">算法类型（AES/DES/3DES）</param>
        /// <param name="key">原始密钥字节，长度必须符合算法要求（AES=16/24/32，DES=8，3DES=16/24）</param>
        /// <param name="iv">第三方约定的 IV 字节（ECB 模式可传 null）；CBC 等需要 IV 的模式必须传</param>
        /// <param name="text">待解密的 Base64 密文</param>
        /// <param name="mode">加密模式（第三方规格，默认 CBC）</param>
        /// <param name="padding">填充方式（第三方规格，默认 PKCS7）</param>
        /// <returns>解密后的明文字符串（按 UTF-8 解码）；参数错误或解密失败时返回 null</returns>
        /// <remarks>
        /// key/iv 的编码按第三方文档转换：hex → Convert.FromHexString()、base64 → Convert.FromBase64String()、
        /// 纯文本 → Encoding.UTF8.GetBytes()。明文按 UTF-8 解码，非 UTF-8 编码的明文需自行处理。
        /// </remarks>
        [return: MaybeNull]
        public static string DecryptRaw(EncrypType type, byte[] key, byte[]? iv, string text, CipherMode mode = CipherMode.CBC, PaddingMode padding = PaddingMode.PKCS7)
        {
            if (key is not { Length: > 0 } || string.IsNullOrEmpty(text)) return default;

            try
            {
                using SymmetricAlgorithm alg = CreateAlgorithm(type);
                alg.Mode = mode;
                alg.Padding = padding;
                alg.Key = key;
                if (iv is not null)
                {
                    alg.IV = iv;
                }

                byte[] cipher = Convert.FromBase64String(text);
                using var decryptor = alg.CreateDecryptor();
                byte[] plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
                return Utf8.GetString(plain);
            }
            catch
            {
                return default;
            }
        }

        /// <summary>
        /// AES对称加密（Base64输出）
        /// </summary>
        /// <param name="key">加密密钥（不能为空）</param>
        /// <param name="text">待加密的明文（不能为空）</param>
        /// <param name="options">加密选项（包含模式、填充方式、IV等，为null时使用默认配置）</param>
        /// <returns>加密后的Base64字符串；密钥/明文为空或加密失败时返回null</returns>
        /// <remarks>AES为高级加密标准，支持128/192/256位密钥，安全性最高，推荐优先使用</remarks>
        [return: MaybeNull]
        public static string EncryptAES(string key, string text, EncrypOptions? options = default) => SymmetricTransform(EncrypType.AES, key, text, options, true);

        /// <summary>
        /// AES对称解密
        /// </summary>
        /// <param name="key">解密密钥（需与加密密钥一致）</param>
        /// <param name="text">待解密的Base64密文（不能为空）</param>
        /// <param name="options">解密选项（需与加密选项一致，为null时使用默认配置）</param>
        /// <returns>解密后的明文字符串；密钥/密文为空或解密失败时返回null</returns>
        [return: MaybeNull]
        public static string DecryptAES(string key, string text, EncrypOptions? options = default) => SymmetricTransform(EncrypType.AES, key, text, options, false);

        /// <summary>
        /// 对称加密/解密核心实现（内部方法）
        /// </summary>
        /// <param name="type">加密算法类型（DES/3DES/AES）</param>
        /// <param name="key">密钥（不能为空）</param>
        /// <param name="text">明文（加密）/密文（解密）（不能为空）</param>
        /// <param name="options">加密/解密选项</param>
        /// <param name="encrypt">true=加密，false=解密</param>
        /// <returns>加密/解密结果；参数为空或处理失败时返回null</returns>
        /// <remarks>
        /// 1. 自动适配算法的密钥/IV长度要求，不足时填充，过长时截断；
        /// 2. 捕获所有异常并返回null，避免业务层因加密失败中断；
        /// 3. 密文统一使用Base64编码，便于传输和存储
        /// </remarks>
        private static string? SymmetricTransform(EncrypType type, string key, string text, EncrypOptions? options, bool encrypt)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(text)) return default;
            options ??= new EncrypOptions();

            try
            {
                using SymmetricAlgorithm alg = CreateAlgorithm(type);
                alg.Mode = options.Mode;
                alg.Padding = options.PaddingMode;

                int ivBytesRequired = alg.BlockSize / 8;
                byte[] keyBytes = DeriveKeyBytes(key, alg.KeySize / 8);

                if (encrypt)
                {
                    // 随机 IV + 前置 + HMAC 认证（encrypt-then-MAC）：
                    // 输出布局 = Base64( IV || Cipher || HMAC-SHA256(key, IV || Cipher) )
                    // 随机 IV 保证同一明文多次加密密文不同；HMAC 保证密文被篡改时解密失败（拒绝）。
                    // 修复旧实现固定 IV（同明文同密文、可比特翻转）的问题。格式与旧版不兼容，旧密文需重新加密。
                    byte[] iv = RandomNumberGenerator.GetBytes(ivBytesRequired);
                    alg.Key = keyBytes;
                    alg.IV = iv;

                    byte[] plain = Utf8.GetBytes(text);
                    using var encryptor = alg.CreateEncryptor();
                    byte[] cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);

                    byte[] payload = new byte[iv.Length + cipher.Length];
                    Buffer.BlockCopy(iv, 0, payload, 0, iv.Length);
                    Buffer.BlockCopy(cipher, 0, payload, iv.Length, cipher.Length);

                    byte[] hmac = ComputeHmac(keyBytes, payload, payload.Length);
                    var blob = new byte[payload.Length + hmac.Length];
                    Buffer.BlockCopy(payload, 0, blob, 0, payload.Length);
                    Buffer.BlockCopy(hmac, 0, blob, payload.Length, hmac.Length);
                    return Convert.ToBase64String(blob);
                }
                else
                {
                    byte[] blob = Convert.FromBase64String(text);
                    const int hmacLength = 32; // SHA-256
                    int payloadLength = blob.Length - hmacLength;
                    if (payloadLength <= ivBytesRequired) return default;

                    // 先验 HMAC 再解密：不匹配说明密文被篡改或密钥错误，拒绝返回明文
                    byte[] providedHmac = blob.AsSpan(payloadLength).ToArray();
                    byte[] expectedHmac = ComputeHmac(keyBytes, blob, payloadLength);
                    if (!CryptographicOperations.FixedTimeEquals(providedHmac, expectedHmac))
                    {
                        return default;
                    }

                    byte[] iv = blob.AsSpan(0, ivBytesRequired).ToArray();
                    byte[] cipher = blob.AsSpan(ivBytesRequired, payloadLength - ivBytesRequired).ToArray();

                    alg.Key = keyBytes;
                    alg.IV = iv;
                    using var decryptor = alg.CreateDecryptor();
                    byte[] plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
                    return Utf8.GetString(plain);
                }
            }
            catch
            {
                return default;
            }
        }

        /// <summary>
        /// HMAC-SHA256 认证密文（防篡改），对 payload 前 payloadLength 字节计算签名。
        /// </summary>
        private static byte[] ComputeHmac(byte[] keyBytes, byte[] payload, int payloadLength)
        {
            using var hmac = new HMACSHA256(keyBytes);
            return hmac.ComputeHash(payload, 0, payloadLength);
        }

        /// <summary>
        /// 创建对称加密算法实例（内部方法）
        /// </summary>
        /// <param name="type">算法类型</param>
        /// <returns>对应的SymmetricAlgorithm实例</returns>
        /// <exception cref="ArgumentOutOfRangeException">不支持的算法类型时抛出</exception>
        private static SymmetricAlgorithm CreateAlgorithm(EncrypType type) => type switch
        {
            EncrypType.DES => DES.Create(),
            EncrypType.TripleDES => TripleDES.Create(),
            EncrypType.AES => Aes.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        /// <summary>
        /// 派生符合算法要求的密钥字节数组（内部方法）
        /// </summary>
        /// <param name="key">原始密钥字符串</param>
        /// <param name="requiredBytes">算法要求的密钥字节长度</param>
        /// <returns>适配长度的密钥字节数组</returns>
        /// <remarks>
        /// 1. 先通过SHA256哈希获取256位密钥种子；
        /// 2. 长度不足时循环填充，过长时截断；
        /// 3. 保证密钥长度符合算法要求（如DES=8字节，AES=16/24/32字节）
        /// </remarks>
        private static byte[] DeriveKeyBytes(string key, int requiredBytes)
        {
            byte[] hash = SHA256.HashData(Utf8.GetBytes(key));
            if (requiredBytes == hash.Length) return hash;
            var result = new byte[requiredBytes];
            Buffer.BlockCopy(hash, 0, result, 0, Math.Min(requiredBytes, hash.Length));
            if (requiredBytes > hash.Length)
            {
                int offset = hash.Length;
                while (offset < requiredBytes)
                {
                    int copy = Math.Min(hash.Length, requiredBytes - offset);
                    Buffer.BlockCopy(hash, 0, result, offset, copy);
                    offset += copy;
                }
            }
            return result;
        }
    }
}