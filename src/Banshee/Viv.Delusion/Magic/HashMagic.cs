using System;
using System.IO.Hashing;
using System.Runtime.InteropServices;
namespace Viv.Delusion.Magic
{
    /// <summary>
    /// 支持的非加密哈希算法类型。
    /// 全部为非加密哈希，**禁止用于密码存储、数字签名、防篡改安全校验场景**。
    /// </summary>
    public enum HashMode
    {
        /// <summary>
        /// CRC-32 IEEE 802.3。输出32位，向上扩展为ulong返回。
        /// </summary>
        Crc32 = 0,

        /// <summary>
        /// CRC-64 ECMA-182。标准非反射实现，64位输出。
        /// 高位熵密度更高，**不要直接取低位做分片/分桶取模**；分桶优先使用 <see cref="XxHash64"/>。
        /// </summary>
        Crc64 = 1,

        /// <summary>
        /// xxHash32。输出32位，向上扩展为ulong返回。高速非加密哈希。
        /// </summary>
        XxHash32 = 2,

        /// <summary>
        /// xxHash64。64位高速哈希，熵分布均匀，适合分桶、路由。
        /// </summary>
        XxHash64 = 3,

        /// <summary>
        /// xxHash3 64bit。新一代高速64位哈希。
        /// </summary>
        XxHash3 = 4,

        /// <summary>
        /// xxHash128。原生128位结果，高低64位异或折叠得到64位返回值。
        /// 相比单纯截断，折叠保留完整熵，降低碰撞概率。
        /// </summary>
        XxHash128 = 5,

        /// <summary>
        /// CRC-64/XZ。反射实现，初始值与最终异或值均为全1。
        /// 多项式底层与ECMA-182一致，但参数集合不同，二者哈希结果不可互通。
        /// System.IO.Hashing 无内置该变体，当前为自研查表实现。
        /// 低位熵分布均匀，**适合直接取低位做分片取模**，和 <see cref="Crc64"/> 行为相反。
        /// </summary>
        Crc64Xz = 6,
    }

    /// <summary>
    /// 统一哈希工具，封装多种非加密哈希算法。
    /// </summary>
    /// <remarks>
    /// 大部分算法底层依赖 System.IO.Hashing，正确性与性能由.NET官方库保障；
    /// 仅 <see cref="HashMode.Crc64Xz"/> 为自研查表实现。
    /// 字符串输入默认直接按 UTF-16 LE 字节流计算（注意：旧版Crc64Magic为CRC-64/XZ，与本类ECMA-182结果不兼容）。
    /// null、空字符串、空字节跨度统一返回0；调用方业务层需要自行区分0是否属于合法哈希结果。
    /// </remarks>
    public static class HashMagic
    {
        /// <summary>
        /// 计算哈希，字符跨度输入，直接映射为UTF-16 LE字节流。
        /// </summary>
        /// <param name="mode">哈希算法</param>
        /// <param name="key">字符跨度</param>
        /// <returns>64位哈希；空跨度返回0</returns>
        public static ulong Compute(HashMode mode, ReadOnlySpan<char> key)
        {
            if (key.IsEmpty)
                return 0;
            return Compute(mode, MemoryMarshal.AsBytes(key));
        }

        /// <summary>
        /// 计算哈希，字符串输入，按UTF-16 LE编码。
        /// </summary>
        /// <param name="mode">哈希算法</param>
        /// <param name="key">目标字符串</param>
        /// <returns>64位哈希；null/空白字符串返回0</returns>
        public static ulong Compute(HashMode mode, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return 0;
            return Compute(mode, key.AsSpan());
        }

        /// <summary>
        /// 计算哈希，字节数组输入。
        /// </summary>
        /// <param name="mode">哈希算法</param>
        /// <param name="keyBytes">字节数组</param>
        /// <returns>64位哈希；null/空数组返回0</returns>
        public static ulong Compute(HashMode mode, byte[] keyBytes)
        {
            if (keyBytes == null || keyBytes.Length == 0)
                return 0;
            return Compute(mode, keyBytes.AsSpan());
        }

        /// <summary>
        /// 计算哈希，字节跨度输入（底层入口）。
        /// </summary>
        /// <param name="mode">哈希算法</param>
        /// <param name="keyBytes">字节跨度</param>
        /// <returns>64位哈希；空跨度返回0</returns>
        /// <exception cref="ArgumentOutOfRangeException">传入未定义的HashMode时抛出</exception>
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
        /// 将128位哈希折叠为64位：高低64位异或。
        /// 相比直接截断，保留全部熵，降低哈希碰撞概率。
        /// </summary>
        private static ulong Fold128(UInt128 value)
        {
            return (ulong)value ^ (ulong)(value >> 64);
        }

        /// <summary>
        /// CRC-64/XZ 反射多项式（ECMA-182多项式0x42F0E1EBA9EA3693的位反转形式）。
        /// </summary>
        private const ulong Crc64XzPolynomial = 0xC96C5795D7870F42;

        private static readonly ulong[] Crc64XzTable = CreateCrc64XzTable();

        /// <summary>
        /// 预生成CRC-64/XZ查表。静态构造阶段一次性生成。
        /// </summary>
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
        /// CRC-64/XZ 哈希计算，反射模式，初始值与输出异或均为全1。
        /// 标准测试向量：ASCII "123456789" → 0x995DC9BBDF1939FA。
        /// 调用前置：上层Compute已过滤空输入。
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
