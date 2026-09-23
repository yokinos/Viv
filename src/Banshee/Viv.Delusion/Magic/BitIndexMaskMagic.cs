using System.Numerics;

namespace Viv.Delusion.Magic
{
    /// <summary>
    /// 位索引掩码工具（基于 BigInteger，支持无限位）
    /// </summary>
    public static class BitIndexMaskMagic
    {
        /// <summary>
        /// 检查掩码是否包含指定权限位
        /// </summary>
        public static bool Has(BigInteger mask, int bitIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(bitIndex);
            return (mask & (BigInteger.One << bitIndex)) != 0;
        }

        /// <summary>
        /// 添加一个权限位
        /// </summary>
        public static BigInteger Add(BigInteger mask, int bitIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(bitIndex);
            return mask | (BigInteger.One << bitIndex);
        }

        /// <summary>
        /// 移除一个权限位
        /// </summary>
        public static BigInteger Remove(BigInteger mask, int bitIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(bitIndex);
            return mask & ~(BigInteger.One << bitIndex);
        }

        /// <summary>
        /// 合并多个已有的权限掩码（多个角色 OR）
        /// </summary>
        public static BigInteger Combine(params BigInteger[] masks)
        {
            BigInteger result = BigInteger.Zero;
            foreach (var m in masks)
                result |= m;
            return result;
        }

        /// <summary>
        /// 从多个权限位索引直接创建掩码（用于给角色批量赋权）
        /// </summary>
        /// <param name="bitIndices">权限位索引列表（如 0, 3, 7）</param>
        /// <returns>组合后的掩码</returns>
        public static BigInteger CreateMask(params int[] bitIndices)
        {
            BigInteger mask = BigInteger.Zero;
            foreach (var idx in bitIndices)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(idx);
                mask |= (BigInteger.One << idx);
            }
            return mask;
        }

        /// <summary>
        /// 转字节数组（存数据库 varbinary）
        /// </summary>
        public static byte[] ToBytes(BigInteger mask) => mask.ToByteArray();

        /// <summary>
        /// 从字节数组还原（读数据库 varbinary）
        /// </summary>
        public static BigInteger FromBytes(byte[] bytes) => new(bytes);

        /// <summary>
        /// 转十进制字符串。掩码列（MenuMask / SubPageMask / ButtonMask）存的就是这个格式 ——
        /// 存数字而不是字节，为的是在数据库客户端里能直接肉眼看。
        /// </summary>
        public static string ToText(BigInteger mask) => mask.ToString();

        /// <summary>
        /// 从十进制字符串还原。null 与空白一律当 0（无权限位）。
        /// 格式非法时 BigInteger.Parse 直接抛 FormatException，不吞成 0 —— 掩码读错位是权限错乱，
        /// 比一条异常难查得多。
        /// </summary>
        public static BigInteger FromText(string? text) =>
            string.IsNullOrWhiteSpace(text) ? BigInteger.Zero : BigInteger.Parse(text);
    }
}