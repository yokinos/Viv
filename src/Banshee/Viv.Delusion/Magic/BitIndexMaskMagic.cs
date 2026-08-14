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
    }
}