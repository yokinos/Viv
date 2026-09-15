using System;
using System.IO.Hashing;
using System.Runtime.InteropServices;

namespace Viv.Delusion.Magic
{
    /// <summary>
    /// 支持的哈希算法类型。
    /// 所有算法均为非加密哈希，不可用于密码存储、签名或防篡改场景。
    /// </summary>
    public enum HashMode
    {
        /// <summary>
        /// CRC-32（IEEE 802.3）。返回 32 位，零扩展为 ulong。
        /// </summary>
        Crc32 = 0,

        /// <summary>
        /// CRC-64/ECMA-182。返回 64 位。
        /// 非反射实现，低位不携带信息，切勿取低位做取模分桶（分桶请用 <see cref="XxHash64"/>）。
        /// </summary>
        Crc64 = 1,

        /// <summary>
        /// xxHash32。返回 32 位，零扩展为 ulong。
        /// </summary>
        XxHash32 = 2,

        /// <summary>xxHash64。返回 64 位。</summary>
        XxHash64 = 3,

        /// <summary>
        /// xxHash3（64 位）。返回 64 位。
        /// </summary>
        XxHash3 = 4,

        /// <summary>
        /// xxHash128（128 位）。折叠高低 64 位后返回 64 位。
        /// </summary>
        XxHash128 = 5,

        /// <summary>
        /// CRC-64/XZ（反射实现，init / xorout 均为全 1）。返回 64 位。
        /// 与 <see cref="Crc64"/> 多项式相同但参数不同，两者结果不通用。
        /// System.IO.Hashing 未提供该变体，故此处自行查表实现。
        /// 注意：反射实现的低位才是有效位，取低位取模分桶不会退化（与 <see cref="Crc64"/> 相反）。
        /// </summary>
        Crc64Xz = 6,
    }

    /// <summary>
    /// 统一哈希工具类，提供多种非加密哈希算法。
    /// </summary>
    /// <remarks>
    /// 内部基于 <c>System.IO.Hashing</c> 实现，性能与正确性由官方库保证；
    /// 唯一例外是 <see cref="HashMode.Crc64Xz"/>（官方库未提供该变体），为查表实现，见 <see cref="ComputeCrc64Xz"/>。
    /// 字符串输入按 UTF-16 LE 字节流计算（注意：旧版 <c>Crc64Magic</c> 为 CRC-64/XZ，结果与本类的 ECMA-182 不一致）。
    /// 空字符串 / null / 空数组一律返回 0，调用方需自行判断 0 是否为合法哈希值。
    /// </remarks>
    public static class HashMagic
    {
        /// <summary>
        /// 计算哈希值（字符跨度）。
        /// </summary>
        /// <param name="mode">哈希算法类型。</param>
        /// <param name="key">输入字符跨度，按 UTF-16 LE 处理。</param>
        /// <returns>64 位哈希值；空输入返回 0。</returns>
        public static ulong Compute(HashMode mode, ReadOnlySpan<char> key)
        {
            if (key.IsEmpty)
                return 0;

            // 零拷贝：把 UTF-16 LE 的 char 流直接重解释为 byte 流
            return Compute(mode, MemoryMarshal.AsBytes(key));
        }

        /// <summary>
        /// 计算哈希值（字符串）。
        /// </summary>
        /// <param name="mode">哈希算法类型。</param>
        /// <param name="key">输入字符串。</param>
        /// <returns>64 位哈希值；null 或空字符串返回 0。</returns>
        public static ulong Compute(HashMode mode, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return 0;

            return Compute(mode, key.AsSpan());
        }

        /// <summary>
        /// 计算哈希值（字节数组）。
        /// </summary>
        /// <param name="mode">哈希算法类型。</param>
        /// <param name="keyBytes">输入字节数组。</param>
        /// <returns>64 位哈希值；null 或空数组返回 0。</returns>
        public static ulong Compute(HashMode mode, byte[] keyBytes)
        {
            if (keyBytes == null || keyBytes.Length == 0)
                return 0;

            return Compute(mode, keyBytes.AsSpan());
        }

        /// <summary>
        /// 计算哈希值（字节跨度）。
        /// </summary>
        /// <param name="mode">哈希算法类型。</param>
        /// <param name="keyBytes">输入字节跨度。</param>
        /// <returns>64 位哈希值；空输入返回 0。</returns>
        /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="mode"/> 不是已知的算法时抛出。</exception>
        public static ulong Compute(HashMode mode, ReadOnlySpan<byte> keyBytes)
        {
            if (keyBytes.IsEmpty)
                return 0;

            return mode switch
            {
                HashMode.Crc32 => Crc32.HashToUInt32(keyBytes),
                HashMode.Crc64 => Crc64.HashToUInt64(keyBytes),
                HashMode.XxHash32 => XxHash32.HashToUInt32(keyBytes),
                HashMode.XxHash64 => XxHash64.HashToUInt64(keyBytes),
                HashMode.XxHash3 => XxHash3.HashToUInt64(keyBytes),
                HashMode.XxHash128 => Fold128(XxHash128.HashToUInt128(keyBytes)),
                HashMode.Crc64Xz => ComputeCrc64Xz(keyBytes),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "不支持的哈希算法"),
            };
        }

        /// <summary>
        /// 把 128 位哈希折叠为 64 位：高 64 位与低 64 位异或。
        /// 相比直接截断，折叠能保留全部熵，降低碰撞概率。
        /// </summary>
        private static ulong Fold128(UInt128 value)
        {
            return (ulong)value ^ (ulong)(value >> 64);
        }

        /// <summary>
        /// CRC-64/XZ 反射多项式，即 ECMA-182 多项式 0x42F0E1EBA9EA3693 的位反转形式。
        /// </summary>
        private const ulong Crc64XzPolynomial = 0xC96C5795D7870F42;

        private static readonly ulong[] Crc64XzTable = CreateCrc64XzTable();

        private static ulong[] CreateCrc64XzTable()
        {
            var table = new ulong[256];
            for (ulong i = 0; i < 256; i++)
            {
                ulong value = i;
                for (int bit = 0; bit < 8; bit++)
                {
                    value = (value >> 1) ^ ((value & 1) == 1 ? Crc64XzPolynomial : 0);
                }

                table[i] = value;
            }

            return table;
        }

        /// <summary>
        /// CRC-64/XZ：反射实现，init 与 xorout 均为全 1。
        /// 已知向量："123456789"（ASCII）→ 0x995DC9BBDF1939FA。
        /// 调用前已由 <see cref="Compute(HashMode, ReadOnlySpan{byte})"/> 排除空输入。
        /// </summary>
        private static ulong ComputeCrc64Xz(ReadOnlySpan<byte> keyBytes)
        {
            ulong crc = ulong.MaxValue;
            foreach (byte b in keyBytes)
            {
                crc = (crc >> 8) ^ Crc64XzTable[(crc ^ b) & 0xFF];
            }

            return ~crc;
        }
    }
}